// SpecialBionicPatcher.cs
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
            var allAnimalDefs = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(d => d.race != null && d.race.Animal && BionicPatcherUtils.CanBeAugmented(d))
                .ToList();
            var createdInstallRecipes = new List<RecipeDef>();
            foreach (var cfg in GlandConfigs)
            {
                var installRecipe = DefDatabase<RecipeDef>.GetNamedSilentFail(cfg.RecipeDefName);
                if (installRecipe == null) continue;
                var removeRecipe = DefDatabase<RecipeDef>.GetNamedSilentFail(cfg.RecipeDefName.Replace("Install", "Remove"));
                var compatible = allAnimalDefs.Where(a => BionicPatcherUtils.HasBodyPart(a.race.body, cfg.HumanPartDefName) ||
                                                           (!string.IsNullOrEmpty(cfg.AnimalPartDefName) && BionicPatcherUtils.HasBodyPart(a.race.body, cfg.AnimalPartDefName))).ToList();
                if (!compatible.Any()) continue;
                var torsoAnimals = compatible.Where(a => BionicPatcherUtils.HasBodyPart(a.race.body, "Torso")).ToList();
                var bodyAnimals = compatible.Where(a => !BionicPatcherUtils.HasBodyPart(a.race.body, "Torso") && BionicPatcherUtils.HasBodyPart(a.race.body, "Body")).ToList();
                var isHealing = cfg.HediffDefName == "HealingEnhancer";
                HediffDef animalHediff = isHealing ? null : CreateOrGetAnimalGlandHediff(cfg);
                // Torso animals
                if (torsoAnimals.Any())
                {
                    var torsoParts = installRecipe.appliedOnFixedBodyParts?.ToList() ?? new List<BodyPartDef> { DefDatabase<BodyPartDef>.GetNamed("Torso") };
                    if (!isHealing)
                    {
                        var newRecipe = ProcessRecipeForAnimals(cfg, torsoAnimals, installRecipe, torsoParts, animalHediff, removeRecipe);
                        if (newRecipe != null) createdInstallRecipes.Add(newRecipe);
                    }
                    else
                    {
                        var grouped = BionicPatcherUtils.GroupAnimalsBySize(torsoAnimals);
                        foreach (var kvp in grouped)
                        {
                            if (!kvp.Value.Any()) continue;
                            var size = kvp.Key;
                            var sizeAnimals = kvp.Value;
                            var hediff = CreateOrGetHealingHediff(cfg, size);
                            if (hediff == null) continue;
                            var newRecipe = ProcessRecipeForAnimals(cfg, sizeAnimals, installRecipe, torsoParts, hediff, removeRecipe, size);
                            if (newRecipe != null) createdInstallRecipes.Add(newRecipe);
                        }
                    }
                }
                // Body animals
                if (bodyAnimals.Any())
                {
                    var bodyParts = new List<BodyPartDef> { DefDatabase<BodyPartDef>.GetNamed("Body") };
                    if (!isHealing)
                    {
                        var newRecipe = ProcessRecipeForAnimals(cfg, bodyAnimals, installRecipe, bodyParts, animalHediff, removeRecipe);
                        if (newRecipe != null) createdInstallRecipes.Add(newRecipe);
                    }
                    else
                    {
                        var grouped = BionicPatcherUtils.GroupAnimalsBySize(bodyAnimals);
                        foreach (var kvp in grouped)
                        {
                            if (!kvp.Value.Any()) continue;
                            var size = kvp.Key;
                            var sizeAnimals = kvp.Value;
                            var hediff = CreateOrGetHealingHediff(cfg, size);
                            if (hediff == null) continue;
                            var newRecipe = ProcessRecipeForAnimals(cfg, sizeAnimals, installRecipe, bodyParts, hediff, removeRecipe, size);
                            if (newRecipe != null) createdInstallRecipes.Add(newRecipe);
                        }
                    }
                }
            }

            int added = createdInstallRecipes.Count;
            Log.Message($"[ZoologyMod] SpecialBionicPatcher: created {added} animal-special install recipe(s).");

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
            if (baseDef == null || baseDef.stages == null || !baseDef.stages.Any()) return null;
            var clone = CloneAndModifyHediff(baseDef, newName, false);
            var stage = clone.stages[0];
            // Remove beauty offset
            stage.statOffsets = stage.statOffsets?.Where(s => s.stat != StatDefOf.PawnBeauty).ToList() ?? new List<StatModifier>();
            // Add damage factor
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
            if (baseDef == null || baseDef.stages == null || !baseDef.stages.Any()) return null;
            var clone = CloneAndModifyHediff(baseDef, newName, true);
            var stage = clone.stages[0];
            var regenBySize = new Dictionary<string, float>
            {
                {"VerySmall", 1f},
                {"Small", 2f},
                {"Medium", 4f},
                {"Large", 8f},
                {"VeryLarge", 16f},
                {"Huge", 32f}
            };
            if (regenBySize.TryGetValue(size, out float regen))
            {
                try
                {
                    stage.regeneration = regen;
                }
                catch (System.Exception)
                {
                    // do nothing
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
            clone.stages = source.stages.Select(s =>
            {
                var newStage = new HediffStage();
                foreach (var field in typeof(HediffStage).GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (isHealing && field.Name == "naturalHealingFactor") continue; // Skip to leave default -1f
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
                        // For other classes, deep copy fields
                        var newValue = Activator.CreateInstance(field.FieldType);
                        foreach (var subField in field.FieldType.GetFields(BindingFlags.Public | BindingFlags.Instance))
                        {
                            subField.SetValue(newValue, subField.GetValue(value));
                        }
                        field.SetValue(newStage, newValue);
                    }
                }
                return newStage;
            }).ToList();
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
