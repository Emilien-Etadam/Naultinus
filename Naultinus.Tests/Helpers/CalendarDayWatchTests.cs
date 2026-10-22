using System;
using Naultinus.Helpers;
using Xunit;

namespace Naultinus.Tests.Helpers
{
    public class CalendarDayWatchTests
    {
        [Fact]
        public void DelayUntilNextLocalMidnight_IsAtLeastOneSecond()
        {
            var almostMidnight = new DateTime(2026, 10, 22, 23, 59, 59, 500);
            var delay = CalendarDayWatch.DelayUntilNextLocalMidnight(almostMidnight);
            Assert.True(delay >= TimeSpan.FromSeconds(1));
            Assert.True(delay <= TimeSpan.FromSeconds(2));
        }

        [Fact]
        public void DelayUntilNextLocalMidnight_FromNoon_IsAboutHalfDay()
        {
            var noon = new DateTime(2026, 10, 22, 12, 0, 0);
            var delay = CalendarDayWatch.DelayUntilNextLocalMidnight(noon);
            Assert.Equal(TimeSpan.FromHours(12), delay);
        }
    }
}
