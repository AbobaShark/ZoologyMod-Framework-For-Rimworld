using System;
using System.Collections.Generic;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    [HarmonyPatch(typeof(Dialog_AutoSlaughter), "RecalculateAnimals")]
    static class Patch_Dialog_AutoSlaughter_RecalculateAnimals_AdjustLactatingCounts
    {
        private struct LactatingCount
        {
            public int Adults;
            public int Young;
        }

        private sealed class MapCache
        {
            public int LastTick = -1;
            public readonly Dictionary<ThingDef, LactatingCount> LactatingByDef = new Dictionary<ThingDef, LactatingCount>(64);
        }

        private static readonly Dictionary<int, MapCache> cacheByMapId = new Dictionary<int, MapCache>(4);
        private const int RefreshIntervalTicks = ZoologyTickLimiter.Lactation.AutoSlaughterCacheIntervalTicks;

        private static readonly Type dialogType = typeof(Dialog_AutoSlaughter);
        private static readonly FieldInfo f_animalCounts = AccessTools.Field(dialogType, "animalCounts");
        private static readonly FieldInfo f_map = AccessTools.Field(dialogType, "map");
        private static readonly Type animalCountRecordType = dialogType.GetNestedType("AnimalCountRecord", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo fi_total = animalCountRecordType != null ? animalCountRecordType.GetField("total", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) : null;
        private static readonly FieldInfo fi_female = animalCountRecordType != null ? animalCountRecordType.GetField("female", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) : null;
        private static readonly FieldInfo fi_femaleYoung = animalCountRecordType != null ? animalCountRecordType.GetField("femaleYoung", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) : null;
        private static readonly bool recordIsValueType = animalCountRecordType != null && animalCountRecordType.IsValueType;
        private static readonly List<KeyValuePair<ThingDef, object>> updateBuffer = new List<KeyValuePair<ThingDef, object>>(64);

        static bool Prepare() => ZoologyModSettings.EnableMammalLactation;

        static void Postfix(Dialog_AutoSlaughter __instance)
        {
            try
            {
                if (!ZoologyModSettings.EnableMammalLactation)
                {
                    return;
                }

                ZoologyModSettings settings = ZoologyModSettings.Instance;
                if (settings == null || settings.AllowSlaughterLactating)
                {
                    return;
                }

                if (f_animalCounts == null || f_map == null || fi_total == null || fi_female == null || fi_femaleYoung == null)
                {
                    return;
                }

                Map map = f_map.GetValue(__instance) as Map;
                if (map == null)
                {
                    return;
                }

                HediffDef lactDef = AnimalLactationUtility.LactatingHediffDef;
                if (lactDef == null)
                {
                    return;
                }

                Dictionary<ThingDef, LactatingCount> lactatingByDef = GetLactatingCounts(map, lactDef);
                if (lactatingByDef == null || lactatingByDef.Count == 0)
                {
                    return;
                }

                object dictObj = f_animalCounts.GetValue(__instance);
                if (dictObj == null)
                {
                    return;
                }

                if (dictObj is not IDictionary dict)
                {
                    return;
                }

                if (recordIsValueType)
                {
                    updateBuffer.Clear();
                }

                foreach (DictionaryEntry entry in dict)
                {
                    ThingDef def = entry.Key as ThingDef;
                    if (def == null || entry.Value == null)
                    {
                        continue;
                    }

                    if (!lactatingByDef.TryGetValue(def, out LactatingCount lactCount))
                    {
                        continue;
                    }

                    object record = entry.Value;
                    int total = (int)(fi_total.GetValue(record) ?? 0);
                    int female = (int)(fi_female.GetValue(record) ?? 0);
                    int femaleYoung = (int)(fi_femaleYoung.GetValue(record) ?? 0);

                    int reduceAdults = lactCount.Adults;
                    int reduceYoung = lactCount.Young;
                    int reduceTotal = reduceAdults + reduceYoung;

                    if (reduceTotal <= 0)
                    {
                        continue;
                    }

                    bool modified = false;
                    if (reduceAdults > 0)
                    {
                        female = Math.Max(0, female - reduceAdults);
                        fi_female.SetValue(record, female);
                        modified = true;
                    }

                    if (reduceYoung > 0)
                    {
                        femaleYoung = Math.Max(0, femaleYoung - reduceYoung);
                        fi_femaleYoung.SetValue(record, femaleYoung);
                        modified = true;
                    }

                    if (reduceTotal > 0)
                    {
                        total = Math.Max(0, total - reduceTotal);
                        fi_total.SetValue(record, total);
                        modified = true;
                    }

                    if (modified && recordIsValueType)
                    {
                        updateBuffer.Add(new KeyValuePair<ThingDef, object>(def, record));
                    }
                }

                if (recordIsValueType && updateBuffer.Count > 0)
                {
                    for (int i = 0; i < updateBuffer.Count; i++)
                    {
                        var kv = updateBuffer[i];
                        dict[kv.Key] = kv.Value;
                    }

                    updateBuffer.Clear();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[Zoology] Patch_Dialog_AutoSlaughter_RecalculateAnimals_AdjustLactatingCounts Postfix failed: {ex}");
            }
        }

        private static Dictionary<ThingDef, LactatingCount> GetLactatingCounts(Map map, HediffDef lactDef)
        {
            if (map == null || lactDef == null)
            {
                return null;
            }

            int mapId = map.uniqueID;
            if (!cacheByMapId.TryGetValue(mapId, out MapCache cache))
            {
                cache = new MapCache();
                cacheByMapId[mapId] = cache;
            }

            int tick = Find.TickManager?.TicksGame ?? 0;
            if (tick <= 0)
            {
                return null;
            }

            if (tick - cache.LastTick < RefreshIntervalTicks)
            {
                return cache.LactatingByDef;
            }

            cache.LastTick = tick;
            cache.LactatingByDef.Clear();

            var animals = map.mapPawns?.SpawnedColonyAnimals;
            if (animals == null || animals.Count == 0)
            {
                return cache.LactatingByDef;
            }

            for (int i = 0; i < animals.Count; i++)
            {
                Pawn pawn = animals[i];
                if (pawn == null || pawn.Dead || pawn.Destroyed || !pawn.Spawned)
                {
                    continue;
                }

                if (pawn.gender != Gender.Female)
                {
                    continue;
                }

                HediffSet hediffSet = pawn.health?.hediffSet;
                if (hediffSet == null || !hediffSet.HasHediff(lactDef, false))
                {
                    continue;
                }

                ThingDef def = pawn.def;
                if (def == null)
                {
                    continue;
                }

                bool isAdult = pawn.ageTracker?.CurLifeStage?.reproductive ?? false;
                if (!cache.LactatingByDef.TryGetValue(def, out LactatingCount count))
                {
                    count = default;
                }

                if (isAdult)
                {
                    count.Adults++;
                }
                else
                {
                    count.Young++;
                }

                cache.LactatingByDef[def] = count;
            }

            return cache.LactatingByDef;
        }
    }

    [HarmonyPatch(typeof(AutoSlaughterManager), nameof(AutoSlaughterManager.CanAutoSlaughterNow))]
    static class Patch_AutoSlaughterManager_CanAutoSlaughterNow
    {
        static bool Prepare() => ZoologyModSettings.EnableMammalLactation;

        static void Postfix(Pawn animal, ref bool __result)
        {
            try
            {
                if (!__result) return;

                ZoologyModSettings settings = ZoologyModSettings.Instance;
                if (settings == null || settings.AllowSlaughterLactating) return;

                if (animal == null) return;

                HediffDef lactDef = AnimalLactationUtility.LactatingHediffDef;
                if (lactDef == null) return;

                HediffSet hediffSet = animal.health?.hediffSet;
                if (hediffSet != null && hediffSet.HasHediff(lactDef, false))
                {
                    __result = false;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[Zoology] Patch_AutoSlaughterManager_CanAutoSlaughterNow Postfix failed: {ex}");
            }
        }
    }
}
