using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Linq;
using System.Threading;
using ScoPosAdapter.Erp;
using WN.Retail.iScan.POSAdapterLibrary;
using WN.Retail.iScan.POSAdapterLibrary.DataObjects;
using WN.Retail.iScan.POSAdapterLibrary.Util;

namespace ScoPosAdapter.Vss
{
    /// <summary>
    /// Реализация входящих групп VSS (main / cancel / authentication).
    /// VSS вызывает эти методы через csPOSAdapterLibrary.
    /// Любое исключение отсюда библиотека не ловит: канал main закрывается и связь с VSS рвётся,
    /// поэтому каждый метод обязан вернуть результат, а не бросить.
    /// </summary>
    internal sealed partial class VssPosAdapter : IfcAllInboundGroupsBaseImpl
    {
        private readonly IErpClient _erp;
        private readonly bool _localPayments;
        private readonly object _gate = new object();
        private readonly Dictionary<string, ResolvedItem> _resolved = new Dictionary<string, ResolvedItem>(StringComparer.Ordinal);

        private string _state = IfcPOSStatus.STATUS_OPERATOR_SIGNED_OFF;
        private bool _trainingMode;
        private Basket _basket;
        private int _transactionCounter;
        private CancellationTokenSource _inflight;
        private IfcPOSStatus _remoteStatus;
        private IIfcPosEventsGroup _posEvents;
        private bool _bagsAsked;

        /// <param name="localPayments">
        /// Принимать оплату локально, без BAF/Adyen. Только для стенда с моком 1С.
        /// </param>
        public VssPosAdapter(IErpClient erp, bool localPayments)
        {
            _erp = erp;
            _localPayments = localPayments;
        }

        public IfcPOSStatus CurrentStatus
        {
            get { return SafeStatus(); }
        }

        public void AttachPosEvents(IIfcPosEventsGroup posEvents)
        {
            _posEvents = posEvents;
        }

        public override void GetItemDetails(
            IfcAuthenticationData authenticationData,
            IfcItemDetailsInfo info,
            string context,
            out IfcItemDetails itemDetails,
            out string result,
            out IfcMessage failureReason,
            out bool posStatusChanged)
        {
            itemDetails = null;
            failureReason = null;
            posStatusChanged = false;
            result = IfcResult.IFC_ERROR_OTHER;

            var code = info == null ? "" : info.ItemCode ?? "";
            AdapterLog.Write("GetItemDetails itemCode=" + code + " context=" + context);

            try
            {
                var erp = CallErp("GetItemDetails", t => _erp.ResolveItem(
                    code,
                    info == null ? null : info.EntryMethod,
                    context,
                    info != null && info.SalesReturn,
                    t));
                result = string.IsNullOrEmpty(erp.Result) ? IfcResult.IFC_ERROR_OTHER : erp.Result;
                failureReason = erp.FailureReason;
                posStatusChanged = erp.PosStatusChanged;
                if (result != IfcResult.IFC_OK)
                    return;

                itemDetails = erp.ItemDetails;
                if (itemDetails == null)
                {
                    result = IfcResult.IFC_ERROR_OTHER;
                    failureReason = Message("1C returned no itemDetails");
                    return;
                }

                if (_localPayments)
                {
                    if (itemDetails.ItemPrice <= 0)
                    {
                        result = IfcResult.IFC_ERROR_OTHER;
                        failureReason = Message("1C returned item " + code + " without a price");
                        itemDetails = null;
                        return;
                    }
                    if (string.IsNullOrEmpty(itemDetails.ItemDescription))
                    {
                        result = IfcResult.IFC_ERROR_OTHER;
                        failureReason = Message("1C returned item " + code + " without a description");
                        itemDetails = null;
                        return;
                    }
                    lock (_gate)
                    {
                        var resolved = new ResolvedItem
                        {
                            ItemId = itemDetails.ItemId,
                            ItemCode = code,
                            Description = itemDetails.ItemDescription,
                            UnitPrice = itemDetails.ItemPrice
                        };
                        _resolved[resolved.ItemId] = resolved;
                        if (!string.IsNullOrEmpty(code))
                            _resolved[code] = resolved;
                    }
                }

                AdapterLog.Write("GetItemDetails OK " + itemDetails.ItemDescription + " price=" + itemDetails.ItemPrice);
            }
            catch (Exception ex)
            {
                result = IfcResult.IFC_ERROR_OTHER;
                failureReason = Message("Adapter error: " + ex.Message);
                AdapterLog.Write("GetItemDetails error " + ex);
            }
        }

