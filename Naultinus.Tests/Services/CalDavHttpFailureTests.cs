using Naultinus.Properties;
using Naultinus.Services;
using System.Globalization;
using Xunit;

namespace Naultinus.Tests.Services
{
    public class CalDavHttpFailureTests
    {
        private const string NginxZimbra502 = """
            <!DOCTYPE html>
            <html>
            <head><title>502 Bad Gateway or Proxy Error</title></head>
            <body>
            <h1>502 Bad Gateway or Proxy Error</h1>
            <p>nginx: connection to ZCS upstream refused</p>
            <hr><center>nginx</center>
            </body>
            </html>
            """;

        [Fact]
        public void Html502_FrenchPanelMessage_OmitsServerPage()
        {
            var previousUi = CultureInfo.CurrentUICulture;
            var previous = CultureInfo.CurrentCulture;
            try
            {
                var french = CultureInfo.GetCultureInfo("fr-FR");
                CultureInfo.CurrentUICulture = french;
                CultureInfo.CurrentCulture = french;

                var detail = CalDavHttpFailure.Format(
                    "REPORT",
                    502,
                    "Bad Gateway or Proxy Error",
                    NginxZimbra502,
                    "text/html");
                var panel = string.Format(french, Strings.SyncFailedFormat, detail);

                Assert.Equal(
                    "Échec de la synchronisation : REPORT a échoué: 502 Bad Gateway or Proxy Error. Le serveur est indisponible.",
                    panel);
                Assert.DoesNotContain("<", detail);
                Assert.DoesNotContain("nginx", detail, System.StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("ZCS", detail, System.StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("upstream", detail, System.StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                CultureInfo.CurrentUICulture = previousUi;
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Theory]
        [InlineData("PROPFIND")]
        [InlineData("REPORT")]
        [InlineData("PUT")]
        [InlineData("DELETE")]
        public void HtmlBody_WithoutContentType_StillHidesPage(string method)
        {
            using var culture = new FrenchCultureScope();
            var message = CalDavHttpFailure.Format(method, 502, "Bad Gateway", "<html><body>secret-page</body></html>", "text/plain");
            Assert.Equal($"{method} a échoué: 502 Bad Gateway. Le serveur est indisponible.", message);
            Assert.DoesNotContain("secret-page", message);
            Assert.DoesNotContain("<", message);
        }

        [Fact]
        public void ShortPlainDetail_IsKept()
        {
            using var culture = new FrenchCultureScope();
            var message = CalDavHttpFailure.Format("PUT", 412, "Precondition Failed", "Précondition non satisfaite", "text/plain");
            Assert.Equal("PUT a échoué: 412 Precondition Failed. Précondition non satisfaite", message);
        }

        [Fact]
        public void ShortCalDavXml_IsKept()
        {
            using var culture = new FrenchCultureScope();
            const string body = "<?xml version=\"1.0\"?><D:error xmlns:D=\"DAV:\"><D:need-privileges/></D:error>";
            var message = CalDavHttpFailure.Format("REPORT", 403, "Forbidden", body, "application/xml");
            Assert.Contains("need-privileges", message, System.StringComparison.Ordinal);
            Assert.DoesNotContain("indisponible", message, System.StringComparison.Ordinal);
            Assert.StartsWith("REPORT a échoué: 403 Forbidden. ", message);
        }

        [Fact]
        public void LongPlainBody_IsReplaced()
        {
            using var culture = new FrenchCultureScope();
            var message = CalDavHttpFailure.Format("REPORT", 502, "Bad Gateway", new string('x', 400), "text/plain");
            Assert.Equal("REPORT a échoué: 502 Bad Gateway. Le serveur est indisponible.", message);
            Assert.DoesNotContain("xxx", message);
        }

        [Fact]
        public void EmptyBody_KeepsStatusOnly()
        {
            using var culture = new FrenchCultureScope();
            var message = CalDavHttpFailure.Format("DELETE", 404, "Not Found", "", null);
            Assert.Equal("DELETE a échoué: 404 Not Found.", message);
        }

        [Fact]
        public void HtmlMediaType_HidesShortPlainBody()
        {
            using var culture = new FrenchCultureScope();
            var message = CalDavHttpFailure.Format("REPORT", 502, "Bad Gateway", "oops", "text/html");
            Assert.Equal("REPORT a échoué: 502 Bad Gateway. Le serveur est indisponible.", message);
            Assert.DoesNotContain("oops", message);
        }

        [Fact]
        public void HtmlReasonPhrase_IsDropped()
        {
            using var culture = new FrenchCultureScope();
            var message = CalDavHttpFailure.Format("REPORT", 502, "<html>Bad Gateway</html>", null, null);
            Assert.Equal("REPORT a échoué: 502.", message);
            Assert.DoesNotContain("<", message);
        }

        private sealed class FrenchCultureScope : System.IDisposable
        {
            private readonly CultureInfo _previousUi = CultureInfo.CurrentUICulture;
            private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

            public FrenchCultureScope()
            {
                var french = CultureInfo.GetCultureInfo("fr-FR");
                CultureInfo.CurrentUICulture = french;
                CultureInfo.CurrentCulture = french;
            }

            public void Dispose()
            {
                CultureInfo.CurrentUICulture = _previousUi;
                CultureInfo.CurrentCulture = _previous;
            }
        }
    }
}
