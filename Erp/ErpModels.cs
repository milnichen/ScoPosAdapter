using System.Collections.Generic;
using System.Threading;
using WN.Retail.iScan.POSAdapterLibrary.DataObjects;

namespace ScoPosAdapter.Erp
{
    internal sealed class ErpResult
    {
        public string Result { get; set; }
        public string FailureText { get; set; }
        public IfcMessage FailureReason { get; set; }
        public IfcErrorDetails ErrorDetails { get; set; }
        public IfcPOSStatus PosStatus { get; set; }
        public IfcItemDetails ItemDetails { get; set; }
        public IfcGlobalProperties GlobalProperties { get; set; }
        public IList<IfcCurrencyDescriptor> ForeignCurrencies { get; set; }
        public bool PosStatusChanged { get; set; }
        public int NumberOfReceipts { get; set; }

        public static ErpResult Fail(string text)
        {
            return new ErpResult
            {
                Result = IfcResult.IFC_ERROR_OTHER,
                FailureText = text,
                FailureReason = new IfcMessage { UntranslatableText = text }
            };
        }
    }

    internal interface IErpClient
    {
        ErpResult GetStatus(CancellationToken cancellation);
        ErpResult GetGlobalProperties(CancellationToken cancellation);
        ErpResult SignOn(string name, string password, bool trainingMode, CancellationToken cancellation);
        ErpResult SignOff(CancellationToken cancellation);
        ErpResult ResolveItem(string itemCode, string entryMethod, string context, bool salesReturn, CancellationToken cancellation);
        ErpResult StartTransaction(string language, bool salesReturn, CancellationToken cancellation);
        ErpResult AddItem(IfcItemInfo info, CancellationToken cancellation);
        ErpResult VoidEntry(string entryId, CancellationToken cancellation);
        ErpResult ProcessCustomerCard(IfcCardInfo info, CancellationToken cancellation);
        ErpResult PreparePayment(CancellationToken cancellation);
        ErpResult PayWithCard(IfcCardInfo info, string tenderType, long amount, CancellationToken cancellation);
        ErpResult Special(string function, IList<string> parameters, string selectedEntryId, CancellationToken cancellation);
        ErpResult VoidTransaction(string reason, CancellationToken cancellation);
        ErpResult FinishTransaction(CancellationToken cancellation);
    }
}
