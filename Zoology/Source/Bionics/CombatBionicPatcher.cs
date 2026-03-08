using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    public static class CombatBionicPatcher
    {
        
        private static readonly List<BionicPatcherUtils.BionicConfigSimple> CombatConfigs = new List<BionicPatcherUtils.BionicConfigSimple>
        {
            new BionicPatcherUtils.BionicConfigSimple("InstallBionicJaw", "Jaw", "AnimalJaw", "BionicJaw"),
            new BionicPatcherUtils.BionicConfigSimple("InstallBionicArm", "Shoulder", null, "BionicArm"),
            new BionicPatcherUtils.BionicConfigSimple("InstallBionicLeg", "Leg", null, "BionicLeg"),
            new BionicPatcherUtils.BionicConfigSimple("InstallArchotechArm", "Shoulder", null, "ArchotechArm"),
            new BionicPatcherUtils.BionicConfigSimple("InstallArchotechLeg", "Leg", null, "ArchotechLeg")
        };

        public static void Patch()
        {
            Log.Message("[ZoologyMod] CombatBionicPatcher: starting patch.");

            int totalRecipesBefore = DefDatabase<RecipeDef>.AllDefsListForReading.Count;

            ClearAnimalsFromRelevantRecipes();

            var allAnimalDefs = BionicPatcherUtils.GetAugmentableAnimalDefs();

            foreach (var config in CombatConfigs)
            {
                var recipe = DefDatabase<RecipeDef>.GetNamedSilentFail(config.RecipeDefName);
                if (recipe == null) continue;

                bool isArmRecipe = config.RecipeDefName.IndexOf("Arm", StringComparison.Ordinal) >= 0;
                string partNameToCheck = config.AnimalPartDefName ?? config.HumanPartDefName;
                var compatibleAnimals = new List<ThingDef>();

                foreach (var animal in allAnimalDefs)
                {
                    if (animal.category != ThingCategory.Pawn)
                    {
                        continue;
                    }

                    if (isArmRecipe)
                    {
                        if (BionicPatcherUtils.HasBodyPart(animal.race.body, "Shoulder") || BionicPatcherUtils.HasBodyPart(animal.race.body, "Arm"))
                        {
                            compatibleAnimals.Add(animal);
                        }
                    }
                    else if (BionicPatcherUtils.HasBodyPart(animal.race.body, partNameToCheck))
                    {
                        compatibleAnimals.Add(animal);
                    }
                }

                if (compatibleAnimals.Count == 0) continue;

                var groupedBySize = BionicPatcherUtils.GroupAnimalsBySize(compatibleAnimals);
                var defaultAnimalBodyPart = BionicPatcherUtils.GetBodyPartDef(config.AnimalPartDefName ?? config.HumanPartDefName);
                var shoulderPart = BionicPatcherUtils.GetBodyPartDef("Shoulder");
                var armPart = BionicPatcherUtils.GetBodyPartDef("Arm");

                foreach (var kvp in groupedBySize)
                {
                    var category = kvp.Key;
                    var animals = kvp.Value;

                    if (animals.Count == 0) continue;

                    var newHediff = BionicPatcherUtils.GetCombatHediff(config, category);
                    if (newHediff == null) continue;

                    List<BodyPartDef> appliedParts = new List<BodyPartDef>();
                    var animalBodyPart = defaultAnimalBodyPart;
                    if (animalBodyPart != null)
                    {
                        appliedParts.Add(animalBodyPart);
                    }

                    if (isArmRecipe)
                    {
                        var shoulderAnimals = new List<ThingDef>();
                        var armOnlyAnimals = new List<ThingDef>();
                        foreach (var animal in animals)
                        {
                            if (BionicPatcherUtils.HasBodyPart(animal.race.body, "Shoulder"))
                            {
                                shoulderAnimals.Add(animal);
                            }
                            else if (BionicPatcherUtils.HasBodyPart(animal.race.body, "Arm"))
                            {
                                armOnlyAnimals.Add(animal);
                            }
                        }

                        if (shoulderAnimals.Count > 0 && shoulderPart != null)
                        {
                            var shoulderParts = new List<BodyPartDef> { shoulderPart };
                            CreateCombatRecipe(config, category, shoulderAnimals, shoulderParts, newHediff, recipe);
                        }
                        if (armOnlyAnimals.Count > 0 && armPart != null)
                        {
                            var armParts = new List<BodyPartDef> { armPart };
                            CreateCombatRecipe(config, category, armOnlyAnimals, armParts, newHediff, recipe);
                        }
                        continue;
                    }

                    if (appliedParts.Count == 0) continue;
                    CreateCombatRecipe(config, category, animals, appliedParts, newHediff, recipe);
                }
            }

            int totalRecipesAfter = DefDatabase<RecipeDef>.AllDefsListForReading.Count;
            int addedCount = totalRecipesAfter - totalRecipesBefore;
            Log.Message($"[ZoologyMod] CombatBionicPatcher: added {addedCount} RecipeDef(s).");

            DefDatabase<RecipeDef>.ResolveAllReferences();
            Log.Message("[ZoologyMod] CombatBionicPatcher: patch completed.");
        }

        private static RecipeDef CreateCombatRecipe(BionicPatcherUtils.BionicConfigSimple config, string category, List<ThingDef> animals, List<BodyPartDef> appliedParts, HediffDef newHediff, RecipeDef recipe)
        {
            if (appliedParts.Count == 0) return null;

            var partNames = string.Join("_", appliedParts.Select(p => p.defName));
            var newRecipeDefName = $"{config.RecipeDefName}_Animal_{category}_{partNames}";
            var existingRecipe = DefDatabase<RecipeDef>.GetNamedSilentFail(newRecipeDefName);

            if (existingRecipe != null)
            {
                BionicPatcherUtils.EnsureRecipeUsersContains(existingRecipe, animals);
                return existingRecipe;
            }

            try
            {
                var newRecipe = BionicPatcherUtils.CloneAndModifyRecipe(recipe, newRecipeDefName, appliedParts, newHediff, animals);
                DefDatabase<RecipeDef>.Add(newRecipe);
                newRecipe.ResolveReferences();
                var vanillaRemove = DefDatabase<RecipeDef>.GetNamedSilentFail(config.RecipeDefName.Replace("Install", "Remove"));
                BionicPatcherUtils.EnsureRemoveRecipe(config, animals, recipe, appliedParts, newHediff, vanillaRemove, category);
                return newRecipe;
            }
            catch (Exception e)
            {
                Log.Error($"[ZoologyMod] Failed to clone/add recipe '{newRecipeDefName}': {e}");
                return null;
            }
        }

        private static void ClearAnimalsFromRelevantRecipes()
        {
            var relevantHediffs = new HashSet<string> { "BionicJaw", "BionicArm", "BionicLeg", "ArchotechArm", "ArchotechLeg" };
            foreach (var recipe in DefDatabase<RecipeDef>.AllDefsListForReading)
            {
                var hediffName = (recipe.addsHediff ?? recipe.removesHediff)?.defName;
                if (hediffName != null && (relevantHediffs.Contains(hediffName) || hediffName.Contains("_Animal")))
                {
                    recipe.recipeUsers?.RemoveAll(td => td.race?.Animal ?? false);
                }
            }
        }
    }
}
