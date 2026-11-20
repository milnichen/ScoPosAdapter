using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Reflection;
using WN.Retail.iScan.POSAdapterLibrary;

namespace ScoPosAdapter.Vss
{
    /// <summary>
    /// Одно подключение к VSS. Адаптер — TCP-клиент (hostName != null), VSS должен слушать порты заранее.
    /// Run() блокирует до обрыва любого канала. Объект одноразовый: на каждую попытку создаётся новый.
    /// </summary>
    internal sealed class VssConnector : IfcIScanConnector
    {
        private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private readonly Action _connected;
        private readonly VssPosAdapter _adapter;

        private VssConnector(VssEndpoints endpoints, VssPosAdapter adapter, Action connected)
            : base(
                endpoints.MainHost, endpoints.MainPort, adapter,
                endpoints.CancelHost, endpoints.CancelPort, adapter,
                endpoints.PosEventsHost, endpoints.PosEventsPort,
                endpoints.CancelPosEventsHost, endpoints.CancelPosEventsPort)
        {
            _adapter = adapter;
            _connected = connected;
        }

        private VssConnector(VssEndpoints endpoints, VssPosAdapter adapter, Action connected, string authHost, int authPort)
            : base(
                endpoints.MainHost, endpoints.MainPort, adapter,
                endpoints.CancelHost, endpoints.CancelPort, adapter,
                endpoints.PosEventsHost, endpoints.PosEventsPort,
                endpoints.CancelPosEventsHost, endpoints.CancelPosEventsPort,
                authHost, authPort, adapter)
        {
            _adapter = adapter;
            _connected = connected;
        }

        public static VssConnector Create(VssEndpoints endpoints, VssPosAdapter adapter, Action connected)
        {
            return endpoints.AuthHost == null
                ? new VssConnector(endpoints, adapter, connected)
                : new VssConnector(endpoints, adapter, connected, endpoints.AuthHost, endpoints.AuthPort);
        }

        public override void PostOpenConnections()
        {
            _adapter.AttachPosEvents(PosEventsGroup);
            _connected?.Invoke();
        }

        /// <summary>
        /// Закрывает все сокеты библиотеки. У библиотеки нет публичного Close, а её потоки каналов — foreground:
        /// без этого процесс не завершается и висит на открытых соединениях.
        /// </summary>
        public void Close()
        {
            _adapter.AttachPosEvents(null);
            foreach (var channel in Channels())
            {
                try
                {
                    (FieldValue(channel, "tcpClient") as TcpClient)?.Close();
                    (FieldValue(channel, "tcpListener") as TcpListener)?.Stop();
                }
                catch
                {
                    // socket already closed
                }
            }
        }

        private IEnumerable<object> Channels()
        {
            var channels = new List<object>();
            try
            {
                var manager = FieldValue(this, "channelManager");
                if (manager != null)
                {
                    foreach (var name in new[] { "receivers", "senders" })
                    {
                        var table = FieldValue(manager, name) as Hashtable;
                        if (table != null)
                            channels.AddRange(new ArrayList(table.Values).ToArray());
                    }
                }
                foreach (var name in new[] { "ifcPosEventsGroup", "ifcCancelPosEventsGroup" })
                {
                    var sender = FieldValue(this, name);
                    if (sender != null && !channels.Contains(sender))
                        channels.Add(sender);
                }
            }
            catch (Exception ex)
            {
                AdapterLog.Write("VSS: не удалось перечислить каналы для закрытия: " + ex.Message);
            }
            return channels;
        }

        private static object FieldValue(object target, string name)
        {
            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(name, InstanceFields | BindingFlags.DeclaredOnly);
                if (field != null)
                    return field.GetValue(target);
            }
            return null;
        }
    }

    internal sealed class VssEndpoints
    {
        public string MainHost;
        public int MainPort;
        public string CancelHost;
        public int CancelPort;
        public string PosEventsHost;
        public int PosEventsPort;
        public string CancelPosEventsHost;
        public int CancelPosEventsPort;
        public string AuthHost;
        public int AuthPort;

        public override string ToString()
        {
            return "main " + MainHost + ":" + MainPort
                + ", cancel " + CancelHost + ":" + CancelPort
                + ", posEvents " + PosEventsHost + ":" + PosEventsPort
                + ", cancelPosEvents " + CancelPosEventsHost + ":" + CancelPosEventsPort
                + (AuthHost == null ? "" : ", auth " + AuthHost + ":" + AuthPort);
        }
    }
}
