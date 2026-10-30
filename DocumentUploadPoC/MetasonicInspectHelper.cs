using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.ServiceModel;
using DmsFormular1.ScanviewWebservice;

namespace MetasonicDocumentUploadPoC
{
    /// <summary>
    /// Diagnostic helper to inspect the live MetaSonic DMS configuration via WCF.
    /// </summary>
    internal static class MetasonicInspectHelper
    {
        private static ScanviewWebserviceClient CreateClient(
            BasicHttpSecurityMode secMode = BasicHttpSecurityMode.None,
            HttpClientCredentialType credType = HttpClientCredentialType.None,
            string winUser = null,
            string winPass = null,
            string winDomain = null)
        {
            var ep = DmsEnvironment.MetasonicEndpoint();
            if (string.IsNullOrWhiteSpace(ep))
            {
                ep = "http://127.0.0.1:8089/ScanviewWebservice.svc";
            }

            var binding = new BasicHttpBinding
            {
                MaxReceivedMessageSize = int.MaxValue,
                MaxBufferSize = int.MaxValue,
                SendTimeout = TimeSpan.FromMinutes(2),
                ReceiveTimeout = TimeSpan.FromMinutes(2),
                ReaderQuotas = new System.Xml.XmlDictionaryReaderQuotas
                {
                    MaxDepth = 32,
                    MaxStringContentLength = int.MaxValue,
                    MaxArrayLength = int.MaxValue,
                    MaxBytesPerRead = int.MaxValue,
                    MaxNameTableCharCount = 16384
                }
            };
            binding.Security.Mode = secMode;
            binding.Security.Transport.ClientCredentialType = credType;

            var client = new ScanviewWebserviceClient(binding, new EndpointAddress(ep));
            if (!string.IsNullOrEmpty(winUser) && client.ClientCredentials != null)
            {
                client.ClientCredentials.Windows.ClientCredential = string.IsNullOrEmpty(winDomain)
                    ? new NetworkCredential(winUser, winPass)
                    : new NetworkCredential(winUser, winPass, winDomain);
                client.ClientCredentials.UserName.UserName = winUser;
                client.ClientCredentials.UserName.Password = winPass;
            }

            return client;
        }

        private static LoginData Authenticate(ScanviewWebserviceClient client)
        {
            var u = DmsEnvironment.MetasonicUser();
            var p = DmsEnvironment.MetasonicPassword();
            var d = DmsEnvironment.MetasonicDatabase();

            if (string.IsNullOrWhiteSpace(u) || string.IsNullOrWhiteSpace(p) || string.IsNullOrWhiteSpace(d))
            {
                throw new InvalidOperationException(
                    "Missing MetaSonic credentials. Set METASONIC_USER, METASONIC_PASSWORD, and METASONIC_DB in config.env.");
            }

            var loginData = new LoginData
            {
                AutoLogin = true,
                NoSession = true,
                UseSSO = false,
                Username = u,
                Password = p,
                LoginDatabaseName = d
            };

            try
            {
                client.Login(ref loginData);
                return loginData;
            }
            catch (FaultException<ServerFaultException> sfe)
            {
                var detailMsg = sfe.Detail != null ? ("ErrorCode=" + sfe.Detail.ErrorCode + ", ErrorMessage='" + sfe.Detail.ErrorMessage + "'") : sfe.Message;
                throw new InvalidOperationException("MetaSonic Login Fault (" + detailMsg + ") [User='" + u + "', DB='" + d + "', PassLen=" + p.Length + "]", sfe);
            }
        }

