using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Naultinus.Services
{
    /// <summary>
    /// Client CalDAV bas niveau. Gère l'authentification, les requêtes WebDAV,
    /// et le parsing des réponses multistatus.
    /// </summary>
    public class CalDAVClient : ICalDAVClient
    {
        private readonly HttpClient _httpClient;
        private readonly Uri _baseUri;
        private readonly bool _isConfigured;
        private bool _disposed;

        private static void EnsureHttps(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return;
            if (!url.TrimStart().StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("L'URL CalDAV doit utiliser HTTPS. Les connexions non sécurisées sont refusées.");
        }

        public CalDAVClient(string baseUrl, string username, string password)
        {
            var url = (baseUrl ?? "").Trim();
            if (string.IsNullOrEmpty(url) || url.Equals("https://localhost/", StringComparison.OrdinalIgnoreCase))
            {
                _isConfigured = false;
                _baseUri = new Uri("https://localhost/");
                var handler = new HttpClientHandler();
                _httpClient = new HttpClient(handler);
                return;
            }
            EnsureHttps(url);
            _isConfigured = true;
            _baseUri = new Uri(url.TrimEnd('/') + "/");

            var handler2 = new HttpClientHandler
            {
                Credentials = new NetworkCredential(username ?? "", password ?? ""),
                PreAuthenticate = true
            };
            _httpClient = new HttpClient(handler2);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Naultinus/1.0");
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/xml"));
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/calendar"));
        }

        /// <summary>
        /// Résout un href relatif par rapport à _baseUri, ou retourne _baseUri si href est vide.
        /// </summary>
        private Uri ResolveUrl(string? href)
        {
            return string.IsNullOrEmpty(href) ? _baseUri : new Uri(_baseUri, href);
        }

        public async Task<XDocument> PropfindAsync(string href, int depth, string? requestBody = null)
        {
            if (!_isConfigured)
                throw new InvalidOperationException("CalDAV client is not configured. Please set a valid CalDAV URL.");
            var url = ResolveUrl(href);
            var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), url);
            request.Headers.Add("Depth", depth.ToString());
            if (!string.IsNullOrEmpty(requestBody))
                request.Content = new StringContent(requestBody, Encoding.UTF8, "application/xml");

            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"PROPFIND a échoué: {(int)response.StatusCode} {response.ReasonPhrase}. {body}");

            return XDocument.Parse(body);
        }

        public async Task<XDocument> ReportAsync(string href, string requestBody)
        {
            if (!_isConfigured)
                throw new InvalidOperationException("CalDAV client is not configured. Please set a valid CalDAV URL.");
            var url = ResolveUrl(href);
            var request = new HttpRequestMessage(new HttpMethod("REPORT"), url);
            request.Headers.Add("Depth", "1");
            request.Content = new StringContent(requestBody, Encoding.UTF8, "application/xml");

            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"REPORT a échoué: {(int)response.StatusCode} {response.ReasonPhrase}. {body}");

            return XDocument.Parse(body);
        }

        public async Task<string?> PutAsync(string href, string icalData, string? etag = null)
        {
            if (!_isConfigured)
                throw new InvalidOperationException("CalDAV client is not configured. Please set a valid CalDAV URL.");
            var url = ResolveUrl(href);
            var request = new HttpRequestMessage(HttpMethod.Put, url);
            request.Content = new StringContent(icalData, Encoding.UTF8, "text/calendar");
            if (!string.IsNullOrEmpty(etag))
                request.Headers.TryAddWithoutValidation("If-Match", "\"" + etag + "\"");

            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"PUT a échoué: {(int)response.StatusCode} {response.ReasonPhrase}. {body}");

            return response.Headers.ETag?.Tag?.Trim('"');
        }

        public async Task DeleteAsync(string href, string? etag = null)
        {
            if (!_isConfigured)
                throw new InvalidOperationException("CalDAV client is not configured. Please set a valid CalDAV URL.");
            var url = ResolveUrl(href);
            var request = new HttpRequestMessage(HttpMethod.Delete, url);
            if (!string.IsNullOrEmpty(etag))
                request.Headers.TryAddWithoutValidation("If-Match", "\"" + etag + "\"");

            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                throw new InvalidOperationException($"DELETE a échoué: {(int)response.StatusCode} {response.ReasonPhrase}. {body}");
            }
        }

        /// <summary>
        /// Parse une réponse multistatus WebDAV et retourne la liste des responses avec href, propriétés (200) et status.
        /// </summary>
        public static List<(string Href, Dictionary<string, string> Props, string? Status)> ParseMultistatus(XDocument doc)
        {
            var result = new List<(string Href, Dictionary<string, string> Props, string? Status)>();
            var dav = XNamespace.Get("DAV:");
            foreach (var response in doc.Descendants(dav + "response"))
            {
                var href = response.Descendants(dav + "href").FirstOrDefault()?.Value?.TrimEnd('/') ?? "";
                var statusEl = response.Descendants(dav + "status").FirstOrDefault();
                var status = statusEl?.Value;

                var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var propstat in response.Descendants(dav + "propstat"))
                {
                    var propstatStatus = propstat.Element(dav + "status")?.Value;
                    if (string.IsNullOrEmpty(propstatStatus) || !propstatStatus.Contains("200"))
                        continue;
                    var prop = propstat.Element(dav + "prop");
                    if (prop == null)
                        continue;
                    foreach (var child in prop.Elements())
                    {
                        var localName = child.Name.LocalName?.ToLowerInvariant() ?? "";
                        if (string.IsNullOrEmpty(localName))
                            continue;
                        var value = child.Value;
                        props[localName] = value;
                    }
                }
                result.Add((href, props, status));
            }
            return result;
        }

        /// <summary>
        /// Découvre les collections de type calendrier (PROPFIND depth 1 sur la base).
        /// Demande aussi supported-calendar-component-set pour distinguer VEVENT des listes VTODO.
        /// </summary>
        public async Task<List<CalDAVCalendarInfo>> DiscoverCalendarsAsync()
        {
            if (!_isConfigured)
                throw new InvalidOperationException("CalDAV client is not configured. Please set a valid CalDAV URL.");
            const string propfindBody = @"<?xml version=""1.0"" encoding=""utf-8""?>
<d:propfind xmlns:d=""DAV:"" xmlns:c=""urn:ietf:params:xml:ns:caldav"">
    <d:prop>
        <d:displayname></d:displayname>
        <d:resourcetype></d:resourcetype>
        <c:supported-calendar-component-set></c:supported-calendar-component-set>
    </d:prop>
</d:propfind>";

            var doc = await PropfindAsync("", 1, propfindBody).ConfigureAwait(false);
            var dav = XNamespace.Get("DAV:");
            var caldavNs = XNamespace.Get("urn:ietf:params:xml:ns:caldav");
            var list = new List<CalDAVCalendarInfo>();

            foreach (var response in doc.Descendants(dav + "response"))
            {
                var href = response.Descendants(dav + "href").FirstOrDefault()?.Value?.TrimEnd('/') ?? "";
                if (string.IsNullOrEmpty(href))
                    continue;

                var propstat = response.Descendants(dav + "propstat").FirstOrDefault(p => p.Element(dav + "status")?.Value?.Contains("200") == true);
                var prop = propstat?.Element(dav + "prop");
                if (prop == null)
                    continue;

                var resourcetype = prop.Element(dav + "resourcetype");
                var isCalendar = resourcetype?.Elements(caldavNs + "calendar").Any() == true
                    || resourcetype?.Descendants().Any(e => string.Equals(e.Name.LocalName, "calendar", StringComparison.OrdinalIgnoreCase) && e.Name.NamespaceName == "urn:ietf:params:xml:ns:caldav") == true;
                if (!isCalendar)
                    continue;

                var displayName = prop.Element(dav + "displayname")?.Value ?? "";
                if (string.IsNullOrEmpty(displayName) && href.Contains('/'))
                    displayName = href.Substring(href.LastIndexOf('/') + 1).TrimEnd('/');
                if (string.IsNullOrEmpty(displayName))
                    displayName = href;

                var calendarId = href.Contains('/') ? href.TrimEnd('/').Substring(href.TrimEnd('/').LastIndexOf('/') + 1) : href;

                var supportedComponents = new List<string>();
                var compSet = prop.Descendants()
                    .FirstOrDefault(e => string.Equals(e.Name.LocalName, "supported-calendar-component-set", StringComparison.OrdinalIgnoreCase));
                if (compSet != null)
                {
                    foreach (var comp in compSet.Descendants().Where(e => string.Equals(e.Name.LocalName, "comp", StringComparison.OrdinalIgnoreCase)))
                    {
                        var name = comp.Attribute("name")?.Value;
                        if (!string.IsNullOrEmpty(name))
                            supportedComponents.Add(name);
                    }
                }

                list.Add(new CalDAVCalendarInfo
                {
                    Href = href,
                    DisplayName = displayName,
                    CalendarId = calendarId,
                    SupportedComponents = supportedComponents,
                });
            }

            return list;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;
            if (disposing)
                _httpClient?.Dispose();
            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
