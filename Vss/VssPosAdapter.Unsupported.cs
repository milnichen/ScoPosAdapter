using System.Collections.Generic;
using WN.Retail.iScan.POSAdapterLibrary.DataObjects;

namespace ScoPosAdapter.Vss
{
    /// <summary>
    /// Методы Interface 4.0, которые пилот не поддерживает. Базовый класс бросает NotImplementedException,
    /// что рвёт канал main, поэтому каждый метод отвечает явно.
    /// Уведомления (журнал, кассовые операции, аномалии, возраст) подтверждаются IFC_OK, остальное — IFC_ERROR_OTHER.
    /// </summary>
    internal sealed partial class VssPosAdapter
    {
        public override void AddBlanketDiscount(IfcAuthenticationData authenticationData, IfcBlanketDiscountInfo info, out IfcPOSStatus posStatus, out string result, out IfcMessage failureReason)
        {
            Reject("addBlanketDiscount", out posStatus, out result, out failureReason);
        }

        public override void AddCoupon(IfcAuthenticationData authenticationData, IfcCouponInfo info, out string suggestedHandling, out IfcErrorDetails errorDetails, out IfcPOSStatus posStatus, out string result)
        {
            suggestedHandling = null;
            Reject("addCoupon", out posStatus, out result, out errorDetails);
        }

        public override void AddEtopup(IfcAuthenticationData authenticationData, IfcCardInfo info, string etopupId, out IfcPOSStatus posStatus, out string result, out IfcErrorDetails errorDetails)
        {
            Reject("addEtopup", out posStatus, out result, out errorDetails);
        }

        public override void AddItemByDepartment(IfcAuthenticationData authenticationData, IfcDepartmentInfo info, out IfcPOSStatus posStatus, out string result, out IfcMessage failureReason)
        {
            Reject("addItemByDepartment", out posStatus, out result, out failureReason);
        }

        public override void AddItemList(IfcAuthenticationData authenticationData, IList<IfcItemInfo> infos, out IfcPOSStatus posStatus, out string result, out IfcMessage failureReason)
        {
            Reject("addItemList", out posStatus, out result, out failureReason);
        }

        public override void AddParticularDiscount(IfcAuthenticationData authenticationData, IfcParticularDiscountInfo info, out IfcPOSStatus posStatus, out string result, out IfcMessage failureReason)
        {
            Reject("addParticularDiscount", out posStatus, out result, out failureReason);
        }

        public override void AddRefund(IfcAuthenticationData authenticationData, IfcRefundInfo info, out string suggestedHandling, out IfcErrorDetails errorDetails, out IfcPOSStatus posStatus, out string result)
        {
            suggestedHandling = null;
            Reject("addRefund", out posStatus, out result, out errorDetails);
        }

        public override void AddSimpleCoupon(IfcAuthenticationData authenticationData, string couponId, int scanDataType, string entryMethod, out string suggestedHandling, out IfcErrorDetails errorDetails, out IfcPOSStatus posStatus, out string result)
        {
            suggestedHandling = null;
            Reject("addSimpleCoupon", out posStatus, out result, out errorDetails);
        }

        public override void AddSimpleRefund(IfcAuthenticationData authenticationData, string refundId, int scanDataType, string entryMethod, out string suggestedHandling, out IfcErrorDetails errorDetails, out IfcPOSStatus posStatus, out string result)
        {
            suggestedHandling = null;
            Reject("addSimpleRefund", out posStatus, out result, out errorDetails);
        }

        public override void AddSimpleVoucher(IfcAuthenticationData authenticationData, string voucherId, int scanDataType, string entryMethod, out string suggestedHandling, out IfcErrorDetails errorDetails, out IfcPOSStatus posStatus, out string result)
        {
            suggestedHandling = null;
            Reject("addSimpleVoucher", out posStatus, out result, out errorDetails);
        }

        public override void AddVoucher(IfcAuthenticationData authenticationData, IfcVoucherInfo info, out string suggestedHandling, out IfcErrorDetails errorDetails, out IfcPOSStatus posStatus, out string result)
        {
            suggestedHandling = null;
            Reject("addVoucher", out posStatus, out result, out errorDetails);
        }

        public override void BalanceChanged(IfcAuthenticationData authenticationData, IfcCurrencyAmount localCurrencyAmount, IList<IfcCurrencyAmount> foreignCurrencyAmounts, IList<IfcCashBalanceInfo> balanceInfos, out IfcPOSStatus posStatus, out string result, out IfcMessage failureReason)
        {
            Acknowledge("balanceChanged", out posStatus, out result, out failureReason);
        }

