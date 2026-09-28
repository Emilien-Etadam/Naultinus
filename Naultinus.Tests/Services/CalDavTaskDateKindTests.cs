using Naultinus.Model;
using Naultinus.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Xml.Linq;
using Xunit;

namespace Naultinus.Tests.Services
{
    public class CalDavTaskDateKindTests
    {
        private const string TaskListHref = "/calendars/tasks/";

        [Fact]
        public async Task CreateTask_LocalDueKeepsWallClock_AndStampsBecomeUtc()
        {
            var client = new RecordingCalDavClient();
            var service = new CalDAVService(client);
            var due = new DateTime(2026, 9, 25, 0, 0, 0, DateTimeKind.Local);
            var created = new DateTime(2026, 9, 24, 22, 15, 30, DateTimeKind.Local);
            var completed = new DateTime(2026, 9, 24, 23, 45, 0, DateTimeKind.Local);
            var task = new CalDAVTask("Acheter du pain")
            {
                DueDate = due,
                CreatedDate = created,
                LastModified = created,
                Completed = true,
                CompletedDate = completed,
                Uid = "task-1",
            };

            await service.CreateTaskAsync(TaskListHref, task);

            var body = client.PutBody ?? string.Empty;
            Assert.Contains("DUE:20260925T000000", body, StringComparison.Ordinal);
            Assert.DoesNotContain("DUE:20260925T000000Z", body, StringComparison.Ordinal);
            Assert.Contains("CREATED:" + FormatUtc(created.ToUniversalTime()), body, StringComparison.Ordinal);
            Assert.Contains("LAST-MODIFIED:" + FormatUtc(created.ToUniversalTime()), body, StringComparison.Ordinal);
            Assert.Contains("COMPLETED:" + FormatUtc(completed.ToUniversalTime()), body, StringComparison.Ordinal);
        }

        [Fact]
        public async Task UpdateTask_KeepsUtcDueAndCreated_AndAcceptsLocalCompletion()
        {
            const string calendarData =
                "BEGIN:VCALENDAR\r\n" +
                "VERSION:2.0\r\n" +
                "BEGIN:VTODO\r\n" +
                "UID:abc\r\n" +
                "SUMMARY:Pain\r\n" +
                "DUE:20260925T150000Z\r\n" +
                "CREATED:20260102T030405Z\r\n" +
                "LAST-MODIFIED:20260102T030405Z\r\n" +
                "STATUS:NEEDS-ACTION\r\n" +
                "END:VTODO\r\n" +
                "END:VCALENDAR\r\n";
            var client = new RecordingCalDavClient
            {
                ReportDocument = ReportWithCalendarData("/calendars/tasks/abc.ics", "\"etag-1\"", calendarData),
            };
            var service = new CalDAVService(client);

            var loaded = Assert.Single(await service.GetTasksAsync(TaskListHref));
            Assert.Equal(DateTimeKind.Utc, loaded.DueDate?.Kind);
            Assert.Equal(new DateTime(2026, 9, 25, 15, 0, 0, DateTimeKind.Utc), loaded.DueDate);
            Assert.Equal(DateTimeKind.Utc, loaded.CreatedDate.Kind);
            Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), loaded.CreatedDate);

            var completedLocal = new DateTime(2026, 9, 24, 23, 45, 0, DateTimeKind.Local);
            loaded.Completed = true;
            loaded.CompletedDate = completedLocal;
            var before = DateTime.UtcNow.AddSeconds(-2);

            await service.UpdateTaskAsync(TaskListHref, loaded);

            var body = client.PutBody ?? string.Empty;
            Assert.Contains("DUE:20260925T150000Z", body, StringComparison.Ordinal);
            Assert.Contains("CREATED:20260102T030405Z", body, StringComparison.Ordinal);
            Assert.Contains("COMPLETED:" + FormatUtc(completedLocal.ToUniversalTime()), body, StringComparison.Ordinal);
            AssertLastModifiedIsRecentUtc(body, before);
        }

        [Fact]
        public async Task UpdateTask_FloatingDueStaysFloating()
        {
            const string calendarData =
                "BEGIN:VCALENDAR\r\n" +
                "VERSION:2.0\r\n" +
                "BEGIN:VTODO\r\n" +
                "UID:float\r\n" +
                "SUMMARY:Flottante\r\n" +
                "DUE:20260925T180000\r\n" +
                "CREATED:20260102T030405Z\r\n" +
                "STATUS:NEEDS-ACTION\r\n" +
                "END:VTODO\r\n" +
                "END:VCALENDAR\r\n";
            var client = new RecordingCalDavClient
            {
                ReportDocument = ReportWithCalendarData("/calendars/tasks/float.ics", "\"etag-2\"", calendarData),
            };
            var service = new CalDAVService(client);
            var loaded = Assert.Single(await service.GetTasksAsync(TaskListHref));
            Assert.Equal(DateTimeKind.Unspecified, loaded.DueDate?.Kind);
            Assert.Equal(new DateTime(2026, 9, 25, 18, 0, 0), loaded.DueDate);

            await service.UpdateTaskAsync(TaskListHref, loaded);

            var body = client.PutBody ?? string.Empty;
            Assert.Contains("DUE:20260925T180000", body, StringComparison.Ordinal);
            Assert.DoesNotContain("DUE:20260925T180000Z", body, StringComparison.Ordinal);
            Assert.Contains("CREATED:20260102T030405Z", body, StringComparison.Ordinal);
        }

        private static string FormatUtc(DateTime utc)
            => utc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

        private static void AssertLastModifiedIsRecentUtc(string body, DateTime notBeforeUtc)
        {
            const string marker = "LAST-MODIFIED:";
            var start = body.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(start >= 0, body);
            var value = body.Substring(start + marker.Length, 16);
            Assert.EndsWith("Z", value, StringComparison.Ordinal);
            var parsed = DateTime.ParseExact(value, "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
            Assert.InRange(parsed, notBeforeUtc, DateTime.UtcNow.AddMinutes(1));
        }

        private static XDocument ReportWithCalendarData(string href, string etag, string calendarData)
        {
            XNamespace dav = "DAV:";
            XNamespace cal = "urn:ietf:params:xml:ns:caldav";
            return new XDocument(
                new XElement(dav + "multistatus",
                    new XElement(dav + "response",
                        new XElement(dav + "href", href),
                        new XElement(dav + "propstat",
                            new XElement(dav + "prop",
                                new XElement(dav + "getetag", etag),
                                new XElement(cal + "calendar-data", calendarData)),
                            new XElement(dav + "status", "HTTP/1.1 200 OK")))));
        }

        private sealed class RecordingCalDavClient : ICalDAVClient
        {
            public XDocument? ReportDocument { get; init; }
            public string? PutBody { get; private set; }

            public Task<XDocument> ReportAsync(string href, string requestBody)
                => Task.FromResult(ReportDocument ?? new XDocument(new XElement(XName.Get("multistatus", "DAV:"))));

            public Task<string?> PutAsync(string href, string icalData, string? etag = null)
            {
                PutBody = icalData;
                return Task.FromResult<string?>("\"updated\"");
            }

            public Task<XDocument> PropfindAsync(string href, int depth, string? requestBody = null)
                => throw new NotSupportedException();

            public Task DeleteAsync(string href, string? etag = null)
                => throw new NotSupportedException();

            public Task<List<CalDAVCalendarInfo>> DiscoverCalendarsAsync()
                => throw new NotSupportedException();

            public void Dispose()
            {
            }
        }
    }
}
