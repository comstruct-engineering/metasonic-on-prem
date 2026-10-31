using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.ServiceModel;
using System.Text;
using System.Web.Script.Serialization;
using DmsFormular1.ScanviewWebservice;

namespace MetasonicDocumentUploadPoC
{
    /// <summary>
    /// Bidirectional MetaSonic ↔ Comstruct Sync Worker:
    /// Phase 1 (MetaSonic → Comstruct):
    ///   - Checks invoices in MetaSonic updated since the last persisted watermark.
    ///   - Checks if each invoice already exists in Comstruct (by external_id = MetaSonic Document ID).
    ///   - Ensures the supplier and project exist in Comstruct (upserting them if missing).
    ///   - Creates the invoice in Comstruct with the MetaSonic Document ID as external_id.
    /// Phase 2 (Comstruct → MetaSonic):
    ///   - Fetches all invoices in Comstruct marked as checked (COMSTRUCT_DMS_QUEUE_STATUS, default CHECKED)
    ///     since the last persisted check that do not yet have external_status = SYNCED.
    ///   - Downloads the stamped &amp; annotated PDF from Comstruct and appends it to the MetaSonic document via AddItem.
    ///   - Persists the last successful check watermark in .dms-worker-state.json and sets external_status = SYNCED.
    /// </summary>
    internal static class DmsUploadWorker
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        internal sealed class WorkerState
        {
            public string LastMetasonicCheckAt;
            public string LastComstructCheckAt;
            public HashSet<string> ImportedMetasonicDocIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        public static int Run()
        {
            return RunBidirectional(runPhase1: DmsEnvironment.MetasonicInboundSyncEnabled(), runPhase2: true);
        }

        public static int RunInboundOnly()
        {
            return RunBidirectional(runPhase1: true, runPhase2: false);
        }

        public static int RunOutboundOnly()
        {
            return RunBidirectional(runPhase1: false, runPhase2: true);
        }

        private static int RunBidirectional(bool runPhase1, bool runPhase2)
        {
            var dry = DmsEnvironment.DmsWorkerDryRun();
            var qs = DmsEnvironment.QueueStatus();
            var statePath = DmsEnvironment.DmsStateFile();
            var state = LoadState(statePath);

            Console.WriteLine("=== MetaSonic ↔ Comstruct Bidirectional Sync Worker ===\n");
            Console.WriteLine(
                "Comstruct=" + DmsEnvironment.ComstructBaseUrl() +
                " | MetaSonic=" + DmsEnvironment.MetasonicEndpoint() +
                " (DB=" + DmsEnvironment.MetasonicDatabase() + ")" +
                " | CheckedStatus=" + qs +
                " | DryRun=" + dry + "\n"
            );

            using (var comstruct = new ComstructApiClient())
            {
                var client = CreateMetasonicClient();
                var loginData = new LoginData
                {
                    AutoLogin = true,
                    NoSession = true,
                    UseSSO = false,
                    Username = DmsEnvironment.MetasonicUser(),
                    Password = DmsEnvironment.MetasonicPassword(),
                    LoginDatabaseName = DmsEnvironment.MetasonicDatabase()
                };

                Console.WriteLine("[Connect] Logging into MetaSonic (" + DmsEnvironment.MetasonicEndpoint() + ")...");
                client.Login(ref loginData);
                Console.WriteLine("[Connect] Connected to MetaSonic as '" + DmsEnvironment.MetasonicUser() + "'.\n");

                if (runPhase1)
                {
                    RunPhase1MetasonicToComstruct(comstruct, client, ref loginData, state, statePath, dry);
                }

                if (runPhase2)
                {
                    RunPhase2ComstructToMetasonic(comstruct, client, ref loginData, state, statePath, dry);
                }

                if (!dry)
                {
                    SaveState(statePath, state);
                }
            }

            Console.WriteLine("\n=== Sync run completed successfully. ===");
            return 0;
        }

        // =====================================================================
        // PHASE 1: MetaSonic → Comstruct (New/Updated Invoices + Supplier/Project)
        // =====================================================================
        private static void RunPhase1MetasonicToComstruct(
            ComstructApiClient comstruct,
            ScanviewWebserviceClient client,
            ref LoginData loginData,
            WorkerState state,
            string statePath,
            bool dry)
        {
            var docClassId = DmsEnvironment.MetasonicDocClassId();
            var lastWatermarkStr = state.LastMetasonicCheckAt ?? DmsEnvironment.DmsWorkerInitialUpdatedAfter();
            DateTime? lastWatermarkUtc = ParseFlexibleDateUtc(lastWatermarkStr);

            Console.WriteLine("--- PHASE 1: MetaSonic → Comstruct (DocClass=" + docClassId + ", lastCheck=" + (lastWatermarkStr ?? "initial") + ") ---");

            var fieldNames = LoadDocClassFieldNames(client, ref loginData, docClassId);
            var docs = SearchMetasonicDocuments(client, ref loginData, docClassId, fieldNames, DmsEnvironment.MetasonicScanMaxResults());
            Console.WriteLine("  Scanned " + docs.Length + " document(s) in MetaSonic DocClass " + docClassId + ".");

            if (docs.Length == 0)
            {
                return;
            }

            // Pre-load recent Comstruct invoices so we can check existence in memory first
            var knownExternalIds = new HashSet<string>(state.ImportedMetasonicDocIds, StringComparer.OrdinalIgnoreCase);
            try
            {
                var recentInComstruct = comstruct.ListInvoices(null, 500);
                foreach (var r in recentInComstruct)
                {
                    var ext = GetStr(r, "external_id");
                    if (!string.IsNullOrWhiteSpace(ext))
                    {
                        knownExternalIds.Add(ext.Trim());
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("  Note: Could not pre-fetch Comstruct invoice list (" + ex.Message + "), will check per document.");
            }

            // Optional lookup maps from MetaSonic master-data DocClasses (e.g. 89035003 Kostenstellen)
            Dictionary<string, string> masterProjects = null;
            DateTime maxSeenUtc = lastWatermarkUtc ?? new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            int importedCount = 0;
            int skippedExisting = 0;

            foreach (var summaryDoc in docs)
            {
                if (summaryDoc == null || string.IsNullOrWhiteSpace(summaryDoc.ID)) continue;
                var docId = summaryDoc.ID.Trim();

                var docTimeUtc = GetDocumentTimestampUtc(summaryDoc);
                if (lastWatermarkUtc.HasValue && docTimeUtc.HasValue && docTimeUtc.Value < lastWatermarkUtc.Value)
                {
                    continue;
                }

                // 1. Check if invoice already exists in Comstruct with external_id = docId
                if (comstruct.InvoiceExistsByExternalId(docId, knownExternalIds))
                {
                    skippedExisting++;
                    if (docTimeUtc.HasValue && docTimeUtc.Value > maxSeenUtc) maxSeenUtc = docTimeUtc.Value;
                    continue;
                }

                // Fetch full document qualifiers and item list
                var fullDoc = client.GetDocument(ref loginData, docId, loadItemData: false, smallDocument: false, smallFieldList: false, mergeBackgroundImage: false)
                    ?? summaryDoc;

                // Skip if already marked as synced by Comstruct
                var syncedQualId = DmsEnvironment.MetasonicComstructSyncedQualifierId();
                if (!string.IsNullOrEmpty(syncedQualId))
                {
                    var syncedVal = GetFieldVal(fullDoc, syncedQualId);
                    if (string.Equals(syncedVal, "Ja", StringComparison.OrdinalIgnoreCase))
                    {
                        knownExternalIds.Add(docId);
                        state.ImportedMetasonicDocIds.Add(docId);
                        continue;
                    }
                }

                // Extract supplier, project, and invoice fields from MetaSonic qualifiers
                string supplierName = ResolveFieldByConfigOrName(fullDoc, fieldNames, DmsEnvironment.QualifierSupplierName(), "kreditor", "lieferant", "name", "geschäftspartner");
                string supplierNumber = ResolveFieldByConfigOrName(fullDoc, fieldNames, DmsEnvironment.QualifierSupplierNumber(), "krednr", "kreditorennummer", "kreditornr", "lieferantennr");
                string projectNumber = ResolveFieldByConfigOrName(fullDoc, fieldNames, DmsEnvironment.QualifierProjectNumber(), "kostenstelle", "kst", "baustelle", "projekt");
                string projectTitle = ResolveFieldByConfigOrName(fullDoc, fieldNames, DmsEnvironment.QualifierProjectTitle(), "baustellenbezeichnung", "kostenstellenbezeichnung", "projektname");
                string invoiceNumber = ResolveFieldByConfigOrName(fullDoc, fieldNames, DmsEnvironment.QualifierInvoiceNumber(), "belegnr", "belegnummer", "rechnungsnummer", "rechnung");
                string netAmountStr = ResolveFieldByConfigOrName(fullDoc, fieldNames, DmsEnvironment.QualifierNetAmount(), "netto", "betrag", "nettobetrag");
                string grossAmountStr = ResolveFieldByConfigOrName(fullDoc, fieldNames, DmsEnvironment.QualifierGrossAmount(), "brutto", "gesamtbetrag", "bruttobetrag");
                string invoiceDateStr = ResolveFieldByConfigOrName(fullDoc, fieldNames, DmsEnvironment.QualifierInvoiceDate(), "belegdatum", "rechnungsdatum", "datum");

                Console.WriteLine("  [New DMS Invoice] DocID=" + docId + " | Supplier='" + (supplierName ?? "-") + "' | Project='" + (projectNumber ?? "-") + "' | BelegNr='" + (invoiceNumber ?? "-") + "'");

                if (dry)
                {
                    Console.WriteLine("    [dry-run] Would ensure supplier & project exist and upload MetaSonic doc " + docId + " as external_id='" + docId + "'");
                    importedCount++;
                    continue;
                }

                try
                {
                    // 2. Ensure Supplier exists in Comstruct (if not, create with external_id = supplierNumber or docId)
                    EnsureSupplierInComstruct(comstruct, docId, supplierNumber, supplierName);

                    // 3. Ensure Project exists in Comstruct (check first; if not, create with external_id = projectNumber or docId)
                    if (masterProjects == null && !string.IsNullOrWhiteSpace(DmsEnvironment.MetasonicProjectsDocClassId()))
                    {
                        masterProjects = LoadMasterProjectsMapFromMetasonic(client, ref loginData, DmsEnvironment.MetasonicProjectsDocClassId());
                    }
                    EnsureProjectInComstruct(comstruct, docId, projectNumber, projectTitle, masterProjects);

                    // 4. Download original invoice PDF/TIFF from MetaSonic
                    var fileTuple = DownloadPrimaryItemFromMetasonic(client, ref loginData, docId, fullDoc);
                    if (fileTuple == null || fileTuple.Item1 == null || fileTuple.Item1.Length == 0)
                    {
                        Console.WriteLine("    Warning: MetaSonic document " + docId + " has no downloadable file attachment yet — skipping until next run.");
                        continue;
                    }

                    // 5. Create invoice in Comstruct with documentId as external_id
                    var resp = comstruct.UploadInvoiceFromMetasonic(
                        documentId: docId,
                        fileBytes: fileTuple.Item1,
                        fileName: fileTuple.Item2,
                        supplierName: supplierName,
                        supplierNumber: supplierNumber,
                        projectNumber: projectNumber,
                        invoiceNumber: invoiceNumber,
                        invoiceDateIso: FormatDateIso(invoiceDateStr),
                        netAmount: ParseAmount(netAmountStr),
                        grossAmount: ParseAmount(grossAmountStr));

                    Console.WriteLine("    Uploaded MetaSonic doc " + docId + " to Comstruct (external_id='" + docId + "').");
                    knownExternalIds.Add(docId);
                    state.ImportedMetasonicDocIds.Add(docId);
                    importedCount++;

                    if (docTimeUtc.HasValue && docTimeUtc.Value > maxSeenUtc)
                    {
                        maxSeenUtc = docTimeUtc.Value;
                    }

                    SaveState(statePath, state);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("    ERROR importing MetaSonic document " + docId + ": " + ex.Message);
                }
            }

            if (!dry && maxSeenUtc > new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc))
            {
                state.LastMetasonicCheckAt = maxSeenUtc.ToString("o", CultureInfo.InvariantCulture);
            }
            else if (!dry && string.IsNullOrEmpty(state.LastMetasonicCheckAt))
            {
                state.LastMetasonicCheckAt = DateTime.UtcNow.AddMinutes(-1).ToString("o", CultureInfo.InvariantCulture);
            }

            Console.WriteLine("  Phase 1 summary: imported=" + importedCount + ", alreadyInComstruct=" + skippedExisting + "\n");
        }

        private static void EnsureSupplierInComstruct(
            ComstructApiClient comstruct,
            string documentId,
            string supplierNumber,
            string supplierName)
        {
            if (string.IsNullOrWhiteSpace(supplierName) && string.IsNullOrWhiteSpace(supplierNumber))
            {
                return;
            }

            var extId = !string.IsNullOrWhiteSpace(supplierNumber) ? supplierNumber.Trim() : documentId;
            var title = !string.IsNullOrWhiteSpace(supplierName) ? supplierName.Trim() : ("Kreditor " + extId);

            var sup = new Dictionary<string, object>();
            sup["external_id"] = extId;
            sup["title"] = title;
            sup["country_code"] = "DE";
            sup["is_active"] = true;

            try
            {
                comstruct.UpsertSuppliers(new List<Dictionary<string, object>> { sup });
                Console.WriteLine("    Ensured supplier in Comstruct: external_id='" + extId + "', title='" + title + "'");
            }
            catch (Exception ex)
            {
                Console.WriteLine("    Warning: could not upsert supplier '" + title + "': " + ex.Message);
            }
        }

        private static void EnsureProjectInComstruct(
            ComstructApiClient comstruct,
            string documentId,
            string projectNumber,
            string projectTitle,
            Dictionary<string, string> masterProjects)
        {
            if (string.IsNullOrWhiteSpace(projectNumber) && string.IsNullOrWhiteSpace(projectTitle))
            {
                return;
            }

            var projNum = !string.IsNullOrWhiteSpace(projectNumber) ? projectNumber.Trim() : documentId;
            var extId = !string.IsNullOrWhiteSpace(projectNumber) ? projectNumber.Trim() : documentId;

            try
            {
                if (comstruct.ProjectExists(extId, projNum))
                {
                    return;
                }
            }
            catch
            {
                // proceed to upsert if check fails
            }

            var title = projectTitle;
            if (string.IsNullOrWhiteSpace(title) && masterProjects != null)
            {
                masterProjects.TryGetValue(projNum, out title);
            }
            if (string.IsNullOrWhiteSpace(title))
            {
                title = "Baustelle " + projNum;
            }

            var proj = new Dictionary<string, object>();
            proj["external_id"] = extId;
            proj["project_number"] = projNum;
            proj["title"] = title.Trim();
            proj["active"] = true;

            try
            {
                comstruct.UpsertProject(proj);
                Console.WriteLine("    Created missing project in Comstruct: external_id='" + extId + "', project_number='" + projNum + "', title='" + title + "'");
            }
            catch (Exception ex)
            {
                Console.WriteLine("    Warning: could not create project '" + projNum + "': " + ex.Message);
            }
        }

        private static Tuple<byte[], string> DownloadPrimaryItemFromMetasonic(
            ScanviewWebserviceClient client,
            ref LoginData loginData,
            string docId,
            ResponseDocument doc)
        {
            var items = client.GetItemList(ref loginData, docId, false, false) ?? (doc != null ? doc.ItemList : null) ?? new ResponseItem[0];
            if (items.Length == 0) return null;

            // Pick the first item that is not already a Comstruct-stamped attachment
            ResponseItem selected = items[0];
            foreach (var it in items)
            {
                var orig = it.OriginalFileName ?? it.ItemName ?? "";
                if (orig.IndexOf("geprueft_von_comstruct", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    selected = it;
                    break;
                }
            }

            var data = client.GetItemData(ref loginData, selected.ID, mergeBackgroundImage: false);
            var name = !string.IsNullOrWhiteSpace(selected.OriginalFileName)
                ? selected.OriginalFileName
                : ("metasonic-" + docId + ".pdf");
            return Tuple.Create(data, name);
        }

        private static Dictionary<string, string> LoadMasterProjectsMapFromMetasonic(
            ScanviewWebserviceClient client,
            ref LoginData loginData,
            string projectsDocClassId)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var fieldNames = LoadDocClassFieldNames(client, ref loginData, projectsDocClassId);
                var docs = SearchMetasonicDocuments(client, ref loginData, projectsDocClassId, fieldNames, 2000);
                foreach (var d in docs)
                {
                    if (d.FieldList == null) continue;
                    string num = ResolveFieldByConfigOrName(d, fieldNames, null, "kostenstelle", "kst", "nummer", "nr", "code");
                    string bez = ResolveFieldByConfigOrName(d, fieldNames, null, "bezeichnung", "name", "titel", "baustelle");
                    if (!string.IsNullOrWhiteSpace(num) && !string.IsNullOrWhiteSpace(bez))
                    {
                        map[num.Trim()] = bez.Trim();
                    }
                }
            }
            catch
            {
                // optional enrichment only
            }
            return map;
        }

        // =====================================================================
        // PHASE 2: Comstruct → MetaSonic (Checked Invoices + Stamped PDF + externalStatus)
        // =====================================================================
        private static void RunPhase2ComstructToMetasonic(
            ComstructApiClient comstruct,
            ScanviewWebserviceClient client,
            ref LoginData loginData,
            WorkerState state,
            string statePath,
            bool dry)
        {
            var qs = DmsEnvironment.QueueStatus();
            var processedStatus = DmsEnvironment.ProcessedExternalStatus();
            var initialWatermark = state.LastComstructCheckAt ?? DmsEnvironment.DmsWorkerInitialUpdatedAfter();
            var epochIso = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToString("o", CultureInfo.InvariantCulture);

            Console.WriteLine("--- PHASE 2: Comstruct → MetaSonic (status=" + qs + ", lastCheck=" + (initialWatermark ?? "initial") + ") ---");

            var fromApi = comstruct.ListInvoiceRowsForQueue();
            var fromApiCount = fromApi.Count;
            var rows = FilterUnprocessedSinceWatermark(fromApi, initialWatermark, processedStatus);
            rows = SortByCursor(rows);

            Console.WriteLine("  Comstruct queue: fetched=" + fromApiCount + ", pendingToSync=" + rows.Count);
            if (rows.Count == 0)
            {
                return;
            }

            if (dry)
            {
                foreach (var r in rows)
                {
                    Console.WriteLine("  [dry-run] Would append stamped PDF for Comstruct invoice " + GetStr(r, "id") + " to MetaSonic doc " + GetStr(r, "external_id"));
                }
                return;
            }

            var maxCursor = string.IsNullOrEmpty(initialWatermark) ? epochIso : initialWatermark;
            foreach (var row in rows)
            {
                var advanceCursor = ProcessOneCheckedInvoice(comstruct, client, ref loginData, row, processedStatus);
                if (advanceCursor)
                {
                    var c = InvoiceCursorTimestamp(row);
                    maxCursor = MaxIso(maxCursor, c);
                    state.LastComstructCheckAt = maxCursor;
                    SaveState(statePath, state);
                }
            }
        }

        private static List<Dictionary<string, object>> FilterUnprocessedSinceWatermark(
            List<Dictionary<string, object>> rows,
            string watermarkIso,
            string processedExternalStatus)
        {
            DateTime? wmUtc = ParseFlexibleDateUtc(watermarkIso);

            return rows
                .Where(r =>
                {
                    var ext = (GetStr(r, "external_status") ?? string.Empty).Trim();
                    if (string.Equals(ext, processedExternalStatus, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(ext, "SYNCED", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    // If an invoice previously FAILED, retry it even if updated_at hasn't changed
                    if (string.Equals(ext, "FAILED", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    if (wmUtc.HasValue)
                    {
                        var rowTime = ParseFlexibleDateUtc(InvoiceCursorTimestamp(row: r));
                        if (rowTime.HasValue && rowTime.Value < wmUtc.Value)
                        {
                            return false;
                        }
                    }

                    return true;
                })
                .ToList();
        }

        private static bool ProcessOneCheckedInvoice(
            ComstructApiClient comstruct,
            ScanviewWebserviceClient client,
            ref LoginData loginData,
            Dictionary<string, object> row,
            string processedExternalStatus)
        {
            var invoiceId = GetStr(row, "id");
            if (string.IsNullOrEmpty(invoiceId))
            {
                return true;
            }

            var correlationValue = MetasonicCorrelationValue(row);
            var correlationQualId = DmsEnvironment.MetasonicComstructQualifierId();
            var allowCreate = DmsEnvironment.DmsWorkerAllowCreateIfNotFound();

            if (string.IsNullOrWhiteSpace(correlationValue) && !allowCreate)
            {
                Console.WriteLine("  Invoice " + invoiceId + ": no " + DmsEnvironment.MetasonicDmsCorrelationKey() + " set, skipping.");
                return true;
            }

            Console.WriteLine("  [Syncing Checked Invoice " + invoiceId + "] -> MetaSonic Doc=" + (correlationValue ?? "new"));

            var download = comstruct.DownloadInvoicePdf(invoiceId);
            if (download == null || download.Item1 == null || download.Item1.Length == 0)
            {
                Console.WriteLine("    No PDF available yet from Comstruct — will retry on next run.");
                return false;
            }

            var pdf = download.Item1;
            var detectedExt = MetasonicUploadHelper.DetectFileExtension(pdf);

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
                    }
                    else if (!allowCreate)
                    {
                        metasonicDocId = correlationValue;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(correlationValue))
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

                    var created = MetasonicUploadHelper.SaveNewDocument(
                        client,
                        ref loginData,
                        fields,
                        pdf,
                        fileName,
                        DmsEnvironment.MetasonicDocClassId());
                    metasonicDocId = created != null ? created.ID : null;
                    Console.WriteLine("    Created MetaSonic document " + metasonicDocId + " with stamped PDF '" + fileName + "'.");
                }
                else
                {
                    var fileName = BuildPdfFileName(client, ref loginData, metasonicDocId, row, invoiceId, detectedExt);
                    MetasonicUploadHelper.AppendDocumentPdfItem(
                        client,
                        ref loginData,
                        metasonicDocId,
                        pdf,
                        fileName);
                    Console.WriteLine("    Appended stamped PDF '" + fileName + "' to MetaSonic document " + metasonicDocId + ".");
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
                    }
                    catch (Exception qEx)
                    {
                        Console.WriteLine("    Warning: could not update qualifiers: " + qEx.Message);
                    }
                }

                PatchDms(comstruct, invoiceId, processedExternalStatus, null);
                Console.WriteLine("    Marked Comstruct invoice " + invoiceId + " external_status=" + processedExternalStatus + ".");

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("    ERROR syncing invoice " + invoiceId + ": " + ex.Message);
                try
                {
                    PatchDms(comstruct, invoiceId, "FAILED", ex.Message);
                }
                catch
                {
                    // ignored
                }

                return false;
            }
        }

        // =====================================================================
        // State Persistence & Helper Methods
        // =====================================================================
        private static WorkerState LoadState(string statePath)
        {
            var state = new WorkerState();
            if (!File.Exists(statePath)) return state;

            try
            {
                var raw = File.ReadAllText(statePath, Encoding.UTF8);
                var st = Json.DeserializeObject(raw) as Dictionary<string, object>;
                if (st == null) return state;

                if (st.ContainsKey("lastMetasonicCheckAt") && st["lastMetasonicCheckAt"] != null)
                {
                    state.LastMetasonicCheckAt = Convert.ToString(st["lastMetasonicCheckAt"]);
                }
                if (st.ContainsKey("lastComstructCheckAt") && st["lastComstructCheckAt"] != null)
                {
                    state.LastComstructCheckAt = Convert.ToString(st["lastComstructCheckAt"]);
                }
                else if (st.ContainsKey("lastCheckedAt") && st["lastCheckedAt"] != null)
                {
                    state.LastComstructCheckAt = Convert.ToString(st["lastCheckedAt"]);
                }

                if (st.ContainsKey("importedMetasonicDocIds") && st["importedMetasonicDocIds"] is System.Collections.IEnumerable)
                {
                    var list = (System.Collections.IEnumerable)st["importedMetasonicDocIds"];
                    foreach (var item in list)
                    {
                        var s = Convert.ToString(item);
                        if (!string.IsNullOrWhiteSpace(s))
                        {
                            state.ImportedMetasonicDocIds.Add(s.Trim());
                        }
                    }
                }
            }
            catch
            {
                // ignore corrupted state file
            }

            return state;
        }

        private static void SaveState(string statePath, WorkerState state)
        {
            try
            {
                var stateOut = new Dictionary<string, object>();
                stateOut["lastMetasonicCheckAt"] = state.LastMetasonicCheckAt;
                stateOut["lastComstructCheckAt"] = state.LastComstructCheckAt;
                stateOut["lastCheckedAt"] = state.LastComstructCheckAt;
                stateOut["importedMetasonicDocIds"] = state.ImportedMetasonicDocIds.ToArray();

                var parent = Path.GetDirectoryName(statePath);
                if (!string.IsNullOrEmpty(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                File.WriteAllText(statePath, Json.Serialize(stateOut), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Warning: could not persist state file '" + statePath + "': " + ex.Message);
            }
        }

        private static ScanviewWebserviceClient CreateMetasonicClient()
        {
            var binding = new BasicHttpBinding
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
            binding.Security.Mode = BasicHttpSecurityMode.None;

            return new ScanviewWebserviceClient(binding, new EndpointAddress(DmsEnvironment.MetasonicEndpoint()));
        }

        private static Dictionary<string, string> LoadDocClassFieldNames(
            ScanviewWebserviceClient client,
            ref LoginData loginData,
            string docClassId)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var dc = client.GetDocClassWithFieldProperties(ref loginData, docClassId);
                if (dc != null && dc.FieldList != null)
                {
                    foreach (var f in dc.FieldList)
                    {
                        if (!string.IsNullOrEmpty(f.ID))
                        {
                            map[f.ID] = f.Name ?? string.Empty;
                        }
                    }
                }
            }
            catch
            {
                // optional
            }
            return map;
        }

        private static ResponseDocument[] SearchMetasonicDocuments(
            ScanviewWebserviceClient client,
            ref LoginData loginData,
            string docClassId,
            Dictionary<string, string> fieldNames,
            int maxResults)
        {
            var ops = new RequestSearchOperators
            {
                MaxResults = maxResults,
                OrSearch = true,
                NoLikeSearch = false,
                RecursiveDocClassSearch = true
            };

            var docs = client.Search(
                ref loginData,
                docClassId,
                DmsEnvironment.MetasonicSearchUserFolderId(),
                string.Empty,
                new RequestField[0],
                ops,
                loadItemList: false,
                smallDocument: false,
                smallFieldList: false) ?? new ResponseDocument[0];

            if (docs.Length == 0 && fieldNames.Count > 0)
            {
                var firstFieldId = fieldNames.Keys.First();
                docs = client.Search(
                    ref loginData,
                    docClassId,
                    DmsEnvironment.MetasonicSearchUserFolderId(),
                    string.Empty,
                    new[] { new RequestField { ID = firstFieldId, Value = "*" } },
                    ops,
                    loadItemList: false,
                    smallDocument: false,
                    smallFieldList: false) ?? new ResponseDocument[0];
            }

            return docs;
        }

        private static string GetFieldVal(ResponseDocument doc, string qualifierId)
        {
            if (doc == null || doc.FieldList == null || string.IsNullOrWhiteSpace(qualifierId)) return null;
            var f = doc.FieldList.FirstOrDefault(x => string.Equals(x.ID, qualifierId.Trim(), StringComparison.OrdinalIgnoreCase));
            return f != null && !string.IsNullOrWhiteSpace(f.Value) ? f.Value.Trim() : null;
        }

        private static string ResolveFieldByConfigOrName(
            ResponseDocument doc,
            Dictionary<string, string> fieldNames,
            string configuredQualifierId,
            params string[] nameKeywords)
        {
            if (!string.IsNullOrWhiteSpace(configuredQualifierId))
            {
                var direct = GetFieldVal(doc, configuredQualifierId);
                if (!string.IsNullOrWhiteSpace(direct)) return direct;
            }

            if (doc == null || doc.FieldList == null) return null;
            foreach (var kw in nameKeywords)
            {
                foreach (var f in doc.FieldList)
                {
                    if (string.IsNullOrWhiteSpace(f.Value)) continue;
                    string fn;
                    fieldNames.TryGetValue(f.ID ?? "", out fn);
                    if (!string.IsNullOrEmpty(fn) && fn.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return f.Value.Trim();
                    }
                }
            }

            return null;
        }

        private static DateTime? GetDocumentTimestampUtc(ResponseDocument doc)
        {
            if (doc == null) return null;
            var mod = ParseFlexibleDateUtc(doc.ModificationDate);
            if (mod.HasValue) return mod;
            return ParseFlexibleDateUtc(doc.CreationDate);
        }

        private static DateTime? ParseFlexibleDateUtc(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            DateTime dt;
            if (DateTime.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out dt))
            {
                return dt;
            }
            if (DateTime.TryParse(value.Trim(), CultureInfo.GetCultureInfo("de-DE"), DateTimeStyles.AssumeLocal, out dt))
            {
                return dt.ToUniversalTime();
            }
            return null;
        }

        private static string FormatDateIso(string rawDate)
        {
            var dt = ParseFlexibleDateUtc(rawDate);
            return dt.HasValue ? dt.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
        }

        private static double? ParseAmount(string rawAmount)
        {
            if (string.IsNullOrWhiteSpace(rawAmount)) return null;
            var s = rawAmount.Trim();
            if (s.Contains(",") && s.Contains("."))
            {
                s = s.Replace(".", "").Replace(",", ".");
            }
            else
            {
                s = s.Replace(",", ".");
            }

            double d;
            if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out d))
            {
                return d;
            }
            return null;
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
                .OrderBy(r =>
                {
                    var dt = ParseFlexibleDateUtc(InvoiceCursorTimestamp(r));
                    return dt ?? DateTime.MinValue;
                })
                .ToList();
        }

        private static string MaxIso(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b;
            if (string.IsNullOrEmpty(b)) return a;

            var da = ParseFlexibleDateUtc(a);
            var db = ParseFlexibleDateUtc(b);
            if (da.HasValue && db.HasValue)
            {
                return da.Value >= db.Value ? a : b;
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
                        var firstName = first.OriginalFileName;
                        if (!string.IsNullOrWhiteSpace(firstName))
                        {
                            var baseName = Path.GetFileNameWithoutExtension(firstName);
                            return SanitizeFilePart(baseName) + "_geprueft_von_comstruct" + sourceExt;
                        }
                    }
                }
                catch
                {
                    // use fallback filename
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
    }
}
