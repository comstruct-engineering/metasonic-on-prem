using System;
using System.IO;
using System.Linq;
using DmsFormular1.ScanviewWebservice;

namespace MetasonicDocumentUploadPoC
{
    /// <summary>
    /// Proof of Concept: Document Upload to Metasonic DMS via WCF
    ///
    /// This demonstrates how to:
    /// 1. Authenticate with Metasonic Scanview DMS
    /// 2. Create a new document with metadata (qualifiers)
    /// 3. Upload a file (PDF, TIFF, etc.)
    /// 4. Retrieve the new document ID
    /// </summary>
    class Program
    {
        // Environment configuration — set METASONIC_* (see config.env.example).
        private static string MetasonicEndpointInteractive()
        {
            var endpoint = Environment.GetEnvironmentVariable("METASONIC_ENDPOINT");
            if (!string.IsNullOrWhiteSpace(endpoint))
            {
                return endpoint.Trim();
            }

            return "http://127.0.0.1:8089/ScanviewWebservice.svc";
        }

        // Document class IDs (these would need to be confirmed with Metasonic)
        private const string DOC_CLASS_INVOICE = "1"; // Example - needs verification

        // Qualifier IDs from analysis
        private const string QUALIFIER_KREDITOR = "89900207";  // Vendor/Creditor name
        private const string QUALIFIER_BELEGNR = "89900202";   // Invoice number
        private const string QUALIFIER_BETRAG = "89900131";    // Amount (net)
        private const string QUALIFIER_SACHLICH = "223";       // Material verification
        private const string QUALIFIER_PREISLICH = "222";      // Price verification

        static int Main(string[] args)
        {
            if (args.Length > 0)
            {
                var mode = (args[0] ?? string.Empty).ToLowerInvariant();
                if (mode == "worker" || mode == "dms-worker")
                {
                    return DmsUploadWorker.Run();
                }
            }

            Console.WriteLine("=== Metasonic DMS Document Upload - Proof of Concept ===\n");
            try
            {
                // Step 1: Initialize WCF client
                Console.WriteLine("Step 1: Initializing WCF client...");
                var endpointResolved = MetasonicEndpointInteractive();
                var client = CreateWcfClient();
                Console.WriteLine($"✓ Connected to: {endpointResolved}\n");

                // Step 2: Authenticate
                Console.WriteLine("Step 2: Authenticating...");
                var loginData = AuthenticateToMetasonic(client);
                Console.WriteLine($"✓ Authenticated as: {loginData.Username}\n");

                // Step 3: Prepare document metadata
                Console.WriteLine("Step 3: Preparing document metadata...");
                var documentMetadata = CreateDocumentMetadata(
                    docClassId: DOC_CLASS_INVOICE,
                    kreditor: "Test Supplier GmbH",
                    belegNr: "INV-2026-001",
                    betrag: "1250.50"
                );
                Console.WriteLine("✓ Metadata prepared\n");

                // Step 4: Prepare file for upload
                Console.WriteLine("Step 4: Preparing file for upload...");
                string testFilePath = GetTestFilePath(args);

                if (!File.Exists(testFilePath))
                {
                    Console.WriteLine($"⚠ Warning: Test file not found at {testFilePath}");
                    Console.WriteLine("Creating a dummy PDF for demonstration...");
                    testFilePath = CreateDummyPdf();
                }

                byte[] fileData = File.ReadAllBytes(testFilePath);
                string fileName = Path.GetFileName(testFilePath);
                Console.WriteLine($"✓ File loaded: {fileName} ({fileData.Length} bytes)\n");

                // Step 5: Create document in DMS
                Console.WriteLine("Step 5: Uploading document to Metasonic DMS...");
                var newDocument = UploadDocument(
                    client,
                    loginData,
                    documentMetadata,
                    fileData,
                    fileName
                );

                Console.WriteLine($"✓ Document uploaded successfully!");
                Console.WriteLine($"✓ New Document ID: {newDocument.ID}\n");

                // Step 6: Verify upload by retrieving the document
                Console.WriteLine("Step 6: Verifying upload...");
                var retrievedDoc = client.GetDocument(ref loginData, newDocument.ID, false, false, true, false);
                Console.WriteLine($"✓ Document retrieved: {retrievedDoc.ID}");
                Console.WriteLine($"  - Number of fields: {retrievedDoc.FieldList?.Length ?? 0}");
                Console.WriteLine($"  - Number of items: {retrievedDoc.ItemList?.Length ?? 0}");

                if (retrievedDoc.FieldList != null)
                {
                    Console.WriteLine("\n  Stored qualifiers:");
                    foreach (var field in retrievedDoc.FieldList.Where(f => !string.IsNullOrEmpty(f.Value)))
                    {
                        Console.WriteLine($"    - {field.ID}: {field.Value}");
                    }
                }

                Console.WriteLine("\n✓ SUCCESS: Document upload PoC completed successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n✗ ERROR: {ex.GetType().Name}");
                Console.WriteLine($"Message: {ex.Message}");

                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                }

                Console.WriteLine($"\nStack Trace:\n{ex.StackTrace}");
                return 1;
            }

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
            return 0;
        }

