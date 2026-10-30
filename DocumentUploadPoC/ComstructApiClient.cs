using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Script.Serialization;

namespace MetasonicDocumentUploadPoC
{
    /// <summary>HTTP client for the Comstruct REST API.</summary>
    internal sealed class ComstructApiClient : IDisposable
    {
        private readonly HttpClient _http;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        public ComstructApiClient()
        {
            var handler = new System.Net.Http.HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
            };
            _http = new HttpClient(handler);
            _http.DefaultRequestHeaders.Add("x-api-key", DmsEnvironment.ComstructApiKey());
            _http.DefaultRequestHeaders.Add("Accept", "application/json");
        }

        private static string InvoicesBasePath()
        {
            var p = DmsEnvironment.ComstructApiPrefix();
            return string.IsNullOrEmpty(p) ? "/invoices" : "/" + p + "/invoices";
        }

        public Dictionary<string, object> GetInvoiceById(string invoiceId)
        {
            var baseUrl = DmsEnvironment.ComstructBaseUrl();
            var url = baseUrl + InvoicesBasePath() + "/" + Uri.EscapeDataString(invoiceId);
            var response = _http.GetAsync(url).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return _json.DeserializeObject(body) as Dictionary<string, object>;
        }

        public List<Dictionary<string, object>> ListInvoiceRowsForQueue()
        {
            var baseUrl = DmsEnvironment.ComstructBaseUrl();
            var q = "limit=" + DmsEnvironment.ListLimit() + "&offset=0";
            var qs = DmsEnvironment.QueueStatus();
            if (!string.IsNullOrEmpty(qs))
            {
                q = "status=" + Uri.EscapeDataString(qs) + "&" + q;
            }

            var url = baseUrl + InvoicesBasePath() + "?" + q;
            var response = _http.GetAsync(url).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return ParseInvoiceRows(body);
        }

        private List<Dictionary<string, object>> ParseInvoiceRows(string json)
        {
            var list = new List<Dictionary<string, object>>();
            var root = _json.DeserializeObject(json) as Dictionary<string, object>;
            if (root == null || !root.ContainsKey("data"))
            {
                return list;
            }

            var data = root["data"] as IEnumerable;
            if (data == null)
            {
                return list;
            }

            foreach (var item in data)
            {
                var row = item as Dictionary<string, object>;
                if (row != null)
                {
                    list.Add(row);
                }
            }

            return list;
        }

        /// <summary>
        /// Downloads the invoice file from Comstruct.
        /// Returns Tuple(bytes, originalFileName) or null when the file is not yet available.
        /// </summary>
        public Tuple<byte[], string> DownloadInvoicePdf(string invoiceId)
        {
            var baseUrl = DmsEnvironment.ComstructBaseUrl();
            var queryParts = new List<string>();
            if (DmsEnvironment.PdfAnnotations())
            {
                queryParts.Add("annotations=true");
            }

            if (DmsEnvironment.PdfOverlay())
            {
                queryParts.Add("overlay=true");
            }

            var query = queryParts.Count > 0 ? "?" + string.Join("&", queryParts) : string.Empty;
            var path = baseUrl + InvoicesBasePath() + "/" + Uri.EscapeDataString(invoiceId) + "/pdf" + query;

            var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Accept.ParseAdd("application/pdf");
            request.Headers.Accept.ParseAdd("application/json");
            var response = _http.SendAsync(request).GetAwaiter().GetResult();
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();

            var contentType = (response.Content.Headers.ContentType != null && response.Content.Headers.ContentType.MediaType != null)
                ? response.Content.Headers.ContentType.MediaType
                : string.Empty;
            if (contentType.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var jsonText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                var obj = _json.DeserializeObject(jsonText) as Dictionary<string, object>;
                if (obj == null || !obj.ContainsKey("pdf_base64") || obj["pdf_base64"] == null)
                {
                    return null;
                }

                var rawB64 = Convert.ToString(obj["pdf_base64"]);
                var b64 = rawB64 != null ? rawB64.Replace("\r", "").Replace("\n", "").Replace(" ", "") : null;
                if (string.IsNullOrEmpty(b64)) return null;
                var data = Convert.FromBase64String(b64);

                var fileName = obj.ContainsKey("file_name") && obj["file_name"] != null
                    ? Convert.ToString(obj["file_name"])
                    : "invoice-" + invoiceId + ".pdf";

                return Tuple.Create(data, fileName);
            }

            var bytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            if (bytes == null || bytes.Length == 0) return null;

            string disposition = "invoice-" + invoiceId + ".pdf";
            if (response.Content.Headers.ContentDisposition != null)
            {
                disposition = response.Content.Headers.ContentDisposition.FileNameStar
                    ?? response.Content.Headers.ContentDisposition.FileName
                    ?? disposition;
            }

            return Tuple.Create(bytes, disposition.Trim('"'));
        }

