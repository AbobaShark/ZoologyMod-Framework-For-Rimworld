#if RIMWORLD17
using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    public sealed partial class Building_AnimalBarn
    {
        // Bind the actual 1.7 fire routine; retain its rounding, body regions,
        // damage worker, apparel handling, instigator and battle log.
        private static readonly Action<Fire, Thing> ApplyVanillaFireDamage =
            AccessTools.MethodDelegate<Action<Fire, Thing>>(AccessTools.Method(typeof(Fire), "DoFireDamage"));

        private void TickBarnFire()
        {
            if (!Spawned || this.GetStatValue(StatDefOf.Flammability) < 0.01f) return;

            // IsBurning for a multi-cell building means a live static fire in
            // its occupied rectangle. Do not keep a stale 'was burning' flag.
            Fire source = this.GetAttachment(ThingDefOf.Fire) as Fire;
            foreach (IntVec3 cell in this.OccupiedRect())
            {
                var things = Map.thingGrid.ThingsListAt(cell);
                for (int i = 0; i < things.Count; i++)
                    if (things[i] is Fire fire && fire.parent == null
                        && (source == null || fire.fireSize > source.fireSize)) source = fire;
            }
            if (source == null || source.Destroyed || !source.Spawned) return;
            for (int i = contents.Count - 1; i >= 0; i--)
            {
                // Death replaces a contained pawn with its real corpse via
                // Pawn.Kill's enclosing-container branch. No Spawn is needed.
                if (i < contents.Count && contents[i] is Pawn pawn && !pawn.Dead && !pawn.Destroyed)
                    ApplyVanillaFireDamage(source, pawn);
            }
        }

    }
}
#endif