        public override void ComputePrice(IfcAuthenticationData authenticationData, IfcComputePriceInfo info, out long finalPrice, out string itemAmount, out string result, out bool posStatusChanged, out IfcMessage failureReason)
        {
            finalPrice = 0;
            itemAmount = null;
            posStatusChanged = false;
            result = IfcResult.IFC_ERROR_OTHER;
            failureReason = NotSupported("computePrice");
        }

        public override void CurrentBalance(IfcAuthenticationData authenticationData, IList<IfcCashBalanceInfo> balanceInfos, string context, out IfcPOSStatus posStatus, out string result, out IfcMessage failureReason)
        {
            Acknowledge("currentBalance", out posStatus, out result, out failureReason);
        }

        public override void DirectMode(IfcAuthenticationData authenticationData, string context, out IfcPOSStatus posStatus, out string result, out IfcMessage failureReason)
        {
            Reject("directMode", out posStatus, out result, out failureReason);
        }

        public override void FinishQsrComposition(IfcAuthenticationData authenticationData, IfcQsrComposition composition, string entryId, string mode, out IfcPOSStatus posStatus, out string result, out IfcErrorDetails errorDetails)
        {
            Reject("finishQsrComposition", out posStatus, out result, out errorDetails);
        }

        public override void GetCrateInfo(IfcAuthenticationData authenticationData, string itemCode, string codeType, int scanDataType, out IList<IfcCrateDescriptor> crateDescriptors, out string result, out IfcMessage failureReason, out bool posStatusChanged)
        {
            crateDescriptors = null;
            posStatusChanged = false;
            result = IfcResult.IFC_ERROR_OTHER;
            failureReason = NotSupported("getCrateInfo");
        }

        public override void GetEtopupDetails(IfcAuthenticationData authenticationData, IfcCardInfo info, out IList<IfcEtopupDescriptor> etopupDescriptors, out string providerDescription, out string result, out IfcErrorDetails errorDetails, out bool posStatusChanged)
        {
            etopupDescriptors = null;
            providerDescription = null;
            posStatusChanged = false;
            result = IfcResult.IFC_ERROR_OTHER;
            errorDetails = NotSupportedDetails("getEtopupDetails");
        }

        public override void GetPicture(IfcAuthenticationData authenticationData, string access, out byte[] data, out string result, out IfcMessage failureReason)
        {
            data = null;
            result = IfcResult.IFC_ERROR_OTHER;
            failureReason = NotSupported("getPicture");
        }

        public override void GetQsrDescription(IfcAuthenticationData authenticationData, string entryId, string itemId, string itemCode, out IfcQsrDescription qsrDescription, out IfcQsrComposition validComposition, out IList<IfcQsrComposition> quickSellCombos, out string result, out IfcErrorDetails errorDetails)
        {
            qsrDescription = null;
            validComposition = null;
            quickSellCombos = null;
            result = IfcResult.IFC_ERROR_OTHER;
            errorDetails = NotSupportedDetails("getQsrDescription");
        }

        public override void HandleAnomaly(IfcAuthenticationData authenticationData, string id, string description, out IfcPOSStatus posStatus, out string result, out IfcMessage failureReason)
        {
            AdapterLog.Write("VSS anomaly id=" + id + " " + description);
            Acknowledge("handleAnomaly", out posStatus, out result, out failureReason);
        }

        public override void JournalData(IfcAuthenticationData authenticationData, IList<IfcJournalInfo> infos, out string result)
        {
            result = IfcResult.IFC_OK;
        }

        public override void Loan(IfcAuthenticationData authenticationData, IList<IfcPickupLoanInfo> loanInfos, IList<IfcPickupLoanInfo> newBalanceInfos, out IfcPOSStatus posStatus, out string result, out IfcMessage failureReason)
        {
            Acknowledge("loan", out posStatus, out result, out failureReason);
        }

        public override void PerformCustomerCard(IfcAuthenticationData authenticationData, string customerCardType, out IfcErrorDetails errorDetails, out IfcPOSStatus posStatus, out string result)
        {
            Reject("performCustomerCard", out posStatus, out result, out errorDetails);
        }

        public override void Pickup(IfcAuthenticationData authenticationData, IList<IfcPickupLoanInfo> pickupInfos, IList<IfcPickupLoanInfo> newBalanceInfos, out IfcPOSStatus posStatus, out string result, out IfcMessage failureReason)
        {
            Acknowledge("pickup", out posStatus, out result, out failureReason);
        }

