using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace ZoologyMod
{
    /// <summary>
    /// Machine-readable marker for PawnGenOptions inserted by Zoology's XML patches.
    /// The global companion safety logic intentionally does not use this marker; it
    /// applies to every animal selected by a standard mixed human group.
    /// </summary>
    public class ZoologyPawnGenOption : PawnGenOption
    {
    }

    /// <summary>
    /// Zoology replaces the vanilla yttakin boar option while enabled.  Disabling
    /// Zoology additions restores that option instead of weakening the vanilla group.
    /// </summary>
    public sealed class ZoologyPawnGenOption_WildBoarReplacement : ZoologyPawnGenOption
    {
    }

    internal static class ZoologyNpcGroupOptionRegistry
    {
        private sealed class Snapshot
        {
            public PawnKindDef Kind;
            public float Weight;
        }

        private sealed class Location
        {
            public List<PawnGenOption> List;
            public ZoologyPawnGenOption Option;
            public int Index;
        }

        private static readonly Dictionary<ZoologyPawnGenOption, Snapshot> snapshots =
            new Dictionary<ZoologyPawnGenOption, Snapshot>();
        private static readonly List<Location> locations = new List<Location>();
        private static bool initialized;

        public static void ApplySettings(ZoologyModSettings settings)
        {
            if (!initialized)
            {
                CaptureMarkedOptions();
            }

            bool enabled = settings == null
                ? ModConstants.DefaultEnableRaidAnimals
                : settings.EnableRaidAnimals && !settings.DisableAllRuntimePatches;
            PawnKindDef wildBoar = DefDatabase<PawnKindDef>.GetNamedSilentFail("WildBoar");

            foreach (KeyValuePair<ZoologyPawnGenOption, Snapshot> entry in snapshots)
            {
                ZoologyPawnGenOption option = entry.Key;
                Snapshot original = entry.Value;
                if (option == null || original == null)
                {
                    continue;
                }

                option.selectionWeight = original.Weight;
                if (option is ZoologyPawnGenOption_WildBoarReplacement)
                {
                    option.kind = enabled || wildBoar == null ? original.Kind : wildBoar;
                    option.selectionWeight = original.Weight;
                }
                else
                {
                    option.kind = original.Kind;
                }
            }

            if (enabled)
            {
                // Locations were captured in list/index order. Re-inserting in that
                // same order reconstructs the original XML order exactly.
                for (int i = 0; i < locations.Count; i++)
                {
                    Location location = locations[i];
                    if (location.List != null && !location.List.Contains(location.Option))
                    {
                        location.List.Insert(Mathf.Min(location.Index, location.List.Count), location.Option);
                    }
                }
            }
            else
            {
                // Physical removal also keeps disabled options out of AnyOptions and
                // MinPointsToGenerateAnything, both of which ignore selectionWeight.
                for (int i = 0; i < locations.Count; i++)
                {
                    locations[i].List?.Remove(locations[i].Option);
                }
            }
        }

        private static void CaptureMarkedOptions()
        {
            initialized = true;
            List<FactionDef> factions = DefDatabase<FactionDef>.AllDefsListForReading;
            for (int i = 0; i < factions.Count; i++)
            {
                List<PawnGroupMaker> makers = factions[i].pawnGroupMakers;
                if (makers == null)
                {
                    continue;
                }

                for (int j = 0; j < makers.Count; j++)
                {
                    PawnGroupMaker maker = makers[j];
                    Capture(maker.options);
                    Capture(maker.guards);
                    Capture(maker.traders);
                    Capture(maker.carriers);
                }
            }

            if (Prefs.DevMode)
            {
                Log.Message($"[Zoology] Registered {snapshots.Count} marked NPC animal options ({locations.Count} removable additions).");
            }
        }

        private static void Capture(List<PawnGenOption> options)
        {
            if (options == null)
            {
                return;
            }

            for (int i = 0; i < options.Count; i++)
            {
                if (options[i] is ZoologyPawnGenOption marked && !snapshots.ContainsKey(marked))
                {
                    snapshots.Add(marked, new Snapshot
                    {
                        Kind = marked.kind,
                        Weight = marked.selectionWeight
                    });
                }
                if (options[i] is ZoologyPawnGenOption addition
                    && !(addition is ZoologyPawnGenOption_WildBoarReplacement))
                {
                    locations.Add(new Location
                    {
                        List = options,
                        Option = addition,
                        Index = i
                    });
                }
            }
        }
    }

    internal static class NpcAnimalCompanionUtility
    {
        public static bool SystemEnabled
        {
            get
            {
                ZoologyModSettings settings = ModConstants.Settings;
                return settings == null || !settings.DisableAllRuntimePatches;
            }
        }

        public static bool IsHumanNpcFaction(Faction faction)
        {
            return faction != null
                && faction != Faction.OfPlayer
                && faction.def != null
                && faction.def.humanlikeFaction;
        }

        public static bool IsEligibleMaster(Pawn master, Pawn animal, Faction faction, bool requireSpawned)
        {
            if (master == null || animal == null || faction == null
                || master.DestroyedOrNull() || master.Dead
                || !master.RaceProps.Humanlike
                || master.Faction != faction
                || master.IsPrisoner
                || master.skills == null
                || master.WorkTypeIsDisabled(WorkTypeDefOf.Handling)
                || master.WorkTagIsDisabled(WorkTags.Animals))
            {
                return false;
            }

            // WorkIsActive is intentionally not checked here: it represents a
            // player-configured priority, not an NPC pawn's permanent capability.
            SkillRecord animalsSkill = master.skills.GetSkill(SkillDefOf.Animals);
            if (animalsSkill == null
                || animalsSkill.Level < TrainableUtility.MinimumHandlingSkill(animal))
            {
                return false;
            }

            // This is vanilla's own master/handling gate. It reads the animal's
            // MinimumHandlingSkill stat and also preserves special gates such as
            // connected dryads. The explicit skill check above intentionally prevents
            // an incidental bond from bypassing the requested handling requirement.
            return TrainableUtility.CanBeMaster(master, animal, requireSpawned);
        }

        public static bool IsAnimal(Pawn pawn)
        {
            return pawn?.RaceProps?.Animal == true;
        }

        public static bool IsHuman(Pawn pawn)
        {
            return pawn?.RaceProps?.Humanlike == true;
        }
    }

    internal sealed class NpcGroupGenerationContext
    {
        public PawnGroupMakerParms Parms;
        public PawnGroupMaker Maker;
        public List<PawnGenOption> SourceOptions;
        public List<PawnGenOptionWithXenotype> Selected;
        public List<Pawn> OutPawns;
        public Pawn Trader;
        public int StartIndex;
        public float TotalPoints;
        public bool TraderGuards;
    }

    internal static class NpcAnimalCompanionGeneration
    {
        private const int MaxReplacementSelections = 256;
        private const float PointEpsilon = 0.001f;

        private static readonly SimpleCurve PawnWeightFactorByMostExpensivePawnCostFractionCurve =
            new SimpleCurve
            {
                new CurvePoint(0.2f, 0.01f),
                new CurvePoint(0.3f, 0.3f),
                new CurvePoint(0.5f, 1f)
            };

        [ThreadStatic] private static List<NpcGroupGenerationContext> activeContexts;

        public static NpcGroupGenerationContext BeginNormal(
            PawnGroupMakerParms parms,
            PawnGroupMaker maker,
            List<Pawn> outPawns)
        {
            if (!ShouldTrack(parms, maker, maker?.options))
            {
                return null;
            }

            return Push(new NpcGroupGenerationContext
            {
                Parms = parms,
                Maker = maker,
                SourceOptions = maker.options,
                OutPawns = outPawns,
                StartIndex = outPawns?.Count ?? 0,
                TotalPoints = parms.points
            });
        }

        public static NpcGroupGenerationContext BeginTraderGuards(
            PawnGroupMakerParms parms,
            PawnGroupMaker maker,
            Pawn trader,
            List<Pawn> outPawns)
        {
            if (!ShouldTrack(parms, maker, maker?.guards) || !NpcAnimalCompanionUtility.IsHuman(trader))
            {
                return null;
            }

            return Push(new NpcGroupGenerationContext
            {
                Parms = parms,
                Maker = maker,
                SourceOptions = maker.guards,
                OutPawns = outPawns,
                Trader = trader,
                StartIndex = outPawns?.Count ?? 0,
                TotalPoints = parms.points,
                TraderGuards = true
            });
        }

        public static void CaptureSelection(
            float pointsTotal,
            List<PawnGenOption> options,
            PawnGroupMakerParms parms,
            IEnumerable<PawnGenOptionWithXenotype> result)
        {
            NpcGroupGenerationContext context = Peek();
            if (context == null
                || !ReferenceEquals(context.Parms, parms)
                || !ReferenceEquals(context.SourceOptions, options)
                || Math.Abs(context.TotalPoints - pointsTotal) > PointEpsilon
                || result == null)
            {
                return;
            }

            context.Selected = new List<PawnGenOptionWithXenotype>(result);
        }

        public static void Finish(NpcGroupGenerationContext context, bool process)
        {
            if (context == null)
            {
                return;
            }

            try
            {
                if (process)
                {
                    Process(context);
                }
            }
            finally
            {
                List<NpcGroupGenerationContext> contexts = activeContexts;
                if (contexts != null)
                {
                    int index = contexts.LastIndexOf(context);
                    if (index >= 0)
                    {
                        contexts.RemoveAt(index);
                    }
                }
            }
        }

        private static bool ShouldTrack(
            PawnGroupMakerParms parms,
            PawnGroupMaker maker,
            List<PawnGenOption> options)
        {
            if (!NpcAnimalCompanionUtility.SystemEnabled
                || parms == null
                || maker == null
                || options == null
                || !NpcAnimalCompanionUtility.IsHumanNpcFaction(parms.faction))
            {
                return false;
            }

            bool hasAnimal = false;
            bool hasHuman = false;
            for (int i = 0; i < options.Count; i++)
            {
                PawnKindDef kind = options[i]?.kind;
                if (kind?.RaceProps?.Animal == true)
                {
                    hasAnimal = true;
                }
                else if (kind?.RaceProps?.Humanlike == true)
                {
                    hasHuman = true;
                }

                if (hasAnimal && hasHuman)
                {
                    return true;
                }
            }

            // A list designed as an all-animal group is not a human companion group.
            return false;
        }

        private static NpcGroupGenerationContext Push(NpcGroupGenerationContext context)
        {
            activeContexts ??= new List<NpcGroupGenerationContext>(2);
            activeContexts.Add(context);
            return context;
        }

        private static NpcGroupGenerationContext Peek()
        {
            List<NpcGroupGenerationContext> contexts = activeContexts;
            return contexts == null || contexts.Count == 0 ? null : contexts[contexts.Count - 1];
        }

        private static void Process(NpcGroupGenerationContext context)
        {
            List<Pawn> outPawns = context.OutPawns;
            List<PawnGenOptionWithXenotype> selected = context.Selected;
            if (outPawns == null || selected == null || selected.Count == 0)
            {
                return;
            }

            int generatedCount = outPawns.Count - context.StartIndex;
            if (generatedCount != selected.Count)
            {
                if (Prefs.DevMode)
                {
                    Log.Warning($"[Zoology] NPC companion selection mismatch: selected={selected.Count}, generated={generatedCount}; group left untouched.");
                }
                return;
            }

            bool containsAnimal = false;
            List<Pawn> humanMembers = new List<Pawn>();
            if (context.Trader != null)
            {
                humanMembers.Add(context.Trader);
            }

            for (int i = 0; i < generatedCount; i++)
            {
                Pawn pawn = outPawns[context.StartIndex + i];
                if (NpcAnimalCompanionUtility.IsHuman(pawn))
                {
                    humanMembers.Add(pawn);
                }
                else if (NpcAnimalCompanionUtility.IsAnimal(pawn))
                {
                    containsAnimal = true;
                }
            }

            if (!containsAnimal)
            {
                return;
            }

            NpcAnimalCompanionManager manager = NpcAnimalCompanionManager.Current;
            Dictionary<Pawn, int> assignedCounts = new Dictionary<Pawn, int>(humanMembers.Count);
            for (int i = 0; i < humanMembers.Count; i++)
            {
                assignedCounts[humanMembers[i]] = manager?.CountForMaster(humanMembers[i]) ?? 0;
            }

            bool[] reject = new bool[generatedCount];
            List<KeyValuePair<Pawn, Pawn>> pairs = new List<KeyValuePair<Pawn, Pawn>>();
            float refundedPoints = 0f;
            float originallyUnspentPoints = context.TotalPoints;
            for (int i = 0; i < generatedCount; i++)
            {
                originallyUnspentPoints -= selected[i].Cost;
                Pawn animal = outPawns[context.StartIndex + i];
                if (!NpcAnimalCompanionUtility.IsAnimal(animal))
                {
                    continue;
                }

                Pawn master = ChooseMaster(humanMembers, animal, context.Parms.faction, assignedCounts);
                if (master == null)
                {
                    reject[i] = true;
                    refundedPoints += selected[i].Cost;
                    continue;
                }

                assignedCounts[master] = assignedCounts[master] + 1;
                pairs.Add(new KeyValuePair<Pawn, Pawn>(animal, master));
            }

            if (refundedPoints > PointEpsilon)
            {
                for (int i = generatedCount - 1; i >= 0; i--)
                {
                    if (!reject[i])
                    {
                        continue;
                    }

                    Pawn pawn = outPawns[context.StartIndex + i];
                    outPawns.RemoveAt(context.StartIndex + i);
                    if (pawn != null && !pawn.Destroyed)
                    {
                        pawn.Destroy(DestroyMode.Vanish);
                    }
                    selected.RemoveAt(i);
                }

                bool forceReplacementDowned = context.Parms.forceOneDowned
                    && !ContainsForceDownedPawn(context, selected.Count);
                float totalUnspent = GenerateHumanReplacements(
                    context,
                    selected,
                    humanMembers,
                    forceReplacementDowned);
                float refundedUnspent = Mathf.Max(0f, totalUnspent - Mathf.Max(0f, originallyUnspentPoints));
                if (refundedUnspent > PointEpsilon)
                {
                    Log.Warning($"[Zoology] Could not spend {refundedUnspent:F1} of {refundedPoints:F1} refunded NPC animal points for {context.Parms.faction}; vanilla constraints left no eligible human option.");
                }
            }

            int registeredCompanions = 0;
            if (pairs.Count > 0 && manager != null)
            {
                manager.RegisterGroup(context.Parms.faction, humanMembers, pairs);
                for (int i = 0; i < pairs.Count; i++)
                {
                    if (manager.IsCompanion(pairs[i].Key))
                    {
                        registeredCompanions++;
                    }
                }
            }

            if (Prefs.DevMode)
            {
                Log.Message($"[Zoology] NPC group {context.Parms.groupKind?.defName}: selectedCompanions={pairs.Count}, registeredCompanions={registeredCompanions}, rejectedAnimalPoints={refundedPoints:F1}, humans={humanMembers.Count}.");
            }
        }

        private static Pawn ChooseMaster(
            List<Pawn> humans,
            Pawn animal,
            Faction faction,
            Dictionary<Pawn, int> assignedCounts)
        {
            Pawn best = null;
            int bestCount = int.MaxValue;
            int bestSkill = int.MinValue;
            int bestId = int.MaxValue;

            for (int i = 0; i < humans.Count; i++)
            {
                Pawn candidate = humans[i];
                if (!NpcAnimalCompanionUtility.IsEligibleMaster(candidate, animal, faction, requireSpawned: false))
                {
                    continue;
                }

                assignedCounts.TryGetValue(candidate, out int count);
                if (count >= NpcAnimalCompanionManager.MaxAnimalsPerMaster)
                {
                    continue;
                }

                int skill = candidate.skills.GetSkill(SkillDefOf.Animals).Level;
                int id = candidate.thingIDNumber;
                if (count < bestCount
                    || (count == bestCount && skill > bestSkill)
                    || (count == bestCount && skill == bestSkill && id < bestId))
                {
                    best = candidate;
                    bestCount = count;
                    bestSkill = skill;
                    bestId = id;
                }
            }

            return best;
        }

        private static float GenerateHumanReplacements(
            NpcGroupGenerationContext context,
            List<PawnGenOptionWithXenotype> retained,
            List<Pawn> humanMembers,
            bool forceFirstReplacementDowned)
        {
            float retainedCost = 0f;
            bool leaderChosen = false;
            float highestCost = -1f;
            for (int i = 0; i < retained.Count; i++)
            {
                retainedCost += retained[i].Cost;
                highestCost = Mathf.Max(highestCost, retained[i].Cost);
                if (retained[i].Option.kind.factionLeader)
                {
                    leaderChosen = true;
                }
            }

            // Vanilla captures the highest option cost on its first selection pass
            // and never lowers it. Reconstruct that value before continuing the same
            // weighting curve with human-only candidates.
            List<PawnGenOptionWithXenotype> initiallyAvailable = PawnGroupMakerUtility.GetOptions(
                context.Parms,
                context.Parms.faction.def,
                context.SourceOptions,
                context.TotalPoints,
                context.TotalPoints,
                null);
            for (int i = 0; i < initiallyAvailable.Count; i++)
            {
                highestCost = Mathf.Max(highestCost, initiallyAvailable[i].Cost);
            }

            float pointsLeft = Mathf.Max(0f, context.TotalPoints - retainedCost);
            List<PawnGenOption> humanOptions = new List<PawnGenOption>();
            for (int i = 0; i < context.SourceOptions.Count; i++)
            {
                PawnGenOption option = context.SourceOptions[i];
                if (option?.kind?.RaceProps?.Humanlike == true)
                {
                    humanOptions.Add(option);
                }
            }

            if (humanOptions.Count == 0)
            {
                return pointsLeft;
            }

            if (context.Parms.seed.HasValue)
            {
                Rand.PushState(Gen.HashCombineInt(context.Parms.seed.Value, 0x5A00106));
            }

            try
            {
                List<PawnGenOptionWithXenotype> available = new List<PawnGenOptionWithXenotype>();
                int iterations = 0;
                while (iterations++ < MaxReplacementSelections)
                {
                    available.Clear();
                    List<PawnGenOptionWithXenotype> options = PawnGroupMakerUtility.GetOptions(
                        context.Parms,
                        context.Parms.faction.def,
                        humanOptions,
                        context.TotalPoints,
                        pointsLeft,
                        null,
                        retained,
                        leaderChosen);

                    for (int i = 0; i < options.Count; i++)
                    {
                        PawnGenOptionWithXenotype option = options[i];
                        if (option.Cost <= pointsLeft)
                        {
                            highestCost = Mathf.Max(highestCost, option.Cost);
                            available.Add(option);
                        }
                    }

                    if (!available.TryRandomElementByWeight(
                        option => PawnGroupMakerUtility.PawnGenOptionValid(option.Option, context.Parms, retained)
                            ? option.SelectionWeight * PawnWeightFactorByMostExpensivePawnCostFractionCurve.Evaluate(option.Cost / highestCost)
                            : 0f,
                        out PawnGenOptionWithXenotype chosen))
                    {
                        break;
                    }

                    Pawn pawn = GenerateReplacementPawn(context, chosen);
                    if (forceFirstReplacementDowned)
                    {
                        // PawnGroupKindWorker_Normal applies forceOneDowned to the
                        // first generated pawn. Preserve that contract if that pawn
                        // was the rejected animal being replaced.
                        pawn.health.forceDowned = true;
                        if (pawn.guest != null)
                        {
                            pawn.guest.Recruitable = true;
                        }
                        pawn.mindState.canFleeIndividual = false;
                        forceFirstReplacementDowned = false;
                    }
                    context.OutPawns.Add(pawn);
                    humanMembers.Add(pawn);
                    retained.Add(chosen);
                    pointsLeft -= chosen.Cost;
                    if (chosen.Option.kind.factionLeader)
                    {
                        leaderChosen = true;
                    }
                }
            }
            finally
            {
                if (context.Parms.seed.HasValue)
                {
                    Rand.PopState();
                }
            }

            return pointsLeft;
        }

        private static bool ContainsForceDownedPawn(
            NpcGroupGenerationContext context,
            int retainedCount)
        {
            for (int i = 0; i < retainedCount; i++)
            {
                Pawn pawn = context.OutPawns[context.StartIndex + i];
                if (pawn?.health?.forceDowned == true)
                {
                    return true;
                }
            }
            return false;
        }

        private static Pawn GenerateReplacementPawn(
            NpcGroupGenerationContext context,
            PawnGenOptionWithXenotype option)
        {
            PawnGroupMakerParms parms = context.Parms;
            PawnGenerationRequest request;
            if (context.TraderGuards)
            {
                request = new PawnGenerationRequest(
                    option.Option.kind,
                    parms.faction,
                    PawnGenerationContext.NonPlayer,
                    parms.tile,
                    forceGenerateNewPawn: false,
                    allowDead: false,
                    allowDowned: false,
                    canGeneratePawnRelations: true,
                    mustBeCapableOfViolence: true,
                    1f,
                    forceAddFreeWarmLayerIfNeeded: false,
                    allowGay: true,
                    allowPregnant: false,
                    allowFood: true,
                    allowAddictions: true,
                    inhabitant: parms.inhabitants,
                    fixedIdeo: parms.ideo,
                    forcedXenotype: option.Xenotype);
            }
            else
            {
                bool allowFood = parms.raidStrategy == null
                    || parms.raidStrategy.pawnsCanBringFood
                    || !parms.faction.HostileTo(Faction.OfPlayer);
                Predicate<Pawn> validator = parms.raidStrategy == null
                    ? null
                    : pawn => parms.raidStrategy.Worker.CanUsePawn(parms.points, pawn, context.OutPawns);
                request = new PawnGenerationRequest(
                    option.Option.kind,
                    parms.faction,
                    PawnGenerationContext.NonPlayer,
                    parms.tile,
                    forceGenerateNewPawn: false,
                    allowDead: false,
                    allowDowned: parms.faction.deactivated,
                    canGeneratePawnRelations: true,
                    mustBeCapableOfViolence: true,
                    1f,
                    forceAddFreeWarmLayerIfNeeded: false,
                    allowGay: true,
                    allowPregnant: true,
                    allowFood: allowFood,
                    allowAddictions: true,
                    inhabitant: parms.inhabitants,
                    validatorPostGear: validator,
                    fixedIdeo: parms.ideo,
                    forcedXenotype: option.Xenotype);
            }

            if (parms.raidAgeRestriction != null
                && parms.raidAgeRestriction.Worker.ShouldApplyToKind(option.Option.kind))
            {
                request.BiologicalAgeRange = parms.raidAgeRestriction.ageRange;
                request.AllowedDevelopmentalStages = parms.raidAgeRestriction.developmentStage;
            }
            if (option.Option.kind.pawnGroupDevelopmentStage.HasValue)
            {
                request.AllowedDevelopmentalStages = option.Option.kind.pawnGroupDevelopmentStage.Value;
            }
            if (!Find.Storyteller.difficulty.ChildRaidersAllowed
                && parms.faction.HostileTo(Faction.OfPlayer))
            {
                request.AllowedDevelopmentalStages = DevelopmentalStage.Adult;
            }

            return PawnGenerator.GeneratePawn(request);
        }
    }

    [HarmonyPatch(typeof(PawnGroupMakerUtility), nameof(PawnGroupMakerUtility.ChoosePawnGenOptionsByPoints))]
    internal static class Patch_ChoosePawnGenOptions_CaptureNpcCompanions
    {
        private static void Postfix(
            float pointsTotal,
            List<PawnGenOption> options,
            PawnGroupMakerParms groupParms,
            IEnumerable<PawnGenOptionWithXenotype> __result)
        {
            NpcAnimalCompanionGeneration.CaptureSelection(pointsTotal, options, groupParms, __result);
        }
    }

    [HarmonyPatch]
    internal static class Patch_NormalPawnGroup_GenerateNpcCompanions
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(PawnGroupKindWorker_Normal),
                "GeneratePawns",
                new[] { typeof(PawnGroupMakerParms), typeof(PawnGroupMaker), typeof(List<Pawn>), typeof(bool) });
        }

        private static void Prefix(
            PawnGroupMakerParms parms,
            PawnGroupMaker groupMaker,
            List<Pawn> outPawns,
            out NpcGroupGenerationContext __state)
        {
            __state = NpcAnimalCompanionGeneration.BeginNormal(parms, groupMaker, outPawns);
        }

        private static void Postfix(NpcGroupGenerationContext __state)
        {
            NpcAnimalCompanionGeneration.Finish(__state, process: true);
        }

        private static Exception Finalizer(Exception __exception, NpcGroupGenerationContext __state)
        {
            if (__exception != null)
            {
                NpcAnimalCompanionGeneration.Finish(__state, process: false);
            }
            return __exception;
        }
    }

    [HarmonyPatch]
    internal static class Patch_TraderGuards_GenerateNpcCompanions
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(PawnGroupKindWorker_Trader),
                "GenerateGuards",
                new[]
                {
                    typeof(PawnGroupMakerParms),
                    typeof(PawnGroupMaker),
                    typeof(Pawn),
                    typeof(List<Thing>),
                    typeof(List<Pawn>)
                });
        }

        private static void Prefix(
            PawnGroupMakerParms parms,
            PawnGroupMaker groupMaker,
            Pawn trader,
            List<Pawn> outPawns,
            out NpcGroupGenerationContext __state)
        {
            __state = NpcAnimalCompanionGeneration.BeginTraderGuards(parms, groupMaker, trader, outPawns);
        }

        private static void Postfix(NpcGroupGenerationContext __state)
        {
            NpcAnimalCompanionGeneration.Finish(__state, process: true);
        }

        private static Exception Finalizer(Exception __exception, NpcGroupGenerationContext __state)
        {
            if (__exception != null)
            {
                NpcAnimalCompanionGeneration.Finish(__state, process: false);
            }
            return __exception;
        }
    }
}
