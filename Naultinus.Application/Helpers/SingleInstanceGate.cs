using System;
using System.Threading;

namespace Naultinus.Helpers
{
    /// <summary>
    /// Empêche deux processus Naultinus de coexister. La mise à jour peut sinon
    /// en lancer deux : l'entrée [Run] de l'installateur, et le Restart Manager
    /// lorsque l'ancienne version a passé <c>/RESTARTAPPLICATIONS</c>.
    /// </summary>
    internal static class SingleInstanceGate
    {
        internal const string MutexName = @"Local\Naultinus.SingleInstance";

        private static Mutex? _mutex;

        /// <summary>
        /// Réserve l'instance. Retourne false si une autre instance détient déjà le mutex.
        /// En cas d'échec inattendu du mutex, laisse démarrer (mieux qu'un blocage total).
        /// </summary>
        internal static bool TryAcquire()
        {
            Mutex mutex;
            try
            {
                mutex = new Mutex(false, MutexName);
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.Log("App", "Mutex d'instance unique indisponible ; démarrage quand même.", ex);
                return true;
            }

            try
            {
                if (!mutex.WaitOne(TimeSpan.Zero))
                {
                    mutex.Dispose();
                    return false;
                }
            }
            catch (AbandonedMutexException)
            {
                // L'instance précédente est morte sans libérer le mutex : on le reprend.
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.Log("App", "Mutex d'instance unique indisponible ; démarrage quand même.", ex);
                mutex.Dispose();
                return true;
            }

            _mutex = mutex;
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Release();
            return true;
        }

        private static void Release()
        {
            var mutex = _mutex;
            _mutex = null;
            if (mutex == null)
                return;

            try
            {
                mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Plus propriétaire.
            }
            finally
            {
                mutex.Dispose();
            }
        }
    }
}
