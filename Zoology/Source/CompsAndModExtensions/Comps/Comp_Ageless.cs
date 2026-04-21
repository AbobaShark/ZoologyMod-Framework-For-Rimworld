using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    
    public class CompProperties_Ageless : CompProperties
    {
        public int cleanupIntervalTicks = 6000;

        public CompProperties_Ageless()
        {
            this.compClass = typeof(CompAgeless);
        }
    }

    
    public class CompAgeless : ThingComp
    {
        private int cleanupInterval = 6000;
        private readonly List<Hediff> hediffRemovalBuffer = new List<Hediff>(4);

        private CompProperties_Ageless PropsAgeless => (CompProperties_Ageless)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (PropsAgeless != null)
                cleanupInterval = Math.Max(60, PropsAgeless.cleanupIntervalTicks);
        }

        public override void CompTick()
        {
            base.CompTick();

            if (!parent.IsHashIntervalTick(cleanupInterval)) return;

            var settings = ZoologyModSettings.Instance;
            if (settings != null && !settings.EnableAgelessPatch) return;

            Pawn pawn = parent as Pawn;
            if (pawn == null) return;
            if (pawn.Dead) return;
            if (pawn.health?.hediffSet == null) return;

            try
            {
                RemoveForbiddenAgeHediffs(pawn);
            }
            catch (Exception e)
            {
                Log.ErrorOnce($"[Zoology] CompAgeless.RemoveForbiddenAgeHediffs exception: {e}", 21847231);
            }
        }

        private void RemoveForbiddenAgeHediffs(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null) return;

            HashSet<HediffDef> forb = AgelessUtils.GetAgeRelatedHediffDefsForPawnView(pawn);

            List<Hediff> hs = pawn.health.hediffSet.hediffs;
            if (hs == null || hs.Count == 0) return;

            hediffRemovalBuffer.Clear();
            for (int i = 0; i < hs.Count; i++)
            {
                Hediff h = hs[i];
                if (h == null || h.def == null) continue;
                if ((forb != null && forb.Contains(h.def)) || AgelessUtils.IsDiseaseHediff(h.def))
                {
                    hediffRemovalBuffer.Add(h);
                }
            }

            for (int i = 0; i < hediffRemovalBuffer.Count; i++)
            {
                Hediff h = hediffRemovalBuffer[i];
                try
                {
                    pawn.health.RemoveHediff(h);
                    if (Prefs.DevMode)
                    {
                        Log.Message($"[Zoology] CompAgeless removed hediff {h.def.defName} from {pawn.LabelShortCap}");
                    }
                }
                catch (Exception e)
                {
                    Log.Error($"[Zoology] CompAgeless failed to remove hediff {h.def.defName} from {pawn}: {e}");
                }
            }

            hediffRemovalBuffer.Clear();
        }
    }

    
    public static class AgelessUtils
    {
        
        private static readonly Dictionary<string, HashSet<HediffDef>> setCache = new Dictionary<string, HashSet<HediffDef>>();
        private static readonly Dictionary<string, HashSet<HediffDef>> pawnKindCache = new Dictionary<string, HashSet<HediffDef>>();
        private static readonly Dictionary<Type, bool> chronicDiseaseTypeCache = new Dictionary<Type, bool>();
        private static readonly Dictionary<HediffDef, bool> chronicDiseaseDefCache = new Dictionary<HediffDef, bool>();
        private static readonly Dictionary<HediffDef, bool> diseaseDefCache = new Dictionary<HediffDef, bool>();
        private static readonly Dictionary<string, float> growthCompletionAgeCache = new Dictionary<string, float>();
        private const string FallbackHediffGiverSetDefName = "OrganicStandard";

        
        private static readonly HashSet<string> ageGiverTypeNames = new HashSet<string>
        {
            "HediffGiver_Birthday",
            "HediffGiver_BrainInjury",
            "HediffGiver_RandomAgeCurved"
        };

        
        public static HashSet<HediffDef> GetAgeRelatedHediffsFromSet(HediffGiverSetDef set)
        {
            HashSet<HediffDef> cached = GetAgeRelatedHediffsFromSetCached(set);
            return cached != null ? new HashSet<HediffDef>(cached) : new HashSet<HediffDef>();
        }

        private static HashSet<HediffDef> GetAgeRelatedHediffsFromSetCached(HediffGiverSetDef set)
        {
            if (set == null) return null;
            if (setCache.TryGetValue(set.defName, out var cached)) return cached;

            var result = new HashSet<HediffDef>();
            if (set.hediffGivers == null)
            {
                setCache[set.defName] = result;
                return result;
            }

            foreach (var giver in set.hediffGivers)
            {
                if (giver == null) continue;
                var gType = giver.GetType();
                if (!ageGiverTypeNames.Contains(gType.Name)) continue;

                
                HediffDef hd = null;
                
                var f = gType.GetField("hediff", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null)
                {
                    hd = f.GetValue(giver) as HediffDef;
                }
                else
                {
                    
                    var p = gType.GetProperty("hediff", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (p != null)
                        hd = p.GetValue(giver) as HediffDef;
                }

                if (hd != null)
                {
                    result.Add(hd);
                }
                else
                {
                    
                    
                    
                    Log.Message($"[Zoology] AgelessUtils: couldn't get HediffDef from giver {gType.Name} in set {set.defName}");
                }
            }

            setCache[set.defName] = result;
            return result;
        }

        
        
        
        public static HashSet<HediffDef> GetAgeRelatedHediffDefsForPawn(Pawn pawn)
        {
            HashSet<HediffDef> cached = GetAgeRelatedHediffDefsForPawnCached(pawn);
            return cached != null ? new HashSet<HediffDef>(cached) : new HashSet<HediffDef>();
        }

        internal static HashSet<HediffDef> GetAgeRelatedHediffDefsForPawnView(Pawn pawn)
        {
            return GetAgeRelatedHediffDefsForPawnCached(pawn);
        }

        private static HashSet<HediffDef> GetAgeRelatedHediffDefsForPawnCached(Pawn pawn)
        {
            if (pawn == null) return null;

            string pawnKindKey = pawn.kindDef?.defName;
            if (!string.IsNullOrEmpty(pawnKindKey) && pawnKindCache.TryGetValue(pawnKindKey, out var cached))
            {
                return cached;
            }

            var result = new HashSet<HediffDef>();

            try
            {
                var pk = pawn.kindDef;
                if (pk != null)
                {
                    
                    var t = pk.GetType();
                    var f = t.GetField("hediffGiverSets", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (f != null)
                    {
                        var list = f.GetValue(pk) as IEnumerable<HediffGiverSetDef>;
                        if (list != null)
                        {
                            foreach (var set in list)
                            {
                                var setDefs = GetAgeRelatedHediffsFromSetCached(set);
                                if (setDefs == null) continue;
                                foreach (var s in setDefs) result.Add(s);
                            }
                        }
                    }
                    else
                    {
                        
                        var p = t.GetProperty("hediffGiverSets", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (p != null)
                        {
                            var list = p.GetValue(pk) as IEnumerable<HediffGiverSetDef>;
                            if (list != null)
                            {
                                foreach (var set in list)
                                {
                                    var setDefs = GetAgeRelatedHediffsFromSetCached(set);
                                    if (setDefs == null) continue;
                                    foreach (var s in setDefs) result.Add(s);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[Zoology] AgelessUtils.GetAgeRelatedHediffDefsForPawn reflection error: {ex}");
            }

            
            if (result.Count == 0)
            {
                var fallback = DefDatabase<HediffGiverSetDef>.GetNamedSilentFail(FallbackHediffGiverSetDefName);
                if (fallback != null)
                {
                    var setDefs = GetAgeRelatedHediffsFromSetCached(fallback);
                    if (setDefs != null)
                    {
                        foreach (var s in setDefs) result.Add(s);
                    }
                }
            }

            if (!string.IsNullOrEmpty(pawnKindKey))
            {
                pawnKindCache[pawnKindKey] = result;
            }

            return result;
        }

        
        public static bool IsHediffForbiddenForPawn(Pawn pawn, HediffDef hediff)
        {
            if (pawn == null || hediff == null) return false;
            if (IsDiseaseHediff(hediff))
            {
                return true;
            }
            var forb = GetAgeRelatedHediffDefsForPawnCached(pawn);
            return forb != null && forb.Contains(hediff);
        }

        public static bool IsDiseaseHediff(HediffDef hediff)
        {
            if (hediff == null) return false;

            if (diseaseDefCache.TryGetValue(hediff, out bool cached))
            {
                return cached;
            }

            bool result = false;
            try
            {
                if (IsChronicDiseaseHediff(hediff))
                {
                    diseaseDefCache[hediff] = true;
                    return true;
                }
            }
            catch
            {
            }

            try
            {
                if (hediff.PossibleToDevelopImmunityNaturally())
                {
                    diseaseDefCache[hediff] = true;
                    return true;
                }
            }
            catch
            {
            }

            if (!result)
            {
                try
                {
                    result = IsDefDescendantOf(hediff, "DiseaseBase");
                }
                catch
                {
                    result = false;
                }
            }

            diseaseDefCache[hediff] = result;
            return result;
        }

        public static bool IsChronicDiseaseHediff(HediffDef hediff)
        {
            if (hediff == null) return false;
            if (chronicDiseaseDefCache.TryGetValue(hediff, out bool cachedDef))
            {
                return cachedDef;
            }

            bool result = false;

            try
            {
                if (hediff.chronic)
                {
                    chronicDiseaseDefCache[hediff] = true;
                    return true;
                }
            }
            catch
            {
            }

            Type t = hediff.hediffClass;
            try
            {
                if (t != null)
                {
                    if (chronicDiseaseTypeCache.TryGetValue(t, out bool cachedType))
                    {
                        if (cachedType) result = true;
                    }
                    else
                    {
                        bool typeResult = false;
                        for (Type cur = t; cur != null; cur = cur.BaseType)
                        {
                            if (string.Equals(cur.Name, "ChronicDiseaseBase", StringComparison.Ordinal)
                                || string.Equals(cur.FullName, "RimWorld.ChronicDiseaseBase", StringComparison.Ordinal)
                                || string.Equals(cur.FullName, "ChronicDiseaseBase", StringComparison.Ordinal))
                            {
                                typeResult = true;
                                break;
                            }
                        }

                        chronicDiseaseTypeCache[t] = typeResult;
                        if (typeResult) result = true;
                    }
                }
            }
            catch
            {
                result = false;
            }

            if (!result)
            {
                try
                {
                    result = IsDefDescendantOf(hediff, "ChronicDiseaseBase");
                }
                catch
                {
                    result = false;
                }
            }

            chronicDiseaseDefCache[hediff] = result;
            return result;
        }

        public static bool ShouldStopBiologicalAgingNow(Pawn pawn, Pawn_AgeTracker ageTracker = null)
        {
            if (pawn == null || pawn.Dead || pawn.Destroyed)
            {
                return false;
            }

            Pawn_AgeTracker tracker = ageTracker ?? pawn.ageTracker;
            if (tracker == null)
            {
                return false;
            }

            var race = pawn.RaceProps;
            if (race?.lifeStageAges == null || race.lifeStageAges.Count == 0)
            {
                return false;
            }

            float stopAge = GetGrowthCompletionMinAgeYears(pawn, tracker);
            if (stopAge <= 0f)
            {
                return false;
            }

            return tracker.AgeBiologicalYearsFloat >= stopAge - 0.0001f;
        }

        private static float GetGrowthCompletionMinAgeYears(Pawn pawn, Pawn_AgeTracker tracker)
        {
            if (pawn == null)
            {
                return 0f;
            }

            string key = pawn.def?.defName;
            if (!string.IsNullOrEmpty(key) && growthCompletionAgeCache.TryGetValue(key, out float cached))
            {
                return cached;
            }

            float result = 0f;
            List<LifeStageAge> stages = pawn.RaceProps?.lifeStageAges;
            if (stages != null && stages.Count > 0)
            {
                float maxBodySizeFactor = float.MinValue;
                for (int i = 0; i < stages.Count; i++)
                {
                    LifeStageAge stage = stages[i];
                    if (stage?.def == null) continue;
                    if (stage.def.bodySizeFactor > maxBodySizeFactor)
                    {
                        maxBodySizeFactor = stage.def.bodySizeFactor;
                    }
                }

                if (maxBodySizeFactor > float.MinValue)
                {
                    bool found = false;
                    float earliestAgeAtMaxBody = float.MaxValue;
                    for (int i = 0; i < stages.Count; i++)
                    {
                        LifeStageAge stage = stages[i];
                        if (stage?.def == null) continue;
                        if (stage.def.bodySizeFactor + 0.0001f < maxBodySizeFactor) continue;

                        found = true;
                        if (stage.minAge < earliestAgeAtMaxBody)
                        {
                            earliestAgeAtMaxBody = stage.minAge;
                        }
                    }

                    if (found && earliestAgeAtMaxBody > 0f)
                    {
                        result = earliestAgeAtMaxBody;
                    }
                }
            }

            if (result <= 0f && tracker != null)
            {
                float fallbackAdultMinAge = tracker.AdultMinAge;
                if (fallbackAdultMinAge > 0f)
                {
                    result = fallbackAdultMinAge;
                }
            }

            if (!string.IsNullOrEmpty(key))
            {
                growthCompletionAgeCache[key] = result;
            }

            return result;
        }

        private static bool IsDefDescendantOf(HediffDef candidate, string baseDefName)
        {
            try
            {
                if (candidate == null) return false;
                if (string.Equals(candidate.defName, baseDefName, StringComparison.Ordinal)) return true;

                var visited = new HashSet<string>();
                HediffDef current = candidate;

                while (current != null)
                {
                    if (string.IsNullOrEmpty(current.defName)) break;
                    if (visited.Contains(current.defName)) break;
                    visited.Add(current.defName);

                    if (string.Equals(current.defName, baseDefName, StringComparison.Ordinal)) return true;

                    HediffDef parentDef = null;
                    var parentFieldNames = new[] { "parent", "baseDef", "parentDef" };
                    foreach (var fname in parentFieldNames)
                    {
                        var fi = current.GetType().GetField(fname, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (fi != null && typeof(HediffDef).IsAssignableFrom(fi.FieldType))
                        {
                            parentDef = fi.GetValue(current) as HediffDef;
                            if (parentDef != null) break;
                        }
                    }
                    if (parentDef != null) { current = parentDef; continue; }

                    string parentName = null;
                    var parentNameField = current.GetType().GetField("parentName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                        ?? (FieldInfo)typeof(Def).GetField("parentName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (parentNameField != null) parentName = parentNameField.GetValue(current) as string;
                    if (string.IsNullOrEmpty(parentName))
                    {
                        var prop = current.GetType().GetProperty("ParentName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (prop != null) parentName = prop.GetValue(current) as string;
                    }

                    if (!string.IsNullOrEmpty(parentName))
                    {
                        if (string.Equals(parentName, baseDefName, StringComparison.Ordinal)) return true;
                        var next = DefDatabase<HediffDef>.GetNamedSilentFail(parentName);
                        if (next != null) { current = next; continue; }
                    }

                    break;
                }
            }
            catch
            {
            }

            return false;
        }
    }

    
    [StaticConstructorOnStartup]
    public static class AgelessHarmonyInit
    {
        private static bool patched;
        private static readonly AccessTools.FieldRef<Pawn_HealthTracker, Pawn> HealthTrackerPawnRef =
            AccessTools.FieldRefAccess<Pawn_HealthTracker, Pawn>("pawn");
        private static readonly AccessTools.FieldRef<HediffSet, Pawn> HediffSetPawnRef =
            AccessTools.FieldRefAccess<HediffSet, Pawn>("pawn");
        private static readonly AccessTools.FieldRef<Pawn_AgeTracker, Pawn> AgeTrackerPawnRef =
            AccessTools.FieldRefAccess<Pawn_AgeTracker, Pawn>("pawn");

        static AgelessHarmonyInit()
        {
            EnsurePatched();
        }

        public static void EnsurePatched()
        {
            if (patched)
            {
                return;
            }

            try
            {
                var settings = ZoologyModSettings.Instance;
                if (settings != null && settings.DisableAllRuntimePatches)
                {
                    return;
                }

                if (settings != null && !settings.EnableAgelessPatch)
                {
                    return;
                }

                patched = true;
                var harmony = new Harmony("zoology.ageless");
                
                var hediffGiverType = typeof(HediffGiver);
                var method = AccessTools.Method(hediffGiverType, "TryApply");
                if (method == null)
                {
                    
                    var ms = hediffGiverType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .Where(m => m.Name == "TryApply").ToArray();
                    method = ms.FirstOrDefault();
                }

                if (method == null)
                {
                    Log.Error("[Zoology] AgelessHarmonyInit: couldn't find HediffGiver.TryApply to patch (method==null). Patch skipped.");
                    return;
                }

                var prefix = new HarmonyMethod(typeof(AgelessHarmonyInit).GetMethod(nameof(TryApply_Prefix), BindingFlags.Static | BindingFlags.NonPublic));
                harmony.Patch(method, prefix: prefix);

                PatchAddHediffMethods(harmony);
                PatchHediffSetAddMethods(harmony);
                PatchAgeTrackerMethods(harmony);
            }
            catch (Exception e)
            {
                Log.Error($"[Zoology] AgelessHarmonyInit failed to patch: {e}");
            }
        }

        public static void ResetPatchedState()
        {
            patched = false;
        }

        
        
        private static bool TryApply_Prefix(HediffGiver __instance, Pawn pawn)
        {
            try
            {
                var settings = ZoologyModSettings.Instance;
                if (settings != null && !settings.EnableAgelessPatch)
                {
                    return true;
                }

                if (pawn == null) return true;

                
                var comp = pawn.TryGetComp<CompAgeless>();
                if (comp == null) return true;

                
                HediffDef candidate = null;
                var gType = __instance.GetType();
                var f = gType.GetField("hediff", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null)
                {
                    candidate = f.GetValue(__instance) as HediffDef;
                }
                else
                {
                    var p = gType.GetProperty("hediff", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (p != null)
                        candidate = p.GetValue(__instance) as HediffDef;
                }

                if (candidate == null)
                {
                    
                    return true;
                }

                
                if (AgelessUtils.IsHediffForbiddenForPawn(pawn, candidate))
                {
                    if (Prefs.DevMode)
                    {
                        Log.Message($"[Zoology] blocked applying {candidate.defName} to {pawn.LabelShortCap} from giver {gType.Name}");
                    }
                    return false;
                }

                return true;
            }
            catch (Exception e)
            {
                Log.Error($"[Zoology] TryApply_Prefix exception: {e}");
                return true; 
            }
        }

        private static void PatchAddHediffMethods(Harmony harmony)
        {
            try
            {
                var ht = typeof(Pawn_HealthTracker);
                var addHediffDef = AccessTools.Method(ht, nameof(Pawn_HealthTracker.AddHediff),
                    new[] { typeof(HediffDef), typeof(BodyPartRecord), typeof(DamageInfo?), typeof(DamageWorker.DamageResult) });
                if (addHediffDef != null)
                {
                    var prefix = new HarmonyMethod(typeof(AgelessHarmonyInit).GetMethod(nameof(AddHediffDef_Prefix), BindingFlags.Static | BindingFlags.NonPublic));
                    harmony.Patch(addHediffDef, prefix: prefix);
                }
                else
                {
                    Log.Error("[Zoology] AgelessHarmonyInit: couldn't find Pawn_HealthTracker.AddHediff(HediffDef) to patch.");
                }

                var addHediff = AccessTools.Method(ht, nameof(Pawn_HealthTracker.AddHediff),
                    new[] { typeof(Hediff), typeof(BodyPartRecord), typeof(DamageInfo?), typeof(DamageWorker.DamageResult) });
                if (addHediff != null)
                {
                    var prefix = new HarmonyMethod(typeof(AgelessHarmonyInit).GetMethod(nameof(AddHediff_Prefix), BindingFlags.Static | BindingFlags.NonPublic));
                    harmony.Patch(addHediff, prefix: prefix);
                }
                else
                {
                    Log.Error("[Zoology] AgelessHarmonyInit: couldn't find Pawn_HealthTracker.AddHediff(Hediff) to patch.");
                }
            }
            catch (Exception e)
            {
                Log.Error($"[Zoology] AgelessHarmonyInit failed to patch AddHediff: {e}");
            }
        }

        private static void PatchHediffSetAddMethods(Harmony harmony)
        {
            try
            {
                var hs = typeof(HediffSet);
                var methods = hs.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                int patchedCount = 0;

                foreach (var m in methods)
                {
                    if (m.Name != "AddDirect" && m.Name != "Add") continue;
                    var parms = m.GetParameters();
                    if (parms.Length == 0 || parms[0].ParameterType != typeof(Hediff)) continue;

                    var prefix = new HarmonyMethod(typeof(AgelessHarmonyInit).GetMethod(nameof(HediffSetAdd_Prefix), BindingFlags.Static | BindingFlags.NonPublic));
                    harmony.Patch(m, prefix: prefix);
                    patchedCount++;
                }

                if (patchedCount == 0)
                {
                    Log.Error("[Zoology] AgelessHarmonyInit: couldn't find HediffSet Add/AddDirect(Hediff) to patch.");
                }
            }
            catch (Exception e)
            {
                Log.Error($"[Zoology] AgelessHarmonyInit failed to patch HediffSet Add/AddDirect: {e}");
            }
        }

        private static void PatchAgeTrackerMethods(Harmony harmony)
        {
            return;
        }

        private static bool AddHediffDef_Prefix(Pawn_HealthTracker __instance, HediffDef def)
        {
            try
            {
                var settings = ZoologyModSettings.Instance;
                if (settings != null && !settings.EnableAgelessPatch)
                {
                    return true;
                }

                if (def == null) return true;

                Pawn pawn = HealthTrackerPawnRef(__instance);
                if (pawn == null) return true;

                var comp = pawn.TryGetComp<CompAgeless>();
                if (comp == null) return true;

                if (AgelessUtils.IsHediffForbiddenForPawn(pawn, def))
                {
                    if (Prefs.DevMode)
                    {
                        Log.Message($"[Zoology] blocked AddHediff(HediffDef) {def.defName} on {pawn.LabelShortCap}");
                    }
                    return false;
                }
            }
            catch (Exception e)
            {
                Log.Error($"[Zoology] AddHediffDef_Prefix exception: {e}");
            }

            return true;
        }

        private static bool HediffSetAdd_Prefix(HediffSet __instance, Hediff hediff)
        {
            try
            {
                var settings = ZoologyModSettings.Instance;
                if (settings != null && !settings.EnableAgelessPatch)
                {
                    return true;
                }

                HediffDef def = hediff?.def;
                if (def == null) return true;

                Pawn pawn = HediffSetPawnRef(__instance);
                if (pawn == null) return true;

                var comp = pawn.TryGetComp<CompAgeless>();
                if (comp == null) return true;

                if (AgelessUtils.IsHediffForbiddenForPawn(pawn, def))
                {
                    if (Prefs.DevMode)
                    {
                        Log.Message($"[Zoology] blocked HediffSet.{__instance?.GetType().Name}.Add/AddDirect {def.defName} on {pawn.LabelShortCap}");
                    }
                    return false;
                }
            }
            catch (Exception e)
            {
                Log.Error($"[Zoology] HediffSetAdd_Prefix exception: {e}");
            }

            return true;
        }

        private static bool AddHediff_Prefix(Pawn_HealthTracker __instance, Hediff hediff)
        {
            try
            {
                var settings = ZoologyModSettings.Instance;
                if (settings != null && !settings.EnableAgelessPatch)
                {
                    return true;
                }

                HediffDef def = hediff?.def;
                if (def == null) return true;

                Pawn pawn = HealthTrackerPawnRef(__instance);
                if (pawn == null) return true;

                var comp = pawn.TryGetComp<CompAgeless>();
                if (comp == null) return true;

                if (AgelessUtils.IsHediffForbiddenForPawn(pawn, def))
                {
                    if (Prefs.DevMode)
                    {
                        Log.Message($"[Zoology] blocked AddHediff(Hediff) {def.defName} on {pawn.LabelShortCap}");
                    }
                    return false;
                }
            }
            catch (Exception e)
            {
                Log.Error($"[Zoology] AddHediff_Prefix exception: {e}");
            }

            return true;
        }
    }
}
