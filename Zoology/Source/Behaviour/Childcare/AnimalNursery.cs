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
        private int canopySlot = -1;

        public Building_AnimalNursery()
        {
            young = new ThingOwner<Thing>(this, false, LookMode.Deep);
        }

        private const float LargeGroundNestBodySize = 0.5f;

        public static ThingDef NurseryDef(OffspringShelterMode kind, Pawn builder = null) =>
            !AnimalOffspringShelter.IsNest(kind) ? null :
            DefDatabase<ThingDef>.GetNamedSilentFail(
                AnimalOffspringShelter.IsTreeNest(kind)
                    ? "Zoology_AnimalTreeNest"
                    : builder?.RaceProps?.baseBodySize >= LargeGroundNestBodySize
                        ? "Zoology_AnimalGroundNestLarge" : "Zoology_AnimalGroundNest");

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
        internal bool CanRaidTarget(Pawn eater, Thing food)
        {
            if (!PredatorCanOpen(eater) || eater.Downed || food == null || food.Destroyed)
                return false;
            if (food is Pawn chick)
                return AnimalBurrowHunt.CanHuntNurseryYoung(eater, this, chick);
            return food == this.TryGetComp<CompEggContainer>()?.ContainedThing
                && food.def.IsNutritionGivingIngestible && eater.WillEat(food);
        }

        internal Thing FindFoodFor(Pawn eater)
        {
            if (!PredatorCanOpen(eater) || eater.Downed) return null;
            Thing best = null;
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < young.Count; i++)
            {
                if (young[i] is not Pawn chick || !CanRaidTarget(eater, chick)) continue;
                float score = PredatorFoodChoiceUtility.ScoreShelterFood(eater, this, chick);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = chick;
                }
            }
            Thing egg = this.TryGetComp<CompEggContainer>()?.ContainedThing;
            if (CanRaidTarget(eater, egg)
                && PredatorFoodChoiceUtility.ScoreShelterFood(eater, this, egg) > bestScore)
                best = egg;
            return best;
        }

        // A tree nest's technical ground cell is not a route to its contents.
        internal bool PredatorCanOpen(Pawn predator)
        {
            if (predator?.Spawned != true || predator.Map != Map || !Spawned
                || !predator.RaceProps.Animal || this.IsForbidden(predator)) return false;
            if (TreeNest)
                return anchorTree?.Spawned == true
                    && predator.RaceProps.Animal
                    && AnimalDefense.CanClimbTree(predator, anchorTree);
            return Nest;
        }

        internal void OpenBy(Pawn attacker, Thing food = null)
        {
            if (!Spawned) return;
            NotifyAttacked(attacker, food);
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

        private bool ActiveClaim(Pawn parent)
        {
            return parent != null && !parent.Dead && !parent.Destroyed
                && parent.MapHeld == Map
                && parent.TryGetComp<CompAnimalStrategyState>()?.nursery == this
                && AnimalNestEggUtility.ReadyToPrepareNest(parent);
        }

        internal bool CanReuseFor(Pawn layer, OffspringShelterMode requested)
        {
            ThingDef required = NurseryDef(requested, layer);
            if (!Spawned || Map != layer.Map || required == null
                || TreeNest != AnimalOffspringShelter.IsTreeNest(requested)
                || def.size.x < required.size.x || def.size.z < required.size.z
                || this.IsBurning()
                || (TreeNest && (anchorTree?.Spawned != true || anchorTree.IsBurning())))
                return false;
            if (HasProtectedContents && mode != requested) return false;
            if (!OwnedBy(layer) && (HasProtectedContents || ActiveClaim(mother) || ActiveClaim(father)))
                return false;
            return this.TryGetComp<CompEggContainer>() is CompEggContainer eggs
                && layer.TryGetComp<CompEggLayer>() is CompEggLayer eggLayer
                && eggs.Accepts(eggLayer.NextEggType());
        }

        internal void ClaimFor(Pawn layer)
        {
            if (!OwnedBy(layer))
            {
                if (mother?.TryGetComp<CompAnimalStrategyState>() is CompAnimalStrategyState previous
                    && previous.nursery == this)
                    previous.nursery = null;
                if (father?.TryGetComp<CompAnimalStrategyState>() is CompAnimalStrategyState previousFather
                    && previousFather.nursery == this)
                    previousFather.nursery = null;
                mother = null;
                father = null;
                mode = AnimalOffspringShelter.For(layer.def);
                if (def.CanHaveFaction && Faction != layer.Faction)
                    SetFaction(layer.Faction);
            }
            AssignParent(layer);
        }

        public bool CanReceiveEgg(Pawn layer)
        {
            return Nest && Spawned && OwnedBy(layer)
                && this.TryGetComp<CompEggContainer>() is CompEggContainer eggs
                && layer?.TryGetComp<CompEggLayer>() is CompEggLayer eggLayer
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
            if (!this.IsHashIntervalTick(2000) || HasProtectedContents) return;
            if (HitPoints > 1) HitPoints--; else Destroy();
        }

        private bool Release(Pawn baby)
        {
            if (baby == null || !young.Contains(baby)) return false;
            ChildcareUtility.RegisterObservedMother(baby, mother);
            if (baby.TryGetComp<CompAnimalStrategyState>() is CompAnimalStrategyState state
                && state.offspringProtectionFather == null)
                state.offspringProtectionFather = father;
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
            if (Spawned && !TreeNest)
                yield return AnimalRefugeOpening.Command(this);
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
        public void NotifyAttacked(Pawn attacker, Thing food = null)
        {
            if (attacker?.Spawned != true || attacker.Dead || attacker.Map != Map || !Spawned || !HasProtectedContents)
                return;
            Thing egg = this.TryGetComp<CompEggContainer>()?.ContainedThing;
            Thing protectedThing = food is Pawn chick && Contains(chick) && !chick.Dead
                ? chick : food == egg && ChildcareDefenseUtility.CouldBeFertilizedEggFoodSource(egg)
                    ? egg : null;
            if (protectedThing == null && food != null) return;
            if (protectedThing == null)
            {
                for (int i = 0; i < young.Count; i++)
                    if (young[i] is Pawn baby && !baby.Dead && !baby.Destroyed)
                    {
                        protectedThing = baby;
                        break;
                    }
                protectedThing ??= egg;
            }
            if (protectedThing == null) return;
            if (protectedThing is Pawn child)
            {
                ChildcareUtility.TryGetBiologicalMother(child, out Pawn biologicalMother);
                ChildcareDefenseUtility.TryOrderProtection(biologicalMother ?? mother, attacker, child);
            }
            else
                ChildcareDefenseUtility.TryTriggerEggProtection(attacker, protectedThing);
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
            if (layer == null || pawn.gender != Gender.Female)
                return false;
            if (layer.CanLayNow)
                return true;
            return EggProgressField != null
                && (float)EggProgressField.GetValue(layer) >= 0.9f;
        }

        internal static Building_AnimalNursery FindReusableNest(Pawn pawn, OffspringShelterMode mode)
        {
            var index = pawn?.Map?.GetComponent<AnimalRefugeMapComponent>();
            if (index == null) return null;
            bool Available(Building_AnimalNursery nest) =>
                nest != null && nest.CanReuseFor(pawn, mode) && !nest.IsForbidden(pawn)
                && pawn.CanReserveAndReach(nest,
                    nest.TreeNest ? PathEndMode.Touch : PathEndMode.OnCell, Danger.Some);
            var known = pawn.TryGetComp<CompAnimalStrategyState>()?.nursery;
            if (known?.OwnedBy(pawn) == true && Available(known)) return known;

            Building_AnimalNursery best = null;
            float distance = float.MaxValue;
            bool own = false;
            foreach (Building_AnimalNursery nest in index.Nurseries)
            {
                if (nest == null) continue;
                bool owned = nest.OwnedBy(pawn);
                float d = (nest.Position - pawn.Position).LengthHorizontalSquared;
                if (best != null && (own && !owned || own == owned && d >= distance))
                    continue;
                if (!Available(nest)) continue;
                best = nest;
                distance = d;
                own = owned;
            }
            return best;
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
            if (pawn?.Map == null || !ModConstants.AdvancedReproduction
                || (ModConstants.IsTamedAnimal(pawn) && !ModConstants.TamedNestBuilding))
                return false;
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
                    && index.GroundNurserySiteAvailable(cell, pawn) && !cell.IsForbidden(pawn)
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
            if (!Building_AnimalBurrow.NeedsFamilyBurrow(mother)) return null;

            Building_AnimalBurrow den = index.FindReusableBurrow(
                mother, state, true, Danger.Some);
            state.familyBurrow = den;
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

            if (ModConstants.IsTamedAnimal(mother) && !ModConstants.TamedFamilyDenBuilding)
                return null;
            ThingDef shelter = Building_AnimalBurrow.SelectDefFor(mother);
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(mother.Position, 8f, true))
                if (JobGiver_AnimalBurrow.Diggable(mother, cell, shelter)
                    && mother.CanReserveAndReach(cell, PathEndMode.OnCell, Danger.None))
                    return JobGiver_AnimalBurrow.DigJob(cell, AnimalShelterDigPurpose.FamilyDen);
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
                || AnimalNestEggUtility.HasUsableEggBox(pawn))
                return null;
            Building_AnimalNursery reusable = AnimalNestEggUtility.FindReusableNest(pawn, mode);
            if (reusable != null)
            {
                reusable.ClaimFor(pawn);
                return null;
            }
            if (!FindLocation(pawn, mode, out IntVec3 cell, out Plant tree))
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
            if (!ModConstants.AdvancedReproduction
                || (ModConstants.IsTamedAnimal(pawn) && !ModConstants.TamedNestBuilding))
                return false;
            var index = Map?.GetComponent<AnimalRefugeMapComponent>();
            Plant tree = TargetB.Thing as Plant;
            if (index == null || !index.TryClaimNurserySite(tree, pawn, TargetA.Cell, out _))
                return false;
            foreach (IntVec3 part in AnimalRefugeMapComponent.NurseryFootprint(
                TargetA.Cell, pawn, tree).Cells)
                if (!pawn.Reserve(new LocalTargetInfo(part), job, 1, -1, null, errorOnFailed))
                {
                    index.ReleaseNurserySite(TargetA.Cell, pawn);
                    return false;
                }
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Map originalMap = Map;
            AddFinishAction(_ => originalMap?.GetComponent<AnimalRefugeMapComponent>()
                .ReleaseNurserySite(TargetA.Cell, pawn));
            this.FailOn(() => !ModConstants.AdvancedReproduction
                || (ModConstants.IsTamedAnimal(pawn) && !ModConstants.TamedNestBuilding)
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
                if (!ModConstants.AdvancedReproduction
                    || (ModConstants.IsTamedAnimal(pawn) && !ModConstants.TamedNestBuilding))
                    return;
                OffspringShelterMode mode = AnimalOffspringShelter.For(pawn.def);
                Plant tree = TargetB.Thing as Plant;
                if (AnimalOffspringShelter.IsTreeNest(mode)
                    && (tree == null || !tree.Spawned || tree.IsBurning()))
                    return;
                if (AnimalNestEggUtility.HasUsableEggBox(pawn)) return;
                Building_AnimalNursery reusable = AnimalNestEggUtility.FindReusableNest(pawn, mode);
                if (reusable != null)
                {
                    reusable.ClaimFor(pawn);
                    return;
                }
                var index = Map.GetComponent<AnimalRefugeMapComponent>();
                if (!index.TryClaimNurserySite(tree, pawn, TargetA.Cell, out int slot))
                    return;
                ThingDef nurseryDef = Building_AnimalNursery.NurseryDef(mode, pawn);
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
            if (!ModConstants.AdvancedReproduction) return true;
            OffspringShelterMode mode = AnimalOffspringShelter.For(pawn?.def);
            if (pawn?.TryGetComp<CompEggLayer>()?.CanLayNow != true)
                return true;
            if (mode == OffspringShelterMode.AQUATIC_CLUTCH)
            {
                AnimalRefugeMapComponent water = pawn.Map?.GetComponent<AnimalRefugeMapComponent>();
                if (water != null && water.TryRandomWaterCell(pawn, pawn.Position, 14f,
                        out IntVec3 waterCell))
                {
                    __result = JobMaker.MakeJob(AnimalEggLayingTerrain.JobDef, waterCell);
                    return false;
                }
                return false; // No accessible water: never deposit aquatic roe on dry ground.
            }
            if (!AnimalOffspringShelter.IsNest(mode))
                return true;
            // Colonist-owned birds use a suitable, available egg box first.
            // A nest is preferred, not mandatory: a fully developed egg
            // must be laid outdoors if no suitable nest can be built.
            if (AnimalNestEggUtility.HasUsableEggBox(pawn))
                return true;
            Building_AnimalNursery nursery = AnimalNestEggUtility.FindReusableNest(pawn, mode);
            if (nursery != null)
            {
                nursery.ClaimFor(pawn);
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
        private static void Prefix(Thing __instance, DamageInfo dinfo)
        {
            if (dinfo.Amount <= 0f || dinfo.Instigator is not Pawn attacker) return;
            if (__instance is Building_AnimalNursery nursery)
                nursery.NotifyAttacked(attacker);
            else if (__instance is Building_AnimalBurrow burrow)
                burrow.NotifyAttacked(attacker);
        }
    }
}
