using System.Windows.Documents;

namespace Naultinus.Helpers
{
    internal class HyperlinkCommander : Hyperlink
    {
        protected override void OnClick()
        {
            Command.Execute(CommandParameter);
        }
    }
}
