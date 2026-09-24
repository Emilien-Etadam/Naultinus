using Naultinus.Helpers;
using Naultinus.Model;
using Naultinus.Properties;
using Naultinus.Services;
using Naultinus.ViewModel;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Naultinus.View
{
    public partial class EditCalendarNaultinus : Window
    {
        private bool _accountUsable;
        private bool _calendarsLoaded;
        private bool _selectionTouched;
        private bool _suppressSelection;
        private int _loadInProgress;

        public EditCalendarNaultinus()
        {
            InitializeComponent();
        }

        public EditCalendarNaultinus(CalendarNaultinusViewModel viewModel) : this()
        {
            DataContext = viewModel;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ViewModeCombo.ItemsSource = new[] { CalendarViewMode.Agenda, CalendarViewMode.Day, CalendarViewMode.Week };
            if (DataContext is CalendarNaultinusViewModel vm)
                ViewModeCombo.SelectedItem = vm.ViewMode;

            var settings = AppSettingsStore.Load();
            _accountUsable = SharedCalDavAccount.IsUsable(settings);
            AccountStatusText.Text = _accountUsable
                ? SharedCalDavAccount.DescribeStatus(settings)
                : Strings.SharedCalDavMissing;
            LoadCalendarsButton.IsEnabled = _accountUsable;
            CalendarsListBox.IsEnabled = _accountUsable;
            if (_accountUsable)
                await LoadCalendarsAsync();
        }

        private async void LoadCalendarsButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadCalendarsAsync();
        }

        private async Task LoadCalendarsAsync()
        {
            if (Interlocked.Exchange(ref _loadInProgress, 1) == 1)
                return;

            try
            {
                if (!SharedCalDavAccount.IsUsable(AppSettingsStore.Load()))
                {
                    ShowAccountMissing();
                    return;
                }

                LoadCalendarsButton.IsEnabled = false;
                SaveButton.IsEnabled = false;
                var calendars = await CalendarCalDAVService.DiscoverSharedCalendarsAsync();
                if (!IsLoaded)
                    return;
                if (calendars == null)
                {
                    ShowAccountMissing();
                    return;
                }

                if (calendars.Count == 0)
                {
                    _calendarsLoaded = false;
                    MessageBox.Show(Strings.CaldavNoCalendar, Strings.CalendarTitle, MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var storedIds = DataContext is CalendarNaultinusViewModel vm
                    ? vm.CalendarIds
                    : Array.Empty<string>();
                SelectStoredCalendars(calendars, storedIds);
            }
            catch (Exception ex)
            {
                if (!IsLoaded)
                    return;
                _calendarsLoaded = false;
                MessageBox.Show(
                    string.Format(CultureInfo.CurrentCulture, Strings.CaldavLoadCalendarFailedFormat, ex.Message),
                    Strings.CalendarTitle,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            finally
            {
                Interlocked.Exchange(ref _loadInProgress, 0);
                if (IsLoaded)
                {
                    _accountUsable = SharedCalDavAccount.IsUsable(AppSettingsStore.Load());
                    LoadCalendarsButton.IsEnabled = _accountUsable;
                    CalendarsListBox.IsEnabled = _accountUsable;
                    SaveButton.IsEnabled = true;
                }
            }
        }

        private void ShowAccountMissing()
        {
            _accountUsable = false;
            _calendarsLoaded = false;
            AccountStatusText.Text = Strings.SharedCalDavMissing;
            LoadCalendarsButton.IsEnabled = false;
            CalendarsListBox.IsEnabled = false;
            MessageBox.Show(Strings.SharedCalDavMissing, Strings.CalendarTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void SelectStoredCalendars(IReadOnlyList<CalDAVCalendarInfo> calendars, IReadOnlyList<string> storedIds)
        {
            var selected = new HashSet<string>(StringComparer.Ordinal);
            foreach (var storedId in storedIds)
            {
                if (string.IsNullOrWhiteSpace(storedId))
                    continue;
                selected.Add(storedId);
                selected.Add(storedId.Trim().TrimEnd('/'));
            }

            _suppressSelection = true;
            try
            {
                CalendarsListBox.ItemsSource = calendars;
                CalendarsListBox.SelectedItems.Clear();
                foreach (var calendar in calendars)
                {
                    var href = calendar.Href ?? string.Empty;
                    if (selected.Contains(href) || selected.Contains(href.Trim().TrimEnd('/')))
                        CalendarsListBox.SelectedItems.Add(calendar);
                }

                _calendarsLoaded = true;
                _selectionTouched = false;
            }
            finally
            {
                _suppressSelection = false;
            }
        }

        private void CalendarsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSelection || !_calendarsLoaded)
                return;
            _selectionTouched = true;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is CalendarNaultinusViewModel vm)
            {
                if (ViewModeCombo.SelectedItem is CalendarViewMode mode)
                    vm.ViewMode = mode;

                // Sans liste chargée, ou sans compte, on garde CalendarIds.
                // Une sélection vide n'est écrite que si l'utilisateur l'a vraiment modifiée.
                if (_calendarsLoaded && SharedCalDavAccount.IsUsable(AppSettingsStore.Load()))
                {
                    var ids = CalendarsListBox.SelectedItems
                        .Cast<CalDAVCalendarInfo>()
                        .Select(calendar => calendar.Href)
                        .ToList();
                    if (ids.Count > 0 || _selectionTouched)
                        vm.ApplyCalendarSelection(ids);
                }

                vm.Save();
            }

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
