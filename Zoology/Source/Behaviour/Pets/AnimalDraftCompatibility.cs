using System;
using HarmonyLib;
using Verse;

namespace ZoologyMod
{
    internal static class AnimalDraftCompatibility
    {
        private static readonly Type VefDraftableComp = AccessTools.TypeByName("VEF.AnimalBehaviours.CompDraftable");
        private static readonly Type VefDraftableHediffComp = AccessTools.TypeByName("VEF.AnimalBehaviours.HediffComp_Draftable");
        private static readonly Func<Pawn, bool> DraftableAnimalsCanDraft = ResolveDraftableAnimals();

        private static Func<Pawn, bool> ResolveDraftableAnimals()
        {
            Type utility = AccessTools.TypeByName("DraftableAnimals.DraftableAnimalsUtility");
            var method = utility == null ? null : AccessTools.Method(utility, "IsTrainedToAttack", new[] { typeof(Pawn) });
            return method == null ? null : (Func<Pawn, bool>)Delegate.CreateDelegate(typeof(Func<Pawn, bool>), method);
        }

        internal static bool IsAlphaAnimalsRace(ThingDef raceDef)
        {
            return string.Equals(raceDef?.modContentPack?.PackageIdPlayerFacing, "sarg.alphaanimals", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool UsesExternalDrafting(Pawn pawn, ThingDef raceDef)
        {
            // Use Draftable Animals' own predicate so its training setting stays authoritative.
            // With no pawn (def normalization), leave its races untouched as well.
            if (DraftableAnimalsCanDraft != null && (pawn == null || DraftableAnimalsCanDraft(pawn)))
            {
                return true;
            }

            raceDef = pawn?.def ?? raceDef;
            // Alpha Animals keeps the existing Zoology/VEF beastmastery bridge.
            if (IsAlphaAnimalsRace(raceDef))
            {
                return false;
            }

            var comps = raceDef?.comps;
            if (VefDraftableComp != null && comps != null)
            {
                for (int i = 0; i < comps.Count; i++)
                {
                    if (comps[i]?.compClass != null && VefDraftableComp.IsAssignableFrom(comps[i].compClass))
                    {
                        return true;
                    }
                }
            }

            var hediffs = pawn?.health?.hediffSet?.hediffs;
            if (VefDraftableHediffComp != null && hediffs != null)
            {
                for (int i = 0; i < hediffs.Count; i++)
                {
                    var hediffComps = (hediffs[i] as HediffWithComps)?.comps;
                    if (hediffComps == null)
                    {
                        continue;
                    }

                    for (int j = 0; j < hediffComps.Count; j++)
                    {
                        if (VefDraftableHediffComp.IsInstanceOfType(hediffComps[j]))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }
    }
}
