using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    [HarmonyPatch(typeof(FoodUtility), "IsAcceptablePreyFor", new[] { typeof(Pawn), typeof(Pawn) })]
    public static class Patch_IsAcceptablePreyForPredator
    {
        public static bool Prepare()
        {
            var s = ZoologyModSettings.Instance;
            return s == null || s.EnableAdvancedPredationLogic;
        }

        
        
        
        
        static bool IsMammalBaby(Pawn p)
        {
            try
            {
                if (p == null) return false;
                if (!p.IsMammal()) return false;
                if (p.needs?.food == null) return false; 
                var curStage = p.ageTracker?.CurLifeStage;
                if (curStage == null) return false;
                return string.Equals(curStage.defName, "AnimalBaby", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Log.Warning($"[Zoology] IsMammalBaby check failed: {ex}");
                return false;
            }
        }

        public static bool Prefix(Pawn predator, Pawn prey, ref bool __result)
        {
            try
            {
                var settings = ZoologyModSettings.Instance;
                if (settings != null && !settings.EnableAdvancedPredationLogic)
                {
                    return true;
                }

                if (predator == null || prey == null)
                {
                    __result = false;
                    return false;
                }

                
                if (settings != null && ZoologyModSettings.EnableMammalLactation)
                {
                    if (IsMammalBaby(predator))
                    {
                        __result = false;
                        return false; 
                    }
                }

                var comp = ZoologyPursuitGameComponent.Instance;
                if (comp != null && comp.IsPairBlockedNow(predator, prey))
                {
                    __result = false;
                    return false;
                }
                
                if (predator.kindDef == null || prey.kindDef == null)
                {
                    __result = false;
                    return false;
                }
				
				
				bool predIsPhotonozoa = false;
				bool preyIsPhotonozoa = false;
				try
				{
					predIsPhotonozoa = PredationCacheUtility.IsPhotonozoa(predator.def);
					preyIsPhotonozoa = PredationCacheUtility.IsPhotonozoa(prey.def);
				}
				catch
				{
					predIsPhotonozoa = false;
					preyIsPhotonozoa = false;
				}

				
				if (preyIsPhotonozoa && !predIsPhotonozoa)
				{
					__result = false;
					return false;
				}

				
				bool photonozoaPairInTheirFaction = predIsPhotonozoa
					&& preyIsPhotonozoa
					&& PredationCacheUtility.IsPhotonozoaPairInTheirFaction(predator, prey);
				

                bool predatorMammal = predator.IsMammal();
                bool preyMammal = prey.IsMammal();

                bool sameDef = predator.def == prey.def;
                bool inCrossbreedRelation = PredationCacheUtility.AreCrossbreedRelated(predator.def, prey.def);

                if ((sameDef || inCrossbreedRelation) && predatorMammal && preyMammal)
                {
                    __result = false;
                    return false;
                }

                if (prey.RaceProps.predator && !prey.Downed)
                {
                    float requiredPredatorCP = prey.kindDef.combatPower * (4f / 3f);
                    if (predator.kindDef.combatPower < requiredPredatorCP)
                    {
                        __result = false;
                        return false;
                    }
                }

                if (!prey.RaceProps.canBePredatorPrey) { __result = false; return false; }
                if (!prey.RaceProps.IsFlesh) { __result = false; return false; }
                if (!Find.Storyteller.difficulty.predatorsHuntHumanlikes && prey.RaceProps.Humanlike) { __result = false; return false; }
                if (prey.BodySize > predator.RaceProps.maxPreyBodySize) {__result = false; return false; }

                if (!prey.Downed)
                {
                    if (prey.kindDef.combatPower > 2f * predator.kindDef.combatPower)
                    {
                        __result = false;
                        return false;
                    }

                    float preyScore = prey.kindDef.combatPower * prey.health.summaryHealth.SummaryHealthPercent * prey.BodySize;
                    float predatorScore = predator.kindDef.combatPower * predator.health.summaryHealth.SummaryHealthPercent * predator.BodySize;

                    if (preyScore >= predatorScore * 1.3f)
                    {
                        __result = false;
                        return false;
                    }
                }

				
				
				if (!photonozoaPairInTheirFaction && !((predator.Faction == null || prey.Faction == null || predator.HostileTo(prey))
					&& (predator.Faction == null || prey.HostFaction == null || predator.HostileTo(prey))
					&& (predator.Faction != Faction.OfPlayer || prey.Faction != Faction.OfPlayer)
					&& (!predator.RaceProps.herdAnimal || predator.def != prey.def)
					&& !prey.IsHiddenFromPlayer()
					&& !prey.IsPsychologicallyInvisible()
					&& (!ModsConfig.AnomalyActive || !prey.IsMutant || prey.mutant.Def.canBleed)))
				{
					__result = false;
					return false;
				}

                __result = true;
                return false; 
            }
            catch (Exception ex)
            {
                Log.Error($"[Zoology] Patch_IsAcceptablePreyForPredator error: {ex}");
                return true; 
            }
        }
    }
}
