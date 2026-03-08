using Verse;

namespace ZoologyMod
{
    
    public class ModExtension_IsMammal : DefModExtension
    {
        
        
    }

    public static class MammalExtensions
    {
        public static bool IsMammal(this Pawn pawn)
        {
            if (pawn == null) return false;

            if (DefModExtensionCache<ModExtension_IsMammal>.Get(pawn.def) != null) return true;

            if (DefModExtensionCache<ModExtension_IsMammal>.Get(pawn.kindDef) != null) return true;

            return false;
        }
    }
}
