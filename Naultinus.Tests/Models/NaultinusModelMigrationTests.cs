using Naultinus.Model;
using Xunit;

namespace Naultinus.Tests.Models
{
    public class NaultinusModelMigrationTests
    {
        [Fact]
        public void ToConcreteModel_StandardType_ReturnsStandardNaultinusModel()
        {
            var legacy = new NaultinusModel { Name = "Old", Type = NaultinusType.Standard, Width = 400, Height = 300 };
            var result = NaultinusModelMigration.ToConcreteModel(legacy);
            Assert.IsType<StandardNaultinusModel>(result);
            Assert.Equal("Old", result.Name);
            Assert.Equal(400, result.Width);
        }

        [Fact]
        public void ToConcreteModel_FolderPortalType_ReturnsFolderPortalModel()
        {
            var legacy = new NaultinusModel { Name = "Folder", Type = NaultinusType.FolderPortal, RootPath = @"C:\Test" };
            var result = NaultinusModelMigration.ToConcreteModel(legacy);
            Assert.IsType<FolderPortalModel>(result);
            Assert.Equal(@"C:\Test", ((FolderPortalModel)result).RootPath);
        }

        [Fact]
        public void ToConcreteModel_TaskType_ReturnsTaskNaultinusModel()
        {
            var legacy = new NaultinusModel { Name = "Tasks", Type = NaultinusType.TaskNaultinus, CalDAVUrl = "https://example.com/dav/" };
            var result = NaultinusModelMigration.ToConcreteModel(legacy);
            Assert.IsType<TaskNaultinusModel>(result);
            Assert.Equal("https://example.com/dav/", ((TaskNaultinusModel)result).CalDAVUrl);
        }

        [Fact]
        public void ToConcreteModel_PreservesCommonProperties()
        {
            var legacy = new NaultinusModel
            {
                Identifier = "test-id",
                Name = "Test",
                FenceX = 100,
                FenceY = 200,
                Width = 500,
                Height = 400
            };
            var result = NaultinusModelMigration.ToConcreteModel(legacy);
            Assert.Equal("test-id", result.Identifier);
            Assert.Equal(100, result.FenceX);
            Assert.Equal(200, result.FenceY);
            Assert.Equal(500, result.Width);
            Assert.Equal(400, result.Height);
        }
    }
}