        public override void AddItem(
            IfcAuthenticationData authenticationData,
            IfcItemInfo info,
            out IfcPOSStatus posStatus,
            out string result,
            out IfcMessage failureReason)
        {
            failureReason = null;
            result = IfcResult.IFC_ERROR_OTHER;
            if (!_localPayments)
            {
                if (info == null)
                {
                    failureReason = Message("addItem without item data");
                    posStatus = SafeStatus();
                    return;
                }
                if (info.Weight > 0)
                {
                    failureReason = Message("Weighted items are not supported");
                    posStatus = SafeStatus();
                    return;
                }
                Apply(CallErp("AddItem", t => _erp.AddItem(info, t)), out result, out failureReason, out posStatus);
                return;
            }

            try
            {
                if (info == null)
                {
                    failureReason = Message("addItem без данных товара");
                    return;
                }

                AdapterLog.Write("AddItem itemId=" + info.ItemId + " itemCode=" + info.ItemCode + " qty=" + info.Quantity);
                if (info.Weight > 0)
                {
                    failureReason = Message("Весовые товары не поддерживаются");
                    return;
                }

                lock (_gate)
                {
                    if (!CanAddItem())
                    {
                        failureReason = Message("Добавление товара сейчас недоступно (статус " + _state + ")");
                        return;
                    }
                }

                var item = FindResolved(info.ItemId) ?? FindResolved(info.ItemCode);
                if (item == null)
                {
                    string reason;
                    item = Resolve(info.ItemCode ?? info.ItemId ?? "", info.EntryMethod, "addItem", info.SalesReturn, out result, out reason);
                    if (item == null)
                    {
                        failureReason = Message(reason ?? "Товар не найден в 1С (" + result + ")");
                        result = IfcResult.IFC_ERROR_OTHER;
                        return;
                    }
                }

                var quantity = info.Quantity > 0 ? info.Quantity : 1;
                var linePrice = info.ForceUseOfFinalPrice ? info.FinalPrice : checked(item.UnitPrice * quantity);

                lock (_gate)
                {
                    if (!CanAddItem())
                    {
                        failureReason = Message("Добавление товара сейчас недоступно (статус " + _state + ")");
                        return;
                    }

                    EnsureTransaction(false);
                    _basket.AddItem(info.ItemId ?? item.ItemId, item, quantity, linePrice);
                    result = IfcResult.IFC_OK;
                    AdapterLog.Write("AddItem OK " + item.Description + " x" + quantity + " = " + linePrice + " total=" + _basket.Total);
                }
            }
            catch (Exception ex)
            {
                result = IfcResult.IFC_ERROR_OTHER;
                failureReason = Message("Ошибка адаптера: " + ex.Message);
                AdapterLog.Write("AddItem error " + ex);
            }
            finally
            {
                posStatus = SafeStatus();
            }
        }

        public override void VoidEntry(
            IfcAuthenticationData authenticationData,
            string entryId,
            out IfcPOSStatus posStatus,
            out string result,
            out IfcMessage failureReason)
        {
            failureReason = null;
            result = IfcResult.IFC_ERROR_OTHER;
            if (!_localPayments)
            {
                Apply(CallErp("VoidEntry", t => _erp.VoidEntry(entryId, t)), out result, out failureReason, out posStatus);
                return;
            }

            try
            {
                lock (_gate)
                {
                    if (_state != IfcPOSStatus.STATUS_TRANSACTION_ACTIVE || _basket == null || _basket.Paid > 0)
                        failureReason = Message("Сторно позиции сейчас недоступно");
                    else if (!_basket.VoidItem(entryId))
                        failureReason = Message("Позиция " + entryId + " не найдена");
                    else
                        result = IfcResult.IFC_OK;
                }
                AdapterLog.Write("VoidEntry " + entryId + " result=" + result);
            }
            catch (Exception ex)
            {
                result = IfcResult.IFC_ERROR_OTHER;
                failureReason = Message("Ошибка адаптера: " + ex.Message);
                AdapterLog.Write("VoidEntry error " + ex);
            }
            finally
            {
                posStatus = SafeStatus();
            }
        }

        public override void VoidTransaction(
            IfcAuthenticationData authenticationData,
            string reason,
            out IfcPOSStatus posStatus,
            out string result,
            out IfcMessage failureReason)
        {
            failureReason = null;
            result = IfcResult.IFC_OK;
            if (!_localPayments)
            {
                _bagsAsked = false;
                Apply(CallErp("VoidTransaction", t => _erp.VoidTransaction(reason, t)), out result, out failureReason, out posStatus);
                return;
            }

            try
            {
                lock (_gate)
                {
                    _bagsAsked = false;
                    if (_state == IfcPOSStatus.STATUS_TRANSACTION_ACTIVE && _basket != null)
                    {
                        _basket.Cancelled = true;
                        _basket.PaymentPhase = false;
                        _state = IfcPOSStatus.STATUS_OPERATOR_SIGNED_ON;
                    }
                }
                AdapterLog.Write("VoidTransaction reason=" + reason);
            }
            catch (Exception ex)
            {
                result = IfcResult.IFC_ERROR_OTHER;
                failureReason = Message("Ошибка адаптера: " + ex.Message);
                AdapterLog.Write("VoidTransaction error " + ex);
            }
            finally
            {
                posStatus = SafeStatus();
            }
        }

