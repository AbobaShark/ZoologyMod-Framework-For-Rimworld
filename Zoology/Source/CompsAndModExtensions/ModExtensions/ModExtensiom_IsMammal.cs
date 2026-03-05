

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

            
            if (pawn.def?.GetModExtension<ModExtension_IsMammal>() != null) return true;

            
            if (pawn.kindDef?.GetModExtension<ModExtension_IsMammal>() != null) return true;

            return false;
        }
    }
}
