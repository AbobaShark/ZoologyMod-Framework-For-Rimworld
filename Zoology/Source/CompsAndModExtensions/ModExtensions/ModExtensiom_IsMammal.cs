using Verse;

namespace ZoologyMod
{
    internal static class LactationSettingsGate
    {
        public static bool Enabled()
        {
            ZoologyModSettings settings = ZoologyModSettings.Instance;
            return settings == null || (!settings.DisableAllRuntimePatches && ZoologyModSettings.EnableMammalLactation);
        }
    }

    public class ModExtension_IsMammal : DefModExtension
    {
    }

    public static class MammalExtensions
    {
        public static bool IsMammal(this Pawn pawn)
        {
            if (pawn == null) return false;

            if (ZoologyCacheUtility.HasMammalExtension(pawn.def)) return true;

            if (ZoologyCacheUtility.HasMammalExtension(pawn.kindDef)) return true;

            return false;
        }
    }
}
