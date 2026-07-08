using Palisades.Helpers;

namespace Palisades.Model
{
    /// <summary>Entrée sérialisable pour <see cref="CalendarPalisadeModel.CalendarColors"/>.</summary>
    public class CalendarColorEntry
    {
        public string CalendarId { get; set; } = string.Empty;
        public string Color { get; set; } = CalendarColorHelper.DefaultColor;
    }
}
