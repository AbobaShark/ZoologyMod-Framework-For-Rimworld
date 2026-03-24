using System;
using System.Collections.Generic;
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
                if (!CannotChewSettingsGate.Enabled())
                {
                    return true;
                }

                if (p == null || food == null)
                {
                    return true;
                }

                if (food is not Corpse corpse)
                {
                    return true;
                }

                if (p.Map != null && !CannotChewPresenceCache.HasCannotChewPawnsOnMap(p.Map))
                {
                    return true;
                }

                if (!CannotChewUtility.HasCannotChew(p))
                {
                    return true;
                }

                if (CannotChewUtility.IsCorpseTooLarge(p, corpse))
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

    internal static class CannotChewPresenceCache
    {
        private static readonly Dictionary<int, int> cannotChewCountByMapId = new Dictionary<int, int>(4);
        private static int totalCannotChew;

        public static bool HasCannotChewPawnsOnMap(Map map)
        {
            if (!CannotChewSettingsGate.Enabled())
            {
                return false;
            }

            if (map == null)
            {
                return totalCannotChew > 0;
            }

            if (cannotChewCountByMapId.TryGetValue(map.uniqueID, out int count))
            {
                return count > 0;
            }

            int computedCount = CountCannotChewOnMap(map);
            if (computedCount <= 0)
            {
                return false;
            }

            cannotChewCountByMapId[map.uniqueID] = computedCount;
            totalCannotChew += computedCount;
            return true;
        }

        public static void NotifyPawnSpawned(Pawn pawn)
        {
            if (pawn == null || pawn.Map == null)
            {
                return;
            }

            if (!CannotChewUtility.HasCannotChew(pawn))
            {
                return;
            }

            int mapId = pawn.Map.uniqueID;
            totalCannotChew++;
            if (cannotChewCountByMapId.TryGetValue(mapId, out int count))
            {
                cannotChewCountByMapId[mapId] = count + 1;
            }
            else
            {
                cannotChewCountByMapId[mapId] = 1;
            }
        }

        public static void NotifyPawnDespawned(Pawn pawn, Map map)
        {
            if (pawn == null || map == null)
            {
                return;
            }

            if (!CannotChewUtility.HasCannotChew(pawn))
            {
                return;
            }

            if (totalCannotChew > 0)
            {
                totalCannotChew--;
            }

            int mapId = map.uniqueID;
            if (!cannotChewCountByMapId.TryGetValue(mapId, out int count))
            {
                return;
            }

            count--;
            if (count <= 0)
            {
                cannotChewCountByMapId.Remove(mapId);
            }
            else
            {
                cannotChewCountByMapId[mapId] = count;
            }
        }

        public static void RebuildFromCurrentMaps()
        {
            cannotChewCountByMapId.Clear();
            totalCannotChew = 0;

            List<Map> maps = Find.Maps;
            if (maps == null || maps.Count == 0)
            {
                return;
            }

            for (int mapIndex = 0; mapIndex < maps.Count; mapIndex++)
            {
                Map map = maps[mapIndex];
                if (map?.mapPawns?.AllPawnsSpawned == null)
                {
                    continue;
                }

                int mapCount = CountCannotChewOnMap(map);

                if (mapCount > 0)
                {
                    cannotChewCountByMapId[map.uniqueID] = mapCount;
                    totalCannotChew += mapCount;
                }
            }
        }

        private static int CountCannotChewOnMap(Map map)
        {
            if (map?.mapPawns?.AllPawnsSpawned == null)
            {
                return 0;
            }

            int count = 0;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn != null && CannotChewUtility.HasCannotChew(pawn))
                {
                    count++;
                }
            }

            return count;
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
    internal static class Patch_Pawn_SpawnSetup_CannotChewPresence
    {
        private static void Postfix(Pawn __instance)
        {
            try
            {
                if (!CannotChewSettingsGate.Enabled())
                {
                    return;
                }

                CannotChewPresenceCache.NotifyPawnSpawned(__instance);
            }
            catch (Exception ex)
            {
                Log.Warning($"[Zoology] CannotChew presence spawn exception: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.DeSpawn))]
    internal static class Patch_Pawn_DeSpawn_CannotChewPresence
    {
        private static void Prefix(Pawn __instance)
        {
            try
            {
                if (!CannotChewSettingsGate.Enabled())
                {
                    return;
                }

                CannotChewPresenceCache.NotifyPawnDespawned(__instance, __instance?.Map);
            }
            catch (Exception ex)
            {
                Log.Warning($"[Zoology] CannotChew presence despawn exception: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(Toils_Ingest), "FinalizeIngest")]
    internal static class Patch_ToilsIngest_FinalizeIngest_CannotChew
    {
        private static void Postfix(ref Toil __result, Pawn ingester, TargetIndex ingestibleInd)
        {
            try
            {
                if (!CannotChewSettingsGate.Enabled())
                {
                    return;
                }

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
