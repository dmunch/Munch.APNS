using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace APNS
{
    public class PushNotification
    {       
        private IList<NotificationPayload> _notifications = new List<NotificationPayload>();

        private readonly List<string> _rejected = new List<string>();
        private readonly Dictionary<int, string> _errorList = new Dictionary<int, string>();

        private readonly Connection _connection;
        
        public PushNotification(NotificationConnection connection)
        {
            this._connection = connection;

            // Loading Apple error response list.
            _errorList.Add(0, "No errors encountered");
            _errorList.Add(1, "Processing error");
            _errorList.Add(2, "Missing device token");
            _errorList.Add(3, "Missing topic");
            _errorList.Add(4, "Missing payload");
            _errorList.Add(5, "Invalid token size");
            _errorList.Add(6, "Invalid topic size");
            _errorList.Add(7, "Invalid payload size");
            _errorList.Add(8, "Invalid token");
            _errorList.Add(255, "None (unknown)");
        }

        public async Task<IEnumerable<string>> SendAsync(IList<NotificationPayload> queue)
        {
            _connection.Logger.Info("Payload queue received.");
            _notifications = queue;
            if (queue.Count < 8999)
            {
                await SendQueueAsync(_notifications);
            }
            else
            {
                const int pageSize = 8999;
                int numberOfPages = (queue.Count / pageSize) + (queue.Count % pageSize == 0 ? 0 : 1);
                int currentPage = 0;

                while (currentPage < numberOfPages)
                {
                    _notifications = (queue.Skip(currentPage * pageSize).Take(pageSize)).ToList();
                    await SendQueueAsync(_notifications);
                    currentPage++;
                }
            }
            //Close the connection
            _connection.Disconnect();
            return _rejected;
        }

        private async Task SendQueueAsync(IEnumerable<NotificationPayload> queue)
        {
            int payloadIdCounter = 1000;
            foreach (var item in queue)
            {
                if (!_connection.Connected)
                {
                    await _connection.ConnectAsync().ConfigureAwait(false);
                }

                try
                {
                    if (item.DeviceToken.Length != 64) //check length of device token, if its shorter or longer stop generating Payload.
                    {
                        _connection.Logger.Error("Invalid device token length, possible simulator entry: " + item.DeviceToken);
                        continue;
                    }
                    
                    item.PayloadId = payloadIdCounter++;
                    using (var frameMemStream = PayloadToByteArray.GeneratePayload(item, _connection.Logger))
                    {
                        await _connection.ApnsStream.WriteAsync(new byte[] { 2 }, 0, 1).ConfigureAwait(false);                        
                        await _connection.ApnsStream.WriteAsync(BitConverter.GetBytes(IPAddress.HostToNetworkOrder((Int32)frameMemStream.Length)), 0, 4).ConfigureAwait(false);
                        frameMemStream.Position = 0;
                        await frameMemStream.CopyToAsync(_connection.ApnsStream).ConfigureAwait(false);
                    }

                    var success = await ReadPossibleResponseAsync(item).ConfigureAwait(false);

                    if (!success)
                    {
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _connection.Logger.Error("An error occurred on sending payload for device token {0} - {1}", item.DeviceToken, ex.Message);
                    _connection.Disconnect();
                }
            }
        }

        private async Task<bool> ReadPossibleResponseAsync(NotificationPayload item)
        {
            var response = new byte[6];
            var readCancelToken = new CancellationTokenSource();

            // We are going to read from the stream, but the stream *may* not ever have any data for us to
            // read (in the case that all the messages sent successfully, apple will send us nothing
            // So, let's make our read timeout after a reasonable amount of time to wait for apple to tell
            // us of any errors that happened.
            readCancelToken.CancelAfter(750);

            int len = -1;

            while (!readCancelToken.IsCancellationRequested)
            {

                // See if there's data to read
                if (_connection.ApnsClient.Client.Available > 0)
                {
                    len = await _connection.ApnsStream.ReadAsync(response, 0, 8).ConfigureAwait(false);
                    break;
                }

                // Let's not tie up too much CPU waiting...
                await Task.Delay(50).ConfigureAwait(false);
            }

            
            if (len < 0)
            {
                //If we timed out waiting, but got no data to read, everything must be ok!

                _connection.Logger.Info("Notification successfully sent to APNS server for Device Token : " + item.DeviceToken);
                return true;
            }
            else if (len == 0)
            {
                // If we got no data back, and we didn't end up canceling, the connection must have closed
                _connection.Logger.Info("APNS-Client: Server Closed Connection");

                // Connection was closed
                _connection.Disconnect();
                return false;
            }

            // If we make it here, we did get data back, so we have errors
            var command = response[0];
            if (command == 8)
            {
                var status = response[1];
                var ID = new byte[4];
                Array.Copy(response, 2, ID, 0, 4);

                var payLoadId = Encoding.ASCII.GetString(ID);
                var payLoadIndex = ((int.Parse(payLoadId)) - 1000);

                _connection.Logger.Error("Apple rejected palyload for device token : " + _notifications[payLoadIndex].DeviceToken);
                _connection.Logger.Error("Apple Error code : " + _errorList[status]);
                _connection.Logger.Error("Connection terminated by Apple.");

                _rejected.Add(_notifications[payLoadIndex].DeviceToken);
                _connection.Disconnect();
            }

            return false;
        }
    }
}
