using System;
using System.Collections;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using WN.Retail.iScan.POSAdapterLibrary.DataObjects;

namespace ScoPosAdapter.Erp
{
    internal static class ErpMapper
    {
        private static readonly JavaScriptSerializer Serializer = CreateSerializer();

        private static JavaScriptSerializer CreateSerializer()
        {
            var serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;
            return serializer;
        }

        public static string ToJson(object value)
        {
            return Serializer.Serialize(value ?? new Dictionary<string, object>());
        }

        public static IDictionary<string, object> FromJsonObject(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new Dictionary<string, object>();
            var raw = Serializer.DeserializeObject(json);
            return AsMap(raw) ?? new Dictionary<string, object>();
        }

        public static ErpResult ToResult(IDictionary<string, object> map)
        {
            var result = new ErpResult
            {
                Result = NormalizeResult(Str(map, "result")),
                PosStatusChanged = Bool(map, "posStatusChanged"),
                FailureReason = Message(Get(map, "failureReason")),
                ErrorDetails = ErrorDetails(Get(map, "errorDetails")),
                PosStatus = PosStatus(AsMap(Get(map, "posStatus"))),
                ItemDetails = ItemDetails(AsMap(Get(map, "itemDetails"))),
                GlobalProperties = GlobalProperties(AsMap(Get(map, "globalProperties"))),
                ForeignCurrencies = CurrencyList(Get(map, "foreignCurrencies")),
                NumberOfReceipts = (int)Long(map, "numberOfReceipts")
            };
            if (result.FailureReason != null)
                result.FailureText = result.FailureReason.UntranslatableText;
            if (string.IsNullOrEmpty(result.Result))
                result.Result = IfcResult.IFC_ERROR_OTHER;
            return result;
        }

        public static string NormalizeResult(string result)
        {
            if (string.Equals(result, "IFC_TRY_ALTERNATIVE", StringComparison.OrdinalIgnoreCase))
                return IfcResult.IFC_ERROR_TRY_ALTERNATIVE;
            return result;
        }

        public static IfcMessage Message(object raw)
        {
            var map = AsMap(raw);
            if (map != null)
            {
                var text = Str(map, "untranslatableText");
                return string.IsNullOrEmpty(text) ? null : new IfcMessage { UntranslatableText = text };
            }
            var plain = raw as string;
            return string.IsNullOrEmpty(plain) ? null : new IfcMessage { UntranslatableText = plain };
        }

        public static IfcErrorDetails ErrorDetails(object raw)
        {
            var map = AsMap(raw);
            if (map == null)
                return null;
            return new IfcErrorDetails
            {
                CustomerInfo = Message(Get(map, "customerInfo")),
                AttendantInfo = Message(Get(map, "attendantInfo"))
            };
        }

        public static IfcErrorDetails ErrorFrom(ErpResult result)
        {
            if (result.ErrorDetails != null)
                return result.ErrorDetails;
            var text = result.FailureText;
            if (string.IsNullOrEmpty(text) && result.FailureReason != null)
                text = result.FailureReason.UntranslatableText;
            if (string.IsNullOrEmpty(text))
                return null;
            var message = new IfcMessage { UntranslatableText = text };
            return new IfcErrorDetails { CustomerInfo = message, AttendantInfo = message };
        }

