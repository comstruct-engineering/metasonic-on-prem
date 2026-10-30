using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Web.Script.Serialization;

namespace MetasonicDocumentUploadPoC
{
    /// <summary>
    /// Client for the local BRZ OData / REST Stammdaten API (e.g. <c>http://YOUR_HOST/api/stammdaten/</c>).
    /// </summary>
    internal static class BrzOdataClient
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        private static HttpClient CreateHttpClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
            };
            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(DmsEnvironment.BrzDbCommandTimeoutSeconds())
            };

            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var user = DmsEnvironment.BrzOdataUser();
            var pass = DmsEnvironment.BrzOdataPassword();
            if (!string.IsNullOrEmpty(user))
            {
                var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(user + ":" + pass));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
            }

            var parent = DmsEnvironment.BrzOdataParent();
            if (!string.IsNullOrEmpty(parent))
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation("Parent", parent);
                client.DefaultRequestHeaders.TryAddWithoutValidation("X-Parent", parent);
            }

            return client;
        }

        private static string BuildUrl(string pathOrQuery)
        {
            var baseUrl = DmsEnvironment.BrzOdataBaseUrl();
            if (string.IsNullOrWhiteSpace(pathOrQuery))
            {
                return baseUrl;
            }

            if (pathOrQuery.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                pathOrQuery.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return pathOrQuery;
            }

            var clean = pathOrQuery.TrimStart('/');
            var parent = DmsEnvironment.BrzOdataParent();
            var betrNr = DmsEnvironment.BrzOdataCompanyNumber();

            if (!clean.Contains("?$filter=") && !string.IsNullOrEmpty(parent))
            {
                if (!string.IsNullOrEmpty(betrNr))
                {
                    clean += "?$filter=Ident eq '" + parent + "' and Betriebsnummer eq '" + betrNr + "'";
                }
                else
                {
                    clean += "?$filter=Ident eq '" + parent + "'";
                }
            }

            return baseUrl + clean;
        }

        public static int QueryEndpoint(string resourceOrQuery = null, string outputFilePath = null)
        {
            Console.WriteLine("=== BRZ OData / REST Stammdaten Client ===\n");
            var url = BuildUrl(resourceOrQuery);
            Console.WriteLine("GET " + url);
            Console.WriteLine("User: " + (string.IsNullOrEmpty(DmsEnvironment.BrzOdataUser()) ? "(none)" : DmsEnvironment.BrzOdataUser()) + "\n");

            try
            {
                using (var http = CreateHttpClient())
                {
                    var response = http.GetAsync(url).GetAwaiter().GetResult();
                    var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                    Console.WriteLine("HTTP " + (int)response.StatusCode + " (" + response.ReasonPhrase + ") — " + (body != null ? body.Length : 0) + " chars\n");
                    if (!response.IsSuccessStatusCode)
                    {
                        Console.WriteLine(body);
                        return 1;
                    }

                    var rows = ParseOdataRows(body);
                    if (rows.Count == 0)
                    {
                        Console.WriteLine("Raw Response:");
                        Console.WriteLine(body);
                        if (!string.IsNullOrWhiteSpace(outputFilePath))
                        {
                            File.WriteAllText(outputFilePath, body ?? string.Empty, Encoding.UTF8);
                            Console.WriteLine("\nSaved raw response to: " + Path.GetFullPath(outputFilePath));
                        }
                        return 0;
                    }

                    var columns = CollectColumnNames(rows);
                    Console.WriteLine("Parsed " + rows.Count + " record(s). Columns: " + string.Join(" | ", columns));
                    Console.WriteLine(new string('-', 90));

                    int preview = Math.Min(25, rows.Count);
                    for (int i = 0; i < preview; i++)
                    {
                        var r = rows[i];
                        var vals = new List<string>();
                        foreach (var col in columns)
                        {
                            object v;
                            r.TryGetValue(col, out v);
                            vals.Add(col + "=" + Convert.ToString(v, CultureInfo.InvariantCulture));
                        }
                        Console.WriteLine("[" + (i + 1) + "] " + string.Join(" | ", vals));
                    }

                    if (rows.Count > preview)
                    {
                        Console.WriteLine("... (" + (rows.Count - preview) + " more row(s) omitted from preview)");
                    }

                    if (!string.IsNullOrWhiteSpace(outputFilePath))
                    {
                        var fullPath = Path.GetFullPath(outputFilePath);
                        var dir = Path.GetDirectoryName(fullPath);
                        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                        if (fullPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                        {
                            File.WriteAllText(fullPath, body, Encoding.UTF8);
                        }
                        else
                        {
                            WriteRowsToCsv(fullPath, columns, rows);
                        }

                        Console.WriteLine("\nSaved " + rows.Count + " row(s) to: " + fullPath);
                    }

                    return 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("OData request failed: " + ex.Message);
                return 1;
            }
        }

        public static int SyncToComstruct(string endpointName = "Kostenstellen")
        {
            var dry = DmsEnvironment.DmsWorkerDryRun();
            var url = BuildUrl(endpointName);
            Console.WriteLine("=== BRZ OData → Comstruct Sync ===\n");
            Console.WriteLine("GET " + url + " (dryRun=" + dry + ")\n");

            try
            {
                using (var http = CreateHttpClient())
                using (var comstruct = new ComstructApiClient())
                {
                    var response = http.GetAsync(url).GetAwaiter().GetResult();
                    var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    if (!response.IsSuccessStatusCode)
                    {
                        Console.WriteLine("HTTP " + (int)response.StatusCode + ": " + body);
                        return 1;
                    }

                    var rows = ParseOdataRows(body);
                    Console.WriteLine("Fetched " + rows.Count + " record(s) from '" + endpointName + "'.");

                    int synced = 0;
                    foreach (var r in rows)
                    {
                        var kstNr = GetStr(r, "KstNr", "Kostenstelle", "Baustellennummer", "Number", "project_number", "Ident");
                        var bez = GetStr(r, "Bezeichnung", "Name", "Title", "Gruppename") ?? kstNr;
                        if (string.IsNullOrEmpty(kstNr)) continue;

                        var extId = GetStr(r, "Ident", "ID", "external_id") ?? kstNr;
                        var project = new Dictionary<string, object>();
                        project["external_id"] = extId;
                        project["project_number"] = kstNr;
                        project["title"] = bez;
                        project["active"] = true;

                        var betrNr = GetStr(r, "Betriebsnummer", "BetrNr", "legal_entity_number");
                        if (!string.IsNullOrEmpty(betrNr))
                        {
                            project["legal_entity_number"] = betrNr;
                        }

                        if (dry)
                        {
                            Console.WriteLine("  [dry-run] would upsert project: number='" + kstNr + "', title='" + bez + "', external_id='" + extId + "'");
                        }
                        else
                        {
                            comstruct.UpsertProject(project);
                        }

                        synced++;
                    }

                    Console.WriteLine("\nProcessed " + synced + " project(s) from OData '" + endpointName + "'.");
                    return 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("OData sync failed: " + ex.Message);
                return 1;
            }
        }

        private static List<Dictionary<string, object>> ParseOdataRows(string json)
        {
            var result = new List<Dictionary<string, object>>();
            if (string.IsNullOrWhiteSpace(json)) return result;

            try
            {
                var parsed = Json.DeserializeObject(json);
                IEnumerable list = parsed as IEnumerable;

                var dict = parsed as Dictionary<string, object>;
                if (dict != null)
                {
                    object valObj;
                    object dataObj;
                    object dObj;
                    if (dict.TryGetValue("value", out valObj) && valObj is IEnumerable && !(valObj is string))
                    {
                        list = (IEnumerable)valObj;
                    }
                    else if (dict.TryGetValue("data", out dataObj) && dataObj is IEnumerable && !(dataObj is string))
                    {
                        list = (IEnumerable)dataObj;
                    }
                    else if (dict.TryGetValue("d", out dObj))
                    {
                        var dDict = dObj as Dictionary<string, object>;
                        object resObj;
                        if (dDict != null && dDict.TryGetValue("results", out resObj) && resObj is IEnumerable)
                        {
                            list = (IEnumerable)resObj;
                        }
                        else if (dObj is IEnumerable && !(dObj is string))
                        {
                            list = (IEnumerable)dObj;
                        }
                    }
                    else
                    {
                        var single = new Dictionary<string, object>(dict, StringComparer.OrdinalIgnoreCase);
                        result.Add(single);
                        return result;
                    }
                }

                if (list != null && !(parsed is string))
                {
                    foreach (var item in list)
                    {
                        var rowDict = item as Dictionary<string, object>;
                        if (rowDict != null)
                        {
                            result.Add(new Dictionary<string, object>(rowDict, StringComparer.OrdinalIgnoreCase));
                        }
                    }
                }
            }
            catch
            {
                // Non-JSON or unexpected structure
            }

            return result;
        }

        private static List<string> CollectColumnNames(List<Dictionary<string, object>> rows)
        {
            var cols = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rows)
            {
                foreach (var k in r.Keys)
                {
                    if (seen.Add(k))
                    {
                        cols.Add(k);
                    }
                }
            }

            return cols;
        }

        private static void WriteRowsToCsv(string filePath, List<string> columns, List<Dictionary<string, object>> rows)
        {
            using (var sw = new StreamWriter(filePath, false, new UTF8Encoding(false)))
            {
                sw.WriteLine(string.Join(";", columns.ConvertAll(EscapeCsv)));
                foreach (var r in rows)
                {
                    var line = new List<string>(columns.Count);
                    foreach (var c in columns)
                    {
                        object val;
                        r.TryGetValue(c, out val);
                        var str = val == null ? string.Empty : Convert.ToString(val, CultureInfo.InvariantCulture) ?? string.Empty;
                        line.Add(EscapeCsv(str));
                    }

                    sw.WriteLine(string.Join(";", line));
                }
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
    }
}
