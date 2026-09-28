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

        [Fact]
        public void DisplayDay_DebutAvantLaPlage_EstLePremierJour_PasLaVeilleNiLeLundi()
        {
            var jeudi = new DateTime(2026, 9, 24);
            var mercredi = new DateTime(2026, 9, 23, 9, 0, 0);
            var lundi = new DateTime(2026, 9, 21, 8, 0, 0);

            Assert.Equal(jeudi, AgendaAnchor.DisplayDay(mercredi, jeudi));
            Assert.Equal(jeudi, AgendaAnchor.DisplayDay(lundi, jeudi));
            Assert.NotEqual(mercredi.Date, AgendaAnchor.DisplayDay(mercredi, jeudi));
            Assert.NotEqual(DayOfWeek.Monday, AgendaAnchor.DisplayDay(lundi, jeudi).DayOfWeek);
        }

        [Fact]
        public void DisplayDay_DebutDansLaPlage_GardeSonJour()
        {
            var jeudi = new DateTime(2026, 9, 24);
            var vendredi = new DateTime(2026, 9, 25, 11, 15, 0);

            Assert.Equal(jeudi, AgendaAnchor.DisplayDay(jeudi.AddHours(18), jeudi));
            Assert.Equal(vendredi.Date, AgendaAnchor.DisplayDay(vendredi, jeudi));
        }
    }
}