        public static int DiagnoseLogin()
        {
            Console.WriteLine("=== MetaSonic Login Diagnostics ===\n");
            var ep = DmsEnvironment.MetasonicEndpoint();
            var u = DmsEnvironment.MetasonicUser();
            var p = DmsEnvironment.MetasonicPassword();
            var d = DmsEnvironment.MetasonicDatabase();

            var maskedPass = string.IsNullOrEmpty(p)
                ? "(empty)"
                : (p.Length <= 2 ? "**" : (p[0] + new string('*', p.Length - 2) + p[p.Length - 1] + " (contains %: " + p.Contains("%") + ")"));

            Console.WriteLine("Endpoint:    " + ep);
            Console.WriteLine("Username:    '" + u + "'");
            Console.WriteLine("Password:    " + maskedPass + " [Len=" + (p != null ? p.Length : 0) + "]");
            Console.WriteLine("Database:    '" + d + "'");
            Console.WriteLine("Machine/Dom: " + Environment.UserDomainName + "\\" + Environment.UserName + "\n");

            var client = CreateClient();

            try
            {
                var ver = client.Version();
                Console.WriteLine("Server Version(): " + ver);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Version() check failed: " + ex.Message);
            }

            string[] configs = new string[0];
            try
            {
                configs = client.GetLoginConfigurations() ?? new string[0];
                Console.WriteLine("Available LoginConfigurations (" + configs.Length + "): " + string.Join(", ", configs));
            }
            catch (Exception ex)
            {
                Console.WriteLine("GetLoginConfigurations() failed: " + ex.Message);
            }

            Console.WriteLine("\n--- 1. Testing Username / Config / DB Combinations ---");

            var userCandidates = new List<string> { u };
            if (!string.IsNullOrEmpty(u) && u != u.ToUpperInvariant())
            {
                userCandidates.Add(u.ToUpperInvariant());
            }
            var dom = Environment.UserDomainName;
            if (!string.IsNullOrEmpty(u) && !u.Contains("\\") && !u.Contains("@"))
            {
                if (!string.IsNullOrEmpty(dom) && !string.Equals(dom, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
                {
                    userCandidates.Add(dom + "\\" + u);
                }
            }

            foreach (var usr in userCandidates)
            {
                // A: Only LoginConfigurationName = DefaultConfig (LoginDatabaseName = null)
                if (TryLoginAttempt(client, "User='" + usr + "', Config='DefaultConfig', DB=null", new LoginData
                {
                    AutoLogin = true,
                    NoSession = true,
                    UseSSO = false,
                    Username = usr,
                    Password = p,
                    LoginConfigurationName = "DefaultConfig",
                    LoginDatabaseName = null
                })) return 0;

                // B: LoginDatabaseName = d (from config.env)
                if (TryLoginAttempt(client, "User='" + usr + "', Config=null, DB='" + d + "'", new LoginData
                {
                    AutoLogin = true,
                    NoSession = true,
                    UseSSO = false,
                    Username = usr,
                    Password = p,
                    LoginConfigurationName = null,
                    LoginDatabaseName = d
                })) return 0;

                // C: Both Config='DefaultConfig' and DB=d
                if (TryLoginAttempt(client, "User='" + usr + "', Config='DefaultConfig', DB='" + d + "'", new LoginData
                {
                    AutoLogin = true,
                    NoSession = true,
                    UseSSO = false,
                    Username = usr,
                    Password = p,
                    LoginConfigurationName = "DefaultConfig",
                    LoginDatabaseName = d
                })) return 0;
            }

            // Also test common BRZ/Metasonic database names in case DefaultConfig points to a named DB
            string[] commonDbNames = new[] { "Metasonic", "METASONIC", "Scanview", "SCANVIEW", "BRZ", "BRZ365", "BRZ8", "DMS", "Archive", "brzdb" };
            Console.WriteLine("\n--- 2. Testing Common MetaSonic Database Names ---");
            foreach (var dbName in commonDbNames)
            {
                if (TryLoginAttempt(client, "User='" + u + "', Config='DefaultConfig', DB='" + dbName + "'", new LoginData
                {
                    AutoLogin = true,
                    NoSession = true,
                    UseSSO = false,
                    Username = u,
                    Password = p,
                    LoginConfigurationName = "DefaultConfig",
                    LoginDatabaseName = dbName
                })) return 0;
            }

            Console.WriteLine("\n--- 3. Testing Windows / AD Transport Auth (UseSSO=true) ---");
            foreach (var credType in new[] { HttpClientCredentialType.Windows, HttpClientCredentialType.Ntlm })
            {
                try
                {
                    var winClient = CreateClient(BasicHttpSecurityMode.TransportCredentialOnly, credType, u, p, dom);
                    if (TryLoginAttempt(winClient, "TransportCredentialOnly(" + credType + ") + UseSSO=true, Config='DefaultConfig'", new LoginData
                    {
                        AutoLogin = true,
                        NoSession = true,
                        UseSSO = true,
                        Username = u,
                        Password = p,
                        LoginConfigurationName = "DefaultConfig",
                        LoginDatabaseName = d
                    })) return 0;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("  [SKIP] TransportCredentialOnly(" + credType + "): " + ex.Message);
                }
            }

            return 1;
        }

        private static bool TryLoginAttempt(ScanviewWebserviceClient client, string label, LoginData ld)
        {
            try
            {
                client.Login(ref ld);
                Console.WriteLine("  [SUCCESS!] " + label + " -> SessionID=" + ld.SessionID + " | Resolved DB='" + ld.LoginDatabaseName + "' | Config='" + ld.LoginConfigurationName + "'");
                return true;
            }
            catch (FaultException<ServerFaultException> sfe)
            {
                var detail = sfe.Detail != null ? ("Code=" + sfe.Detail.ErrorCode + ": " + sfe.Detail.ErrorMessage) : sfe.Message;
                Console.WriteLine("  [FAILED]  " + label + " -> " + detail);
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine("  [FAILED]  " + label + " -> " + ex.Message);
                return false;
            }
        }

        public static int InspectDocClasses(string specificDocClassId = null)
        {
            Console.WriteLine("=== MetaSonic DMS Inspection: Document Classes, Qualifiers & Accounting ===\n");
            try
            {
                var client = CreateClient();
                var loginData = Authenticate(client);
                Console.WriteLine("Connected to " + DmsEnvironment.MetasonicEndpoint() + " (DB: " + DmsEnvironment.MetasonicDatabase() + ")\n");

                if (!string.IsNullOrWhiteSpace(specificDocClassId))
                {
                    PrintSingleDocClassDetails(client, ref loginData, specificDocClassId.Trim());
                }
                else
                {
                    var docClasses = client.GetDocClasses(ref loginData, loadComplete: true, withPermissions: false, withTranslations: true)
                        ?? new ResponseDocClass[0];

                    Console.WriteLine("Found " + docClasses.Length + " top-level document class(es):\n");
                    foreach (var dc in docClasses)
                    {
                        PrintDocClassTree(client, ref loginData, dc, 0);
                    }
                }

                Console.WriteLine("\n=== Accounting / Kontierung Document Classes (GetAccountingDocClasses) ===\n");
                try
                {
                    var accClasses = client.GetAccountingDocClasses(ref loginData) ?? new ResponseAccountingDocClass[0];
                    if (accClasses.Length == 0)
                    {
                        Console.WriteLine("No dedicated accounting document classes returned by GetAccountingDocClasses.");
                    }
                    else
                    {
                        foreach (var ac in accClasses)
                        {
                            Console.WriteLine("Accounting DocClass ID: " + ac.ID + " | Name: " + ac.Name);
                            if (ac.FieldList != null && ac.FieldList.Length > 0)
                            {
                                Console.WriteLine(string.Format("  {0,-15} {1,-35}", "Field ID", "Field Name"));
                                Console.WriteLine("  " + new string('-', 52));
                                foreach (var f in ac.FieldList)
                                {
                                    Console.WriteLine(string.Format("  {0,-15} {1,-35}", f.ID, f.Name));
                                }
                            }
                            Console.WriteLine();
                        }
                    }
                }
                catch (Exception accEx)
                {
                    Console.WriteLine("  Note: GetAccountingDocClasses returned error: " + accEx.Message);
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Inspection failed: " + ex.Message);
                return 1;
            }
        }

        private static void PrintDocClassTree(
            ScanviewWebserviceClient client,
            ref LoginData loginData,
            ResponseDocClass dc,
            int indent)
        {
            if (dc == null) return;
            var pad = new string(' ', indent * 2);
            Console.WriteLine(pad + "DocClass ID: " + dc.ID + " | Name: " + dc.Name + " (Parent: " + (dc.ParentDocClassID ?? "-") + ")");

            ResponseField[] fields = dc.FieldList;
            try
            {
                var detailed = client.GetDocClassWithFieldProperties(ref loginData, dc.ID);
                if (detailed != null && detailed.FieldList != null && detailed.FieldList.Length > 0)
                {
                    fields = detailed.FieldList;
                }
            }
            catch
            {
                // fall back to dc.FieldList
            }

            if (fields != null && fields.Length > 0)
            {
                Console.WriteLine(string.Format("{0}  {1,-14} {2,-32} {3,-10} {4,-10} {5}", pad, "Qualifier ID", "Name", "Mandatory", "ReadOnly", "SQL / Lookup Info"));
                Console.WriteLine(pad + "  " + new string('-', 95));
                foreach (var f in fields)
                {
                    var prop = f.IndexingProperties ?? f.CreatingProperties ?? f.SearchingProperties;
                    string mandatory = prop != null ? (prop.Mandatory ? "YES" : "no") : "-";
                    string readOnly = prop != null ? (prop.ReadOnly ? "YES" : "no") : "-";
                    string sqlInfo = FormatSqlLookupInfo(prop != null ? prop.WordlistSQLInfo : null);
                    Console.WriteLine(string.Format("{0}  {1,-14} {2,-32} {3,-10} {4,-10} {5}", pad, f.ID, f.Name ?? "", mandatory, readOnly, sqlInfo));
                }
            }

            Console.WriteLine();

            if (dc.ChildClassList != null)
            {
                foreach (var child in dc.ChildClassList)
                {
                    PrintDocClassTree(client, ref loginData, child, indent + 1);
                }
            }
        }

        private static void PrintSingleDocClassDetails(
            ScanviewWebserviceClient client,
            ref LoginData loginData,
            string docClassId)
        {
            var dc = client.GetDocClassWithFieldProperties(ref loginData, docClassId);
            if (dc == null)
            {
                Console.WriteLine("Document class '" + docClassId + "' not found.");
                return;
            }

            PrintDocClassTree(client, ref loginData, dc, 0);
        }

        private static string FormatSqlLookupInfo(ResponseWordListSQLInfo sqlInfo)
        {
            if (sqlInfo == null) return string.Empty;
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(sqlInfo.OdbcDSN))
                parts.Add("DSN=" + sqlInfo.OdbcDSN);
            if (!string.IsNullOrWhiteSpace(sqlInfo.SqlStatement))
                parts.Add("SQL=\"" + sqlInfo.SqlStatement.Replace("\r", " ").Replace("\n", " ").Trim() + "\"");
            if (!string.IsNullOrWhiteSpace(sqlInfo.ColumnDestinations))
                parts.Add("Dest=" + sqlInfo.ColumnDestinations);
            return string.Join(" | ", parts);
        }

        public static int InspectDocumentAndAccounting(string docId = null)
        {
            Console.WriteLine("=== MetaSonic Document, Qualifier & Accounting (Kontierung) Inspector ===\n");
            try
            {
                var client = CreateClient();
                var loginData = Authenticate(client);
                Console.WriteLine("Connected to " + DmsEnvironment.MetasonicEndpoint() + " (DB: " + DmsEnvironment.MetasonicDatabase() + ")\n");

                if (string.IsNullOrWhiteSpace(docId))
                {
                    var docClassId = DmsEnvironment.MetasonicDocClassId();
                    Console.WriteLine("No DocID specified — searching for recent documents in DocClass " + docClassId + "...");
                    var ops = new RequestSearchOperators
                    {
                        MaxResults = 5,
                        OrSearch = true,
                        NoLikeSearch = false
                    };
                    var sampleDocs = client.Search(
                        ref loginData,
                        docClassId,
                        DmsEnvironment.MetasonicSearchUserFolderId(),
                        string.Empty,
                        new RequestField[0],
                        ops,
                        loadItemList: true,
                        smallDocument: false,
                        smallFieldList: false) ?? new ResponseDocument[0];

                    if (sampleDocs.Length == 0)
                    {
                        Console.WriteLine("No documents returned by empty search in DocClass " + docClassId + ". Pass a specific <docId> to inspect.");
                        return 0;
                    }

                    Console.WriteLine("Found " + sampleDocs.Length + " sample document(s). Inspecting first document ID=" + sampleDocs[0].ID + ":\n");
                    docId = sampleDocs[0].ID;
                }

                var doc = client.GetDocument(ref loginData, docId, loadItemData: false, smallDocument: false, smallFieldList: false, mergeBackgroundImage: false);
                if (doc == null)
                {
                    Console.WriteLine("Document '" + docId + "' not found.");
                    return 1;
                }

                var fieldNames = LoadFieldNamesForDocClass(client, ref loginData, doc.DocClassID);

                Console.WriteLine("Document ID:   " + doc.ID);
                Console.WriteLine("DocClass ID:   " + doc.DocClassID);
                Console.WriteLine("Creation Date: " + doc.CreationDate + " (by " + doc.CreatorName + ")");
                Console.WriteLine("Modified Date: " + doc.ModificationDate + " (by " + doc.ModificatorName + ")");
                Console.WriteLine("In Workflow:   " + doc.InWorkflow + " (Done: " + doc.InWorkflowDone + ")\n");

                Console.WriteLine("1. Qualifiers / Fields (All fields on document):");
                Console.WriteLine(new string('-', 90));
                Console.WriteLine(string.Format("{0,-15} {1,-35} {2,-38}", "Qualifier ID", "Field Name (from DocClass)", "Current Value"));
                Console.WriteLine(new string('-', 90));

                if (doc.FieldList != null)
                {
                    foreach (var f in doc.FieldList)
                    {
                        string fName;
                        fieldNames.TryGetValue(f.ID ?? string.Empty, out fName);
                        Console.WriteLine(string.Format("{0,-15} {1,-35} {2,-38}", f.ID, fName ?? "", f.Value ?? ""));
                    }
                }

                Console.WriteLine("\n2. Attached Files / Items (GetItemList):");
                Console.WriteLine(new string('-', 90));
                var items = client.GetItemList(ref loginData, docId, false, false) ?? doc.ItemList ?? new ResponseItem[0];
                if (items.Length == 0)
                {
                    Console.WriteLine("  (No items attached)");
                }
                else
                {
                    foreach (var it in items)
                    {
                        Console.WriteLine("  Item ID: " + it.ID + " | Name: '" + it.ItemName + "' | OriginalFileName: '" + it.OriginalFileName + "' | Ext: '" + it.FileExtension + "' | Created: " + it.CreationDate);
                    }
                }

                Console.WriteLine("\n3. Accounting / Kontierung Table (GetAccountingTable):");
                Console.WriteLine(new string('-', 90));
                try
                {
                    var accTable = client.GetAccountingTable(ref loginData, docId, withFields: true);
                    if (accTable == null)
                    {
                        Console.WriteLine("  (No accounting table returned for this document)");
                    }
                    else
                    {
                        Console.WriteLine("  CategoryDocClassID: " + accTable.CategoryDocClassID);
                        var accFieldNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        if (accTable.Fields != null && accTable.Fields.Length > 0)
                        {
                            Console.WriteLine("  Accounting Table Columns:");
                            foreach (var af in accTable.Fields)
                            {
                                accFieldNames[af.ID ?? string.Empty] = af.Name ?? string.Empty;
                                Console.WriteLine("    - FieldID " + af.ID + ": " + af.Name);
                            }
                        }

                        if (accTable.Rows == null || accTable.Rows.Length == 0)
                        {
                            Console.WriteLine("  Accounting Rows: 0 row(s)");
                        }
                        else
                        {
                            Console.WriteLine("  Accounting Rows (" + accTable.Rows.Length + "):");
                            foreach (var r in accTable.Rows)
                            {
                                var cols = (r.Columns ?? new ResponseAccountingValue[0])
                                    .Select(c =>
                                    {
                                        string colName;
                                        accFieldNames.TryGetValue(c.FieldID ?? string.Empty, out colName);
                                        var label = string.IsNullOrEmpty(colName) ? c.FieldID : (c.FieldID + "(" + colName + ")");
                                        return label + "='" + c.Value + "'";
                                    });
                                Console.WriteLine("    Row " + r.Row + ": " + string.Join(", ", cols));
                            }
                        }
                    }
                }
                catch (Exception accEx)
                {
                    Console.WriteLine("  (GetAccountingTable not active or returned: " + accEx.Message + ")");
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Document inspection failed: " + ex.Message);
                return 1;
            }
        }

        private static Dictionary<string, string> LoadFieldNamesForDocClass(
            ScanviewWebserviceClient client,
            ref LoginData loginData,
            string docClassId)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(docClassId)) return map;

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
                // ignore if user has no permission to read doc class definition
            }

            return map;
        }

