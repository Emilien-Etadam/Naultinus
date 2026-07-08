using System.IO;
using Naultinus.Model;
using Naultinus.ViewModel;
using Xunit;

namespace Naultinus.Tests.Models
{
    public class GroupIdTabOrderSerializationTests
    {
        [Fact]
        public void StandardNaultinusModel_GroupId_Persisted()
        {
            var model = new StandardNaultinusModel { Name = "Grouped", GroupId = "group-abc", TabOrder = 2 };
            using var writer = new StringWriter();
            ViewModelBase.SharedSerializer.Serialize(writer, model);
            using var reader = new StringReader(writer.ToString());
            var d = (StandardNaultinusModel)ViewModelBase.SharedSerializer.Deserialize(reader)!;
            Assert.Equal("group-abc", d.GroupId);
            Assert.Equal(2, d.TabOrder);
        }

        [Fact]
        public void NullGroupId_DeserializesToNull()
        {
            var model = new StandardNaultinusModel { Name = "Solo", GroupId = null, TabOrder = 0 };
            using var writer = new StringWriter();
            ViewModelBase.SharedSerializer.Serialize(writer, model);
            using var reader = new StringReader(writer.ToString());
            var d = (StandardNaultinusModel)ViewModelBase.SharedSerializer.Deserialize(reader)!;
            Assert.Null(d.GroupId);
            Assert.Equal(0, d.TabOrder);
        }
    }
}
