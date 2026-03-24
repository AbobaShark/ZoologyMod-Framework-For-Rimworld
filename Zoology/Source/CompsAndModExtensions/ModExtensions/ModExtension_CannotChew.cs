using Verse;

namespace ZoologyMod
{
    internal static class CannotChewSettingsGate
    {
        public static bool Enabled()
        {
            ZoologyModSettings settings = ZoologyModSettings.Instance;
            return settings == null || (!settings.DisableAllRuntimePatches && settings.EnableCannotChewExtension);
        }
    }

    public class ModExtension_CannotChew : DefModExtension
    {
    }

    public static class CannotChewExtensions
    {
        public static bool CannotChew(this Pawn pawn)
        {
            return CannotChewUtility.HasCannotChew(pawn);
        }
    }
}
