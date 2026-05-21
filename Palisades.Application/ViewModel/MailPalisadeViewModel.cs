using Palisades.Helpers;
using Palisades.Properties;
using Palisades;
using Palisades.Model;
using Palisades.Services;
using Palisades.View;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace Palisades.ViewModel
{
    public class MailPalisadeViewModel : ViewModelBase
    {
        private readonly MailPalisadeModel _model;
        private IImapMailService? _mailService;
        private string _errorMessage = string.Empty;
        private bool _isConnected;
        private bool _isLoading;
        private Timer? _pollTimer;
        private readonly object _countsLock = new object();
        private Dictionary<string, int> _unreadCounts = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _previousUnreadCounts = new Dictionary<string, int>();
        private bool _firstLoad = true;

        public MailPalisadeViewModel() : this(new MailPalisadeModel { Name = Strings.MailDefaultName, Width = 320, Height = 240 })
        { }

        public MailPalisadeViewModel(MailPalisadeModel model) : base(model)
        {
            _model = model;
            RecentSubjects = new ObservableCollection<MailSummaryItem>();
            _ = EnsureConnectedAndRefreshAsync();
            StartPollTimer();
        }

        public ObservableCollection<MailSummaryItem> RecentSubjects { get; }

        public int TotalUnreadCount
        {
            get
            {
                lock (_countsLock)
                    return _unreadCounts.Values.Sum();
            }
        }

        public string UnreadCountsDisplay
        {
            get
            {
                lock (_countsLock)
                {
                    if (_unreadCounts.Count == 0) return "";
                    if (_unreadCounts.Count == 1) return _unreadCounts.First().Value.ToString();
                    return string.Join(", ", _unreadCounts.Select(kv => $"{kv.Key}: {kv.Value}"));
                }
            }
        }

        public MailDisplayMode DisplayMode
        {
            get => _model.DisplayMode;
            set { _model.DisplayMode = value; OnPropertyChanged(); Save(); }
        }

        public bool IsConnected
        {
            get => _isConnected;
            set { _isConnected = value; OnPropertyChanged(); }
        }

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

        private void StartPollTimer()
        {
            _pollTimer?.Dispose();
            int minutes = _model.PollIntervalMinutes <= 0 ? 3 : _model.PollIntervalMinutes;
            _pollTimer = new Timer(async _ => await RefreshAsync(), null, TimeSpan.FromMinutes(minutes), TimeSpan.FromMinutes(minutes));
        }

        private async Task EnsureConnectedAndRefreshAsync()
        {
            if (_mailService == null)
                _mailService = PalisadeFactory.CreateImapMailService(_model);
            try
            {
                await _mailService.ConnectAsync();
                Dispatch(() => { IsConnected = true; ErrorMessage = ""; });
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                Dispatch(() => { IsConnected = false; ErrorMessage = ex.Message; });
            }
        }

        public async Task RefreshAsync()
        {
            if (_mailService == null || !_mailService.IsConnected)
            {
                await EnsureConnectedAndRefreshAsync();
                return;
            }
            IsLoading = true;
            try
            {
                var counts = new Dictionary<string, int>();
                foreach (var folder in _model.MonitoredFolders ?? new List<string> { "INBOX" })
                {
                    try
                    {
                        var count = await _mailService.GetUnreadCountAsync(folder);
                        counts[folder] = count;
                    }
                    catch { counts[folder] = 0; }
                }
                lock (_countsLock)
                {
                    _unreadCounts = counts;
                    if (_firstLoad)
                    {
                        _firstLoad = false;
                        foreach (var kvp in _unreadCounts)
                            _previousUnreadCounts[kvp.Key] = kvp.Value;
                    }
                    else
                    {
                        foreach (var kvp in _unreadCounts)
                        {
                            _previousUnreadCounts.TryGetValue(kvp.Key, out var prev);
                            if (kvp.Value > prev)
                                ToastHelper.ShowMailNotification(kvp.Key, kvp.Value - prev);
                            _previousUnreadCounts[kvp.Key] = kvp.Value;
                        }
                    }
                }
                Dispatch(() =>
                {
                    OnPropertyChanged(nameof(TotalUnreadCount));
                    OnPropertyChanged(nameof(UnreadCountsDisplay));
                });
                if (_model.DisplayMode == MailDisplayMode.CountAndSubjects && _model.MonitoredFolders?.Count > 0)
                {
                    var firstFolder = _model.MonitoredFolders[0];
                    var subjects = await _mailService.GetRecentUnreadSubjectsAsync(firstFolder, _model.MaxSubjectsShown);
                    Dispatch(() =>
                    {
                        RecentSubjects.Clear();
                        foreach (var s in subjects)
                            RecentSubjects.Add(s);
                    });
                }
            }
            catch (Exception ex)
            {
                Dispatch(() => ErrorMessage = ex.Message);
            }
            finally
            {
                Dispatch(() => IsLoading = false);
            }
        }

        private static void Dispatch(Action action)
        {
            if (Application.Current?.Dispatcher != null)
                Application.Current.Dispatcher.BeginInvoke(action);
            else
                action();
        }

        public void OpenWebmail()
        {
            var url = _model.WebmailUrl?.Trim();
            if (string.IsNullOrEmpty(url)) return;
            try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
            catch { }
        }

        #region Commands

        public ICommand RefreshCommand { get; } = new RelayCommand<MailPalisadeViewModel>(async vm => { if (vm != null) await vm.RefreshAsync(); });
        public ICommand OpenWebmailCommand { get; } = new RelayCommand<MailPalisadeViewModel>(vm => { vm?.OpenWebmail(); });

        #endregion

        public override void Dispose()
        {
            _pollTimer?.Dispose();
            base.Dispose();
        }
    }
}