        private static IfcPOSStatus PosStatus(IDictionary<string, object> map)
        {
            if (map == null)
                return null;
            return new IfcPOSStatus
            {
                Status = Str(map, "status"),
                Transaction = Transaction(AsMap(Get(map, "transaction"))),
                ErrorStatusReason = Message(Get(map, "errorStatusReason")),
                Info1 = Message(Get(map, "info1")),
                Info2 = Message(Get(map, "info2")),
                Info3 = Message(Get(map, "info3")),
                Info4 = Message(Get(map, "info4")),
                VoidTransactionAllowed = Bool(map, "voidTransactionAllowed"),
                AddItemAllowed = Bool(map, "addItemAllowed"),
                AddItemByDepartmentAllowed = Bool(map, "addItemByDepartmentAllowed"),
                AddEtopupAllowed = Bool(map, "addEtopupAllowed"),
                PreparePaymentAllowed = Bool(map, "preparePaymentAllowed"),
                UnpreparePaymentAllowed = Bool(map, "unpreparePaymentAllowed"),
                CustomerCardAllowed = Bool(map, "customerCardAllowed"),
                AddCouponAllowed = Bool(map, "addCouponAllowed"),
                AddVoucherAllowed = Bool(map, "addVoucherAllowed"),
                AddRefundAllowed = Bool(map, "addRefundAllowed"),
                StoreTransactionAllowed = Bool(map, "storeTransactionAllowed"),
                LoanAllowed = Bool(map, "loanAllowed"),
                PickupAllowed = Bool(map, "pickupAllowed"),
                BalanceChangedAllowed = Bool(map, "balanceChangedAllowed"),
                TillReportAllowed = Bool(map, "tillReportAllowed"),
                DirectModeAllowed = Bool(map, "directModeAllowed"),
                TenderDescriptors = TenderDescriptors(Get(map, "tenderDescriptors")),
                AllowedBlanketDiscounts = StringList(Get(map, "allowedBlanketDiscounts")),
                PrintableDocumentIds = StringList(Get(map, "printableDocumentIds")),
                Offline = Bool(map, "offline"),
                TrainingMode = Bool(map, "trainingMode"),
                Customer = Customer(AsMap(Get(map, "customer"))),
                EnabledSpecialFunctions = StringList(Get(map, "enabledSpecialFunctions"))
            };
        }

        private static IfcTransaction Transaction(IDictionary<string, object> map)
        {
            if (map == null)
                return null;
            return new IfcTransaction
            {
                TransactionId = Str(map, "transactionId"),
                Cancelled = Bool(map, "cancelled"),
                ItemEntries = MapList(Get(map, "itemEntries"), ItemEntry),
                ItemCount = (int)Long(map, "itemCount"),
                TotalWithTax = Long(map, "totalWithTax"),
                TotalWithoutTax = Long(map, "totalWithoutTax"),
                ExtraTotal = Long(map, "extraTotal"),
                PriceReductionsTotal = Long(map, "priceReductionsTotal"),
                RemainingAmount = Long(map, "remainingAmount"),
                Change = Long(map, "change"),
                Tax = Long(map, "tax"),
                TenderEntries = MapList(Get(map, "tenderEntries"), TenderEntry),
                NumberOfReceipts = (int)Long(map, "numberOfReceipts"),
                SelectedEntryIds = StringList(Get(map, "selectedEntryIds"))
            };
        }

        private static IfcItemEntry ItemEntry(IDictionary<string, object> map)
        {
            return new IfcItemEntry
            {
                SeqNr = (int)Long(map, "seqNr"),
                EntryId = Str(map, "entryId"),
                IsVoidable = Bool(map, "isVoidable"),
                ItemId = Str(map, "itemId"),
                ParentEntryId = Str(map, "parentEntryId"),
                OriginatingItemId = Str(map, "originatingItemId"),
                ItemCode = Str(map, "itemCode"),
                ItemDescription = Str(map, "itemDescription"),
                ItemQuantity = (int)Long(map, "itemQuantity"),
                ItemPrice = Long(map, "itemPrice"),
                ItemAmount = Str(map, "itemAmount"),
                OptionalAmountInfo = Str(map, "optionalAmountInfo"),
                ReferencePricePerUnit = Long(map, "referencePricePerUnit"),
                ReferenceUnit = Str(map, "referenceUnit"),
                PriceReductions = MapList(Get(map, "priceReductions"), PriceReduction),
                Marks = StringList(Get(map, "marks"))
            };
        }

        private static IfcPriceReduction PriceReduction(IDictionary<string, object> map)
        {
            return new IfcPriceReduction
            {
                Id = Str(map, "id"),
                Amount = Long(map, "amount"),
                PointsEarned = (int)Long(map, "pointsEarned"),
                PromotionText = Str(map, "promotionText")
            };
        }

