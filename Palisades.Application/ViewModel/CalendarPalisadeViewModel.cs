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
using System.Linq;
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
        private int _loadEventsInProgress;
        private bool _disposed;
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
            if (_disposed || Interlocked.Exchange(ref _loadEventsInProgress, 1) == 1)
                return;

            Dispatch(() => { IsLoading = true; ErrorMessage = ""; });
            try
            {
                if (_model.CalendarIds == null || _model.CalendarIds.Count == 0)
                {
                    Dispatch(() => { Events.Clear(); ErrorMessage = Strings.CalendarNoCalendarsConfigured; });
                    Dispatch(() => OnPropertyChanged(nameof(HasNoEvents)));
                    return;
                }

                IsLoading = true;
                ErrorMessage = "";
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
                Interlocked.Exchange(ref _loadEventsInProgress, 0);
            }
        }

        private void StartRefreshTimer()
        {
            _refreshTimer?.Dispose();
            _refreshTimer = new Timer(async _ =>
            {
                if (_disposed)
                    return;

                await LoadEventsAsync();
            }, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        private async void ShowAddEventDialog()
        {
            var dialog = new AddCalendarEventDialog();
            try { dialog.Owner = PalisadesManager.GetWindow(Identifier); }
            catch (KeyNotFoundException) { /* fenêtre non enregistrée : dialogue sans owner */ }
            if (dialog.ShowDialog() == true && dialog.NewEvent != null)
            {
                try
                {
                    await CreateEventAsync(dialog.NewEvent);
                    await LoadEventsAsync();
                }
                catch (Exception ex)
                {
                    // Sans ce catch, un échec du PUT CalDAV partait dans un async void et était
                    // avalé par le handler global : l'utilisateur croyait l'événement créé.
                    Dispatch(() => ErrorMessage = ex.Message);
                }
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

        public ICommand PreviousDayCommand { get; }
        public ICommand NextDayCommand { get; }
        public ICommand TodayCommand { get; }
        public ICommand AddEventCommand { get; }
        public ICommand RefreshCommand { get; } = new RelayCommand<CalendarPalisadeViewModel>(async vm => { if (vm != null) await vm.LoadEventsAsync(); });

        public override void Dispose()
        {
            _disposed = true;
            _refreshTimer?.Dispose();
            _refreshTimer = null;
            (_calendarService as IDisposable)?.Dispose();
            base.Dispose();
        }
    }
}