        /// <summary>
        /// Lists all documents/elements in a specific MetaSonic DocClass (e.g. 89035003 Kostenstellen),
        /// displays their fields/qualifiers, and optionally exports them to a CSV file or syncs them to Comstruct.
        /// </summary>
        public static int ListDocClassElements(string docClassId, string csvOutputPath = null, bool syncToComstruct = false)
        {
            Console.WriteLine("=== MetaSonic DocClass Elements Listing (DocClass=" + docClassId + ") ===\n");
            if (string.IsNullOrWhiteSpace(docClassId))
            {
                Console.WriteLine("ERROR: DocClassID is required (e.g. 89035003).");
                return 1;
            }

            try
            {
                var client = CreateClient();
                var loginData = Authenticate(client);
                Console.WriteLine("Connected to " + DmsEnvironment.MetasonicEndpoint() + " (DB: " + DmsEnvironment.MetasonicDatabase() + ")\n");

                PrintSingleDocClassDetails(client, ref loginData, docClassId.Trim());

                var fieldNames = LoadFieldNamesForDocClass(client, ref loginData, docClassId.Trim());

                var ops = new RequestSearchOperators
                {
                    MaxResults = 5000,
                    OrSearch = true,
                    NoLikeSearch = false,
                    RecursiveDocClassSearch = true
                };

                var docs = client.Search(
                    ref loginData,
                    docClassId.Trim(),
                    string.Empty,
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
                        docClassId.Trim(),
                        string.Empty,
                        string.Empty,
                        new[] { new RequestField { ID = firstFieldId, Value = "*" } },
                        ops,
                        loadItemList: false,
                        smallDocument: false,
                        smallFieldList: false) ?? new ResponseDocument[0];
                }

                Console.WriteLine("Found " + docs.Length + " element(s) in DocClass " + docClassId + ":\n");
                if (docs.Length == 0)
                {
                    return 0;
                }

                var allFieldIds = new List<string>(fieldNames.Keys);
                foreach (var d in docs)
                {
                    if (d.FieldList == null) continue;
                    foreach (var f in d.FieldList)
                    {
                        if (!string.IsNullOrEmpty(f.ID) && !allFieldIds.Contains(f.ID))
                        {
                            allFieldIds.Add(f.ID);
                        }
                    }
                }

                var headerLabels = new List<string> { "DocID" };
                foreach (var fid in allFieldIds)
                {
                    string fn;
                    fieldNames.TryGetValue(fid, out fn);
                    headerLabels.Add(string.IsNullOrEmpty(fn) ? fid : (fid + ":" + fn));
                }

                Console.WriteLine(string.Join(" | ", headerLabels));
                Console.WriteLine(new string('-', 100));

                int maxPreview = Math.Min(100, docs.Length);
                for (int i = 0; i < maxPreview; i++)
                {
                    var d = docs[i];
                    var rowVals = new List<string> { d.ID ?? "" };
                    foreach (var fid in allFieldIds)
                    {
                        string val = "";
                        if (d.FieldList != null)
                        {
                            var match = d.FieldList.FirstOrDefault(x => x.ID == fid);
                            if (match != null && match.Value != null) val = match.Value;
                        }
                        rowVals.Add(val);
                    }
                    Console.WriteLine(string.Join(" | ", rowVals));
                }

                if (docs.Length > maxPreview)
                {
                    Console.WriteLine("... (" + (docs.Length - maxPreview) + " more element(s) omitted from console preview)");
                }

                if (!string.IsNullOrWhiteSpace(csvOutputPath))
                {
                    var fullPath = System.IO.Path.GetFullPath(csvOutputPath);
                    var dir = System.IO.Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);

                    using (var sw = new System.IO.StreamWriter(fullPath, false, new System.Text.UTF8Encoding(false)))
                    {
                        sw.WriteLine(string.Join(";", headerLabels.ConvertAll(EscapeCsv)));
                        foreach (var d in docs)
                        {
                            var rowVals = new List<string> { EscapeCsv(d.ID ?? "") };
                            foreach (var fid in allFieldIds)
                            {
                                string val = "";
                                if (d.FieldList != null)
                                {
                                    var match = d.FieldList.FirstOrDefault(x => x.ID == fid);
                                    if (match != null && match.Value != null) val = match.Value;
                                }
                                rowVals.Add(EscapeCsv(val));
                            }
                            sw.WriteLine(string.Join(";", rowVals));
                        }
                    }
                    Console.WriteLine("\nExported " + docs.Length + " element(s) to CSV: " + fullPath);
                }