        /// <summary>
        /// Creates WCF client for Metasonic Scanview service
        /// </summary>
        private static ScanviewWebserviceClient CreateWcfClient()
        {
            string endpoint = MetasonicEndpointInteractive();

            // Create client with BasicHttpBinding
            var client = new ScanviewWebserviceClient(
                "ScanviewWebserviceBasicHTTP",
                endpoint
            );

            return client;
        }

        /// <summary>
        /// Authenticates to Metasonic DMS and returns login session data
        /// </summary>
        private static LoginData AuthenticateToMetasonic(ScanviewWebserviceClient client)
        {
            var username = Environment.GetEnvironmentVariable("METASONIC_USER");
            var password = Environment.GetEnvironmentVariable("METASONIC_PASSWORD");
            var database = Environment.GetEnvironmentVariable("METASONIC_DB");
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password) ||
                string.IsNullOrWhiteSpace(database))
            {
                throw new InvalidOperationException(
                    "Set METASONIC_USER, METASONIC_PASSWORD, and METASONIC_DB for the interactive PoC (Windows environment variables or your shell)."
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

            if (string.IsNullOrEmpty(loginData.SessionID))
            {
                throw new Exception("Authentication failed - no session ID returned");
            }

            return loginData;
        }

        /// <summary>
        /// Creates document metadata (qualifiers) for the new document
        /// </summary>
        private static RequestField[] CreateDocumentMetadata(
            string docClassId,
            string kreditor,
            string belegNr,
            string betrag)
        {
            return new[]
            {
                new RequestField { ID = QUALIFIER_KREDITOR, Value = kreditor },
                new RequestField { ID = QUALIFIER_BELEGNR, Value = belegNr },
                new RequestField { ID = QUALIFIER_BETRAG, Value = betrag },
                new RequestField { ID = QUALIFIER_SACHLICH, Value = "ungeprüft" },
                new RequestField { ID = QUALIFIER_PREISLICH, Value = "ungeprüft" }
            };
        }

        /// <summary>
        /// Uploads a document with file and metadata to Metasonic DMS
        /// </summary>
        private static ResponseDocument UploadDocument(
            ScanviewWebserviceClient client,
            LoginData loginData,
            RequestField[] metadata,
            byte[] fileData,
            string fileName)
        {
                var item = new RequestItem
                {
                    ItemData = fileData,
                    ItemName = "Document",
                    OriginalFileName = fileName,
                    ItemTypeNumber = MetasonicUploadHelper.GetItemTypeNumber(fileName)
                };

                // Create RequestDocument
                var document = new RequestDocument
                {
                    ID = null, // Null/empty for new documents - DMS will assign ID
                    DocClassID = DmsEnvironment.MetasonicDocClassId(),
                    MainUserFolderID = null, // Optional - can be left null
                    FieldList = metadata,
                    ItemList = new[] { item }
                };

                // Upload to DMS
                var response = client.SaveDocument(ref loginData, document);
                return response;
            }

        /// <summary>
        /// Gets test file path from command line args or uses default
        /// </summary>
        private static string GetTestFilePath(string[] args)
        {
            if (args.Length > 0 && File.Exists(args[0]))
            {
                return args[0];
            }

            // Default test file locations
            string[] defaultPaths = {
                @"C:\Temp\test.pdf",
                @".\test.pdf",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "test.pdf")
            };

            foreach (var path in defaultPaths)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return @".\test.pdf"; // Will trigger dummy file creation
        }

        /// <summary>
        /// Creates a minimal valid PDF file for testing purposes
        /// </summary>
        private static string CreateDummyPdf()
        {
            string filePath = Path.Combine(Path.GetTempPath(), "metasonic_test.pdf");

            // Minimal valid PDF content
            string pdfContent = @"%PDF-1.4
1 0 obj
<<
/Type /Catalog
/Pages 2 0 R
>>
endobj
2 0 obj
<<
/Type /Pages
/Kids [3 0 R]
/Count 1
>>
endobj
3 0 obj
<<
/Type /Page
/Parent 2 0 R
/Resources <<
/Font <<
/F1 <<
/Type /Font
/Subtype /Type1
/BaseFont /Helvetica
>>
>>
>>
/MediaBox [0 0 612 792]
/Contents 4 0 R
>>
endobj
4 0 obj
<<
/Length 44
>>
stream
BT
/F1 12 Tf
100 700 Td
(Metasonic Test PDF) Tj
ET
endstream
endobj
xref
0 5
0000000000 65535 f
0000000009 00000 n
0000000058 00000 n
0000000115 00000 n
0000000317 00000 n
trailer
<<
/Size 5
/Root 1 0 R
>>
startxref
410
%%EOF";

            File.WriteAllText(filePath, pdfContent);
            Console.WriteLine($"✓ Created dummy PDF at: {filePath}");
            return filePath;
        }
    }
}
