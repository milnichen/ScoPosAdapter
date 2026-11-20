using System;
using System.Configuration;
using System.IO;
using System.Text;

namespace ScoPosAdapter
{
    internal static class AdapterLog
    {
        private static readonly object Gate = new object();

        public static void Write(string message)
        {
            var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message;
            Console.WriteLine(line);
            try
            {
                var dir = ConfigurationManager.AppSettings["LogDirectory"] ?? "logs";
                if (!Path.IsPathRooted(dir))
                    dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dir);
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, "sco-pos-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log");
                lock (Gate)
                    File.AppendAllText(file, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // logging must not break POS
            }
        }
    }
}
