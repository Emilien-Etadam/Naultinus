using Naultinus.Helpers;
using Naultinus.Model;
using System;
using Xunit;

namespace Naultinus.Tests.Helpers
{
    /// <summary>
    /// Enregistrement des réglages sans fenêtre WPF : le dialogue délègue à <see cref="TaskNaultinusSettings.TryApply"/>.
    /// </summary>
    public class TaskNaultinusSettingsTests
    {
        private static TaskNaultinusModel Sample() => new()
        {
            SyncIntervalMinutes = 12,
            EnableLogging = true,
            ShowCompletedTasks = false,
        };

        [Fact]
        public void TryApply_ValidInterval_WritesTheThreeSettings()
        {
            var model = Sample();

            bool applied = TaskNaultinusSettings.TryApply(model, 30, false, true);

            Assert.True(applied);
            Assert.Equal(30, model.SyncIntervalMinutes);
            Assert.False(model.EnableLogging);
            Assert.True(model.ShowCompletedTasks);
        }

        [Theory]
        [InlineData(TaskNaultinusSettings.MinSyncIntervalMinutes)]
        [InlineData(TaskNaultinusSettings.MaxSyncIntervalMinutes)]
        public void TryApply_BoundaryInterval_WritesTheThreeSettings(int interval)
        {
            var model = Sample();

            bool applied = TaskNaultinusSettings.TryApply(model, interval, false, true);

            Assert.True(applied);
            Assert.Equal(interval, model.SyncIntervalMinutes);
            Assert.False(model.EnableLogging);
            Assert.True(model.ShowCompletedTasks);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(61)]
        public void TryApply_InvalidInterval_LeavesModelUnchanged(int interval)
        {
            var model = Sample();

            bool applied = TaskNaultinusSettings.TryApply(model, interval, false, true);

            Assert.False(applied);
            Assert.Equal(12, model.SyncIntervalMinutes);
            Assert.True(model.EnableLogging);
            Assert.False(model.ShowCompletedTasks);
        }

        [Fact]
        public void TryApply_NullModel_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => TaskNaultinusSettings.TryApply(null!, 5, false, true));
        }
    }
}
