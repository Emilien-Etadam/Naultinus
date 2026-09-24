using System;
using Naultinus.Helpers;
using Naultinus.Model;
using Xunit;

namespace Naultinus.Tests
{
    public class AgendaAnchorTests
    {
        private static readonly DateTime Jeudi = new DateTime(2026, 9, 24, 18, 30, 0);

        [Fact]
        public void LocalDate_EstLeJourLocal_PasLaVeilleNiLeLundi()
        {
            var aujourdHui = AgendaAnchor.LocalDate(Jeudi);

            Assert.Equal(new DateTime(2026, 9, 24), aujourdHui);
            Assert.Equal(DayOfWeek.Thursday, aujourdHui.DayOfWeek);
            Assert.NotEqual(new DateTime(2026, 9, 23), aujourdHui);
            Assert.NotEqual(DayOfWeek.Monday, aujourdHui.DayOfWeek);
        }

        [Fact]
        public void ShouldRealign_Agenda_RecaleQuandLeJourAChange()
        {
            var hier = new DateTime(2026, 9, 23);

            Assert.True(AgendaAnchor.ShouldRealign(CalendarViewMode.Agenda, hier, hier, Jeudi));
            Assert.Equal(new DateTime(2026, 9, 24), AgendaAnchor.LocalDate(Jeudi));
        }

        [Fact]
        public void ShouldRealign_MemeJour_ConserveUnDecalageManuel()
        {
            var aujourdHui = new DateTime(2026, 9, 24);
            var pageSuivante = aujourdHui.AddDays(14);

            Assert.False(AgendaAnchor.ShouldRealign(CalendarViewMode.Agenda, pageSuivante, aujourdHui, Jeudi));
        }

        [Theory]
        [InlineData(CalendarViewMode.Week)]
        [InlineData(CalendarViewMode.Day)]
        public void ShouldRealign_AutresModes_NeRecalePas(CalendarViewMode mode)
        {
            var hier = new DateTime(2026, 9, 23);

            Assert.False(AgendaAnchor.ShouldRealign(mode, hier, hier, Jeudi));
        }
    }
}