        private static IfcTenderEntry TenderEntry(IDictionary<string, object> map)
        {
            return new IfcTenderEntry
            {
                SeqNr = (int)Long(map, "seqNr"),
                EntryId = Str(map, "entryId"),
                IsVoidable = Bool(map, "isVoidable"),
                TenderDescription = FirstNonEmpty(Str(map, "tenderDescription"), Str(map, "tenderType")),
                CurrencyAmount = CurrencyAmount(Get(map, "currencyAmount"), Str(map, "currencyCode"))
            };
        }

        private static IfcCurrencyAmount CurrencyAmount(object raw, string fallbackCode)
        {
            var map = AsMap(raw);
            if (map != null)
            {
                return new IfcCurrencyAmount
                {
                    Amount = Long(map, "amount"),
                    CurrencyCode = FirstNonEmpty(Str(map, "currencyCode"), fallbackCode)
                };
            }
            return new IfcCurrencyAmount
            {
                Amount = ToLong(raw),
                CurrencyCode = fallbackCode
            };
        }

        private static IfcItemDetails ItemDetails(IDictionary<string, object> map)
        {
            if (map == null)
                return null;
            return new IfcItemDetails
            {
                ItemId = Str(map, "itemId"),
                ItemDescription = Str(map, "itemDescription"),
                ItemPrice = Long(map, "itemPrice"),
                ItemAmount = Str(map, "itemAmount"),
                OptionalAmountInfo = Str(map, "optionalAmountInfo"),
                ReferencePricePerUnit = Long(map, "referencePricePerUnit"),
                ReferenceUnit = Str(map, "referenceUnit"),
                TaxDescription = Str(map, "taxDescription"),
                MinimumCustomerAge = (int)Long(map, "minimumCustomerAge"),
                MinimumEmployeeAge = (int)Long(map, "minimumEmployeeAge"),
                HighRisk = Bool(map, "highRisk"),
                WeighingRequired = Bool(map, "weighingRequired"),
                DimensionsInputRequired = Bool(map, "dimensionsInputRequired"),
                PriceVerifyRequired = Bool(map, "priceVerifyRequired"),
                PriceEntryRequired = Bool(map, "priceEntryRequired"),
                QuantityEntryRequired = Bool(map, "quantityEntryRequired"),
                QuantityDisallowed = Bool(map, "quantityDisallowed"),
                PriceOverrideAllowed = Bool(map, "priceOverrideAllowed"),
                NotForSale = Bool(map, "notForSale"),
                NotForScoSale = Bool(map, "notForScoSale"),
                PharmacyItem = Bool(map, "pharmacyItem"),
                PromotionText = Str(map, "promotionText"),
                AllowSkipBagging = Bool(map, "allowSkipBagging")
            };
        }

        private static IfcGlobalProperties GlobalProperties(IDictionary<string, object> map)
        {
            if (map == null)
                return null;
            return new IfcGlobalProperties
            {
                StoreId = Str(map, "storeId"),
                CashRegisterId = Str(map, "cashRegisterId"),
                LocalCurrency = CurrencyDescriptor(AsMap(Get(map, "localCurrency"))),
                ForeignCurrencies = CurrencyList(Get(map, "foreignCurrencies")),
                SupportedLanguages = StringList(Get(map, "supportedLanguages")),
                AgeVerificationLimits = IntList(Get(map, "ageVerificationLimits")),
                GrayHairAge = (int)Long(map, "grayHairAge"),
                WeighingUnit = FirstNonEmpty(Str(map, "weighingUnit"), IfcGlobalProperties.WEIGHINGUNIT_NONE),
                PictureAccessBase = Str(map, "pictureAccessBase"),
                AdditionalProperties = MapList(Get(map, "additionalProperties"), Property)
            };
        }

        private static IfcProperty Property(IDictionary<string, object> map)
        {
            return new IfcProperty
            {
                Property = Str(map, "property"),
                Value = Str(map, "value")
            };
        }

