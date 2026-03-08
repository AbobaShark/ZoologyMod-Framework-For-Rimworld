using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;
namespace ZoologyMod
{
    public static class SpecialBionicPatcher
    {
        private static readonly Dictionary<string, float> HealingRegenBySize = new Dictionary<string, float>
        {
            { "VerySmall", 1f },
            { "Small", 2f },
            { "Medium", 4f },
            { "Large", 8f },
            { "VeryLarge", 16f },
            { "Huge", 32f }
        };
        private static readonly FieldInfo[] HediffStageFields = typeof(HediffStage).GetFields(BindingFlags.Public | BindingFlags.Instance);

        private static readonly List<BionicPatcherUtils.BionicConfigSimple> GlandConfigs = new List<BionicPatcherUtils.BionicConfigSimple>
        {
            new BionicPatcherUtils.BionicConfigSimple("InstallToughskinGland", "Torso", "Body", "ToughskinGland", 0.9f),
            new BionicPatcherUtils.BionicConfigSimple("InstallArmorskinGland", "Torso", "Body", "ArmorskinGland", 0.6f),
            new BionicPatcherUtils.BionicConfigSimple("InstallStoneskinGland", "Torso", "Body", "StoneskinGland", 0.3f),
            new BionicPatcherUtils.BionicConfigSimple("InstallHealingEnhancer", "Torso", "Body", "HealingEnhancer")
        };
        public static void Patch()
        {
            Log.Message("[ZoologyMod] SpecialBionicPatcher: starting patch.");

            ClearAnimalsFromRelevantRecipes();
            var allAnimalDefs = BionicPatcherUtils.GetAugmentableAnimalDefs();
            var torsoPartDef = BionicPatcherUtils.GetBodyPartDef("Torso");
            var bodyPartDef = BionicPatcherUtils.GetBodyPartDef("Body");
            int createdInstallRecipeCount = 0;
            foreach (var cfg in GlandConfigs)
            {
                var installRecipe = DefDatabase<RecipeDef>.GetNamedSilentFail(cfg.RecipeDefName);
                if (installRecipe == null) continue;
                var removeRecipe = DefDatabase<RecipeDef>.GetNamedSilentFail(cfg.RecipeDefName.Replace("Install", "Remove"));
                var torsoAnimals = new List<ThingDef>();
                var bodyAnimals = new List<ThingDef>();
                bool hasAlternateAnimalPart = !string.IsNullOrEmpty(cfg.AnimalPartDefName);

                foreach (var animal in allAnimalDefs)
                {
                    bool hasHumanPart = BionicPatcherUtils.HasBodyPart(animal.race.body, cfg.HumanPartDefName);
                    bool hasAnimalPart = hasAlternateAnimalPart && BionicPatcherUtils.HasBodyPart(animal.race.body, cfg.AnimalPartDefName);
                    if (!hasHumanPart && !hasAnimalPart) continue;

                    if (BionicPatcherUtils.HasBodyPart(animal.race.body, "Torso"))
                    {
                        torsoAnimals.Add(animal);
                    }
                    else if (BionicPatcherUtils.HasBodyPart(animal.race.body, "Body"))
                    {
                        bodyAnimals.Add(animal);
                    }
                }

                if (torsoAnimals.Count == 0 && bodyAnimals.Count == 0) continue;
                var isHealing = cfg.HediffDefName == "HealingEnhancer";
                HediffDef animalHediff = isHealing ? null : CreateOrGetAnimalGlandHediff(cfg);
                
                if (torsoAnimals.Count > 0)
                {
                    var torsoParts = installRecipe.appliedOnFixedBodyParts?.ToList() ?? new List<BodyPartDef> { torsoPartDef };
                    if (!isHealing)
                    {
                        var newRecipe = ProcessRecipeForAnimals(cfg, torsoAnimals, installRecipe, torsoParts, animalHediff, removeRecipe);
                        if (newRecipe != null) createdInstallRecipeCount++;
                    }
                    else
                    {
                        var grouped = BionicPatcherUtils.GroupAnimalsBySize(torsoAnimals);
                        foreach (var kvp in grouped)
                        {
                            if (kvp.Value.Count == 0) continue;
                            var size = kvp.Key;
                            var sizeAnimals = kvp.Value;
                            var hediff = CreateOrGetHealingHediff(cfg, size);
                            if (hediff == null) continue;
                            var newRecipe = ProcessRecipeForAnimals(cfg, sizeAnimals, installRecipe, torsoParts, hediff, removeRecipe, size);
                            if (newRecipe != null) createdInstallRecipeCount++;
                        }
                    }
                }
                
                if (bodyAnimals.Count > 0 && bodyPartDef != null)
                {
                    var bodyParts = new List<BodyPartDef> { bodyPartDef };
                    if (!isHealing)
                    {
                        var newRecipe = ProcessRecipeForAnimals(cfg, bodyAnimals, installRecipe, bodyParts, animalHediff, removeRecipe);
                        if (newRecipe != null) createdInstallRecipeCount++;
                    }
                    else
                    {
                        var grouped = BionicPatcherUtils.GroupAnimalsBySize(bodyAnimals);
                        foreach (var kvp in grouped)
                        {
                            if (kvp.Value.Count == 0) continue;
                            var size = kvp.Key;
                            var sizeAnimals = kvp.Value;
                            var hediff = CreateOrGetHealingHediff(cfg, size);
                            if (hediff == null) continue;
                            var newRecipe = ProcessRecipeForAnimals(cfg, sizeAnimals, installRecipe, bodyParts, hediff, removeRecipe, size);
                            if (newRecipe != null) createdInstallRecipeCount++;
                        }
                    }
                }
            }

            Log.Message($"[ZoologyMod] SpecialBionicPatcher: created {createdInstallRecipeCount} animal-special install recipe(s).");

            DefDatabase<RecipeDef>.ResolveAllReferences();
            Log.Message("[ZoologyMod] SpecialBionicPatcher: patch completed.");
        }
        private static RecipeDef ProcessRecipeForAnimals(BionicPatcherUtils.BionicConfigSimple cfg, List<ThingDef> animals, RecipeDef originalInstall, List<BodyPartDef> appliedParts, HediffDef hediff, RecipeDef vanillaRemove, string sizeCategory = null)
        {
            var newInstallDefName = $"{originalInstall.defName}_Animal_{appliedParts[0].defName}";
            if (sizeCategory != null) newInstallDefName += $"_{sizeCategory}";
            var existingInstall = DefDatabase<RecipeDef>.GetNamedSilentFail(newInstallDefName);
            if (existingInstall == null)
            {
                var newInstall = BionicPatcherUtils.CloneAndModifyRecipe(originalInstall, newInstallDefName, appliedParts, hediff, animals);
                DefDatabase<RecipeDef>.Add(newInstall);
                newInstall.ResolveReferences();
                BionicPatcherUtils.EnsureRemoveRecipe(cfg, animals, originalInstall, appliedParts, hediff, vanillaRemove, sizeCategory);
                return newInstall;
            }
            else
            {
                BionicPatcherUtils.EnsureRecipeUsersContains(existingInstall, animals);
                BionicPatcherUtils.EnsureRemoveRecipe(cfg, animals, originalInstall, appliedParts, hediff, vanillaRemove, sizeCategory);
                return existingInstall;
            }
        }
        private static HediffDef CreateOrGetAnimalGlandHediff(BionicPatcherUtils.BionicConfigSimple cfg)
        {
            var newName = $"{cfg.HediffDefName}_Animal";
            var existing = DefDatabase<HediffDef>.GetNamedSilentFail(newName);
            if (existing != null) return existing;
            var baseDef = DefDatabase<HediffDef>.GetNamedSilentFail(cfg.HediffDefName);
            if (baseDef == null || baseDef.stages == null || baseDef.stages.Count == 0) return null;
            var clone = CloneAndModifyHediff(baseDef, newName, false);
            var stage = clone.stages[0];
            
            stage.statOffsets = stage.statOffsets?.Where(s => s.stat != StatDefOf.PawnBeauty).ToList() ?? new List<StatModifier>();
            
            stage.statFactors ??= new List<StatModifier>();
            stage.statFactors.Add(new StatModifier { stat = StatDefOf.IncomingDamageFactor, value = cfg.DamageFactor });
            DefDatabase<HediffDef>.Add(clone);
            return clone;
        }
        private static HediffDef CreateOrGetHealingHediff(BionicPatcherUtils.BionicConfigSimple cfg, string size)
        {
            var newName = $"{cfg.HediffDefName}_Animal_{size}";
            var existing = DefDatabase<HediffDef>.GetNamedSilentFail(newName);
            if (existing != null) return existing;
            var baseDef = DefDatabase<HediffDef>.GetNamedSilentFail(cfg.HediffDefName);
            if (baseDef == null || baseDef.stages == null || baseDef.stages.Count == 0) return null;
            var clone = CloneAndModifyHediff(baseDef, newName, true);
            var stage = clone.stages[0];
            if (HealingRegenBySize.TryGetValue(size, out float regen))
            {
                try
                {
                    stage.regeneration = regen;
                }
                catch (System.Exception)
                {
                    
                }
            }
            DefDatabase<HediffDef>.Add(clone);
            return clone;
        }
        private static HediffDef CloneAndModifyHediff(HediffDef source, string newDefName, bool isHealing)
        {
            var clone = new HediffDef
            {
                defName = newDefName,
                label = source.label,
                labelNoun = source.labelNoun,
                description = source.description?.Replace("the user", "the animal").Replace("the user's", "the animal's").Replace("someone using this implant", "an animal using this implant"),
                defaultLabelColor = source.defaultLabelColor,
                descriptionHyperlinks = source.descriptionHyperlinks?.ListFullCopyOrNull(),
                isBad = source.isBad,
                spawnThingOnRemoved = source.spawnThingOnRemoved,
                tags = source.tags?.ToList(),
                hediffClass = source.hediffClass,
                comps = source.comps?.ListFullCopyOrNull()
            };

            clone.stages = new List<HediffStage>(source.stages.Count);
            foreach (var s in source.stages)
            {
                var newStage = new HediffStage();
                foreach (var field in HediffStageFields)
                {
                    if (isHealing && field.Name == "naturalHealingFactor") continue;
                    var value = field.GetValue(s);
                    if (value == null)
                    {
                        field.SetValue(newStage, null);
                        continue;
                    }
                    if (field.FieldType.IsValueType || field.FieldType == typeof(string))
                    {
                        field.SetValue(newStage, value);
                    }
                    else if (value is IList list)
                    {
                        var elementType = field.FieldType.GetGenericArguments()[0];
                        var newList = Activator.CreateInstance(field.FieldType) as IList;
                        foreach (var item in list)
                        {
                            if (item == null) continue;
                            if (elementType.IsValueType || elementType == typeof(string))
                            {
                                newList.Add(item);
                            }
                            else
                            {
                                var newItem = Activator.CreateInstance(elementType);
                                foreach (var itemField in elementType.GetFields(BindingFlags.Public | BindingFlags.Instance))
                                {
                                    itemField.SetValue(newItem, itemField.GetValue(item));
                                }
                                newList.Add(newItem);
                            }
                        }
                        field.SetValue(newStage, newList);
                    }
                    else
                    {
                        
                        var newValue = Activator.CreateInstance(field.FieldType);
                        foreach (var subField in field.FieldType.GetFields(BindingFlags.Public | BindingFlags.Instance))
                        {
                            subField.SetValue(newValue, subField.GetValue(value));
                        }
                        field.SetValue(newStage, newValue);
                    }
                }
                clone.stages.Add(newStage);
            }
            return clone;
        }
        private static void ClearAnimalsFromRelevantRecipes()
        {
            var relevantHediffs = new HashSet<string> { "ToughskinGland", "ArmorskinGland", "StoneskinGland", "HealingEnhancer" };
            foreach (var recipe in DefDatabase<RecipeDef>.AllDefsListForReading)
            {
                var hediffName = (recipe.addsHediff ?? recipe.removesHediff)?.defName;
                if (hediffName != null && (relevantHediffs.Contains(hediffName) ||
                                           relevantHediffs.Any(h => hediffName.StartsWith(h + "_Animal")) ||
                                           hediffName.StartsWith("HealingEnhancer_Animal_")))
                {
                    recipe.recipeUsers?.RemoveAll(td => td.race?.Animal ?? false);
                }
            }
        }
    }
}
