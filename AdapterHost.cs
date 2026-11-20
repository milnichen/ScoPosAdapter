using System;
using System.Configuration;
using System.Threading;
using ScoPosAdapter.Erp;
using ScoPosAdapter.Vss;

namespace ScoPosAdapter
{
    internal sealed class AdapterHost
    {
        private readonly HttpDebugHost _http = new HttpDebugHost();
        private readonly ManualResetEvent _stop = new ManualResetEvent(false);
        private readonly object _sync = new object();
        private Thread _vssThread;
        private VssConnector _connector;
        private volatile bool _running;
        private volatile bool _vssConnected;
        private volatile string _vssLastError;
        private DateTime? _vssConnectedSince;

        public VssPosAdapter Adapter { get; private set; }
        public bool UseErpMock { get; private set; }
        public bool VssEnabled { get; private set; }
        public bool VssConnected { get { return _vssConnected; } }
        public string VssLastError { get { return _vssLastError; } }
        public DateTime? VssConnectedSince { get { lock (_sync) return _vssConnectedSince; } }

        public void Start()
        {
            UseErpMock = Flag("UseErpMock");
            IErpClient erp = UseErpMock ? (IErpClient)new MockErpClient() : new HttpErpClient();
            Adapter = new VssPosAdapter(erp, UseErpMock);
            _running = true;

            var prefix = ConfigurationManager.AppSettings["ListenPrefix"] ?? HttpDebugHost.DefaultPrefix;
            try
            {
                _http.Start(this, prefix);
                AdapterLog.Write("HTTP debug on " + prefix + " UseErpMock=" + UseErpMock);
            }
            catch (Exception ex)
            {
                AdapterLog.Write("HTTP debug отключён, адаптер работает без него: " + ex.Message);
            }

            VssEnabled = Flag("EnableVss");
            if (!VssEnabled)
            {
                AdapterLog.Write("EnableVss=false — соединение с VSS не открывается.");
                return;
            }

            _vssThread = new Thread(RunVss) { IsBackground = true, Name = "VSS-Connector" };
            _vssThread.Start();
        }

        public void Stop()
        {
            _running = false;
            _stop.Set();
            for (var i = 0; i < 3; i++)
            {
                VssConnector connector;
                lock (_sync)
                    connector = _connector;
                connector?.Close();
                if (_vssThread == null || _vssThread.Join(TimeSpan.FromSeconds(2)))
                    break;
            }
            if (_vssThread != null && _vssThread.IsAlive)
                AdapterLog.Write("VSS connector не остановился за 6 с");
            _http.Stop();
            AdapterLog.Write("Adapter stopped");
        }

        private void RunVss()
        {
            var endpoints = ReadEndpoints();
            var delay = TimeSpan.FromSeconds(Math.Max(1, Number("VssReconnectSeconds", 5)));
            AdapterLog.Write("VSS: адаптер подключается как клиент к " + endpoints + ", повтор каждые " + delay.TotalSeconds + " с");

            string lastLogged = null;
            while (_running)
            {
                var connector = VssConnector.Create(endpoints, Adapter, OnVssConnected);
                lock (_sync)
                    _connector = connector;
                if (!_running)
                {
                    connector.Close();
                    break;
                }

                string error;
                try
                {
                    connector.Run();
                    error = "соединение с VSS закрыто";
                }
                catch (Exception ex)
                {
                    error = ex.GetType().Name + ": " + ex.Message;
                }
                finally
                {
                    lock (_sync)
                    {
                        _connector = null;
                        _vssConnectedSince = null;
                    }
                    connector.Close();
                }

                var wasConnected = _vssConnected;
                _vssConnected = false;
                if (!_running)
                    break;

                _vssLastError = error;
                if (wasConnected || error != lastLogged)
                {
                    AdapterLog.Write("VSS: " + error + (wasConnected ? "" : " (дальше повторы молча, пока ошибка та же)"));
                    lastLogged = error;
                }

                if (_stop.WaitOne(delay))
                    break;
            }
        }

        private void OnVssConnected()
        {
            lock (_sync)
                _vssConnectedSince = DateTime.Now;
            _vssConnected = true;
            _vssLastError = null;
            AdapterLog.Write("VSS: все каналы подключены");
        }

        private static VssEndpoints ReadEndpoints()
        {
            var endpoints = new VssEndpoints
            {
                MainHost = Host("VssMainHost"),
                MainPort = Number("VssMainPort", 17002),
                CancelHost = Host("VssCancelHost"),
                CancelPort = Number("VssCancelPort", 17003),
                PosEventsHost = Host("VssPosEventsHost"),
                PosEventsPort = Number("VssPosEventsPort", 17004),
                CancelPosEventsHost = Host("VssCancelPosEventsHost"),
                CancelPosEventsPort = Number("VssCancelPosEventsPort", 17005)
            };
            if (Flag("VssAuthEnabled"))
            {
                endpoints.AuthHost = Host("VssAuthHost");
                endpoints.AuthPort = Number("VssAuthPort", 17006);
            }
            return endpoints;
        }

        private static string Host(string key)
        {
            var value = ConfigurationManager.AppSettings[key];
            return string.IsNullOrWhiteSpace(value) ? "127.0.0.1" : value.Trim();
        }

        private static int Number(string key, int fallback)
        {
            int value;
            return int.TryParse(ConfigurationManager.AppSettings[key], out value) ? value : fallback;
        }

        private static bool Flag(string key)
        {
            return string.Equals(ConfigurationManager.AppSettings[key], "true", StringComparison.OrdinalIgnoreCase);
        }
    }
}
