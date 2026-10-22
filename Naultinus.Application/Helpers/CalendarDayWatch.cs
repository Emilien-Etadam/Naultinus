using System;
using System.Threading;

namespace Naultinus.Helpers
{
    /// <summary>Réarme un timer jusqu'au prochain minuit local, puis invoque un callback UI.</summary>
    internal sealed class CalendarDayWatch : IDisposable
    {
        private readonly Func<bool> _isDisposed;
        private readonly Action<Action> _dispatch;
        private readonly Action _onLocalDayChanged;
        private Timer? _timer;
        private bool _disposed;

        public CalendarDayWatch(Func<bool> isDisposed, Action<Action> dispatch, Action onLocalDayChanged)
        {
            _isDisposed = isDisposed ?? throw new ArgumentNullException(nameof(isDisposed));
            _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
            _onLocalDayChanged = onLocalDayChanged ?? throw new ArgumentNullException(nameof(onLocalDayChanged));
        }

        public void Start()
        {
            if (_disposed)
                return;

            var delay = DelayUntilNextLocalMidnight(DateTime.Now);
            if (_timer == null)
                _timer = new Timer(OnTick, null, delay, Timeout.InfiniteTimeSpan);
            else
                _timer.Change(delay, Timeout.InfiniteTimeSpan);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }

        internal static TimeSpan DelayUntilNextLocalMidnight(DateTime localNow)
        {
            var delay = localNow.Date.AddDays(1) - localNow;
            return delay < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : delay;
        }

        private void OnTick(object? state)
        {
            if (_disposed || _isDisposed())
                return;

            _dispatch(() =>
            {
                if (_disposed || _isDisposed())
                    return;
                _onLocalDayChanged();
                if (!_disposed && !_isDisposed())
                    Start();
            });
        }
    }
}
