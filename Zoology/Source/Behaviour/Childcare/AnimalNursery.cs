using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    // Bird nests retain eggs and dependent hatchlings. Mammal dens use
    // Building_AnimalBurrow and do not share this ThingOwner.
    public sealed partial class Building_AnimalNursery : Building, IThingHolderTickable
    {
        private ThingOwner<Thing> young;
        private Pawn mother;
        private Pawn father;
        private Plant anchorTree;
        private OffspringShelterMode mode;
        private int emptySince;
        private int canopySlot = -1;

        public Building_AnimalNursery()
        {
            young = new ThingOwner<Thing>(this, false, LookMode.Deep);
        }

        public static ThingDef NurseryDef(OffspringShelterMode kind) =>
            !AnimalOffspringShelter.IsNest(kind) ? null :
            DefDatabase<ThingDef>.GetNamedSilentFail(
                AnimalOffspringShelter.IsTreeNest(kind)
                    ? "Zoology_AnimalTreeNest" : "Zoology_AnimalGroundNest");

        // Render, selectable position and sway are defined in the presentation partial.

        public bool ShouldTickContents => false;
        public ThingOwner GetDirectlyHeldThings() => young;

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, young);
        }

        public void Initialize(Pawn builder, OffspringShelterMode kind, Plant tree, int slot = -1)
        {
            mode = kind;
            anchorTree = tree;
            canopySlot = slot;
            mother = builder?.gender == Gender.Female ? builder : null;
            father = builder?.gender == Gender.Male ? builder : null;
            emptySince = 0;
        }

        public bool TreeNest => AnimalOffspringShelter.IsTreeNest(mode);
        public Plant AnchorTree => anchorTree;
        public Pawn Mother => mother;
        public Pawn Father => father;
        public override AcceptanceReport ClaimableBy(Faction fac) => false;
        // Deconstructibility is enforced at the instance level. XML's
        // deconstructible=false instead produces a disabled reverse command
        // telling players to attack an otherwise natural bird nest.
        public override AcceptanceReport DeconstructibleBy(Faction fac) => false;
        public int CanopySlot => canopySlot;
        internal void SetCanopySlot(int slot) => canopySlot = slot;
        public float CanopyFootprint
        {
            get
            {
                Vector2 size = def.graphicData?.drawSize ?? new Vector2(1f, 1f);
                float area = Mathf.Max(0.01f, size.x * size.y);
                float youngArea = 0f;
                for (int i = 0; i < young.Count; i++)
                    if (young[i] is Pawn baby && !baby.Dead)
                        youngArea += Mathf.Max(0.01f, baby.BodySize);
                return Mathf.Max(area, youngArea);
            }
        }
        public bool Nest => AnimalOffspringShelter.IsNest(mode);
        public bool HasYoung => young.Count > 0;
        public bool HasProtectedContents => HasYoung
            || this.TryGetComp<CompEggContainer>()?.ContainedThing != null;
        public bool OwnedBy(Pawn adult) => adult != null && (adult == mother || adult == father);
        public bool Contains(Pawn baby) => baby != null && young.Contains(baby);
        // Evaluate sheltered prey without spawning or modifying their identity.
        internal Thing FindFoodFor(Pawn predator)
        {
            if (predator?.Spawned != true || predator.Map != Map || predator.Downed
                || !predator.RaceProps.predator)
                return null;
            for (int i = 0; i < young.Count; i++)
                if (young[i] is Pawn baby && !baby.Dead && !baby.Destroyed
                    && AnimalBurrowHunt.CanHuntNurseryYoung(predator, this, baby))
                    return baby;
            Thing egg = this.TryGetComp<CompEggContainer>()?.ContainedThing;
            return egg != null && predator.WillEat(egg) ? egg : null;
        }

        // A tree nest's technical ground cell is not a route to its contents.
        internal bool PredatorCanOpen(Pawn predator)
        {
            if (predator?.Spawned != true || predator.Map != Map || !Spawned
                || this.IsForbidden(predator)) return false;
            if (TreeNest)
                return anchorTree?.Spawned == true
                    && (AnimalDefense.CanClimbTree(predator, anchorTree)
                        || predator.flight?.CanFlyNow == true);
            return Nest;
        }

        internal void OpenForPredator(Pawn attacker)
        {
            if (!Spawned) return;
            NotifyAttacked(attacker);
            Destroy();
        }

        private void EjectEggContents()
        {
            if (!Spawned) return;
            // Using the actual owner preserves stack count, embryo progress
            // and incubation state on any removal, including tree death.
            IThingHolder holder = this.TryGetComp<CompEggContainer>() as IThingHolder;
            ThingOwner contents = holder?.GetDirectlyHeldThings();
            if (contents == null) return;
            for (int i = contents.Count - 1; i >= 0; i--)
                contents.TryDrop(contents[i], Position, Map, ThingPlaceMode.Near, out _);
        }


        public void AssignParent(Pawn adult)
        {
            if (adult == null) return;
            if (adult.gender == Gender.Female) mother = adult;
            if (adult.gender == Gender.Male) father = adult;
            CompAnimalStrategyState state = adult.TryGetComp<CompAnimalStrategyState>();
            if (state != null) state.nursery = this;
        }

        public bool CanReceiveEgg(Pawn layer)
        {
            return Nest && Spawned && OwnedBy(layer)
                && this.TryGetComp<CompEggContainer>() is CompEggContainer eggs
                && layer?.TryGetComp<CompEggLayer>() is CompEggLayer eggLayer
                && CompAnimalNestEggContainer.IsFertilizedEgg(eggLayer.NextEggType())
                && eggs.Accepts(eggLayer.NextEggType());
        }

        public bool TryReceiveBaby(Pawn baby, Thing hatchEgg = null)
        {
            if (!Spawned || baby == null || baby.Dead || baby.Destroyed
                || !AnimalOffspringShelter.KeepsBaby(mode)
                || !AnimalLifeStageUtility.IsAnimalInfantLifeStage(baby.ageTracker?.CurLifeStage)
                || baby.holdingOwner != null || Contains(baby))
                return false;

            Pawn knownMother = null;
            if (hatchEgg != null)
                knownMother = hatchEgg.TryGetComp<CompHatcher>()?.hatcheeParent;
            if (knownMother == null)
                ChildcareUtility.TryGetKnownBiologicalMother(baby, out knownMother);
            if (knownMother != null && mother != knownMother)
                return false;
            if (knownMother == null || mother == null || baby.def != mother.def)
                return false;

            Map originalMap = baby.Map;
            IntVec3 originalPos = baby.Position;
            if (baby.Spawned)
            {
                if (baby.Map != Map || !baby.Position.InHorDistOf(Position, 2f))
                    return false;
                baby.DeSpawn();
            }
            if (young.TryAdd(baby, false))
            {
                baby.Drawer?.renderer?.SetAnimation(null);
                emptySince = 0;
                return true;
            }
            if (originalMap != null && baby.holdingOwner == null && !baby.Spawned)
                GenSpawn.Spawn(baby, originalPos, originalMap);
            return false;
        }

        // Called from an animal's carry job, not from a remote child search.
        public bool TryReceiveCarriedBaby(Pawn carrier)
        {
            if (!ModConstants.AdvancedReproduction) return false;
            Pawn baby = carrier?.carryTracker?.CarriedThing as Pawn;
            if (baby == null || !OwnedBy(carrier) || !AnimalOffspringShelter.KeepsBaby(mode)
                || baby.def != carrier.def
                || !AnimalLifeStageUtility.IsAnimalInfantLifeStage(baby.ageTracker?.CurLifeStage))
                return false;

            if (!ChildcareUtility.TryGetKnownBiologicalMother(baby, out Pawn biologicalMother)
                || biologicalMother != mother)
                return false;

            if (carrier.carryTracker.innerContainer.TryTransferToContainer(baby, young, 1) <= 0)
                return false;
            emptySince = 0;
            return true;
        }

        public bool NeedsFeeding(Pawn adult)
        {
            if (!OwnedBy(adult) || !Spawned || adult.Dead || adult.Downed
                || young.Count == 0 || adult.needs?.food == null
                || adult.needs.food.CurLevelPercentage < 0.55f)
                return false;
            for (int i = 0; i < young.Count; i++)
                if (young[i] is Pawn child && !child.Dead
                    && child.needs?.food?.CurLevelPercentage < 0.55f)
                    return true;
            return false;
        }

        public void FeedYoung(Pawn adult)
        {
            if (!OwnedBy(adult) || !adult.Spawned || adult.Map != Map
                || !adult.Position.InHorDistOf(Position, 2f)
                || !NeedsFeeding(adult))
                return;

            // Altricial chicks are not lactating mammals. They receive only
            // Need_Food nutrition, and the exact amount leaves the parent.
            Need_Food adultFood = adult.needs.food;
            float available = Math.Max(0f, adultFood.CurLevel - adultFood.MaxLevel * 0.35f);
            if (available <= 0f) return;

            float required = 0f;
            for (int i = 0; i < young.Count; i++)
                if (young[i] is Pawn chick && !chick.Dead && chick.needs?.food != null)
                    required += Math.Max(0f, chick.needs.food.NutritionWanted);

            float transfer = Math.Min(available, required);
            if (transfer <= 0f) return;
            float remainingWanted = required;
            float remainingTransfer = transfer;
            for (int i = 0; i < young.Count; i++)
            {
                if (!(young[i] is Pawn chick) || chick.Dead || chick.needs?.food == null)
                    continue;
                float wanted = Math.Max(0f, chick.needs.food.NutritionWanted);
                if (wanted <= 0f) continue;
                float allocated = Math.Min(wanted, remainingTransfer * wanted / remainingWanted);
                chick.needs.food.CurLevel += allocated;
                adultFood.CurLevel -= allocated;
                remainingTransfer -= allocated;
                remainingWanted -= wanted;
                if (remainingTransfer <= 0f) break;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref young, "zoologyNurseryYoung", this);
            Scribe_References.Look(ref mother, "zoologyNurseryMother");
            Scribe_References.Look(ref father, "zoologyNurseryFather");
            Scribe_References.Look(ref anchorTree, "zoologyNurseryTree");
            Scribe_Values.Look(ref mode, "zoologyNurseryMode", OffspringShelterMode.GROUND_NEST_PRECOCIAL);
            Scribe_Values.Look(ref emptySince, "zoologyNurseryEmptySince");
            Scribe_Values.Look(ref canopySlot, "zoologyNurseryCanopySlot", -1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                young ??= new ThingOwner<Thing>(this, false, LookMode.Deep);
        }

        protected override void Tick()
        {
            // The old nest may still hold eggs or chicks when the feature is
            // switched off. Destroy() ejects them using the normal safe paths.
            if (this.IsHashIntervalTick(250) && !ModConstants.AdvancedReproduction)
            {
                Destroy();
                return;
            }
            base.Tick(); // Vanilla CompEggContainer advances actual eggs.
            if (TreeNest && (anchorTree == null || !anchorTree.Spawned))
            {
                Destroy();
                return;
            }
            for (int i = young.Count - 1; i >= 0; i--)
            {
                if (i >= young.Count || !(young[i] is Pawn baby))
                    continue;
                if (baby.Dead || baby.Destroyed
                    || !AnimalLifeStageUtility.IsAnimalInfantLifeStage(baby.ageTracker?.CurLifeStage))
                {
                    Release(baby);
                    continue;
                }
                AnimalGameApi.TickContainedPhysiology(baby);
                if (baby.Dead
                    || !AnimalLifeStageUtility.IsAnimalInfantLifeStage(baby.ageTracker?.CurLifeStage))
                    Release(baby);
            }
            if (!this.IsHashIntervalTick(2500)) return;
            if (HasProtectedContents)
            {
                emptySince = 0;
                return;
            }
            if (emptySince == 0) emptySince = GenTicks.TicksGame;
            if (GenTicks.TicksGame - emptySince >= 5000)
            {
                if (HitPoints > 1) HitPoints--; else Destroy();
            }
        }

        private bool Release(Pawn baby)
        {
            if (baby == null || !young.Contains(baby)) return false;
            if (Spawned && young.TryDrop(baby, Position, Map, ThingPlaceMode.Near, out _))
                return true;
            return false;
        }

        private void EjectAll()
        {
            if (!Spawned) return;
            for (int i = young.Count - 1; i >= 0; i--)
                if (young[i] is Pawn baby) Release(baby);
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            DisposeCanopyMesh();
            EjectEggContents();
            EjectAll();
            // A blocked map must not delete the occupants together with the
            // building. Keep surviving Pawns in the world-pawn collection.
            for (int i = young.Count - 1; i >= 0; i--)
                if (young[i] is Pawn baby && !baby.Dead && !baby.Destroyed)
                {
                    young.Remove(baby);
                    Find.WorldPawns.PassToWorld(baby, RimWorld.Planet.PawnDiscardDecideMode.KeepForever);
                }
            base.Destroy(mode);
        }

        public override string GetInspectString()
        {
            string result = base.GetInspectString();
            string youngCount = "Zoology_NestYoungCount".Translate(young.Count).ToString();
            return string.IsNullOrWhiteSpace(result) ? youngCount
                : result.TrimEnd('\r', '\n') + "\n" + youngCount;
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos()) yield return gizmo;
            if (TreeNest && anchorTree?.Spawned == true && anchorTree.Map == Map)
            {
                Gizmo treeSelection = AnimalTreeSelection.SelectTree(anchorTree);
                if (treeSelection != null) yield return treeSelection;
            }
            for (int i = 0; i < young.Count; i++)
                if (young[i] is Pawn child)
                {
                    Gizmo gizmo = Building.SelectContainedItemGizmo(this, child);
                    if (gizmo != null) yield return gizmo;
                }
        }

        // A real attack event is sufficient; no per-pawn enemy search.
        public void NotifyAttacked(Pawn attacker)
        {
            if (attacker?.Spawned != true || attacker.Dead || !HasProtectedContents)
                return;
            Thing protectedThing = null;
            for (int i = 0; i < young.Count; i++)
                if (young[i] is Pawn baby && !baby.Dead && !baby.Destroyed)
                {
                    protectedThing = baby;
                    break;
                }
            protectedThing ??= this.TryGetComp<CompEggContainer>()?.ContainedThing;
            if (protectedThing == null) return;
            Pawn first = CanRespond(mother, attacker) ? mother : null;
            Pawn second = CanRespond(father, attacker) ? father : null;
            ChildcareDefenseUtility.TryOrderProtection(first, attacker, protectedThing, second);
        }

        private bool CanRespond(Pawn adult, Pawn attacker)
        {
            return adult?.Spawned == true && adult.Map == Map && !adult.Downed
                && !adult.InMentalState && adult != attacker
                && adult.Position.InHorDistOf(Position, 30f);
        }
    }


    internal static class AnimalNestEggUtility
    {
        // CompEggLayer deliberately exposes CanLayNow but not intermediate
        // progress. Resolve the one private field once; this is used only in
        // the rare nest-planning decision, never per animal tick.
        private static readonly FieldInfo EggProgressField =
            AccessTools.Field(typeof(CompEggLayer), "eggProgress");

        internal static bool ReadyToPrepareNest(Pawn pawn)
        {
            CompEggLayer layer = pawn?.TryGetComp<CompEggLayer>();
            // Building a nest is a parental behaviour, not a test of egg
            // fertility. An unfertilized egg must be laid outside the nest,
            // but its mother can still prepare a nest for a later clutch.
            if (layer == null || pawn.gender != Gender.Female)
                return false;
            if (layer.CanLayNow)
                return true;
            return EggProgressField != null
                && (float)EggProgressField.GetValue(layer) >= 0.9f;
        }

        internal static bool HasUsableEggBox(Pawn pawn)
        {
            if (pawn?.Faction != Faction.OfPlayer || pawn.Map == null)
                return false;
            CompEggLayer layer = pawn.TryGetComp<CompEggLayer>();
            ThingDef eggDef = layer?.NextEggType();
            if (eggDef == null) return false;

            // ThingDef-indexed lookup, not a search of all map buildings.
            foreach (Thing box in pawn.Map.listerThings.ThingsOfDef(ThingDefOf.EggBox))
                if (box.Spawned && !box.IsForbidden(pawn)
                    && pawn.Position.InHorDistOf(box.Position, 30f)
                    && pawn.CanReserve(box)
                    && box.TryGetComp<CompEggContainer>()?.Accepts(eggDef) == true
                    && pawn.CanReach(box, PathEndMode.OnCell, Danger.Some))
                    return true;
            return false;
        }
    }

    // All work is initiated from a parent with a known family, not from a
    // scanner of every nest, chick, or egg on the map.
    public sealed class JobGiver_AnimalNursery : ThinkNode_JobGiver
    {
        internal static bool FindLocation(Pawn pawn, OffspringShelterMode mode,
            out IntVec3 result, out Plant anchor)
        {
            result = IntVec3.Invalid;
            anchor = null;
            if (pawn?.Map == null) return false;
            AnimalRefugeMapComponent index = pawn.Map.GetComponent<AnimalRefugeMapComponent>();
            if (index == null || !index.TryPlan()) return false;

            if (AnimalOffspringShelter.IsTreeNest(mode))
            {
                // Nest selection already encodes the species' nesting mode;
                // flight-specific comps are not required for normal birds.
                foreach (Thing thing in index.Nearby(pawn.Position, 18))
                    if (thing is Plant tree && tree.def.plant.IsTree
                        && tree.Spawned && !tree.IsBurning()
                        && tree.Growth >= 0.65f && !tree.IsForbidden(pawn)
                        && index.TreeHasRoomForNest(tree)
                        && index.NurserySiteAvailable(tree.Position, pawn, tree)
                        && pawn.CanReach(tree, PathEndMode.Touch, Danger.Some))
                    {
                        // Tree and nest share one map tile; the renderer and
                        // mouse selector both follow the actual tree canopy.
                        result = tree.Position;
                        anchor = tree;
                        return true;
                    }
                return false;
            }

            foreach (IntVec3 cell in GenRadial.RadialCellsAround(pawn.Position, 6f, true))
                if (cell.InBounds(pawn.Map) && cell.Standable(pawn.Map)
                    && index.NurserySiteAvailable(cell, pawn) && !cell.IsForbidden(pawn)
                    && pawn.CanReach(cell, PathEndMode.OnCell, Danger.Some))
                {
                    result = cell;
                    return true;
                }
            return false;
        }

        private static Job CarryBaby(Pawn child, Thing den)
        {
            Job job = JobMaker.MakeJob(
                DefDatabase<JobDef>.GetNamed("Zoology_DeliverNurseryBaby"), child, den);
            job.count = 1;
            return job;
        }

        private static Job TryFamilyBurrow(Pawn mother, CompAnimalStrategyState state,
            AnimalRefugeMapComponent index)
        {
            bool hasDependentChild = false;
            if (mother.relations != null)
                foreach (Pawn child in mother.relations.Children)
                    if (child != null && !child.Dead && child.def == mother.def
                        && child.MapHeld == mother.Map
                        && AnimalLifeStageUtility.IsAnimalInfantLifeStage(
                            child.ageTracker?.CurLifeStage))
                    {
                        hasDependentChild = true;
                        break;
                    }
            if (!hasDependentChild) return null;

            Building_AnimalBurrow den = state.familyBurrow;
            if (den?.Spawned != true || den.Map != mother.Map
                || !den.CanHouseFamily(mother))
                den = null;

            if (den == null)
            {
                // A free, compatible burrow is better than excavating an
                // additional family structure. The index is spatially bounded.
                int pathCandidates = 0;
                foreach (Thing refuge in index.Nearby(mother.Position, 24))
                {
                    if (refuge is not Building_AnimalBurrow candidate
                        || candidate.IsForbidden(mother)
                        || !candidate.CanHouseFamily(mother))
                        continue;
                    if (++pathCandidates > ZoologyTickLimiter.Refuges.MaxPathCandidates)
                        break;
                    if (mother.CanReserveAndReach(candidate, PathEndMode.Touch,
                            Danger.Some, int.MaxValue, 0)
                        && candidate.TryAssignFamily(mother))
                    {
                        den = candidate;
                        break;
                    }
                }
                state.familyBurrow = den;
            }
            if (den != null)
            {
                if (!den.TryAssignFamily(mother)) return null;
                if (mother.relations != null)
                    foreach (Pawn child in mother.relations.Children)
                        if (child?.Spawned == true && child.Map == mother.Map
                            && child.def == mother.def
                            && AnimalLifeStageUtility.IsAnimalInfantLifeStage(
                                child.ageTracker?.CurLifeStage)
                            && mother.CanReserveAndReach(child, PathEndMode.Touch,
                                Danger.Some))
                            return CarryBaby(child, den);
                if (den.NeedsFeeding(mother))
                    return JobMaker.MakeJob(
                        DefDatabase<JobDef>.GetNamed("Zoology_FeedNurseryYoung"), den);
                return null;
            }

            ThingDef shelter = Building_AnimalBurrow.SelectDefFor(mother);
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(mother.Position, 8f, true))
                if (JobGiver_AnimalBurrow.Diggable(mother, cell, shelter)
                    && mother.CanReserveAndReach(cell, PathEndMode.OnCell, Danger.None))
                    return JobMaker.MakeJob(
                        DefDatabase<JobDef>.GetNamed("Zoology_DigBurrow"), cell);
            return null;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!ModConstants.AdvancedReproduction) return null;
            OffspringShelterMode mode = AnimalOffspringShelter.For(pawn?.def);
            if (!AnimalOffspringShelter.IsNest(mode) && mode != OffspringShelterMode.DEN)
                return null;
            if (pawn?.Spawned != true || pawn.Dead || pawn.Downed || pawn.InMentalState
                || pawn.gender != Gender.Female
                || pawn.TryGetComp<CompAnimalStrategyState>() is not CompAnimalStrategyState state)
                return null;

            if (state.nextNurseryPlan > GenTicks.TicksGame)
                return null;
            state.nextNurseryPlan = GenTicks.TicksGame + 250;
            AnimalRefugeMapComponent index = pawn.Map.GetComponent<AnimalRefugeMapComponent>();
            if (index == null
                || (mode == OffspringShelterMode.DEN && !index.TryPlan()))
                return null;

            if (mode == OffspringShelterMode.DEN)
                return TryFamilyBurrow(pawn, state, index);

            Building_AnimalNursery nursery = state.nursery;
            if (nursery?.Spawned != true || nursery.Map != pawn.Map
                || !nursery.OwnedBy(pawn))
            {
                nursery = null;
                foreach (Building_AnimalNursery existing in index.Nurseries)
                    if (existing?.Spawned == true && existing.OwnedBy(pawn)
                        && existing.Map == pawn.Map)
                    {
                        nursery = existing;
                        state.nursery = existing;
                        break;
                    }
            }
            if (nursery != null)
            {
                if (nursery.NeedsFeeding(pawn))
                    return JobMaker.MakeJob(
                        DefDatabase<JobDef>.GetNamed("Zoology_FeedNurseryYoung"), nursery);
                return null;
            }

            if (!AnimalNestEggUtility.ReadyToPrepareNest(pawn)
                || AnimalNestEggUtility.HasUsableEggBox(pawn)
                || !FindLocation(pawn, mode, out IntVec3 cell, out Plant tree))
                return null;
            if (!index.TryClaimNurserySite(tree, pawn, cell, out _)) return null;
            JobDef def = DefDatabase<JobDef>.GetNamed("Zoology_BuildAnimalNursery");
            return tree != null ? JobMaker.MakeJob(def, cell, tree)
                : JobMaker.MakeJob(def, cell);
        }
    }

    public sealed class JobDriver_BuildAnimalNursery : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            var index = Map?.GetComponent<AnimalRefugeMapComponent>();
            Plant tree = TargetB.Thing as Plant;
            if (index == null || !index.TryClaimNurserySite(tree, pawn, TargetA.Cell, out _))
                return false;
            if (pawn.Reserve(TargetA, job, 1, -1, null, errorOnFailed)) return true;
            index.ReleaseNurserySite(TargetA.Cell, pawn);
            return false;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Map originalMap = Map;
            AddFinishAction(_ => originalMap?.GetComponent<AnimalRefugeMapComponent>()
                .ReleaseNurserySite(TargetA.Cell, pawn));
            this.FailOn(() => !ModConstants.AdvancedReproduction
                || pawn.Downed || pawn.InMentalState
                || (TargetB.Thing is Plant anchor && (!anchor.Spawned || anchor.IsBurning())));
            // Tree tiles need not be Standable. The builder works from
            // an adjacent reachable cell; ground nests retain OnCell.
            if (TargetB.Thing is Plant)
                yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);
            else
                yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            yield return Toils_General.Wait(400);
            yield return Toils_General.Do(() =>
            {
                OffspringShelterMode mode = AnimalOffspringShelter.For(pawn.def);
                Plant tree = TargetB.Thing as Plant;
                if (AnimalOffspringShelter.IsTreeNest(mode)
                    && (tree == null || !tree.Spawned || tree.IsBurning()))
                    return;
                var index = Map.GetComponent<AnimalRefugeMapComponent>();
                if (!index.TryClaimNurserySite(tree, pawn, TargetA.Cell, out int slot))
                    return;
                ThingDef nurseryDef = Building_AnimalNursery.NurseryDef(mode);
                if (nurseryDef == null) return;
                var nursery = (Building_AnimalNursery)ThingMaker.MakeThing(nurseryDef);
                nursery.Initialize(pawn, mode, tree, slot);
                if (pawn.Faction != null && nursery.def.CanHaveFaction)
                    nursery.SetFaction(pawn.Faction);
                index.ReleaseNurserySite(TargetA.Cell, pawn);
                GenSpawn.Spawn(nursery, tree?.Position ?? TargetA.Cell, Map);
                nursery.AssignParent(pawn);
            });
        }
    }

    public sealed class JobDriver_DeliverNurseryBaby : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) =>
            pawn.Reserve(TargetA, job, 1, -1, null, errorOnFailed)
            && pawn.Reserve(TargetB, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Pawn intendedBaby = TargetA.Thing as Pawn;
            // The baby becomes unspawned after pickup, so the standard
            // FailOnDespawnedOrNull(A) would abort the delivery mid-carry.
            // Accept either the mother's carry tracker or the final den owner.
            this.FailOn(() => !ModConstants.FamilyDens
                || intendedBaby == null || intendedBaby.Dead
                || intendedBaby.Destroyed
                || (!intendedBaby.Spawned
                    && pawn.carryTracker?.CarriedThing != intendedBaby
                    && !(TargetB.Thing is Building_AnimalBurrow den
                        && den.Contains(intendedBaby))));
            this.FailOnDespawnedOrNull(TargetIndex.B);
            // Never leave a child in the carrier if a job is interrupted.
            AddFinishAction(condition =>
            {
                if (pawn.Spawned && pawn.carryTracker?.CarriedThing == intendedBaby)
                    pawn.carryTracker.TryDropCarriedThing(
                        pawn.Position, ThingPlaceMode.Near, out _);
            });
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_Haul.StartCarryThing(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);
            yield return Toils_General.Do(() =>
            {
                bool received = TargetB.Thing is Building_AnimalBurrow den
                    && den.TryReceiveCarriedBaby(pawn);
                if (!received)
                    pawn.carryTracker.TryDropCarriedThing(
                        pawn.Position, ThingPlaceMode.Near, out _);
            });
        }
    }

    public sealed class JobDriver_FeedNurseryYoung : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) =>
            pawn.Reserve(TargetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => !ModConstants.AdvancedReproduction);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Do(() =>
            {
                if (TargetA.Thing is Building_AnimalBurrow burrow)
                    burrow.FeedYoung(pawn);
                else if (TargetA.Thing is Building_AnimalNursery nest)
                    nest.FeedYoung(pawn);
            });
        }
    }

    // Vanilla JobDriver_LayEgg accepts any thing carrying CompEggContainer;
    // its JobGiver only searches ThingDefOf.EggBox, so supply our known nest
    // before it tries an unrelated egg box or a random ground cell.
    [HarmonyPatch(typeof(JobGiver_LayEgg), "TryGiveJob")]
    internal static class Patch_LayEgg_UseAnimalNursery
    {
        private static bool Prefix(Pawn pawn, ref Job __result)
        {
            OffspringShelterMode mode = AnimalOffspringShelter.For(pawn?.def);
            if (pawn?.TryGetComp<CompEggLayer>()?.CanLayNow != true)
                return true;
            if (mode == OffspringShelterMode.AQUATIC_CLUTCH)
            {
                AnimalRefugeMapComponent water = pawn.Map?.GetComponent<AnimalRefugeMapComponent>();
                if (water != null && water.TryWaterCell(pawn, pawn.Position, 14f,
                        out IntVec3 waterCell) && !waterCell.IsForbidden(pawn)
                    && pawn.CanReach(waterCell, PathEndMode.OnCell, Danger.Some))
                {
                    __result = JobMaker.MakeJob(JobDefOf.LayEgg, waterCell);
                    return false;
                }
                return false; // No accessible water: never deposit aquatic roe on dry ground.
            }
            if (!AnimalOffspringShelter.IsNest(mode))
                return true;
            // Unfertilized eggs are laid by vanilla outside natural nests.
            // Neither a nest build job nor an egg-box nest diversion may
            // insert them into the protected hatching container.
            if (!CompAnimalNestEggContainer.IsFertilizedEgg(
                    pawn.TryGetComp<CompEggLayer>()?.NextEggType()))
                return true;

            // Colonist-owned birds use a suitable, available egg box first.
            // A nest is preferred, not mandatory: a fully developed egg
            // must be laid outdoors if no suitable nest can be built.
            if (AnimalNestEggUtility.HasUsableEggBox(pawn))
                return true;
            Building_AnimalNursery nursery =
                pawn.TryGetComp<CompAnimalStrategyState>()?.nursery;
            if (nursery?.CanReceiveEgg(pawn) == true
                && pawn.CanReserveAndReach(nursery,
                    nursery.TreeNest ? PathEndMode.Touch : PathEndMode.OnCell,
                    Danger.Some))
            {
                // Vanilla LayEgg always walks OnCell, which is invalid for
                // the tree's footprint. Use a Touch-based driver only there.
                JobDef layDef = nursery.TreeNest
                    ? DefDatabase<JobDef>.GetNamed("Zoology_LayEggInTreeNest")
                    : JobDefOf.LayEgg;
                __result = JobMaker.MakeJob(layDef, nursery);
                return false;
            }

            if (JobGiver_AnimalNursery.FindLocation(pawn, mode, out IntVec3 cell, out Plant tree)
                && pawn.Map.GetComponent<AnimalRefugeMapComponent>()
                    .TryClaimNurserySite(tree, pawn, cell, out _))
            {
                JobDef def = DefDatabase<JobDef>.GetNamed("Zoology_BuildAnimalNursery");
                __result = tree != null ? JobMaker.MakeJob(def, cell, tree)
                    : JobMaker.MakeJob(def, cell);
                return false;
            }
            // No reachable or buildable nest. Let vanilla select a safe
            // ground cell and lay the egg rather than retaining it forever.
            // This fallback also handles a competing nest-site reservation.
            return true;
        }
    }

    // The vanilla egg-laying driver always uses GotoCell(..., OnCell).
    // Tree nests occupy their anchor plant's tile, which may not be standable.
    // A bird reaches the nest from the adjacent tile and deposits the actual
    // CompEggLayer egg without changing its fertilization or parentage.
    public sealed class JobDriver_LayEggInTreeNest : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) =>
            pawn.Reserve(TargetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => TargetA.Thing is not Building_AnimalNursery nest
                || !nest.TreeNest || nest.AnchorTree?.Spawned != true
                || nest.IsForbidden(pawn));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(500);
            yield return Toils_General.Do(() =>
            {
                CompEggLayer layer = pawn.TryGetComp<CompEggLayer>();
                if (layer?.CanLayNow != true)
                    return;

                // ProduceEgg can consume the last fertilization charge, so
                // test the produced egg's def rather than NextEggType after
                // producing it.
                Thing egg = layer.ProduceEgg();
                if (egg == null) return;

                Building_AnimalNursery nest = TargetA.Thing as Building_AnimalNursery;
                CompEggContainer container = nest?.TryGetComp<CompEggContainer>();
                if (nest?.Spawned == true && nest.OwnedBy(pawn)
                    && container?.Accepts(egg.def) == true
                    && container.innerContainer.TryAdd(egg))
                    return;

                // Preserve the laid egg even when the nest was filled,
                // destroyed, or the fertilization state changed en route.
                GenPlace.TryPlaceThing(egg, pawn.Position, pawn.Map,
                    ThingPlaceMode.Near, (Thing placed, int count) =>
                    {
                        if (pawn.Faction != Faction.OfPlayer)
                            placed.SetForbidden(true);
                    });
            });
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    internal static class Patch_NurseryAttackDefense
    {
        private static void Postfix(Thing __instance, DamageInfo dinfo)
        {
            if (__instance is Building_AnimalNursery nursery)
                nursery.NotifyAttacked(dinfo.Instigator as Pawn);
            else if (__instance is Building_AnimalBurrow burrow)
                burrow.NotifyAttacked(dinfo.Instigator as Pawn);
        }
    }
}
