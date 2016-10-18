using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace APNS
{
    public interface ILogger
    {
        void Error(string message, params object[] val);
        void Info(string message, params object[] val);
    }
    public class ConsoleLogger : ILogger
    {
        public void Error(string message, params object[] val)
        {
            Console.WriteLine($"ERROR: {message}", val);
        }

        public void Info(string message, params object[] val)
        {
            Console.WriteLine($"INFO: {message}", val);
        }
    }
}
