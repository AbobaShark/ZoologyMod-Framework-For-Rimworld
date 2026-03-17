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
            if (forb == null || forb.Count == 0) return;

            List<Hediff> hs = pawn.health.hediffSet.hediffs;
            if (hs == null || hs.Count == 0) return;

            hediffRemovalBuffer.Clear();
            for (int i = 0; i < hs.Count; i++)
            {
                Hediff h = hs[i];
                if (h == null || h.def == null) continue;
                if (forb.Contains(h.def))
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
                        Log.Message($"[Zoology] CompAgeless removed age hediff {h.def.defName} from {pawn.LabelShortCap}");
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
            var forb = GetAgeRelatedHediffDefsForPawnCached(pawn);
            return forb != null && forb.Contains(hediff);
        }
    }

    
    [StaticConstructorOnStartup]
    public static class AgelessHarmonyInit
    {
        private static bool patched;

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
                Log.Message("[Zoology] AgelessHarmonyInit: patched HediffGiver.TryApply");
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
    }
}
