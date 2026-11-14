using System;
using System.Collections.Generic;
using RimWorld;
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

            WinterDormancyMode mode = AnimalWinterDormancy.For(pawn.def);
            return mode == WinterDormancyMode.DENNING
                || mode == WinterDormancyMode.MATERNITY_DENNING
                || mode == WinterDormancyMode.HIBERNATION
                || mode == WinterDormancyMode.WINTER_REST;
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
        public void NotifyAttacked(Pawn attacker)
        {
            Pawn mother = familyMother;
            if (!Spawned || attacker?.Spawned != true || attacker.Map != Map
                || mother == null || mother.Dead || mother.Downed
                || mother.Destroyed || mother.MapHeld != Map || attacker == mother)
                return;

            Pawn protectedChild = null;
            for (int i = 0; i < innerContainer.Count; i++)
                if (innerContainer[i] is Pawn child && OwnInfant(child))
                {
                    protectedChild = child;
                    break;
                }
            if (protectedChild == null) return;

            // A mother sheltering alongside her babies must be able to
            // confront a digger before the shelter is broken open. Only the
            // adult emerges; dependent young stay protected inside.
            if (Contains(mother) && !Drop(mother)) return;
            if (!mother.Spawned || mother.Map != Map
                || !mother.Position.InHorDistOf(Position, 30f))
                return;

            if (ZoologyNotificationUtility.IsActiveProtection(mother, attacker, protectedChild))
                return;

            ChildcareDefenseUtility.TryOrderProtection(
                mother, attacker, protectedChild, allowBurrowFallback: true);
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
                        && ProtectedByMotherFrom(incoming, resident))
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
            if (thing is not Pawn pawn || pawn.Dead || Contains(pawn)
                || (!ModConstants.AdvancedThreatAvoidance
                    && !AnimalWinterDormancy.CanUseWinterDen(pawn))
                || (!ModConstants.FamilyDens && OwnInfant(pawn))
                || !AnimalDefense.CanUseShelter(pawn)
                || (pawn.TryGetComp<CompAnimalStrategyState>()?.burrowReentryAfter ?? 0)
                    > GenTicks.TicksGame
                || !SafeToJoin(pawn))
                return false;

            foreach (Hediff h in pawn.health.hediffSet.hediffs)
                if (h is Hediff_Pregnant && h.Severity > 0.9f)
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
                    occupiedAdults += BodyUnits(resident);
            return occupiedAdults + BodyUnits(pawn) <= totalBodyCapacity + 0.0001f;
        }

        public override bool TryAcceptThing(Thing thing, bool allowSpecialEffects = true)
        {
            if (thing is not Pawn pawn || !Accepts(pawn)
                || pawn.stances.stunner.Stunned
                || ZoologyFleeSafetyUtility.IsPawnUnderMeleeAttack(pawn))
                return false;

            // For a prospective parent, remember the fact that newborns can
            // follow. Do not copy the offspring list or allocate closures.
            for (int i = innerContainer.Count - 1; i >= 0; i--)
                if (innerContainer[i] is Pawn resident
                    && Threatens(pawn, resident))
                    ExitFamily(resident, pawn, false, true);

            Map originalMap = Map;
            IntVec3 originalPos = Position;
            if (pawn.Spawned) pawn.DeSpawn();
            if (!base.TryAcceptThing(thing, allowSpecialEffects))
            {
                if (pawn.holdingOwner == null)
                    GenSpawn.Spawn(pawn, originalPos, originalMap);
                return false;
            }

            enteredAt = GenTicks.TicksGame;
            pawn.TryGetComp<CompAnimalStrategyState>()?.Remember(this);
            AnimalWinterDormancy.Enter(pawn, this);
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
                AnimalWinterDormancy.Wake(pawn, Map);
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

        public void Excavate(Pawn excavator)
        {
            if (!Spawned) return;
            NotifyAttacked(excavator);
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

        protected override void Tick()
        {
            base.Tick();
            for (int i = innerContainer.Count - 1; i >= 0; i--)
            {
                if (i >= innerContainer.Count || innerContainer[i] is not Pawn pawn
                    || pawn.Dead)
                    continue;

                bool aboutToGiveBirth = false;
                foreach (Hediff h in pawn.health.hediffSet.hediffs)
                    if (h is Hediff_Pregnant && h.Severity > 0.9f)
                    {
                        aboutToGiveBirth = true;
                        break;
                    }
                if (aboutToGiveBirth)
                {
                    ExitFamily(pawn, threat, false, false);
                    continue;
                }

                AnimalGameApi.TickContainedPhysiology(pawn);
                if (!pawn.Dead && !AnimalLifeStageUtility.IsAnimalInfantLifeStage(
                        pawn.ageTracker?.CurLifeStage)
                    && ChildcareUtility.TryGetKnownBiologicalMother(
                        pawn, out Pawn parent)
                    && (parent == familyMother || Contains(parent))
                    && Contains(pawn))
                    Drop(pawn);
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
                if (innerContainer[i] is Corpse corpse)
                    Drop(corpse);

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
                    // Dependents grow and sleep inside regardless of hunger,
                    // bleeding or the mother's outside threat. They leave
                    // naturally at the Baby -> Juvenile stage change above.
                    // This fast path also skips threat/path evaluations.
                    if (AnimalLifeStageUtility.IsAnimalInfantLifeStage(
                            pawn.ageTracker?.CurLifeStage))
                        continue;

                    List<Pawn> dangers = Patch_AnimalFleeFromPredators.RefugeThreats(
                        pawn, threat);
                    AnimalDefense.RemoveUncatchablePassiveThreats(pawn, dangers);

                    // Only this adult's needs determine its voluntary exit.
                    // A hungry baby requests milk; it must not evict its mother.
                    bool hungry = pawn.needs?.food?.CurLevelPercentage
                        < Settings.hungerExitThreshold;
                    bool rested = pawn.needs?.rest == null
                        || pawn.needs.rest.CurLevelPercentage >= 0.95f;
                    bool urgent = pawn.health.hediffSet.BleedRateTotal > 0f;
                    if (!urgent)
                        foreach (Hediff h in pawn.health.hediffSet.hediffs)
                            if (h is Hediff_Pregnant && h.Severity > 0.9f)
                            {
                                urgent = true;
                                break;
                            }

                    Pawn firstThreat = dangers.Count > 0 ? dangers[0] : threat;
                    if (AnimalWinterDormancy.IsDormant(pawn))
                    {
                        bool rangedThreat = false;
                        for (int j = 0; j < dangers.Count; j++)
                            if (AnimalDefense.IsRanged(dangers[j]))
                            {
                                rangedThreat = true;
                                break;
                            }
                        if (!AnimalWinterDormancy.Winter(Map) || hungry || urgent
                            || rangedThreat)
                            ExitFamily(pawn, firstThreat, hungry, false);
                        continue;
                    }
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

        private void ChooseOpener()
        {
            if (!Spawned || Map?.mapPawns == null) return;
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (Pawn colonist in Map.mapPawns.FreeColonistsSpawned)
            {
                if (colonist.Dead || colonist.Downed
                    || colonist.health?.capacities?.CapableOf(
                        PawnCapacityDefOf.Manipulation) != true
                    || !colonist.CanReserveAndReach(this, PathEndMode.Touch, Danger.Some))
                    continue;
                Pawn opener = colonist;
                options.Add(new FloatMenuOption(opener.LabelShortCap, () =>
                {
                    if (!Spawned || !opener.Spawned
                        || !opener.CanReserveAndReach(this, PathEndMode.Touch, Danger.Some))
                        return;
                    Job job = JobMaker.MakeJob(
                        DefDatabase<JobDef>.GetNamed("Zoology_OpenAnimalBurrow"), this);
                    opener.jobs.TryTakeOrderedJob(job);
                }));
            }
            if (options.Count > 0)
                Find.WindowStack.Add(new FloatMenu(options));
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            // Suppress the inherited no-actor Open command. Every deliberate
            // opening has a real colonist and a corresponding job.
            foreach (Gizmo gizmo in base.GetGizmos())
                if (gizmo is not Command_Action)
                    yield return gizmo;
            if (Spawned && Map?.mapPawns != null)
                yield return new Command_Action
                {
                    defaultLabel = "Zoology_OpenBurrow_Label".Translate(),
                    defaultDesc = "Zoology_OpenBurrow_Desc".Translate(),
                    action = ChooseOpener
                };
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

    // A concrete player pawn performs the opening, allowing the mother to
    // retaliate against the correct instigator instead of becoming angry at
    // an unrelated person.
    public sealed class JobDriver_OpenAnimalBurrow : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) =>
            pawn.Reserve(TargetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Do(() =>
                AnimalBurrowHunt.BeginOpening(pawn, TargetA.Thing));
            Toil opening = Toils_General.Wait(100, TargetIndex.A)
                .WithProgressBarToilDelay(TargetIndex.A);
            opening.AddPreTickAction(() =>
            {
                if (pawn.IsHashIntervalTick(60))
                    AnimalBurrowHunt.BeginOpening(pawn, TargetA.Thing);
            });
            yield return opening;
            yield return Toils_General.Do(() =>
                (TargetA.Thing as Building_AnimalBurrow)?.Excavate(pawn));
        }
    }
}