        public override void PreparePayment(
            IfcAuthenticationData authenticationData,
            out IList<IfcCurrencyDescriptor> foreignCurrencies,
            out IfcPOSStatus posStatus,
            out string result,
            out IfcMessage failureReason)
        {
            foreignCurrencies = null;
            failureReason = null;
            result = IfcResult.IFC_ERROR_OTHER;
            if (!_localPayments)
            {
                AskForBagsOnce("preparePayment");
                var erp = CallErp("PreparePayment", t => _erp.PreparePayment(t));
                Apply(erp, out result, out failureReason, out posStatus);
                foreignCurrencies = erp.ForeignCurrencies;
                return;
            }

            AskForBagsOnce("preparePayment");
            try
            {
                lock (_gate)
                {
                    if (_state != IfcPOSStatus.STATUS_TRANSACTION_ACTIVE || _basket == null || _basket.ItemCount == 0)
                        failureReason = Message("Нет товаров для оплаты");
                    else
                    {
                        _basket.PaymentPhase = true;
                        result = IfcResult.IFC_OK;
                    }
                }
                AdapterLog.Write("PreparePayment result=" + result);
            }
            catch (Exception ex)
            {
                result = IfcResult.IFC_ERROR_OTHER;
                failureReason = Message("Ошибка адаптера: " + ex.Message);
                AdapterLog.Write("PreparePayment error " + ex);
            }
            finally
            {
                posStatus = SafeStatus();
            }
        }

        public override void UnpreparePayment(
            IfcAuthenticationData authenticationData,
            out IfcPOSStatus posStatus,
            out string result,
            out IfcMessage failureReason)
        {
            failureReason = null;
            result = IfcResult.IFC_ERROR_OTHER;
            if (!_localPayments)
            {
                failureReason = Message("unpreparePayment is not available in BAF yet");
                posStatus = RefreshRemoteStatus();
                return;
            }

            try
            {
                lock (_gate)
                {
                    if (_state != IfcPOSStatus.STATUS_TRANSACTION_ACTIVE || _basket == null || _basket.Paid > 0)
                        failureReason = Message("Возврат к сканированию недоступен после начала оплаты");
                    else
                    {
                        _basket.PaymentPhase = false;
                        result = IfcResult.IFC_OK;
                    }
                }
                AdapterLog.Write("UnpreparePayment result=" + result);
            }
            catch (Exception ex)
            {
                result = IfcResult.IFC_ERROR_OTHER;
                failureReason = Message("Ошибка адаптера: " + ex.Message);
                AdapterLog.Write("UnpreparePayment error " + ex);
            }
            finally
            {
                posStatus = SafeStatus();
            }
        }

        public override void AddTender(
            IfcAuthenticationData authenticationData,
            IfcTenderInfo info,
            out IfcPOSStatus posStatus,
            out string result,
            out IfcErrorDetails errorDetails)
        {
            ApplyPayment("addTender", null, info == null ? null : info.TenderType, info == null ? 0 : info.Amount, out result, out errorDetails, out posStatus);
        }

        public override void PerformPayment(
            IfcAuthenticationData authenticationData,
            string tenderType,
            long amount,
            out IfcErrorDetails errorDetails,
            out IfcPOSStatus posStatus,
            out string result)
        {
            ApplyPayment("performPayment", null, tenderType, amount, out result, out errorDetails, out posStatus);
        }

        public override void PayWithCard(
            IfcAuthenticationData authenticationData,
            IfcCardInfo info,
            string tenderType,
            long amount,
            out IfcErrorDetails errorDetails,
            out IfcPOSStatus posStatus,
            out string result)
        {
            ApplyPayment("payWithCard", info, tenderType, amount, out result, out errorDetails, out posStatus);
        }

        public override void TransactionFinished(
            IfcAuthenticationData authenticationData,
            out int numberOfReceipts,
            out IfcPOSStatus posStatus,
            out string result,
            out IfcMessage failureReason)
        {
            numberOfReceipts = 0;
            failureReason = null;
            result = IfcResult.IFC_OK;
            if (!_localPayments)
            {
                _bagsAsked = false;
                var erp = CallErp("FinishTransaction", t => _erp.FinishTransaction(t));
                Apply(erp, out result, out failureReason, out posStatus);
                numberOfReceipts = erp.NumberOfReceipts;
                if (numberOfReceipts <= 0 && posStatus != null && posStatus.Transaction != null)
                    numberOfReceipts = posStatus.Transaction.NumberOfReceipts;
                return;
            }

            try
            {
                lock (_gate)
                {
                    _bagsAsked = false;
                    if (_state == IfcPOSStatus.STATUS_TRANSACTION_ACTIVE)
                        _state = IfcPOSStatus.STATUS_OPERATOR_SIGNED_ON;
                    _resolved.Clear();
                    AdapterLog.Write("TransactionFinished " + (_basket == null ? "-" : _basket.Id + " cancelled=" + _basket.Cancelled));
                }
            }
            catch (Exception ex)
            {
                AdapterLog.Write("TransactionFinished error " + ex);
            }
            finally
            {
                posStatus = SafeStatus();
            }
        }

