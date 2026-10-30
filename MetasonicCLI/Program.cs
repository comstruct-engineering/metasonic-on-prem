using System;
using System.Collections.Generic;
using System.Linq;
using DmsFormular1.ScanviewWebservice;
using MetasonicDocumentUploadPoC;

namespace MetasonicCLI
{
    /// <summary>
    /// CLI tool for updating Metasonic DMS document qualifiers
    /// </summary>
    class Program
    {
        static string ResolveMetasonicEndpointCli()
        {
            string endpoint = Environment.GetEnvironmentVariable("METASONIC_ENDPOINT");
            if (!string.IsNullOrWhiteSpace(endpoint))
            {
                return endpoint.Trim();
            }

            return "http://127.0.0.1:8089/ScanviewWebservice.svc";
        }

        static string CliOrEnv(Dictionary<string, string> options, string optionKey, string envVarName)
        {
            if (options != null && options.TryGetValue(optionKey, out string fromCli) &&
                !string.IsNullOrWhiteSpace(fromCli))
            {
                return fromCli;
            }

            return Environment.GetEnvironmentVariable(envVarName)?.Trim();
        }

        // Common qualifier IDs
        private const string QUALIFIER_KREDITOR = "89900207";
        private const string QUALIFIER_BELEGNR = "89900202";
        private const string QUALIFIER_BETRAG = "89900131";
        private const string QUALIFIER_SACHLICH = "223";
        private const string QUALIFIER_PREISLICH = "222";
        private const string QUALIFIER_IS_SPLIT = "234";

        static int Main(string[] args)
        {
            DmsEnvironment.LoadDotEnvIfPresent();

            if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
            {
                ShowHelp();
                return 0;
            }

            try
            {
                var command = args[0].ToLower();

                switch (command)
                {
                    case "get":
                        return HandleGet(args);
                    case "update":
                        return HandleUpdate(args);
                    case "list":
                        return HandleList(args);
                    case "search":
                        return HandleSearch(args);
                    case "worker":
                    case "dms-worker":
                        return DmsUploadWorker.Run();
                    case "masterdata":
                    case "brz-sync":
                        return BrzMasterDataWorker.Run();
                    case "db-test":
                    case "brz-test":
                        return BrzMasterDataWorker.TestConnection();
                    case "db-tables":
                    case "brz-tables":
                        return BrzMasterDataWorker.ListTables(args.Length > 1 ? args[1] : null);
                    case "db-query":
                    case "brz-query":
                    case "brz-read":
                        return BrzMasterDataWorker.RunQuery(
                            args.Length > 1 ? args[1] : null,
                            args.Length > 2 ? args[2] : null);
                    case "dms-login":
                    case "dms-test":
                    case "login-test":
                        return MetasonicInspectHelper.DiagnoseLogin();
                    case "dms-classes":
                    case "docclasses":
                    case "inspect-classes":
                        return MetasonicInspectHelper.InspectDocClasses(args.Length > 1 ? args[1] : null);
                    case "dms-inspect":
                    case "inspect-doc":
                    case "accounting":
                        return MetasonicInspectHelper.InspectDocumentAndAccounting(args.Length > 1 ? args[1] : null);
                    case "dms-list":
                    case "dms-elements":
                        return MetasonicInspectHelper.ListDocClassElements(
                            args.Length > 1 ? args[1] : "89035003",
                            args.Length > 2 ? args[2] : null,
                            syncToComstruct: false);
                    case "dms-sync-projects":
                    case "dms-sync-kst":
                        return MetasonicInspectHelper.ListDocClassElements(
                            args.Length > 1 ? args[1] : "89035003",
                            null,
                            syncToComstruct: true);
                    case "odata":
                    case "odata-query":
                        return BrzOdataClient.QueryEndpoint(
                            args.Length > 1 ? args[1] : "Kostenstellen",
                            args.Length > 2 ? args[2] : null);
                    case "odata-sync":
                        return BrzOdataClient.SyncToComstruct(args.Length > 1 ? args[1] : "Kostenstellen");
                    default:
                        Console.WriteLine($"Unknown command: {command}");
                        Console.WriteLine("Use --help to see available commands");
                        return 1;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Details: {ex.InnerException.Message}");
                }
                return 1;
            }
        }

