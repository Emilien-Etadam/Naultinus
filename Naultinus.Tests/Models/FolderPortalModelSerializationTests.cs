using System.IO;
using Naultinus.Model;
using Naultinus.ViewModel;
using Xunit;

namespace Naultinus.Tests.Models
{
    public class FolderPortalModelSerializationTests
    {
        [Fact]
        public void FolderPortalModel_SharedSerializer_RoundTrip()
        {
            var model = new FolderPortalModel
            {
                Name = "Portal",
                RootPath = @"C:\Users\Test\Documents",
                CurrentPath = @"C:\Users\Test\Documents\Sub",
                NavigationHistory = { @"C:\Users\Test\Documents", @"C:\Users\Test\Documents\Sub" },
                NavigationIndex = 1,
                FilterQuery = "rap",
            };
            using var writer = new StringWriter();
            ViewModelBase.SharedSerializer.Serialize(writer, model);
            using var reader = new StringReader(writer.ToString());
            var d = (FolderPortalModel)ViewModelBase.SharedSerializer.Deserialize(reader)!;

            Assert.Equal(model.Name, d.Name);
            Assert.Equal(model.RootPath, d.RootPath);
            Assert.Equal(model.CurrentPath, d.CurrentPath);
            Assert.Equal(model.NavigationHistory, d.NavigationHistory);
            Assert.Equal(model.NavigationIndex, d.NavigationIndex);
            Assert.Equal(model.FilterQuery, d.FilterQuery);
        }
    }
}
