using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.Odbc;
using System.Data.OleDb;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Text;

namespace MetasonicDocumentUploadPoC
{
    /// <summary>
    /// Connects to the local BRZ database (via read-only database user) and synchronizes
    /// master data (suppliers/creditors, projects/cost centers, G/L accounts, tax codes, payment terms)
    /// to the Comstruct REST API, or inspects/exports BRZ database tables.
    /// </summary>
    internal static class BrzMasterDataWorker
    {
        private const int SupplierBatchSize = 200;
        private const int AccountBatchSize = 500;

        public static int TestConnection()
        {
            Console.WriteLine("=== BRZ Database Connection Test ===\n");
            var provider = DmsEnvironment.BrzDbProvider();
            var connStr = DmsEnvironment.BrzDbConnectionString();

            if (string.IsNullOrWhiteSpace(connStr))
            {
                Console.WriteLine("ERROR: BRZ_DB_CONNECTION_STRING is not set in config.env or environment.");
                return 1;
            }

            Console.WriteLine("Provider: " + provider);
            try
            {
                using (var conn = CreateConnection(provider, connStr))
                {
                    conn.Open();
                    Console.WriteLine("Connected successfully to database '" + conn.Database + "' on server '" + conn.DataSource + "'.");

                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandTimeout = DmsEnvironment.BrzDbCommandTimeoutSeconds();
                        cmd.CommandText = "SELECT 1";
                        var result = cmd.ExecuteScalar();
                        Console.WriteLine("Test query ('SELECT 1') returned: " + result);
                    }
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Database connection failed: " + ex.Message);
                return 1;
            }
        }

        public static int ListTables(string filterTerm = null)
        {
            Console.WriteLine("=== BRZ Database Schema Discovery (Tables & Views) ===\n");
            var provider = DmsEnvironment.BrzDbProvider();
            var connStr = DmsEnvironment.BrzDbConnectionString();

            if (string.IsNullOrWhiteSpace(connStr))
            {
                Console.WriteLine("ERROR: BRZ_DB_CONNECTION_STRING is not set in config.env or environment.");
                return 1;
            }

            try
            {
                using (var conn = CreateConnection(provider, connStr))
                {
                    conn.Open();
                    Console.WriteLine("Connected to '" + conn.Database + "' on '" + conn.DataSource + "'.\n");

                    DataTable schemaTable = conn.GetSchema("Tables");
                    int count = 0;

                    Console.WriteLine(string.Format("{0,-20} {1,-45} {2,-15}", "Schema", "Table / View Name", "Type"));
                    Console.WriteLine(new string('-', 82));

                    foreach (DataRow row in schemaTable.Rows)
                    {
                        string schema = GetSchemaCol(row, "TABLE_SCHEMA", "OWNER");
                        string name = GetSchemaCol(row, "TABLE_NAME");
                        string type = GetSchemaCol(row, "TABLE_TYPE");

                        if (string.IsNullOrEmpty(name)) continue;
                        if (!string.IsNullOrWhiteSpace(filterTerm) &&
                            name.IndexOf(filterTerm, StringComparison.OrdinalIgnoreCase) < 0 &&
                            schema.IndexOf(filterTerm, StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }

                        Console.WriteLine(string.Format("{0,-20} {1,-45} {2,-15}", schema, name, type));
                        count++;
                    }

                    Console.WriteLine("\nTotal matching tables/views: " + count);
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to list tables: " + ex.Message);
                return 1;
            }
        }

        public static int RunQuery(string sql, string csvOutputPath = null, int maxPreviewRows = 25)
        {
            Console.WriteLine("=== BRZ Read Query ===\n");
            var provider = DmsEnvironment.BrzDbProvider();
            var connStr = DmsEnvironment.BrzDbConnectionString();

            if (string.IsNullOrWhiteSpace(connStr))
            {
                Console.WriteLine("ERROR: BRZ_DB_CONNECTION_STRING is not set in config.env or environment.");
                return 1;
            }

            if (string.IsNullOrWhiteSpace(sql))
            {
                Console.WriteLine("ERROR: SQL query is required.");
                return 1;
            }

            try
            {
                using (var conn = CreateConnection(provider, connStr))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandTimeout = DmsEnvironment.BrzDbCommandTimeoutSeconds();
                        cmd.CommandText = sql;

                        using (var reader = cmd.ExecuteReader())
                        {
                            int fieldCount = reader.FieldCount;
                            var headers = new string[fieldCount];
                            for (int i = 0; i < fieldCount; i++)
                            {
                                headers[i] = reader.GetName(i) ?? ("Col" + i);
                            }

                            StreamWriter csvWriter = null;
                            try
                            {
                                if (!string.IsNullOrWhiteSpace(csvOutputPath))
                                {
                                    var dir = Path.GetDirectoryName(Path.GetFullPath(csvOutputPath));
                                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                                    csvWriter = new StreamWriter(csvOutputPath, false, new UTF8Encoding(false));
                                    csvWriter.WriteLine(string.Join(";", Array.ConvertAll(headers, EscapeCsvField)));
                                }

                                Console.WriteLine("Columns: " + string.Join(" | ", headers));
                                Console.WriteLine(new string('-', 80));

                                int rowCount = 0;
                                while (reader.Read())
                                {
                                    rowCount++;
                                    var vals = new string[fieldCount];
                                    for (int i = 0; i < fieldCount; i++)
                                    {
                                        vals[i] = reader.IsDBNull(i)
                                            ? string.Empty
                                            : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture) ?? string.Empty;
                                    }

                                    if (csvWriter != null)
                                    {
                                        csvWriter.WriteLine(string.Join(";", Array.ConvertAll(vals, EscapeCsvField)));
                                    }

                                    if (rowCount <= maxPreviewRows)
                                    {
                                        Console.WriteLine(string.Join(" | ", vals));
                                    }
                                }

                                if (rowCount > maxPreviewRows)
                                {
                                    Console.WriteLine("... (" + (rowCount - maxPreviewRows) + " more row(s) omitted from console preview)");
                                }

                                Console.WriteLine("\nTotal rows read: " + rowCount);
                                if (csvWriter != null)
                                {
                                    Console.WriteLine("Exported to CSV: " + Path.GetFullPath(csvOutputPath));
                                }
                            }
                            finally
                            {
                                if (csvWriter != null)
                                {
                                    csvWriter.Dispose();
                                }
                            }
                        }
                    }
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Query failed: " + ex.Message);
                return 1;
            }
        }