        public override void GetStatus(IfcAuthenticationData authenticationData, out IfcPOSStatus posStatus, out string result)
        {
            if (!_localPayments)
                posStatus = RefreshRemoteStatus();
            else
                posStatus = SafeStatus();
            result = IfcResult.IFC_OK;
        }

        public override void CheckError(IfcAuthenticationData authenticationData, out IfcPOSStatus posStatus, out string result)
        {
            GetStatus(authenticationData, out posStatus, out result);
        }

        public override void GetGlobalProperties(
            IfcAuthenticationData authenticationData,
            out IfcGlobalProperties globalProperties,
            out string result,
            out IfcMessage failureReason)
        {
            failureReason = null;
            if (!_localPayments)
            {
                var erp = CallErp("GetGlobalProperties", t => _erp.GetGlobalProperties(t));
                result = string.IsNullOrEmpty(erp.Result) ? IfcResult.IFC_ERROR_OTHER : erp.Result;
                failureReason = erp.FailureReason;
                globalProperties = erp.GlobalProperties ?? new IfcGlobalProperties();
                return;
            }

            try
            {
                var languages = (ConfigurationManager.AppSettings["Languages"] ?? "sk,en")
                    .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .ToList();
                globalProperties = new IfcGlobalProperties
                {
                    StoreId = ConfigurationManager.AppSettings["StoreId"] ?? "",
                    CashRegisterId = ConfigurationManager.AppSettings["LaneId"] ?? "SCO01",
                    LocalCurrency = new IfcCurrencyDescriptor
                    {
                        CurrencyCode = ConfigurationManager.AppSettings["CurrencyCode"] ?? "EUR",
                        FractionDigits = 2,
                        ConversionFactor = 1.0
                    },
                    ForeignCurrencies = new List<IfcCurrencyDescriptor>(),
                    SupportedLanguages = languages,
                    AgeVerificationLimits = new List<int>(),
                    WeighingUnit = IfcGlobalProperties.WEIGHINGUNIT_KG
                };
                result = IfcResult.IFC_OK;
                AdapterLog.Write("GetGlobalProperties OK");
            }
            catch (Exception ex)
            {
                globalProperties = new IfcGlobalProperties();
                result = IfcResult.IFC_ERROR_OTHER;
                failureReason = Message("Ошибка адаптера: " + ex.Message);
                AdapterLog.Write("GetGlobalProperties error " + ex);
            }
        }

        public override void SignOn(
            IfcAuthenticationData authenticationData,
            string name,
            string password,
            bool trainingMode,
            out IfcPOSStatus posStatus,
            out string result,
            out IfcMessage failureReason)
        {
            failureReason = null;
            if (!_localPayments)
            {
                Apply(CallErp("SignOn", t => _erp.SignOn(name, password, trainingMode, t)), out result, out failureReason, out posStatus);
                return;
            }

            lock (_gate)
            {
                if (_state != IfcPOSStatus.STATUS_TRANSACTION_ACTIVE)
                    _state = IfcPOSStatus.STATUS_OPERATOR_SIGNED_ON;
                _trainingMode = trainingMode;
            }
            posStatus = SafeStatus();
            result = IfcResult.IFC_OK;
            AdapterLog.Write("SignOn name=" + name + " training=" + trainingMode);
        }

        public override void SignOff(
            IfcAuthenticationData authenticationData,
            out IfcPOSStatus posStatus,
            out string result,
            out IfcMessage failureReason)
        {
            failureReason = null;
            if (!_localPayments)
            {
                Apply(CallErp("SignOff", t => _erp.SignOff(t)), out result, out failureReason, out posStatus);
                return;
            }

            lock (_gate)
            {
                if (_basket != null && _state == IfcPOSStatus.STATUS_TRANSACTION_ACTIVE)
                    _basket.Cancelled = true;
                _state = IfcPOSStatus.STATUS_OPERATOR_SIGNED_OFF;
                _resolved.Clear();
            }
            posStatus = SafeStatus();
            result = IfcResult.IFC_OK;
            AdapterLog.Write("SignOff");
        }

