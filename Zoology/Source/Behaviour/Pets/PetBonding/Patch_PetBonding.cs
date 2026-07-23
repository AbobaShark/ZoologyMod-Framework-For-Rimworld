using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    internal enum ExpandedBondingScope : byte
    {
        None,
        Pets,
        Animals
    }

    internal static class ExpandedBondingUtility
    {
        public const float NuzzleBondChance = 0.002f;

        private const float WildnessTolerance = 0.0001f;

        public static ExpandedBondingScope Scope
        {
            get
            {
                ZoologyModSettings settings = ZoologyMod.Settings ?? ZoologyModSettings.Instance;
                if (settings == null)
                {
                    return ModConstants.DefaultEnableAnimalBonding
                        ? ExpandedBondingScope.Animals
                        : ModConstants.DefaultEnablePetBonding
                            ? ExpandedBondingScope.Pets
                            : ExpandedBondingScope.None;
                }

                if (settings.DisableAllRuntimePatches)
                {
                    return ExpandedBondingScope.None;
                }

                if (settings.EnableAnimalBonding)
                {
                    return ExpandedBondingScope.Animals;
                }

                return settings.EnablePetBonding
                    ? ExpandedBondingScope.Pets
                    : ExpandedBondingScope.None;
            }
        }

        public static bool Enabled => Scope != ExpandedBondingScope.None;

        public static bool IsEligibleBondPair(Pawn colonist, Pawn animal)
        {
            ExpandedBondingScope scope = Scope;
            RaceProperties race = animal?.RaceProps;
            if (scope == ExpandedBondingScope.None
                || colonist?.RaceProps?.Humanlike != true
                || !colonist.IsColonist
                || colonist.Faction != Faction.OfPlayer
                || race == null
                || !race.Animal)
            {
                return false;
            }

            return scope == ExpandedBondingScope.Animals
                || (race.petness > 0f
                    && animal.def.GetStatValueAbstract(StatDefOf.Wildness)
                        <= PetPlayUtility.MaximumWildness + WildnessTolerance);
        }

        public static TrainabilityDef GetTrainabilityForBond(Pawn animal, Pawn colonist)
        {
            TrainabilityDef trainability = TrainableUtility.GetTrainability(animal);
            if (!IsEligibleBondPair(colonist, animal)
                || (trainability != null
                    && trainability.intelligenceOrder >= TrainabilityDefOf.Intermediate.intelligenceOrder))
            {
                return trainability;
            }

            return TrainabilityDefOf.Intermediate;
        }
    }

    [HarmonyPatch(
        typeof(RelationsUtility),
        nameof(RelationsUtility.TryDevelopBondRelation),
        new[] { typeof(Pawn), typeof(Pawn), typeof(float) })]
    internal static class Patch_RelationsUtility_ExpandedBondTrainability
    {
        private static readonly MethodInfo GetTrainabilityMethod = AccessTools.Method(
            typeof(TrainableUtility),
            nameof(TrainableUtility.GetTrainability),
            new[] { typeof(Pawn) });

        private static readonly MethodInfo GetTrainabilityForBondMethod = AccessTools.Method(
            typeof(ExpandedBondingUtility),
            nameof(ExpandedBondingUtility.GetTrainabilityForBond));

        public static bool Prepare()
        {
            return ExpandedBondingUtility.Enabled;
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replacements = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(GetTrainabilityMethod))
                {
                    // The animal is already on the evaluation stack. Preserve the original
                    // instruction's labels, load the humanlike pawn, then call our wrapper.
                    instruction.opcode = OpCodes.Ldarg_0;
                    instruction.operand = null;
                    yield return instruction;
                    yield return new CodeInstruction(OpCodes.Call, GetTrainabilityForBondMethod);
                    replacements++;
                    continue;
                }

                yield return instruction;
            }

            if (replacements != 1)
            {
                Log.Error(
                    $"[Zoology] Expected one trainability lookup in "
                    + $"{nameof(RelationsUtility.TryDevelopBondRelation)}, found {replacements}. "
                    + "Expanded bonding trainability bypass was not applied correctly.");
            }
        }
    }

    [HarmonyPatch(typeof(InteractionWorker_Nuzzle), nameof(InteractionWorker_Nuzzle.Interacted))]
    internal static class Patch_InteractionWorker_Nuzzle_ExpandedBondChance
    {
        public static bool Prepare()
        {
            return ExpandedBondingUtility.Enabled;
        }

        [HarmonyPostfix]
        public static void Postfix(Pawn initiator, Pawn recipient)
        {
            if (ExpandedBondingUtility.IsEligibleBondPair(recipient, initiator))
            {
                RelationsUtility.TryDevelopBondRelation(
                    recipient,
                    initiator,
                    ExpandedBondingUtility.NuzzleBondChance);
            }
        }
    }
}
