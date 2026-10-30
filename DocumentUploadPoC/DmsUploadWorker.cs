using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using DmsFormular1.ScanviewWebservice;

namespace MetasonicDocumentUploadPoC
{
    /// <summary>Comstruct → MetaSonic batch worker: downloads verified invoice PDFs from Comstruct, appends them via <c>AddItem</c>, updates qualifiers, and reports sync status back to Comstruct.</summary>
    internal static class DmsUploadWorker
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        public static int Run()
        {
            var dry = DmsEnvironment.DmsWorkerDryRun();
            Console.WriteLine("=== DMS upload worker (.NET) — Comstruct → MetaSonic ===\n");
            var qs = DmsEnvironment.QueueStatus();
            Console.WriteLine(
                "Comstruct base=" + DmsEnvironment.ComstructBaseUrl() +
                (string.IsNullOrEmpty(qs) ? "" : " statusFilter=" + qs) +
                " metasonicCorrelationKey=" + DmsEnvironment.MetasonicDmsCorrelationKey() +
                " dryRun=" + dry + "\n"
            );

            var statePath = DmsEnvironment.DmsStateFile();
            string lastChecked = null;
            if (File.Exists(statePath))
            {
                try
                {
                    var raw = File.ReadAllText(statePath);
                    var st = Json.DeserializeObject(raw) as Dictionary<string, object>;
                    if (st != null && st.ContainsKey("lastCheckedAt"))
                    {
                        lastChecked = Convert.ToString(st["lastCheckedAt"]);
                    }
                }
                catch
                {
                    // ignore bad state
                }
            }

            if (string.IsNullOrEmpty(lastChecked))
            {
                lastChecked = DmsEnvironment.DmsWorkerInitialUpdatedAfter();
            }

            var initialWatermark = lastChecked;
            var epochIso = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                .ToString("o", CultureInfo.InvariantCulture);

            using (var comstruct = new ComstructApiClient())
            {
                Console.WriteLine("[1/5] Connecting to Comstruct (" + DmsEnvironment.ComstructBaseUrl() + ")...");
                var fromApi = comstruct.ListInvoiceRowsForQueue();
                Console.WriteLine("[1/5] Connected to Comstruct. Fetched " + fromApi.Count + " invoice(s).");
                var fromApiCount = fromApi.Count;
                var rows = SkipFinishedRows(fromApi);
                var afterSkip = rows.Count;
                rows = SortByCursor(rows);

                Console.WriteLine(
                    "DMS worker: fromApi=" + fromApiCount + " afterSkip=" + afterSkip + " (processing " + rows.Count + ")"
                );

                if (rows.Count == 0)
                {
                    Console.WriteLine("Nothing to process.");
                    return 0;
                }

                if (dry)
                {
                    foreach (var r in rows)
                    {
                        var id = GetStr(r, "id");
                        Console.WriteLine("[dry-run] would process invoice " + id);
                    }

                    return 0;
                }

                var binding = new System.ServiceModel.BasicHttpBinding
                {
                    MaxReceivedMessageSize = 2147483647,
                    MaxBufferSize = 2147483647,
                    SendTimeout = TimeSpan.FromMinutes(5),
                    ReceiveTimeout = TimeSpan.FromMinutes(5),
                    ReaderQuotas = new System.Xml.XmlDictionaryReaderQuotas
                    {
                        MaxDepth = 32,
                        MaxStringContentLength = 2147483647,
                        MaxArrayLength = 2147483647,
                        MaxBytesPerRead = 2147483647,
                        MaxNameTableCharCount = 16384
                    }
                };
                binding.Security.Mode = System.ServiceModel.BasicHttpSecurityMode.None;

                var endpoint = new System.ServiceModel.EndpointAddress(DmsEnvironment.MetasonicEndpoint());
                var client = new ScanviewWebserviceClient(binding, endpoint);

                var loginData = new LoginData
                {
                    AutoLogin = true,
                    NoSession = true,
                    UseSSO = false,
                    Username = DmsEnvironment.MetasonicUser(),
                    Password = DmsEnvironment.MetasonicPassword(),
                    LoginDatabaseName = DmsEnvironment.MetasonicDatabase()
                };
                Console.WriteLine("\n[2/5] Connecting to MetaSonic (" + DmsEnvironment.MetasonicEndpoint() + ")...");
                client.Login(ref loginData);
                Console.WriteLine("[2/5] Connected to MetaSonic as '" + DmsEnvironment.MetasonicUser() + "'.");

                Console.WriteLine("\n[3/5] Processing " + rows.Count + " invoice(s)...\n");
                var maxCursor = string.IsNullOrEmpty(initialWatermark) ? epochIso : initialWatermark;
                foreach (var row in rows)
                {
                    var advanceCursor = ProcessOne(
                        comstruct,
                        client,
                        ref loginData,
                        row);
                    if (advanceCursor)
                    {
                        var c = InvoiceCursorTimestamp(row);
                        maxCursor = MaxIso(maxCursor, c);
                    }
                }

                Console.WriteLine("\n=== All invoices processed. ===\n");

                try
                {
                    var stateOut = new Dictionary<string, object> { { "lastCheckedAt", maxCursor } };
                    var parent = Path.GetDirectoryName(statePath);
                    if (!string.IsNullOrEmpty(parent))
                    {
                        Directory.CreateDirectory(parent);
                    }

                    File.WriteAllText(statePath, Json.Serialize(stateOut), Encoding.UTF8);
                    Console.WriteLine("DMS worker: state written " + statePath + " lastCheckedAt=" + maxCursor);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Warning: could not write state file: " + ex.Message);
                }
            }

            return 0;
        }