                if (syncToComstruct)
                {
                    var dry = DmsEnvironment.DmsWorkerDryRun();
                    Console.WriteLine("\nSyncing " + docs.Length + " element(s) from DocClass " + docClassId + " to Comstruct (dryRun=" + dry + ")...");
                    using (var comstruct = new ComstructApiClient())
                    {
                        int synced = 0;
                        foreach (var d in docs)
                        {
                            if (d.FieldList == null) continue;
                            string kstNr = null;
                            string bez = null;
                            string betrNr = null;

                            foreach (var f in d.FieldList)
                            {
                                if (string.IsNullOrWhiteSpace(f.Value)) continue;
                                string fn;
                                fieldNames.TryGetValue(f.ID ?? "", out fn);
                                var label = (fn ?? "").ToLowerInvariant();
                                var val = f.Value.Trim();

                                if (kstNr == null && (label.Contains("kostenstelle") || label.Contains("kst") || label.Contains("nummer") || label.Contains("nr") || label.Contains("code")))
                                {
                                    kstNr = val;
                                }
                                else if (bez == null && (label.Contains("bezeichnung") || label.Contains("name") || label.Contains("titel") || label.Contains("baustelle") || label.Contains("text")))
                                {
                                    bez = val;
                                }
                                else if (betrNr == null && (label.Contains("betrieb") || label.Contains("mandant") || label.Contains("firma")))
                                {
                                    betrNr = val;
                                }
                            }

                            if (string.IsNullOrEmpty(kstNr))
                            {
                                var nonEmpty = d.FieldList.Where(x => !string.IsNullOrWhiteSpace(x.Value)).ToList();
                                if (nonEmpty.Count > 0) kstNr = nonEmpty[0].Value.Trim();
                                if (nonEmpty.Count > 1 && string.IsNullOrEmpty(bez)) bez = nonEmpty[1].Value.Trim();
                            }

                            if (string.IsNullOrEmpty(kstNr)) continue;
                            if (string.IsNullOrEmpty(bez)) bez = kstNr;

                            var proj = new Dictionary<string, object>();
                            proj["external_id"] = kstNr;
                            proj["project_number"] = kstNr;
                            proj["title"] = bez;
                            proj["active"] = true;
                            if (!string.IsNullOrEmpty(betrNr)) proj["legal_entity_number"] = betrNr;

                            if (dry)
                            {
                                Console.WriteLine("  [dry-run] project_number='" + kstNr + "', title='" + bez + "'" + (betrNr != null ? ", legal_entity='" + betrNr + "'" : ""));
                            }
                            else
                            {
                                comstruct.UpsertProject(proj);
                            }
                            synced++;
                        }
                        Console.WriteLine("Synced " + synced + " project(s) to Comstruct.");
                    }
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to list DocClass elements: " + ex.Message);
                return 1;
            }
        }

        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var cleaned = value.Replace("\r", " ").Replace("\n", " ").Trim();
            if (cleaned.IndexOf(';') >= 0 || cleaned.IndexOf('"') >= 0)
            {
                return "\"" + cleaned.Replace("\"", "\"\"") + "\"";
            }

            return cleaned;
        }
    }
}
