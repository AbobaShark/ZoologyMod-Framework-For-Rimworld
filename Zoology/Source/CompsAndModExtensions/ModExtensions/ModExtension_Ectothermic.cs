using System;
using System.Linq;
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
        static Ectothermic_HarmonyPatches()
        {
            var settings = ZoologyModSettings.Instance;
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
            try
            {
                var settings = ZoologyModSettings.Instance;
                if (settings != null && !settings.EnableEctothermicPatch)
                {
                    return true;
                }

                if (pawn == null) return true;

                if (DefModExtensionCache<ModExtension_Ectothermic>.Get(pawn.def) == null)
                {
                    return true;
                }

                
                float ambientTemperature = pawn.AmbientTemperature;
                FloatRange comfortable = pawn.ComfortableTemperatureRange();
                FloatRange safe = pawn.SafeTemperatureRange();
                HediffSet hediffSet = pawn.health?.hediffSet;
                if (hediffSet == null)
                {
                    return false; 
                }

                
                HediffDef hediffDef = __instance.hediffInsectoid ?? __instance.hediff;
                Hediff firstHediffOfDef = hediffSet.GetFirstHediffOfDef(hediffDef, false);

                if (ambientTemperature < safe.min)
                {
                    float num = Mathf.Abs(ambientTemperature - safe.min) * 6.45E-05f;
                    num = Mathf.Max(num, 0.00075f);
                    HealthUtility.AdjustSeverity(pawn, hediffDef, num);
                    if (pawn.Dead)
                    {
                        return false;
                    }
                }
                if (firstHediffOfDef != null)
                {
                    if (ambientTemperature > comfortable.min || !pawn.SpawnedOrAnyParentSpawned || pawn.PositionHeld.GetTerrain(pawn.MapHeld).heatPerTick > 0f)
                    {
                        float num2 = firstHediffOfDef.Severity * 0.027f;
                        num2 = Mathf.Clamp(num2, 0.0015f, 0.015f);
                        firstHediffOfDef.Severity -= num2;
                        return false;
                    }

                    
                    if (pawn.RaceProps.FleshType != FleshTypeDefOf.Insectoid && ambientTemperature < 0f && firstHediffOfDef.Severity > 0.37f)
                    {
                        float num3 = 0.025f * firstHediffOfDef.Severity;
                        if (Rand.Value < num3)
                        {
                            BodyPartRecord bodyPartRecord;
                            if ((from x in pawn.RaceProps.body.AllPartsVulnerableToFrostbite
                                 where !hediffSet.PartIsMissing(x)
                                 select x).TryRandomElementByWeight((BodyPartRecord x) => x.def.frostbiteVulnerability, out bodyPartRecord))
                            {
                                int num4 = Mathf.CeilToInt((float)bodyPartRecord.def.hitPoints * 0.5f);
                                DamageInfo dinfo = new DamageInfo(DamageDefOf.Frostbite, (float)num4, 0f, -1f, null, bodyPartRecord, null, DamageInfo.SourceCategory.ThingOrUnknown, null, true, true, QualityCategory.Normal, true, false);
                                pawn.TakeDamage(dinfo);
                            }
                        }
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
    }
}
