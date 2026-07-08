using System;

namespace Naultinus.Model
{
    /// <summary>Résumé d'un mail non lu pour l'affichage (expéditeur, sujet, date).</summary>
    public class MailSummaryItem
    {
        public string Sender { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public DateTime Date { get; set; }
    }
}
