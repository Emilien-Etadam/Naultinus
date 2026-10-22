using System.Linq;
using Naultinus.Helpers;
using Naultinus.Model;
using Xunit;

namespace Naultinus.Tests.Helpers
{
    public class NaultinusLoadPlannerTests
    {
        [Fact]
        public void Plan_BumpsSchemaVersionZeroToOne()
        {
            var model = new StandardNaultinusModel { Name = "A", SchemaVersion = 0 };
            NaultinusLoadPlan plan = NaultinusLoadPlanner.Plan(new[] { model });

            Assert.Equal(1, model.SchemaVersion);
            Assert.Single(plan.Standalone);
        }

        [Fact]
        public void Plan_ExcludesHidden()
        {
            var visible = new StandardNaultinusModel { Name = "Show", IsHidden = false };
            var hidden = new StandardNaultinusModel { Name = "Hide", IsHidden = true };

            NaultinusLoadPlan plan = NaultinusLoadPlanner.Plan(new[] { visible, hidden });

            Assert.Single(plan.Standalone);
            Assert.Equal("Show", plan.Standalone[0].Name);
            Assert.Empty(plan.Groups);
        }

        [Fact]
        public void Plan_GroupsByGroupId_OrderedByTabOrder()
        {
            var a = new StandardNaultinusModel { Name = "Second", GroupId = "g1", TabOrder = 2 };
            var b = new StandardNaultinusModel { Name = "First", GroupId = "g1", TabOrder = 0 };
            var solo = new FolderPortalModel { Name = "Solo", RootPath = @"C:\T" };

            NaultinusLoadPlan plan = NaultinusLoadPlanner.Plan(new NaultinusModelBase[] { a, b, solo });

            Assert.Single(plan.Groups);
            Assert.Equal(new[] { "First", "Second" }, plan.Groups[0].Select(m => m.Name).ToArray());
            Assert.Single(plan.Standalone);
            Assert.Equal("Solo", plan.Standalone[0].Name);
        }

        [Fact]
        public void Plan_HiddenMemberDroppedFromGroup()
        {
            var keep = new StandardNaultinusModel { Name = "Keep", GroupId = "g", TabOrder = 0 };
            var drop = new StandardNaultinusModel { Name = "Drop", GroupId = "g", TabOrder = 1, IsHidden = true };

            NaultinusLoadPlan plan = NaultinusLoadPlanner.Plan(new[] { keep, drop });

            Assert.Single(plan.Groups);
            Assert.Single(plan.Groups[0]);
            Assert.Equal("Keep", plan.Groups[0][0].Name);
        }

        [Fact]
        public void Plan_TwoDistinctGroups()
        {
            var g1 = new StandardNaultinusModel { Name = "A", GroupId = "one", TabOrder = 0 };
            var g2 = new StandardNaultinusModel { Name = "B", GroupId = "two", TabOrder = 0 };

            NaultinusLoadPlan plan = NaultinusLoadPlanner.Plan(new[] { g1, g2 });

            Assert.Equal(2, plan.Groups.Count);
            Assert.Empty(plan.Standalone);
        }
    }
}
