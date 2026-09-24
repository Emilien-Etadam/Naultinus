using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using Naultinus;
using Naultinus.Helpers;
using Naultinus.Properties;
using Naultinus.Model;
using Naultinus.Services;
using Naultinus.View;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace Naultinus.ViewModel
{
    public class CalendarNaultinusViewModel : ViewModelBase
    {
        private readonly CalendarNaultinusModel _model;
        private readonly ICalendarCalDAVService _calendarService;
        private DateTime _selectedDate = DateTime.Today;
        private DateTime _agendaAnchor = DateTime.Today;
        private DateTime _observedLocalDate = DateTime.Today;
        private string _errorMessage = string.Empty;
        private bool _isLoading;
        private Timer? _refreshTimer;
        private Timer? _dayChangeTimer;
        private int _loadEventsInProgress;
        private int _reloadRequested;
        private bool _disposed;
        private readonly HashSet<string> _notifiedEventUids = new HashSet<string>();
        private static readonly CalendarSerializer _calendarSerializer = new CalendarSerializer();

        /// <summary>Figé à l'ouverture : le client CalDAV a été construit avec le compte partagé de ce moment-là.</summary>
        private readonly bool _sharedAccountConfigured = SharedCalDavAccount.IsConfigured();

        public CalendarNaultinusViewModel() : this(
            new CalendarNaultinusModel { Name = Strings.CalendarDefaultName, Width = 500, Height = 400 },
            new CalendarCalDAVService(new CalDAVClient("https://localhost/", "", "")))
        { }

        public CalendarNaultinusViewModel(CalendarNaultinusModel model, ICalendarCalDAVService calendarService)
            : base(model)
        {
            _model = model;
            _calendarService = calendarService;
            Events = new ObservableCollection<Model.CalendarEvent>();
            PreviousDayCommand = new RelayCommand(() => SetVisibleStart(VisibleStart.AddDays(-DaysToShow)));
            NextDayCommand = new RelayCommand(() => SetVisibleStart(VisibleStart.AddDays(DaysToShow)));
            TodayCommand = new RelayCommand(() => SetVisibleStart(DateTime.Today));
            AddEventCommand = new RelayCommand(() => ShowAddEventDialog());
            EditEventCommand = new RelayCommand<Model.CalendarEvent>(ShowEditEventDialog);
            DeleteEventCommand = new RelayCommand<Model.CalendarEvent>(DeleteLocalEvent);
            _ = LoadEventsAsync();
            StartDayWatch();
            if (IsRemote)
                StartRefreshTimer();
        }

        /// <summary>Synchro seulement si un compte partagé existe et que des calendriers ont été choisis.</summary>
        public bool IsRemote => LocalPlannerStore.UsesRemoteCalendars(_model, _sharedAccountConfigured);

        public IReadOnlyList<string> CalendarIds => _model.CalendarIds ?? new List<string>();

        public bool IsLocalMode => !IsRemote;

        public ObservableCollection<Model.CalendarEvent> Events { get; }
        public ObservableCollection<CalendarLegendItem> CalendarLegend { get; } = new ObservableCollection<CalendarLegendItem>();

        public bool HasCalendarLegend => CalendarLegend.Count > 0;

        public CalendarViewMode[] ViewModes { get; } = Enum.GetValues<CalendarViewMode>();

        public CalendarViewMode ViewMode
        {
            get => _model.ViewMode;
            set
            {
                // Réassigner le mode déjà actif (Enregistrer du dialogue) ne doit pas
                // écraser un DaysToShow saisi : Agenda forçait 14 ici.
                if (_model.ViewMode == value)
                    return;

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

        /// <summary>Ancre affichée : aujourd'hui en agenda, la date naviguée pour les autres modes.</summary>
        private DateTime VisibleStart => ViewMode == CalendarViewMode.Agenda ? _agendaAnchor : _selectedDate;

        public string DateRangeDisplay => DaysToShow == 1
            ? VisibleStart.ToString("ddd dd MMM yyyy")
            : VisibleStart.ToString("ddd dd MMM") + " → " + VisibleStart.AddDays(DaysToShow - 1).ToString("ddd dd MMM yyyy");

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
            if (_disposed)
                return;
            if (Interlocked.Exchange(ref _loadEventsInProgress, 1) == 1)
            {
                Interlocked.Exchange(ref _reloadRequested, 1);
                return;
            }

            Dispatch(() => { IsLoading = true; ErrorMessage = ""; });
            try
            {
                if (!IsRemote)
                {
                    // Sans compte, ou sans calendrier distant : afficher les événements du state.xml, sans réseau.
                    PublishLocalEvents();
                    if (_sharedAccountConfigured == false && _model.CalendarIds != null && _model.CalendarIds.Count > 0)
                        Dispatch(() => ErrorMessage = Strings.SharedCalDavMissing);
                    Dispatch(() => OnPropertyChanged(nameof(HasNoEvents)));
                    return;
                }

                IsLoading = true;
                ErrorMessage = "";
                var start = VisibleStart.Date;
                var end = start.AddDays(DaysToShow);
                var allEvents = new List<Model.CalendarEvent>();
                var colorsChanged = EnsureCalendarColors();
                UpdateCalendarLegend();
                for (var i = 0; i < _model.CalendarIds.Count; i++)
                {
                    var calId = _model.CalendarIds[i];
                    var color = _model.CalendarColors[calId];
                    var list = await _calendarService.GetEventsAsync(calId, start, end, color);
                    allEvents.AddRange(list);
                }
                if (colorsChanged)
                    Save();
                allEvents = allEvents.Where(e => e.DtEnd > start && e.DtStart < end).ToList();
                var ordered = allEvents.OrderBy(e => e.DtStart).ToList();
                DecorateDayHeaders(ordered);
                Dispatch(() => ShowEvents(ordered));
            }
            catch (Exception ex)
            {
                Dispatch(() => ErrorMessage = ex.Message);
            }
            finally
            {
                Dispatch(() => { IsLoading = false; OnPropertyChanged(nameof(HasNoEvents)); });
                Interlocked.Exchange(ref _loadEventsInProgress, 0);
                if (!_disposed && Interlocked.Exchange(ref _reloadRequested, 0) == 1)
                    _ = LoadEventsAsync();
            }
        }

        /// <summary>Remplace les calendriers affichés et recharge le panneau.</summary>
        public void ApplyCalendarSelection(IReadOnlyList<string> calendarIds)
        {
            var next = new List<string>();
            if (calendarIds != null)
            {
                foreach (var calendarId in calendarIds)
                {
                    if (string.IsNullOrWhiteSpace(calendarId) || next.Contains(calendarId))
                        continue;
                    next.Add(calendarId);
                }
            }

            _model.CalendarIds = next;
            OnPropertyChanged(nameof(CalendarIds));
            OnPropertyChanged(nameof(IsRemote));
            OnPropertyChanged(nameof(IsLocalMode));
            if (IsRemote)
            {
                if (_refreshTimer == null)
                    StartRefreshTimer();
            }
            else
            {
                _refreshTimer?.Dispose();
                _refreshTimer = null;
            }

            Save();
            _ = LoadEventsAsync();
        }

        private void PublishLocalEvents()
        {
            var start = VisibleStart.Date;
            var end = start.AddDays(DaysToShow);
            var ordered = LocalPlannerStore.EventsOverlapping(_model, start, end)
                .Select(LocalPlannerStore.ToCalendarEvent)
                .ToList();
            DecorateDayHeaders(ordered);
            Dispatch(() =>
            {
                CalendarLegend.Clear();
                OnPropertyChanged(nameof(HasCalendarLegend));
                ShowEvents(ordered);
            });
        }

        private static void DecorateDayHeaders(List<Model.CalendarEvent> ordered)
        {
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
        }

        private void ShowEvents(List<Model.CalendarEvent> ordered)
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

            // Borne la taille du set (sinon croissance sans fin) : on ne garde que les UID
            // des événements encore chargés.
            _notifiedEventUids.IntersectWith(Events.Select(e => e.Uid));
        }

        private bool EnsureCalendarColors()
        {
            var calendarIds = _model.CalendarIds ?? new List<string>();
            var changed = false;

            foreach (var calendarId in _model.CalendarColors.Keys.ToList())
            {
                if (!calendarIds.Contains(calendarId))
                {
                    _model.CalendarColors.Remove(calendarId);
                    changed = true;
                }
            }

            for (var i = 0; i < calendarIds.Count; i++)
            {
                var calendarId = calendarIds[i];
                var resolvedColor = CalendarColorHelper.ResolveColor(
                    i,
                    _model.CalendarColors.TryGetValue(calendarId, out var storedColor) ? storedColor : null);

                if (!_model.CalendarColors.TryGetValue(calendarId, out var currentColor)
                    || !string.Equals(currentColor, resolvedColor, StringComparison.OrdinalIgnoreCase))
                {
                    _model.CalendarColors[calendarId] = resolvedColor;
                    changed = true;
                }
            }

            return changed;
        }

        private void UpdateCalendarLegend()
        {
            Dispatch(() =>
            {
                CalendarLegend.Clear();
                if (_model.CalendarIds == null)
                {
                    OnPropertyChanged(nameof(HasCalendarLegend));
                    return;
                }

                foreach (var calendarId in _model.CalendarIds)
                {
                    if (!_model.CalendarColors.TryGetValue(calendarId, out var color))
                        continue;

                    CalendarLegend.Add(new CalendarLegendItem
                    {
                        DisplayName = CalendarColorHelper.GetDisplayNameFromHref(calendarId),
                        ColorHex = color,
                    });
                }

                OnPropertyChanged(nameof(HasCalendarLegend));
            });
        }

        private void SetVisibleStart(DateTime value)
        {
            var date = value.Date;
            if (ViewMode == CalendarViewMode.Agenda)
            {
                if (_agendaAnchor == date)
                    return;
                _agendaAnchor = date;
                OnPropertyChanged(nameof(DateRangeDisplay));
                _ = LoadEventsAsync();
                return;
            }

            SelectedDate = date;
        }

        /// <summary>Minuit, ou retour sur la fenêtre : recale l'agenda sur le jour local sans toucher aux autres modes.</summary>
        public void OnWindowActivated()
        {
            if (_disposed)
                return;
            AlignAgendaWithLocalDay(DateTime.Now);
        }

        private void AlignAgendaWithLocalDay(DateTime localNow)
        {
            var today = AgendaAnchor.LocalDate(localNow);
            if (!AgendaAnchor.ShouldRealign(ViewMode, _agendaAnchor, _observedLocalDate, localNow))
            {
                if (today != _observedLocalDate)
                {
                    _observedLocalDate = today;
                    if (ViewMode != CalendarViewMode.Agenda)
                        _agendaAnchor = today;
                }

                return;
            }

            _observedLocalDate = today;
            _agendaAnchor = today;
            OnPropertyChanged(nameof(DateRangeDisplay));
            _ = LoadEventsAsync();
        }

        private void StartDayWatch()
        {
            var delay = DelayUntilNextLocalMidnight(DateTime.Now);
            if (_dayChangeTimer == null)
                _dayChangeTimer = new Timer(OnDayWatch, null, delay, Timeout.InfiniteTimeSpan);
            else
                _dayChangeTimer.Change(delay, Timeout.InfiniteTimeSpan);
        }

        private void OnDayWatch(object? state)
        {
            if (_disposed)
                return;

            Dispatch(() =>
            {
                if (_disposed)
                    return;
                AlignAgendaWithLocalDay(DateTime.Now);
                if (!_disposed)
                    StartDayWatch();
            });
        }

        private static TimeSpan DelayUntilNextLocalMidnight(DateTime localNow)
        {
            var delay = localNow.Date.AddDays(1) - localNow;
            return delay < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : delay;
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
            try { dialog.Owner = NaultinusManager.GetWindow(Identifier); }
            catch (KeyNotFoundException) { /* fenêtre non enregistrée : dialogue sans owner */ }
            if (dialog.ShowDialog() == true && dialog.NewEvent != null)
            {
                try
                {
                    await CreateEventAsync(dialog.NewEvent, dialog.Draft);
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

        private void ShowEditEventDialog(Model.CalendarEvent? evt)
        {
            if (evt == null || !IsLocalMode)
                return;
            var dialog = new AddCalendarEventDialog(evt);
            try { dialog.Owner = NaultinusManager.GetWindow(Identifier); }
            catch (KeyNotFoundException) { /* fenêtre non enregistrée : dialogue sans owner */ }
            if (dialog.ShowDialog() != true || dialog.Draft == null)
                return;
            if (!LocalPlannerStore.TryUpdateEvent(_model, dialog.Draft, out var error))
            {
                ErrorMessage = LocalPlannerStore.Describe(error);
                return;
            }

            Save();
            _ = LoadEventsAsync();
        }

        private void DeleteLocalEvent(Model.CalendarEvent? evt)
        {
            if (evt == null || !IsLocalMode)
                return;
            if (!LocalPlannerStore.TryRemoveEvent(_model, evt.Uid, out var error))
            {
                ErrorMessage = LocalPlannerStore.Describe(error);
                return;
            }

            Save();
            _ = LoadEventsAsync();
        }

        private async Task CreateEventAsync(Model.CalendarEvent evt, StoredCalendarEvent? draft)
        {
            if (!IsRemote)
            {
                var stored = draft ?? new StoredCalendarEvent
                {
                    Uid = string.IsNullOrWhiteSpace(evt.Uid) ? Guid.NewGuid().ToString("D") : evt.Uid,
                    Summary = evt.Summary,
                    Description = evt.Description,
                    Location = evt.Location,
                    DtStart = evt.DtStart,
                    DtEnd = evt.DtEnd,
                    IsAllDay = evt.IsAllDay,
                };
                if (!LocalPlannerStore.TryAddEvent(_model, stored, out var error))
                    throw new InvalidOperationException(LocalPlannerStore.Describe(error));
                Save();
                return;
            }

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
        public ICommand EditEventCommand { get; }
        public ICommand DeleteEventCommand { get; }
        public ICommand RefreshCommand { get; } = new AsyncRelayCommand<CalendarNaultinusViewModel>(async vm => { if (vm != null) await vm.LoadEventsAsync(); });

        public override void Dispose()
        {
            _disposed = true;
            _refreshTimer?.Dispose();
            _refreshTimer = null;
            _dayChangeTimer?.Dispose();
            _dayChangeTimer = null;
            (_calendarService as IDisposable)?.Dispose();
            base.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
