using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    // The only underground refuge holder. Dens, winter shelters and ordinary
    // burrows are different defs, not distinct container implementations.
    public class Building_AnimalBurrow : Building_Casket, IThingHolderTickable
    {
        public bool ShouldTickContents => false;

        private Pawn threat;
        private Pawn acceptingPawn;
        private Pawn familyMother;
        private int enteredAt;
        private float entranceBodySize = 2f;
        private float totalBodyCapacity = 2f;

        public static AnimalRefugeDef Settings =>
            DefDatabase<AnimalRefugeDef>.GetNamed("Zoology_AnimalRefuges");

        public int ContentsCount => innerContainer.Count;
        public Pawn PawnAt(int index) =>
            index >= 0 && index < innerContainer.Count
                ? innerContainer[index] as Pawn : null;

        public float TotalBodyCapacity => totalBodyCapacity;
        public float OccupiedBodySize
        {
            get
            {
                float total = 0f;
                for (int i = 0; i < innerContainer.Count; i++)
                    if (innerContainer[i] is Pawn occupant)
                        total += BodyUnits(occupant);
                return total;
            }
        }

        private static float BodyUnits(Pawn pawn) => Math.Max(0.001f, pawn.BodySize);
        public bool Contains(Pawn pawn) => pawn != null && innerContainer.Contains(pawn);
        public override AcceptanceReport ClaimableBy(Faction fac) => false;
        // Natural refuges are never player construction projects, even if
        // a resident's faction happens to match the colony.
        public override AcceptanceReport DeconstructibleBy(Faction fac) => false;

        public static bool CanConstructShelter(Pawn pawn)
        {
            if (pawn == null) return false;
            if (DefModExtensionCache<ModExtension_Burrower>.Has(pawn)
                || AnimalOffspringShelter.For(pawn.def) == OffspringShelterMode.DEN)
                return true;

            return AnimalWinterDormancy.For(pawn.def);
        }

        public static ThingDef SelectDefFor(Pawn builder)
        {
            if (builder == null) return null;
            float size = builder.BodySize;
            int side = size < 0.5f ? 1 : size < 2f ? 2 : 3;
            bool burrow = DefModExtensionCache<ModExtension_Burrower>.Has(builder);
            string name = burrow
                ? side == 1 ? "Zoology_AnimalBurrow"
                    : side == 2 ? "Zoology_AnimalBurrowMedium"
                        : "Zoology_AnimalBurrowLarge"
                : side == 1 ? "Zoology_AnimalFamilyDen"
                    : side == 2 ? "Zoology_AnimalFamilyDenMedium"
                        : "Zoology_AnimalFamilyDenLarge";
            return DefDatabase<ThingDef>.GetNamedSilentFail(name);
        }

        public void InitializeFor(Pawn creator)
        {
            entranceBodySize = BodyUnits(creator);
            totalBodyCapacity = entranceBodySize * Math.Max(1, def.size.x * def.size.z);
        }

        private static bool InfantOf(Pawn infant, Pawn mother)
        {
            return infant != null && mother != null && !infant.Dead
                && infant.def == mother.def
                && AnimalLifeStageUtility.IsAnimalInfantLifeStage(
                    infant.ageTracker?.CurLifeStage)
                && ChildcareUtility.TryGetKnownBiologicalMother(infant, out Pawn parent)
                && parent == mother;
        }

        internal static bool NeedsFamilyBurrow(Pawn mother)
        {
            if (!ModConstants.FamilyDens || mother?.gender != Gender.Female
                || AnimalOffspringShelter.For(mother.def) != OffspringShelterMode.DEN
                || mother.relations == null)
                return false;
            foreach (Pawn child in mother.relations.Children)
                if (child != null && child.MapHeld == mother.MapHeld
                    && InfantOf(child, mother))
                    return true;
            return false;
        }

        private bool OwnInfant(Pawn infant) => InfantOf(infant, familyMother);

        public bool HasDependentYoung
        {
            get
            {
                for (int i = 0; i < innerContainer.Count; i++)
                    if (innerContainer[i] is Pawn infant && OwnInfant(infant))
                        return true;
                return false;
            }
        }

        // A family claims its refuge even while the first infant is being
        // carried to it. After the last infant matures, it becomes freely usable.
        private bool FamilyNeedsDen
        {
            get
            {
                Pawn mother = familyMother;
                if (mother == null || mother.Dead || mother.Destroyed)
                    return false;
                if (HasDependentYoung) return true;
                if (mother.relations == null) return false;
                foreach (Pawn child in mother.relations.Children)
                    if (child != null && child.MapHeld == Map && InfantOf(child, mother))
                        return true;
                return false;
            }
        }

        public bool IsFamilyDenFor(Pawn mother) =>
            mother != null && mother == familyMother && FamilyNeedsDen;

        public bool CanHouseFamily(Pawn mother)
        {
            if (mother?.Spawned != true || mother.gender != Gender.Female
                || mother.Map != Map || mother.BodySize > entranceBodySize + 0.0001f
                || (familyMother != null && familyMother != mother && FamilyNeedsDen))
                return false;
            for (int i = 0; i < innerContainer.Count; i++)
                if (innerContainer[i] is Pawn resident
                    && resident != mother && !(familyMother == mother && OwnInfant(resident)))
                    return false;
            return true;
        }

        public bool TryAssignFamily(Pawn mother)
        {
            if (!ModConstants.FamilyDens || !CanHouseFamily(mother)) return false;
            familyMother = mother;
            mother.TryGetComp<CompAnimalStrategyState>()?.Remember(this);
            return true;
        }

        public bool TryReceiveCarriedBaby(Pawn carrier)
        {
            if (!ModConstants.FamilyDens) return false;
            Pawn baby = carrier?.carryTracker?.CarriedThing as Pawn;
            if (!Spawned || carrier == null || carrier != familyMother
                || carrier.Map != Map || !OwnInfant(baby))
                return false;
            // The carrying check is intentionally independent of BodySize:
            // babies may continue growing until they leave the infant stage.
            return carrier.carryTracker.innerContainer.TryTransferToContainer(
                baby, innerContainer, 1) == 1;
        }

        internal bool CanBirthInside(Pawn mother)
        {
            if (!ModConstants.FamilyDens || !Spawned || !Contains(mother)
                || familyMother != mother
                || !AnimalWinterDormancy.MaternityDenning(mother))
                return false;
            for (int i = 0; i < innerContainer.Count; i++)
                if (innerContainer[i] is Pawn resident && resident != mother
                    && !InfantOf(resident, mother))
                    return false;
            return true;
        }

        internal bool PrepareContainedBirth(Pawn mother, out IntVec3 birthCell)
        {
            birthCell = IntVec3.Invalid;
            if (!CanBirthInside(mother)) return false;
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(Position, 3f, true))
                if (cell.InBounds(Map) && cell.Standable(Map))
                {
                    birthCell = cell;
                    return true;
                }
            ExitFamily(mother, threat, false, false);
            return false;
        }

        internal bool ReceiveNewborn(Pawn mother, Pawn baby)
        {
            if (!Spawned || !Contains(mother) || !InfantOf(baby, mother)
                || baby.holdingOwner != null || !baby.Spawned || baby.Map != Map)
                return false;
            IntVec3 position = baby.Position;
            Map map = baby.Map;
            baby.DeSpawn();
            if (!innerContainer.TryAdd(baby, false))
            {
                if (!baby.Spawned && baby.holdingOwner == null)
                    GenSpawn.Spawn(baby, position, map);
                return false;
            }
            familyMother = mother;
            CompAnimalStrategyState state = mother.TryGetComp<CompAnimalStrategyState>();
            if (state != null)
            {
                state.familyBurrow = this;
                state.Remember(this);
            }
            baby.Drawer?.renderer?.SetAnimation(null);
            return true;
        }

        internal void ReleaseMotherForBirth(Pawn mother)
        {
            if (Contains(mother)) ExitFamily(mother, threat, false, false);
        }

        public bool NeedsFeeding(Pawn mother)
        {
            if (mother != familyMother || mother?.Spawned != true || !Spawned
                || mother.needs?.food?.CurLevelPercentage < 0.55f)
                return false;
            for (int i = 0; i < innerContainer.Count; i++)
                if (innerContainer[i] is Pawn infant && OwnInfant(infant)
                    && infant.needs?.food?.CurLevelPercentage < 0.55f)
                    return true;
            return false;
        }

        public void FeedYoung(Pawn mother)
        {
            if (!NeedsFeeding(mother) || !mother.Position.InHorDistOf(Position, 4f))
                return;
            for (int i = 0; i < innerContainer.Count; i++)
                if (innerContainer[i] is Pawn infant && OwnInfant(infant))
                    AnimalLactationUtility.SuckleFromLactatingPawn(infant, mother, 2500);
        }

        private static bool Threatens(Pawn hunter, Pawn prey) =>
            prey != null && !prey.Dead
            && Patch_IsAcceptablePreyForPredator.IsSuitableWithoutYoungProtection(
                hunter, prey);

        private bool HasResidentMother(Pawn child, out Pawn mother)
        {
            mother = null;
            return ChildcareUtility.IsAnimalChild(child)
                && ChildcareUtility.TryGetKnownBiologicalMother(child, out mother)
                && Contains(mother);
        }

        internal bool ProtectedByMotherFrom(Pawn predator, Pawn child)
        {
            if (ChildcareDefenseUtility.ShouldBlockProtectedYoungPredation(predator, child))
                return true;
            Pawn mother = OwnInfant(child) ? familyMother
                : HasResidentMother(child, out Pawn resident) ? resident : null;
            return mother != null && !mother.Dead && mother.MapHeld == Map
                && (Contains(mother)
                    || mother.Spawned && mother.Position.InHorDistOf(Position, 30f))
                && !Patch_IsAcceptablePreyForPredator.IsSuitableWithoutYoungProtection(
                    predator, mother);
        }

        // Shared response for the start of digging, an ongoing excavation,
        // damage, and the final opening. The attacker is always the actual
        // digger, never an inferred nearby pawn.
        internal void WakeDormantOccupants(Pawn attacker)
        {
            if (!Spawned) return;
            bool human = attacker?.RaceProps?.Humanlike == true;
            for (int i = innerContainer.Count - 1; i >= 0; i--)
            {
                if (i >= innerContainer.Count || innerContainer[i] is not Pawn resident
                    || !AnimalWinterDormancy.IsDormant(resident))
                    continue;
                if (!Drop(resident) || !resident.Spawned || resident.Dead) continue;
                var state = resident.TryGetComp<CompAnimalStrategyState>();
                if (state != null)
                {
                    state.burrowReentryAfter = GenTicks.TicksGame + Settings.forcedExitReentryDelay;
                    state.nextDefensePlan = 0;
                }
                if (human && Rand.Chance(resident.RaceProps.manhunterOnDamageChance)
                    && !resident.InMentalState
                    && resident.mindState.mentalStateHandler.TryStartMentalState(
                        MentalStateDefOf.Manhunter, forceWake: true))
                {
                    ZoologyNotificationUtility.NotifyProvokedManhunter(resident, attacker);
                    continue;
                }
                AnimalDefense.StartGroundEscape(resident, attacker, false);
            }
        }

        public void NotifyAttacked(Pawn attacker, Pawn targetedChild = null)
        {
            if (!Spawned || attacker?.Spawned != true || attacker.Dead || attacker.Map != Map)
                return;

            Pawn protectedChild = targetedChild != null && Contains(targetedChild)
                && ChildcareUtility.IsAnimalChild(targetedChild) ? targetedChild : null;
            if (protectedChild == null)
                for (int i = 0; i < innerContainer.Count; i++)
                    if (innerContainer[i] is Pawn child && !child.Dead && OwnInfant(child))
                    {
                        protectedChild = child;
                        break;
                    }

            if (protectedChild != null && ChildcareDefenseUtility.IsYoungProtectionEnabled
                && ChildcareUtility.TryGetBiologicalMother(protectedChild, out Pawn mother)
                && mother != attacker && !mother.Dead && !mother.Downed
                && mother.MapHeld == Map && ChildcareUtility.HasChildcareExtension(mother))
            {
                if (!Contains(mother) || Drop(mother))
                    ChildcareDefenseUtility.TryOrderProtection(mother, attacker, protectedChild);
            }

            WakeDormantOccupants(attacker);
        }

        private bool SafeToJoin(Pawn incoming)
        {
            for (int i = 0; i < innerContainer.Count; i++)
            {
                if (innerContainer[i] is not Pawn resident
                    || resident == incoming || resident.Dead)
                    continue;
                if (Threatens(resident, incoming)
                    || Threatens(incoming, resident)
                        && (AnimalWinterDormancy.IsDormant(resident)
                            || ProtectedByMotherFrom(incoming, resident)))
                    return false;

                if (incoming == familyMother && incoming.relations != null)
                    foreach (Pawn infant in incoming.relations.Children)
                        if (infant != null && InfantOf(infant, incoming)
                            && Threatens(resident, infant))
                            return false;
            }
            return true;
        }

        public override bool Accepts(Thing thing)
        {
            // Building_Casket rechecks acceptance after the pawn has been despawned.
            if (acceptingPawn != null && thing == acceptingPawn)
                return base.Accepts(thing);
            if (thing is not Pawn pawn || pawn.Dead || Contains(pawn)
                || (!ModConstants.AdvancedThreatAvoidance
                    && !AnimalWinterDormancy.CanUseWinterDen(pawn))
                || (!ModConstants.FamilyDens && OwnInfant(pawn))
                || !AnimalDefense.CanUseShelter(pawn)
                || (pawn.TryGetComp<CompAnimalStrategyState>()?.burrowReentryAfter ?? 0)
                    > GenTicks.TicksGame
                || !SafeToJoin(pawn))
                return false;

            bool maternalWinter = ModConstants.FamilyDens
                && AnimalWinterDormancy.CanEnter(pawn)
                && AnimalWinterDormancy.MaternityDenning(pawn);
            if (maternalWinter && !CanHouseFamily(pawn))
                return false;

            foreach (Hediff h in pawn.health.hediffSet.hediffs)
                if (h is Hediff_Pregnant && h.Severity > 0.9f && !maternalWinter)
                    return false;

            bool infant = OwnInfant(pawn);
            if (FamilyNeedsDen && pawn != familyMother && !infant)
                return false;
            if (infant)
                return true;

            bool burrower = DefModExtensionCache<ModExtension_Burrower>.Has(pawn);
            bool denSpecies = AnimalOffspringShelter.For(pawn.def) == OffspringShelterMode.DEN;
            bool winterGuest = AnimalWinterDormancy.CanUseWinterDen(pawn);
            if (!burrower && !denSpecies && !winterGuest && pawn != familyMother)
                return false;
            ModExtension_Burrower digging = burrower
                ? DefModExtensionCache<ModExtension_Burrower>.Get(pawn.def) : null;
            if (digging != null && pawn.BodySize > digging.maxBodySize
                && pawn != familyMother && !winterGuest)
                return false;
            if (pawn.BodySize > entranceBodySize + 0.0001f)
                return false;

            float occupiedAdults = 0f;
            for (int i = 0; i < innerContainer.Count; i++)
                if (innerContainer[i] is Pawn resident && !OwnInfant(resident))
                {
                    if (AnimalWinterDormancy.IsDormant(resident)
                        && AnimalWinterDormancy.MaternityDenning(resident))
                        return false;
                    occupiedAdults += BodyUnits(resident);
                }
            return occupiedAdults + BodyUnits(pawn) <= totalBodyCapacity + 0.0001f;
        }

        public override bool TryAcceptThing(Thing thing, bool allowSpecialEffects = true)
        {
            if (thing is not Pawn pawn || !Accepts(pawn)
                || pawn.stances.stunner.Stunned
                || ZoologyFleeSafetyUtility.IsPawnUnderMeleeAttack(pawn))
                return false;

            bool winterEntry = AnimalWinterDormancy.CanEnter(pawn);
            bool maternalWinter = ModConstants.FamilyDens && winterEntry
                && AnimalWinterDormancy.MaternityDenning(pawn);

            for (int i = innerContainer.Count - 1; i >= 0; i--)
                if (innerContainer[i] is Pawn resident
                    && Threatens(pawn, resident))
                    ExitFamily(resident, pawn, false, true);

            Map originalMap = Map;
            IntVec3 originalPos = pawn.Spawned ? pawn.Position : Position;
            if (pawn.Spawned) pawn.DeSpawn();
            bool accepted;
            acceptingPawn = pawn;
            try
            {
                accepted = base.TryAcceptThing(thing, allowSpecialEffects) && Contains(pawn);
            }
            finally
            {
                acceptingPawn = null;
            }
            if (!accepted)
            {
                if (pawn.holdingOwner == null)
                    GenSpawn.Spawn(pawn, originalPos, originalMap);
                return false;
            }

            enteredAt = GenTicks.TicksGame;
            var state = pawn.TryGetComp<CompAnimalStrategyState>();
            state?.Remember(this);
            AnimalWinterDormancy.Enter(pawn, this, winterEntry);
            if (maternalWinter && AnimalWinterDormancy.IsDormant(pawn))
            {
                familyMother = pawn;
                if (state != null) state.familyBurrow = this;
            }
            AnimalDefense.InvalidatePrey(pawn, Map);

            if (pawn.relations != null)
                foreach (Pawn infant in pawn.relations.Children)
                    if (infant?.Spawned == true && infant.Map == Map
                        && InfantOf(infant, pawn)
                        && infant.Position.InHorDistOf(Position, Settings.searchRadius)
                        && AnimalDefense.CanUseShelter(infant)
                        && !ZoologyFleeSafetyUtility.IsPawnUnderMeleeAttack(infant)
                        && infant.CanReserveAndReach(this, PathEndMode.OnCell,
                            Danger.Some, int.MaxValue, 0))
                        infant.jobs.StartJob(JobMaker.MakeJob(
                            DefDatabase<JobDef>.GetNamed("Zoology_EnterBurrow"),
                            this, threat), JobCondition.InterruptForced);
            return true;
        }

        private static bool InFamily(Pawn mother, Pawn member)
        {
            if (mother == member) return true;
            return member != null && mother != null
                && ChildcareUtility.TryGetKnownBiologicalMother(member, out Pawn parent)
                && parent == mother;
        }

        private Pawn ResidentMotherOrSelf(Pawn animal) =>
            HasResidentMother(animal, out Pawn mother) ? mother : animal;

        internal void EnsureBirthSite(Pawn mother)
        {
            if (!CanBirthInside(mother))
                ExitFamily(mother, threat, false, false);
        }

        private void ExitFamily(Pawn animal, Pawn attacker, bool hungry, bool displaced)
        {
            // Ordinary exits must never evict dependent babies. Their need
            // meters are serviced through the shelter; only an actual attack
            // that displaces the family, excavation or destruction may release
            // infants before they reach Juvenile.
            if (!displaced && AnimalLifeStageUtility.IsAnimalInfantLifeStage(
                    animal?.ageTracker?.CurLifeStage))
                return;
            Pawn mother = ResidentMotherOrSelf(animal);
            for (int i = innerContainer.Count - 1; i >= 0; i--)
            {
                if (i >= innerContainer.Count || innerContainer[i] is not Pawn member
                    || !InFamily(mother, member)
                    || !displaced && AnimalLifeStageUtility.IsAnimalInfantLifeStage(
                        member.ageTracker?.CurLifeStage))
                    continue;
                CompAnimalStrategyState state = member.TryGetComp<CompAnimalStrategyState>();
                if (state != null)
                {
                    if (hungry) state.groundEscapeUntilFed = true;
                    if (displaced)
                        state.burrowReentryAfter =
                            GenTicks.TicksGame + Settings.forcedExitReentryDelay;
                }
                if (Drop(member) && member.Spawned)
                    AnimalDefense.StartGroundEscape(member, attacker, hungry);
            }
        }

        private bool Drop(Thing thing)
        {
            if (!Spawned || thing == null || !innerContainer.Contains(thing))
                return false;
            if (!innerContainer.TryDrop(thing, Position, Map, ThingPlaceMode.Near, out _))
                return false;
            if (thing is Pawn pawn)
            {
                AnimalWinterDormancy.Wake(pawn);
                if (pawn.TryGetComp<CompAnimalStrategyState>() is CompAnimalStrategyState state)
                    state.burrowWoundLickFinishTick = 0;
                ResumeAfterContainer(pawn);
                PredationDecisionCache.InvalidatePrey(pawn);
            }
            return true;
        }

        private static void ResumeAfterContainer(Pawn pawn)
        {
            if (pawn.Spawned && !pawn.Dead && pawn.CurJob?.def == JobDefOf.Carried)
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, false);
        }

        public void SetThreat(Pawn attacker) => threat = attacker;

        // Other mods and vanilla jobs may call Open() directly. Resolve an
        // actor only when exactly one nearby pawn is actually assigned a job
        // targeting this refuge; never blame an unrelated nearby colonist.
        public override void Open()
        {
            Pawn opener = null;
            if (Map?.mapPawns != null)
                foreach (Pawn candidate in Map.mapPawns.AllPawnsSpawned)
                {
                    Job current = candidate.CurJob;
                    if (current == null || !candidate.Position.InHorDistOf(Position, 3f)
                        || current.targetA.Thing != this
                            && current.targetB.Thing != this
                            && current.targetC.Thing != this)
                        continue;
                    if (opener != null)
                    {
                        opener = null;
                        break;
                    }
                    opener = candidate;
                }
            Excavate(opener);
        }

        public void Excavate(Pawn excavator, Pawn targetedChild = null)
        {
            if (!Spawned) return;
            NotifyAttacked(excavator, targetedChild);
            for (int i = innerContainer.Count - 1; i >= 0; i--)
            {
                if (i >= innerContainer.Count || innerContainer[i] is not Pawn resident)
                    continue;
                CompAnimalStrategyState state = resident.TryGetComp<CompAnimalStrategyState>();
                if (state != null)
                {
                    state.burrowReentryAfter =
                        GenTicks.TicksGame + Settings.forcedExitReentryDelay;
                    state.nextDefensePlan = 0;
                }
                if (Drop(resident) && resident.Spawned && !resident.Dead)
                {
                    if (resident.CurJob != null)
                        resident.jobs.EndCurrentJob(JobCondition.InterruptForced, false);
                    AnimalDefense.StartGroundEscape(resident, excavator, false);
                }
            }
            EjectContents(); // Eject non-Pawn contents too, without extra lists.

        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref threat, "refugeThreat");
            Scribe_Values.Look(ref enteredAt, "enteredAt");
            Scribe_Values.Look(ref entranceBodySize, "entranceBodySize", 2f);
            Scribe_Values.Look(ref totalBodyCapacity, "totalBodyCapacity", 2f);
            Scribe_References.Look(ref familyMother, "zoologyFamilyBurrowMother");
        }

        private static bool TickWoundLicking(Pawn pawn)
        {
            var state = pawn.TryGetComp<CompAnimalStrategyState>();
            if (state == null) return false;
            if (!AnimalWoundLickingUtility.CanUseWoundLicking(pawn)
                || pawn.health.hediffSet.BleedRateTotal <= 0f)
            {
                state.burrowWoundLickFinishTick = 0;
                return false;
            }
            if (state.burrowWoundLickFinishTick > 0
                && GenTicks.TicksGame >= state.burrowWoundLickFinishTick)
            {
                AnimalWoundLickingUtility.TryApplyWoundLicking(pawn);
                state.burrowWoundLickFinishTick = 0;
            }
            if (!AnimalWoundLickingUtility.HasLickableWounds(pawn))
            {
                state.burrowWoundLickFinishTick = 0;
                return false;
            }
            if (state.burrowWoundLickFinishTick == 0)
                state.burrowWoundLickFinishTick = GenTicks.TicksGame
                    + AnimalWoundLickingUtility.GetLickDurationTicks();
            return true;
        }

        protected override void Tick()
        {
            if (innerContainer.Count == 0 && !this.IsHashIntervalTick(250))
                return;
            base.Tick();
            for (int i = innerContainer.Count - 1; i >= 0; i--)
            {
                if (i >= innerContainer.Count || innerContainer[i] is not Pawn pawn
                    || pawn.Dead)
                    continue;

                if (!AnimalWinterDormancy.FactionAllowed(pawn)
                    && pawn.TryGetComp<CompAnimalStrategyState>()?.inWinterDormancy == true)
                {
                    Drop(pawn);
                    continue;
                }

                AnimalGameApi.TickContainedPhysiology(pawn);
                if (!pawn.Dead && !AnimalWinterDormancy.IsDormant(pawn))
                    pawn.TryGetComp<CompEggLayer>()?.CompTick();
            }

            if (!this.IsHashIntervalTick(250)) return;
            // Do not strand previously sheltered pawns after either switch
            // is disabled. Winter dormancy is independent and can retain its
            // occupants; everyone else returns to the map safely.
            bool disableShelter = !ModConstants.AdvancedThreatAvoidance
                || (!ModConstants.AdvancedReproduction && familyMother != null);
            if ((disableShelter || !ModConstants.WinterDormancy)
                && innerContainer.Count > 0)
            {
                for (int i = innerContainer.Count - 1; i >= 0; i--)
                {
                    if (innerContainer[i] is not Pawn resident) continue;
                    bool wasDormant = resident.TryGetComp<CompAnimalStrategyState>()
                        ?.inWinterDormancy == true;
                    if ((disableShelter && !AnimalWinterDormancy.IsDormant(resident))
                        || (!ModConstants.WinterDormancy && wasDormant))
                        Drop(resident);
                }
            }
            for (int i = innerContainer.Count - 1; i >= 0; i--)
            {
                if (i >= innerContainer.Count) continue;
                if (innerContainer[i] is Corpse corpse)
                    Drop(corpse);
                else if (innerContainer[i] is Pawn pawn && !pawn.Dead
                    && !AnimalLifeStageUtility.IsAnimalInfantLifeStage(pawn.ageTracker?.CurLifeStage)
                    && ChildcareUtility.TryGetKnownBiologicalMother(pawn, out Pawn parent)
                    && (parent == familyMother || Contains(parent)))
                    Drop(pawn);
            }

            // A real mother shelters and nurses her young without duplicating
            // their physiology or creating nutrients.
            for (int i = 0; i < innerContainer.Count; i++)
                if (innerContainer[i] is Pawn infant && ChildcareUtility.IsAnimalChild(infant)
                    && infant.needs?.food?.CurLevelPercentage < 0.7f
                    && ChildcareUtility.TryGetKnownBiologicalMother(infant, out Pawn mom)
                    && Contains(mom))
                    AnimalLactationUtility.SuckleFromLactatingPawn(infant, mom, 250);

            if (HasAnyContents)
            {
                if (GenTicks.TicksGame % Settings.occupiedRepairInterval < 250)
                    HitPoints = Math.Min(MaxHitPoints, HitPoints + 1);
                for (int i = innerContainer.Count - 1; i >= 0; i--)
                {
                    if (i >= innerContainer.Count || innerContainer[i] is not Pawn pawn)
                        continue;
                    bool hungry = pawn.needs?.food?.CurLevelPercentage
                        < Settings.hungerExitThreshold;
                    bool urgent = pawn.health.hediffSet.BleedRateTotal > 0f;
                    if (AnimalWinterDormancy.IsDormant(pawn))
                    {
                        if (!AnimalWinterDormancy.Winter(pawn, Map) || hungry)
                        {
                            ExitFamily(pawn, null, hungry, false);
                            continue;
                        }
                        if (!urgent) continue;
                        AnimalWinterDormancy.Wake(pawn);
                    }

                    // Treat reachable surface wounds before deciding to leave.
                    bool licking = !hungry && TickWoundLicking(pawn);
                    if (AnimalLifeStageUtility.IsAnimalInfantLifeStage(
                            pawn.ageTracker?.CurLifeStage))
                        continue;
                    if (licking) continue;

                    // Late pregnancy requires a birth site unless the family den can host it.
                    if (pawn.gender != Gender.Male
                        && pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.Pregnant)?.Severity > 0.9f
                        && !CanBirthInside(pawn))
                    {
                        ExitFamily(pawn, threat, false, false);
                        continue;
                    }

                    List<Pawn> dangers = Patch_AnimalFleeFromPredators.RefugeThreats(
                        pawn, threat);
                    AnimalDefense.RemoveUncatchablePassiveThreats(pawn, dangers);

                    // Only this adult's needs determine its voluntary exit.
                    // A hungry baby requests milk; it must not evict its mother.
                    bool rested = pawn.needs?.rest == null
                        || pawn.needs.rest.CurLevelPercentage >= 0.95f;
                    urgent = pawn.health.hediffSet.BleedRateTotal > 0f;

                    Pawn firstThreat = dangers.Count > 0 ? dangers[0] : threat;
                    if (hungry || dangers.Count == 0
                            && (urgent || rested || threat != null)
                        || urgent && GenTicks.TicksGame - enteredAt > 2500)
                        ExitFamily(pawn, firstThreat, hungry, false);
                }
                if (!HasAnyContents) threat = null;
            }
            else if (GenTicks.TicksGame % Settings.unusedDecayInterval < 250)
            {
                if (HitPoints <= 1) Destroy();
                else HitPoints--;
            }
        }

        public override void EjectContents()
        {
            if (!Spawned) return;
            for (int i = innerContainer.Count - 1; i >= 0; i--)
            {
                if (i >= innerContainer.Count) continue;
                Drop(innerContainer[i]);
            }
            threat = null;
        }

        public override string GetInspectString()
        {
            string description = base.GetInspectString();
            if (!Prefs.DevMode) return description;
            string capacity = "Zoology_BurrowCapacity".Translate(
                OccupiedBodySize.ToString("0.###"), totalBodyCapacity.ToString("0.###"));
            return string.IsNullOrWhiteSpace(description)
                ? capacity : description.TrimEnd() + "\n" + capacity;
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
                if (gizmo is not Command_Action && gizmo is not Command_Toggle)
                    yield return gizmo;
            if (Spawned)
                yield return AnimalRefugeOpening.Command(this);
            for (int i = 1; i < innerContainer.Count; i++)
                if (innerContainer[i] is Pawn occupant)
                {
                    Gizmo selection = Building.SelectContainedItemGizmo(this, occupant);
                    if (selection != null) yield return selection;
                }
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            EjectContents();
            for (int i = innerContainer.Count - 1; i >= 0; i--)
                if (innerContainer[i] is Pawn occupant
                    && !occupant.Dead && !occupant.Destroyed)
                {
                    innerContainer.Remove(occupant);
                    Find.WorldPawns.PassToWorld(
                        occupant, RimWorld.Planet.PawnDiscardDecideMode.KeepForever);
                }
            base.Destroy(mode);
        }
    }

    [HarmonyPatch(typeof(Hediff_Pregnant), nameof(Hediff_Pregnant.DoBirthSpawn))]
    internal static class Patch_BurrowBirthSite
    {
        private static bool Prepare() => ModConstants.AdvancedThreatAvoidance
            || ModConstants.WinterDormancy;

        private static void Prefix(Pawn __0)
        {
            if (__0?.Spawned != false || __0.RaceProps?.Animal != true) return;
            if (ThingOwnerUtility.GetAnyParent<Building_AnimalBurrow>(__0) is Building_AnimalBurrow den)
                den.EnsureBirthSite(__0);
        }
    }


}