        private static string InvoiceCursorTimestamp(Dictionary<string, object> row)
        {
            var t = GetStr(row, "updated_at");
            if (string.IsNullOrEmpty(t))
            {
                t = GetStr(row, "created_at");
            }

            if (string.IsNullOrEmpty(t))
            {
                return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    .ToString("o", CultureInfo.InvariantCulture);
            }

            return t;
        }

        private static string GetStr(Dictionary<string, object> row, string key)
        {
            if (row == null || !row.ContainsKey(key) || row[key] == null)
            {
                return null;
            }

            return Convert.ToString(row[key]);
        }

        private static string MetasonicCorrelationValue(Dictionary<string, object> row)
        {
            if (string.Equals("id", DmsEnvironment.MetasonicDmsCorrelationKey(), StringComparison.OrdinalIgnoreCase))
            {
                return GetStr(row, "id");
            }

            return GetStr(row, "external_id");
        }

        private static List<Dictionary<string, object>> SortByCursor(List<Dictionary<string, object>> rows)
        {
            return rows
                .OrderBy(r => DateTime.Parse(InvoiceCursorTimestamp(r), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind))
                .ToList();
        }

        private static List<Dictionary<string, object>> SkipFinishedRows(
            List<Dictionary<string, object>> rows)
        {
            return rows
                .Where(r =>
                {
                    var ext = (GetStr(r, "external_status") ?? string.Empty).Trim();
                    return !string.Equals(ext, "SYNCED", StringComparison.OrdinalIgnoreCase);
                })
                .ToList();
        }

        private static string MaxIso(string a, string b)
        {
            if (string.IsNullOrEmpty(a))
            {
                return b;
            }

            if (string.IsNullOrEmpty(b))
            {
                return a;
            }

            DateTime da;
            DateTime db;
            if (DateTime.TryParse(a, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out da) &&
                DateTime.TryParse(b, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out db))
            {
                return da >= db ? a : b;
            }

            return b;
        }

        private static void PatchDms(ComstructApiClient com, string invoiceId, string externalStatus, string errOrNull)
        {
            var o = new Dictionary<string, object> { { "external_status", externalStatus } };
            if (!string.IsNullOrEmpty(errOrNull))
            {
                o["dmsLastError"] = errOrNull;
            }

            com.PatchInvoiceDms(invoiceId, Json.Serialize(o));
        }

