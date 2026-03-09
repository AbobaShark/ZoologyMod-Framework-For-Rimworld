using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ZoologyMod
{
    
    public class ModExtension_Ectothermic : DefModExtension
    {
        
        
    }

    
    [StaticConstructorOnStartup]
    public static class Ectothermic_HarmonyPatches
    {
        private static readonly System.Collections.Generic.Dictionary<BodyDef, System.Collections.Generic.List<BodyPartRecord>> frostbitePartsByBodyDef
            = new System.Collections.Generic.Dictionary<BodyDef, System.Collections.Generic.List<BodyPartRecord>>(16);

        static Ectothermic_HarmonyPatches()
        {
            var settings = ModConstants.Settings;
            if (settings != null && !settings.EnableEctothermicPatch)
            {
                return;
            }

            var harmony = new Harmony("com.abobashark.zoology.ectothermic");
            var original = AccessTools.Method(typeof(HediffGiver_Hypothermia), nameof(HediffGiver_Hypothermia.OnIntervalPassed));
            var prefix = new HarmonyMethod(typeof(Ectothermic_HarmonyPatches), nameof(OnIntervalPassed_Prefix));
            harmony.Patch(original, prefix: prefix);
        }

        
        
        public static bool OnIntervalPassed_Prefix(HediffGiver_Hypothermia __instance, Pawn pawn, Hediff cause)
        {
            ThingDef def = pawn?.def;
            if (def == null || !ZoologyCacheUtility.HasEctothermicExtension(def))
            {
                return true;
            }

            return HandleEctothermicHypothermia(__instance, pawn);
        }

        private static bool HandleEctothermicHypothermia(HediffGiver_Hypothermia giver, Pawn pawn)
        {
            try
            {
                HediffSet hediffSet = pawn.health?.hediffSet;
                if (hediffSet == null)
                {
                    return false;
                }

                HediffDef hediffDef = giver.hediffInsectoid ?? giver.hediff;
                float ambientTemperature = pawn.AmbientTemperature;
                FloatRange safe = pawn.SafeTemperatureRange();
                bool belowSafe = ambientTemperature < safe.min;
                Hediff firstHediffOfDef = hediffSet.GetFirstHediffOfDef(hediffDef, false);

                if (belowSafe)
                {
                    float num = Mathf.Abs(ambientTemperature - safe.min) * 6.45E-05f;
                    num = Mathf.Max(num, 0.00075f);
                    HealthUtility.AdjustSeverity(pawn, hediffDef, num);
                    if (pawn.Dead)
                    {
                        return false;
                    }
                }

                if (firstHediffOfDef == null)
                {
                    return false;
                }

                FloatRange comfortable = pawn.ComfortableTemperatureRange();
                if (ambientTemperature > comfortable.min || !pawn.SpawnedOrAnyParentSpawned || TerrainProvidesHeat(pawn))
                {
                    float num2 = firstHediffOfDef.Severity * 0.027f;
                    num2 = Mathf.Clamp(num2, 0.0015f, 0.015f);
                    firstHediffOfDef.Severity -= num2;
                    return false;
                }

                if (pawn.RaceProps.FleshType != FleshTypeDefOf.Insectoid && ambientTemperature < 0f && firstHediffOfDef.Severity > 0.37f)
                {
                    float num3 = 0.025f * firstHediffOfDef.Severity;
                    if (Rand.Value < num3 && TryGetRandomFrostbitePart(pawn, hediffSet, out BodyPartRecord bodyPartRecord))
                    {
                        int num4 = Mathf.CeilToInt((float)bodyPartRecord.def.hitPoints * 0.5f);
                        DamageInfo dinfo = new DamageInfo(DamageDefOf.Frostbite, (float)num4, 0f, -1f, null, bodyPartRecord, null, DamageInfo.SourceCategory.ThingOrUnknown, null, true, true, QualityCategory.Normal, true, false);
                        pawn.TakeDamage(dinfo);
                    }
                }

                return false;
            }
            catch (Exception e)
            {
                Log.Error($"[Zoology.Ectothermic] error in OnIntervalPassed prefix: {e}");
                return true;
            }
        }

        private static bool TerrainProvidesHeat(Pawn pawn)
        {
            Map map = pawn?.MapHeld;
            if (map == null || !pawn.SpawnedOrAnyParentSpawned)
            {
                return false;
            }

            TerrainDef terrain = pawn.PositionHeld.GetTerrain(map);
            return terrain != null && terrain.heatPerTick > 0f;
        }

        private static bool TryGetRandomFrostbitePart(Pawn pawn, HediffSet hediffSet, out BodyPartRecord result)
        {
            result = null;
            if (pawn?.RaceProps?.body == null || hediffSet == null)
            {
                return false;
            }

            var parts = GetFrostbiteParts(pawn.RaceProps.body);
            float totalWeight = 0f;
            for (int i = 0; i < parts.Count; i++)
            {
                BodyPartRecord part = parts[i];
                if (part == null || hediffSet.PartIsMissing(part))
                {
                    continue;
                }

                totalWeight += part.def.frostbiteVulnerability;
            }

            if (totalWeight <= 0f)
            {
                return false;
            }

            float roll = Rand.Value * totalWeight;
            for (int i = 0; i < parts.Count; i++)
            {
                BodyPartRecord part = parts[i];
                if (part == null || hediffSet.PartIsMissing(part))
                {
                    continue;
                }

                roll -= part.def.frostbiteVulnerability;
                if (roll <= 0f)
                {
                    result = part;
                    return true;
                }
            }

            return false;
        }

        private static System.Collections.Generic.List<BodyPartRecord> GetFrostbiteParts(BodyDef bodyDef)
        {
            if (bodyDef == null)
            {
                return new System.Collections.Generic.List<BodyPartRecord>(0);
            }

            if (frostbitePartsByBodyDef.TryGetValue(bodyDef, out var cached))
            {
                return cached;
            }

            var parts = new System.Collections.Generic.List<BodyPartRecord>();
            var source = bodyDef.AllPartsVulnerableToFrostbite;
            if (source != null)
            {
                for (int i = 0; i < source.Count; i++)
                {
                    BodyPartRecord part = source[i];
                    if (part != null)
                    {
                        parts.Add(part);
                    }
                }
            }

            frostbitePartsByBodyDef[bodyDef] = parts;
            return parts;
        }
    }
}