        private static IfcCustomer Customer(IDictionary<string, object> map)
        {
            if (map == null)
                return null;
            return new IfcCustomer
            {
                CustomerNumber = Str(map, "customerNumber"),
                FirstName = Str(map, "firstName"),
                LastName = Str(map, "lastName"),
                GenderType = Str(map, "genderType"),
                BirthDate = Str(map, "birthDate"),
                Info1 = Str(map, "info1"),
                FrequentShopperPoints = Long(map, "frequentShopperPoints"),
                Language = Str(map, "language"),
                Homestore = Str(map, "homestore"),
                Redeemable = Bool(map, "redeemable"),
                Staff = Bool(map, "staff")
            };
        }

        private static IList<IfcTenderDescriptor> TenderDescriptors(object raw)
        {
            return MapList(raw, map => new IfcTenderDescriptor
            {
                TenderType = Str(map, "tenderType"),
                OverpayAllowed = Bool(map, "overpayAllowed"),
                UnderpayAllowed = Bool(map, "underpayAllowed"),
                MinAmount = Long(map, "minAmount"),
                MaxAmount = Long(map, "maxAmount")
            });
        }

        private static IList<IfcCurrencyDescriptor> CurrencyList(object raw)
        {
            return MapList(raw, CurrencyDescriptor);
        }

        private static IfcCurrencyDescriptor CurrencyDescriptor(IDictionary<string, object> map)
        {
            if (map == null)
                return null;
            return new IfcCurrencyDescriptor
            {
                CurrencyCode = Str(map, "currencyCode"),
                FractionDigits = (int)Long(map, "fractionDigits"),
                ConversionFactor = ToDouble(Get(map, "conversionFactor"))
            };
        }

        private static IList<T> MapList<T>(object raw, Func<IDictionary<string, object>, T> map) where T : class
        {
            var list = new List<T>();
            foreach (var item in Enumerate(raw))
            {
                var dict = AsMap(item);
                if (dict == null)
                    continue;
                var value = map(dict);
                if (value != null)
                    list.Add(value);
            }
            return list;
        }

        private static IList<string> StringList(object raw)
        {
            var list = new List<string>();
            foreach (var item in Enumerate(raw))
            {
                if (item != null)
                    list.Add(Convert.ToString(item));
            }
            return list;
        }

        private static IList<int> IntList(object raw)
        {
            var list = new List<int>();
            foreach (var item in Enumerate(raw))
                list.Add((int)ToLong(item));
            return list;
        }

        private static IEnumerable Enumerate(object raw)
        {
            var enumerable = raw as IEnumerable;
            if (enumerable == null || raw is string)
                yield break;
            foreach (var item in enumerable)
                yield return item;
        }

        private static IDictionary<string, object> AsMap(object raw)
        {
            if (raw is IDictionary<string, object> typed)
                return typed;
            var dict = raw as IDictionary;
            if (dict == null)
                return null;
            var map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry entry in dict)
            {
                if (entry.Key != null)
                    map[Convert.ToString(entry.Key)] = entry.Value;
            }
            return map;
        }

        private static object Get(IDictionary<string, object> map, string key)
        {
            object value;
            return map != null && map.TryGetValue(key, out value) ? value : null;
        }

        private static string Str(IDictionary<string, object> map, string key)
        {
            var value = Get(map, key);
            return value == null ? null : Convert.ToString(value);
        }

        private static bool Bool(IDictionary<string, object> map, string key)
        {
            var value = Get(map, key);
            if (value is bool)
                return (bool)value;
            bool parsed;
            return bool.TryParse(Convert.ToString(value), out parsed) && parsed;
        }

        private static long Long(IDictionary<string, object> map, string key)
        {
            return ToLong(Get(map, key));
        }

        private static long ToLong(object value)
        {
            if (value == null)
                return 0;
            try
            {
                return Convert.ToInt64(value);
            }
            catch
            {
                return 0;
            }
        }

        private static double ToDouble(object value)
        {
            if (value == null)
                return 0;
            try
            {
                return Convert.ToDouble(value);
            }
            catch
            {
                return 0;
            }
        }

        private static string FirstNonEmpty(string a, string b)
        {
            return string.IsNullOrEmpty(a) ? b : a;
        }
    }
}