        public override void StartTransaction(
            IfcAuthenticationData authenticationData,
            string language,
            bool salesReturn,
            out IfcPOSStatus posStatus,
            out string result,
            out IfcMessage failureReason)
        {
            failureReason = null;
            result = IfcResult.IFC_OK;
            if (!_localPayments)
            {
                _bagsAsked = false;
                Apply(CallErp("StartTransaction", t => _erp.StartTransaction(language, salesReturn, t)), out result, out failureReason, out posStatus);
                return;
            }

            lock (_gate)
            {
                _bagsAsked = false;
                if (_state == IfcPOSStatus.STATUS_OPERATOR_SIGNED_OFF)
                {
                    result = IfcResult.IFC_ERROR_OTHER;
                    failureReason = Message("Касса не открыта (signOn)");
                }
                else if (_state == IfcPOSStatus.STATUS_TRANSACTION_ACTIVE && _basket != null && _basket.ItemCount > 0)
                {
                    result = IfcResult.IFC_ERROR_OTHER;
                    failureReason = Message("Предыдущий чек не закрыт");
                }
                else
                {
                    EnsureTransaction(salesReturn);
                }
            }
            posStatus = SafeStatus();
            AdapterLog.Write("StartTransaction language=" + language + " salesReturn=" + salesReturn + " result=" + result);
        }

        public override void Cancel(IfcAuthenticationData authenticationData, out string result)
        {
            result = IfcResult.IFC_OK;
            try
            {
                var inflight = _inflight;
                if (inflight != null)
                    inflight.Cancel();
                AdapterLog.Write("Cancel" + (inflight == null ? " (нет активного запроса)" : ""));
            }
            catch (Exception ex)
            {
                AdapterLog.Write("Cancel error " + ex.Message);
            }
        }

        public override void ProcessCustomerCard(
            IfcAuthenticationData authenticationData,
            IfcCardInfo info,
            out IfcErrorDetails errorDetails,
            out IfcPOSStatus posStatus,
            out string result)
        {
            if (!_localPayments)
            {
                AskForBagsOnce("processCustomerCard");
                ApplyError(CallErp("ProcessCustomerCard", t => _erp.ProcessCustomerCard(info, t)), out result, out errorDetails, out posStatus);
                return;
            }
            Reject("processCustomerCard", out posStatus, out result, out errorDetails);
        }

        public override void Special(
            IfcAuthenticationData authenticationData,
            string function,
            IList<string> parameters,
            string selectedEntryId,
            out IfcPOSStatus posStatus,
            out string result,
            out IfcMessage failureReason)
        {
            if (!_localPayments)
            {
                Apply(CallErp("Special", t => _erp.Special(function, parameters, selectedEntryId, t)), out result, out failureReason, out posStatus);
                return;
            }
            Reject("special(" + function + ")", out posStatus, out result, out failureReason);
        }

        private ErpResult CallErp(string name, Func<CancellationToken, ErpResult> call)
        {
            var owns = false;
            var cts = _inflight;
            if (cts == null)
            {
                cts = new CancellationTokenSource();
                _inflight = cts;
                owns = true;
            }
            try
            {
                var erp = call(cts.Token) ?? ErpResult.Fail("Empty 1C response");
                if (erp.PosStatus != null)
                {
                    lock (_gate)
                        _remoteStatus = erp.PosStatus;
                }
                AdapterLog.Write(name + " result=" + erp.Result + (string.IsNullOrEmpty(erp.FailureText) ? "" : " " + erp.FailureText));
                return erp;
            }
            catch (Exception ex)
            {
                AdapterLog.Write(name + " error " + ex);
                return ErpResult.Fail("Adapter error: " + ex.Message);
            }
            finally
            {
                if (owns)
                {
                    Interlocked.CompareExchange(ref _inflight, null, cts);
                    cts.Dispose();
                }
            }
        }

        private void AskForBagsOnce(string reason)
        {
            if (_bagsAsked)
                return;
            if (!Flag("BagPromptEnabled", true))
                return;
            if (_posEvents == null)
            {
                AdapterLog.Write("Bags skipped: posEvents is not connected");
                return;
            }
            if (!HasItemsForBags())
                return;

            _bagsAsked = true;
            try
            {
                AdapterLog.Write("Bags prompt from " + reason);
                var quantity = AskBagQuantity();
                if (quantity <= 0)
                {
                    AdapterLog.Write("Bags skipped: quantity=" + quantity);
                    return;
                }

                var bags = ReadBagOptions();
                var chosen = AskBagChoice(bags);
                if (chosen == null)
                {
                    AdapterLog.Write("Bags skipped: no bag selected");
                    return;
                }
                if (string.IsNullOrWhiteSpace(chosen.Code))
                {
                    AdapterLog.Write("Bags GUI OK qty=" + quantity + " option=" + chosen.Option + " (BagNCode empty, item not added)");
                    return;
                }

                var info = new IfcItemInfo
                {
                    ItemCode = chosen.Code,
                    Quantity = quantity,
                    EntryMethod = "customer selected"
                };
                var erp = CallErp("AddItem(bag)", t => _erp.AddItem(info, t));
                AdapterLog.Write("Bags added qty=" + quantity + " code=" + chosen.Code + " result=" + erp.Result);
            }
            catch (Exception ex)
            {
                AdapterLog.Write("Bags prompt error " + ex);
            }
        }

