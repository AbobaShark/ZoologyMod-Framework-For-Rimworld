using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;
using RimWorld;
using Verse.AI;

namespace ZoologyMod
{
    internal static class MammalBabyCache
    {
        private readonly struct BabyFoodRulesCacheEntry
        {
            public BabyFoodRulesCacheEntry(Pawn pawn, ThingDef def, PawnKindDef kindDef, int lifeStageIndex, LifeStageDef lifeStage, bool shouldUse)
            {
                Pawn = pawn;
                Def = def;
                KindDef = kindDef;
                LifeStageIndex = lifeStageIndex;
                LifeStage = lifeStage;
                ShouldUse = shouldUse;
            }

            public Pawn Pawn { get; }
            public ThingDef Def { get; }
            public PawnKindDef KindDef { get; }
            public int LifeStageIndex { get; }
            public LifeStageDef LifeStage { get; }
            public bool ShouldUse { get; }
        }

        private static Game runtimeCacheGame;
        private static Pawn lastPawn;
        private static ThingDef lastDef;
        private static PawnKindDef lastKindDef;
        private static int lastLifeStageIndex = -1;
        private static LifeStageDef lastLifeStage;
        private static bool lastShouldUse;
        private static readonly Dictionary<int, BabyFoodRulesCacheEntry> shouldUseBabyFoodRulesByPawnId = new Dictionary<int, BabyFoodRulesCacheEntry>(128);

        public static bool ShouldUseBabyFoodRules(Pawn pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            EnsureRuntimeCacheState();

            int lifeStageIndex = GetLifeStageIndex(pawn);
            LifeStageDef lifeStage = pawn.ageTracker?.CurLifeStage;
            ThingDef def = pawn.def;
            PawnKindDef kindDef = pawn.kindDef;
            if (ReferenceEquals(lastPawn, pawn)
                && ReferenceEquals(lastDef, def)
                && ReferenceEquals(lastKindDef, kindDef)
                && lastLifeStageIndex == lifeStageIndex
                && ReferenceEquals(lastLifeStage, lifeStage))
            {
                return lastShouldUse;
            }

            int id = pawn.thingIDNumber;
            if (id > 0
                && shouldUseBabyFoodRulesByPawnId.TryGetValue(id, out BabyFoodRulesCacheEntry cached)
                && ReferenceEquals(cached.Pawn, pawn)
                && ReferenceEquals(cached.Def, def)
                && ReferenceEquals(cached.KindDef, kindDef)
                && cached.LifeStageIndex == lifeStageIndex
                && ReferenceEquals(cached.LifeStage, lifeStage))
            {
                RememberLast(pawn, lifeStageIndex, lifeStage, cached.ShouldUse);
                return cached.ShouldUse;
            }

            bool shouldUse = ComputeShouldUseBabyFoodRules(pawn);
            if (id > 0)
            {
                shouldUseBabyFoodRulesByPawnId[id] = new BabyFoodRulesCacheEntry(pawn, def, kindDef, lifeStageIndex, lifeStage, shouldUse);
            }
            RememberLast(pawn, lifeStageIndex, lifeStage, shouldUse);
            return shouldUse;
        }

        public static bool IsMammalBaby(Pawn pawn)
        {
            return ShouldUseBabyFoodRules(pawn);
        }

        public static void Clear()
        {
            runtimeCacheGame = Current.Game;
            lastPawn = null;
            lastDef = null;
            lastKindDef = null;
            lastLifeStageIndex = -1;
            lastLifeStage = null;
            lastShouldUse = false;
            shouldUseBabyFoodRulesByPawnId.Clear();
        }

        private static bool ComputeShouldUseBabyFoodRules(Pawn pawn)
        {
            if (pawn == null) return false;
            if (pawn.RaceProps?.Animal != true) return false;
            if (!ZoologyCacheUtility.HasMammalExtension(pawn.def)
                && !ZoologyCacheUtility.HasMammalExtension(pawn.kindDef))
            {
                return false;
            }

            var stage = pawn.ageTracker?.CurLifeStage;
            if (AnimalLactationUtility.IsAnimalBabyLifeStage(stage))
            {
                return true;
            }

            if (stage != null && stage.developmentalStage == DevelopmentalStage.Baby)
            {
                return true;
            }

            try
            {
                var ages = pawn.RaceProps?.lifeStageAges;
                if (ages != null && ages.Count > 1 && pawn.ageTracker != null)
                {
                    return pawn.ageTracker.CurLifeStageIndex == 0;
                }
            }
            catch
            {
            }

            return false;
        }

