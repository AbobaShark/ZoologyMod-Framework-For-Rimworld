using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    [HarmonyPatch(typeof(FoodUtility), "WillEat", new[] { typeof(Pawn), typeof(Thing), typeof(Pawn), typeof(bool), typeof(bool) })]
    internal static class Patch_FoodUtility_WillEat_Thing_HotPath
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            if (codes.Count == 0)
            {
                return codes;
            }

            LocalBuilder resultLocal = generator.DeclareLocal(typeof(bool));
            Label runOriginalLabel = generator.DefineLabel();
            codes[0].labels.Add(runOriginalLabel);

            List<CodeInstruction> patched = new List<CodeInstruction>(codes.Count + 16)
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Ldloca_S, resultLocal),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Patch_FoodUtility_WillEat_Thing_HotPath), nameof(TryBlockBeforeOriginal))),
                new CodeInstruction(OpCodes.Brfalse_S, runOriginalLabel),
                new CodeInstruction(OpCodes.Ldloc_S, resultLocal),
                new CodeInstruction(OpCodes.Ret)
            };

            bool patchedFinalTrueReturn = false;
            for (int i = 0; i < codes.Count; i++)
            {
                if (!patchedFinalTrueReturn
                    && i + 1 < codes.Count
                    && codes[i].opcode == OpCodes.Ldc_I4_1
                    && codes[i + 1].opcode == OpCodes.Ret)
                {
                    Label keepTrueLabel = generator.DefineLabel();
                    List<Label> labels = codes[i].labels;
                    codes[i].labels = new List<Label>();

                    patched.Add(new CodeInstruction(OpCodes.Ldarg_0) { labels = labels });
                    patched.Add(new CodeInstruction(OpCodes.Ldarg_1));
                    patched.Add(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Patch_FoodUtility_WillEat_Thing_HotPath), nameof(ShouldBlockAfterOriginal))));
                    patched.Add(new CodeInstruction(OpCodes.Brfalse_S, keepTrueLabel));
                    patched.Add(new CodeInstruction(OpCodes.Ldc_I4_0));
                    patched.Add(new CodeInstruction(OpCodes.Ret));
                    codes[i].labels.Add(keepTrueLabel);
                    patchedFinalTrueReturn = true;
                }

                patched.Add(codes[i]);
            }

            return patched;
        }

        private static bool TryBlockBeforeOriginal(Pawn p, Thing food, out bool result)
        {
            result = false;
            if (p == null || food is not Corpse corpse)
            {
                return false;
            }

            if (p.RaceProps?.Animal == true
                && LactationSettingsGate.Enabled()
                && MammalBabyCache.ShouldUseBabyFoodRules(p))
            {
                result = false;
                return true;
            }

            if (!CannotChewSettingsGate.Enabled())
            {
                return false;
            }

            Map map = p.MapHeld ?? corpse.MapHeld;
            if (map != null && !CannotChewPresenceCache.HasCannotChewPawnsOnMap(map))
            {
                return false;
            }

            if (!CannotChewUtility.HasCannotChew(p))
            {
                return false;
            }

            if (!CannotChewUtility.IsCorpseTooLarge(p, corpse))
            {
                return false;
            }

            result = false;
            return true;
        }

        private static bool ShouldBlockAfterOriginal(Pawn p, Thing food)
        {
            if (p == null || food == null)
            {
                return false;
            }

            if (ChildcareDefenseUtility.IsEggProtectionEnabled
                && ChildcareDefenseUtility.CouldBeFertilizedEggFoodSource(food)
                && ChildcareDefenseUtility.ShouldBlockEggConsumption(p, food))
            {
                return true;
            }

            Corpse corpse = TryGetCorpseFromThing(food);
            return corpse != null && PredationHarmonyPatches.ShouldBlockGuardedCorpseConsumptionForFoodPatch(p, corpse);
        }

        private static Corpse TryGetCorpseFromThing(Thing thing)
        {
            if (thing is Corpse corpse)
            {
                return corpse;
            }

            if (thing is Pawn { Dead: true } deadPawn)
            {
                return deadPawn.Corpse;
            }

            return null;
        }
    }
}
