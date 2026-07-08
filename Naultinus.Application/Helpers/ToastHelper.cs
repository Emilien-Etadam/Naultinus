using Microsoft.Toolkit.Uwp.Notifications;
using Naultinus.Properties;
using System;
using System.Globalization;

namespace Naultinus.Helpers
{
    public static class ToastHelper
    {
        public static void ShowMailNotification(string folderName, int newCount)
        {
            if (newCount <= 0) return;
            try
            {
                new ToastContentBuilder()
                    .AddText(string.Format(CultureInfo.CurrentCulture, Strings.ToastNewMessagesFormat, newCount, folderName))
                    .AddAttributionText(Strings.ToastMailAttribution)
                    .Show();
            }
            catch (Exception ex) { NaultinusDiagnostics.LogDebug("ToastHelper: notification courriel", ex); }
        }

        public static void ShowEventReminder(string summary, DateTime startTime)
        {
            try
            {
                var timeStr = startTime.ToString("HH:mm");
                new ToastContentBuilder()
                    .AddText(summary)
                    .AddText(string.Format(CultureInfo.CurrentCulture, Strings.ToastStartsAtFormat, timeStr))
                    .AddAttributionText(Strings.ToastCalendarAttribution)
                    .Show();
            }
            catch (Exception ex) { NaultinusDiagnostics.LogDebug("ToastHelper: rappel d'événement", ex); }
        }

        public static void Cleanup()
        {
            try { ToastNotificationManagerCompat.Uninstall(); } catch (Exception ex) { NaultinusDiagnostics.LogDebug("ToastHelper.Cleanup", ex); }
        }
    }
}
