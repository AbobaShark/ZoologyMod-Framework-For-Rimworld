using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    [StaticConstructorOnStartup]
    public static class LactationPatcher
    {
        private const string HarmonyId = "com.abobashark.zoology.lactation";
        private const int RecentBirthAgeThresholdTicks = 10000;
        private const int MotherNearBabyMaxDistance = 30;
        private const int MotherNearBabyMaxDistanceSq = MotherNearBabyMaxDistance * MotherNearBabyMaxDistance;

        private static readonly MethodInfo BirthPostfixVoid =
            typeof(LactationPatcher).GetMethod(nameof(BirthPostfix_Void), BindingFlags.Static | BindingFlags.NonPublic);

        private static readonly Dictionary<int, int> processedBirthTickByMotherId = new Dictionary<int, int>(16);
        private static Game processedBirthGame;
        private static int processedBirthLastTick = -1;
        private static bool patched;

        static LactationPatcher()
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
                ZoologyModSettings settings = ZoologyModSettings.Instance;
                if (settings != null && settings.DisableAllRuntimePatches)
                {
                    return;
                }

                if (!ZoologyModSettings.EnableMammalLactation)
                {
                    return;
                }

                if (BirthPostfixVoid == null)
                {
                    Log.Error("ZoologyMod: Could not resolve LactationPatcher birth postfix.");
                    return;
                }

                Harmony harmony = new Harmony(HarmonyId);
                int patchedCount = 0;

                patchedCount += PatchMethod(
                    harmony,
                    AccessTools.Method(typeof(Hediff_Pregnant), nameof(Hediff_Pregnant.DoBirthSpawn), new[] { typeof(Pawn), typeof(Pawn) }),
                    BirthPostfixVoid,
                    "Verse.Hediff_Pregnant.DoBirthSpawn");

                if (patchedCount == 0)
                {
                    Log.Warning("ZoologyMod: No supported birth hooks were found for lactation.");
                    return;
                }

                patched = true;
            }
            catch (Exception ex)
            {
                Log.Error("ZoologyMod: LactationPatcher initialization failed: " + ex);
            }
        }

        public static void ResetPatchedState()
        {
            patched = false;
            processedBirthGame = null;
            processedBirthLastTick = -1;
            processedBirthTickByMotherId.Clear();
            RJWLactationCompatibility.ResetCache();
        }

        private static int PatchMethod(Harmony harmony, MethodBase target, MethodInfo postfix, string label)
        {
            if (harmony == null || target == null || postfix == null)
            {
                return 0;
            }

            try
            {
                harmony.Patch(target, postfix: new HarmonyMethod(postfix));
                return 1;
            }
            catch (Exception ex)
            {
                Log.Error($"ZoologyMod: Failed to patch {label}: {ex}");
                return 0;
            }
        }

        private static void BirthPostfix_Void(object __instance, object[] __args)
        {
            try
            {
                ProcessBirth(__instance, __args);
            }
            catch (Exception ex)
            {
                Log.Error("ZoologyMod: BirthPostfix_Void error: " + ex);
            }
        }

        private static void ProcessBirth(object instance, object[] args)
        {
            if (!ZoologyModSettings.EnableMammalLactation)
            {
                return;
            }

            Pawn mother = TryGetMotherFromBirthContext(instance, args);
            if (mother == null
                || mother.Dead
                || !mother.IsMammal()
                || mother.gender != Gender.Female)
            {
                return;
            }

            HediffDef lactDef = AnimalLactationUtility.LactatingHediffDef;
            if (lactDef == null)
            {
                Log.Warning("ZoologyMod: HediffDef 'Zoology_Lactating' not found.");
                return;
            }

            if (mother.health?.hediffSet?.HasHediff(lactDef) == true)
            {
                return;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (ShouldSkipDuplicateBirthProcessing(mother, currentTick))
            {
                return;
            }

            List<Pawn> pups = TryGetNewbornsFromBirthContext(instance);
            if (pups.Count == 0)
            {
                pups = FindRecentPupsNearMother(mother);
            }

            AnimalLactationUtility.OnAnimalGaveBirth(mother, pups.Count > 0 ? pups : null);
            MarkBirthProcessed(mother, currentTick);
        }

        private static Pawn TryGetMotherFromBirthContext(object instance, object[] args)
        {
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] is Pawn pawn)
                    {
                        return pawn;
                    }
                }
            }

            if (instance is Hediff hediff && hediff.pawn != null)
            {
                return hediff.pawn;
            }

            return null;
        }

        private static List<Pawn> TryGetNewbornsFromBirthContext(object instance)
        {
            var result = new List<Pawn>(4);

            try
            {
                if (instance == null)
                {
                    return result;
                }

                Type type = instance.GetType();
                FieldInfo babiesField = type.GetField("babies", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (babiesField != null)
                {
                    AddPawnsFromValue(babiesField.GetValue(instance), result);
                }

                PropertyInfo babiesProperty = type.GetProperty("babies", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (babiesProperty != null && babiesProperty.CanRead)
                {
                    AddPawnsFromValue(babiesProperty.GetValue(instance), result);
                }
            }
            catch (Exception ex)
            {
                Log.Error("ZoologyMod: Exception in TryGetNewbornsFromBirthContext: " + ex);
            }

            return result;
        }

        private static void AddPawnsFromValue(object value, List<Pawn> result)
        {
            if (value == null || result == null)
            {
                return;
            }

            if (value is Pawn pawn)
            {
                AddPawnDistinct(pawn, result);
                return;
            }

            if (value is IEnumerable enumerable)
            {
                foreach (object entry in enumerable)
                {
                    if (entry is Pawn entryPawn)
                    {
                        AddPawnDistinct(entryPawn, result);
                    }
                }
            }
        }

        private static void AddPawnDistinct(Pawn pawn, List<Pawn> result)
        {
            if (pawn == null || result == null)
            {
                return;
            }

            for (int i = 0; i < result.Count; i++)
            {
                if (ReferenceEquals(result[i], pawn))
                {
                    return;
                }
            }

            result.Add(pawn);
        }

        private static List<Pawn> FindRecentPupsNearMother(Pawn mother)
        {
            var result = new List<Pawn>(4);

            try
            {
                Map map = mother?.Map;
                if (map?.mapPawns?.AllPawnsSpawned == null)
                {
                    return result;
                }

                IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn pawn = pawns[i];
                    if (pawn == null || ReferenceEquals(pawn, mother) || pawn.Dead || !pawn.Spawned)
                    {
                        continue;
                    }

                    if (pawn.ageTracker == null || pawn.ageTracker.AgeBiologicalTicks > RecentBirthAgeThresholdTicks)
                    {
                        continue;
                    }

                    if (!IsChildOfMother(pawn, mother))
                    {
                        continue;
                    }

                    if ((pawn.Position - mother.Position).LengthHorizontalSquared > MotherNearBabyMaxDistanceSq)
                    {
                        continue;
                    }

                    result.Add(pawn);
                }
            }
            catch (Exception ex)
            {
                Log.Error("ZoologyMod: Exception in FindRecentPupsNearMother: " + ex);
            }

            return result;
        }

        private static bool IsChildOfMother(Pawn child, Pawn mother)
        {
            List<DirectPawnRelation> relations = child?.relations?.DirectRelations;
            if (relations == null)
            {
                return false;
            }

            for (int i = 0; i < relations.Count; i++)
            {
                DirectPawnRelation relation = relations[i];
                if (relation.otherPawn == mother && relation.def == PawnRelationDefOf.Parent)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ShouldSkipDuplicateBirthProcessing(Pawn mother, int currentTick)
        {
            EnsureBirthProcessingState(currentTick);
            return currentTick > 0
                && processedBirthTickByMotherId.TryGetValue(mother.thingIDNumber, out int lastTick)
                && lastTick == currentTick;
        }

        private static void MarkBirthProcessed(Pawn mother, int currentTick)
        {
            EnsureBirthProcessingState(currentTick);
            if (currentTick > 0)
            {
                processedBirthTickByMotherId[mother.thingIDNumber] = currentTick;
            }
        }

        private static void EnsureBirthProcessingState(int currentTick)
        {
            Game currentGame = Current.Game;
            bool gameChanged = !ReferenceEquals(processedBirthGame, currentGame);
            bool tickRewound = currentTick > 0 && processedBirthLastTick > 0 && currentTick < processedBirthLastTick;
            if (gameChanged || tickRewound)
            {
                processedBirthTickByMotherId.Clear();
                processedBirthGame = currentGame;
            }

            if (currentTick > 0)
            {
                processedBirthLastTick = currentTick;
            }
        }
    }
}
