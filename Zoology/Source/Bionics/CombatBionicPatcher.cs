
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

            
            var allAnimalDefs = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(def => def.category == ThingCategory.Pawn && def.race != null && def.race.Animal && BionicPatcherUtils.CanBeAugmented(def))
                .ToList();

            var createdInstallRecipes = new List<RecipeDef>();

            foreach (var config in CombatConfigs)
            {
                var recipe = DefDatabase<RecipeDef>.GetNamedSilentFail(config.RecipeDefName);
                if (recipe == null) continue;

                string partNameToCheck = config.AnimalPartDefName ?? config.HumanPartDefName;
                var compatibleAnimals = allAnimalDefs.Where(animal => BionicPatcherUtils.HasBodyPart(animal.race.body, partNameToCheck)).ToList();

                
                if (config.RecipeDefName.Contains("Arm"))
                {
                    compatibleAnimals = allAnimalDefs.Where(animal =>
                        BionicPatcherUtils.HasBodyPart(animal.race.body, "Shoulder") || BionicPatcherUtils.HasBodyPart(animal.race.body, "Arm")).ToList();
                }

                if (compatibleAnimals.Count == 0) continue;

                var groupedBySize = BionicPatcherUtils.GroupAnimalsBySize(compatibleAnimals);

                foreach (var kvp in groupedBySize)
                {
                    var category = kvp.Key;
                    var animals = kvp.Value;

                    if (animals.Count == 0) continue;

                    var originalHediff = DefDatabase<HediffDef>.GetNamedSilentFail(config.HediffDefName);
                    if (originalHediff == null) continue;

                    
                    var newHediffName = $"{config.HediffDefName}_Animal_{category}";
                    var newHediff = BionicPatcherUtils.GetCombatHediff(config, category);
                    if (newHediff == null) continue;

                    
                    List<BodyPartDef> appliedParts = new List<BodyPartDef>();
                    var animalBodyPart = DefDatabase<BodyPartDef>.GetNamedSilentFail(config.AnimalPartDefName ?? config.HumanPartDefName);
                    if (animalBodyPart != null)
                    {
                        appliedParts.Add(animalBodyPart);
                    }

                    
                    if (config.RecipeDefName.Contains("Arm"))
                    {
                        var shoulderAnimals = animals.Where(a => BionicPatcherUtils.HasBodyPart(a.race.body, "Shoulder")).ToList();
                        var armOnlyAnimals = animals.Where(a => !BionicPatcherUtils.HasBodyPart(a.race.body, "Shoulder") && BionicPatcherUtils.HasBodyPart(a.race.body, "Arm")).ToList();

                        if (shoulderAnimals.Count > 0)
                        {
                            var shoulderParts = new List<BodyPartDef> { DefDatabase<BodyPartDef>.GetNamedSilentFail("Shoulder") };
                            var newRecipe = CreateCombatRecipe(config, category, shoulderAnimals, shoulderParts, newHediff, recipe);
                            if (newRecipe != null) createdInstallRecipes.Add(newRecipe);
                        }
                        if (armOnlyAnimals.Count > 0)
                        {
                            var armParts = new List<BodyPartDef> { DefDatabase<BodyPartDef>.GetNamedSilentFail("Arm") };
                            var newRecipe = CreateCombatRecipe(config, category, armOnlyAnimals, armParts, newHediff, recipe);
                            if (newRecipe != null) createdInstallRecipes.Add(newRecipe);
                        }
                        continue; 
                    }

                    
                    if (appliedParts.Count == 0) continue;
                    var defaultRecipe = CreateCombatRecipe(config, category, animals, appliedParts, newHediff, recipe);
                    if (defaultRecipe != null) createdInstallRecipes.Add(defaultRecipe);
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