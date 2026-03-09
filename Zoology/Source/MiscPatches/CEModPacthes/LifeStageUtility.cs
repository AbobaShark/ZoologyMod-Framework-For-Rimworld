using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    public static class LifeStageUtility
    {
        private static bool warnedNoDefsOnce = false;
        private static readonly Dictionary<LifeStageDef, LifeStagePenetrationDef> stageCache = new Dictionary<LifeStageDef, LifeStagePenetrationDef>();
        private static readonly Dictionary<string, LifeStagePenetrationDef> exactDefNameCache = new Dictionary<string, LifeStagePenetrationDef>(StringComparer.Ordinal);
        private static readonly Dictionary<string, LifeStagePenetrationDef> ignoreCaseDefNameCache = new Dictionary<string, LifeStagePenetrationDef>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, LifeStagePenetrationDef> labelCache = new Dictionary<string, LifeStagePenetrationDef>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, LifeStagePenetrationDef> partialMatchCache = new Dictionary<string, LifeStagePenetrationDef>(StringComparer.OrdinalIgnoreCase);
        private static int cachedDefCount = -1;

        public static LifeStagePenetrationDef GetPenetrationDefForPawn(Pawn pawn)
        {
            if (pawn == null) return null;

            var stage = pawn.ageTracker?.CurLifeStage;
            if (stage == null) return null;

            var all = DefDatabase<LifeStagePenetrationDef>.AllDefsListForReading;
            if (all.Count == 0)
            {
                if (Prefs.DevMode && !warnedNoDefsOnce)
                {
                    warnedNoDefsOnce = true;
                    Log.Warning("[Zoology] No LifeStagePenetrationDef defs found in DefDatabase. Zoology will not replace life-stage penetration multipliers until XML defs are provided.");
                }

                return null;
            }

            EnsureCaches(all);
            if (stageCache.TryGetValue(stage, out var cached))
            {
                return cached;
            }

            LifeStagePenetrationDef result = null;
            string stageName = stage.defName;
            if (!string.IsNullOrEmpty(stageName))
            {
                if (!exactDefNameCache.TryGetValue(stageName, out result))
                {
                    ignoreCaseDefNameCache.TryGetValue(stageName, out result);
                }

                if (result == null)
                {
                    labelCache.TryGetValue(stageName, out result);
                }

                if (result == null && !partialMatchCache.TryGetValue(stageName, out result))
                {
                    result = FindPartialMatch(all, stageName);
                    partialMatchCache[stageName] = result;
                }
            }

            stageCache[stage] = result;
            return result;
        }

        private static void EnsureCaches(List<LifeStagePenetrationDef> defs)
        {
            if (cachedDefCount == defs.Count)
            {
                return;
            }

            stageCache.Clear();
            exactDefNameCache.Clear();
            ignoreCaseDefNameCache.Clear();
            labelCache.Clear();
            partialMatchCache.Clear();

            for (int i = 0; i < defs.Count; i++)
            {
                var def = defs[i];
                if (def == null)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(def.defName))
                {
                    exactDefNameCache[def.defName] = def;
                    if (!ignoreCaseDefNameCache.ContainsKey(def.defName))
                    {
                        ignoreCaseDefNameCache[def.defName] = def;
                    }
                }

                if (!string.IsNullOrEmpty(def.label) && !labelCache.ContainsKey(def.label))
                {
                    labelCache[def.label] = def;
                }
            }

            cachedDefCount = defs.Count;
        }

        private static LifeStagePenetrationDef FindPartialMatch(List<LifeStagePenetrationDef> defs, string stageName)
        {
            for (int i = 0; i < defs.Count; i++)
            {
                var def = defs[i];
                string defName = def?.defName;
                if (string.IsNullOrEmpty(defName))
                {
                    continue;
                }

                if (stageName.IndexOf(defName, StringComparison.OrdinalIgnoreCase) >= 0
                    || defName.IndexOf(stageName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return def;
                }
            }

            return null;
        }
    }
}
