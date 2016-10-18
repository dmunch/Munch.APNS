using System;

namespace APNS.Console
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            
            var connection = new NotificationConnection(true, "certificate.pfx", "lalala");
            var client = new PushNotification(connection);

            var notification = new NotificationPayload("<334b1ddf c30c4582 c497c058 c0d94ccf ad6409b2 7fa1ffbf 6f372427 7ba33e54>");

            notification.Badge = 0;
            notification.Alert = new Alert() { Body = "hello" };
            client.SendAsync(new[] { notification, notification, notification }).Wait();
            
            var feedbackConnection = new FeedbackConnection(true, "certificate.pfx", "lalala");
            var feedbackReader = new FeedbackReader(feedbackConnection);


            var feedback = feedbackReader.GetFeedBackAsync().Result;

            System.Console.ReadLine();
        }
    }
}
