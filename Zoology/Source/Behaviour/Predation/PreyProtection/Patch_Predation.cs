using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using HarmonyLib;
using Verse;
using RimWorld;
using Verse.AI;

namespace ZoologyMod
{
    [StaticConstructorOnStartup]
    public static class PredationHarmonyPatches
    {
        private static bool patched;
        private readonly struct CorpseFoodStateCacheEntry
        {
            public CorpseFoodStateCacheEntry(bool isPaired, bool isEffectivelyUnowned, int tick)
            {
                IsPaired = isPaired;
                IsEffectivelyUnowned = isEffectivelyUnowned;
                Tick = tick;
            }

            public bool IsPaired { get; }

            public bool IsEffectivelyUnowned { get; }

            public int Tick { get; }
        }

        private readonly struct FoodOptimalityDeltaCacheEntry
        {
            public FoodOptimalityDeltaCacheEntry(long key, float delta, int tick)
            {
                Key = key;
                Delta = delta;
                Tick = tick;
            }

            public long Key { get; }

            public float Delta { get; }

            public int Tick { get; }
        }

        private readonly struct VanillaHuntTargetCacheEntry
        {
            public VanillaHuntTargetCacheEntry(Pawn target, bool hasTarget, int mapId, int tick)
            {
                Target = target;
                HasTarget = hasTarget;
                MapId = mapId;
                Tick = tick;
            }

            public Pawn Target { get; }

            public bool HasTarget { get; }

            public int MapId { get; }

            public int Tick { get; }
        }

        private static readonly Func<Pawn, bool, Pawn> BestPawnToHuntForPredatorFunc = CreateBestPawnToHuntForPredatorDelegate();
        private static readonly Dictionary<long, CorpseFoodStateCacheEntry> corpseFoodStateCacheByPairKey = new Dictionary<long, CorpseFoodStateCacheEntry>(256);
        private static readonly Dictionary<ThingDef, float> staticIngestibleOptimalityDeltaByDefFallback = new Dictionary<ThingDef, float>(64);
        private static readonly ThingDef[] staticIngestibleDefsByShortHash = new ThingDef[ushort.MaxValue + 1];
        private static readonly float[] staticIngestibleOptimalityDeltaByShortHash = new float[ushort.MaxValue + 1];
        private static readonly byte[] staticIngestibleDeltaStateByShortHash = new byte[ushort.MaxValue + 1];
        private static readonly Dictionary<long, VanillaHuntTargetCacheEntry> vanillaHuntTargetCacheByPredatorKey =
            new Dictionary<long, VanillaHuntTargetCacheEntry>(512);
        private static readonly List<long> vanillaHuntTargetCleanupBuffer = new List<long>(64);
        private static readonly FoodOptimalityDeltaCacheEntry[] foodOptimalityDeltaHotCacheSlots =
            new FoodOptimalityDeltaCacheEntry[FoodOptimalityDeltaHotCacheSize];

        private const float OwnedCorpseBonus = 10000f;
        private const float UnpairedCorpseBonus = 300f;
        private const float GuardedCorpsePenalty = -10000f;
        private const float LiveAcceptablePreyBonus = 600f;
        private const int FoodOptimalityDeltaHotCacheSize = 32768;
        private const int FoodOptimalityDeltaHotCacheMask = FoodOptimalityDeltaHotCacheSize - 1;
        private const int VanillaHuntTargetCacheDurationTicks = 12;
        private const int VanillaHuntTargetCacheCleanupIntervalTicks = 300;
        private const int VanillaHuntTargetBudgetPerTick = 256;
        private const byte StaticIngestibleDeltaStateUnknown = 0;
        private const byte StaticIngestibleDeltaStateNoDelta = 1;
        private const byte StaticIngestibleDeltaStateHasDelta = 2;

        private static int lastCorpseFoodStateCacheCleanupTick = -ZoologyTickLimiter.PredationFood.CorpseFoodStateCacheCleanupIntervalTicks;
        private static int lastVanillaHuntTargetCleanupTick = -VanillaHuntTargetCacheCleanupIntervalTicks;
        private static int corpseFoodStateBudgetTick = -1;
        private static int corpseFoodStateBudgetRemaining = 0;
        private static int livePreyBudgetTick = -1;
        private static int livePreyBudgetRemaining = 0;
        private static int vanillaHuntTargetBudgetTick = -1;
        private static int vanillaHuntTargetBudgetRemaining = 0;
        private static TickManager runtimeCacheTickManager;
        private static Game runtimeCacheGame;
        private static int runtimeCacheLastTick = -1;
        private static int runtimeCacheEnsureTick = -1;

        static PredationHarmonyPatches()
        {
            EnsurePatched();
        }

