using System;
using System.IO;

namespace MetasonicDocumentUploadPoC
{
    /// <summary>Environment variables for the Comstruct ↔ MetaSonic DMS sync worker.</summary>
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

        private static string Env(string key, string defaultVal = "")
        {
            var v = Environment.GetEnvironmentVariable(key);
            return string.IsNullOrWhiteSpace(v) ? defaultVal : v.Trim();
        }

        public static string ComstructBaseUrl()
        {
            return Env("COMSTRUCT_NEST_URL", "https://api.comstruct.com").TrimEnd('/');
        }

        public static string ComstructApiPrefix()
        {
            return Env("COMSTRUCT_API_PREFIX", "v1").Trim('/');
        }

        public static string ComstructApiKey()
        {
            return Env("COMSTRUCT_API_KEY");
        }

        public static string ComstructTenant()
        {
            return Env("COMSTRUCT_TENANT", "default");
        }

        public static string QueueStatus()
        {
            return Env("COMSTRUCT_DMS_QUEUE_STATUS", "CHECKED");
        }

        public static string ProcessedExternalStatus()
        {
            return Env("COMSTRUCT_DMS_PROCESSED_STATUS", "SYNCED");
        }

        public static int ListLimit()
        {
            int n;
            return Math.Min(500, Math.Max(1, int.TryParse(Env("COMSTRUCT_DMS_LIMIT"), out n) ? n : 100));
        }

        public static string UpdatedAfterParam()
        {
            return Env("COMSTRUCT_DMS_UPDATED_AFTER_PARAM", "updated_after");
        }

        public static string PatchPathTemplate()
        {
            return Env("COMSTRUCT_DMS_PATCH_PATH", "/{prefix}/invoices/{id}/external-status");
        }

