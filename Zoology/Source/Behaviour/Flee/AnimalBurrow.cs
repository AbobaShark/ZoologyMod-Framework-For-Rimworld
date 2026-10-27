using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    public class Building_AnimalBurrow : Building_Casket
    {
        private Pawn threat;
        private int enteredAt;
        private float entranceBodySize = 2f, totalBodyCapacity = 2f;
        private ThingDef creatorSpecies;
        private List<Pawn> expectedYoung = new List<Pawn>();
        public static AnimalRefugeDef Settings => DefDatabase<AnimalRefugeDef>.GetNamed("Zoology_AnimalRefuges");
        public IEnumerable<Pawn> Occupants => innerContainer.OfType<Pawn>();
        public float TotalBodyCapacity => totalBodyCapacity;
        public float OccupiedBodySize => Occupants.Sum(BodyUnits);
        private static float BodyUnits(Pawn pawn) => Math.Max(0.001f, pawn.BodySize);
        public bool Contains(Pawn pawn) => pawn != null && innerContainer.Contains(pawn);
        public override AcceptanceReport ClaimableBy(Faction fac) => false;

        public void InitializeFor(Pawn creator)
        {
            creatorSpecies = creator.def;
            entranceBodySize = BodyUnits(creator);
            totalBodyCapacity = entranceBodySize * (creator.RaceProps.herdAnimal ? Settings.socialCapacityMultiplier : 1f);
        }
        private IEnumerable<Pawn> FollowingYoung(Pawn mother)
        {
            if (mother.gender != Gender.Female || mother.relations == null) yield break;
            // Relations expose the reverse child links; no map-wide search for babies.
            foreach (Pawn child in mother.relations.Children)
                if (child != null && !child.Dead && ChildcareUtility.IsAnimalChild(child)
                    && child.MapHeld == Map && (Contains(child) || (child.Spawned && child.Position.InHorDistOf(Position, Settings.searchRadius)))
                    && DefModExtensionCache<ModExtension_Burrower>.Has(child)) yield return child;
        }
        private float FamilyCapacity(Pawn incoming)
        {
            if (incoming.gender != Gender.Female) return totalBodyCapacity;
            var young = FollowingYoung(incoming).ToList();
            if (young.Count == 0) return totalBodyCapacity;
            return Math.Max(totalBodyCapacity, OccupiedBodySize + BodyUnits(incoming)
                + young.Where(p => !Contains(p)).Sum(BodyUnits));
        }
        private bool HasResidentMother(Pawn child, out Pawn mother)
        {
            mother = null;
            return ChildcareUtility.IsAnimalChild(child) && ChildcareUtility.TryGetKnownBiologicalMother(child, out mother) && Contains(mother);
        }
        internal bool ProtectedByMotherFrom(Pawn predator, Pawn child)
        {
            return HasResidentMother(child, out Pawn mother)
                && !Patch_IsAcceptablePreyForPredator.IsSuitableWithoutYoungProtection(predator, mother);
        }
        private static bool Threatens(Pawn predator, Pawn prey) => !prey.Dead
            && Patch_IsAcceptablePreyForPredator.IsSuitableWithoutYoungProtection(predator, prey);
        private bool SafeToJoin(Pawn incoming)
        {
            foreach (Pawn resident in Occupants)
            {
                if (resident == incoming || resident.Dead) continue;
                if (Threatens(resident, incoming)) return false;
                if (Threatens(incoming, resident) && ProtectedByMotherFrom(incoming, resident)) return false;
                // A mother must not enter and draw her babies into a resident predator's refuge.
                foreach (Pawn child in FollowingYoung(incoming))
                    if (Threatens(resident, child)) return false;
            }
            return true;
        }
        private void PruneExpectedYoung()
        {
            expectedYoung.RemoveAll(p => p == null || p.Dead || p.Destroyed || Contains(p) || !p.Spawned || p.Map != Map
                || !p.Position.InHorDistOf(Position, Settings.searchRadius)
                || !ChildcareUtility.TryGetKnownBiologicalMother(p, out Pawn mother) || !Contains(mother));
        }
        public override bool Accepts(Thing thing)
        {
            if (!(thing is Pawn pawn) || pawn.Dead || Contains(pawn) || !DefModExtensionCache<ModExtension_Burrower>.Has(pawn)
                || !AnimalDefense.CanUseShelter(pawn)
                || (pawn.TryGetComp<CompAnimalStrategyState>()?.burrowReentryAfter ?? 0) > GenTicks.TicksGame
                || pawn.health.hediffSet.hediffs.Any(h => h is Hediff_Pregnant && h.Severity > 0.9f)
                || pawn.BodySize > Math.Min(Settings.maxBurrowBodySize, DefModExtensionCache<ModExtension_Burrower>.Get(pawn.def).maxBodySize)
                || !SafeToJoin(pawn)) return false;
            float capacity = FamilyCapacity(pawn);
            bool expandingFamily = capacity > totalBodyCapacity;
            if (pawn.BodySize > entranceBodySize + 0.0001f && !expandingFamily) return false;
            PruneExpectedYoung();
            float retained = Occupants.Where(p => !Threatens(pawn, p)).Sum(BodyUnits);
            float reserved = expectedYoung.Where(p => p != pawn).Sum(BodyUnits);
            return retained + reserved + BodyUnits(pawn) <= capacity + 0.0001f;
        }
        public override bool TryAcceptThing(Thing thing, bool allowSpecialEffects = true)
        {
            if (!(thing is Pawn pawn) || !Accepts(pawn) || pawn.stances.stunner.Stunned
                || ZoologyFleeSafetyUtility.IsPawnUnderMeleeAttack(pawn)) return false;
            var followers = FollowingYoung(pawn).Where(p => p.Spawned).ToList();
            float capacity = FamilyCapacity(pawn);
            if (followers.Count > 0)
            {
                totalBodyCapacity = capacity;
                entranceBodySize = Math.Max(entranceBodySize, BodyUnits(pawn));
            }
            // Reject unsafe joins before ejecting anyone. Only the entrant's real prey leave.
            foreach (Pawn resident in Occupants.Where(p => Threatens(pawn, p)).ToList())
                ExitFamily(resident, pawn, false, true);
            if (pawn.Spawned) pawn.DeSpawn();
            if (!base.TryAcceptThing(thing, allowSpecialEffects))
            {
                if (pawn.holdingOwner == null) GenSpawn.Spawn(pawn, Position, Map);
                return false;
            }
            enteredAt = GenTicks.TicksGame;
            pawn.TryGetComp<CompAnimalStrategyState>()?.Remember(this);
            AnimalDefense.InvalidatePrey(pawn, Map);
            foreach (Pawn child in followers)
            {
                if (expectedYoung.Contains(child) || !AnimalDefense.CanUseShelter(child)
                    || ZoologyFleeSafetyUtility.IsPawnUnderMeleeAttack(child)
                    || !child.CanReserveAndReach(this, PathEndMode.OnCell, Danger.Some, int.MaxValue, 0)) continue;
                expectedYoung.Add(child);
                child.jobs.StartJob(JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Zoology_EnterBurrow"), this, threat), JobCondition.InterruptForced);
            }
            return true;
        }
        private List<Pawn> FamilyInside(Pawn pawn)
        {
            Pawn mother = HasResidentMother(pawn, out Pawn found) ? found : pawn;
            var family = FollowingYoung(mother).Where(Contains).ToList();
            if (Contains(mother)) family.Insert(0, mother);
            if (!family.Contains(pawn) && Contains(pawn)) family.Add(pawn);
            return family;
        }
        private void ExitFamily(Pawn pawn, Pawn source, bool hungry, bool displaced)
        {
            foreach (Pawn member in FamilyInside(pawn))
            {
                var state = member.TryGetComp<CompAnimalStrategyState>();
                if (hungry) state.groundEscapeUntilFed = true;
                if (displaced) state.burrowReentryAfter = GenTicks.TicksGame + Settings.forcedExitReentryDelay;
                Drop(member);
                if (member.Spawned) AnimalDefense.StartGroundEscape(member, source, hungry);
            }
        }
        private void Drop(Thing thing)
        {
            innerContainer.TryDrop(thing, Position, Map, ThingPlaceMode.Near, out _);
            if (thing is Pawn pawn) ResumeAfterContainer(pawn);
            PredationDecisionCache.InvalidatePrey(thing as Pawn);
        }
        private static void ResumeAfterContainer(Pawn pawn)
        {
            if (pawn.Spawned && !pawn.Dead && pawn.CurJob?.def == JobDefOf.Carried)
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, false);
        }
        public void SetThreat(Pawn source) { threat = source; }
        public override void Open() => Excavate(null);
        public void Excavate(Pawn excavator)
        {
            var occupants = Occupants.ToList();
            foreach (Pawn occupant in occupants)
            {
                var state = occupant.TryGetComp<CompAnimalStrategyState>();
                state.burrowReentryAfter = GenTicks.TicksGame + Settings.forcedExitReentryDelay;
                state.nextDefensePlan = 0;
            }
            EjectContents();
            foreach (Pawn occupant in occupants)
                if (occupant.Spawned && !occupant.Dead)
                {
                    if (occupant.CurJob != null) occupant.jobs.EndCurrentJob(JobCondition.InterruptForced, false);
                    AnimalDefense.StartGroundEscape(occupant, excavator, false);
                }
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref threat, "refugeThreat");
            Scribe_Values.Look(ref enteredAt, "enteredAt");
            Scribe_Values.Look(ref entranceBodySize, "entranceBodySize", 2f);
            Scribe_Values.Look(ref totalBodyCapacity, "totalBodyCapacity", 2f);
            Scribe_Defs.Look(ref creatorSpecies, "creatorSpecies");
            Scribe_Collections.Look(ref expectedYoung, "expectedYoung", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) expectedYoung = expectedYoung ?? new List<Pawn>();
        }
        protected override void Tick()
        {
            base.Tick();
            // Reverse iteration tolerates a dying pawn being replaced with its corpse.
            for (int i = innerContainer.Count - 1; i >= 0; i--)
            {
                if (i >= innerContainer.Count || !(innerContainer[i] is Pawn pawn) || pawn.Dead) continue;
                // Leave before the health tracker can trigger birth outside a spawned map pawn.
                // This also covers short gestation periods between the slower refuge decisions.
                bool needsBirthExit = false;
                foreach (Hediff condition in pawn.health.hediffSet.hediffs)
                    if (condition is Hediff_Pregnant && condition.Severity > .9f) { needsBirthExit = true; break; }
                if (needsBirthExit) { ExitFamily(pawn, threat, false, false); continue; }
                AnimalGameApi.TickContainedPhysiology(pawn);
            }
            if (!this.IsHashIntervalTick(250)) return;
            PruneExpectedYoung();
            foreach (Corpse corpse in innerContainer.OfType<Corpse>().ToList()) Drop(corpse);
            if (HasAnyContents)
            {
                if (GenTicks.TicksGame % Settings.occupiedRepairInterval < 250) HitPoints = Math.Min(MaxHitPoints, HitPoints + 1);
                foreach (Pawn pawn in Occupants.ToList())
                {
                    if (!Contains(pawn)) continue;
                    List<Pawn> dangers = Patch_AnimalFleeFromPredators.RefugeThreats(pawn, threat);
                    var family = FamilyInside(pawn);
                    bool hungry = family.Any(p => p.needs?.food?.CurLevelPercentage < Settings.hungerExitThreshold);
                    bool urgent = pawn.health.hediffSet.BleedRateTotal > 0f
                        || pawn.health.hediffSet.hediffs.Any(h => h is Hediff_Pregnant && h.Severity > 0.9f);
                    bool rested = family.All(p => p.needs?.rest == null || p.needs.rest.CurLevelPercentage >= 0.95f);
                    if (hungry || (dangers.Count == 0 && (urgent || rested || threat != null))
                        || (urgent && GenTicks.TicksGame - enteredAt > 2500))
                        ExitFamily(pawn, dangers.FirstOrDefault() ?? threat, hungry, false);
                }
                if (!HasAnyContents) threat = null;
            }
            else if (GenTicks.TicksGame % Settings.unusedDecayInterval < 250)
            {
                if (HitPoints <= 1) Destroy(); else HitPoints--;
            }
        }
        public override void EjectContents()
        {
            if (!Spawned) return;
            var released = Occupants.ToList();
            foreach (Pawn occupant in released) PredationDecisionCache.InvalidatePrey(occupant);
            innerContainer.TryDropAll(Position, Map, ThingPlaceMode.Near);
            foreach (Pawn occupant in released) ResumeAfterContainer(occupant);
            expectedYoung.Clear(); threat = null;
        }
        public override string GetInspectString() => base.GetInspectString() + "\n"
            + "Zoology_BurrowCapacity".Translate(OccupiedBodySize.ToString("0.###"), totalBodyCapacity.ToString("0.###"));
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos()) yield return gizmo;
            foreach (Pawn pawn in Occupants.Skip(1))
            {
                Gizmo gizmo = Building.SelectContainedItemGizmo(this, pawn);
                if (gizmo != null) yield return gizmo;
            }
        }
        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            EjectContents();
            foreach (Pawn pawn in Occupants.ToList())
            {
                innerContainer.Remove(pawn);
                Find.WorldPawns.PassToWorld(pawn, RimWorld.Planet.PawnDiscardDecideMode.KeepForever);
            }
            base.Destroy(mode);
        }
    }
}
