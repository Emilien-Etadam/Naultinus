using System.Windows;
using Naultinus.Helpers;
using Xunit;

namespace Naultinus.Tests.Helpers
{
    public class PortalDragDropKeysTests
    {
        [Fact]
        public void IsCopy_TrueOnlyWhenControlPressed()
        {
            Assert.False(PortalDragDropKeys.IsCopy(DragDropKeyStates.None));
            Assert.False(PortalDragDropKeys.IsCopy(DragDropKeyStates.ShiftKey));
            Assert.True(PortalDragDropKeys.IsCopy(DragDropKeyStates.ControlKey));
            Assert.True(PortalDragDropKeys.IsCopy(DragDropKeyStates.ControlKey | DragDropKeyStates.ShiftKey));
        }
    }
}