        private static string BuildPdfFileName(
            ScanviewWebserviceClient client,
            ref LoginData loginData,
            string docId,
            Dictionary<string, object> row,
            string invoiceId,
            string detectedExt)
        {
            var sourceExt = string.IsNullOrEmpty(detectedExt) ? ".pdf" : detectedExt;

            if (!string.IsNullOrWhiteSpace(docId))
            {
                try
                {
                    var items = client.GetItemList(ref loginData, docId, false, false);
                    if (items != null && items.Length > 0)
                    {
                        var first = items[0];
                        Console.WriteLine("  [DEBUG] Existing item[0]: OriginalFileName='" + first.OriginalFileName + "', ItemName='" + first.ItemName + "'");
                        var firstName = first.OriginalFileName;
                        if (!string.IsNullOrWhiteSpace(firstName))
                        {
                            var baseName = Path.GetFileNameWithoutExtension(firstName);
                            return SanitizeFilePart(baseName) + "_geprueft_von_comstruct" + sourceExt;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("  Warning: could not read existing item names (" + ex.Message + "), using fallback filename.");
                }
            }

            var number = SanitizeFilePart(GetStr(row, "invoice_number"));
            var label = string.IsNullOrEmpty(number) ? invoiceId : number;
            return "Comstruct_Rechnung_" + label + sourceExt;
        }

        private static string SanitizeFilePart(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();
            foreach (var c in value.Trim())
            {
                switch (c)
                {
                    case 'ä': sb.Append("ae"); break;
                    case 'ö': sb.Append("oe"); break;
                    case 'ü': sb.Append("ue"); break;
                    case 'Ä': sb.Append("Ae"); break;
                    case 'Ö': sb.Append("Oe"); break;
                    case 'Ü': sb.Append("Ue"); break;
                    case 'ß': sb.Append("ss"); break;
                    default:
                        sb.Append(Array.IndexOf(invalid, c) >= 0 || c == ' ' ? '_' : c);
                        break;
                }
            }
            return sb.ToString().Trim('_');
        }

        private static bool ProcessOne(
            ComstructApiClient comstruct,
            ScanviewWebserviceClient client,
            ref LoginData loginData,
            Dictionary<string, object> row)
        {
            var invoiceId = GetStr(row, "id");
            if (string.IsNullOrEmpty(invoiceId))
            {
                Console.WriteLine("--- row missing id, skipping ---");
                return true;
            }

            var correlationValue = MetasonicCorrelationValue(row);
            var correlationQualId = DmsEnvironment.MetasonicComstructQualifierId();
            var allowCreate = DmsEnvironment.DmsWorkerAllowCreateIfNotFound();

            if (string.IsNullOrWhiteSpace(correlationValue) && !allowCreate)
            {
                Console.WriteLine("--- Invoice " + invoiceId + ": no " + DmsEnvironment.MetasonicDmsCorrelationKey() + " set, skipping ---");
                return true;
            }

            Console.WriteLine("--- Invoice " + invoiceId + " (correlation=" + (correlationValue ?? invoiceId) + ") ---");

            Console.WriteLine("  [4/5] Downloading file from Comstruct...");
            var download = comstruct.DownloadInvoicePdf(invoiceId);
            if (download == null || download.Item1 == null || download.Item1.Length == 0)
            {
                Console.WriteLine("  [4/5] No file available yet — skipping invoice " + invoiceId + ".");
                return false;
            }
            var pdf = download.Item1;
            var detectedExt = MetasonicUploadHelper.DetectFileExtension(pdf);
            Console.WriteLine("  [4/5] File downloaded (" + pdf.Length + " bytes, detected type: " + detectedExt + ").");

            try
            {
                string metasonicDocId = null;

                if (!string.IsNullOrWhiteSpace(correlationQualId) && !string.IsNullOrWhiteSpace(correlationValue))
                {
                    var found = MetasonicSearchHelper.FindExistingByCorrelationValue(
                        client,
                        ref loginData,
                        correlationValue,
                        correlationQualId,
                        DmsEnvironment.MetasonicDocClassId(),
                        DmsEnvironment.MetasonicSearchUserFolderId());

                    if (found != null && !string.IsNullOrEmpty(found.ID))
                    {
                        metasonicDocId = found.ID;
                        Console.WriteLine("  [5/5] Found existing MetaSonic document " + metasonicDocId + " via qualifier " + correlationQualId + "='" + correlationValue + "'.");
                    }
                    else if (!allowCreate)
                    {
                        throw new InvalidOperationException(
                            "MetaSonic: no document found for qualifier " + correlationQualId + "='" + correlationValue + "' (set DMS_WORKER_ALLOW_CREATE_DMS_DOCUMENT=true to create new documents).");
                    }
                }
                else if (!allowCreate)
                {
                    metasonicDocId = correlationValue;
                }

                if (string.IsNullOrEmpty(metasonicDocId) && allowCreate)
                {
                    var fileName = BuildPdfFileName(client, ref loginData, null, row, invoiceId, detectedExt);
                    var fields = MetasonicUploadHelper.BuildFieldsFromInvoiceJson(
                        row,
                        correlationValue ?? invoiceId,
                        correlationQualId);

                    Console.WriteLine("  [5/5] Creating new MetaSonic document in DocClass " + DmsEnvironment.MetasonicDocClassId() + " (fileName='" + fileName + "')...");
                    var created = MetasonicUploadHelper.SaveNewDocument(
                        client,
                        ref loginData,
                        fields,
                        pdf,
                        fileName,
                        DmsEnvironment.MetasonicDocClassId());
                    metasonicDocId = created != null ? created.ID : null;
                    Console.WriteLine("  [5/5] Created MetaSonic document " + metasonicDocId + " with '" + fileName + "'.");
                }
                else
                {
                    var fileName = BuildPdfFileName(client, ref loginData, metasonicDocId, row, invoiceId, detectedExt);
                    var itemTypeNum = MetasonicUploadHelper.GetItemTypeNumber(fileName);
                    Console.WriteLine("  [5/5] Appending file to MetaSonic document " + metasonicDocId + " (ItemTypeNumber=" + itemTypeNum + ", fileName='" + fileName + "')...");
                    MetasonicUploadHelper.AppendDocumentPdfItem(
                        client,
                        ref loginData,
                        metasonicDocId,
                        pdf,
                        fileName);
                    Console.WriteLine("  [5/5] PDF appended as '" + fileName + "'.");
                }

                var qualUpdates = new Dictionary<string, string>();
                var syncedQualId = DmsEnvironment.MetasonicComstructSyncedQualifierId();
                if (!string.IsNullOrEmpty(syncedQualId))
                {
                    qualUpdates[syncedQualId] = "Ja";
                }

                var urlQualId = DmsEnvironment.MetasonicComstructUrlQualifierId();
                if (!string.IsNullOrEmpty(urlQualId))
                {
                    qualUpdates[urlQualId] = "https://app.comstruct.com/invoices/" + invoiceId;
                }

                if (qualUpdates.Count > 0 && !string.IsNullOrEmpty(metasonicDocId))
                {
                    try
                    {
                        MetasonicUploadHelper.SetDocumentQualifiers(
                            client, ref loginData, metasonicDocId, qualUpdates);
                        Console.WriteLine("  [5/5] Qualifiers updated (" + qualUpdates.Count + " field(s)).");
                    }
                    catch (Exception qEx)
                    {
                        Console.WriteLine("  Warning: could not update qualifiers: " + qEx.Message);
                    }
                }

                PatchDms(comstruct, invoiceId, "SYNCED", null);
                Console.WriteLine("  [5/5] Comstruct external_status set to SYNCED.");

                var callbackId = !string.IsNullOrWhiteSpace(GetStr(row, "external_id"))
                    ? GetStr(row, "external_id")
                    : metasonicDocId;
                if (!string.IsNullOrWhiteSpace(callbackId))
                {
                    try
                    {
                        comstruct.PostInvoiceCallback(callbackId, true);
                        Console.WriteLine("  [5/5] Comstruct invoice status set to CHECKED.");
                    }
                    catch (Exception cbEx)
                    {
                        Console.WriteLine("  Warning: could not set invoice status to CHECKED: " + cbEx.Message);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("  ERROR invoice " + invoiceId + ": " + ex.Message);
                try
                {
                    PatchDms(comstruct, invoiceId, "FAILED", ex.Message);
                    Console.WriteLine("  Comstruct status set to FAILED.");
                }
                catch
                {
                    // ignored
                }

                return true;
            }
        }
    }
}
