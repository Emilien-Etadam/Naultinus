using Naultinus.Helpers;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace Naultinus.ViewModel
{
    public class AboutViewModel : INotifyPropertyChanged
    {
        #region Constants
        private const string NOT_FOUND = "not found";
        #endregion

        #region Attributs
        private readonly string version;
        private readonly string releaseDate;
        #endregion

        #region Accessors
        public string Version
        {
            get
            {
                return version;
            }
        }
        public string ReleaseDate
        {
            get
            {
                return releaseDate;
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Propriété liée en XAML ({Binding FrameworkRuntime}) : doit rester une instance.")]
        public string FrameworkRuntime => RuntimeInformation.FrameworkDescription;
        #endregion

        public AboutViewModel()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            Version? maybeVersion = assembly.GetName().Version;
            version = maybeVersion != null ? "v" + maybeVersion.Major + "." + maybeVersion.Minor + "." + maybeVersion.Build : NOT_FOUND;

            ReleaseDateAttribute? maybeDateAttribute = assembly.GetCustomAttribute<ReleaseDateAttribute>();
            releaseDate = $"(released {(maybeDateAttribute != null ? maybeDateAttribute.DateTime.ToShortDateString() : NOT_FOUND)})";
        }

        #region Commands
        public ICommand NavigateCommand { get; private set; } = new RelayCommand<string>((url) =>
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        });
        #endregion


        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