        public static bool PdfAnnotations()
        {
            var v = Env("COMSTRUCT_PDF_ANNOTATIONS");
            if (string.Equals("0", v, StringComparison.Ordinal) ||
                string.Equals("false", v, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        public static bool PdfOverlay()
        {
            var v = Env("COMSTRUCT_PDF_OVERLAY");
            if (string.Equals("0", v, StringComparison.Ordinal) ||
                string.Equals("false", v, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        public static string MetasonicEndpoint()
        {
            return Env("METASONIC_ENDPOINT");
        }

        public static string MetasonicUser()
        {
            return Env("METASONIC_USER");
        }

        public static string MetasonicPassword()
        {
            return Env("METASONIC_PASSWORD");
        }

        public static string MetasonicDatabase()
        {
            return Env("METASONIC_DB");
        }

        public static string MetasonicDocClassId()
        {
            return Env("METASONIC_DOC_CLASS_ID", "1");
        }

        public static string MetasonicProjectsDocClassId()
        {
            return Env("METASONIC_PROJECTS_DOC_CLASS_ID", "89035003");
        }

        public static string MetasonicSuppliersDocClassId()
        {
            return Env("METASONIC_SUPPLIERS_DOC_CLASS_ID");
        }

        public static string MetasonicComstructQualifierId()
        {
            return Env("METASONIC_COMSTRUCT_INVOICE_QUALIFIER_ID");
        }

        public static string MetasonicSearchUserFolderId()
        {
            return Env("METASONIC_SEARCH_USER_FOLDER_ID");
        }

        public static int MetasonicScanMaxResults()
        {
            int n;
            return Math.Max(10, int.TryParse(Env("METASONIC_SCAN_MAX_RESULTS"), out n) ? n : 500);
        }

        public static bool MetasonicInboundSyncEnabled()
        {
            var v = Env("METASONIC_INBOUND_SYNC", "true");
            return !string.Equals("0", v, StringComparison.Ordinal) &&
                   !string.Equals("false", v, StringComparison.OrdinalIgnoreCase);
        }

        public static string QualifierCreditorNumber()
        {
            return Env("METASONIC_QUAL_CREDITOR_NUMBER", Env("METASONIC_QUAL_SUPPLIER_NUMBER"));
        }

        public static string QualifierCreditorName()
        {
            return Env("METASONIC_QUAL_CREDITOR_NAME", Env("METASONIC_QUAL_SUPPLIER_NAME", "89900207"));
        }

        public static string QualifierProjectNumber()
        {
            return Env("METASONIC_QUAL_PROJECT_NUMBER");
        }

        public static string QualifierProjectTitle()
        {
            return Env("METASONIC_QUAL_PROJECT_TITLE");
        }

        public static string QualifierInvoiceNumber()
        {
            return Env("METASONIC_QUAL_INVOICE_NUMBER", "89900202");
        }

        public static string QualifierInvoiceDate()
        {
            return Env("METASONIC_QUAL_INVOICE_DATE");
        }

        public static string QualifierDueDate()
        {
            return Env("METASONIC_QUAL_DUE_DATE");
        }

        public static string QualifierReceiptDate()
        {
            return Env("METASONIC_QUAL_RECEIPT_DATE");
        }

        public static string QualifierNetAmount()
        {
            return Env("METASONIC_QUAL_NET_AMOUNT", "89900131");
        }

        public static string QualifierGrossAmount()
        {
            return Env("METASONIC_QUAL_GROSS_AMOUNT");
        }

        public static string QualifierTaxAmount()
        {
            return Env("METASONIC_QUAL_TAX_AMOUNT");
        }

        public static string QualifierTenant()
        {
            return Env("METASONIC_QUAL_TENANT", "89000001");
        }

        public static string QualifierPaymentTerm()
        {
            return Env("METASONIC_QUAL_PAYMENT_TERM");
        }

        public static string QualifierAssignee()
        {
            return Env("METASONIC_QUAL_ASSIGNEE");
        }

        public static string QualifierComment()
        {
            return Env("METASONIC_QUAL_COMMENT");
        }

        public static string MetasonicDmsCorrelationKey()
        {
            return string.Equals("id", Env("METASONIC_DMS_CORRELATION_KEY"), StringComparison.OrdinalIgnoreCase)
                ? "id"
                : "external_id";
        }

        public static string DmsStateFile()
        {
            var v = Env("DMS_WORKER_STATE_FILE");
            return string.IsNullOrEmpty(v)
                ? Path.Combine(Environment.CurrentDirectory, ".dms-worker-state.json")
                : v;
        }

        public static string DmsWorkerInitialUpdatedAfter()
        {
            var v = Env("DMS_WORKER_INITIAL_UPDATED_AFTER");
            return string.IsNullOrEmpty(v) ? null : v;
        }

        public static bool DmsWorkerDryRun()
        {
            var v = Env("DMS_WORKER_DRY_RUN");
            return "1" == v || string.Equals("true", v, StringComparison.OrdinalIgnoreCase);
        }

        public static bool DmsWorkerAllowCreateIfNotFound()
        {
            var v = Env("DMS_WORKER_ALLOW_CREATE_DMS_DOCUMENT");
            return "1" == v || string.Equals("true", v, StringComparison.OrdinalIgnoreCase);
        }

        public static string MetasonicComstructSyncedQualifierId()
        {
            return Env("METASONIC_COMSTRUCT_SYNCED_QUALIFIER_ID");
        }

        public static string MetasonicComstructUrlQualifierId()
        {
            return Env("METASONIC_COMSTRUCT_URL_QUALIFIER_ID");
        }

        public static string BrzDbProvider()
        {
            return Env("BRZ_DB_PROVIDER", "sqlserver").ToLowerInvariant();
        }

        public static string BrzOdataBaseUrl()
        {
            return Env("BRZ_ODATA_URL", "http://127.0.0.1/api/stammdaten/").TrimEnd('/') + "/";
        }

        public static string BrzOdataUser()
        {
            return Env("BRZ_ODATA_USER");
        }

        public static string BrzOdataPassword()
        {
            return Env("BRZ_ODATA_PASSWORD");
        }

        public static string BrzOdataParent()
        {
            return Env("BRZ_ODATA_PARENT");
        }

        public static string BrzOdataCompanyNumber()
        {
            return Env("BRZ_ODATA_BETRIEBSNUMMER");
        }

        public static string BrzDbConnectionString()
        {
            return Env("BRZ_DB_CONNECTION_STRING");
        }

        public static int BrzDbCommandTimeoutSeconds()
        {
            int n;
            return Math.Max(5, int.TryParse(Env("BRZ_DB_TIMEOUT_SECONDS"), out n) ? n : 60);
        }

        public static string BrzSuppliersQuery()
        {
            return Env("BRZ_SQL_SUPPLIERS_QUERY");
        }

        public static string BrzProjectsQuery()
        {
            return Env("BRZ_SQL_PROJECTS_QUERY");
        }

        public static string BrzAccountsQuery()
        {
            return Env("BRZ_SQL_ACCOUNTS_QUERY");
        }

        public static string BrzTaxCodesQuery()
        {
            return Env("BRZ_SQL_TAX_CODES_QUERY");
        }

        public static string BrzPaymentTermsQuery()
        {
            return Env("BRZ_SQL_PAYMENT_TERMS_QUERY");
        }
    }
}
