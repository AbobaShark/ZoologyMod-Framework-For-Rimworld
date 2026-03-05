//ModExtensiom_IsMammal.cs

using Verse;

namespace ZoologyMod
{
    // Маркер на уровне def — добавляйте в ThingDef (раса) или PawnKindDef
    public class ModExtension_IsMammal : DefModExtension
    {
        // сюда можно добавить параметры в будущем, например:
        // public bool affectsReproduction = true;
    }

    public static class MammalExtensions
    {
        public static bool IsMammal(this Pawn pawn)
        {
            if (pawn == null) return false;

            // Проверяем ThingDef (обычно раса)
            if (pawn.def?.GetModExtension<ModExtension_IsMammal>() != null) return true;

            // Проверяем PawnKindDef (если вдруг добавлено на уровне PawnKind)
            if (pawn.kindDef?.GetModExtension<ModExtension_IsMammal>() != null) return true;

            return false;
        }
    }
}