        private static void EnsureRuntimeCacheState()
        {
            Game currentGame = Current.Game;
            if (ReferenceEquals(runtimeCacheGame, currentGame))
            {
                return;
            }

            Clear();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetLifeStageIndex(Pawn pawn)
        {
            try
            {
                return pawn?.ageTracker?.CurLifeStageIndex ?? -1;
            }
            catch
            {
                return -1;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void RememberLast(Pawn pawn, int lifeStageIndex, LifeStageDef lifeStage, bool shouldUse)
        {
            lastPawn = pawn;
            lastDef = pawn?.def;
            lastKindDef = pawn?.kindDef;
            lastLifeStageIndex = lifeStageIndex;
            lastLifeStage = lifeStage;
            lastShouldUse = shouldUse;
        }
    }
    
    
    
    [HarmonyPatch(typeof(FoodUtility), "FoodIsSuitable", new Type[] { typeof(Pawn), typeof(ThingDef) })]
    static class Patch_FoodUtility_FoodIsSuitable
    {
        static bool Prepare() => LactationSettingsGate.Enabled();

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            if (codes.Count == 0)
            {
                return codes;
            }

            LocalBuilder resultLocal = generator.DeclareLocal(typeof(bool));
            Label runOriginalLabel = generator.DefineLabel();
            codes[0].labels.Add(runOriginalLabel);

            List<CodeInstruction> patched = new List<CodeInstruction>(codes.Count + 8)
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Ldloca_S, resultLocal),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Patch_FoodUtility_FoodIsSuitable), nameof(TryOverrideFoodIsSuitable))),
                new CodeInstruction(OpCodes.Brfalse_S, runOriginalLabel),
                new CodeInstruction(OpCodes.Ldloc_S, resultLocal),
                new CodeInstruction(OpCodes.Ret)
            };
            patched.AddRange(codes);
            return patched;
        }

        private static bool TryOverrideFoodIsSuitable(Pawn p, ThingDef food, out bool result)
        {
            result = false;
            if (!LactationSettingsGate.Enabled())
            {
                return false;
            }

            if (p == null || food == null)
            {
                return false;
            }

            if (p.RaceProps?.Animal != true)
            {
                return false;
            }

            if (p.needs?.food == null)
            {
                result = false;
                return true;
            }

            if (!MammalBabyCache.ShouldUseBabyFoodRules(p))
            {
                return false;
            }

            IngestibleProperties ingestible = food.ingestible;
            if (ingestible == null)
            {
                result = false;
                return true;
            }

            bool ok = ingestible.babiesCanIngest && p.RaceProps.CanEverEat(food);
            if (p.MapHeld == null)
            {
                bool isDrug = food.IsDrug || ingestible.drugCategory != DrugCategory.None;
                bool isCorpse = typeof(Corpse).IsAssignableFrom(food.thingClass);
                ok = ok && food.IsNutritionGivingIngestible && !isDrug && !isCorpse;
            }

            result = ok;
            return true;
        }
    }

    
    
    
    [HarmonyPatch(typeof(JobGiver_GetFood), "TryFindFishJob", new Type[] { typeof(Pawn) })]
    static class Patch_JobGiver_GetFood_TryFindFishJob_BlockForMammalBabies
    {
        static bool Prepare() => LactationSettingsGate.Enabled();

        static bool Prefix(Pawn pawn, ref Job __result)
        {
            try
            {
                if (!LactationSettingsGate.Enabled())
                {
                    return true;
                }

                if (pawn == null) return true;

                
                if (pawn.needs?.food == null)
                {
                    __result = null;
                    return false;
                }

                if (MammalBabyCache.ShouldUseBabyFoodRules(pawn))
                {
                    __result = null;
                    return false; 
                }

                
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning($"[Zoology] Patch_TryFindFishJob Prefix failed: {ex}");
                return true;
            }
        }
    }

    
    
    
    
    
    [HarmonyPatch(typeof(FoodUtility), "WillEat", new Type[] { typeof(Pawn), typeof(Thing), typeof(Pawn), typeof(bool), typeof(bool) })]
    static class Patch_FoodUtility_WillEat_Thing_CorpseBlockForMammalBabies
    {
        static bool Prepare() => false;

        static bool Prefix(Pawn p, Thing food, Pawn getter, bool careIfNotAcceptableForTitle, bool allowVenerated, ref bool __result)
        {
            try
            {
                
                if (!LactationSettingsGate.Enabled())
                    return true; 

                if (p == null || !(food is Corpse)) return true;

                if (!MammalBabyCache.ShouldUseBabyFoodRules(p))
                {
                    return true;
                }

                if (p.needs?.food == null)
                {
                    __result = false;
                    return false;
                }

                __result = false;
                return false; 
            }
            catch (Exception ex)
            {
                Log.Warning($"[Zoology] Patch_FoodUtility_WillEat_Thing Prefix failed: {ex}");
                return true; 
            }
        }
    }
}