        private bool HasItemsForBags()
        {
            lock (_gate)
            {
                if (_remoteStatus != null && _remoteStatus.Transaction != null)
                    return _remoteStatus.Transaction.ItemCount > 0;
                return _basket != null && _basket.ItemCount > 0;
            }
        }

        private int AskBagQuantity()
        {
            var descriptor = new IfcInputDescriptor
            {
                LayoutId = "BagQuantity",
                Mode = IfcInputDescriptor.DIGIT_SEQUENCE,
                Message = Message(Setting("BagQuantityMessage", "How many bags do you need?")),
                MinLength = 1,
                MaxLength = 2,
                CancelAllowed = true,
                KeyedInInput = true,
                ScannerInput = false,
                DefaultResponse = "1"
            };

            IfcAuthenticationData auth;
            string response;
            IfcMsrData msr;
            IfcRFIDData rfid;
            int scanDataType;
            string entryMethod;
            string result;
            _posEvents.CustomerInputRequest(descriptor, out auth, out response, out msr, out rfid, out scanDataType, out entryMethod, out result);
            AdapterLog.Write("BagQuantity result=" + result + " response=" + response);

            int quantity;
            if (string.IsNullOrWhiteSpace(response)
                || !int.TryParse(response.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out quantity))
                return 0;
            return quantity;
        }

        private BagOption AskBagChoice(IList<BagOption> bags)
        {
            var options = new List<IfcConfirmationOption>();
            foreach (var bag in bags.Take(4))
            {
                options.Add(new IfcConfirmationOption
                {
                    Option = bag.Option,
                    Label = Message(bag.Label)
                });
            }
            if (options.Count == 0)
                return null;

            var descriptor = new IfcConfirmationDescriptor
            {
                LayoutId = "ChooseBag",
                Message = Message(Setting("BagChooseMessage", "Choose a bag")),
                ConfirmationOptions = options,
                DefaultConfirmationOption = options[0].Option,
                CancelAllowed = true
            };

            IfcAuthenticationData auth;
            string response;
            string result;
            _posEvents.CustomerConfirmationRequest(descriptor, false, out auth, out response, out result);
            AdapterLog.Write("ChooseBag result=" + result + " response=" + response);
            if (string.IsNullOrWhiteSpace(response))
                return null;
            return bags.FirstOrDefault(b => string.Equals(b.Option, response, StringComparison.OrdinalIgnoreCase));
        }

        private static IList<BagOption> ReadBagOptions()
        {
            var bags = new List<BagOption>();
            for (var i = 1; i <= 4; i++)
            {
                var label = Setting("Bag" + i + "Label", "");
                var code = Setting("Bag" + i + "Code", "");
                if (string.IsNullOrWhiteSpace(label) && string.IsNullOrWhiteSpace(code))
                    continue;
                bags.Add(new BagOption
                {
                    Option = "bag" + i,
                    Label = string.IsNullOrWhiteSpace(label) ? ("Bag " + i) : label,
                    Code = code
                });
            }
            return bags;
        }

