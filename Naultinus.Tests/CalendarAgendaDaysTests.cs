using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Naultinus.Helpers;
using Naultinus.Model;
using Naultinus.Services;
using Naultinus.ViewModel;
using Xunit;

namespace Naultinus.Tests
{
    /// <summary>
    /// Le nombre de jours d'agenda saisi (30 par défaut, ou 90) survit à un aller-retour
    /// jour/semaine. Ces modes gardent leur date.
    /// </summary>
    public class CalendarAgendaDaysTests
    {
        [Fact]
        public void NouvelAgenda_Affiche30Jours()
        {
            var model = new CalendarNaultinusModel();

            Assert.Equal(CalendarViewMode.Agenda, model.ViewMode);
            Assert.Equal(CalendarSpan.DefaultAgendaDays, model.DaysToShow);
            Assert.Equal(30, model.AgendaDaysToShow);
            Assert.Equal(30, CalendarSpan.DaysForMode(CalendarViewMode.Agenda, 0));
        }

        [Theory]
        [InlineData(30)]
        [InlineData(90)]
        public void QuitterAgenda_PuisYRevenir_GardeLeNombreSaisi(int days)
        {
            var model = new CalendarNaultinusModel
            {
                ViewMode = CalendarViewMode.Agenda,
                DaysToShow = days,
                AgendaDaysToShow = days,
            };
            using var scope = NewScope(model, remote: false);
            var vm = scope.ViewModel;
            var date = new DateTime(2026, 4, 3);

            vm.ViewMode = CalendarViewMode.Week;
            vm.SelectedDate = date;
            Assert.Equal(CalendarSpan.WeekSpan, vm.DaysToShow);
            Assert.Equal(days, model.AgendaDaysToShow);
            Assert.Equal(date, vm.SelectedDate);

            vm.ViewMode = CalendarViewMode.Day;
            Assert.Equal(CalendarSpan.DaySpan, vm.DaysToShow);
            Assert.Equal(date, vm.SelectedDate);

            vm.ViewMode = CalendarViewMode.Agenda;
            Assert.Equal(days, vm.DaysToShow);
            Assert.Equal(days, model.AgendaDaysToShow);
            Assert.Equal(date, vm.SelectedDate);

            vm.ViewMode = CalendarViewMode.Week;
            Assert.Equal(date, vm.SelectedDate);
            Assert.Equal(CalendarSpan.WeekSpan, vm.DaysToShow);
        }

        [Fact]
        public void ReassignerAgenda_NeRemplacePasLeNombreSaisi()
        {
            using var scope = NewScope(new CalendarNaultinusModel(), remote: false);
            scope.ViewModel.DaysToShow = 90;

            scope.ViewModel.ViewMode = CalendarViewMode.Agenda;

            Assert.Equal(90, scope.ViewModel.DaysToShow);
            Assert.Equal(90, scope.Model.AgendaDaysToShow);
        }

        [Fact]
        public void SaisieEnSemaine_EstRetrouveeEnAgenda()
        {
            using var scope = NewScope(new CalendarNaultinusModel { DaysToShow = 30, AgendaDaysToShow = 30 }, remote: false);
            var vm = scope.ViewModel;

            vm.ViewMode = CalendarViewMode.Week;
            vm.DaysToShow = 90;
            vm.ViewMode = CalendarViewMode.Day;
            vm.ViewMode = CalendarViewMode.Agenda;

            Assert.Equal(90, vm.DaysToShow);
            Assert.Equal(90, scope.Model.AgendaDaysToShow);
        }

        [Fact]
        public void AncienFichierAgenda_SansPreference_ReprendLeNombreEnregistre()
        {
            var model = new CalendarNaultinusModel
            {
                ViewMode = CalendarViewMode.Agenda,
                DaysToShow = 90,
                AgendaDaysToShow = 0,
            };
            using var scope = NewScope(model, remote: false);

            Assert.Equal(90, scope.ViewModel.DaysToShow);
            Assert.Equal(90, model.AgendaDaysToShow);

            scope.ViewModel.ViewMode = CalendarViewMode.Week;
            scope.ViewModel.ViewMode = CalendarViewMode.Agenda;

            Assert.Equal(90, scope.ViewModel.DaysToShow);
        }

        [Fact]
        public void FichierSemaine_AvecPreference90_LaRestitueEnAgenda()
        {
            var date = new DateTime(2026, 5, 11);
            var model = new CalendarNaultinusModel
            {
                ViewMode = CalendarViewMode.Week,
                DaysToShow = 7,
                AgendaDaysToShow = 90,
            };
            using var scope = NewScope(model, remote: false);
            scope.ViewModel.SelectedDate = date;

            scope.ViewModel.ViewMode = CalendarViewMode.Agenda;

            Assert.Equal(90, scope.ViewModel.DaysToShow);
            Assert.Equal(date, scope.ViewModel.SelectedDate);
        }

        private static Scope NewScope(CalendarNaultinusModel model, bool remote)
        {
            model.Name = "agenda-days-test";
            model.Width = 400;
            model.Height = 300;
            var viewModel = new CalendarNaultinusViewModel(model, new IdleCalendarService(), remote, startBackgroundWork: false);
            return new Scope(model, viewModel);
        }

        private sealed class Scope : IDisposable
        {
            public Scope(CalendarNaultinusModel model, CalendarNaultinusViewModel viewModel)
            {
                Model = model;
                ViewModel = viewModel;
            }

            public CalendarNaultinusModel Model { get; }

            public CalendarNaultinusViewModel ViewModel { get; }

            public void Dispose()
            {
                var identifier = ViewModel.Identifier;
                ViewModel.Dispose();
                var directory = AppPaths.GetNaultinusDirectory(identifier);
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        private sealed class IdleCalendarService : ICalendarCalDAVService
        {
            public Task<List<CalDAVCalendarInfo>> GetCalendarListAsync() => Task.FromResult(new List<CalDAVCalendarInfo>());

            public Task<List<CalendarEvent>> GetEventsAsync(string calendarHref, DateTime start, DateTime rangeEnd, string colorHex) =>
                Task.FromResult(new List<CalendarEvent>());

            public Task<string?> CreateEventAsync(string calendarHref, string icalData) => Task.FromResult<string?>(null);

            public Task DeleteEventAsync(string eventHref, string? etag, IReadOnlyList<string>? calendarCollectionHrefs) => Task.CompletedTask;
        }
    }
}
