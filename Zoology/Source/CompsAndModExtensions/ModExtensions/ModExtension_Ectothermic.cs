using System;
using System.Collections.Generic;
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
        private static bool patched;
        private readonly struct FrostbitePartEntry
        {
            public FrostbitePartEntry(BodyPartRecord part, float vulnerability, int damageAmount)
            {
                Part = part;
                Vulnerability = vulnerability;
                DamageAmount = damageAmount;
            }

            public BodyPartRecord Part { get; }
            public float Vulnerability { get; }
            public int DamageAmount { get; }
        }

        private static readonly Dictionary<BodyDef, List<FrostbitePartEntry>> frostbitePartsByBodyDef
            = new Dictionary<BodyDef, List<FrostbitePartEntry>>(16);

        static Ectothermic_HarmonyPatches()
        {
            EnsurePatched();
        }

        public static void EnsurePatched()
        {
            if (patched)
            {
                return;
            }

            var settings = ModConstants.Settings;
            if (settings != null && settings.DisableAllRuntimePatches)
            {
                return;
            }

            if (settings != null && !settings.EnableEctothermicPatch)
            {
                return;
            }

            patched = true;
            var harmony = new Harmony("com.abobashark.zoology.ectothermic");
            var original = AccessTools.Method(typeof(HediffGiver_Hypothermia), nameof(HediffGiver_Hypothermia.OnIntervalPassed));
            var prefix = new HarmonyMethod(typeof(Ectothermic_HarmonyPatches), nameof(OnIntervalPassed_Prefix));
            harmony.Patch(original, prefix: prefix);
        }

        public static void ResetPatchedState()
        {
            patched = false;
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
                if (giver == null || pawn == null)
                {
                    return true;
                }

                HediffSet hediffSet = pawn.health?.hediffSet;
                if (hediffSet == null)
                {
                    return false;
                }

                HediffDef hediffDef = giver.hediffInsectoid ?? giver.hediff;
                if (hediffDef == null)
                {
                    return false;
                }

                float ambientTemperature = pawn.AmbientTemperature;
                FloatRange safeRange = pawn.SafeTemperatureRange();
                Hediff hypothermia = hediffSet.GetFirstHediffOfDef(hediffDef, false);

                if (ambientTemperature < safeRange.min)
                {
                    float addedSeverity = Mathf.Abs(ambientTemperature - safeRange.min) * 6.45E-05f;
                    if (addedSeverity < 0.00075f)
                    {
                        addedSeverity = 0.00075f;
                    }

                    HealthUtility.AdjustSeverity(pawn, hediffDef, addedSeverity);
                    if (pawn.Dead)
                    {
                        return false;
                    }
                }

                if (hypothermia == null)
                {
                    return false;
                }

                if (!pawn.SpawnedOrAnyParentSpawned || TerrainProvidesHeat(pawn))
                {
                    ReduceHypothermiaSeverity(hypothermia);
                    return false;
                }

                if (ambientTemperature > pawn.ComfortableTemperatureRange().min)
                {
                    ReduceHypothermiaSeverity(hypothermia);
                    return false;
                }

                if (pawn.RaceProps.FleshType != FleshTypeDefOf.Insectoid
                    && ambientTemperature < 0f
                    && hypothermia.Severity > 0.37f)
                {
                    float frostbiteChance = 0.025f * hypothermia.Severity;
                    if (frostbiteChance > 0f
                        && Rand.Value < frostbiteChance
                        && TryGetRandomFrostbitePart(pawn, hediffSet, out FrostbitePartEntry part))
                    {
                        DamageInfo dinfo = new DamageInfo(
                            DamageDefOf.Frostbite,
                            part.DamageAmount,
                            0f,
                            -1f,
                            null,
                            part.Part,
                            null,
                            DamageInfo.SourceCategory.ThingOrUnknown,
                            null,
                            true,
                            true,
                            QualityCategory.Normal,
                            true,
                            false);
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

        private static void ReduceHypothermiaSeverity(Hediff hediff)
        {
            if (hediff == null)
            {
                return;
            }

            float reduction = hediff.Severity * 0.027f;
            if (reduction < 0.0015f)
            {
                reduction = 0.0015f;
            }
            else if (reduction > 0.015f)
            {
                reduction = 0.015f;
            }

            hediff.Severity -= reduction;
        }

        private static bool TerrainProvidesHeat(Pawn pawn)
        {
            if (pawn == null || !pawn.SpawnedOrAnyParentSpawned)
            {
                return false;
            }

            Map map = pawn.MapHeld;
            if (map?.terrainGrid == null)
            {
                return false;
            }

            TerrainDef terrain = map.terrainGrid.TerrainAt(pawn.PositionHeld);
            return terrain != null && terrain.heatPerTick > 0f;
        }

        private static bool TryGetRandomFrostbitePart(Pawn pawn, HediffSet hediffSet, out FrostbitePartEntry result)
        {
            result = default(FrostbitePartEntry);
            BodyDef bodyDef = pawn?.RaceProps?.body;
            if (bodyDef == null || hediffSet == null)
            {
                return false;
            }

            List<FrostbitePartEntry> parts = GetFrostbiteParts(bodyDef);
            if (parts == null || parts.Count == 0)
            {
                return false;
            }

            float totalWeight = 0f;
            bool found = false;
            FrostbitePartEntry selected = default(FrostbitePartEntry);
            for (int i = 0; i < parts.Count; i++)
            {
                FrostbitePartEntry entry = parts[i];
                BodyPartRecord part = entry.Part;
                if (part == null || entry.Vulnerability <= 0f || hediffSet.PartIsMissing(part))
                {
                    continue;
                }

                totalWeight += entry.Vulnerability;
                if (Rand.Value * totalWeight <= entry.Vulnerability)
                {
                    selected = entry;
                    found = true;
                }
            }

            if (!found)
            {
                return false;
            }

            result = selected;
            return true;
        }

        private static List<FrostbitePartEntry> GetFrostbiteParts(BodyDef bodyDef)
        {
            if (bodyDef == null)
            {
                return null;
            }

            if (frostbitePartsByBodyDef.TryGetValue(bodyDef, out List<FrostbitePartEntry> cached))
            {
                return cached;
            }

            List<FrostbitePartEntry> parts = new List<FrostbitePartEntry>();
            List<BodyPartRecord> source = bodyDef.AllPartsVulnerableToFrostbite;
            if (source != null)
            {
                for (int i = 0; i < source.Count; i++)
                {
                    BodyPartRecord part = source[i];
                    if (part?.def == null)
                    {
                        continue;
                    }

                    float vulnerability = part.def.frostbiteVulnerability;
                    if (vulnerability <= 0f)
                    {
                        continue;
                    }

                    int damageAmount = Mathf.Max(1, Mathf.CeilToInt(part.def.hitPoints * 0.5f));
                    parts.Add(new FrostbitePartEntry(part, vulnerability, damageAmount));
                }
            }

            frostbitePartsByBodyDef[bodyDef] = parts;
            return parts;
        }
    }
}
