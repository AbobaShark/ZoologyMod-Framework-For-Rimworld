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

            if (ZoologyCacheUtility.HasMammalExtension(pawn.def)) return true;

            if (ZoologyCacheUtility.HasMammalExtension(pawn.kindDef)) return true;

            return false;
        }
    }
}
