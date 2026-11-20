using System.ServiceProcess;

namespace ScoPosAdapter
{
    internal sealed class AdapterWindowsService : ServiceBase
    {
        private readonly AdapterHost _host = new AdapterHost();

        public AdapterWindowsService()
        {
            ServiceName = "ScoPosAdapter";
            CanStop = true;
            CanPauseAndContinue = false;
        }

        protected override void OnStart(string[] args)
        {
            _host.Start();
        }

        protected override void OnStop()
        {
            _host.Stop();
        }
    }
}
