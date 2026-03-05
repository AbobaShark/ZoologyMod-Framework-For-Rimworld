// Patch_IsAcceptablePreyForPredator.cs

using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    [HarmonyPatch(typeof(FoodUtility), "IsAcceptablePreyFor", new[] { typeof(Pawn), typeof(Pawn) })]
    public static class Patch_IsAcceptablePreyForPredator
    {

        // Детёныш млекопитающего — та же логика, что и в вашем FoodIsSuitable патче:
        // - имеет IsMammal
        // - имеет life stage с defName == "AnimalBaby" (точечная проверка)
        // - (в вашем патче при отсутствии needs.food вы возвращали false — здесь воспроизводим ту же детекцию)
        static bool IsMammalBaby(Pawn p)
        {
            try
            {
                if (p == null) return false;
                if (!p.IsMammal()) return false;
                if (p.needs?.food == null) return false; // совпадает с вашей логикой для пищи
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
                if (predator == null || prey == null)
                {
                    __result = false;
                    return false;
                }

                // --- запрет охоты для детёнышей млекопитающих ---
                var settings = ZoologyModSettings.Instance;
                if (settings != null && ZoologyModSettings.EnableMammalLactation)
                {
                    if (IsMammalBaby(predator))
                    {
                        __result = false;
                        return false; // skip original -> запрещаем охоту
                    }
                }

                var comp = ZoologyPursuitGameComponent.Instance;
                if (comp != null)
                {
                    long pairKey = comp.PairKey(predator, prey);
                    bool isBlockedNow = comp.IsPairBlockedNow(predator, prey);
                    bool isAllowedNow = comp.IsPairAllowedNow(predator, prey);
                    if (isBlockedNow)
                    {
                        __result = false;
                        return false;
                    }
                }
                // --- остальная логика: базовые проверки + расширения ---
                if (predator.kindDef == null || prey.kindDef == null)
                {
                    __result = false;
                    return false;
                }
				
				// --- Photonozoa: detection and simple rules (safe: no compile-time dependency) ---
				bool predIsPhotonozoa = false;
				bool preyIsPhotonozoa = false;
				try
				{
					// Проверяем мод-расширения по имени типа, чтобы не требовать наличия типа PhotonozoaProperties при компиляции
					predIsPhotonozoa = predator.def.modExtensions != null && predator.def.modExtensions.Any(me =>
						me != null && (me.GetType().Name == "PhotonozoaProperties" || me.GetType().FullName.EndsWith(".PhotonozoaProperties")));
					preyIsPhotonozoa = prey.def.modExtensions != null && prey.def.modExtensions.Any(me =>
						me != null && (me.GetType().Name == "PhotonozoaProperties" || me.GetType().FullName.EndsWith(".PhotonozoaProperties")));
				}
				catch
				{
					predIsPhotonozoa = false;
					preyIsPhotonozoa = false;
				}

				// Запрет: животные без PhotonozoaProperties не должны охотиться на фотонозой-животных
				if (preyIsPhotonozoa && !predIsPhotonozoa)
				{
					__result = false;
					return false;
				}

				// Флаг: оба — фотонозои и оба в фракции "Photonozoa" (фракция может отсутствовать)
				bool photonozoaPairInTheirFaction = false;
				if (predIsPhotonozoa && preyIsPhotonozoa)
				{
					var photFactionDef = DefDatabase<FactionDef>.GetNamedSilentFail("Photonozoa");
					if (photFactionDef != null && predator.Faction != null && prey.Faction != null
						&& predator.Faction.def == photFactionDef && prey.Faction.def == photFactionDef)
					{
						photonozoaPairInTheirFaction = true;
					}
				}
				// --- end Photonozoa block ---

                bool predatorMammal = predator.IsMammal();
                bool preyMammal = prey.IsMammal();

                bool sameDef = predator.def == prey.def;
                bool inCrossbreedRelation = IsInCrossbreedRelation(predator, prey);

                if ((sameDef || inCrossbreedRelation) && predatorMammal && preyMammal)
                {
                    __result = false;
                    return false;
                }

                if (ModConstants.Settings.EnablePredatorOnPredatorHuntCheck && prey.RaceProps.predator && !prey.Downed)
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

				// Если это не специально разрешённая пара Photonozoa внутри их фракции,
				// то применяем обычный набор ограничений (hostility, player, herd, hidden, anomaly и т.д.)
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
                return false; // skip original
            }
            catch (Exception ex)
            {
                Log.Error($"[Zoology] Patch_IsAcceptablePreyForPredator error: {ex}");
                return true; // fallback on vanilla in case of exception
            }
        }

        static bool IsInCrossbreedRelation(Pawn a, Pawn b)
        {
            try
            {
                if (a?.def?.race?.canCrossBreedWith != null)
                {
                    foreach (var td in a.def.race.canCrossBreedWith)
                    {
                        if (td == null) continue;
                        if (td == b.def || string.Equals(td.defName, b.def?.defName, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }

                if (b?.def?.race?.canCrossBreedWith != null)
                {
                    foreach (var td in b.def.race.canCrossBreedWith)
                    {
                        if (td == null) continue;
                        if (td == a.def || string.Equals(td.defName, a.def?.defName, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[Zoology] IsInCrossbreedRelation error: {ex}");
            }
            return false;
        }
    }
}