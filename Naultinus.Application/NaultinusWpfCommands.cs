using System.Windows.Input;
using Naultinus.ViewModel;

namespace Naultinus
{
    public static class NaultinusWpfCommands
    {
        public static ICommand OpenEditSelectedTabCommand { get; } =
            new RelayCommand<INaultinusViewModel>(vm => NaultinusManager.OpenEditDialog(vm));

        public static ICommand OpenWebmailSelectedTabCommand { get; } =
            new RelayCommand<INaultinusViewModel>(vm =>
            {
                if (vm is MailNaultinusViewModel mail)
                    mail.OpenWebmail();
            });
    }
}
