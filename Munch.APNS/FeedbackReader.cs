using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace APNS
{
    public class FeedbackReader
    {
        readonly Connection _connection;

        public FeedbackReader(FeedbackConnection connection)
        {
            this._connection = connection;
        }

        public async Task<List<Feedback>> GetFeedBackAsync()
        {
            var feedbacks = new List<Feedback>();

            try
            {                
                _connection.Logger.Info("Connecting to feedback service.");

                if (!_connection.Connected)
                {
                    await _connection.ConnectAsync().ConfigureAwait(false);
                }
                //Set up
                byte[] buffer = new byte[38];
                DateTime minTimestamp = DateTime.Now.AddYears(-1);

                var readCancelToken = new CancellationTokenSource();
                readCancelToken.CancelAfter(750);

                int recd = -1;

                while (!readCancelToken.IsCancellationRequested)
                {

                    // See if there's data to read
                    if (_connection.ApnsClient.Client.Available > 0)
                    {
                        recd = await _connection.ApnsStream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                        _connection.Logger.Info("Feedback response received.");
                        break;
                    }

                    // Let's not tie up too much CPU waiting...
                    await Task.Delay(50).ConfigureAwait(false);
                }

                if (recd <= 0)
                {
                    _connection.Logger.Info("Feedback response is empty.");
                }

                //Continue while we have results and are not disposing
                while (recd > 0)
                {
                    _connection.Logger.Info("processing feedback response");
                    var fb = new Feedback();

                    //Get our seconds since 1970 ?
                    byte[] bSeconds = new byte[4];
                    byte[] bDeviceToken = new byte[32];

                    Array.Copy(buffer, 0, bSeconds, 0, 4);

                    //Check endianness
                    if (BitConverter.IsLittleEndian)
                        Array.Reverse(bSeconds);

                    int tSeconds = BitConverter.ToInt32(bSeconds, 0);

                    //Add seconds since 1970 to that date, in UTC and then get it locally
                    fb.Timestamp = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(tSeconds).ToLocalTime();


                    //Now copy out the device token
                    Array.Copy(buffer, 6, bDeviceToken, 0, 32);

                    fb.DeviceToken = BitConverter.ToString(bDeviceToken).Replace("-", "").ToLowerInvariant().Trim();

                    //Make sure we have a good feedback tuple
                    if (fb.DeviceToken.Length == 64 && fb.Timestamp > minTimestamp)
                    {
                        //Raise event
                        //this.Feedback(this, fb);
                        feedbacks.Add(fb);
                    }

                    //Clear our array to reuse it
                    Array.Clear(buffer, 0, buffer.Length);

                    //Read the next feedback
                    recd = _connection.ApnsStream.Read(buffer, 0, buffer.Length);
                }

                //close the connection here !
                _connection.Disconnect();
                if (feedbacks.Count > 0)
                {
                    _connection.Logger.Info("Total {0} feedbacks received.", feedbacks.Count);
                }

                return feedbacks;                
            }
            catch (Exception ex)
            {
                _connection.Logger.Error("Error occurred on receiving feed back. - " + ex.Message);
                return null;
            }
        }
    }
}
