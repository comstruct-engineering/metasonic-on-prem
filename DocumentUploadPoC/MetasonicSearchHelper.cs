using System;
using DmsFormular1.ScanviewWebservice;

namespace MetasonicDocumentUploadPoC
{
    /// <summary>Helper methods for searching documents in MetaSonic Scanview by qualifier values.</summary>
    internal static class MetasonicSearchHelper
    {
        public static ResponseDocument[] SearchByFields(
            ScanviewWebserviceClient client,
            ref LoginData loginData,
            string docClassId,
            string userFolderId,
            RequestField[] searchFields,
            int maxResults = 25,
            bool noLikeSearch = true)
        {
            if (searchFields == null || searchFields.Length == 0)
            {
                return new ResponseDocument[0];
            }

            var ops = new RequestSearchOperators
            {
                MaxResults = maxResults,
                OrSearch = false,
                NoLikeSearch = noLikeSearch
            };

            return client.Search(
                ref loginData,
                docClassId,
                userFolderId ?? string.Empty,
                string.Empty,
                searchFields,
                ops,
                loadItemList: false,
                smallDocument: true,
                smallFieldList: true
            ) ?? new ResponseDocument[0];
        }

        public static ResponseDocument FindExistingByCorrelationValue(
            ScanviewWebserviceClient client,
            ref LoginData loginData,
            string correlationValue,
            string comstructInvoiceQualifierId,
            string docClassId,
            string userFolderId)
        {
            if (string.IsNullOrWhiteSpace(comstructInvoiceQualifierId))
            {
                return null;
            }

            var hits = SearchByFields(
                client,
                ref loginData,
                docClassId,
                userFolderId,
                new[]
                {
                    new RequestField { ID = comstructInvoiceQualifierId, Value = correlationValue }
                },
                maxResults: 5,
                noLikeSearch: true
            );

            if (hits.Length > 1)
            {
                Console.WriteLine(
                    "Warning: Metasonic Search returned " + hits.Length + " documents for correlation value; using the first."
                );
            }

            return hits.Length > 0 ? hits[0] : null;
        }
    }
}