        public static int Run()
        {
            var dry = DmsEnvironment.DmsWorkerDryRun();
            var provider = DmsEnvironment.BrzDbProvider();
            var connStr = DmsEnvironment.BrzDbConnectionString();

            Console.WriteLine("=== BRZ → Comstruct Master Data Sync Worker (.NET) ===\n");
            Console.WriteLine("Comstruct base=" + DmsEnvironment.ComstructBaseUrl() + " dbProvider=" + provider + " dryRun=" + dry + "\n");

            if (string.IsNullOrWhiteSpace(connStr))
            {
                Console.WriteLine("ERROR: BRZ_DB_CONNECTION_STRING is not set. Configure your read-only BRZ database connection in config.env.");
                return 1;
            }

            if (string.IsNullOrWhiteSpace(DmsEnvironment.ComstructApiKey()) && !dry)
            {
                Console.WriteLine("ERROR: COMSTRUCT_API_KEY is not set.");
                return 1;
            }

            try
            {
                using (var conn = CreateConnection(provider, connStr))
                using (var comstruct = new ComstructApiClient())
                {
                    Console.WriteLine("[1/6] Opening BRZ database connection...");
                    conn.Open();
                    Console.WriteLine("[1/6] Connected to '" + conn.Database + "' (" + conn.DataSource + ").\n");

                    int errors = 0;
                    errors += SyncTaxCodes(conn, comstruct, dry);
                    errors += SyncPaymentTerms(conn, comstruct, dry);
                    errors += SyncAccounts(conn, comstruct, dry);
                    errors += SyncSuppliers(conn, comstruct, dry);
                    errors += SyncProjects(conn, comstruct, dry);

                    Console.WriteLine("\n=== Master data sync finished. ===");
                    return errors == 0 ? 0 : 1;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("\nERROR: " + ex.Message);
                return 1;
            }
        }

        private static string GetSchemaCol(DataRow row, params string[] colNames)
        {
            foreach (var col in colNames)
            {
                if (row.Table.Columns.Contains(col) && row[col] != null && row[col] != DBNull.Value)
                {
                    var s = Convert.ToString(row[col], CultureInfo.InvariantCulture);
                    return s != null ? s.Trim() : string.Empty;
                }
            }

            return string.Empty;
        }

        private static string EscapeCsvField(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var cleaned = value.Replace("\r", " ").Replace("\n", " ").Trim();
            if (cleaned.IndexOf(';') >= 0 || cleaned.IndexOf('"') >= 0)
            {
                return "\"" + cleaned.Replace("\"", "\"\"") + "\"";
            }

            return cleaned;
        }

        private static DbConnection CreateConnection(string provider, string connectionString)
        {
            switch (provider)
            {
                case "odbc":
                    return new OdbcConnection(connectionString);
                case "oledb":
                    return new OleDbConnection(connectionString);
                default:
                    return new SqlConnection(connectionString);
            }
        }

        private static List<Dictionary<string, object>> ExecuteQuery(DbConnection conn, string sql)
        {
            var rows = new List<Dictionary<string, object>>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandTimeout = DmsEnvironment.BrzDbCommandTimeoutSeconds();
                cmd.CommandText = sql;
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            var name = reader.GetName(i);
                            if (!string.IsNullOrEmpty(name))
                            {
                                row[name] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                            }
                        }

                        rows.Add(row);
                    }
                }
            }

            return rows;
        }

        private static string GetStr(Dictionary<string, object> row, params string[] keys)
        {
            foreach (var k in keys)
            {
                object val;
                if (row.TryGetValue(k, out val) && val != null)
                {
                    var raw = Convert.ToString(val, CultureInfo.InvariantCulture);
                    var s = raw != null ? raw.Trim() : null;
                    if (!string.IsNullOrEmpty(s))
                    {
                        return s;
                    }
                }
            }

            return null;
        }

        private static bool? GetBool(Dictionary<string, object> row, params string[] keys)
        {
            var s = GetStr(row, keys);
            if (string.IsNullOrEmpty(s)) return null;
            if (s == "1" || string.Equals(s, "true", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "ja", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "j", StringComparison.OrdinalIgnoreCase))
                return true;
            if (s == "0" || string.Equals(s, "false", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "nein", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "n", StringComparison.OrdinalIgnoreCase))
                return false;
            return null;
        }

        private static double? GetDouble(Dictionary<string, object> row, params string[] keys)
        {
            foreach (var k in keys)
            {
                object val;
                if (row.TryGetValue(k, out val) && val != null)
                {
                    if (val is double) return (double)val;
                    if (val is float) return (double)(float)val;
                    if (val is decimal) return (double)(decimal)val;
                    if (val is int) return (double)(int)val;
                    if (val is long) return (double)(long)val;
                    var raw = Convert.ToString(val, CultureInfo.InvariantCulture);
                    var s = raw != null ? raw.Replace(',', '.').Trim() : string.Empty;
                    double parsed;
                    if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out parsed))
                    {
                        return parsed;
                    }
                }
            }

            return null;
        }

        private static int GetInt(Dictionary<string, object> row, int defaultValue, params string[] keys)
        {
            var d = GetDouble(row, keys);
            return d.HasValue ? (int)Math.Round(d.Value) : defaultValue;
        }

        private static int SyncTaxCodes(DbConnection conn, ComstructApiClient comstruct, bool dry)
        {
            var sql = DmsEnvironment.BrzTaxCodesQuery();
            if (string.IsNullOrWhiteSpace(sql))
            {
                Console.WriteLine("[2/6] Tax codes: BRZ_SQL_TAX_CODES_QUERY not set, skipping.");
                return 0;
            }

            try
            {
                var dbRows = ExecuteQuery(conn, sql);
                var payload = new List<Dictionary<string, object>>();
                foreach (var r in dbRows)
                {
                    var number = GetStr(r, "number", "steuercode", "steuerkennzeichen", "code", "id");
                    var title = GetStr(r, "title", "bezeichnung", "name", "description") ?? number;
                    if (string.IsNullOrEmpty(number)) continue;

                    var item = new Dictionary<string, object>();
                    item["number"] = number;
                    item["title"] = title;
                    item["rate"] = GetDouble(r, "rate", "percentage", "steuersatz", "satz") ?? 0.0;
                    item["recognition"] = GetBool(r, "recognition") ?? true;
                    item["active"] = GetBool(r, "active", "is_active", "aktiv") ?? true;
                    payload.Add(item);
                }

                Console.WriteLine("[2/6] Tax codes: queried " + dbRows.Count + " row(s), mapped " + payload.Count + " valid tax code(s).");
                if (!dry && payload.Count > 0)
                {
                    comstruct.UpsertTaxCodes(payload);
                    Console.WriteLine("[2/6] Tax codes: synced " + payload.Count + " tax code(s) to Comstruct.");
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[2/6] ERROR syncing tax codes: " + ex.Message);
                return 1;
            }
        }

        private static int SyncPaymentTerms(DbConnection conn, ComstructApiClient comstruct, bool dry)
        {
            var sql = DmsEnvironment.BrzPaymentTermsQuery();
            if (string.IsNullOrWhiteSpace(sql))
            {
                Console.WriteLine("[3/6] Payment terms: BRZ_SQL_PAYMENT_TERMS_QUERY not set, skipping.");
                return 0;
            }

            try
            {
                var dbRows = ExecuteQuery(conn, sql);
                var payload = new List<Dictionary<string, object>>();
                foreach (var r in dbRows)
                {
                    var number = GetStr(r, "number", "key", "code", "zahlungsbedingung", "id");
                    var title = GetStr(r, "title", "bezeichnung", "name") ?? number;
                    if (string.IsNullOrEmpty(number)) continue;

                    var item = new Dictionary<string, object>();
                    item["number"] = number;
                    item["title"] = title;
                    item["net_days"] = GetInt(r, 0, "net_days", "days_for_payment", "nettotage", "tage_netto");
                    item["discount_days1"] = GetInt(r, 0, "discount_days1", "discount_days", "skontotage1", "skonto1_tage");
                    item["discount_rate1"] = GetDouble(r, "discount_rate1", "discount_percentage", "skontoprozent1", "skonto1_prozent") ?? 0.0;
                    item["discount_days2"] = GetInt(r, 0, "discount_days2", "skontotage2", "skonto2_tage");
                    item["discount_rate2"] = GetDouble(r, "discount_rate2", "skontoprozent2", "skonto2_prozent") ?? 0.0;
                    item["active"] = GetBool(r, "active", "is_active", "aktiv") ?? true;
                    payload.Add(item);
                }

                Console.WriteLine("[3/6] Payment terms: queried " + dbRows.Count + " row(s), mapped " + payload.Count + " valid payment term(s).");
                if (!dry && payload.Count > 0)
                {
                    comstruct.UpsertPaymentTerms(payload);
                    Console.WriteLine("[3/6] Payment terms: synced " + payload.Count + " payment term(s) to Comstruct.");
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[3/6] ERROR syncing payment terms: " + ex.Message);
                return 1;
            }
        }

        private static int SyncAccounts(DbConnection conn, ComstructApiClient comstruct, bool dry)
        {
            var sql = DmsEnvironment.BrzAccountsQuery();
            if (string.IsNullOrWhiteSpace(sql))
            {
                Console.WriteLine("[4/6] G/L accounts (Sachkonten): BRZ_SQL_ACCOUNTS_QUERY not set, skipping.");
                return 0;
            }

            try
            {
                var dbRows = ExecuteQuery(conn, sql);
                var payload = new List<Dictionary<string, object>>();
                foreach (var r in dbRows)
                {
                    var number = GetStr(r, "number", "konto", "kontonummer", "ktonr", "sachkonto", "id");
                    var title = GetStr(r, "title", "bezeichnung", "name", "kontobezeichnung") ?? number;
                    if (string.IsNullOrEmpty(number)) continue;

                    var item = new Dictionary<string, object>();
                    item["number"] = number;
                    item["title"] = title;
                    item["active"] = GetBool(r, "active", "is_active", "aktiv") ?? true;
                    payload.Add(item);
                }

                Console.WriteLine("[4/6] G/L accounts: queried " + dbRows.Count + " row(s), mapped " + payload.Count + " valid account(s).");
                if (!dry && payload.Count > 0)
                {
                    for (int i = 0; i < payload.Count; i += AccountBatchSize)
                    {
                        var batch = payload.GetRange(i, Math.Min(AccountBatchSize, payload.Count - i));
                        comstruct.UpsertAccounts(batch);
                    }

                    Console.WriteLine("[4/6] G/L accounts: synced " + payload.Count + " account(s) to Comstruct.");
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[4/6] ERROR syncing G/L accounts: " + ex.Message);
                return 1;
            }
        }

        private static int SyncSuppliers(DbConnection conn, ComstructApiClient comstruct, bool dry)
        {
            var sql = DmsEnvironment.BrzSuppliersQuery();
            if (string.IsNullOrWhiteSpace(sql))
            {
                Console.WriteLine("[5/6] Suppliers (Kreditoren): BRZ_SQL_SUPPLIERS_QUERY not set, skipping.");
                return 0;
            }

            try
            {
                var dbRows = ExecuteQuery(conn, sql);
                var byExternalId = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);

                foreach (var r in dbRows)
                {
                    var extId = GetStr(r, "external_id", "number", "krednr", "kreditor", "kreditorennummer", "lieferantennummer", "id");
                    var title = GetStr(r, "title", "name", "bezeichnung", "kreditorname", "firma");
                    if (string.IsNullOrEmpty(extId) || string.IsNullOrEmpty(title)) continue;

                    Dictionary<string, object> supplier;
                    if (!byExternalId.TryGetValue(extId, out supplier))
                    {
                        supplier = new Dictionary<string, object>();
                        supplier["external_id"] = extId;
                        supplier["title"] = title;
                        supplier["country_code"] = GetStr(r, "country_code", "land", "lkz", "country") ?? "DE";

                        var legalUid = GetStr(r, "legal_uid", "ust_id", "ustid", "steuernummer", "vat_id");
                        if (!string.IsNullOrEmpty(legalUid)) supplier["legal_uid"] = legalUid;

                        var address = GetStr(r, "address", "street", "strasse");
                        if (!string.IsNullOrEmpty(address)) supplier["address"] = address;

                        var city = GetStr(r, "city", "ort", "stadt");
                        if (!string.IsNullOrEmpty(city)) supplier["city"] = city;

                        var zip = GetStr(r, "zip", "plz", "postleitzahl");
                        if (!string.IsNullOrEmpty(zip)) supplier["zip"] = zip;

                        var comment = GetStr(r, "comment", "bemerkung", "notiz");
                        if (!string.IsNullOrEmpty(comment)) supplier["comment"] = comment;

                        var active = GetBool(r, "is_active", "active", "aktiv");
                        if (!active.HasValue)
                        {
                            var gesperrt = GetBool(r, "gesperrt", "sperre");
                            if (gesperrt.HasValue) active = !gesperrt.Value;
                        }
                        if (active.HasValue) supplier["is_active"] = active.Value;

                        byExternalId[extId] = supplier;
                    }

                    var rawIban = GetStr(r, "iban", "bank_iban");
                    var iban = rawIban != null ? rawIban.Replace(" ", "") : null;
                    if (!string.IsNullOrEmpty(iban))
                    {
                        object banksObj;
                        List<Dictionary<string, object>> banks = null;
                        if (supplier.TryGetValue("supplier_bank_details", out banksObj))
                        {
                            banks = banksObj as List<Dictionary<string, object>>;
                        }
                        if (banks == null)
                        {
                            banks = new List<Dictionary<string, object>>();
                            supplier["supplier_bank_details"] = banks;
                        }

                        var bankExtId = GetStr(r, "bank_external_id", "bank_id") ?? iban;
                        if (!banks.Exists(b => string.Equals(Convert.ToString(b["iban"]), iban, StringComparison.OrdinalIgnoreCase)))
                        {
                            var bankEntry = new Dictionary<string, object>();
                            bankEntry["iban"] = iban;
                            bankEntry["external_id"] = bankExtId;
                            banks.Add(bankEntry);
                        }
                    }
                }

                var payload = new List<Dictionary<string, object>>(byExternalId.Values);
                Console.WriteLine("[5/6] Suppliers (Kreditoren): queried " + dbRows.Count + " row(s), mapped " + payload.Count + " unique supplier(s).");

                if (!dry && payload.Count > 0)
                {
                    for (int i = 0; i < payload.Count; i += SupplierBatchSize)
                    {
                        var batch = payload.GetRange(i, Math.Min(SupplierBatchSize, payload.Count - i));
                        comstruct.UpsertSuppliers(batch);
                    }

                    Console.WriteLine("[5/6] Suppliers: synced " + payload.Count + " supplier(s) to Comstruct.");
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[5/6] ERROR syncing suppliers: " + ex.Message);
                return 1;
            }
        }

        private static int SyncProjects(DbConnection conn, ComstructApiClient comstruct, bool dry)
        {
            var sql = DmsEnvironment.BrzProjectsQuery();
            if (string.IsNullOrWhiteSpace(sql))
            {
                Console.WriteLine("[6/6] Projects (Kostenstellen / Baustellen): BRZ_SQL_PROJECTS_QUERY not set, skipping.");
                return 0;
            }

            try
            {
                var dbRows = ExecuteQuery(conn, sql);
                var payload = new List<Dictionary<string, object>>();

                foreach (var r in dbRows)
                {
                    var projectNumber = GetStr(r, "project_number", "number", "kostenstelle", "kst", "kstnr", "baustelle", "baustellennummer");
                    var title = GetStr(r, "title", "name", "bezeichnung", "baustellenbezeichnung") ?? projectNumber;
                    if (string.IsNullOrEmpty(projectNumber)) continue;

                    var extId = GetStr(r, "external_id", "id") ?? projectNumber;
                    var item = new Dictionary<string, object>();
                    item["external_id"] = extId;
                    item["project_number"] = projectNumber;
                    item["title"] = title;
                    item["active"] = GetBool(r, "active", "is_active", "aktiv") ?? true;

                    var desc = GetStr(r, "description", "beschreibung");
                    if (!string.IsNullOrEmpty(desc)) item["description"] = desc;

                    var region = GetStr(r, "region", "bereich", "abteilung");
                    if (!string.IsNullOrEmpty(region)) item["region"] = region;

                    var legalEntity = GetStr(r, "legal_entity_number", "betriebsnummer", "betrnr", "firma", "buchungskreis", "mandant");
                    if (!string.IsNullOrEmpty(legalEntity)) item["legal_entity_number"] = legalEntity;

                    var street = GetStr(r, "address", "street", "strasse");
                    var city = GetStr(r, "city", "ort", "stadt");
                    var zip = GetStr(r, "zip", "plz", "postleitzahl");
                    if (!string.IsNullOrEmpty(street) || !string.IsNullOrEmpty(city) || !string.IsNullOrEmpty(zip))
                    {
                        var addr = new Dictionary<string, object>();
                        addr["address"] = street ?? string.Empty;
                        addr["city"] = city ?? string.Empty;
                        addr["zip"] = zip ?? string.Empty;
                        item["address"] = addr;
                    }

                    payload.Add(item);
                }

                Console.WriteLine("[6/6] Projects (Kostenstellen): queried " + dbRows.Count + " row(s), mapped " + payload.Count + " valid project(s).");
                if (!dry && payload.Count > 0)
                {
                    int synced = 0;
                    foreach (var proj in payload)
                    {
                        comstruct.UpsertProject(proj);
                        synced++;
                    }

                    Console.WriteLine("[6/6] Projects: synced " + synced + " project(s) to Comstruct.");
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[6/6] ERROR syncing projects: " + ex.Message);
                return 1;
            }
        }
    }
}