        static void ShowHelp()
        {
            Console.WriteLine(@"
Metasonic DMS CLI - Document Qualifier Update Tool

USAGE:
  MetasonicCLI <command> [options]

COMMANDS:
  get <docid>                          Get all qualifiers for a document
  update <docid> <qualifier> <value>   Update a single qualifier
  list <docid>                         List all qualifiers (same as get)
  search <correlationValue>             Find DMS doc by correlation field (e.g. external_id; see METASONIC_DMS_CORRELATION_KEY)
  worker                               Run Comstruct → MetaSonic DMS batch (see env in DocumentUploadPoC)
  masterdata                           Run BRZ DB → Comstruct master data sync (suppliers, projects, accounts, tax codes, payment terms)
  db-test                              Test read-only BRZ database connection (BRZ_DB_CONNECTION_STRING)
  db-tables [filter]                   List tables/views accessible to the read-only BRZ user
  db-query "<sql>" [output.csv]        Execute a read-only SQL query on BRZ and optionally export to CSV
  dms-classes [docClassId]             List MetaSonic document classes, qualifier IDs/names, mandatory flags, SQL lookups & accounting classes
  dms-inspect [docId]                  Inspect a MetaSonic document (all qualifiers with names, files, and GetAccountingTable Kontierung rows)
  odata [endpoint] [out.csv|out.json]  Query local BRZ OData/REST Stammdaten service (e.g. Kostenstellen, Baustellen) and optionally export
  odata-sync [endpoint]                Sync Kostenstellen/Baustellen from BRZ OData service to Comstruct projects

OPTIONS:
  --env <test|live>      Reserved for future use; endpoint is METASONIC_ENDPOINT
  --user <username>      Username (or METASONIC_USER)
  --pass <password>      Password (or METASONIC_PASSWORD)
  --db <database>        Database name (or METASONIC_DB)

COMMON QUALIFIERS:
  kreditor    89900207   Creditor/Vendor name
  belegnr     89900202   Invoice/Document number
  betrag      89900131   Amount (net)
  sachlich    223        Material verification (richtig/falsch/ungeprüft)
  preislich   222        Price verification (richtig/falsch/ungeprüft)
  issplit     234        Split document indicator (JA/NEIN)

EXAMPLES:
  # Get all qualifiers for document 1527018
  MetasonicCLI get 1527018

  # Update Sachlich status to ""richtig""
  MetasonicCLI update 1527018 223 richtig

  # Update using qualifier name
  MetasonicCLI update 1527018 sachlich richtig

  # Update on live environment
  MetasonicCLI update 1527018 sachlich richtig --env live

  # List all qualifiers
  MetasonicCLI list 1527018
");
        }

        static int HandleGet(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Error: Document ID required");
                Console.WriteLine("Usage: MetasonicCLI get <docid>");
                return 1;
            }

            string docId = args[1];
            var options = ParseOptions(args.Skip(2).ToArray());

            Console.WriteLine($"Fetching document {docId}...");

            var client = CreateClient(options);
            var loginData = Authenticate(client, options);
            var document = client.GetDocument(ref loginData, docId, false, false, true, false);

            if (document == null)
            {
                Console.WriteLine("Document not found");
                return 1;
            }

            Console.WriteLine($"\nDocument ID: {document.ID}");
            Console.WriteLine("Qualifiers:");
            Console.WriteLine(new string('-', 80));
            Console.WriteLine($"{"ID",-15} {"Name",-20} {"Value",-40}");
            Console.WriteLine(new string('-', 80));

            foreach (var field in document.FieldList.Where(f => !string.IsNullOrEmpty(f.Value)))
            {
                string name = GetQualifierName(field.ID);
                Console.WriteLine($"{field.ID,-15} {name,-20} {field.Value,-40}");
            }

            return 0;
        }

        static int HandleList(string[] args)
        {
            return HandleGet(args);
        }

        static int HandleSearch(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Error: Correlation value required (value stored in MetaSonic qualifier, e.g. external_id)");
                Console.WriteLine("Usage: MetasonicCLI search <correlationValue> [options]");
                Console.WriteLine("Requires METASONIC_COMSTRUCT_INVOICE_QUALIFIER_ID, METASONIC_DOC_CLASS_ID, METASONIC_SEARCH_USER_FOLDER_ID");
                return 1;
            }

            var correlationValue = args[1];
            var options = ParseOptions(args.Skip(2).ToArray());
            var qual = DmsEnvironment.MetasonicComstructQualifierId();
            if (string.IsNullOrWhiteSpace(qual))
            {
                Console.WriteLine("Error: Set environment variable METASONIC_COMSTRUCT_INVOICE_QUALIFIER_ID");
                return 1;
            }

            var client = CreateClient(options);
            var loginData = Authenticate(client, options);
            var found = MetasonicSearchHelper.FindExistingByCorrelationValue(
                client,
                ref loginData,
                correlationValue,
                qual,
                DmsEnvironment.MetasonicDocClassId(),
                DmsEnvironment.MetasonicSearchUserFolderId());

