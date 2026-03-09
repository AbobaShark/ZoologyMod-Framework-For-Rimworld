using System.Collections.Generic;
using RimWorld;
using Verse;
using System.Reflection;
using System;
using System.Collections;
namespace ZoologyMod
{
    public static class BionicPatcherUtils
    {
        private static readonly Dictionary<BodyDef, HashSet<string>> bodyPartNameCache = new Dictionary<BodyDef, HashSet<string>>();
        private static readonly Dictionary<Type, FieldInfo[]> publicInstanceFieldCache = new Dictionary<Type, FieldInfo[]>();
        private static readonly Dictionary<string, HediffDef> combatHediffCache = new Dictionary<string, HediffDef>(StringComparer.Ordinal);
        private static readonly Dictionary<string, BodyPartDef> bodyPartDefCache = new Dictionary<string, BodyPartDef>(StringComparer.Ordinal);
        private static List<ThingDef> augmentableAnimalDefsCache;

        public static bool CanBeAugmented(ThingDef animal)
        {
            if (animal == null) return false;
            if (animal.race == null || !animal.race.Animal) return false;

            if (ZoologyCacheUtility.HasCannotBeAugmentedExtension(animal))
                return false;

            return true;
        }

        public static List<ThingDef> GetAugmentableAnimalDefs()
        {
            if (augmentableAnimalDefsCache != null)
            {
                return augmentableAnimalDefsCache;
            }

            var animals = new List<ThingDef>();
            foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def?.race != null && def.race.Animal && CanBeAugmented(def))
                {
                    animals.Add(def);
                }
            }

