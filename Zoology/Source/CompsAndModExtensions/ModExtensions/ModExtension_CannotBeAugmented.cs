// ModExtension_CannotBeAugmented.cs
using RimWorld;
using Verse;

namespace ZoologyMod
{
    /// <summary>
    /// Marker ModExtension: если добавлен к ThingDef/PawnKindDef, то животное нельзя бионизировать/улучшать.
    /// Удобно добавлять в PawnKindDef или ThingDef через &lt;modExtensions&gt; в XML.
    /// </summary>
    public class ModExtension_CannotBeAugmented : DefModExtension
    {
        // intentionally empty - marker only
        // Можно добавить настройки (например, reason string) при необходимости.
    }
}
