using Naultinus.Services;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Naultinus.Tests.Services
{
    public class CalDavPutAuthorizationTests
    {
        private const string User = "emilien@etadam.com";
        private const string Password = "s3cret-value";
        private const string BaseUrl = "https://zimbra.exemple.test/dav/emilien/";
        private const string CalendarHref = "/dav/emilien/Calendar";
        private const string Ical = "BEGIN:VCALENDAR\r\nEND:VCALENDAR\r\n";

        [Fact]
        public async Task CreateEvent_PutSendsBasicAuthorization_WithoutPuttingTheSecretInTheUrl()
        {
            var handler = new ScriptedHandler();
            handler.Enqueue(_ => Created());
            using var client = new CalDAVClient(handler, BaseUrl, User, Password);
            using var service = new CalendarCalDAVService(client);

            var etag = await service.CreateEventAsync(CalendarHref, Ical);

            var call = Assert.Single(handler.Calls);
            Assert.Equal("PUT", call.Method);
            Assert.StartsWith("https://zimbra.exemple.test/dav/emilien/Calendar/", call.Uri, StringComparison.Ordinal);
            Assert.EndsWith(".ics", call.Uri, StringComparison.Ordinal);
            Assert.Equal(Basic(User, Password), call.Authorization);
            Assert.Equal(Ical, call.Body);
            Assert.Equal("abc", etag);
            Assert.DoesNotContain(Password, call.Uri, StringComparison.Ordinal);
            Assert.DoesNotContain(Password, call.Authorization ?? string.Empty, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Put_SameOriginHttpsRedirect_KeepsAuthorizationAndBody()
        {
            var handler = new ScriptedHandler();
            handler.Enqueue(_ => Redirect("https://zimbra.exemple.test/dav/emilien/Calendar/canonical/evt.ics"));
            handler.Enqueue(_ => Created());
            using var client = new CalDAVClient(handler, BaseUrl, User, Password);

            var etag = await client.PutAsync(CalendarHref + "/evt.ics", Ical, "etag-1");

            Assert.Equal(2, handler.Calls.Count);
            Assert.Equal(Basic(User, Password), handler.Calls[0].Authorization);
            Assert.Equal(Basic(User, Password), handler.Calls[1].Authorization);
            Assert.Equal("https://zimbra.exemple.test/dav/emilien/Calendar/canonical/evt.ics", handler.Calls[1].Uri);
            Assert.Equal("PUT", handler.Calls[1].Method);
            Assert.Equal(Ical, handler.Calls[1].Body);
            Assert.Contains("etag-1", handler.Calls[1].IfMatch ?? string.Empty, StringComparison.Ordinal);
            Assert.Equal("abc", etag);
            Assert.DoesNotContain(Password, handler.Calls[1].Uri, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Put_DoesNotFollowHttpOrForeignHost_AndDoesNotLeakTheSecret()
        {
            var handler = new ScriptedHandler();
            handler.Enqueue(_ => Redirect("http://zimbra.exemple.test/dav/emilien/Calendar/evt.ics"));
            using var client = new CalDAVClient(handler, BaseUrl, User, Password);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.PutAsync(CalendarHref + "/evt.ics", Ical));

            var httpCall = Assert.Single(handler.Calls);
            Assert.Contains("302", error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(Password, error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(Password, httpCall.Uri, StringComparison.Ordinal);

            handler.Calls.Clear();
            handler.Enqueue(_ => Redirect("https://autre.exemple.test/dav/emilien/Calendar/evt.ics"));
            var foreign = await Assert.ThrowsAsync<InvalidOperationException>(() => client.PutAsync(CalendarHref + "/evt.ics", Ical));
            var foreignCall = Assert.Single(handler.Calls);
            Assert.Contains("302", foreign.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(Password, foreign.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(Password, foreignCall.Uri, StringComparison.Ordinal);
        }

        private static string Basic(string user, string password)
        {
            return "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(user + ":" + password));
        }

        private static HttpResponseMessage Created()
        {
            var response = new HttpResponseMessage(HttpStatusCode.Created);
            response.Headers.ETag = EntityTagHeaderValue.Parse("\"abc\"");
            response.Content = new StringContent(string.Empty);
            return response;
        }

        private static HttpResponseMessage Redirect(string location)
        {
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
            response.Content = new StringContent(string.Empty);
            return response;
        }

        private sealed class ScriptedHandler : HttpMessageHandler
        {
            private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

            public List<RecordedCall> Calls { get; } = new();

            public void Enqueue(Func<HttpRequestMessage, HttpResponseMessage> respond)
            {
                _responses.Enqueue(respond);
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var body = request.Content == null
                    ? null
                    : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                Calls.Add(new RecordedCall(
                    request.Method.Method,
                    request.RequestUri?.ToString() ?? string.Empty,
                    request.Headers.Authorization?.ToString(),
                    request.Headers.TryGetValues("If-Match", out var ifMatch) ? string.Join(",", ifMatch) : null,
                    body));
                if (_responses.Count == 0)
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent(string.Empty) };
                return _responses.Dequeue()(request);
            }
        }

        private sealed class RecordedCall
        {
            public RecordedCall(string method, string uri, string? authorization, string? ifMatch, string? body)
            {
                Method = method;
                Uri = uri;
                Authorization = authorization;
                IfMatch = ifMatch;
                Body = body;
            }

            public string Method { get; }

            public string Uri { get; }

            public string? Authorization { get; }

            public string? IfMatch { get; }

            public string? Body { get; }
        }
    }
}
