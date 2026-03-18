using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    [HarmonyPatch(typeof(FoodUtility), "WillEat", new Type[] { typeof(Pawn), typeof(Thing), typeof(Pawn), typeof(bool), typeof(bool) })]
    internal static class Patch_FoodUtility_WillEat_CannotChew
    {
        private static bool Prefix(Pawn p, Thing food, Pawn getter, bool careIfNotAcceptableForTitle, bool allowVenerated, ref bool __result)
        {
            try
            {
                if (p == null || food == null)
                {
                    return true;
                }

                if (!CannotChewUtility.HasCannotChew(p))
                {
                    return true;
                }

                if (food is Corpse corpse && CannotChewUtility.IsCorpseTooLarge(p, corpse))
                {
                    __result = false;
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[Zoology] CannotChew WillEat prefix exception: {ex}");
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Toils_Ingest), "FinalizeIngest")]
    internal static class Patch_ToilsIngest_FinalizeIngest_CannotChew
    {
        private static void Postfix(ref Toil __result, Pawn ingester, TargetIndex ingestibleInd)
        {
            try
            {
                if (__result == null || ingester == null)
                {
                    return;
                }

                if (!CannotChewUtility.HasCannotChew(ingester))
                {
                    return;
                }

                __result.AddFinishAction(() =>
                {
                    try
                    {
                        if (ingester == null)
                        {
                            return;
                        }

                        var job = ingester.CurJob;
                        if (job == null)
                        {
                            return;
                        }

                        Thing target = job.GetTarget(ingestibleInd).Thing;
                        if (target is not Corpse corpse)
                        {
                            return;
                        }

                        if (CannotChewUtility.IsCorpseTooLarge(ingester, corpse))
                        {
                            return;
                        }

                        if (corpse.Destroyed)
                        {
                            return;
                        }

                        float remaining = CannotChewUtility.GetRemainingCorpseNutrition(corpse, ingester);
                        if (remaining > 0f)
                        {
                            var foodNeed = ingester.needs?.food;
                            if (foodNeed != null)
                            {
                                foodNeed.CurLevel = Math.Min(foodNeed.MaxLevel, foodNeed.CurLevel + remaining);
                            }

                            ingester.records?.AddTo(RecordDefOf.NutritionEaten, remaining);
                        }

                        corpse.Destroy(DestroyMode.Vanish);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[Zoology] CannotChew FinalizeIngest finish exception: {ex}");
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Warning($"[Zoology] CannotChew FinalizeIngest postfix exception: {ex}");
            }
        }
    }
}