        public static void EnsurePatched()
        {
            if (patched)
            {
                return;
            }

            var s = ZoologyModSettings.Instance;
            if (s != null && s.DisableAllRuntimePatches)
            {
                return;
            }

            if (s != null && !s.EnablePredatorDefendCorpse)
            {
                return;
            }

            patched = true;
            var harmony = new Harmony("com.abobashark.zoology.predatorpairs");
            try
            {
                
                var targetFoodOpt = AccessTools.Method(typeof(FoodUtility), nameof(FoodUtility.FoodOptimality));
                var postfixFood = typeof(PredationHarmonyPatches).GetMethod(nameof(FoodOptimality_Postfix), BindingFlags.Static | BindingFlags.Public);
                var targetBestFoodSource = AccessTools.Method(typeof(FoodUtility), nameof(FoodUtility.BestFoodSourceOnMap));
                var postfixBestFoodSource = typeof(PredationHarmonyPatches).GetMethod(nameof(BestFoodSourceOnMap_Postfix), BindingFlags.Static | BindingFlags.Public);
                if (targetFoodOpt != null && postfixFood != null)
                {
                    harmony.Patch(targetFoodOpt, null, new HarmonyMethod(postfixFood));
                }
                else
                {
                    Log.Warning("Zoology: FoodUtility.FoodOptimality method or postfix not found; skipping patch.");
                }

                if (targetBestFoodSource != null && postfixBestFoodSource != null)
                {
                    harmony.Patch(targetBestFoodSource, null, new HarmonyMethod(postfixBestFoodSource));
                }
                else
                {
                    Log.Warning("Zoology: FoodUtility.BestFoodSourceOnMap method or postfix not found; skipping patch.");
                }

                
                MethodInfo pawnKillMethod = null;

                var killMethods = typeof(Pawn).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(m => m.Name == "Kill")
                    .ToList();

                Type damageInfoType = typeof(DamageInfo);
                Type nullableDamageInfoType = typeof(Nullable<>).MakeGenericType(damageInfoType);

                foreach (var m in killMethods)
                {
                    var pars = m.GetParameters();
                    if (pars.Length >= 1)
                    {
                        var p0 = pars[0].ParameterType;
                        if (p0 == damageInfoType || p0 == nullableDamageInfoType)
                        {
                            pawnKillMethod = m;
                            break;
                        }
                        if (p0.IsGenericType && p0.GetGenericTypeDefinition() == typeof(Nullable<>))
                        {
                            var ga = p0.GetGenericArguments();
                            if (ga.Length == 1 && ga[0] == damageInfoType)
                            {
                                pawnKillMethod = m;
                                break;
                            }
                        }
                        if (!string.IsNullOrEmpty(p0.FullName) && p0.FullName.IndexOf("DamageInfo", StringComparison.InvariantCultureIgnoreCase) >= 0)
                        {
                            pawnKillMethod = m;
                            break;
                        }
                    }
                }

                if (pawnKillMethod != null)
                {
                    var postfixKill = typeof(PredationHarmonyPatches).GetMethod(nameof(Pawn_Kill_Postfix), BindingFlags.Static | BindingFlags.Public);
                    harmony.Patch(pawnKillMethod, null, new HarmonyMethod(postfixKill));
                }
                else
                {
                    Log.Warning("Zoology: Pawn.Kill overload not found for patching.");
                }

                
                try
                {
                    Type carryType = AppDomain.CurrentDomain.GetAssemblies()
                        .SelectMany(a =>
                        {
                            try { return a.GetTypes(); } catch { return new Type[0]; }
                        })
                        .FirstOrDefault(t => t.Name == "Pawn_CarryTracker" || t.Name == "CarryTracker");

                    if (carryType != null)
                    {
                        
                        var method1 = carryType.GetMethod("TryStartCarry", new Type[] { typeof(Thing) });
                        if (method1 != null)
                        {
                            var postfix1 = typeof(PredationHarmonyPatches).GetMethod(nameof(PawnCarry_TryStartCarry_Postfix), BindingFlags.Static | BindingFlags.Public);
                            harmony.Patch(method1, null, new HarmonyMethod(postfix1));
                        }

                        
                        var method2 = carryType.GetMethod("TryStartCarry", new Type[] { typeof(Thing), typeof(int), typeof(bool) });
                        if (method2 != null)
                        {
                            var postfix2 = typeof(PredationHarmonyPatches).GetMethod(nameof(PawnCarry_TryStartCarry_Int_Postfix), BindingFlags.Static | BindingFlags.Public);
                            harmony.Patch(method2, null, new HarmonyMethod(postfix2));
                        }
                    }
                    else
                    {
                        Log.Warning("Zoology: Pawn_CarryTracker type not found in loaded assemblies; skipping TryStartCarry patches.");
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"Zoology: Exception while trying to patch TryStartCarry: {ex}");
                }

                
                try
                {
                    var targetIngested = AccessTools.Method(typeof(Thing), "Ingested", new Type[] { typeof(Pawn), typeof(float) });
                    if (targetIngested == null)
                    {
                        targetIngested = typeof(Thing).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                            .FirstOrDefault(m => m.Name == "Ingested" && m.GetParameters().Length >= 1 && m.GetParameters()[0].ParameterType == typeof(Pawn));
                    }

                    if (targetIngested != null)
                    {
                        var postfixIngested = typeof(PredationHarmonyPatches).GetMethod(nameof(Thing_Ingested_Postfix), BindingFlags.Static | BindingFlags.Public);
                        harmony.Patch(targetIngested, null, new HarmonyMethod(postfixIngested));
                    }
                    else
                    {
                        Log.Warning("Zoology: Thing.Ingested method not found; skipping ingest-time defend patch.");
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"Zoology: Exception while trying to patch Thing.Ingested: {ex}");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: Harmony patches failed: {ex}");
            }
        }

        public static void ResetPatchedState()
        {
            patched = false;
        }

        
        public static void FoodOptimality_Postfix(Pawn eater, Thing foodSource, ThingDef foodDef, float dist, bool takingToInventory, ref float __result)
        {
            if (eater == null || foodSource == null) return;
            bool isPredator = eater.RaceProps?.predator == true;
            if (!isPredator && eater.RaceProps?.Animal != true) return;

            var corpse = foodSource as Corpse;
            Pawn livePawn = null;
            if (corpse == null)
            {
                Pawn maybePawn = foodSource as Pawn;
                if (maybePawn != null && maybePawn.Dead)
                {
                    corpse = maybePawn.Corpse;
                }
                else
                {
                    livePawn = maybePawn;
                }
            }

            bool hasDynamicPairSource = corpse != null || livePawn != null;
            if (!hasDynamicPairSource)
            {
                if (!isPredator)
                {
                    return;
                }

                if (TryGetStaticIngestiblePreferabilityDelta(foodSource, out float staticDelta))
                {
                    __result += staticDelta;
                }

                return;
            }

            TickManager tickManager = Find.TickManager;
            int currentTick = tickManager?.TicksGame ?? 0;
            EnsureRuntimeCacheState(currentTick, tickManager);

            Thing cacheKey = corpse ?? (Thing)livePawn;
            long pairKey = FoodOptimalityPairKey(eater, cacheKey);
            if (TryGetFoodOptimalityDeltaCached(currentTick, pairKey, out float cachedDelta))
            {
                __result += cachedDelta;
                return;
            }

            if (!ZoologyTickLimiter.TryConsumeFoodOptimality(ZoologyTickLimiter.FoodOptimalityBudgetPerTick))
            {
                return;
            }

            float delta = 0f;
            if (corpse != null)
            {
                var comp = PredatorPreyPairGameComponent.Instance;
                if (comp == null)
                {
                    return;
                }
                try
                {
                    if (TryGetCorpseFoodState(comp, eater, corpse, out CorpseFoodStateCacheEntry state))
                    {
                        if (isPredator)
                        {
                            if (state.IsPaired)
                            {
                                delta += OwnedCorpseBonus;
                            }
                            else if (state.IsEffectivelyUnowned)
                            {
                                delta += UnpairedCorpseBonus;
                            }
                            else
                            {
                                delta += GuardedCorpsePenalty;
                            }
                        }
                        else
                        {
                            if (!state.IsPaired && !state.IsEffectivelyUnowned)
                            {
                                delta += GuardedCorpsePenalty;
                            }
                        }
                        StoreFoodOptimalityDelta(currentTick, pairKey, delta);
                        __result += delta;
                        return;
                    }
                }
                catch (Exception exCorp)
                {
                    Log.Warning($"Zoology: FoodOptimality_Postfix: error handling corpse logic: {exCorp}");
                }
            }

            if (!isPredator)
            {
                return;
            }

            if (livePawn != null && !livePawn.Dead)
            {
                bool isAcceptable;
                if (PredationDecisionCache.TryGetAcceptablePrey(eater, livePawn, out bool cachedAcceptable))
                {
                    isAcceptable = cachedAcceptable;
                }
                else if (TryConsumeBudget(ref livePreyBudgetTick, ref livePreyBudgetRemaining, ZoologyTickLimiter.PredationFood.LivePreyAcceptableBudgetPerTick))
                {
                    isAcceptable = FoodUtility.IsAcceptablePreyFor(eater, livePawn);
                    PredationDecisionCache.StoreAcceptablePrey(eater, livePawn, isAcceptable);
                }
                else
                {
                    return;
                }
                if (isAcceptable)
                {
                    delta += LiveAcceptablePreyBonus;
                }
                StoreFoodOptimalityDelta(currentTick, pairKey, delta);
                __result += delta;
                return;
            }
        }

        public static void BestFoodSourceOnMap_Postfix(Pawn getter, Pawn eater, bool desperate, ref Thing __result, ref ThingDef foodDef)
        {
            try
            {
                Pawn predator = eater ?? getter;
                if (predator?.RaceProps?.predator != true || __result == null)
                {
                    return;
                }

                var comp = PredatorPreyPairGameComponent.Instance;
                if (comp == null)
                {
                    return;
                }

                if (ShouldBlockLivePreyHunt(predator) && __result is Pawn livePawn && !livePawn.Dead)
                {
                    var paired = comp.GetPairedCorpse(predator);
                    if (paired != null && !paired.Destroyed && TryGetCorpseFoodDefForPredator(predator, paired, out ThingDef pairedFoodDef))
                    {
                        __result = paired;
                        if (pairedFoodDef != null)
                        {
                            foodDef = pairedFoodDef;
                        }
                        return;
                    }

                    __result = null;
                    foodDef = null;
                    return;
                }

                Corpse corpse = TryGetCorpseFromThing(__result);
                if (corpse == null)
                {
                    return;
                }

                if (TryGetPreferredPairedCorpse(comp, predator, corpse, out Corpse preferredPairedCorpse, out ThingDef preferredFoodDef))
                {
                    __result = preferredPairedCorpse;
                    if (preferredFoodDef != null)
                    {
                        foodDef = preferredFoodDef;
                    }
                    return;
                }

                if (!ShouldPreferLivePreyOverCorpse(predator, corpse))
                {
                    return;
                }

                Pawn huntTarget = TryGetVanillaHuntTarget(predator, desperate);
                if (huntTarget == null)
                {
                    return;
                }

                __result = null;
                foodDef = null;
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: BestFoodSourceOnMap_Postfix exception: {ex}");
            }
        }

        private static Corpse TryGetCorpseFromThing(Thing thing)
        {
            if (thing == null)
            {
                return null;
            }

            if (thing is Corpse corpse)
            {
                return corpse;
            }

            if (thing is Pawn deadPawn && deadPawn.Dead)
            {
                return deadPawn.Corpse;
            }

            return null;
        }

        private static bool TryGetPreferredPairedCorpse(
            PredatorPreyPairGameComponent comp,
            Pawn predator,
            Corpse selectedCorpse,
            out Corpse preferredCorpse,
            out ThingDef preferredFoodDef)
        {
            preferredCorpse = null;
            preferredFoodDef = null;

            if (comp == null || predator == null || selectedCorpse == null)
            {
                return false;
            }

            Corpse pairedCorpse;
            try
            {
                pairedCorpse = comp.GetPairedCorpse(predator);
            }
            catch
            {
                return false;
            }

            if (pairedCorpse == null || pairedCorpse == selectedCorpse || pairedCorpse.Destroyed || pairedCorpse.Map != predator.Map)
            {
                return false;
            }

            try
            {
                if (!comp.IsPaired(predator, pairedCorpse))
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }

            if (!TryGetCorpseFoodDefForPredator(predator, pairedCorpse, out ThingDef pairedFoodDef))
            {
                return false;
            }

            bool canReserveAndReach;
            try
            {
                canReserveAndReach = predator.CanReserveAndReach(pairedCorpse, PathEndMode.OnCell, Danger.Some, 1, -1, null, false);
            }
            catch
            {
                canReserveAndReach = false;
            }

            if (!canReserveAndReach)
            {
                try
                {
                    canReserveAndReach = predator.CanReserveAndReach(pairedCorpse, PathEndMode.OnCell, Danger.Deadly, 1, -1, null, false);
                }
                catch
                {
                    canReserveAndReach = false;
                }
            }

            if (!canReserveAndReach)
            {
                return false;
            }

            preferredCorpse = pairedCorpse;
            preferredFoodDef = pairedFoodDef;
            return true;
        }

        private static bool TryGetCorpseFoodDefForPredator(Pawn predator, Corpse corpse, out ThingDef foodDef)
        {
            foodDef = null;
            if (predator == null || corpse == null || corpse.Destroyed)
            {
                return false;
            }

            try
            {
                if (corpse.IsDessicated())
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }

            try
            {
                if (!corpse.IngestibleNow)
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }

            try
            {
                if (corpse.IsForbidden(predator))
                {
                    return false;
                }
            }
            catch
            {
            }

            try
            {
                foodDef = FoodUtility.GetFinalIngestibleDef(corpse, false);
            }
            catch
            {
                foodDef = null;
            }

            if (foodDef == null || !foodDef.IsNutritionGivingIngestible)
            {
                return false;
            }

            try
            {
                return predator.RaceProps == null || predator.RaceProps.CanEverEat(foodDef);
            }
            catch
            {
                return false;
            }
        }

        private static bool ShouldPreferLivePreyOverCorpse(Pawn predator, Corpse corpse)
        {
            if (predator == null || corpse == null)
            {
                return false;
            }

            return TryGetCorpseFoodState(PredatorPreyPairGameComponent.Instance, predator, corpse, out CorpseFoodStateCacheEntry state)
                && !state.IsPaired
                && !state.IsEffectivelyUnowned;
        }

        internal static bool ShouldBlockLivePreyHunt(Pawn predator)
        {
            if (predator == null || predator.RaceProps?.predator != true)
            {
                return false;
            }

            Faction faction = predator.Faction;
            if (faction != null && !IsPhotonozoaFaction(faction))
            {
                return false;
            }

            var comp = PredatorPreyPairGameComponent.Instance;
            if (comp == null)
            {
                return false;
            }

            Corpse paired = comp.GetPairedCorpse(predator);
            if (paired == null || paired.Destroyed)
            {
                return false;
            }

            return comp.IsPaired(predator, paired);
        }

        private static bool IsPhotonozoaFaction(Faction faction)
        {
            var defName = faction?.def?.defName;
            return !string.IsNullOrEmpty(defName)
                && defName.Equals("Photonozoa", StringComparison.OrdinalIgnoreCase);
        }

        private static Pawn TryGetVanillaHuntTarget(Pawn predator, bool desperate)
        {
            if (BestPawnToHuntForPredatorFunc == null || predator == null)
            {
                return null;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            int mapId = predator.Map?.uniqueID ?? -1;
            long key = VanillaHuntTargetPairKey(predator, desperate);
            if (key != 0L
                && currentTick > 0
                && vanillaHuntTargetCacheByPredatorKey.TryGetValue(key, out VanillaHuntTargetCacheEntry cached)
                && currentTick - cached.Tick <= VanillaHuntTargetCacheDurationTicks
                && cached.MapId == mapId)
            {
                if (!cached.HasTarget)
                {
                    return null;
                }

                if (IsValidVanillaHuntTarget(predator, cached.Target))
                {
                    return cached.Target;
                }

                vanillaHuntTargetCacheByPredatorKey.Remove(key);
            }

            if (!TryConsumeBudget(
                    ref vanillaHuntTargetBudgetTick,
                    ref vanillaHuntTargetBudgetRemaining,
                    VanillaHuntTargetBudgetPerTick))
            {
                return null;
            }

            try
            {
                Pawn huntTarget = BestPawnToHuntForPredatorFunc(predator, desperate);
                if (!IsValidVanillaHuntTarget(predator, huntTarget))
                {
                    huntTarget = null;
                }

                if (key != 0L && currentTick > 0)
                {
                    vanillaHuntTargetCacheByPredatorKey[key] = new VanillaHuntTargetCacheEntry(
                        huntTarget,
                        huntTarget != null,
                        mapId,
                        currentTick);
                    CleanupVanillaHuntTargetCacheIfNeeded(currentTick);
                }

                return huntTarget;
            }
            catch
            {
                return null;
            }
        }

        private static long VanillaHuntTargetPairKey(Pawn predator, bool desperate)
        {
            if (predator == null)
            {
                return 0L;
            }

            uint predatorId = (uint)predator.thingIDNumber;
            return ((long)predatorId << 1) | (desperate ? 1L : 0L);
        }

        private static bool IsValidVanillaHuntTarget(Pawn predator, Pawn target)
        {
            return predator != null
                && target != null
                && !target.Dead
                && !target.Destroyed
                && target.Spawned
                && predator.Map != null
                && target.Map == predator.Map;
        }

        private static void CleanupVanillaHuntTargetCacheIfNeeded(int currentTick)
        {
            if (currentTick - lastVanillaHuntTargetCleanupTick < VanillaHuntTargetCacheCleanupIntervalTicks)
            {
                return;
            }

            lastVanillaHuntTargetCleanupTick = currentTick;
            vanillaHuntTargetCleanupBuffer.Clear();
            foreach (KeyValuePair<long, VanillaHuntTargetCacheEntry> entry in vanillaHuntTargetCacheByPredatorKey)
            {
                VanillaHuntTargetCacheEntry cached = entry.Value;
                if (currentTick - cached.Tick > VanillaHuntTargetCacheDurationTicks)
                {
                    vanillaHuntTargetCleanupBuffer.Add(entry.Key);
                    continue;
                }

                if (!cached.HasTarget)
                {
                    continue;
                }

                if (cached.Target == null || cached.Target.Dead || cached.Target.Destroyed)
                {
                    vanillaHuntTargetCleanupBuffer.Add(entry.Key);
                }
            }

            for (int i = 0; i < vanillaHuntTargetCleanupBuffer.Count; i++)
            {
                vanillaHuntTargetCacheByPredatorKey.Remove(vanillaHuntTargetCleanupBuffer[i]);
            }

            vanillaHuntTargetCleanupBuffer.Clear();
        }

        private static Func<Pawn, bool, Pawn> CreateBestPawnToHuntForPredatorDelegate()
        {
            try
            {
                var method = AccessTools.Method(typeof(FoodUtility), "BestPawnToHuntForPredator", new[] { typeof(Pawn), typeof(bool) });
                if (method == null)
                {
                    return null;
                }

                return (Func<Pawn, bool, Pawn>)Delegate.CreateDelegate(typeof(Func<Pawn, bool, Pawn>), method);
            }
            catch
            {
                return null;
            }
        }

        private static bool TryGetCorpseFoodState(PredatorPreyPairGameComponent comp, Pawn eater, Corpse corpse, out CorpseFoodStateCacheEntry state)
        {
            state = default;
            if (comp == null || eater == null || corpse == null)
            {
                return false;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            long pairKey = CorpseFoodPairKey(eater, corpse);
            if (pairKey != 0L
                && currentTick > 0
                && corpseFoodStateCacheByPairKey.TryGetValue(pairKey, out CorpseFoodStateCacheEntry cached)
                && currentTick - cached.Tick <= ZoologyTickLimiter.PredationFood.CorpseFoodStateCacheDurationTicks)
            {
                state = cached;
                return true;
            }

            bool isPaired;
            try
            {
                isPaired = comp.IsPaired(eater, corpse);
            }
            catch
            {
                isPaired = false;
            }

            bool isEffectivelyUnowned = !isPaired;
            if (!isPaired)
            {
                if (!comp.HasAnyPairedPredatorsForCorpse(corpse))
                {
                    isEffectivelyUnowned = true;
                }
                else if (!TryConsumeBudget(ref corpseFoodStateBudgetTick, ref corpseFoodStateBudgetRemaining, ZoologyTickLimiter.PredationFood.CorpseFoodStateBudgetPerTick))
                {
                    return false;
                }
                else
                {
                    try
                    {
                        isEffectivelyUnowned = comp.IsCorpseEffectivelyUnownedFor(eater, corpse);
                    }
                    catch
                    {
                        isEffectivelyUnowned = true;
                    }
                }
            }

            state = new CorpseFoodStateCacheEntry(isPaired, isEffectivelyUnowned, currentTick);
            if (pairKey != 0L && currentTick > 0)
            {
                corpseFoodStateCacheByPairKey[pairKey] = state;
                CleanupCorpseFoodStateCacheIfNeeded(currentTick);
            }

            return true;
        }

        private static bool TryConsumeBudget(ref int tick, ref int remaining, int perTick)
        {
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (currentTick <= 0)
            {
                return false;
            }

            if (tick != currentTick)
            {
                tick = currentTick;
                remaining = perTick;
            }

            if (remaining <= 0)
            {
                return false;
            }

            remaining--;
            return true;
        }

        private static void CleanupCorpseFoodStateCacheIfNeeded(int currentTick)
        {
            if (currentTick - lastCorpseFoodStateCacheCleanupTick < ZoologyTickLimiter.PredationFood.CorpseFoodStateCacheCleanupIntervalTicks)
            {
                return;
            }

            lastCorpseFoodStateCacheCleanupTick = currentTick;
            List<long> staleKeys = null;
            foreach (KeyValuePair<long, CorpseFoodStateCacheEntry> entry in corpseFoodStateCacheByPairKey)
            {
                if (currentTick - entry.Value.Tick <= ZoologyTickLimiter.PredationFood.CorpseFoodStateCacheDurationTicks)
                {
                    continue;
                }

                if (staleKeys == null)
                {
                    staleKeys = new List<long>(64);
                }

                staleKeys.Add(entry.Key);
            }

            if (staleKeys == null)
            {
                return;
            }

            for (int i = 0; i < staleKeys.Count; i++)
            {
                corpseFoodStateCacheByPairKey.Remove(staleKeys[i]);
            }
        }

        private static bool TryGetStaticIngestiblePreferabilityDelta(Thing foodSource, out float delta)
        {
            delta = 0f;
            if (foodSource is Pawn)
            {
                return false;
            }

            ThingDef def = foodSource?.def;
            if (def == null)
            {
                return false;
            }

            if (TryGetStaticIngestiblePreferabilityDeltaByShortHash(def, out float cachedByShortHash, out bool hasDeltaByShortHash))
            {
                delta = cachedByShortHash;
                return hasDeltaByShortHash;
            }

            if (staticIngestibleOptimalityDeltaByDefFallback.TryGetValue(def, out float cachedByFallback))
            {
                delta = cachedByFallback;
                return cachedByFallback != 0f;
            }

            IngestibleProperties ingestible = def.ingestible;
            if (ingestible == null || !def.IsNutritionGivingIngestible || def.IsDrug)
            {
                StoreStaticIngestiblePreferabilityDelta(def, 0f, false);
                return false;
            }

            float computed = (float)ingestible.preferability * 4f;
            bool hasDelta = computed != 0f;
            StoreStaticIngestiblePreferabilityDelta(def, computed, hasDelta);
            delta = computed;
            return hasDelta;
        }

        private static bool TryGetStaticIngestiblePreferabilityDeltaByShortHash(ThingDef def, out float delta, out bool hasDelta)
        {
            delta = 0f;
            hasDelta = false;

            ushort shortHash = def.shortHash;
            if (shortHash == 0)
            {
                return false;
            }

            if (!ReferenceEquals(staticIngestibleDefsByShortHash[shortHash], def))
            {
                return false;
            }

            byte state = staticIngestibleDeltaStateByShortHash[shortHash];
            if (state == StaticIngestibleDeltaStateUnknown)
            {
                return false;
            }

            if (state == StaticIngestibleDeltaStateHasDelta)
            {
                delta = staticIngestibleOptimalityDeltaByShortHash[shortHash];
                hasDelta = true;
            }

            return true;
        }

        private static void StoreStaticIngestiblePreferabilityDelta(ThingDef def, float delta, bool hasDelta)
        {
            ushort shortHash = def.shortHash;
            if (shortHash != 0)
            {
                ThingDef cachedDef = staticIngestibleDefsByShortHash[shortHash];
                if (cachedDef == null || ReferenceEquals(cachedDef, def))
                {
                    staticIngestibleDefsByShortHash[shortHash] = def;
                    staticIngestibleOptimalityDeltaByShortHash[shortHash] = delta;
                    staticIngestibleDeltaStateByShortHash[shortHash] = hasDelta
                        ? StaticIngestibleDeltaStateHasDelta
                        : StaticIngestibleDeltaStateNoDelta;
                    return;
                }
            }

            staticIngestibleOptimalityDeltaByDefFallback[def] = delta;
        }

        private static bool TryGetFoodOptimalityDeltaCached(int currentTick, long pairKey, out float delta)
        {
            delta = 0f;
            if (pairKey == 0L || currentTick <= 0)
            {
                return false;
            }

            int slotIndex = (int)((ulong)pairKey & FoodOptimalityDeltaHotCacheMask);
            FoodOptimalityDeltaCacheEntry cached = foodOptimalityDeltaHotCacheSlots[slotIndex];
            if (cached.Key != pairKey)
            {
                return false;
            }

            if (currentTick - cached.Tick > ZoologyTickLimiter.PredationFood.FoodOptimalityDeltaCacheDurationTicks)
            {
                return false;
            }

            delta = cached.Delta;
            return true;
        }

        private static void StoreFoodOptimalityDelta(int currentTick, long pairKey, float delta)
        {
            if (pairKey == 0L || currentTick <= 0)
            {
                return;
            }

            int slotIndex = (int)((ulong)pairKey & FoodOptimalityDeltaHotCacheMask);
            foodOptimalityDeltaHotCacheSlots[slotIndex] = new FoodOptimalityDeltaCacheEntry(pairKey, delta, currentTick);
        }

        private static void EnsureRuntimeCacheState(int currentTick, TickManager tickManager)
        {
            if (currentTick > 0
                && runtimeCacheEnsureTick == currentTick
                && ReferenceEquals(runtimeCacheTickManager, tickManager))
            {
                return;
            }

            Game currentGame = Current.Game;
            bool tickManagerChanged = !ReferenceEquals(runtimeCacheTickManager, tickManager);
            bool gameChanged = !ReferenceEquals(runtimeCacheGame, currentGame);
            bool tickRewound = currentTick > 0 && runtimeCacheLastTick > 0 && currentTick < runtimeCacheLastTick;
            if (tickManagerChanged || gameChanged || tickRewound)
            {
                corpseFoodStateCacheByPairKey.Clear();
                vanillaHuntTargetCacheByPredatorKey.Clear();
                vanillaHuntTargetCleanupBuffer.Clear();
                System.Array.Clear(foodOptimalityDeltaHotCacheSlots, 0, foodOptimalityDeltaHotCacheSlots.Length);
                lastCorpseFoodStateCacheCleanupTick = -ZoologyTickLimiter.PredationFood.CorpseFoodStateCacheCleanupIntervalTicks;
                lastVanillaHuntTargetCleanupTick = -VanillaHuntTargetCacheCleanupIntervalTicks;
                corpseFoodStateBudgetTick = -1;
                corpseFoodStateBudgetRemaining = 0;
                livePreyBudgetTick = -1;
                livePreyBudgetRemaining = 0;
                vanillaHuntTargetBudgetTick = -1;
                vanillaHuntTargetBudgetRemaining = 0;
                runtimeCacheTickManager = tickManager;
                runtimeCacheGame = currentGame;
                runtimeCacheLastTick = -1;
            }

            if (currentTick > 0)
            {
                runtimeCacheLastTick = currentTick;
                runtimeCacheEnsureTick = currentTick;
            }
            else
            {
                runtimeCacheEnsureTick = -1;
            }
        }

        private static long CorpseFoodPairKey(Pawn eater, Corpse corpse)
        {
            if (eater == null || corpse == null)
            {
                return 0L;
            }

            uint eaterId = (uint)eater.thingIDNumber;
            uint corpseId = (uint)corpse.thingIDNumber;
            return ((long)eaterId << 32) | corpseId;
        }

        private static long FoodOptimalityPairKey(Pawn eater, Thing foodSource)
        {
            if (eater == null || foodSource == null)
            {
                return 0L;
            }

            uint eaterId = (uint)eater.thingIDNumber;
            uint foodId = (uint)foodSource.thingIDNumber;
            return ((long)eaterId << 32) | foodId;
        }

        
        public static void Pawn_Kill_Postfix(Pawn __instance, object[] __args)
        {
            try
            {
                if (__instance == null) return;
                if (__args == null || __args.Length == 0) return;

                object dinfoObj = __args.Length > 0 ? __args[0] : null;
                if (dinfoObj == null) return;

                DamageInfo? dinfo = null;
                try
                {
                    if (dinfoObj is DamageInfo dd) dinfo = dd;
                    else
                    {
                        var t = dinfoObj.GetType();
                        var hasValProp = t.GetProperty("HasValue");
                        var valProp = t.GetProperty("Value");
                        if (hasValProp != null && valProp != null)
                        {
                            bool has = (bool)hasValProp.GetValue(dinfoObj, null);
                            if (has)
                            {
                                var val = valProp.GetValue(dinfoObj, null);
                                if (val is DamageInfo) dinfo = (DamageInfo)val;
                            }
                        }
                    }
                }
                catch
                {
                    dinfo = null;
                }

                if (dinfo == null) return;
                var inst = dinfo.Value.Instigator;
                if (!(inst is Pawn killer)) return;
                if (!killer.RaceProps.predator) return;

                Job kj = null;
                try { kj = killer.CurJob; } catch { kj = null; }
                bool isHuntDriver = false;
                if (kj != null && kj.def != null && kj.def.driverClass != null)
                {
                    var driver = kj.def.driverClass;
                    if (typeof(JobDriver_PredatorHunt).IsAssignableFrom(driver)) isHuntDriver = true;
                }
                if (!isHuntDriver)
                {
                    return;
                }

                Pawn huntPrey = null;
                try { huntPrey = killer.jobs?.curDriver is JobDriver_PredatorHunt huntDriver ? huntDriver.Prey : null; } catch { huntPrey = null; }
                Thing jobTarget = null;
                try { jobTarget = killer.CurJob?.targetA.Thing; } catch { jobTarget = null; }

                if (huntPrey != null && huntPrey != __instance)
                {
                    return;
                }

                if (huntPrey == null && jobTarget != null && jobTarget != __instance)
                {
                    return;
                }

                Corpse corpse = __instance.Corpse ?? PredationLookupUtility.FindSpawnedCorpseForInnerPawn(__instance);

                if (corpse == null)
                {
                    return;
                }

                var comp = PredatorPreyPairGameComponent.Instance;
                if (comp != null)
                {
                    comp.RegisterPairFromKill(killer, corpse);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: Pawn_Kill_Postfix exception: {ex}");
            }
        }

        
        public static void PawnCarry_TryStartCarry_Postfix(object __instance, Thing item, bool __result)
        {
            try
            {
                if (!__result || item == null) return;
                var corpse = item as Corpse;
                if (corpse == null) return;

                Pawn carrier = PredationLookupUtility.TryGetCarrierPawn(__instance);
                if (carrier == null)
                {
                    carrier = PredationLookupUtility.FindPawnHoldingThing(item.thingIDNumber);
                }

                if (carrier == null) return;

                var comp = PredatorPreyPairGameComponent.Instance;
                if (comp != null)
                {
                    comp.TryTriggerDefendFor(corpse, carrier);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: PawnCarry_TryStartCarry_Postfix exception: {ex}");
            }
        }

        
        public static void PawnCarry_TryStartCarry_Int_Postfix(object __instance, Thing item, int count, bool reserve, int __result)
        {
            try
            {
                if (__result <= 0 || item == null) return;
                var corpse = item as Corpse;
                if (corpse == null) return;

                Pawn carrier = PredationLookupUtility.TryGetCarrierPawn(__instance);
                if (carrier == null)
                {
                    carrier = PredationLookupUtility.FindPawnHoldingThing(item.thingIDNumber);
                }

                if (carrier == null) return;

                var comp = PredatorPreyPairGameComponent.Instance;
                if (comp != null)
                {
                    comp.TryTriggerDefendFor(corpse, carrier);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: PawnCarry_TryStartCarry_Int_Postfix exception: {ex}");
            }
        }

        
        public static void Thing_Ingested_Postfix(Thing __instance, Pawn ingester, float nutritionWanted)
        {
            try
            {
                if (__instance == null || ingester == null) return;
                var corpse = __instance as Corpse;
                if (corpse == null) return;

                var comp = PredatorPreyPairGameComponent.Instance;
                if (comp != null)
                {
                    comp.TryTriggerDefendFor(corpse, ingester);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: Thing_Ingested_Postfix exception: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(FoodUtility), "BestPawnToHuntForPredator", new Type[] { typeof(Pawn), typeof(bool) })]
    public static class Patch_FoodUtility_BestPawnToHuntForPredator_BlockWhilePaired
    {
        public static bool Prepare() => PredationSettingsGate.EnablePredatorDefendCorpse();

        public static bool Prefix(Pawn predator, bool forceScanWholeMap, ref Pawn __result)
        {
            try
            {
                if (!PredationHarmonyPatches.ShouldBlockLivePreyHunt(predator))
                {
                    return true;
                }

                __result = null;
                return false;
            }
            catch
            {
                return true;
            }
        }
    }
}
