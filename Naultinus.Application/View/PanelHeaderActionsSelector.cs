using System.Windows;
using System.Windows.Controls;
using Naultinus.ViewModel;

namespace Naultinus.View
{
    /// <summary>
    /// Choisit les actions d'en-tête du panneau sélectionné. Les modèles sont ceux des fenêtres seules.
    /// </summary>
    public class PanelHeaderActionsSelector : DataTemplateSelector
    {
        public override DataTemplate? SelectTemplate(object? item, DependencyObject container)
        {
            if (container is not FrameworkElement element)
                return null;

            var key = item switch
            {
                TaskNaultinusViewModel => "TaskHeaderActionsTemplate",
                CalendarNaultinusViewModel => "CalendarHeaderActionsTemplate",
                MailNaultinusViewModel => "MailHeaderActionsTemplate",
                FolderPortalViewModel => "FolderHeaderActionsTemplate",
                _ => "EmptyHeaderActionsTemplate",
            };

            return element.TryFindResource(key) as DataTemplate;
        }
    }
}
