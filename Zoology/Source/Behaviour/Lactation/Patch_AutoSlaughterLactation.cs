using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using UnityEngine;
using Verse.Sound;

namespace ZoologyMod
{
    [HarmonyPatch(typeof(AutoSlaughterManager), "get_AnimalsToSlaughter")]
    static class Patch_AutoSlaughterManager_GetAnimalsToSlaughter
    {
        static bool Prepare() => ZoologyModSettings.EnableMammalLactation;

        static bool Prefix(AutoSlaughterManager __instance, ref List<Pawn> __result)
        {
            try
            {
                if (!ZoologyModSettings.EnableMammalLactation)
                    return true;

                if (__instance == null || __instance.map == null)
                    return true;

                var settings = ZoologyModSettings.Instance;
                var mode = settings?.LactationSlaughterHandling ?? ZoologyModSettings.LactationSlaughterMode.TreatAsPregnant;
                var lactDef = AnimalChildcareUtility.LactatingHediffDef;

                List<Pawn> tmpAnimals = new List<Pawn>();
                List<Pawn> tmpAnimalsMale = new List<Pawn>();
                List<Pawn> tmpAnimalsMaleYoung = new List<Pawn>();
                List<Pawn> tmpAnimalsFemale = new List<Pawn>();
                List<Pawn> tmpAnimalsFemaleYoung = new List<Pawn>();
                List<Pawn> tmpAnimalsPregnant = new List<Pawn>();

                List<Pawn> animalsToSlaughterCachedLocal = new List<Pawn>();

                var configs = __instance.configs;
                var map = __instance.map;
                var spawnedColonyAnimals = map.mapPawns.SpawnedColonyAnimals;

                foreach (AutoSlaughterConfig autoSlaughterConfig in configs)
                {
                    if (!autoSlaughterConfig.AnyLimit)
                        continue;

                    tmpAnimals.Clear();
                    tmpAnimalsMale.Clear();
                    tmpAnimalsMaleYoung.Clear();
                    tmpAnimalsFemale.Clear();
                    tmpAnimalsFemaleYoung.Clear();
                    tmpAnimalsPregnant.Clear();

                    foreach (Pawn pawn in spawnedColonyAnimals)
                    {
                        if (pawn.def != autoSlaughterConfig.animal) continue;
                        if (!AutoSlaughterManager.CanAutoSlaughterNow(pawn)) continue;
                        if (!autoSlaughterConfig.allowSlaughterBonded && pawn.relations.GetDirectRelationsCount(PawnRelationDefOf.Bond, null) > 0) continue;

                        if (pawn.gender == Gender.Male)
                        {
                            if (pawn.ageTracker.CurLifeStage.reproductive)
                                tmpAnimalsMale.Add(pawn);
                            else
                                tmpAnimalsMaleYoung.Add(pawn);
                            tmpAnimals.Add(pawn);
                        }
                        else if (pawn.gender == Gender.Female)
                        {
                            if (pawn.ageTracker.CurLifeStage.reproductive)
                            {
                                bool isPreg = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.Pregnant, false) != null;
                                bool isLact = (lactDef != null && pawn.health.hediffSet.HasHediff(lactDef, false));

                                if (!isPreg && !isLact)
                                {
                                    tmpAnimalsFemale.Add(pawn);
                                    tmpAnimals.Add(pawn);
                                }
                                else
                                {
                                    
                                    bool allowByPreg = true;
                                    bool allowByLact = true;

                                    if (isPreg)
                                    {
                                        allowByPreg = autoSlaughterConfig.allowSlaughterPregnant;
                                    }

                                    if (isLact)
                                    {
                                        switch (mode)
                                        {
                                            case ZoologyModSettings.LactationSlaughterMode.TreatAsPregnant:
                                                allowByLact = autoSlaughterConfig.allowSlaughterPregnant;
                                                break;
                                            case ZoologyModSettings.LactationSlaughterMode.SeparateSetting:
                                                allowByLact = settings != null && settings.GetAllowSlaughterLactatingFor(autoSlaughterConfig.animal);
                                                break;
                                            case ZoologyModSettings.LactationSlaughterMode.Ignore:
                                                allowByLact = true;
                                                break;
                                            case ZoologyModSettings.LactationSlaughterMode.DisableSlaughterLactatingGlobal:
                                                allowByLact = false;
                                                break;
                                            default:
                                                allowByLact = true;
                                                break;
                                        }
                                    }

                                    
                                    bool allowedOverall = true;
                                    if (isPreg && !allowByPreg) allowedOverall = false;
                                    if (isLact && !allowByLact) allowedOverall = false;

                                    if (!allowedOverall)
                                    {
                                        
                                        continue;
                                    }

                                    
                                    
                                    
                                    tmpAnimalsFemale.Add(pawn);
                                    tmpAnimals.Add(pawn);

                                    if (isPreg)
                                    {
                                        tmpAnimalsPregnant.Add(pawn);
                                    }
                                    else if (isLact)
                                    {
                                        
                                        if (mode == ZoologyModSettings.LactationSlaughterMode.TreatAsPregnant)
                                        {
                                            tmpAnimalsPregnant.Add(pawn);
                                        }
                                        else if (mode == ZoologyModSettings.LactationSlaughterMode.SeparateSetting)
                                        {
                                            
                                            
                                            tmpAnimalsPregnant.Add(pawn);
                                        }
                                        else if (mode == ZoologyModSettings.LactationSlaughterMode.Ignore)
                                        {
                                            
                                        }
                                        
                                    }
                                }
                            }
                            else
                            {
                                tmpAnimalsFemaleYoung.Add(pawn);
                                tmpAnimals.Add(pawn);
                            }
                        }
                        else
                        {
                            tmpAnimals.Add(pawn);
                        }
                    } 

                    tmpAnimals.SortByDescending((Pawn a) => a.ageTracker.AgeBiologicalTicks);
                    tmpAnimalsMale.SortByDescending((Pawn a) => a.ageTracker.AgeBiologicalTicks);
                    tmpAnimalsMaleYoung.SortByDescending((Pawn a) => a.ageTracker.AgeBiologicalTicks);
                    tmpAnimalsFemale.SortByDescending((Pawn a) => a.ageTracker.AgeBiologicalTicks);
                    tmpAnimalsFemaleYoung.SortByDescending((Pawn a) => a.ageTracker.AgeBiologicalTicks);

                    if (tmpAnimalsPregnant.Count > 0)
                    {
                        tmpAnimalsPregnant.SortByDescending((Pawn a) =>
                        {
                            var hd = a.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.Pregnant, false);
                            return hd?.Severity ?? 0f;
                        });
                        
                        
                        
                        

                        
                        foreach (var p in tmpAnimalsPregnant)
                        {
                            if (tmpAnimalsFemale.Remove(p))
                            {
                                
                            }
                        }
                        
                        tmpAnimalsFemale.InsertRange(0, tmpAnimalsPregnant);
                        foreach (var p in tmpAnimalsPregnant)
                        {
                            if (tmpAnimals.Remove(p))
                            {
                                
                            }
                        }
                        tmpAnimals.InsertRange(0, tmpAnimalsPregnant);
                    }

                    
                    if (autoSlaughterConfig.maxFemales != -1)
                    {
                        while (tmpAnimalsFemale.Count > autoSlaughterConfig.maxFemales)
                        {
                            Pawn item = tmpAnimalsFemale.PopFront();
                            tmpAnimals.Remove(item);
                            animalsToSlaughterCachedLocal.Add(item);
                        }
                    }
                    if (autoSlaughterConfig.maxFemalesYoung != -1)
                    {
                        while (tmpAnimalsFemaleYoung.Count > autoSlaughterConfig.maxFemalesYoung)
                        {
                            Pawn item2 = tmpAnimalsFemaleYoung.PopFront();
                            tmpAnimals.Remove(item2);
                            animalsToSlaughterCachedLocal.Add(item2);
                        }
                    }
                    if (autoSlaughterConfig.maxMales != -1)
                    {
                        while (tmpAnimalsMale.Count > autoSlaughterConfig.maxMales)
                        {
                            Pawn item3 = tmpAnimalsMale.PopFront();
                            tmpAnimals.Remove(item3);
                            animalsToSlaughterCachedLocal.Add(item3);
                        }
                    }
                    if (autoSlaughterConfig.maxMalesYoung != -1)
                    {
                        while (tmpAnimalsMaleYoung.Count > autoSlaughterConfig.maxMalesYoung)
                        {
                            Pawn item4 = tmpAnimalsMaleYoung.PopFront();
                            tmpAnimals.Remove(item4);
                            animalsToSlaughterCachedLocal.Add(item4);
                        }
                    }
                    if (autoSlaughterConfig.maxTotal != -1)
                    {
                        while (tmpAnimals.Count > autoSlaughterConfig.maxTotal)
                        {
                            Pawn pawn2 = tmpAnimals.PopFront();
                            if (pawn2.gender == Gender.Male)
                            {
                                if (pawn2.ageTracker.CurLifeStage.reproductive)
                                    tmpAnimalsMale.Remove(pawn2);
                                else
                                    tmpAnimalsMaleYoung.Remove(pawn2);
                            }
                            else if (pawn2.gender == Gender.Female)
                            {
                                if (pawn2.ageTracker.CurLifeStage.reproductive)
                                    tmpAnimalsFemale.Remove(pawn2);
                                else
                                    tmpAnimalsFemaleYoung.Remove(pawn2);
                            }
                            animalsToSlaughterCachedLocal.Add(pawn2);
                        }
                    }
                } 

