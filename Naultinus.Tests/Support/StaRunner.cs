using System;
using System.Threading;

namespace Naultinus.Tests.Support
{
    /// <summary>Exécute une action sur un fil STA (requis pour construire des contrôles WPF sous xUnit).</summary>
    internal static class StaRunner
    {
        internal static void Run(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);

            Exception? error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            if (!thread.Join(TimeSpan.FromSeconds(30)))
                throw new TimeoutException("Le fil STA de test ne s'est pas terminé.");

            if (error != null)
                throw error;
        }
    }
}