        public override void PriceChange(IfcAuthenticationData authenticationData, IfcPriceChangeInfo info, out IfcPOSStatus posStatus, out string result, out IfcMessage failureReason)
        {
            Reject("priceChange", out posStatus, out result, out failureReason);
        }

        public override void Print(IfcAuthenticationData authenticationData, string documentId, IList<IfcLine> lines, out string result, out IfcMessage failureReason)
        {
            result = IfcResult.IFC_ERROR_OTHER;
            failureReason = NotSupported("print");
        }

        public override void RecallTransaction(IfcAuthenticationData authenticationData, IfcTransactionInfo info, out IfcErrorDetails errorDetails, out IfcPOSStatus posStatus, out string result)
        {
            Reject("recallTransaction", out posStatus, out result, out errorDetails);
        }

        public override void StoreTransaction(IfcAuthenticationData authenticationData, IfcVerifiedAge confirmedCustomerAge, out IfcPOSStatus posStatus, out string result, out IfcMessage failureReason)
        {
            Reject("storeTransaction", out posStatus, out result, out failureReason);
        }

        public override void TillReport(IfcAuthenticationData authenticationData, out IfcPOSStatus posStatus, out string result, out IfcMessage failureReason)
        {
            Reject("tillReport", out posStatus, out result, out failureReason);
        }

        public override void UnregisterCustomer(IfcAuthenticationData authenticationData, out IfcErrorDetails errorDetails, out IfcPOSStatus posStatus, out string result)
        {
            Reject("unregisterCustomer", out posStatus, out result, out errorDetails);
        }

        public override void UpdateQsrComposition(IfcAuthenticationData authenticationData, IfcQsrComposition composition, out IfcQsrUpdateResult qsrUpdateResult, out string result, out IfcErrorDetails errorDetails)
        {
            qsrUpdateResult = null;
            result = IfcResult.IFC_ERROR_OTHER;
            errorDetails = NotSupportedDetails("updateQsrComposition");
        }

        public override void VerifiedCustomerAge(IfcAuthenticationData authenticationData, IfcVerifiedAge confirmedCustomerAge, out IfcPOSStatus posStatus, out string result, out IfcMessage failureReason)
        {
            Acknowledge("verifiedCustomerAge", out posStatus, out result, out failureReason);
        }

        public override void VoidTransactionById(IfcAuthenticationData authenticationData, IfcTransactionInfo info, string reason, out long payOutAmount, out IfcPOSStatus posStatus, out string result, out IfcMessage failureReason)
        {
            payOutAmount = 0;
            Reject("voidTransactionById", out posStatus, out result, out failureReason);
        }

        public override void WeighItem(IfcAuthenticationData authenticationData, IfcWeighItemInfo info, out IfcWeighedItem weighedItem, out IfcPOSStatus posStatus, out string result, out IfcMessage failureReason)
        {
            weighedItem = null;
            Reject("weighItem", out posStatus, out result, out failureReason);
        }

        public override void ValidateUser(string user, string password, string scanCode, IfcMsrData msrData, IfcRFIDData rfidData, int scanDataType, string entryMethod, out string userDesc, out IList<string> userGroups, out string result, out IfcMessage failureReason)
        {
            userDesc = null;
            userGroups = null;
            result = IfcResult.IFC_ERROR_OTHER;
            failureReason = NotSupported("validateUser");
        }

        private void Reject(string method, out IfcPOSStatus posStatus, out string result, out IfcMessage failureReason)
        {
            failureReason = NotSupported(method);
            posStatus = SafeStatus();
            result = IfcResult.IFC_ERROR_OTHER;
        }

        private void Reject(string method, out IfcPOSStatus posStatus, out string result, out IfcErrorDetails errorDetails)
        {
            errorDetails = NotSupportedDetails(method);
            posStatus = SafeStatus();
            result = IfcResult.IFC_ERROR_OTHER;
        }

        private void Acknowledge(string method, out IfcPOSStatus posStatus, out string result, out IfcMessage failureReason)
        {
            AdapterLog.Write("VSS " + method + ": принято без обработки");
            failureReason = null;
            posStatus = SafeStatus();
            result = IfcResult.IFC_OK;
        }

        private static IfcMessage NotSupported(string method)
        {
            AdapterLog.Write("VSS " + method + ": не поддерживается");
            return Message("Функция не поддерживается: " + method);
        }

        private static IfcErrorDetails NotSupportedDetails(string method)
        {
            AdapterLog.Write("VSS " + method + ": не поддерживается");
            return Error("Функция не поддерживается: " + method);
        }
    }
}
