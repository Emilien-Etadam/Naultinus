using System;
using System.Windows;

namespace Naultinus.Helpers
{
    /// <summary>
    /// Confirmations utilisateur remplaçables (tests / UI). Par défaut : MessageBox Oui/Non.
    /// </summary>
    public static class UserDialogs
    {
        public static Func<string, string, bool> ConfirmYesNo { get; set; } =
            static (message, title) =>
                MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
                    == MessageBoxResult.Yes;
    }
}
