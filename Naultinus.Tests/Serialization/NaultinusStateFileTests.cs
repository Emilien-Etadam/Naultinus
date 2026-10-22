using System;
using System.IO;
using Naultinus.Model;
using Naultinus.Serialization;
using Xunit;

namespace Naultinus.Tests.Serialization
{
    /// <summary>Chemin critique de <c>NaultinusManager.LoadNaultinus</c> : lecture/écriture state.xml.</summary>
    public class NaultinusStateFileTests
    {
        [Fact]
        public void WriteThenRead_RoundTripsStandardModel()
        {
            var dir = Path.Combine(Path.GetTempPath(), "NaultinusStateFile_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var model = new StandardNaultinusModel
                {
                    Name = "Desk",
                    Width = 320,
                    Height = 240,
                    SchemaVersion = 1,
                };
                model.Shortcuts.Add(new UrlShortcut("Docs", "", "https://example.test"));

                NaultinusStateFile.Write(dir, model);
                var loaded = NaultinusStateFile.Read(dir);

                Assert.NotNull(loaded);
                var roundTrip = Assert.IsType<StandardNaultinusModel>(loaded);
                Assert.Equal("Desk", roundTrip.Name);
                Assert.Equal(320, roundTrip.Width);
                Assert.Single(roundTrip.Shortcuts);
                Assert.Equal("Docs", roundTrip.Shortcuts[0].Name);
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
            }
        }

        [Fact]
        public void Read_MissingDirectory_ReturnsNull()
        {
            Assert.Null(NaultinusStateFile.Read(Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid().ToString("N"))));
        }
    }
}