                __result = animalsToSlaughterCachedLocal;
                return false; 
            }
            catch (Exception ex)
            {
                Log.Error($"[Zoology] Patch_AutoSlaughterManager_GetAnimalsToSlaughter Prefix failed: {ex}");
                return true;
            }
        }
    }

    
    static class DialogAS_Helper
    {
        public static FieldInfo f_animalCounts = typeof(RimWorld.Dialog_AutoSlaughter).GetField("animalCounts", BindingFlags.Instance | BindingFlags.NonPublic);
        public static FieldInfo f_configsOrdered = typeof(RimWorld.Dialog_AutoSlaughter).GetField("configsOrdered", BindingFlags.Instance | BindingFlags.NonPublic);
        public static FieldInfo f_map = typeof(RimWorld.Dialog_AutoSlaughter).GetField("map", BindingFlags.Instance | BindingFlags.NonPublic);
        public static MethodInfo m_recalculateAnimals = typeof(RimWorld.Dialog_AutoSlaughter).GetMethod("RecalculateAnimals", BindingFlags.Instance | BindingFlags.NonPublic);
        public static readonly MethodInfo m_calculateLabelWidth = typeof(RimWorld.Dialog_AutoSlaughter).GetMethod("CalculateLabelWidth", BindingFlags.Instance | BindingFlags.NonPublic);
        public static readonly FieldInfo f_tmpMouseoverHighlightRects = typeof(RimWorld.Dialog_AutoSlaughter).GetField("tmpMouseoverHighlightRects", BindingFlags.Instance | BindingFlags.NonPublic);
        public static readonly FieldInfo f_tmpGroupRects = typeof(RimWorld.Dialog_AutoSlaughter).GetField("tmpGroupRects", BindingFlags.Instance | BindingFlags.NonPublic);

        public static readonly Type dialogType = typeof(RimWorld.Dialog_AutoSlaughter);
        public static readonly Type animalCountRecordType = dialogType.GetNestedType("AnimalCountRecord", BindingFlags.Instance | BindingFlags.NonPublic);
        public static readonly Type dictAnimalCountType = animalCountRecordType != null
            ? typeof(Dictionary<,>).MakeGenericType(typeof(ThingDef), animalCountRecordType)
            : null;

        public static readonly ConstructorInfo animalCountCtor = animalCountRecordType != null
            ? animalCountRecordType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                new Type[] { typeof(int), typeof(int), typeof(int), typeof(int), typeof(int), typeof(int), typeof(int) }, null)
            : null;

        public static readonly MethodInfo dictAddMethod = dictAnimalCountType != null
            ? dictAnimalCountType.GetMethod("Add", new Type[] { typeof(ThingDef), animalCountRecordType })
            : null;

        public static readonly MethodInfo dictTryGetValue = dictAnimalCountType != null
            ? dictAnimalCountType.GetMethod("TryGetValue")
            : null;

        public static readonly FieldInfo fi_total = animalCountRecordType != null ? animalCountRecordType.GetField("total", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) : null;
        public static readonly FieldInfo fi_male = animalCountRecordType != null ? animalCountRecordType.GetField("male", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) : null;
        public static readonly FieldInfo fi_maleYoung = animalCountRecordType != null ? animalCountRecordType.GetField("maleYoung", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) : null;
        public static readonly FieldInfo fi_female = animalCountRecordType != null ? animalCountRecordType.GetField("female", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) : null;
        public static readonly FieldInfo fi_femaleYoung = animalCountRecordType != null ? animalCountRecordType.GetField("femaleYoung", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) : null;
        public static readonly FieldInfo fi_pregnant = animalCountRecordType != null ? animalCountRecordType.GetField("pregnant", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) : null;
        public static readonly FieldInfo fi_bonded = animalCountRecordType != null ? animalCountRecordType.GetField("bonded", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) : null;

        public static Dictionary<ThingDef, int> lactatingCounts = new Dictionary<ThingDef, int>();

        public static int GetTotalFromDict(object dictObj, ThingDef def)
        {
            if (dictObj == null || dictTryGetValue == null) return 0;
            object[] pr = new object[] { def, null };
            bool found = (bool)dictTryGetValue.Invoke(dictObj, pr);
            if (!found) return 0;
            object rec = pr[1];
            if (rec == null || fi_total == null) return 0;
            return (int)(fi_total.GetValue(rec) ?? 0);
        }

        public static float GetLabelWidth(RimWorld.Dialog_AutoSlaughter dialog, Rect rect)
        {
            object widthObj = m_calculateLabelWidth?.Invoke(dialog, new object[] { rect });
            return (widthObj is float wf) ? wf : (rect.width - 24f - 4f - 4f - 64f * 7f - 420f - 32f);
        }
    }

    [HarmonyPatch(typeof(RimWorld.Dialog_AutoSlaughter), "RecalculateAnimals")]
    static class Patch_Dialog_AutoSlaughter_Recalculate
    {
        static bool Prepare() => ZoologyModSettings.EnableMammalLactation;

        static bool Prefix(RimWorld.Dialog_AutoSlaughter __instance)
        {
            try
            {
                var settings = ZoologyModSettings.Instance;
                var mode = settings?.LactationSlaughterHandling ?? ZoologyModSettings.LactationSlaughterMode.TreatAsPregnant;
                
                if (mode == ZoologyModSettings.LactationSlaughterMode.Ignore)
                    return true;

                var map = (Map)DialogAS_Helper.f_map.GetValue(__instance);
                var manager = map?.autoSlaughterManager;
                if (manager == null)
                    return true;

                DialogAS_Helper.lactatingCounts.Clear();

                if (DialogAS_Helper.dictAnimalCountType == null || DialogAS_Helper.animalCountCtor == null || DialogAS_Helper.dictAddMethod == null || DialogAS_Helper.dictTryGetValue == null)
                {
                    return true;
                }

                object localAnimalCountsObj = Activator.CreateInstance(DialogAS_Helper.dictAnimalCountType);
                var lactDef = AnimalChildcareUtility.LactatingHediffDef;
                var spawnedColonyAnimals = map.mapPawns.SpawnedColonyAnimals;

                foreach (AutoSlaughterConfig config in manager.configs)
                {
                    var def = config.animal;
                    int male = 0, maleYoung = 0, female = 0, femaleYoung = 0, total = 0, pregnant = 0, bonded = 0;
                    int lactating = 0;
                    bool allowSlaughterLactating = settings != null && settings.GetAllowSlaughterLactatingFor(def);

                    foreach (Pawn pawn in spawnedColonyAnimals)
                    {
                        if (pawn.def != def || !AutoSlaughterManager.CanEverAutoSlaughter(pawn)) continue;

                        if (pawn.relations.GetDirectRelationsCount(PawnRelationDefOf.Bond, null) > 0)
                        {
                            bonded++;
                            if (!config.allowSlaughterBonded)
                                continue;
                        }

                        if (pawn.gender == Gender.Male)
                        {
                            if (pawn.ageTracker.CurLifeStage.reproductive) male++;
                            else maleYoung++;
                            total++;
                        }
                        else if (pawn.gender == Gender.Female)
                        {
                            if (pawn.ageTracker.CurLifeStage.reproductive)
                            {
                                Hediff firstPreg = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.Pregnant, false);
                                bool isLact = (lactDef != null && pawn.health.hediffSet.HasHediff(lactDef, false));

                                if (isLact)
                                    lactating++;

                                
                                if (firstPreg == null && !isLact)
                                {
                                    female++;
                                    total++;
                                }
                                else
                                {
                                    
                                    bool allowByPreg = true;
                                    bool allowByLact = true;

                                    if (firstPreg != null && firstPreg.Visible)
                                    {
                                        pregnant++;
                                        allowByPreg = config.allowSlaughterPregnant;
                                    }

                                    if (isLact)
                                    {
                                        switch (mode)
                                        {
                                            case ZoologyModSettings.LactationSlaughterMode.TreatAsPregnant:
                                                allowByLact = config.allowSlaughterPregnant;
                                                break;
                                            case ZoologyModSettings.LactationSlaughterMode.SeparateSetting:
                                                allowByLact = allowSlaughterLactating;
                                                break;
                                            case ZoologyModSettings.LactationSlaughterMode.Ignore:
                                                allowByLact = true;
                                                break;
                                            case ZoologyModSettings.LactationSlaughterMode.DisableSlaughterLactatingGlobal:
                                                allowByLact = false;
                                                break;
                                            default:
                                                allowByLact = true;
                                                break;
                                        }
                                    }

                                    
                                    bool allowedOverall = true;
                                    if (firstPreg != null && firstPreg.Visible && !allowByPreg) allowedOverall = false;
                                    if (isLact && !allowByLact) allowedOverall = false;

                                    if (!allowedOverall)
                                        continue;

                                    
                                    female++;
                                    total++;
                                }
                            }
                            else
                            {
                                femaleYoung++;
                                total++;
                            }
                        }
                        else
                        {
                            total++;
                        }
                    }

                    object recObj = DialogAS_Helper.animalCountCtor.Invoke(new object[] { total, male, maleYoung, female, femaleYoung, pregnant, bonded });
                    DialogAS_Helper.dictAddMethod.Invoke(localAnimalCountsObj, new object[] { def, recObj });

                    DialogAS_Helper.lactatingCounts[def] = lactating;
                }

                DialogAS_Helper.f_animalCounts.SetValue(__instance, localAnimalCountsObj);

                var configsOrdered = new List<AutoSlaughterConfig>(manager.configs);
                configsOrdered.Sort((a, b) =>
                {
                    int totalCompare = DialogAS_Helper.GetTotalFromDict(localAnimalCountsObj, b.animal)
                        .CompareTo(DialogAS_Helper.GetTotalFromDict(localAnimalCountsObj, a.animal));
                    if (totalCompare != 0) return totalCompare;
                    return string.Compare(a.animal.label, b.animal.label, StringComparison.CurrentCulture);
                });
                DialogAS_Helper.f_configsOrdered.SetValue(__instance, configsOrdered);

                return false; 
            }
            catch (Exception ex)
            {
                Log.Error($"[Zoology] Dialog_AutoSlaughter.RecalculateAnimals Prefix failed: {ex}");
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(RimWorld.Dialog_AutoSlaughter), "DoAnimalHeader")]
    static class Patch_Dialog_AutoSlaughter_DoAnimalHeader
    {
        static bool Prepare() => ZoologyModSettings.EnableMammalLactation;

        static bool Prefix(RimWorld.Dialog_AutoSlaughter __instance, Rect rect1, Rect rect2)
        {
            try
            {
                var settings = ZoologyModSettings.Instance;
                if (settings == null || settings.LactationSlaughterHandling != ZoologyModSettings.LactationSlaughterMode.SeparateSetting)
                    return true;

                float widthBase = DialogAS_Helper.GetLabelWidth(__instance, rect1);
                
                float width = widthBase - 64f;

                Widgets.BeginGroup(new Rect(rect1.x, rect1.y, rect1.width, rect1.height + rect2.height + 1f));
                var tmpMouseoverList = DialogAS_Helper.f_tmpMouseoverHighlightRects?.GetValue(__instance) as List<Rect>;
                var tmpGroupRectsList = DialogAS_Helper.f_tmpGroupRects?.GetValue(__instance) as List<Rect>;
                if (tmpMouseoverList != null && tmpGroupRectsList != null)
                {
                    tmpMouseoverList.Clear();
                    tmpGroupRectsList.Clear();
                }
                Widgets.EndGroup();

                Widgets.BeginGroup(rect1);
                var row = new WidgetRow(0f, 0f, UIDirection.RightThenUp, 99999f, 4f);
                TextAnchor anchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleCenter;
                row.Label(string.Empty, 24f, null, -1f);
                float startX = row.FinalX;
                row.Label(string.Empty, width, "AutoSlaugtherHeaderTooltipLabel".Translate(), -1f);
                Rect item = new Rect(startX, rect1.height, row.FinalX - startX, rect2.height);

                var tmpMouseoverList2 = DialogAS_Helper.f_tmpMouseoverHighlightRects?.GetValue(__instance) as List<Rect>;
                var tmpGroupRectsList2 = DialogAS_Helper.f_tmpGroupRects?.GetValue(__instance) as List<Rect>;
                if (tmpMouseoverList2 != null && tmpGroupRectsList2 != null)
                {
                    tmpMouseoverList2.Add(item);
                    tmpGroupRectsList2.Add(item);
                }

                Action<string, float, float> AddCurrentAndMaxEntries = (string headerKey, float extraWidthFirst, float extraWidthSecond) =>
                {
                    float start = row.FinalX;
                    row.Label(string.Empty, 60f + extraWidthFirst, null, -1f);
                    tmpMouseoverList2.Add(new Rect(start, rect1.height, row.FinalX - start, rect2.height));
                    float prev = row.FinalX;
                    row.Label(string.Empty, 56f + extraWidthSecond, null, -1f);
                    tmpMouseoverList2.Add(new Rect(prev, rect1.height, row.FinalX - prev, rect2.height));
                    Rect r = new Rect(start, 0f, row.FinalX - start, rect2.height);
                    Widgets.Label(r, headerKey.Translate());
                    tmpGroupRectsList2.Add(r);
                };

                AddCurrentAndMaxEntries("AutoSlaugtherHeaderColTotal", 0f, 0f);
                AddCurrentAndMaxEntries("AnimalMaleAdult", 0f, 0f);
                AddCurrentAndMaxEntries("AnimalMaleYoung", 0f, 0f);
                AddCurrentAndMaxEntries("AnimalFemaleAdult", 0f, 0f);
                AddCurrentAndMaxEntries("AnimalFemaleYoung", 0f, 0f);
                
                AddCurrentAndMaxEntries("AnimalPregnant", 0f, 16f);
                AddCurrentAndMaxEntries("AnimalBonded", 0f, 16f);

                
                AddCurrentAndMaxEntries("AnimalLactating", 0f, 16f);

                Text.Anchor = anchor;
                Widgets.EndGroup();

                Widgets.BeginGroup(rect2);
                var row2 = new WidgetRow(0f, 0f, UIDirection.RightThenUp, 99999f, 4f);
                TextAnchor anchor2 = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleCenter;
                row2.Label(string.Empty, 24f, null, -1f);
                row2.Label("AutoSlaugtherHeaderColLabel".Translate(), width, "AutoSlaugtherHeaderTooltipLabel".Translate(), -1f);
                row2.Label("AutoSlaugtherHeaderColCurrent".Translate(), 60f, "AutoSlaugtherHeaderTooltipCurrentTotal".Translate(), -1f);
                row2.Label("AutoSlaugtherHeaderColMax".Translate(), 56f, "AutoSlaugtherHeaderTooltipMaxTotal".Translate(), -1f);
                row2.Label("AutoSlaugtherHeaderColCurrent".Translate(), 60f, "AutoSlaugtherHeaderTooltipCurrentMales".Translate(), -1f);
                row2.Label("AutoSlaugtherHeaderColMax".Translate(), 56f, "AutoSlaugtherHeaderTooltipMaxMales".Translate(), -1f);
                row2.Label("AutoSlaugtherHeaderColCurrent".Translate(), 60f, "AutoSlaughterHeaderTooltipCurrentMalesYoung".Translate(), -1f);
                row2.Label("AutoSlaugtherHeaderColMax".Translate(), 56f, "AutoSlaughterHeaderTooltipMaxMalesYoung".Translate(), -1f);
                row2.Label("AutoSlaugtherHeaderColCurrent".Translate(), 60f, "AutoSlaugtherHeaderTooltipCurrentFemales".Translate(), -1f);
                row2.Label("AutoSlaugtherHeaderColMax".Translate(), 56f, "AutoSlaugtherHeaderTooltipMaxFemales".Translate(), -1f);
                row2.Label("AutoSlaugtherHeaderColCurrent".Translate(), 60f, "AutoSlaugtherHeaderTooltipCurrentFemalesYoung".Translate(), -1f);
                row2.Label("AutoSlaugtherHeaderColMax".Translate(), 56f, "AutoSlaughterHeaderTooltipMaxFemalesYoung".Translate(), -1f);

                
                row2.Label("AutoSlaugtherHeaderColCurrent".Translate(), 60f, "AutoSlaughterHeaderTooltipCurrentPregnant".Translate(), -1f);
                row2.Label("AllowSlaughter".Translate(), 72f, "AutoSlaughterHeaderTooltipAllowSlaughterPregnant".Translate(), -1f);

                row2.Label("AutoSlaugtherHeaderColCurrent".Translate(), 60f, "AutoSlaughterHeaderTooltipCurrentBonded".Translate(), -1f);
                row2.Label("AllowSlaughter".Translate(), 72f, "AutoSlaughterHeaderTooltipAllowSlaughterBonded".Translate(), -1f);

                
                row2.Label("AutoSlaugtherHeaderColCurrent".Translate(), 60f, "AutoSlaughterHeaderTooltipCurrentLactating".Translate(), -1f);
                row2.Label("AllowSlaughter".Translate(), 72f, "AutoSlaughterHeaderTooltipAllowSlaughterLactating".Translate(), -1f);

                Text.Anchor = anchor2;
                Widgets.EndGroup();

                GUI.color = Color.gray;
                Widgets.DrawLineHorizontal(rect2.x, rect2.y + rect2.height + 1f, rect2.width);
                GUI.color = Color.white;

                return false;
            }
            catch (Exception ex)
            {
                Log.Error($"[Zoology] DoAnimalHeader replacement failed: {ex}");
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(RimWorld.Dialog_AutoSlaughter), "DoAnimalRow")]
    static class Patch_Dialog_AutoSlaughter_DoAnimalRow
    {
        static bool Prepare() => ZoologyModSettings.EnableMammalLactation;

        static bool Prefix(RimWorld.Dialog_AutoSlaughter __instance, Rect rect, AutoSlaughterConfig config, int index)
        {
            bool groupStarted = false;
            bool groupEnded = false;

            try
            {
                var settings = ZoologyModSettings.Instance;
                if (settings == null || settings.LactationSlaughterHandling != ZoologyModSettings.LactationSlaughterMode.SeparateSetting)
                    return true;

                
                bool prevAllow = settings.GetAllowSlaughterLactatingFor(config.animal);

                object dictObj = DialogAS_Helper.f_animalCounts.GetValue(__instance);

                object[] tryParams = new object[] { config.animal, null };
                int total = 0, male = 0, maleYoung = 0, female = 0, femaleYoung = 0, pregnant = 0, bonded = 0;
                if (dictObj != null && DialogAS_Helper.dictTryGetValue != null)
                {
                    bool found = (bool)DialogAS_Helper.dictTryGetValue.Invoke(dictObj, tryParams);
                    if (found)
                    {
                        object rec = tryParams[1];
                        total = DialogAS_Helper.fi_total != null ? (int)(DialogAS_Helper.fi_total.GetValue(rec) ?? 0) : 0;
                        male = DialogAS_Helper.fi_male != null ? (int)(DialogAS_Helper.fi_male.GetValue(rec) ?? 0) : 0;
                        maleYoung = DialogAS_Helper.fi_maleYoung != null ? (int)(DialogAS_Helper.fi_maleYoung.GetValue(rec) ?? 0) : 0;
                        female = DialogAS_Helper.fi_female != null ? (int)(DialogAS_Helper.fi_female.GetValue(rec) ?? 0) : 0;
                        femaleYoung = DialogAS_Helper.fi_femaleYoung != null ? (int)(DialogAS_Helper.fi_femaleYoung.GetValue(rec) ?? 0) : 0;
                        pregnant = DialogAS_Helper.fi_pregnant != null ? (int)(DialogAS_Helper.fi_pregnant.GetValue(rec) ?? 0) : 0;
                        bonded = DialogAS_Helper.fi_bonded != null ? (int)(DialogAS_Helper.fi_bonded.GetValue(rec) ?? 0) : 0;
                    }
                }

                DialogAS_Helper.lactatingCounts.TryGetValue(config.animal, out int lactCount);

                if (index % 2 == 1)
                    Widgets.DrawLightHighlight(rect);

                Color color = GUI.color;

                float widthBase = DialogAS_Helper.GetLabelWidth(__instance, rect);
                float width = widthBase - 64f; 

                Widgets.BeginGroup(rect);
                groupStarted = true;

                var row = new WidgetRow(0f, 0f, UIDirection.RightThenUp, 99999f, 4f);

                row.DefIcon(config.animal, null);
                row.Gap(4f);
                GUI.color = (total == 0 ? Color.gray : color);
                row.Label(config.animal.LabelCap.Truncate(width, null), width, null, -1f);
                GUI.color = color;

                DrawCurrentCol(row, total, config.maxTotal == -1 ? (int?)null : config.maxTotal);
                DoMaxColumn(row, ref config.maxTotal, ref config.uiMaxTotalBuffer, total);

                DrawCurrentCol(row, male, config.maxMales == -1 ? (int?)null : config.maxMales);
                DoMaxColumn(row, ref config.maxMales, ref config.uiMaxMalesBuffer, male);

                DrawCurrentCol(row, maleYoung, config.maxMalesYoung == -1 ? (int?)null : config.maxMalesYoung);
                DoMaxColumn(row, ref config.maxMalesYoung, ref config.uiMaxMalesYoungBuffer, maleYoung);

                DrawCurrentCol(row, female, config.maxFemales == -1 ? (int?)null : config.maxFemales);
                DoMaxColumn(row, ref config.maxFemales, ref config.uiMaxFemalesBuffer, female);

                DrawCurrentCol(row, femaleYoung, config.maxFemalesYoung == -1 ? (int?)null : config.maxFemalesYoung);
                DoMaxColumn(row, ref config.maxFemalesYoung, ref config.uiMaxFemalesYoungBuffer, femaleYoung);

                
                Text.Anchor = TextAnchor.MiddleCenter;
                row.Label(pregnant.ToString(), 60f, null, -1f);
                Text.Anchor = TextAnchor.UpperLeft;

                bool prevAllowPreg = config.allowSlaughterPregnant;
                row.Gap(26f);
                Widgets.Checkbox(row.FinalX, 0f, ref config.allowSlaughterPregnant, 24f, false, true, null, null);
                if (prevAllowPreg != config.allowSlaughterPregnant)
                {
                    try { DialogAS_Helper.m_recalculateAnimals.Invoke(__instance, null); } catch { }
                }

                
                Text.Anchor = TextAnchor.MiddleCenter;
                row.Label(bonded.ToString(), 60f, null, -1f);
                Text.Anchor = TextAnchor.UpperLeft;
                row.Gap(24f);
                bool prevAllowBonded = config.allowSlaughterBonded;
                Widgets.Checkbox(row.FinalX, 0f, ref config.allowSlaughterBonded, 24f, false, true, null, null);
                if (prevAllowBonded != config.allowSlaughterBonded)
                {
                    try { DialogAS_Helper.m_recalculateAnimals.Invoke(__instance, null); } catch { }
                }

                
                Text.Anchor = TextAnchor.MiddleCenter;
                row.Label(lactCount.ToString(), 60f, null, -1f);
                Text.Anchor = TextAnchor.UpperLeft;

                
                bool newAllow = prevAllow;
                row.Gap(26f);
                float checkboxX = row.FinalX;
                Widgets.Checkbox(checkboxX, 0f, ref newAllow, 24f, false, true, null, null);
                if (newAllow != prevAllow)
                {
                    settings.SetAllowSlaughterLactatingFor(config.animal, newAllow);
                    try { DialogAS_Helper.m_recalculateAnimals.Invoke(__instance, null); } catch { }
                }

                Widgets.EndGroup();
                groupEnded = true;
                GUI.color = color;

                return false;
            }
            catch (Exception ex)
            {
                Log.Error($"[Zoology] DoAnimalRow replacement failed: {ex}");
                return true;
            }
            finally
            {
                if (groupStarted && !groupEnded)
                {
                    try { Widgets.EndGroup(); } catch { }
                }
            }
        }

        static void DrawCurrentCol(WidgetRow row, int val, int? limit = null)
        {
            Color? color = null;
            if (val == 0)
            {
                color = new Color?(Color.gray);
            }
            else if (limit != null)
            {
                if (limit.HasValue && limit.Value != -1 && val > limit.Value)
                    color = new Color?(ColorLibrary.RedReadable);
            }
            Color color2 = GUI.color;
            TextAnchor anchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = (color ?? Color.white);
            row.Label(val.ToString(), 60f, null, -1f);
            Text.Anchor = anchor;
            GUI.color = color2;
        }

        static void DoMaxColumn(WidgetRow row, ref int val, ref string buffer, int current)
        {
            int prev = val;
            if (val == -1)
            {
                float num2 = 68f;
                float width = (60f - num2) / 2f;
                row.Gap(width);
                if (row.ButtonIconWithBG(TexButton.Infinity, 48f, "AutoSlaughterTooltipSetLimit".Translate(), true))
                {
                    
                    try { SoundDefOf.Click.PlayOneShotOnCamera(null); } catch { }
                    val = current;
                }
                row.Gap(width);
            }
            else
            {
                row.CellGap = 0f;
                row.Gap(-4f);
                row.TextFieldNumeric<int>(ref val, ref buffer, 40f);
                val = Math.Max(0, val);
                if (row.ButtonIcon(TexButton.CloseXSmall, null, new Color?(Color.white), null, null, true, 16f))
                {
                    try { SoundDefOf.Click.PlayOneShotOnCamera(null); } catch { }
                    val = -1;
                    buffer = null;
                }
                row.CellGap = 4f;
                row.Gap(4f);
            }
        }
    }
}
