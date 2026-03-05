// ModExtension_IsScavenger.cs

using Verse;

namespace ZoologyMod
{
    // Используется как: pawn.def.GetModExtension<ModExtension_IsScavenger>().
    public class ModExtension_IsScavenger : DefModExtension
    {
        // можно добавить опции, напр. разрешать/запрещать "сверхтухлое" мясо и т.п.
        public bool allowVeryRotten = false;
    }
}