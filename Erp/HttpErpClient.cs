using System;
using System.Collections.Generic;
using System.Configuration;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using WN.Retail.iScan.POSAdapterLibrary.DataObjects;

namespace ScoPosAdapter.Erp
{
    internal sealed class HttpErpClient : IErpClient
    {
        private const int MaxLoggedBody = 500;

        private readonly HttpClient _http;
        private readonly string _baseUrl;
        private readonly string _laneId;
        private readonly int _timeoutSeconds;
        private readonly int _paymentTimeoutSeconds;

        public HttpErpClient()
        {
            _timeoutSeconds = Number("ErpTimeoutSeconds", 15);
            _paymentTimeoutSeconds = Math.Max(_timeoutSeconds, Number("ErpPaymentTimeoutSeconds", 120));

            var auth = (ConfigurationManager.AppSettings["ErpAuth"] ?? "None").Trim();
            var handler = new HttpClientHandler();
            if (string.Equals(auth, "Os", StringComparison.OrdinalIgnoreCase))
                handler.UseDefaultCredentials = true;

            _http = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(_paymentTimeoutSeconds + 5)
            };

            if (string.Equals(auth, "Basic", StringComparison.OrdinalIgnoreCase))
            {
                var user = ConfigurationManager.AppSettings["ErpUserName"] ?? "";
                var password = ConfigurationManager.AppSettings["ErpPassword"] ?? "";
                var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(user + ":" + password));
                _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
            }
            else if (!string.Equals(auth, "Os", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(auth, "None", StringComparison.OrdinalIgnoreCase))
            {
                AdapterLog.Write("ErpAuth='" + auth + "' не поддерживается (None | Os | Basic), запросы пойдут без авторизации.");
            }

            _baseUrl = (ConfigurationManager.AppSettings["ErpBaseUrl"] ?? "").TrimEnd('/');
            _laneId = ConfigurationManager.AppSettings["LaneId"] ?? "SCO01";
            AdapterLog.Write("ERP " + _baseUrl + " lane=" + _laneId + " auth=" + auth
                + " timeout=" + _timeoutSeconds + "s paymentTimeout=" + _paymentTimeoutSeconds + "s");
        }

        public ErpResult GetStatus(CancellationToken cancellation)
        {
            return Send(HttpMethod.Get, "status", null, _timeoutSeconds, cancellation);
        }

        public ErpResult GetGlobalProperties(CancellationToken cancellation)
        {
            return Send(HttpMethod.Get, "global-properties", null, _timeoutSeconds, cancellation);
        }

        public ErpResult SignOn(string name, string password, bool trainingMode, CancellationToken cancellation)
        {
            return Send(HttpMethod.Post, "sign-on", new Dictionary<string, object>
            {
                { "name", name },
                { "password", password },
                { "trainingMode", trainingMode }
            }, _timeoutSeconds, cancellation);
        }

        public ErpResult SignOff(CancellationToken cancellation)
        {
            return Send(HttpMethod.Post, "sign-off", new Dictionary<string, object>(), _timeoutSeconds, cancellation);
        }

        public ErpResult ResolveItem(string itemCode, string entryMethod, string context, bool salesReturn, CancellationToken cancellation)
        {
            return Send(HttpMethod.Post, "items/resolve", new Dictionary<string, object>
            {
                { "context", context },
                { "itemCode", itemCode },
                { "entryMethod", entryMethod },
                { "info", new Dictionary<string, object>
                    {
                        { "itemCode", itemCode },
                        { "entryMethod", entryMethod },
                        { "salesReturn", salesReturn }
                    }
                }
            }, _timeoutSeconds, cancellation);
        }

        public ErpResult StartTransaction(string language, bool salesReturn, CancellationToken cancellation)
        {
            return Send(HttpMethod.Post, "transactions", new Dictionary<string, object>
            {
                { "language", language },
                { "salesReturn", salesReturn }
            }, _timeoutSeconds, cancellation);
        }

        public ErpResult AddItem(IfcItemInfo info, CancellationToken cancellation)
        {
            var item = info ?? new IfcItemInfo();
            return Send(HttpMethod.Post, "items", new Dictionary<string, object>
            {
                { "info", new Dictionary<string, object>
                    {
                        { "itemId", item.ItemId },
                        { "itemCode", item.ItemCode },
                        { "codeType", item.CodeType },
                        { "scanDataType", item.ScanDataType },
                        { "entryMethod", item.EntryMethod },
                        { "quantity", item.Quantity },
                        { "weight", item.Weight },
                        { "referencePricePerUnit", item.ReferencePricePerUnit },
                        { "finalPrice", item.FinalPrice },
                        { "forceUseOfFinalPrice", item.ForceUseOfFinalPrice },
                        { "salesReturn", item.SalesReturn }
                    }
                }
            }, _timeoutSeconds, cancellation);
        }