        public void PostInvoiceCallback(string externalId, bool success, string errorMessage = null)
        {
            var basePath = InvoicesBasePath();
            var url = DmsEnvironment.ComstructBaseUrl() + basePath + "/callback";

            var o = new Dictionary<string, object>();
            o["external_id"] = externalId;
            o["success"] = success;
            if (!string.IsNullOrEmpty(errorMessage))
            {
                o["messages"] = new[] { errorMessage };
            }

            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(_json.Serialize(o), Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Accept", "application/json");

            var response = _http.SendAsync(request).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                var err = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                throw new InvalidOperationException("POST callback failed HTTP " + (int)response.StatusCode + ": " + err);
            }
        }

        public void PatchInvoiceDms(string invoiceId, string jsonBody)
        {
            var template = DmsEnvironment.PatchPathTemplate();
            var prefix = DmsEnvironment.ComstructApiPrefix();
            var path = template
                .Replace("{prefix}", prefix)
                .Replace("{id}", Uri.EscapeDataString(invoiceId));
            if (!path.StartsWith("/"))
            {
                path = "/" + path;
            }

            var url = DmsEnvironment.ComstructBaseUrl() + path;
            var request = new HttpRequestMessage(new HttpMethod("PATCH"), url)
            {
                Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Accept", "application/json");

            var response = _http.SendAsync(request).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                var err = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                throw new InvalidOperationException("PATCH DMS failed HTTP " + (int)response.StatusCode + ": " + err);
            }
        }

        private static string ApiPath(string endpoint)
        {
            var p = DmsEnvironment.ComstructApiPrefix();
            var clean = endpoint.TrimStart('/');
            return string.IsNullOrEmpty(p) ? "/" + clean : "/" + p + "/" + clean;
        }

        private string PostJson(string endpoint, object payload)
        {
            var url = DmsEnvironment.ComstructBaseUrl() + ApiPath(endpoint);
            var jsonBody = _json.Serialize(payload);
            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Accept", "application/json");

            var response = _http.SendAsync(request).GetAwaiter().GetResult();
            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException("POST " + endpoint + " failed HTTP " + (int)response.StatusCode + ": " + body);
            }

            return body;
        }

        public void UpsertSuppliers(List<Dictionary<string, object>> suppliers)
        {
            if (suppliers == null || suppliers.Count == 0) return;
            PostJson("suppliers", suppliers);
        }

        public void UpsertProject(Dictionary<string, object> project)
        {
            if (project == null || project.Count == 0) return;
            PostJson("projects", project);
        }

        public void UpsertAccounts(List<Dictionary<string, object>> accounts)
        {
            if (accounts == null || accounts.Count == 0) return;
            PostJson("accounts", accounts);
        }

        public void UpsertTaxCodes(List<Dictionary<string, object>> taxCodes)
        {
            if (taxCodes == null || taxCodes.Count == 0) return;
            PostJson("tax-codes", taxCodes);
        }

        public void UpsertPaymentTerms(List<Dictionary<string, object>> paymentTerms)
        {
            if (paymentTerms == null || paymentTerms.Count == 0) return;
            PostJson("payment-terms", paymentTerms);
        }

        public void Dispose()
        {
            if (_http != null)
            {
                _http.Dispose();
            }
        }
    }
}
