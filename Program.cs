using System;
using System.ServiceProcess;
using System.Threading;

namespace ScoPosAdapter
{
    internal static class Program
    {
        /// <summary>
        /// Без аргументов — консольный режим (так exe запускает VSS, в том числе из своей службы).
        /// --service — только для Windows-службы, binPath ставит install-service.ps1.
        /// </summary>
        private static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                AdapterLog.Write("Необработанное исключение, процесс завершается: " + e.ExceptionObject);

            if (Array.Exists(args, a => string.Equals(a, "--service", StringComparison.OrdinalIgnoreCase)))
            {
                ServiceBase.Run(new AdapterWindowsService());
                Environment.Exit(0);
                return;
            }

            var host = new AdapterHost();
            var stop = new ManualResetEvent(false);
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                stop.Set();
            };
            AppDomain.CurrentDomain.ProcessExit += (s, e) => stop.Set();

            host.Start();
            Console.WriteLine("SCO POS adapter. Health GET http://127.0.0.1:15002/health");
            Console.WriteLine("Simulate POST http://127.0.0.1:15002/simulate/getItemDetails");
            Console.WriteLine("Ctrl+C to stop.");
            stop.WaitOne();
            host.Stop();
            Environment.Exit(0);
        }
    }
}