        public ErpResult VoidEntry(string entryId, CancellationToken cancellation)
        {
            var id = Uri.EscapeDataString(entryId ?? "");
            return Send(HttpMethod.Post, "items/" + id + "/void", new Dictionary<string, object>
            {
                { "entryId", entryId }
            }, _timeoutSeconds, cancellation);
        }

        public ErpResult ProcessCustomerCard(IfcCardInfo info, CancellationToken cancellation)
        {
            var card = info ?? new IfcCardInfo();
            return Send(HttpMethod.Post, "loyalty", new Dictionary<string, object>
            {
                { "scanCode", card.ScanCode },
                { "info", new Dictionary<string, object>
                    {
                        { "scanCode", card.ScanCode },
                        { "entryMethod", card.EntryMethod },
                        { "scanDataType", card.ScanDataType }
                    }
                }
            }, _timeoutSeconds, cancellation);
        }

        public ErpResult PreparePayment(CancellationToken cancellation)
        {
            return Send(HttpMethod.Post, "payment/prepare", new Dictionary<string, object>(), _timeoutSeconds, cancellation);
        }

        public ErpResult PayWithCard(IfcCardInfo info, string tenderType, long amount, CancellationToken cancellation)
        {
            var card = info ?? new IfcCardInfo();
            return Send(HttpMethod.Post, "payment/card", new Dictionary<string, object>
            {
                { "tenderType", tenderType },
                { "amount", amount },
                { "info", new Dictionary<string, object>
                    {
                        { "scanCode", card.ScanCode },
                        { "entryMethod", card.EntryMethod },
                        { "scanDataType", card.ScanDataType }
                    }
                }
            }, _paymentTimeoutSeconds, cancellation);
        }

        public ErpResult Special(string function, IList<string> parameters, string selectedEntryId, CancellationToken cancellation)
        {
            return Send(HttpMethod.Post, "special", new Dictionary<string, object>
            {
                { "function", function },
                { "parameters", parameters ?? new List<string>() },
                { "selectedEntryId", selectedEntryId }
            }, _timeoutSeconds, cancellation);
        }

        public ErpResult VoidTransaction(string reason, CancellationToken cancellation)
        {
            return Send(HttpMethod.Post, "transactions/void", new Dictionary<string, object>
            {
                { "reason", reason ?? "" }
            }, _timeoutSeconds, cancellation);
        }

        public ErpResult FinishTransaction(CancellationToken cancellation)
        {
            return Send(HttpMethod.Post, "transactions/finish", new Dictionary<string, object>(), _timeoutSeconds, cancellation);
        }

        private ErpResult Send(HttpMethod method, string path, IDictionary<string, object> body, int timeoutSeconds, CancellationToken cancellation)
        {
            var url = _baseUrl + "/sco/v1/lanes/" + Uri.EscapeDataString(_laneId) + "/" + path;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
                try
                {
                    using (var request = new HttpRequestMessage(method, url))
                    {
                    if (method != HttpMethod.Get)
                        request.Content = new StringContent(ErpMapper.ToJson(body), Encoding.UTF8, "application/json");

                    var response = _http.SendAsync(request, timeout.Token).GetAwaiter().GetResult();
                    var text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    using (response)
                    {
                        if (!response.IsSuccessStatusCode)
                            return Fail(url, "1C returned HTTP " + (int)response.StatusCode + " " + response.ReasonPhrase + ": " + Cut(text));

                        try
                        {
                            return ErpMapper.ToResult(ErpMapper.FromJsonObject(text));
                        }
                        catch (Exception ex)
                        {
                            return Fail(url, "1C response is not valid JSON: " + ex.Message + " Body: " + Cut(text));
                        }
                    }
                    }
                }
                catch (OperationCanceledException)
                {
                    return Fail(url, cancellation.IsCancellationRequested
                        ? "1C request cancelled by VSS"
                        : "1C did not respond within " + timeoutSeconds + " s");
                }
                catch (HttpRequestException ex)
                {
                    return Fail(url, "No connection to 1C: " + (ex.InnerException ?? ex).Message);
                }
            }
        }

        private static ErpResult Fail(string url, string text)
        {
            AdapterLog.Write("ERP " + url + " — " + text);
            return ErpResult.Fail(text);
        }

        private static string Cut(string body)
        {
            if (string.IsNullOrEmpty(body))
                return "<empty>";
            return body.Length <= MaxLoggedBody ? body : body.Substring(0, MaxLoggedBody) + "…";
        }

        private static int Number(string key, int fallback)
        {
            int value;
            return int.TryParse(ConfigurationManager.AppSettings[key], out value) && value > 0 ? value : fallback;
        }
    }
}
