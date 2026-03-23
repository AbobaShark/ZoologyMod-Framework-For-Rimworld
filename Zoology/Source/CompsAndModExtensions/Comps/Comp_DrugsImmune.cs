using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    public class CompProperties_DrugsImmune : CompProperties
    {
        
        public int cleanupIntervalTicks = 2000;

        public CompProperties_DrugsImmune()
        {
            this.compClass = typeof(CompDrugsImmune);
        }
    }

    public class CompDrugsImmune : ThingComp
    {
        private int cleanupInterval = 2000;
        private readonly List<Hediff> hediffRemovalBuffer = new List<Hediff>(4);

        private CompProperties_DrugsImmune PropsDrugsImmune => (CompProperties_DrugsImmune)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            cleanupInterval = Math.Max(60, PropsDrugsImmune?.cleanupIntervalTicks ?? 2000);
        }

        public override void CompTick()
        {
            var settings = ZoologyModSettings.Instance;
            if (settings != null && !settings.EnableDrugsImmunePatch) return;

            Pawn pawn = parent as Pawn;
            if (pawn == null) return;
            if (pawn.Destroyed || pawn.Dead) return;
            if (pawn.health?.hediffSet == null) return;

            if (!parent.IsHashIntervalTick(cleanupInterval)) return;

            try
            {
                RemoveDrugHediffs(pawn);
            }
            catch (Exception e)
            {
                Log.Error($"[Zoology] CompDrugsImmune.RemoveDrugHediffs exception: {e}");
            }
        }

        private void RemoveDrugHediffs(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null) return;

            var hs = pawn.health.hediffSet.hediffs;
            if (hs == null || hs.Count == 0) return;

            hediffRemovalBuffer.Clear();
            for (int i = 0; i < hs.Count; i++)
            {
                Hediff h = hs[i];
                if (h == null || h.def == null) continue;

                try
                {
                    if (DrugUtils.IsHediffDrugOrAddiction(h.def))
                    {
                        hediffRemovalBuffer.Add(h);
                    }
                }
                catch (Exception e)
                {
                    Log.Error($"[Zoology] CompDrugsImmune failed to remove hediff {h.def?.defName} from {pawn}: {e}");
                }
            }

            bool removedAny = false;
            for (int i = 0; i < hediffRemovalBuffer.Count; i++)
            {
                try
                {
                    pawn.health.RemoveHediff(hediffRemovalBuffer[i]);
                    removedAny = true;
                }
                catch (Exception e)
                {
                    Log.Error($"[Zoology] CompDrugsImmune failed to remove buffered hediff from {pawn}: {e}");
                }
            }

            hediffRemovalBuffer.Clear();

            if (removedAny)
            {
                if (Prefs.DevMode)
                {
                    Log.Message($"[Zoology] CompDrugsImmune: removed drug/addiction hediffs from {pawn.LabelShortCap}");
                }
            }
        }
    }

    public static class DrugUtils
    {
        private static HashSet<HediffDef> cachedDrugOutcomeHediffs = null;
        private static HashSet<HediffDef> cachedExpandedSet = null;

        
        private static readonly string[] addictionBaseNames = new[] { "AddictionBase", "DrugToleranceBase" };

        
        
        
        public static HediffDef TryExtractHediffDefFromOutcomeDoer(object outcomeDoer)
        {
            if (outcomeDoer == null) return null;
            var t = outcomeDoer.GetType();

            try
            {
                
                var f = t.GetField("hediffDef", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null)
                {
                    var val = f.GetValue(outcomeDoer);
                    if (val is HediffDef hd1) return hd1;
                    
                    if (val is string s1)
                    {
                        var hd = DefDatabase<HediffDef>.GetNamedSilentFail(s1);
                        if (hd != null) return hd;
                    }
                }

                var p = t.GetProperty("hediffDef", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p != null)
                {
                    var val = p.GetValue(outcomeDoer);
                    if (val is HediffDef hd2) return hd2;
                    if (val is string s2)
                    {
                        var hd = DefDatabase<HediffDef>.GetNamedSilentFail(s2);
                        if (hd != null) return hd;
                    }
                }

                
                var f2 = t.GetField("hediff", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f2 != null)
                {
                    var val = f2.GetValue(outcomeDoer);
                    if (val is HediffDef hd3) return hd3;
                    if (val is string s3)
                    {
                        var hd = DefDatabase<HediffDef>.GetNamedSilentFail(s3);
                        if (hd != null) return hd;
                    }
                }

                var p2 = t.GetProperty("hediff", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p2 != null)
                {
                    var val = p2.GetValue(outcomeDoer);
                    if (val is HediffDef hd4) return hd4;
                    if (val is string s4)
                    {
                        var hd = DefDatabase<HediffDef>.GetNamedSilentFail(s4);
                        if (hd != null) return hd;
                    }
                }

                
                var any = t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                           .FirstOrDefault(fi => typeof(HediffDef).IsAssignableFrom(fi.FieldType));
                if (any != null) return any.GetValue(outcomeDoer) as HediffDef;

                
                var anyProp = t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                              .FirstOrDefault(pr => typeof(HediffDef).IsAssignableFrom(pr.PropertyType));
                if (anyProp != null) return anyProp.GetValue(outcomeDoer) as HediffDef;
            }
            catch
            {
                
            }

            return null;
        }

        
        
        
        public static HashSet<HediffDef> GetAllDrugOutcomeHediffs()
        {
            return new HashSet<HediffDef>(EnsureDrugOutcomeHediffs());
        }

        private static HashSet<HediffDef> EnsureDrugOutcomeHediffs()
        {
            if (cachedDrugOutcomeHediffs != null) return cachedDrugOutcomeHediffs;

            var result = new HashSet<HediffDef>();

            try
            {
                foreach (var td in DefDatabase<ThingDef>.AllDefsListForReading)
                {
                    if (td?.ingestible?.outcomeDoers == null) continue;
                    foreach (var od in td.ingestible.outcomeDoers)
                    {
                        if (od == null) continue;

                        
                        var hd = TryExtractHediffDefFromOutcomeDoer(od);
                        if (hd != null)
                        {
                            result.Add(hd);
                        }
                        else
                        {
                            
                            var odType = od.GetType();
                            if (IsOrInheritsFrom(odType, "IngestionOutcomeDoer_GiveHediff"))
                            {
                                Log.Message($"[Zoology] DrugUtils: couldn't find hediffDef on outcomeDoer {odType.FullName} (ThingDef: {td.defName})");
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error($"[Zoology] DrugUtils.GetAllDrugOutcomeHediffs exception: {e}");
            }

            cachedDrugOutcomeHediffs = result;
            return cachedDrugOutcomeHediffs;
        }

        
        
        
        public static HashSet<HediffDef> GetAllDrugOutcomeAndAddictionRelatedHediffs()
        {
            return new HashSet<HediffDef>(EnsureExpandedSet());
        }

        private static HashSet<HediffDef> EnsureExpandedSet()
        {
            if (cachedExpandedSet != null) return cachedExpandedSet;

            var result = new HashSet<HediffDef>(EnsureDrugOutcomeHediffs());

            try
            {
                var allHediffs = DefDatabase<HediffDef>.AllDefsListForReading;
                foreach (var hd in allHediffs)
                {
                    if (hd == null) continue;
                    if (IsHediffAddictionRelated(hd)) result.Add(hd);
                }
            }
            catch (Exception e)
            {
                Log.Error($"[Zoology] DrugUtils.GetAllDrugOutcomeAndAddictionRelatedHediffs exception: {e}");
            }

            cachedExpandedSet = result;
            return cachedExpandedSet;
        }

        
        
        
        
        
        
        
        private static bool IsHediffAddictionRelated(HediffDef candidate)
        {
            try
            {
                if (candidate == null) return false;

                
                var hc = candidate.hediffClass;
                if (hc != null)
                {
                    var hcn = hc.Name;
                    if (!string.IsNullOrEmpty(hcn) && (hcn.IndexOf("Addict", StringComparison.OrdinalIgnoreCase) >= 0 || hcn.IndexOf("Addiction", StringComparison.OrdinalIgnoreCase) >= 0))
                        return true;
                }

                
                if (!string.IsNullOrEmpty(candidate.defName) && (candidate.defName.IndexOf("addict", StringComparison.OrdinalIgnoreCase) >= 0 || candidate.defName.IndexOf("addiction", StringComparison.OrdinalIgnoreCase) >= 0))
                    return true;

                
                object needFieldVal = null;
                var hdType = candidate.GetType();
                var fi = hdType.GetField("chemicalNeed", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (fi != null) needFieldVal = fi.GetValue(candidate);
                else
                {
                    var pi = hdType.GetProperty("chemicalNeed", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (pi != null) needFieldVal = pi.GetValue(candidate);
                }

                if (needFieldVal != null) return true;
                
                if (needFieldVal is string sNeed && !string.IsNullOrEmpty(sNeed))
                {
                    var nd = DefDatabase<NeedDef>.GetNamedSilentFail(sNeed);
                    if (nd != null && IsNeedDescendantOf(nd, "DrugAddictionNeedBase")) return true;
                    
                    return true;
                }

                
                foreach (var baseName in addictionBaseNames)
                {
                    if (IsDescendantOf(candidate, baseName)) return true;
                }
            }
            catch
            {
                
            }

            return false;
        }

        private static bool IsDescendantOf(HediffDef candidate, string baseDefName)
        {
            try
            {
                if (candidate == null) return false;
                if (string.Equals(candidate.defName, baseDefName, StringComparison.Ordinal)) return true;

                var visited = new HashSet<string>();
                HediffDef current = candidate;

                while (current != null)
                {
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
                        break;
                    }

                    break;
                }
            }
            catch
            {
                
            }
            return false;
        }

        private static bool IsNeedDescendantOf(NeedDef candidate, string baseDefName)
        {
            try
            {
                if (candidate == null) return false;
                if (string.Equals(candidate.defName, baseDefName, StringComparison.Ordinal)) return true;

                var visited = new HashSet<string>();
                NeedDef current = candidate;

                while (current != null)
                {
                    if (visited.Contains(current.defName)) break;
                    visited.Add(current.defName);

                    if (string.Equals(current.defName, baseDefName, StringComparison.Ordinal)) return true;

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
                        var next = DefDatabase<NeedDef>.GetNamedSilentFail(parentName);
                        if (next != null) { current = next; continue; }
                        break;
                    }

                    break;
                }
            }
            catch
            {
                
            }
            return false;
        }

        private static bool IsOrInheritsFrom(Type type, string baseTypeName)
        {
            if (type == null) return false;
            while (type != null)
            {
                if (type.Name == baseTypeName) return true;
                type = type.BaseType;
            }
            return false;
        }

        
        
        
        public static void InvalidateCache()
        {
            cachedDrugOutcomeHediffs = null;
            cachedExpandedSet = null;
        }

        
        
        
        public static bool IsHediffFromDrug(HediffDef hediff)
        {
            if (hediff == null) return false;
            return EnsureDrugOutcomeHediffs().Contains(hediff);
        }

        
        
        
        public static bool IsHediffDrugOrAddiction(HediffDef hediff)
        {
            if (hediff == null) return false;

            try
            {
                return EnsureExpandedSet().Contains(hediff);
            }
            catch
            {
                
            }

            return false;
        }
    }

    [StaticConstructorOnStartup]
    public static class DrugsImmuneHarmonyInit
    {
        private static bool patched;

        static DrugsImmuneHarmonyInit()
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

                if (settings != null && !settings.EnableDrugsImmunePatch)
                {
                    return;
                }

                patched = true;
                var harmony = new Harmony("zoology.drugsimmune");

                MethodInfo targetMethod = null;
                try
                {
                    var baseType = typeof(IngestionOutcomeDoer);
                    if (baseType != null)
                    {
                        targetMethod = AccessTools.Method(baseType, "DoIngestionOutcome", new Type[] { typeof(Pawn), typeof(Thing), typeof(int) });
                    }
                }
                catch
                {
                    targetMethod = null;
                }

                if (targetMethod == null)
                {
                    Type doerType = AppDomain.CurrentDomain.GetAssemblies()
                        .SelectMany(a => a.GetTypesSafe())
                        .FirstOrDefault(t => t.Name == "IngestionOutcomeDoer_GiveHediff");

                    if (doerType != null)
                    {
                        targetMethod = AccessTools.Method(doerType, "DoIngestionOutcome")
                                       ?? doerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                                           .FirstOrDefault(m =>
                                           {
                                               var ps = m.GetParameters();
                                               return ps.Length >= 2 &&
                                                      (ps[0].ParameterType == typeof(Pawn) || ps[0].ParameterType.IsSubclassOf(typeof(Pawn))) &&
                                                      (ps[1].ParameterType == typeof(Thing) || ps[1].ParameterType.IsSubclassOf(typeof(Thing)));
                                           });
                    }
                    else
                    {
                        Log.Message("[Zoology] DrugsImmuneHarmonyInit: IngestionOutcomeDoer_GiveHediff type not found — ingestion blocking will be skipped.");
                    }
                }

                if (targetMethod == null)
                {
                    Log.Message("[Zoology] DrugsImmuneHarmonyInit: target method for IngestionOutcomeDoer_GiveHediff not found. Ingestion blocking skipped.");
                    return;
                }

                var prefix = new HarmonyMethod(typeof(DrugsImmuneHarmonyInit).GetMethod(nameof(DoerGiveHediff_Prefix), BindingFlags.Static | BindingFlags.NonPublic));
                harmony.Patch(targetMethod, prefix: prefix);
            }
            catch (Exception e)
            {
                Log.Error($"[Zoology] DrugsImmuneHarmonyInit failed to patch: {e}");
            }
        }

        public static void ResetPatchedState()
        {
            patched = false;
        }

        
        private static bool DoerGiveHediff_Prefix(object __instance, Pawn pawn, Thing ingested, int ingestedCount)
        {
            try
            {
                var settings = ZoologyModSettings.Instance;
                if (settings != null && !settings.EnableDrugsImmunePatch)
                {
                    return true;
                }

                if (pawn == null) return true;

                var comp = pawn.TryGetComp<CompDrugsImmune>();
                if (comp == null) return true;

                
                HediffDef candidate = null;

                
                candidate = DrugUtils.TryExtractHediffDefFromOutcomeDoer(__instance);

                if (candidate == null)
                {
                    
                    return true;
                }

                
                if (DrugUtils.IsHediffDrugOrAddiction(candidate))
                {
                    if (Prefs.DevMode)
                    {
                        Log.Message($"[Zoology] blocked drug/addiction hediff {candidate.defName} being given to {pawn.LabelShortCap} (CompDrugsImmune present).");
                    }
                    return false;
                }

                return true;
            }
            catch (Exception e)
            {
                Log.Error($"[Zoology] DoerGiveHediff_Prefix exception: {e}");
                return true;
            }
        }
    }

    internal static class ReflectionHelpers
    {
        public static IEnumerable<Type> GetTypesSafe(this Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(t => t != null);
            }
            catch
            {
                return Enumerable.Empty<Type>();
            }
        }
    }
}
