using System;
using System.IO;

namespace MetasonicDocumentUploadPoC
{
    /// <summary>Environment variables for the Comstruct ↔ MetaSonic DMS upload worker and BRZ sync.</summary>
    internal static class DmsEnvironment
    {
        static DmsEnvironment()
        {
            LoadDotEnvIfPresent();
        }

        /// <summary>
        /// Loads <c>config.env</c> directly from the working directory or executable directory without
        /// cmd.exe variable expansion, so special characters like <c>%</c>, <c>!</c>, <c>&amp;</c>, or <c>=</c> in passwords
        /// are preserved verbatim.
        /// </summary>
        public static void LoadDotEnvIfPresent()
        {
            string[] candidates = new[]
            {
                Path.Combine(Environment.CurrentDirectory, "config.env"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.env"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..\\..\\config.env")
            };

            foreach (var path in candidates)
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    foreach (var rawLine in File.ReadAllLines(path))
                    {
                        var line = rawLine != null ? rawLine.Trim() : string.Empty;
                        if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

                        int eqIdx = line.IndexOf('=');
                        if (eqIdx <= 0) continue;

                        var key = line.Substring(0, eqIdx).Trim();
                        var val = line.Substring(eqIdx + 1).Trim();
                        if (!string.IsNullOrEmpty(key))
                        {
                            Environment.SetEnvironmentVariable(key, val, EnvironmentVariableTarget.Process);
                        }
                    }
                    break;
                }
                catch
                {
                    // ignore file read errors
                }
            }
        }
        public static string ComstructBaseUrl()
        {
            return (Environment.GetEnvironmentVariable("COMSTRUCT_NEST_URL") ?? "https://api.comstruct.com").TrimEnd('/');
        }

        public static string ComstructApiPrefix()
        {
            return (Environment.GetEnvironmentVariable("COMSTRUCT_API_PREFIX") ?? "v1").Trim('/');
        }

        public static string ComstructApiKey()
        {
            return Environment.GetEnvironmentVariable("COMSTRUCT_API_KEY") ?? string.Empty;
        }

        public static string QueueStatus()
        {
            return Environment.GetEnvironmentVariable("COMSTRUCT_DMS_QUEUE_STATUS") ?? "PRICE_CHECKING";
        }

        public static int ListLimit()
        {
            int n;
            return Math.Min(500, Math.Max(1, int.TryParse(Environment.GetEnvironmentVariable("COMSTRUCT_DMS_LIMIT"), out n) ? n : 50));
        }

        public static string UpdatedAfterParam()
        {
            return Environment.GetEnvironmentVariable("COMSTRUCT_DMS_UPDATED_AFTER_PARAM") ?? "updated_after";
        }

        public static string PatchPathTemplate()
        {
            return Environment.GetEnvironmentVariable("COMSTRUCT_DMS_PATCH_PATH") ?? "/{prefix}/invoices/{id}/external-status";
        }

        public static bool PdfAnnotations()
        {
            var v = Environment.GetEnvironmentVariable("COMSTRUCT_PDF_ANNOTATIONS");
            if (string.Equals("0", v, StringComparison.Ordinal) ||
                string.Equals("false", v, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// When true (default), requests <c>GET .../pdf?overlay=true</c> so match-status boxes are embedded in the PDF.
        /// Set <c>COMSTRUCT_PDF_OVERLAY=0</c> or <c>false</c> to omit the parameter.
        /// </summary>
        public static bool PdfOverlay()
        {
            var v = Environment.GetEnvironmentVariable("COMSTRUCT_PDF_OVERLAY");
            if (string.Equals("0", v, StringComparison.Ordinal) ||
                string.Equals("false", v, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        public static string MetasonicEndpoint()
        {
            return Environment.GetEnvironmentVariable("METASONIC_ENDPOINT") ?? string.Empty;
        }

        public static string MetasonicUser()
        {
            return Environment.GetEnvironmentVariable("METASONIC_USER") ?? string.Empty;
        }

        public static string MetasonicPassword()
        {
            return Environment.GetEnvironmentVariable("METASONIC_PASSWORD") ?? string.Empty;
        }

        public static string MetasonicDatabase()
        {
            return Environment.GetEnvironmentVariable("METASONIC_DB") ?? string.Empty;
        }

        public static string MetasonicDocClassId()
        {
            return Environment.GetEnvironmentVariable("METASONIC_DOC_CLASS_ID") ?? "1";
        }

        public static string MetasonicComstructQualifierId()
        {
            return Environment.GetEnvironmentVariable("METASONIC_COMSTRUCT_INVOICE_QUALIFIER_ID") ?? "";
        }

        public static string MetasonicSearchUserFolderId()
        {
            return Environment.GetEnvironmentVariable("METASONIC_SEARCH_USER_FOLDER_ID") ?? "";
        }

        /// <summary>
        /// <c>external_id</c> (default) or <c>id</c> — value used for MetaSonic <c>Search</c> and correlation qualifier (must match DMS).
        /// </summary>
        public static string MetasonicDmsCorrelationKey()
        {
            return string.Equals("id", Environment.GetEnvironmentVariable("METASONIC_DMS_CORRELATION_KEY"), StringComparison.OrdinalIgnoreCase)
                ? "id"
                : "external_id";
        }

        public static string DmsStateFile()
        {
            return Environment.GetEnvironmentVariable("DMS_WORKER_STATE_FILE")
                ?? System.IO.Path.Combine(Environment.CurrentDirectory, ".dms-worker-state.json");
        }

        public static string DmsWorkerInitialUpdatedAfter()
        {
            return Environment.GetEnvironmentVariable("DMS_WORKER_INITIAL_UPDATED_AFTER");
        }

        public static bool DmsWorkerDryRun()
        {
            return "1" == (Environment.GetEnvironmentVariable("DMS_WORKER_DRY_RUN") ?? "") ||
                string.Equals("true", Environment.GetEnvironmentVariable("DMS_WORKER_DRY_RUN"), StringComparison.OrdinalIgnoreCase);
        }

        public static bool DmsWorkerAllowCreateIfNotFound()
        {
            return "1" == (Environment.GetEnvironmentVariable("DMS_WORKER_ALLOW_CREATE_DMS_DOCUMENT") ?? "") ||
                string.Equals("true", Environment.GetEnvironmentVariable("DMS_WORKER_ALLOW_CREATE_DMS_DOCUMENT"), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Optional MetaSonic qualifier ID for the boolean sync flag.
        /// When set, writes "Ja" after a successful upload.
        /// </summary>
        public static string MetasonicComstructSyncedQualifierId()
        {
            return Environment.GetEnvironmentVariable("METASONIC_COMSTRUCT_SYNCED_QUALIFIER_ID") ?? string.Empty;
        }

        /// <summary>
        /// Optional MetaSonic qualifier ID to store the Comstruct invoice URL.
        /// When set, writes "https://app.comstruct.com/invoices/{id}" after a successful upload.
        /// </summary>
        public static string MetasonicComstructUrlQualifierId()
        {
            return Environment.GetEnvironmentVariable("METASONIC_COMSTRUCT_URL_QUALIFIER_ID") ?? string.Empty;
        }

        /// <summary>
        /// Database provider for direct BRZ SQL connections: <c>sqlserver</c> (default), <c>odbc</c>, <c>oledb</c>, or <c>odata</c>.
        /// </summary>
        public static string BrzDbProvider()
        {
            return (Environment.GetEnvironmentVariable("BRZ_DB_PROVIDER") ?? "sqlserver").Trim().ToLowerInvariant();
        }

        public static string BrzOdataBaseUrl()
        {
            return (Environment.GetEnvironmentVariable("BRZ_ODATA_URL") ?? "http://127.0.0.1/api/stammdaten/").TrimEnd('/') + "/";
        }

        public static string BrzOdataUser()
        {
            return Environment.GetEnvironmentVariable("BRZ_ODATA_USER") ?? string.Empty;
        }

        public static string BrzOdataPassword()
        {
            return Environment.GetEnvironmentVariable("BRZ_ODATA_PASSWORD") ?? string.Empty;
        }

        public static string BrzOdataParent()
        {
            return Environment.GetEnvironmentVariable("BRZ_ODATA_PARENT") ?? string.Empty;
        }

        public static string BrzOdataCompanyNumber()
        {
            return Environment.GetEnvironmentVariable("BRZ_ODATA_BETRIEBSNUMMER") ?? string.Empty;
        }

        /// <summary>
        /// Connection string for the read-only BRZ database user (ADO.NET SqlClient, ODBC, or OleDb).
        /// </summary>
        public static string BrzDbConnectionString()
        {
            return Environment.GetEnvironmentVariable("BRZ_DB_CONNECTION_STRING") ?? string.Empty;
        }

        public static int BrzDbCommandTimeoutSeconds()
        {
            int n;
            return Math.Max(5, int.TryParse(Environment.GetEnvironmentVariable("BRZ_DB_TIMEOUT_SECONDS"), out n) ? n : 60);
        }

        public static string BrzSuppliersQuery()
        {
            return Environment.GetEnvironmentVariable("BRZ_SQL_SUPPLIERS_QUERY") ?? string.Empty;
        }

        public static string BrzProjectsQuery()
        {
            return Environment.GetEnvironmentVariable("BRZ_SQL_PROJECTS_QUERY") ?? string.Empty;
        }

        public static string BrzAccountsQuery()
        {
            return Environment.GetEnvironmentVariable("BRZ_SQL_ACCOUNTS_QUERY") ?? string.Empty;
        }

        public static string BrzTaxCodesQuery()
        {
            return Environment.GetEnvironmentVariable("BRZ_SQL_TAX_CODES_QUERY") ?? string.Empty;
        }

        public static string BrzPaymentTermsQuery()
        {
            return Environment.GetEnvironmentVariable("BRZ_SQL_PAYMENT_TERMS_QUERY") ?? string.Empty;
        }
    }
}
