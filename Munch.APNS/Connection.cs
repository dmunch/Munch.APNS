using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace APNS
{
    public class FeedbackConnection : Connection
    {
        // Default configurations for Feedback Service
        private const string ProductionFeedbackHost = "feedback.push.apple.com";
        private const string SandboxFeedbackHost = "feedback.sandbox.push.apple.com";
                
        public FeedbackConnection(bool useSandbox, string p12File, string p12FilePassword)
            :base(useSandbox ? SandboxFeedbackHost : ProductionFeedbackHost, 2195, p12File, p12FilePassword)
        {
        }
    }

    public class NotificationConnection : Connection
    {
        // Default configurations for APNS
        private const string ProductionHost = "gateway.push.apple.com";
        private const string SandboxHost = "gateway.sandbox.push.apple.com";        
                
        public NotificationConnection(bool useSandbox, string p12File, string p12FilePassword)
            :base(useSandbox ? SandboxHost : ProductionHost, 2195, p12File, p12FilePassword)
        {
        }
    }

    public class Connection
    {
        public TcpClient ApnsClient { get; protected set; }
        public SslStream ApnsStream { get; protected set; }

        private readonly X509Certificate _certificate;
        private readonly X509CertificateCollection _certificates;
        public ILogger Logger { get; protected set; } = new ConsoleLogger();

        public string P12File { get; set; }
        public string P12FilePassword { get; set; }

        private bool _connected = false;

        public readonly string Host;
        public readonly int Port;
        

        public Connection(string host, int port, string p12File, string p12FilePassword)
        {
            this.Host = host;
            this.Port = port;

            //Load Certificates in to collection.
            _certificate = string.IsNullOrEmpty(p12FilePassword)
                ? new X509Certificate2(File.ReadAllBytes(p12File))
                : new X509Certificate2(File.ReadAllBytes(p12File), p12FilePassword);

            _certificates = new X509CertificateCollection { _certificate };
        }
        public bool Connected
        {
            get
            {
                return _connected;
            }
        }

        public Task ConnectAsync()
        {
            return ConnectAsync(Host, Port, _certificates);
        }

        private async Task ConnectAsync(string host, int port, X509CertificateCollection certificates)
        {
            Logger.Info("Connecting to apple server.");
            try
            {
                ApnsClient = new TcpClient();
                await ApnsClient.ConnectAsync(host, port).ConfigureAwait(false);

                //Set keep alive on the socket may help maintain our APNS connection
                try
                {
                    ApnsClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                }
                catch
                {
                    //ignore
                }

                //Really not sure if this will work on MONO....
                // This may help windows azure users
                try
                {
                    SetSocketKeepAliveValues(ApnsClient.Client, 5000, 5000);
                }
                catch
                {
                    //ignore
                }
            }
            catch (SocketException ex)
            {
                Logger.Error("An error occurred while connecting to APNS servers - " + ex.Message);
            }

            if (await OpenSslStreamAsync(host, certificates))
            {
                _connected = true;
                Logger.Info("Connected.");
            }
        }

        public void Disconnect()
        {
            try
            {
                ApnsClient.Dispose();
                ApnsStream.Dispose();
                ApnsStream = null;
                ApnsClient = null;
                _connected = false;

                Logger.Info("Disconnected.");
            }
            catch (Exception ex)
            {
                Logger.Error("An error occurred while disconnecting. - " + ex.Message);
            }
        }

        private async Task<bool> OpenSslStreamAsync(string host, X509CertificateCollection certificates)
        {
            Logger.Info("Creating SSL connection.");
            ApnsStream = new SslStream(ApnsClient.GetStream(), false, ValidateServerCertificate, SelectLocalCertificate);

            try
            {
                await ApnsStream.AuthenticateAsClientAsync(host, certificates, System.Security.Authentication.SslProtocols.Tls12, false).ConfigureAwait(false);
            }
            catch (System.Security.Authentication.AuthenticationException ex)
            {
                Logger.Error(ex.Message);
                return false;
            }

            if (!ApnsStream.IsMutuallyAuthenticated)
            {
                Logger.Error("SSL Stream Failed to Authenticate");
                return false;
            }

            if (!ApnsStream.CanWrite)
            {
                Logger.Error("SSL Stream is not Writable");
                return false;
            }
            return true;
        }

        private bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return true; // Dont care about server's cert
        }

        private X509Certificate SelectLocalCertificate(object sender, string targetHost, X509CertificateCollection localCertificates, X509Certificate remoteCertificate, string[] acceptableIssuers)
        {
            return _certificate;
        }


        /// <summary>
        /// Using IOControl code to configue socket KeepAliveValues for line disconnection detection(because default is toooo slow) 
        /// </summary>
        /// <param name="tcpc">TcpClient</param>
        /// <param name="KeepAliveTime">The keep alive time. (ms)</param>
        /// <param name="KeepAliveInterval">The keep alive interval. (ms)</param>
        public static void SetSocketKeepAliveValues(Socket socket, int KeepAliveTime, int KeepAliveInterval)
        {
            //KeepAliveTime: default value is 2hr
            //KeepAliveInterval: default value is 1s and Detect 5 times

            uint dummy = 0; //lenth = 4
            byte[] inOptionValues = new byte[System.Runtime.InteropServices.Marshal.SizeOf(dummy) * 3]; //size = lenth * 3 = 12

            BitConverter.GetBytes((uint)1).CopyTo(inOptionValues, 0);
            BitConverter.GetBytes((uint)KeepAliveTime).CopyTo(inOptionValues, System.Runtime.InteropServices.Marshal.SizeOf(dummy));
            BitConverter.GetBytes((uint)KeepAliveInterval).CopyTo(inOptionValues, System.Runtime.InteropServices.Marshal.SizeOf(dummy) * 2);
            // of course there are other ways to marshal up this byte array, this is just one way
            // call WSAIoctl via IOControl

            // .net 3.5 type
            socket.IOControl(IOControlCode.KeepAliveValues, inOptionValues, null);
        }
    }
}
