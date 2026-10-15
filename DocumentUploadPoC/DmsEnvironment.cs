using System;

namespace MetasonicDocumentUploadPoC
{
    /// <summary>Environment variables for the Comstruct ↔ MetaSonic DMS upload worker.</summary>
    internal static class DmsEnvironment
    {
        public static string ComstructBaseUrl() =>
            (Environment.GetEnvironmentVariable("COMSTRUCT_NEST_URL") ?? "https://api.comstruct.com").TrimEnd('/');

        public static string ComstructApiPrefix() =>
            (Environment.GetEnvironmentVariable("COMSTRUCT_API_PREFIX") ?? "v1").Trim('/');

        public static string ComstructApiKey() =>
            Environment.GetEnvironmentVariable("COMSTRUCT_API_KEY") ?? string.Empty;

        public static string QueueStatus() =>
            Environment.GetEnvironmentVariable("COMSTRUCT_DMS_QUEUE_STATUS") ?? "PRICE_CHECKING";

        public static int ListLimit() =>
            Math.Min(500, Math.Max(1, int.TryParse(Environment.GetEnvironmentVariable("COMSTRUCT_DMS_LIMIT"), out int n) ? n : 50));

        public static string UpdatedAfterParam() =>
            Environment.GetEnvironmentVariable("COMSTRUCT_DMS_UPDATED_AFTER_PARAM") ?? "updated_after";

        public static string PatchPathTemplate() =>
            Environment.GetEnvironmentVariable("COMSTRUCT_DMS_PATCH_PATH") ?? "/{prefix}/invoices/{id}/external-status";

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

        public static string MetasonicEndpoint() =>
            Environment.GetEnvironmentVariable("METASONIC_ENDPOINT") ?? string.Empty;

        public static string MetasonicUser() =>
            Environment.GetEnvironmentVariable("METASONIC_USER") ?? string.Empty;

        public static string MetasonicPassword() =>
            Environment.GetEnvironmentVariable("METASONIC_PASSWORD") ?? string.Empty;

        public static string MetasonicDatabase() =>
            Environment.GetEnvironmentVariable("METASONIC_DB") ?? string.Empty;

        public static string MetasonicDocClassId() =>
            Environment.GetEnvironmentVariable("METASONIC_DOC_CLASS_ID") ?? "1";

        public static string MetasonicComstructQualifierId() =>
            Environment.GetEnvironmentVariable("METASONIC_COMSTRUCT_INVOICE_QUALIFIER_ID") ?? "";

        public static string MetasonicSearchUserFolderId() =>
            Environment.GetEnvironmentVariable("METASONIC_SEARCH_USER_FOLDER_ID") ?? "";

        /// <summary>
        /// <c>external_id</c> (default) or <c>id</c> — value used for MetaSonic <c>Search</c> and correlation qualifier (must match DMS).
        /// </summary>
        public static string MetasonicDmsCorrelationKey() =>
            string.Equals("id", Environment.GetEnvironmentVariable("METASONIC_DMS_CORRELATION_KEY"), StringComparison.OrdinalIgnoreCase)
                ? "id"
                : "external_id";

        public static string DmsStateFile() =>
            Environment.GetEnvironmentVariable("DMS_WORKER_STATE_FILE")
            ?? System.IO.Path.Combine(Environment.CurrentDirectory, ".dms-worker-state.json");

        public static string DmsWorkerInitialUpdatedAfter() =>
            Environment.GetEnvironmentVariable("DMS_WORKER_INITIAL_UPDATED_AFTER");

        public static bool DmsWorkerDryRun() =>
            "1" == (Environment.GetEnvironmentVariable("DMS_WORKER_DRY_RUN") ?? "") ||
            string.Equals("true", Environment.GetEnvironmentVariable("DMS_WORKER_DRY_RUN"), StringComparison.OrdinalIgnoreCase);

        public static bool DmsWorkerAllowCreateIfNotFound() =>
            "1" == (Environment.GetEnvironmentVariable("DMS_WORKER_ALLOW_CREATE_DMS_DOCUMENT") ?? "") ||
            string.Equals("true", Environment.GetEnvironmentVariable("DMS_WORKER_ALLOW_CREATE_DMS_DOCUMENT"), StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Optional MetaSonic qualifier ID for the boolean sync flag.
        /// When set, writes "Ja" after a successful upload.
        /// </summary>
        public static string MetasonicComstructSyncedQualifierId() =>
            Environment.GetEnvironmentVariable("METASONIC_COMSTRUCT_SYNCED_QUALIFIER_ID") ?? string.Empty;

        /// <summary>
        /// Optional MetaSonic qualifier ID to store the Comstruct invoice URL.
        /// When set, writes "https://app.comstruct.com/invoices/{id}" after a successful upload.
        /// </summary>
        public static string MetasonicComstructUrlQualifierId() =>
            Environment.GetEnvironmentVariable("METASONIC_COMSTRUCT_URL_QUALIFIER_ID") ?? string.Empty;

    }
}
