namespace Naultinus.Helpers
{
    internal static class AppEnvironment
    {
        internal static bool IsDev()
        {
#if DEBUG
            return true;
#else
            return false;
#endif
        }
    }
}