            if (found == null || string.IsNullOrEmpty(found.ID))
            {
                Console.WriteLine($"No document found for correlation value: {correlationValue}");
                return 2;
            }

            Console.WriteLine($"DMS document id: {found.ID}");
            return 0;
        }

        static int HandleUpdate(string[] args)
        {
            if (args.Length < 4)
            {
                Console.WriteLine("Error: Missing arguments");
                Console.WriteLine("Usage: MetasonicCLI update <docid> <qualifier> <value>");
                return 1;
            }

            string docId = args[1];
            string qualifierIdOrName = args[2];
            string value = args[3];
            var options = ParseOptions(args.Skip(4).ToArray());

            // Convert qualifier name to ID if needed
            string qualifierId = ResolveQualifierId(qualifierIdOrName);

            Console.WriteLine($"Updating document {docId}...");
            Console.WriteLine($"  Qualifier: {qualifierId} ({GetQualifierName(qualifierId)})");
            Console.WriteLine($"  New value: {value}");

            var client = CreateClient(options);
            var loginData = Authenticate(client, options);

            // Validate document exists
            var document = client.GetDocument(ref loginData, docId, false, false, true, false);
            if (document == null)
            {
                Console.WriteLine("Error: Document not found");
                return 1;
            }

            // Show current value
            var currentField = document.FieldList.FirstOrDefault(f => f.ID == qualifierId);
            if (currentField != null)
            {
                Console.WriteLine($"  Current value: {currentField.Value}");
            }

            // Update qualifier
            var fields = new List<RequestField> {
                new RequestField { ID = qualifierId, Value = value }
            };
            client.UpdateDocumentFields(ref loginData, docId, fields.ToArray());

            Console.WriteLine("✓ Update successful");

            // Verify update
            var updatedDoc = client.GetDocument(ref loginData, docId, false, false, true, false);
            var updatedField = updatedDoc.FieldList.FirstOrDefault(f => f.ID == qualifierId);
            if (updatedField != null)
            {
                Console.WriteLine($"  Verified value: {updatedField.Value}");
            }

            return 0;
        }

        static ScanviewWebserviceClient CreateClient(Dictionary<string, string> _)
        {
            return new ScanviewWebserviceClient(
                "ScanviewWebserviceBasicHTTP",
                ResolveMetasonicEndpointCli()
            );
        }

        static LoginData Authenticate(ScanviewWebserviceClient client, Dictionary<string, string> options)
        {
            string username = CliOrEnv(options, "user", "METASONIC_USER");
            string password = CliOrEnv(options, "pass", "METASONIC_PASSWORD");
            string database = CliOrEnv(options, "db", "METASONIC_DB");
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password) ||
                string.IsNullOrEmpty(database))
            {
                throw new InvalidOperationException(
                    "Missing MetaSonic credentials. Pass --user, --pass, and --db, or set METASONIC_USER, METASONIC_PASSWORD, METASONIC_DB."
                );
            }

            var loginData = new LoginData
            {
                AutoLogin = true,
                NoSession = true,
                UseSSO = false,
                Username = username,
                Password = password,
                LoginDatabaseName = database
            };

            client.Login(ref loginData);
            return loginData;
        }

        static Dictionary<string, string> ParseOptions(string[] args)
        {
            var options = new Dictionary<string, string>();

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith("--") && i + 1 < args.Length)
                {
                    string key = args[i].Substring(2).ToLower();
                    string value = args[i + 1];
                    options[key] = value;
                    i++;
                }
            }

            return options;
        }

        static string ResolveQualifierId(string qualifierIdOrName)
        {
            return qualifierIdOrName.ToLower() switch
            {
                "kreditor" => QUALIFIER_KREDITOR,
                "belegnr" => QUALIFIER_BELEGNR,
                "betrag" => QUALIFIER_BETRAG,
                "sachlich" => QUALIFIER_SACHLICH,
                "preislich" => QUALIFIER_PREISLICH,
                "issplit" => QUALIFIER_IS_SPLIT,
                _ => qualifierIdOrName // Assume it's already an ID
            };
        }

        static string GetQualifierName(string qualifierId)
        {
            return qualifierId switch
            {
                QUALIFIER_KREDITOR => "Kreditor",
                QUALIFIER_BELEGNR => "Belegnummer",
                QUALIFIER_BETRAG => "Betrag",
                QUALIFIER_SACHLICH => "Sachlich",
                QUALIFIER_PREISLICH => "Preislich",
                QUALIFIER_IS_SPLIT => "IsSplit",
                _ => ""
            };
        }
    }
}
