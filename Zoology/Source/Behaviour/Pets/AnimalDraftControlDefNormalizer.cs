using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    internal static class AnimalDraftControlDefNormalizer
    {
        private static bool normalized;

        internal static void NormalizeDefs()
        {
            if (normalized)
            {
                return;
            }

            normalized = true;

            TrainableDef attackTarget = TrainableDefOf.AttackTarget;
            TrainableDef beastmastery = AnimalDraftControlUtility.DraftControlTrainable;
            TrainableDef legacyDraftControl = AnimalDraftControlUtility.LegacyDraftControlTrainable;
            TrainableDef vefBeastmastery = AnimalDraftControlUtility.VefDraftControlTrainable;
            if (attackTarget == null || beastmastery == null)
            {
                return;
            }

            int normalizedDefs = 0;
            List<ThingDef> allDefs = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < allDefs.Count; i++)
            {
                ThingDef thingDef = allDefs[i];
                List<TrainableDef> specialTrainables = thingDef?.race?.specialTrainables;
                if (specialTrainables == null || specialTrainables.Count == 0)
                {
                    continue;
                }

                bool hasBeastmasteryVariant = false;
                for (int j = 0; j < specialTrainables.Count; j++)
                {
                    TrainableDef trainable = specialTrainables[j];
                    if (trainable == beastmastery || trainable == legacyDraftControl || trainable == vefBeastmastery)
                    {
                        hasBeastmasteryVariant = true;
                        break;
                    }
                }

                if (!hasBeastmasteryVariant)
                {
                    continue;
                }

                List<TrainableDef> normalizedTrainables = new List<TrainableDef>(specialTrainables.Count + 1);
                bool addedAttackTarget = false;
                bool addedBeastmastery = false;
                bool changed = false;

                for (int j = 0; j < specialTrainables.Count; j++)
                {
                    TrainableDef trainable = specialTrainables[j];
                    if (trainable == null)
                    {
                        changed = true;
                        continue;
                    }

                    if (trainable == attackTarget)
                    {
                        if (!addedAttackTarget)
                        {
                            normalizedTrainables.Add(attackTarget);
                            addedAttackTarget = true;
                        }
                        else
                        {
                            changed = true;
                        }

                        continue;
                    }

                    if (trainable == beastmastery || trainable == legacyDraftControl || trainable == vefBeastmastery)
                    {
                        if (!addedBeastmastery)
                        {
                            normalizedTrainables.Add(beastmastery);
                            addedBeastmastery = true;
                        }
                        else
                        {
                            changed = true;
                        }

                        if (trainable != beastmastery)
                        {
                            changed = true;
                        }

                        continue;
                    }

                    if (normalizedTrainables.Contains(trainable))
                    {
                        changed = true;
                        continue;
                    }

                    normalizedTrainables.Add(trainable);
                }

                if (!addedAttackTarget)
                {
                    normalizedTrainables.Add(attackTarget);
                    addedAttackTarget = true;
                    changed = true;
                }

                if (!addedBeastmastery)
                {
                    normalizedTrainables.Add(beastmastery);
                    changed = true;
                }

                if (!changed || SameSequence(specialTrainables, normalizedTrainables))
                {
                    continue;
                }

                thingDef.race.specialTrainables = normalizedTrainables;
                normalizedDefs++;
            }

            if (normalizedDefs > 0)
            {
                Log.Message($"[Zoology] Normalized beastmastery special trainables on {normalizedDefs} race defs.");
            }
        }

        private static bool SameSequence(List<TrainableDef> left, List<TrainableDef> right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null || left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
