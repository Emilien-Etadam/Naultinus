using Naultinus.Helpers;

namespace Naultinus.Model
{
    /// <summary>Entrée sérialisable pour <see cref="CalendarNaultinusModel.CalendarColors"/>.</summary>
    public class CalendarColorEntry
    {
        public string CalendarId { get; set; } = string.Empty;
        public string Color { get; set; } = CalendarColorHelper.DefaultColor;
    }
}
