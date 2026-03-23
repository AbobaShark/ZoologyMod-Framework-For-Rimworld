using Verse;

namespace ZoologyMod
{
    public class ModExtension_CannotChew : DefModExtension
    {
    }

    public static class CannotChewExtensions
    {
        public static bool CannotChew(this Pawn pawn)
        {
            return pawn != null && DefModExtensionCache<ModExtension_CannotChew>.Has(pawn);
        }
    }
}
