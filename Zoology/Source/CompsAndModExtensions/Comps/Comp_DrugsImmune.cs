// Comp_DrugsImmune.cs

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
        // Через сколько тиков проверять (по-умолчанию 2000)
        public int cleanupIntervalTicks = 2000;

        public CompProperties_DrugsImmune()
        {
            this.compClass = typeof(CompDrugsImmune);
        }
    }

    public class CompDrugsImmune : ThingComp
    {
        private CompProperties_DrugsImmune PropsDrugsImmune => (CompProperties_DrugsImmune)props;

        public override void CompTick()
        {
            Pawn pawn = parent as Pawn;
            if (pawn == null) return;
            if (pawn.Destroyed || pawn.Dead) return;
            if (pawn.health?.hediffSet == null) return;

            int interval = Math.Max(60, PropsDrugsImmune?.cleanupIntervalTicks ?? 2000);
            if (!parent.IsHashIntervalTick(interval)) return;

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

            // Копия списка для безопасной итерации
            var hs = pawn.health.hediffSet.hediffs.ToList();

            bool removedAny = false;
            foreach (var h in hs)
            {
                if (h == null || h.def == null) continue;

                try
                {
                    // Удаляем, если это явно связанный с препаратами hediff (outcome or addiction)
                    if (DrugUtils.IsHediffDrugOrAddiction(h.def))
                    {
                        pawn.health.RemoveHediff(h);
                        removedAny = true;
                    }
                }
                catch (Exception e)
                {
                    Log.Error($"[Zoology] CompDrugsImmune failed to remove hediff {h.def?.defName} from {pawn}: {e}");
                }
            }

            if (removedAny)
            {
                Log.Message($"[Zoology] CompDrugsImmune: removed drug/addiction hediffs from {pawn.LabelShortCap}");
            }
        }
    }

    public static class DrugUtils
    {
        private static HashSet<HediffDef> cachedDrugOutcomeHediffs = null;
        private static HashSet<HediffDef> cachedExpandedSet = null;

        // Быстрые константы-эвристики
        private static readonly string[] addictionBaseNames = new[] { "AddictionBase", "DrugToleranceBase" };

        /// <summary>
        /// Попытка извлечь HediffDef из любого outcomeDoer: поле/propery hediffDef, field/property hediff (string), или любое поле типа HediffDef.
        /// </summary>
        public static HediffDef TryExtractHediffDefFromOutcomeDoer(object outcomeDoer)
        {
            if (outcomeDoer == null) return null;
            var t = outcomeDoer.GetType();

            try
            {
                // 1) поле/property hediffDef (HediffDef)
                var f = t.GetField("hediffDef", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null)
                {
                    var val = f.GetValue(outcomeDoer);
                    if (val is HediffDef hd1) return hd1;
                    // иногда может быть строка
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

                // 2) поле/property "hediff" (часто у старых реализаций это string)
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

                // 3) любой field типа HediffDef
                var any = t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                           .FirstOrDefault(fi => typeof(HediffDef).IsAssignableFrom(fi.FieldType));
                if (any != null) return any.GetValue(outcomeDoer) as HediffDef;

                // 4) любой property типа HediffDef
                var anyProp = t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                              .FirstOrDefault(pr => typeof(HediffDef).IsAssignableFrom(pr.PropertyType));
                if (anyProp != null) return anyProp.GetValue(outcomeDoer) as HediffDef;
            }
            catch
            {
                // swallow - best effort
            }

            return null;
        }

        /// <summary>
        /// Возвращает набор HediffDef, которые даются непосредственно outcomeDoer-ами у ingestible.
        /// </summary>
        public static HashSet<HediffDef> GetAllDrugOutcomeHediffs()
        {
            if (cachedDrugOutcomeHediffs != null) return new HashSet<HediffDef>(cachedDrugOutcomeHediffs);

            var result = new HashSet<HediffDef>();

            try
            {
                foreach (var td in DefDatabase<ThingDef>.AllDefsListForReading)
                {
                    if (td?.ingestible?.outcomeDoers == null) continue;
                    foreach (var od in td.ingestible.outcomeDoers)
                    {
                        if (od == null) continue;

                        // Пытаемся извлечь HediffDef
                        var hd = TryExtractHediffDefFromOutcomeDoer(od);
                        if (hd != null)
                        {
                            result.Add(hd);
                        }
                        else
                        {
                            // Если outcomeDoer имеет имя типа "GiveHediff", всё равно логируем один раз для отладки
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

            cachedDrugOutcomeHediffs = new HashSet<HediffDef>(result);
            return new HashSet<HediffDef>(cachedDrugOutcomeHediffs);
        }

        /// <summary>
        /// Возвращает расширенный набор: hediff'ы из outcome + явно связанные с зависимостью (addiction-related).
        /// </summary>
        public static HashSet<HediffDef> GetAllDrugOutcomeAndAddictionRelatedHediffs()
        {
            if (cachedExpandedSet != null) return new HashSet<HediffDef>(cachedExpandedSet);

            var result = new HashSet<HediffDef>(GetAllDrugOutcomeHediffs());

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

            cachedExpandedSet = new HashSet<HediffDef>(result);
            return new HashSet<HediffDef>(cachedExpandedSet);
        }

        /// <summary>
        /// Эвристики: считается связанным с зависимостью, если:
        /// - имя класса hediff содержит "Addict" или "Addiction"
        /// - defName содержит "addict" / "addiction"
        /// - имеет chemicalNeed (NeedDef либо строка)
        /// - является потомком AddictionBase или DrugToleranceBase
        /// </summary>
        private static bool IsHediffAddictionRelated(HediffDef candidate)
        {
            try
            {
                if (candidate == null) return false;

                // 1) Hediff class name
                var hc = candidate.hediffClass;
                if (hc != null)
                {
                    var hcn = hc.Name;
                    if (!string.IsNullOrEmpty(hcn) && (hcn.IndexOf("Addict", StringComparison.OrdinalIgnoreCase) >= 0 || hcn.IndexOf("Addiction", StringComparison.OrdinalIgnoreCase) >= 0))
                        return true;
                }

                // 2) defName heuristic
                if (!string.IsNullOrEmpty(candidate.defName) && (candidate.defName.IndexOf("addict", StringComparison.OrdinalIgnoreCase) >= 0 || candidate.defName.IndexOf("addiction", StringComparison.OrdinalIgnoreCase) >= 0))
                    return true;

                // 3) chemicalNeed field/property (может быть NeedDef или string)
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
                // если это string имя
                if (needFieldVal is string sNeed && !string.IsNullOrEmpty(sNeed))
                {
                    var nd = DefDatabase<NeedDef>.GetNamedSilentFail(sNeed);
                    if (nd != null && IsNeedDescendantOf(nd, "DrugAddictionNeedBase")) return true;
                    // даже если NeedDef не найден, наличие chemicalNeed-имени даёт вескую догадку, поэтому считаем true
                    return true;
                }

                // 4) parent/ParentName и т.п.
                foreach (var baseName in addictionBaseNames)
                {
                    if (IsDescendantOf(candidate, baseName)) return true;
                }
            }
            catch
            {
                // best-effort
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

                    // Попытка получить parent field (как HediffDef)
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

                    // Попробуем parentName / ParentName
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
                // swallow
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
                // swallow
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

        /// <summary>
        /// Инвалидировать кешы (если defs поменялись динамически)
        /// </summary>
        public static void InvalidateCache()
        {
            cachedDrugOutcomeHediffs = null;
            cachedExpandedSet = null;
        }

        /// <summary>
        /// Быстрая проверка - является ли hediff результатом drug outcome (исключая addiction heuristics).
        /// </summary>
        public static bool IsHediffFromDrug(HediffDef hediff)
        {
            if (hediff == null) return false;
            var set = GetAllDrugOutcomeHediffs();
            return set.Contains(hediff);
        }

        /// <summary>
        /// Скомбинированная проверка: исходный outcome OR addiction-related (с использованием эвристик).
        /// </summary>
        public static bool IsHediffDrugOrAddiction(HediffDef hediff)
        {
            if (hediff == null) return false;

            try
            {
                if (IsHediffFromDrug(hediff)) return true;
                if (IsHediffAddictionRelated(hediff)) return true;
            }
            catch
            {
                // swallow
            }

            return false;
        }
    }

    [StaticConstructorOnStartup]
    public static class DrugsImmuneHarmonyInit
    {
        static DrugsImmuneHarmonyInit()
        {
            try
            {
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

                Log.Message("[Zoology] DrugsImmuneHarmonyInit: patched IngestionOutcomeDoer.DoIngestionOutcome (drug hediff blocking enabled).");
            }
            catch (Exception e)
            {
                Log.Error($"[Zoology] DrugsImmuneHarmonyInit failed to patch: {e}");
            }
        }

        // Префикс: если outcomeDoer даёт hediff, и Pawn имеет CompDrugsImmune, блокируем.
        private static bool DoerGiveHediff_Prefix(object __instance, Pawn pawn, Thing ingested, int ingestedCount)
        {
            try
            {
                if (pawn == null) return true;

                var comp = pawn.TryGetComp<CompDrugsImmune>();
                if (comp == null) return true;

                // Пытаемся получить HediffDef от outcomeDoer
                HediffDef candidate = null;

                // reuse utility
                candidate = DrugUtils.TryExtractHediffDefFromOutcomeDoer(__instance);

                if (candidate == null)
                {
                    // ничего не нашли — безопасно не блокировать, т.к. не знаем что даётся
                    return true;
                }

                // Если этот hediff явно относится к препаратам/зависимости — блокируем
                if (DrugUtils.IsHediffDrugOrAddiction(candidate))
                {
                    Log.Message($"[Zoology] blocked drug/addiction hediff {candidate.defName} being given to {pawn.LabelShortCap} (CompDrugsImmune present).");
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