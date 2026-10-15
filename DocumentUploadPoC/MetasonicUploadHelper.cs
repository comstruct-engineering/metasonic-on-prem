using System;
using System.IO;
using System.Collections.Generic;
using DmsFormular1.ScanviewWebservice;

namespace MetasonicDocumentUploadPoC
{
    /// <summary>Helper methods for creating/updating MetaSonic documents and appending file attachments via WCF.</summary>
    internal static class MetasonicUploadHelper
    {
        private const string QualKreditor = "89900207";
        private const string QualBelegnr = "89900202";
        private const string QualBetrag = "89900131";
        private const string QualSachlich = "223";
        private const string QualPreislich = "222";

        public static int GetItemTypeNumber(string fileName)
        {
            var extension = Path.GetExtension(fileName)?.ToLowerInvariant();
            if (extension == ".pdf") return 54;
            if (extension == ".tif" || extension == ".tiff") return 1;
            if (extension == ".jpg" || extension == ".jpeg") return 8;
            if (extension == ".png") return 14;
            if (extension == ".doc") return 50;
            if (extension == ".docx") return 66;
            if (extension == ".xls") return 100;
            if (extension == ".xlsx") return 102;
            if (extension == ".ppt") return 150;
            if (extension == ".pptx") return 151;
            if (extension == ".txt") return 2;
            if (extension == ".rtf") return 59;
            if (extension == ".html" || extension == ".htm") return 57;
            if (extension == ".zip") return 200;
            if (extension == ".xml") return 700;
            if (extension == ".eml") return 800;
            return 54;
        }

        /// <summary>
        /// Detects the actual file type from magic bytes and returns the correct extension.
        /// The Comstruct backend always reports .pdf as the filename even for TIFF files,
        /// so we inspect the raw bytes to determine the true format.
        /// </summary>
        public static string DetectFileExtension(byte[] data)
        {
            if (data == null || data.Length < 4) return ".pdf";

            // PDF: %PDF
            if (data[0] == 0x25 && data[1] == 0x50 && data[2] == 0x44 && data[3] == 0x46)
                return ".pdf";

            // TIFF little-endian: II*\0
            if (data[0] == 0x49 && data[1] == 0x49 && data[2] == 0x2A && data[3] == 0x00)
                return ".tif";

            // TIFF big-endian: MM\0*
            if (data[0] == 0x4D && data[1] == 0x4D && data[2] == 0x00 && data[3] == 0x2A)
                return ".tif";

            // JPEG: FF D8 FF
            if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
                return ".jpg";

            // PNG: \x89PNG
            if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
                return ".png";

            return ".pdf";
        }

        public static RequestField[] BuildFieldsFromInvoiceJson(
            System.Collections.Generic.Dictionary<string, object> inv,
            string metasonicCorrelationValue,
            string correlationQualifierIdOrNull)
        {
            string GetS(string key)
            {
                return inv != null && inv.ContainsKey(key) && inv[key] != null
                    ? Convert.ToString(inv[key])
                    : null;
            }

            var fields = new System.Collections.Generic.List<RequestField>();
            var supplier = GetS("supplier_name")?.Trim();
            if (!string.IsNullOrEmpty(supplier))
            {
                fields.Add(new RequestField { ID = QualKreditor, Value = supplier });
            }

            var beleg = GetS("invoice_number")?.Trim()
                ?? GetS("external_id")?.Trim()
                ?? GetS("id");
            if (!string.IsNullOrEmpty(beleg))
            {
                fields.Add(new RequestField { ID = QualBelegnr, Value = beleg });
            }

            if (inv != null && inv.ContainsKey("net_amount") && inv["net_amount"] != null)
            {
                fields.Add(new RequestField { ID = QualBetrag, Value = Convert.ToString(inv["net_amount"]) });
            }
            else if (inv != null && inv.ContainsKey("total_amount") && inv["total_amount"] != null)
            {
                fields.Add(new RequestField { ID = QualBetrag, Value = Convert.ToString(inv["total_amount"]) });
            }

            fields.Add(new RequestField { ID = QualSachlich, Value = "ungeprüft" });
            fields.Add(new RequestField { ID = QualPreislich, Value = "ungeprüft" });

            if (!string.IsNullOrWhiteSpace(correlationQualifierIdOrNull))
            {
                fields.Add(
                    new RequestField
                    {
                        ID = correlationQualifierIdOrNull.Trim(),
                        Value = metasonicCorrelationValue
                    });
            }

            return fields.ToArray();
        }

        public static RequestField[] BuildCorrelationOnlyFields(
            string metasonicCorrelationValue,
            string correlationQualifierIdOrNull)
        {
            if (string.IsNullOrWhiteSpace(correlationQualifierIdOrNull))
            {
                return new RequestField[0];
            }

            return new[]
            {
                new RequestField
                {
                    ID = correlationQualifierIdOrNull.Trim(),
                    Value = metasonicCorrelationValue ?? string.Empty
                }
            };
        }

        public static ResponseDocument SaveNewDocument(
            ScanviewWebserviceClient client,
            ref LoginData loginData,
            RequestField[] fieldList,
            byte[] fileData,
            string fileName,
            string docClassId)
        {
            var item = new RequestItem
            {
                ItemData = fileData,
                ItemName = Path.GetFileNameWithoutExtension(fileName),
                OriginalFileName = fileName,
                ItemTypeNumber = GetItemTypeNumber(fileName)
            };

            var document = new RequestDocument
            {
                ID = null,
                DocClassID = docClassId,
                MainUserFolderID = null,
                FieldList = fieldList,
                ItemList = new[] { item }
            };

            return client.SaveDocument(ref loginData, document);
        }

        /// <summary>
        /// Updates Comstruct-related qualifiers on an existing document via <c>SaveDocument</c> (fields only, no ItemList).
        /// Merges the provided key/value pairs into the existing field list and saves in a single call.
        /// </summary>
        public static void SetDocumentQualifiers(
            ScanviewWebserviceClient client,
            ref LoginData loginData,
            string docId,
            System.Collections.Generic.Dictionary<string, string> updates)
        {
            var existing = client.GetDocument(ref loginData, docId, false, false, false, false);

            var fieldMap = new System.Collections.Generic.Dictionary<string, string>();
            if (existing?.FieldList != null)
            {
                foreach (var f in existing.FieldList)
                {
                    if (!string.IsNullOrEmpty(f.ID))
                    {
                        fieldMap[f.ID] = f.Value ?? string.Empty;
                    }
                }
            }

            foreach (var kv in updates)
            {
                fieldMap[kv.Key] = kv.Value;
            }

            var fields = new System.Collections.Generic.List<RequestField>();
            foreach (var kv in fieldMap)
            {
                fields.Add(new RequestField { ID = kv.Key, Value = kv.Value });
            }

            var document = new RequestDocument
            {
                ID = docId,
                DocClassID = existing?.DocClassID,
                MainUserFolderID = null,
                FieldList = fields.ToArray(),
                ItemList = null
            };

            client.SaveDocument(ref loginData, document);
        }

        /// <summary>Append a new PDF item via <c>AddItem</c> — existing attachments are left unchanged.</summary>
        public static ResponseItem AppendDocumentPdfItem(
            ScanviewWebserviceClient client,
            ref LoginData loginData,
            string docId,
            byte[] fileData,
            string fileName)
        {
            var item = new RequestItem
            {
                ItemData = fileData,
                ItemName = Path.GetFileNameWithoutExtension(fileName),
                OriginalFileName = fileName,
                ItemTypeNumber = GetItemTypeNumber(fileName)
            };

            return client.AddItem(ref loginData, docId, item);
        }
    }
}