        private static string Setting(string key, string fallback)
        {
            var value = ConfigurationManager.AppSettings[key];
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static bool Flag(string key, bool fallback)
        {
            var value = ConfigurationManager.AppSettings[key];
            if (string.IsNullOrWhiteSpace(value))
                return fallback;
            return string.Equals(value.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        }

        private sealed class BagOption
        {
            public string Option;
            public string Label;
            public string Code;
        }

        private void Apply(ErpResult erp, out string result, out IfcMessage failureReason, out IfcPOSStatus posStatus)
        {
            result = string.IsNullOrEmpty(erp.Result) ? IfcResult.IFC_ERROR_OTHER : erp.Result;
            failureReason = erp.FailureReason;
            posStatus = erp.PosStatus ?? SafeStatus();
        }

        private void ApplyError(ErpResult erp, out string result, out IfcErrorDetails errorDetails, out IfcPOSStatus posStatus)
        {
            result = string.IsNullOrEmpty(erp.Result) ? IfcResult.IFC_ERROR_OTHER : erp.Result;
            errorDetails = ErpMapper.ErrorFrom(erp);
            posStatus = erp.PosStatus ?? SafeStatus();
        }

        private IfcPOSStatus RefreshRemoteStatus()
        {
            var erp = CallErp("GetStatus", t => _erp.GetStatus(t));
            if (erp.PosStatus != null)
                return erp.PosStatus;
            return ErrorStatus(erp.FailureText ?? "1C unavailable");
        }

        private static IfcPOSStatus ErrorStatus(string text)
        {
            return new IfcPOSStatus
            {
                Status = IfcPOSStatus.STATUS_ERROR,
                ErrorStatusReason = new IfcMessage { UntranslatableText = text }
            };
        }

        private ResolvedItem Resolve(string code, string entryMethod, string context, bool salesReturn, out string result, out string reason)
        {
            reason = null;
            var erp = CallErp("ResolveItem", t => _erp.ResolveItem(code, entryMethod, context, salesReturn, t));
            result = string.IsNullOrEmpty(erp.Result) ? IfcResult.IFC_ERROR_OTHER : erp.Result;
            if (result != IfcResult.IFC_OK)
            {
                reason = erp.FailureText;
                return null;
            }

            var details = erp.ItemDetails;
            if (details == null || details.ItemPrice <= 0 || string.IsNullOrEmpty(details.ItemDescription))
            {
                result = IfcResult.IFC_ERROR_OTHER;
                reason = "1C returned an item without price or description";
                return null;
            }

            var item = new ResolvedItem
            {
                ItemId = string.IsNullOrEmpty(details.ItemId) ? code : details.ItemId,
                ItemCode = code,
                Description = details.ItemDescription,
                UnitPrice = details.ItemPrice
            };
            lock (_gate)
            {
                _resolved[item.ItemId] = item;
                if (!string.IsNullOrEmpty(code))
                    _resolved[code] = item;
            }
            return item;
        }

        private ResolvedItem FindResolved(string key)
        {
            if (string.IsNullOrEmpty(key))
                return null;
            lock (_gate)
            {
                ResolvedItem item;
                return _resolved.TryGetValue(key, out item) ? item : null;
            }
        }

        private void ApplyPayment(string method, IfcCardInfo info, string tenderType, long amount, out string result, out IfcErrorDetails errorDetails, out IfcPOSStatus posStatus)
        {
            errorDetails = null;
            result = IfcResult.IFC_ERROR_OTHER;
            posStatus = SafeStatus();
            try
            {
                if (!_localPayments)
                {
                    if (IsCashTender(tenderType))
                    {
                        errorDetails = Error("Only card payment is supported");
                        posStatus = RefreshRemoteStatus();
                        return;
                    }
                    ApplyError(CallErp(method, t => _erp.PayWithCard(info, tenderType, amount, t)), out result, out errorDetails, out posStatus);
                    return;
                }

                lock (_gate)
                {
                    if (_state != IfcPOSStatus.STATUS_TRANSACTION_ACTIVE || _basket == null || _basket.ItemCount == 0)
                    {
                        errorDetails = Error("Нет активного чека для оплаты");
                        return;
                    }

                    var value = amount > 0 ? amount : _basket.Remaining;
                    _basket.AddTender(string.IsNullOrEmpty(tenderType) ? "creditcard" : tenderType, value);
                    if (_basket.Remaining == 0)
                    {
                        _basket.PaymentPhase = false;
                        _state = IfcPOSStatus.STATUS_OPERATOR_SIGNED_ON;
                    }
                    result = IfcResult.IFC_OK;
                    AdapterLog.Write(method + " " + tenderType + " " + value + " (мок) remaining=" + _basket.Remaining + " change=" + _basket.Change);
                }
            }
            catch (Exception ex)
            {
                result = IfcResult.IFC_ERROR_OTHER;
                errorDetails = Error("Ошибка адаптера: " + ex.Message);
                AdapterLog.Write(method + " error " + ex);
            }
            finally
            {
                if (_localPayments)
                    posStatus = SafeStatus();
            }
        }

        private static bool IsCashTender(string tenderType)
        {
            return string.Equals(tenderType, "cash", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tenderType, "cashnotes", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tenderType, "cashcoins", StringComparison.OrdinalIgnoreCase);
        }

        private bool CanAddItem()
        {
            return (_state == IfcPOSStatus.STATUS_OPERATOR_SIGNED_ON || _state == IfcPOSStatus.STATUS_TRANSACTION_ACTIVE)
                && !(_basket != null && _state == IfcPOSStatus.STATUS_TRANSACTION_ACTIVE && _basket.PaymentPhase);
        }

        private void EnsureTransaction(bool salesReturn)
        {
            if (_state == IfcPOSStatus.STATUS_TRANSACTION_ACTIVE && _basket != null)
                return;
            _transactionCounter++;
            _basket = new Basket(DateTime.Now.ToString("yyyyMMddHHmmss") + "-" + _transactionCounter, salesReturn);
            _state = IfcPOSStatus.STATUS_TRANSACTION_ACTIVE;
        }

        private IfcPOSStatus SafeStatus()
        {
            try
            {
                lock (_gate)
                {
                    if (!_localPayments)
                        return _remoteStatus ?? new IfcPOSStatus { Status = IfcPOSStatus.STATUS_OPERATOR_SIGNED_OFF };
                    return BuildStatus();
                }
            }
            catch (Exception ex)
            {
                AdapterLog.Write("BuildStatus error " + ex);
                return new IfcPOSStatus { Status = IfcPOSStatus.STATUS_OPERATOR_SIGNED_OFF };
            }
        }

        private IfcPOSStatus BuildStatus()
        {
            var active = _state == IfcPOSStatus.STATUS_TRANSACTION_ACTIVE && _basket != null;
            var paying = active && _basket.PaymentPhase;
            return new IfcPOSStatus
            {
                Status = _state,
                Transaction = _basket == null ? null : _basket.ToIfc(),
                TrainingMode = _trainingMode,
                AddItemAllowed = CanAddItem(),
                VoidTransactionAllowed = active,
                PreparePaymentAllowed = active && !paying && _basket.ItemCount > 0,
                UnpreparePaymentAllowed = paying && _basket.Paid == 0,
                TenderDescriptors = TenderDescriptors()
            };
        }

        private static IList<IfcTenderDescriptor> TenderDescriptors()
        {
            return new List<IfcTenderDescriptor>
            {
                new IfcTenderDescriptor { TenderType = "creditcard", OverpayAllowed = false, UnderpayAllowed = false },
                new IfcTenderDescriptor { TenderType = "debitcard", OverpayAllowed = false, UnderpayAllowed = false }
            };
        }

        private static IfcMessage Message(string text)
        {
            return new IfcMessage { UntranslatableText = text };
        }

        private static IfcErrorDetails Error(string text)
        {
            return new IfcErrorDetails { CustomerInfo = Message(text), AttendantInfo = Message(text) };
        }

        private sealed class ResolvedItem
        {
            public string ItemId;
            public string ItemCode;
            public string Description;
            public long UnitPrice;
        }

        private sealed class Basket
        {
            private readonly List<IfcItemEntry> _items = new List<IfcItemEntry>();
            private readonly List<IfcTenderEntry> _tenders = new List<IfcTenderEntry>();
            private int _seq;

            public Basket(string id, bool salesReturn)
            {
                Id = id;
                SalesReturn = salesReturn;
            }

            public string Id { get; }
            public bool SalesReturn { get; }
            public bool Cancelled { get; set; }
            public bool PaymentPhase { get; set; }

            public int ItemCount
            {
                get { return _items.Sum(i => i.ItemQuantity); }
            }

            public long Total
            {
                get { return _items.Sum(i => i.ItemPrice); }
            }

            public long Paid
            {
                get { return _tenders.Sum(t => t.CurrencyAmount.Amount); }
            }

            public long Remaining
            {
                get { return Math.Max(0, Total - Paid); }
            }

            public long Change
            {
                get { return Math.Max(0, Paid - Total); }
            }

            public void AddItem(string itemId, ResolvedItem item, int quantity, long linePrice)
            {
                _seq++;
                _items.Add(new IfcItemEntry
                {
                    SeqNr = _seq,
                    EntryId = "I" + _seq,
                    IsVoidable = true,
                    ItemId = itemId,
                    ItemCode = item.ItemCode,
                    ItemDescription = item.Description,
                    ItemQuantity = quantity,
                    ItemPrice = linePrice,
                    ReferencePricePerUnit = item.UnitPrice
                });
            }

            public bool VoidItem(string entryId)
            {
                return _items.RemoveAll(i => i.EntryId == entryId) > 0;
            }

            public void AddTender(string tenderType, long amount)
            {
                _seq++;
                _tenders.Add(new IfcTenderEntry
                {
                    SeqNr = _seq,
                    EntryId = "T" + _seq,
                    IsVoidable = false,
                    TenderDescription = tenderType,
                    CurrencyAmount = new IfcCurrencyAmount
                    {
                        Amount = amount,
                        CurrencyCode = ConfigurationManager.AppSettings["CurrencyCode"] ?? "EUR"
                    }
                });
            }

            public IfcTransaction ToIfc()
            {
                return new IfcTransaction
                {
                    TransactionId = Id,
                    Cancelled = Cancelled,
                    ItemEntries = _items.Select(i => (IfcItemEntry)i.Clone()).ToList(),
                    ItemCount = ItemCount,
                    TotalWithTax = Total,
                    TotalWithoutTax = Total,
                    RemainingAmount = Remaining,
                    Change = Change,
                    TenderEntries = _tenders.Select(t => (IfcTenderEntry)t.Clone()).ToList(),
                    NumberOfReceipts = 0
                };
            }
        }
    }
}
