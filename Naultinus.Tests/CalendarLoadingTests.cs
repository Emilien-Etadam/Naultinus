using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Naultinus.Helpers;
using Naultinus.Model;
using Naultinus.Services;
using Naultinus.ViewModel;
using Xunit;

namespace Naultinus.Tests
{
    /// <summary>
    /// La barre « Chargement… » suit un REPORT CalDAV et s'éteint à la fin, y compris en erreur.
    /// Sans requête (agenda local), elle ne s'allume pas.
    /// </summary>
    public class CalendarLoadingTests
    {
        [Fact]
        public async Task AgendaLocal_NeMontrePasLaBarre_EtNeFaitPasDeRequete()
        {
            using var scope = NewScope(remote: false);
            var seen = false;
            scope.ViewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(CalendarNaultinusViewModel.IsLoading) && scope.ViewModel.IsLoading)
                    seen = true;
            };

            await scope.ViewModel.LoadEventsAsync();

            Assert.False(seen);
            Assert.False(scope.ViewModel.IsLoading);
            Assert.Equal(0, scope.Service.Calls);
        }

        [Fact]
        public async Task FetchCalDav_AllumeLaBarre_PuisLEteint()
        {
            using var scope = NewScope(remote: true);
            var gate = new TaskCompletionSource<List<CalendarEvent>>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool? loadingDuringFetch = null;
            scope.Service.OnGet = () =>
            {
                loadingDuringFetch = scope.ViewModel.IsLoading;
                return gate.Task;
            };

            var pending = scope.ViewModel.LoadEventsAsync();

            Assert.Equal(true, loadingDuringFetch);
            Assert.True(scope.ViewModel.IsLoading);
            gate.SetResult(new List<CalendarEvent>());
            await pending;

            Assert.False(scope.ViewModel.IsLoading);
            Assert.Equal(1, scope.Service.Calls);
            Assert.Equal(string.Empty, scope.ViewModel.ErrorMessage);
        }

        [Fact]
        public async Task FetchCalDav_EnErreur_EteintLaBarre()
        {
            using var scope = NewScope(remote: true);
            scope.Service.OnGet = () => Task.FromException<List<CalendarEvent>>(new InvalidOperationException("calendrier injoignable"));

            await scope.ViewModel.LoadEventsAsync();

            Assert.False(scope.ViewModel.IsLoading);
            Assert.Equal(1, scope.Service.Calls);
            Assert.Contains("calendrier injoignable", scope.ViewModel.ErrorMessage, StringComparison.Ordinal);

            scope.Service.OnGet = () => Task.FromResult(new List<CalendarEvent>());
            await scope.ViewModel.LoadEventsAsync();

            Assert.False(scope.ViewModel.IsLoading);
            Assert.Equal(string.Empty, scope.ViewModel.ErrorMessage);
        }

        [Fact]
        public async Task FetchCoalesced_EteintLaBarre_QuandLaDerniereRequeteFinit()
        {
            using var scope = NewScope(remote: true);
            TaskCompletionSource<List<CalendarEvent>>? first = null;
            TaskCompletionSource<List<CalendarEvent>>? second = null;
            scope.Service.OnGet = () =>
            {
                var gate = new TaskCompletionSource<List<CalendarEvent>>(TaskCreationOptions.RunContinuationsAsynchronously);
                if (scope.Service.Calls == 1)
                    first = gate;
                else
                    second = gate;
                return gate.Task;
            };

            var load = scope.ViewModel.LoadEventsAsync();
            Assert.Equal(1, scope.Service.Calls);
            Assert.True(scope.ViewModel.IsLoading);

            var overlapped = scope.ViewModel.LoadEventsAsync();
            await overlapped;
            Assert.True(scope.ViewModel.IsLoading);

            first!.SetResult(new List<CalendarEvent>());
            await WaitUntil(() => scope.Service.Calls >= 2);
            Assert.True(scope.ViewModel.IsLoading);

            second!.SetResult(new List<CalendarEvent>());
            await load;

            Assert.False(scope.ViewModel.IsLoading);
            Assert.Equal(2, scope.Service.Calls);
        }

        private static async Task WaitUntil(Func<bool> condition)
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                if (condition())
                    return;
                await Task.Delay(10);
            }

            throw new TimeoutException("La condition n'a pas été atteinte.");
        }

        private static Scope NewScope(bool remote)
        {
            var model = new CalendarNaultinusModel
            {
                Name = "calendar-loading-test",
                Width = 400,
                Height = 300,
                ViewMode = CalendarViewMode.Agenda,
                DaysToShow = 30,
                AgendaDaysToShow = 30,
                CalendarIds = remote ? new List<string> { "/dav/cal" } : new List<string>(),
            };
            var service = new GateCalendarService();
            var viewModel = new CalendarNaultinusViewModel(model, service, sharedAccountConfigured: remote, startBackgroundWork: false);
            return new Scope(viewModel, service);
        }

        private sealed class Scope : IDisposable
        {
            public Scope(CalendarNaultinusViewModel viewModel, GateCalendarService service)
            {
                ViewModel = viewModel;
                Service = service;
            }

            public CalendarNaultinusViewModel ViewModel { get; }

            public GateCalendarService Service { get; }

            public void Dispose()
            {
                var identifier = ViewModel.Identifier;
                ViewModel.Dispose();
                var directory = AppPaths.GetNaultinusDirectory(identifier);
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        private sealed class GateCalendarService : ICalendarCalDAVService
        {
            private int _calls;

            public int Calls => _calls;

            public Func<Task<List<CalendarEvent>>>? OnGet { get; set; }

            public Task<List<CalDAVCalendarInfo>> GetCalendarListAsync() => Task.FromResult(new List<CalDAVCalendarInfo>());

            public Task<List<CalendarEvent>> GetEventsAsync(string calendarHref, DateTime start, DateTime rangeEnd, string colorHex)
            {
                Interlocked.Increment(ref _calls);
                return OnGet != null ? OnGet() : Task.FromResult(new List<CalendarEvent>());
            }

            public Task<string?> CreateEventAsync(string calendarHref, string icalData) => Task.FromResult<string?>(null);

            public Task DeleteEventAsync(string eventHref, string? etag, IReadOnlyList<string>? calendarCollectionHrefs) => Task.CompletedTask;
        }
    }
}
