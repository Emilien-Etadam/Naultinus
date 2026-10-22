using System.Linq;
using System.Xml.Linq;
using Naultinus.Services;
using Xunit;

namespace Naultinus.Tests.Services
{
    public class CalDavEnumerateCalendarDataTests
    {
        [Fact]
        public void EnumerateCalendarData_YieldsHrefEtagAndPayload()
        {
            var xml = """
                <?xml version="1.0" encoding="utf-8"?>
                <d:multistatus xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:caldav">
                  <d:response>
                    <d:href>/dav/u/Tasks/a.ics</d:href>
                    <d:propstat>
                      <d:status>HTTP/1.1 200 OK</d:status>
                      <d:prop>
                        <d:getetag>"etag-1"</d:getetag>
                        <c:calendar-data>BEGIN:VCALENDAR&#13;&#10;END:VCALENDAR</c:calendar-data>
                      </d:prop>
                    </d:propstat>
                  </d:response>
                  <d:response>
                    <d:href>/dav/u/Tasks/skip.ics</d:href>
                    <d:propstat>
                      <d:status>HTTP/1.1 200 OK</d:status>
                      <d:prop>
                        <d:getetag>"x"</d:getetag>
                      </d:prop>
                    </d:propstat>
                  </d:response>
                </d:multistatus>
                """;
            var doc = XDocument.Parse(xml);
            var items = CalDAVClient.EnumerateCalendarData(doc).ToList();
            var one = Assert.Single(items);
            Assert.Equal("/dav/u/Tasks/a.ics", one.Href);
            Assert.Equal("etag-1", one.Etag);
            Assert.Contains("VCALENDAR", one.CalendarData, System.StringComparison.Ordinal);
        }
    }
}
