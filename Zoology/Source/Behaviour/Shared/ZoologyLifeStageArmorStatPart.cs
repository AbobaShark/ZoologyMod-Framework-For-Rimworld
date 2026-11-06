using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    internal sealed class ZoologyLifeStageArmorOffsets
    {
        internal readonly float sharp;
        internal readonly float blunt;

        internal ZoologyLifeStageArmorOffsets(float sharp, float blunt)
        {
            this.sharp = sharp;
            this.blunt = blunt;
        }
    }

    // Armor additions cannot be represented by LifeStageDef.statFactors when
    // the original animal's base armor equals zero. Existing StatPart hooks
    // evaluate only on stat requests: no Harmony patches or per-tick work.
    // All numbers come from precomputed AnimalStats LifeStageProfiles.
    public sealed class ZoologyLifeStageArmorStatPart : StatPart
    {
        private bool sharp;

        internal static void Install()
        {
            InstallFor("ArmorRating_Sharp", true);
            InstallFor("ArmorRating_Blunt", false);
        }

        private static void InstallFor(string name, bool isSharp)
        {
            var stat = DefDatabase<StatDef>.GetNamedSilentFail(name);
            if (stat == null)
            {
                Log.Warning("[Zoology] Missing armor StatDef " + name
                    + " for precomputed species life-stage offsets.");
                return;
            }

            if (stat.parts == null)
            {
                stat.parts = new List<StatPart>();
            }
            for (int i = 0; i < stat.parts.Count; i++)
            {
                if (stat.parts[i] is ZoologyLifeStageArmorStatPart)
                {
                    return; // Def installation is idempotent.
                }
            }
            stat.parts.Add(new ZoologyLifeStageArmorStatPart
            {
                sharp = isSharp,
                parentStat = stat
            });
        }

        public override void TransformValue(StatRequest req, ref float val)
        {
            var pawn = req.Thing as Pawn;
            if (pawn?.RaceProps?.Animal != true)
            {
                return;
            }

            float offset;
            if (ZoologyLifeStageProfileInstaller.TryGetArmorOffset(
                    pawn.ageTracker?.CurLifeStage, sharp, out offset))
            {
                val += offset;
            }
        }

        public override string ExplanationPart(StatRequest req)
        {
            var pawn = req.Thing as Pawn;
            if (pawn?.RaceProps?.Animal != true)
            {
                return null;
            }
            float offset;
            if (ZoologyLifeStageProfileInstaller.TryGetArmorOffset(
                    pawn.ageTracker?.CurLifeStage, sharp, out offset))
            {
                return "Zoology: life-stage natural armor +" + offset.ToString("0.###");
            }
            return null;
        }
    }
}
