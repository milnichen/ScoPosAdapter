using System;
using System.Collections.Generic;
using System.Threading;
using WN.Retail.iScan.POSAdapterLibrary.DataObjects;

namespace ScoPosAdapter.Erp
{
    internal sealed class MockErpClient : IErpClient
    {
        public ErpResult GetStatus(CancellationToken cancellation)
        {
            throw new NotSupportedException();
        }

        public ErpResult GetGlobalProperties(CancellationToken cancellation)
        {
            throw new NotSupportedException();
        }

        public ErpResult SignOn(string name, string password, bool trainingMode, CancellationToken cancellation)
        {
            throw new NotSupportedException();
        }

        public ErpResult SignOff(CancellationToken cancellation)
        {
            throw new NotSupportedException();
        }

        public ErpResult ResolveItem(string itemCode, string entryMethod, string context, bool salesReturn, CancellationToken cancellation)
        {
            var code = itemCode ?? "";
            if (code == "8584000123456" || code.Length > 0 && code.StartsWith("TEST", StringComparison.OrdinalIgnoreCase))
            {
                return new ErpResult
                {
                    Result = IfcResult.IFC_OK,
                    ItemDetails = new IfcItemDetails
                    {
                        ItemId = "00-00001234",
                        ItemDescription = "Creme 50 ml",
                        ItemPrice = 1299
                    }
                };
            }

            return new ErpResult { Result = IfcResult.IFC_ERROR_ITEM_UNKNOWN };
        }

        public ErpResult StartTransaction(string language, bool salesReturn, CancellationToken cancellation)
        {
            throw new NotSupportedException();
        }

        public ErpResult AddItem(IfcItemInfo info, CancellationToken cancellation)
        {
            throw new NotSupportedException();
        }

        public ErpResult VoidEntry(string entryId, CancellationToken cancellation)
        {
            throw new NotSupportedException();
        }

        public ErpResult ProcessCustomerCard(IfcCardInfo info, CancellationToken cancellation)
        {
            throw new NotSupportedException();
        }

        public ErpResult PreparePayment(CancellationToken cancellation)
        {
            throw new NotSupportedException();
        }

        public ErpResult PayWithCard(IfcCardInfo info, string tenderType, long amount, CancellationToken cancellation)
        {
            throw new NotSupportedException();
        }

        public ErpResult Special(string function, IList<string> parameters, string selectedEntryId, CancellationToken cancellation)
        {
            throw new NotSupportedException();
        }

        public ErpResult VoidTransaction(string reason, CancellationToken cancellation)
        {
            throw new NotSupportedException();
        }

        public ErpResult FinishTransaction(CancellationToken cancellation)
        {
            throw new NotSupportedException();
        }
    }
}