            augmentableAnimalDefsCache = animals;
            return animals;
        }

        public class BionicConfigSimple
        {
            public string RecipeDefName, HumanPartDefName, AnimalPartDefName, HediffDefName;
            public float DamageFactor = 1f;
            public BionicConfigSimple(string recipe, string humanPart, string animalPart, string hediff, float damageFactor = 1f)
            {
                RecipeDefName = recipe;
                HumanPartDefName = humanPart;
                AnimalPartDefName = animalPart;
                HediffDefName = hediff;
                DamageFactor = damageFactor;
            }
        }
        public static HediffDef GetCombatHediff(BionicConfigSimple config, string category)
        {
            var newHediffName = $"{config.HediffDefName}_Animal_{category}";
            if (!combatHediffCache.TryGetValue(newHediffName, out var hediff))
            {
                hediff = DefDatabase<HediffDef>.GetNamedSilentFail(newHediffName);
                combatHediffCache[newHediffName] = hediff;
            }
            if (hediff == null)
            {
                Log.Warning($"[ZoologyMod] Failed to load combat hediff: {newHediffName}");
            }
            return hediff;
        }
        public static BodyPartDef GetBodyPartDef(string defName)
        {
            if (string.IsNullOrEmpty(defName))
            {
                return null;
            }

            if (bodyPartDefCache.TryGetValue(defName, out var partDef))
            {
                return partDef;
            }

            partDef = DefDatabase<BodyPartDef>.GetNamedSilentFail(defName);
            bodyPartDefCache[defName] = partDef;
            return partDef;
        }
        public static void EnsureRemoveRecipe(BionicConfigSimple cfg, List<ThingDef> animals, RecipeDef originalInstall, List<BodyPartDef> appliedParts, HediffDef hediff, RecipeDef vanillaRemove, string sizeCategory = null)
        {
            if (hediff == null || appliedParts == null || appliedParts.Count == 0)
            {
                return;
            }

            if (hediff.addedPartProps != null)
            {
                return;
            }

            if (vanillaRemove != null && AppliedPartsMatch(vanillaRemove.appliedOnFixedBodyParts, appliedParts))
            {
                EnsureRecipeUsersContains(vanillaRemove, animals);
                return;
            }
            var removeDefName = $"{originalInstall.defName.Replace("Install", "Remove")}_Animal_{appliedParts[0].defName}";
            if (sizeCategory != null) removeDefName += $"_{sizeCategory}";
            var existingRemove = DefDatabase<RecipeDef>.GetNamedSilentFail(removeDefName);
            if (existingRemove != null)
            {
                EnsureRecipeUsersContains(existingRemove, animals);
                return;
            }
            try
            {
                var newRemove = CloneAndModifyRecipe(vanillaRemove ?? originalInstall, removeDefName, appliedParts, hediff, animals);
                newRemove.workerClass = typeof(Recipe_RemoveImplant);
                newRemove.removesHediff = hediff;
                newRemove.addsHediff = null;
                newRemove.label = $"remove {hediff.label.ToLower()} (animal)";
                newRemove.description = $"Remove {hediff.label} (for animals).";
                newRemove.jobString = $"Removing {hediff.label}.";
                if (hediff.spawnThingOnRemoved != null)
                {
                    newRemove.descriptionHyperlinks = new List<DefHyperlink> { new DefHyperlink(hediff.spawnThingOnRemoved) };
                }
                DefDatabase<RecipeDef>.Add(newRemove);
                newRemove.ResolveReferences();
            }
            catch (Exception e)
            {
                Log.Error($"[ZoologyMod] Failed to clone/add remove recipe '{removeDefName}': {e}");
            }
        }
        public static RecipeDef CloneAndModifyRecipe(RecipeDef source, string newDefName, List<BodyPartDef> newParts, HediffDef newHediff, List<ThingDef> newUsers)
        {
            var clone = DeepCloneDef(source);
            clone.defName = newDefName;
            clone.label = source.label + " (animal)";
            clone.description = source.description + " (for animals)";
            clone.appliedOnFixedBodyParts = newParts;
            clone.addsHediff = newHediff ?? source.addsHediff;
            clone.recipeUsers = newUsers;
            clone.developmentalStageFilter = null; 
            clone.researchPrerequisite = null; 
            return clone;
        }
        private static RecipeDef DeepCloneDef(RecipeDef source)
        {
            var clone = new RecipeDef();
            var fields = GetPublicInstanceFields(typeof(RecipeDef));
            foreach (var field in fields)
            {
                var value = field.GetValue(source);
                if (value == null)
                {
                    field.SetValue(clone, null);
                    continue;
                }
                if (IsShallowCopyType(field.FieldType))
                {
                    field.SetValue(clone, value);
                }
                else if (value is System.Collections.IList list)
                {
                    var listType = field.FieldType;
                    if (!listType.IsGenericType) continue;
                    var elementType = listType.GetGenericArguments()[0];
                    
                    System.Collections.IList newList = null;
                    try
                    {
                        newList = Activator.CreateInstance(listType) as System.Collections.IList;
                    }
                    catch
                    {
                        var concreteListType = typeof(List<>).MakeGenericType(elementType);
                        newList = Activator.CreateInstance(concreteListType) as System.Collections.IList;
                    }
                    if (newList == null) continue;
                    foreach (var item in list)
                    {
                        if (item == null) continue;
                        
                        if (elementType == typeof(Type) ||
                            typeof(Def).IsAssignableFrom(elementType) ||
                            typeof(Delegate).IsAssignableFrom(elementType) ||
                            elementType.IsEnum ||
                            elementType == typeof(string))
                        {
                            newList.Add(item);
                        }
                        else
                        {
                            var newItem = DeepCloneDefItem(elementType, item);
                            newList.Add(newItem);
                        }
                    }
                    field.SetValue(clone, newList);
                }
                else
                {
                    var newValue = DeepCloneDefItem(field.FieldType, value);
                    field.SetValue(clone, newValue);
                }
            }
            return clone;
        }
        private static object DeepCloneDefItem(System.Type type, object value)
        {
            if (value == null) return null;

            if (IsShallowCopyType(type))
            {
                return value;
            }

            if (type == typeof(ThingFilter))
            {
                return CloneFilter(value as ThingFilter);
            }
            object newValue;
            try
            {
                newValue = Activator.CreateInstance(type);
            }
            catch (MissingMethodException)
            {
                return value;
            }
            catch (Exception)
            {
                return value;
            }
            var subFields = GetPublicInstanceFields(type);
            foreach (var subField in subFields)
            {
                var subValue = subField.GetValue(value);
                if (subValue == null)
                {
                    subField.SetValue(newValue, null);
                    continue;
                }
                if (IsShallowCopyType(subField.FieldType))
                {
                    subField.SetValue(newValue, subValue);
                }
                else if (subValue is System.Collections.IList subList)
                {
                    var listType = subField.FieldType;
                    if (!listType.IsGenericType) continue;
                    var elementType = listType.GetGenericArguments()[0];
                    System.Collections.IList newList = null;
                    try
                    {
                        newList = Activator.CreateInstance(listType) as System.Collections.IList;
                    }
                    catch
                    {
                        var concreteListType = typeof(List<>).MakeGenericType(elementType);
                        newList = Activator.CreateInstance(concreteListType) as System.Collections.IList;
                    }
                    if (newList == null) continue;
                    foreach (var item in subList)
                    {
                        if (item == null) continue;
                        if (elementType == typeof(Type) ||
                            typeof(Def).IsAssignableFrom(elementType) ||
                            typeof(Delegate).IsAssignableFrom(elementType) ||
                            elementType.IsEnum ||
                            elementType == typeof(string))
                        {
                            newList.Add(item);
                        }
                        else
                        {
                            var newItem = DeepCloneDefItem(elementType, item);
                            newList.Add(newItem);
                        }
                    }
                    subField.SetValue(newValue, newList);
                }
                else
                {
                    var subNewValue = DeepCloneDefItem(subField.FieldType, subValue);
                    subField.SetValue(newValue, subNewValue);
                }
            }
            return newValue;
        }
        private static bool IsShallowCopyType(Type type)
        {
            return type.IsValueType
                || type == typeof(string)
                || type == typeof(Type)
                || typeof(Def).IsAssignableFrom(type)
                || typeof(Delegate).IsAssignableFrom(type)
                || type.IsEnum;
        }
        private static FieldInfo[] GetPublicInstanceFields(Type type)
        {
            if (publicInstanceFieldCache.TryGetValue(type, out var fields))
            {
                return fields;
            }

            fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            publicInstanceFieldCache[type] = fields;
            return fields;
        }
        public static ThingFilter CloneFilter(ThingFilter source)
        {
            if (source == null) return null;
            var clone = new ThingFilter();
            clone.CopyAllowancesFrom(source);
            return clone;
        }
        public static bool HasBodyPart(BodyDef body, string partDefName)
        {
            if (body == null || string.IsNullOrEmpty(partDefName))
            {
                return false;
            }

            if (!bodyPartNameCache.TryGetValue(body, out var partNames))
            {
                partNames = new HashSet<string>(StringComparer.Ordinal);
                if (body.AllParts != null)
                {
                    foreach (var part in body.AllParts)
                    {
                        var defName = part?.def?.defName;
                        if (defName != null)
                        {
                            partNames.Add(defName);
                        }
                    }
                }

                bodyPartNameCache[body] = partNames;
            }

            return partNames.Contains(partDefName);
        }
        public static void EnsureRecipeUsersContains(RecipeDef recipe, List<ThingDef> animals)
        {
            var recipeUsers = recipe.recipeUsers ??= new List<ThingDef>();
            var existingUsers = new HashSet<ThingDef>(recipeUsers);
            foreach (var animal in animals)
            {
                if (!CanBeAugmented(animal)) continue;

                if (existingUsers.Add(animal))
                {
                    recipeUsers.Add(animal);
                }
            }
        }
        public static bool AppliedPartsMatch(List<BodyPartDef> a, List<BodyPartDef> b)
        {
            if (ReferenceEquals(a, b))
            {
                return true;
            }

            if (a == null || b == null)
            {
                return (a == null || a.Count == 0) && (b == null || b.Count == 0);
            }

            if (a.Count != b.Count)
            {
                return false;
            }

            for (int i = 0; i < a.Count; i++)
            {
                if (!string.Equals(a[i]?.defName, b[i]?.defName, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }
        public static Dictionary<string, List<ThingDef>> GroupAnimalsBySize(List<ThingDef> animals)
        {
            var groups = new Dictionary<string, List<ThingDef>>
            {
                { "VerySmall", new List<ThingDef>() },
                { "Small", new List<ThingDef>() },
                { "Medium", new List<ThingDef>() },
                { "Large", new List<ThingDef>() },
                { "VeryLarge", new List<ThingDef>() },
                { "Huge", new List<ThingDef>() }
            };
            foreach (var animal in animals)
            {
                var bodySize = animal.race.baseBodySize;
                string category;
                if (bodySize < 0.2f) category = "VerySmall";
                else if (bodySize < 0.5f) category = "Small";
                else if (bodySize < 1f) category = "Medium";
                else if (bodySize < 2f) category = "Large";
                else if (bodySize < 3.5f) category = "VeryLarge";
                else category = "Huge";
                groups[category].Add(animal);
            }
            return groups;
        }
    }
}
