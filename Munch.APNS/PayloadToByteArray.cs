using System;
using System.IO;
using System.Text;

namespace APNS
{
    public static class PayloadToByteArray
    {
        private static readonly DateTime UNIX_EPOCH = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        internal static MemoryStream GeneratePayload(NotificationPayload payload, ILogger logger)
        {
            try
            {
                //convert Devide token to HEX value.
                byte[] deviceToken = new byte[payload.DeviceToken.Length / 2];
                for (int i = 0; i < deviceToken.Length; i++)
                    deviceToken[i] = byte.Parse(payload.DeviceToken.Substring(i * 2, 2), System.Globalization.NumberStyles.HexNumber);


                var frameMemStream = new MemoryStream();
                var endianWriter = new MiscUtil.IO.EndianBinaryWriter(MiscUtil.Conversion.EndianBitConverter.Big, frameMemStream);

                //item 1, device token
                endianWriter.Write((byte)1);
                endianWriter.Write((UInt16)32);
                frameMemStream.Write(deviceToken, 0, 32);

                //item 2, Payload
                string apnMessage = payload.ToJson();
                endianWriter.Write((byte)2);
                endianWriter.Write((UInt16)apnMessage.Length);
                frameMemStream.Write(Encoding.UTF8.GetBytes(apnMessage), 0, apnMessage.Length);

                //item 3, Identifier
                endianWriter.Write((byte)3);
                endianWriter.Write((UInt16)4);
                endianWriter.Write((UInt32)payload.PayloadId);

                //item 4, Expiry date
                DateTime concreteExpireDateUtc = (DateTime.UtcNow.AddMonths(1)).ToUniversalTime();
                TimeSpan epochTimeSpan = concreteExpireDateUtc - UNIX_EPOCH;
                var expiryTimeStamp = (int)epochTimeSpan.TotalSeconds;

                endianWriter.Write((byte)4);
                endianWriter.Write((UInt16)4);
                endianWriter.Write((UInt32)expiryTimeStamp);

                //item5
                endianWriter.Write((byte)5);
                endianWriter.Write((UInt16)1);
                frameMemStream.WriteByte(5);

                return frameMemStream;
            }
            catch (Exception ex)
            {
                logger.Error("Unable to generate payload - " + ex.Message);
                return null;
            }
        }
    }
}
