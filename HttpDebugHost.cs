using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using WN.Retail.iScan.POSAdapterLibrary.DataObjects;

namespace ScoPosAdapter
{
    /// <summary>
    /// Локальный HTTP для отладки без GUI VSS. Не путать с FMSK :15001.
    /// Необязателен: если не запустился, адаптер продолжает работать с VSS.
    /// </summary>
    internal sealed class HttpDebugHost
    {
        public const string DefaultPrefix = "http://127.0.0.1:15002/";

        private HttpListener _listener;
        private Thread _thread;
        private volatile bool _running;
        private AdapterHost _host;

        public void Start(AdapterHost host, string prefix)
        {
            _host = host;
            var listener = new HttpListener();
            listener.Prefixes.Add(prefix);
            try
            {
                listener.Start();
            }
            catch (HttpListenerException ex)
            {
                listener.Close();
                throw new InvalidOperationException(
                    "Не удалось занять " + prefix + " (код " + ex.ErrorCode + ": " + ex.Message + "). "
                    + "Не используйте 15000/15001; 183 — порт занят другим процессом; 5 — нет urlacl: "
                    + "netsh http add urlacl url=" + prefix + " sddl=D:(A;;GX;;;WD)", ex);
            }

            _listener = listener;
            _running = true;
            _thread = new Thread(Listen) { IsBackground = true, Name = "SCO-HTTP" };
            _thread.Start();
        }

        public void Stop()
        {
            _running = false;
            try { _listener?.Stop(); } catch { /* ignore */ }
            try { _listener?.Close(); } catch { /* ignore */ }
        }

        private void Listen()
        {
            while (_running)
            {
                HttpListenerContext context;
                try
                {
                    context = _listener.GetContext();
                }
                catch (HttpListenerException)
                {
                    if (!_running)
                        break;
                    continue;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (InvalidOperationException)
                {
                    break;
                }

                try
                {
                    Handle(context);
                }
                catch (Exception ex)
                {
                    AdapterLog.Write("HTTP " + context.Request.HttpMethod + " " + context.Request.RawUrl + ": " + ex);
                    TryWriteJson(context, 500, "{\"result\":\"IFC_ERROR_OTHER\"}");
                }
            }
        }

        private void Handle(HttpListenerContext context)
        {
            var path = (context.Request.Url.AbsolutePath ?? "").TrimEnd('/').ToLowerInvariant();
            var method = context.Request.HttpMethod;
            var serializer = new JavaScriptSerializer();

            if (path == "/health" && method == "GET")
            {
                var since = _host.VssConnectedSince;
                var health = new Dictionary<string, object>
                {
                    { "ok", true },
                    { "posStatus", _host.Adapter.CurrentStatus.Status },
                    { "useErpMock", _host.UseErpMock },
                    { "vssEnabled", _host.VssEnabled },
                    { "vssConnected", _host.VssConnected },
                    { "vssConnectedSince", since.HasValue ? since.Value.ToString("yyyy-MM-dd HH:mm:ss") : null },
                    { "vssLastError", _host.VssLastError }
                };
                TryWriteJson(context, 200, serializer.Serialize(health));
                return;
            }

            if (path == "/simulate/getitemdetails" && method == "POST")
            {
                string body;
                using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
                    body = reader.ReadToEnd();

                Dictionary<string, object> map;
                try
                {
                    map = serializer.Deserialize<Dictionary<string, object>>(body) ?? new Dictionary<string, object>();
                }
                catch (ArgumentException)
                {
                    TryWriteJson(context, 400, "{\"result\":\"IFC_ERROR_OTHER\",\"failureReason\":\"invalid JSON\"}");
                    return;
                }

                var itemCode = map.ContainsKey("itemCode") ? Convert.ToString(map["itemCode"]) : "";
                var ctx = map.ContainsKey("context") ? Convert.ToString(map["context"]) : "scanning";

                var info = new IfcItemDetailsInfo { ItemCode = itemCode, EntryMethod = "customer scanned" };
                IfcItemDetails details;
                string result;
                IfcMessage failure;
                bool changed;
                _host.Adapter.GetItemDetails(null, info, ctx, out details, out result, out failure, out changed);

                var response = new Dictionary<string, object> { { "result", result } };
                if (details != null)
                {
                    response["itemId"] = details.ItemId;
                    response["itemDescription"] = details.ItemDescription;
                    response["itemPrice"] = details.ItemPrice;
                }
                if (failure != null && !string.IsNullOrEmpty(failure.UntranslatableText))
                    response["failureReason"] = failure.UntranslatableText;

                TryWriteJson(context, 200, serializer.Serialize(response));
                return;
            }

            TryWriteJson(context, 404, "{\"result\":\"IFC_ERROR_OTHER\"}");
        }

        /// <summary>Клиент мог уже закрыть соединение: ошибка записи не должна выходить из потока и ронять процесс.</summary>
        private static void TryWriteJson(HttpListenerContext context, int status, string json)
        {
            try
            {
                var buffer = Encoding.UTF8.GetBytes(json);
                context.Response.StatusCode = status;
                context.Response.ContentType = "application/json; charset=utf-8";
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                context.Response.Close();
            }
            catch (Exception ex)
            {
                AdapterLog.Write("HTTP: не удалось отправить ответ: " + ex.Message);
                try { context.Response.Abort(); } catch { /* ignore */ }
            }
        }
    }
}
