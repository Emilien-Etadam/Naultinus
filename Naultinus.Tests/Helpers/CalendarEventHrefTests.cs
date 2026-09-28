using Naultinus.Helpers;
using Naultinus.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Xml.Linq;
using Xunit;

namespace Naultinus.Tests.Helpers
{
    public class CalendarEventHrefTests
    {
        private const string Collection = "https://zimbra.example/dav/user/Calendar/";
        private const string EventHref = "https://zimbra.example/dav/user/Calendar/abc.ics";

        [Fact]
        public void TryGetDeletableResource_AccepteLaRessourceEnfant()
        {
            var ok = CalendarEventHref.TryGetDeletableResource(EventHref, new[] { Collection }, out var resource);

            Assert.True(ok);
            Assert.Equal(EventHref, resource);
        }

        [Theory]
        [InlineData(Collection)]
        [InlineData("https://zimbra.example/dav/user/Calendar")]
        [InlineData("/dav/user/Calendar/")]
        public void TryGetDeletableResource_RefuseLaCollection(string href)
        {
            var ok = CalendarEventHref.TryGetDeletableResource(href, new[] { Collection }, out var resource);

            Assert.False(ok);
            Assert.Equal(string.Empty, resource);
        }

        [Fact]
        public void TryGetDeletableResource_RefuseUnHrefHorsCalendrier()
        {
            var ok = CalendarEventHref.TryGetDeletableResource(
                "https://autre.example/dav/user/Calendar/abc.ics",
                new[] { Collection },
                out _);

            Assert.False(ok);
        }

        [Fact]
        public void TryGetDeletableResource_AligneUnHrefRelatifSurLaCollectionAbsolue()
        {
            var ok = CalendarEventHref.TryGetDeletableResource(
                "/dav/user/Calendar/abc.ics",
                new[] { Collection },
                out var resource);

            Assert.True(ok);
            Assert.Equal("/dav/user/Calendar/abc.ics", resource);
        }

        [Fact]
        public void SharesResource_ReconnaitLeMemeFichier()
        {
            Assert.True(CalendarEventHref.SharesResource(EventHref, "/dav/user/Calendar/abc.ics"));
            Assert.False(CalendarEventHref.SharesResource(EventHref, "/dav/user/Calendar/autre.ics"));
        }

        [Fact]
        public async Task DeleteEventAsync_SupprimeLaRessourceEtPasLaCollection()
        {
            var client = new RecordingCalDavClient();
            var service = new CalendarCalDAVService(client);

            await service.DeleteEventAsync(EventHref, "etag-1", new[] { Collection });

            Assert.Equal(EventHref, client.DeletedHref);
            Assert.Equal("etag-1", client.DeletedEtag);
        }

        [Fact]
        public async Task DeleteEventAsync_RefuseDeViderLeCalendrier()
        {
            var client = new RecordingCalDavClient();
            var service = new CalendarCalDAVService(client);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.DeleteEventAsync(Collection, null, new[] { Collection }));

            Assert.False(string.IsNullOrWhiteSpace(error.Message));
            Assert.Null(client.DeletedHref);
        }

        private sealed class RecordingCalDavClient : ICalDAVClient
        {
            public string? DeletedHref { get; private set; }

            public string? DeletedEtag { get; private set; }

            public Task DeleteAsync(string href, string? etag = null)
            {
                DeletedHref = href;
                DeletedEtag = etag;
                return Task.CompletedTask;
            }

            public Task<XDocument> PropfindAsync(string href, int depth, string? requestBody = null) =>
                throw new NotSupportedException();

            public Task<XDocument> ReportAsync(string href, string requestBody) =>
                throw new NotSupportedException();

            public Task<string?> PutAsync(string href, string icalData, string? etag = null) =>
                throw new NotSupportedException();

            public Task<List<CalDAVCalendarInfo>> DiscoverCalendarsAsync() =>
                throw new NotSupportedException();

            public void Dispose()
            {
            }
        }
    }
}
