using System.Collections.Generic;
using RimWorld;
using Verse;
namespace ZoologyMod
{
    public static class SimpleBionicPatcher
    {
        private static readonly List<BionicPatcherUtils.BionicConfigSimple> SimpleConfigs = new List<BionicPatcherUtils.BionicConfigSimple>
        {
            new BionicPatcherUtils.BionicConfigSimple("InstallBionicEye", "Eye", null, "BionicEye"),
            new BionicPatcherUtils.BionicConfigSimple("InstallBionicEar", "Ear", null, "BionicEar"),
            new BionicPatcherUtils.BionicConfigSimple("InstallBionicSpine", "Spine", null, "BionicSpine"),
            new BionicPatcherUtils.BionicConfigSimple("InstallBionicHeart", "Heart", null, "BionicHeart"),
            new BionicPatcherUtils.BionicConfigSimple("InstallBionicStomach", "Stomach", null, "BionicStomach"),
            new BionicPatcherUtils.BionicConfigSimple("InstallPainstopper", "Brain", null, "Painstopper"),
            new BionicPatcherUtils.BionicConfigSimple("InstallArchotechEye", "Eye", null, "ArchotechEye"),
            new BionicPatcherUtils.BionicConfigSimple("InstallImmunoenhancer", "Kidney", null, "Immunoenhancer"),
            new BionicPatcherUtils.BionicConfigSimple("InstallCoagulator", "Torso", "Body", "Coagulator"),
            new BionicPatcherUtils.BionicConfigSimple("InstallVacskinGland", "Torso", "Body", "VacskinGland"),
            new BionicPatcherUtils.BionicConfigSimple("InstallDetoxifierStomach", "Stomach", null, "DetoxifierStomach"),
            new BionicPatcherUtils.BionicConfigSimple("InstallReprocessorStomach", "Stomach", null, "ReprocessorStomach"),
            new BionicPatcherUtils.BionicConfigSimple("InstallNuclearStomach", "Stomach", null, "NuclearStomach"),
            new BionicPatcherUtils.BionicConfigSimple("InstallCircadianAssistant", "Brain", null, "CircadianAssistant"),
            new BionicPatcherUtils.BionicConfigSimple("InstallCircadianHalfCycler", "Brain", null, "CircadianHalfCycler"),
            new BionicPatcherUtils.BionicConfigSimple("InstallMindscrew", "Brain", null, "Mindscrew"),
            new BionicPatcherUtils.BionicConfigSimple("InstallDetoxifierLung", "Lung", null, "DetoxifierLung"),
            new BionicPatcherUtils.BionicConfigSimple("InstallDetoxifierKidney", "Kidney", null, "DetoxifierKidney")
        };
        public static void Patch()
        {
            var allAnimalDefs = BionicPatcherUtils.GetAugmentableAnimalDefs();
            foreach (var cfg in SimpleConfigs)
            {
                var installRecipe = DefDatabase<RecipeDef>.GetNamedSilentFail(cfg.RecipeDefName);
                if (installRecipe == null) continue;
                var removeRecipe = DefDatabase<RecipeDef>.GetNamedSilentFail(cfg.RecipeDefName.Replace("Install", "Remove"));
                var torsoAnimals = new List<ThingDef>();
                var bodyAnimals = new List<ThingDef>();
                bool hasAlternateAnimalPart = !string.IsNullOrEmpty(cfg.AnimalPartDefName) && cfg.AnimalPartDefName != cfg.HumanPartDefName;

                foreach (var animal in allAnimalDefs)
                {
                    bool hasHumanPart = BionicPatcherUtils.HasBodyPart(animal.race.body, cfg.HumanPartDefName);
                    if (hasHumanPart)
                    {
                        torsoAnimals.Add(animal);
                        continue;
                    }

                    if (hasAlternateAnimalPart && BionicPatcherUtils.HasBodyPart(animal.race.body, cfg.AnimalPartDefName))
                    {
                        bodyAnimals.Add(animal);
                    }
                }

                if (torsoAnimals.Count > 0)
                {
                    BionicPatcherUtils.EnsureRecipeUsersContains(installRecipe, torsoAnimals);
                    if (removeRecipe != null) BionicPatcherUtils.EnsureRecipeUsersContains(removeRecipe, torsoAnimals);
                }

                if (hasAlternateAnimalPart && bodyAnimals.Count > 0)
                {
                    var bodyPart = BionicPatcherUtils.GetBodyPartDef(cfg.AnimalPartDefName);
                    if (bodyPart == null) continue;
                    var appliedParts = new List<BodyPartDef> { bodyPart };

                    var newInstallDefName = $"{cfg.RecipeDefName}_Animal_{cfg.AnimalPartDefName}";
                    var existingInstall = DefDatabase<RecipeDef>.GetNamedSilentFail(newInstallDefName);
                    if (existingInstall == null)
                    {
                        var newInstall = BionicPatcherUtils.CloneAndModifyRecipe(installRecipe, newInstallDefName, appliedParts, null, bodyAnimals);
                        DefDatabase<RecipeDef>.Add(newInstall);
                    }
                    else
                    {
                        BionicPatcherUtils.EnsureRecipeUsersContains(existingInstall, bodyAnimals);
                    }

                    BionicPatcherUtils.EnsureRemoveRecipe(cfg, bodyAnimals, installRecipe, appliedParts, installRecipe.addsHediff, removeRecipe);
                }
            }
        }
    }
}
