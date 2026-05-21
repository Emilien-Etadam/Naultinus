using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using Palisades;
using Palisades.Helpers;
using Palisades.Properties;
using Palisades.Model;
using Palisades.Services;
using Palisades.View;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace Palisades.ViewModel
{
    public class CalendarPalisadeViewModel : ViewModelBase
    {
        private readonly CalendarPalisadeModel _model;
        private readonly ICalendarCalDAVService _calendarService;
        private DateTime _selectedDate = DateTime.Today;
        private string _errorMessage = string.Empty;
        private bool _isLoading;
        private Timer? _refreshTimer;
        private readonly HashSet<string> _notifiedEventUids = new HashSet<string>();
        private static readonly CalendarSerializer _calendarSerializer = new CalendarSerializer();

        public CalendarPalisadeViewModel() : this(
            new CalendarPalisadeModel { Name = "Calendar palisade", Width = 500, Height = 400 },
            new CalendarCalDAVService(new CalDAVClient("https://localhost/", "", "")))
        { }

        public CalendarPalisadeViewModel(CalendarPalisadeModel model, ICalendarCalDAVService calendarService)
            : base(model)
        {
            _model = model;
            _calendarService = calendarService;
            Events = new ObservableCollection<Model.CalendarEvent>();
            PreviousDayCommand = new RelayCommand(() => SelectedDate = SelectedDate.AddDays(-DaysToShow));
            NextDayCommand = new RelayCommand(() => SelectedDate = SelectedDate.AddDays(DaysToShow));
            TodayCommand = new RelayCommand(() => SelectedDate = DateTime.Today);
            AddEventCommand = new RelayCommand(() => ShowAddEventDialog());
            _ = LoadEventsAsync();
            StartRefreshTimer();
        }

        public ObservableCollection<Model.CalendarEvent> Events { get; }

        public CalendarViewMode[] ViewModes { get; } = Enum.GetValues<CalendarViewMode>();

        public CalendarViewMode ViewMode
        {
            get => _model.ViewMode;
            set
            {
                _model.ViewMode = value;
                OnPropertyChanged();
                Save();
                DaysToShow = value switch
                {
                    CalendarViewMode.Day => 1,
                    CalendarViewMode.Week => 7,
                    CalendarViewMode.Agenda => 14,
                    _ => 7
                };
            }
        }

        public DateTime SelectedDate
        {
            get => _selectedDate;
            set { _selectedDate = value; OnPropertyChanged(); OnPropertyChanged(nameof(DateRangeDisplay)); _ = LoadEventsAsync(); }
        }

        public int DaysToShow
        {
            get => _model.DaysToShow;
            set { _model.DaysToShow = value; OnPropertyChanged(); Save(); OnPropertyChanged(nameof(DateRangeDisplay)); _ = LoadEventsAsync(); }
        }

        public string DateRangeDisplay => DaysToShow == 1
            ? SelectedDate.ToString("ddd dd MMM yyyy")
            : SelectedDate.ToString("ddd dd MMM") + " → " + SelectedDate.AddDays(DaysToShow - 1).ToString("ddd dd MMM yyyy");

        public string ErrorMessage
        {
            get => _errorMessage;
            set { _errorMessage = value; OnPropertyChanged(); }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; OnPropertyChanged(); }
        }

        public bool HasNoEvents => !IsLoading && Events.Count == 0;

        public async Task LoadEventsAsync()
        {
            if (_model.CalendarIds == null || _model.CalendarIds.Count == 0)
            {
                Dispatch(() => { Events.Clear(); ErrorMessage = Strings.CalendarNoCalendarsConfigured; });
                Dispatch(() => OnPropertyChanged(nameof(HasNoEvents)));
                return;
            }
            Dispatch(() => { IsLoading = true; ErrorMessage = ""; });
            try
            {
                var start = SelectedDate.Date;
                var end = start.AddDays(DaysToShow);
                var allEvents = new List<Model.CalendarEvent>();
                foreach (var calId in _model.CalendarIds)
                {
                    var list = await _calendarService.GetEventsAsync(calId, start, end);
                    allEvents.AddRange(list);
                }
                allEvents = allEvents.Where(e => e.DtEnd > start && e.DtStart < end).ToList();
                var ordered = allEvents.OrderBy(e => e.DtStart).ToList();
                DateTime? prevDate = null;
                foreach (var evt in ordered)
                {
                    var evtDate = evt.DtStart.Date;
                    evt.IsToday = evtDate == DateTime.Today;
                    if (evtDate != prevDate)
                    {
                        evt.DayHeader = evt.DtStart.ToString("ddd dd MMM");
                        prevDate = evtDate;
                    }
                }
                Dispatch(() =>
                {
                    Events.Clear();
                    foreach (var evt in ordered)
                        Events.Add(evt);
                    OnPropertyChanged(nameof(HasNoEvents));
                    var now = DateTime.Now;
                    var threshold = now.AddMinutes(15);
                    foreach (var evt in Events)
                    {
                        if (evt.DtStart >= now && evt.DtStart <= threshold && _notifiedEventUids.Add(evt.Uid))
                            ToastHelper.ShowEventReminder(evt.Summary, evt.DtStart);
                    }
                });
            }
            catch (Exception ex)
            {
                Dispatch(() => ErrorMessage = ex.Message);
            }
            finally
            {
                Dispatch(() => { IsLoading = false; OnPropertyChanged(nameof(HasNoEvents)); });
            }
        }

        private void StartRefreshTimer()
        {
            _refreshTimer?.Dispose();
            _refreshTimer = new Timer(async _ => await LoadEventsAsync(), null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        private async void ShowAddEventDialog()
        {
            var dialog = new AddCalendarEventDialog();
            try { dialog.Owner = PalisadesManager.GetWindow(Identifier); } catch { }
            if (dialog.ShowDialog() == true && dialog.NewEvent != null)
            {
                await CreateEventAsync(dialog.NewEvent);
                await LoadEventsAsync();
            }
        }

        private async Task CreateEventAsync(Model.CalendarEvent evt)
        {
            if (_model.CalendarIds == null || _model.CalendarIds.Count == 0) return;
            var calendar = new Ical.Net.Calendar();
            var dtStart = evt.IsAllDay
                ? new CalDateTime(evt.DtStart.Year, evt.DtStart.Month, evt.DtStart.Day)
                : new CalDateTime(evt.DtStart);
            var dtEnd = evt.IsAllDay
                ? new CalDateTime(evt.DtEnd.Year, evt.DtEnd.Month, evt.DtEnd.Day)
                : new CalDateTime(evt.DtEnd);
            var vevent = new Ical.Net.CalendarComponents.CalendarEvent
            {
                Uid = Guid.NewGuid().ToString(),
                Summary = evt.Summary,
                Description = evt.Description ?? "",
                Location = evt.Location ?? "",
                DtStart = dtStart,
                DtEnd = dtEnd
            };
            calendar.Events.Add(vevent);
            var icalData = _calendarSerializer.SerializeToString(calendar);
            await _calendarService.CreateEventAsync(_model.CalendarIds[0], icalData ?? "");
        }

        private static void Dispatch(Action action)
        {
            if (Application.Current?.Dispatcher != null)
                Application.Current.Dispatcher.BeginInvoke(action);
            else
                action();
        }

        public ICommand PreviousDayCommand { get; }
        public ICommand NextDayCommand { get; }
        public ICommand TodayCommand { get; }
        public ICommand AddEventCommand { get; }
        public ICommand RefreshCommand { get; } = new RelayCommand<CalendarPalisadeViewModel>(async vm => { if (vm != null) await vm.LoadEventsAsync(); });

        public ICommand EditCalendarPalisadeCommand { get; } = new RelayCommand<CalendarPalisadeViewModel>(vm =>
        {
            if (vm == null) return;
            var edit = new EditCalendarPalisade(vm);
            try { edit.Owner = PalisadesManager.GetWindow(vm.Identifier); } catch { }
            edit.ShowDialog();
        });

        public override void Dispose()
        {
            _refreshTimer?.Dispose();
            base.Dispose();
        }
    }
}
