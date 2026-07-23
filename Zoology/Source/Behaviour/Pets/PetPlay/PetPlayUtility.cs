using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    public enum PetPlayKind
    {
        Canine,
        OtherPet
    }

    [DefOf]
    public static class ZoologyPetPlayDefOf
    {
        public static JobDef Zoology_WalkCanine;
        public static JobDef Zoology_PlayFetchCanine;
        public static JobDef Zoology_PlayWithPet;
        public static JobDef Zoology_PetFetch;
        public static JobDef Zoology_PetWait;
        public static JobDef Zoology_PetChaseToy;

        static ZoologyPetPlayDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(ZoologyPetPlayDefOf));
        }
    }

    public sealed class PetPlayMapComponent : MapComponent
    {
        private const int CacheLifetimeTicks = 1200;

        private readonly List<Thing> candidatePets = new List<Thing>();
        private int refreshAtTick = -1;

        public PetPlayMapComponent(Map map) : base(map)
        {
        }

        public List<Thing> GetCandidatePets()
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            if (refreshAtTick < 0 || now >= refreshAtTick)
            {
                Refresh(now);
            }

            return candidatePets;
        }

        private void Refresh(int now)
        {
            candidatePets.Clear();

            IReadOnlyList<Pawn> spawnedPawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < spawnedPawns.Count; i++)
            {
                Pawn animal = spawnedPawns[i];
                RaceProperties race = animal?.RaceProps;
                if (race != null && race.Animal && race.petness > 0f)
                {
                    candidatePets.Add(animal);
                }
            }

            refreshAtTick = now + CacheLifetimeTicks;
        }
    }

    public static class PetPlayUtility
    {
        public const float MaximumPetDistance = 30f;
        public const float BondChanceOnPlayStart = 0.007f;

        private const float MinimumConsciousness = 0.6f;
        private const float MinimumMoving = 0.7f;
        private const float ToyMinRadiusSquared = 4f;
        private const float ToyRadius = 6f;

        private static readonly string[] JoyGiverDefNames =
        {
            "Zoology_WantToWalkCanine",
            "Zoology_WantToPlayFetchCanine",
            "Zoology_WantToPlayWithPet"
        };

        private static readonly float[] JoyGiverBaseChances = { 4f, 2f, 4f };

        public static bool Enabled
        {
            get
            {
                ZoologyModSettings settings = ZoologyMod.Settings ?? ZoologyModSettings.Instance;
                return settings == null
                    ? ModConstants.DefaultEnablePetPlay
                    : !settings.DisableAllRuntimePatches && settings.EnablePetPlay;
            }
        }

        public static float MaximumWildness
        {
            get
            {
                ZoologyModSettings settings = ZoologyMod.Settings ?? ZoologyModSettings.Instance;
                return settings?.PetPlayMaxWildness ?? ModConstants.DefaultPetPlayMaxWildness;
            }
        }

        public static void SyncJoyGiverDefAvailability(ZoologyModSettings settings)
        {
            bool enabled = settings == null
                ? ModConstants.DefaultEnablePetPlay
                : !settings.DisableAllRuntimePatches && settings.EnablePetPlay;

            for (int i = 0; i < JoyGiverDefNames.Length; i++)
            {
                JoyGiverDef joyGiver = DefDatabase<JoyGiverDef>.GetNamedSilentFail(JoyGiverDefNames[i]);
                if (joyGiver != null)
                {
                    joyGiver.baseChance = enabled ? JoyGiverBaseChances[i] : 0f;
                }
            }
        }

        public static Pawn FindAvailablePet(Pawn pawn, PetPlayKind kind)
        {
            if (!CanPawnPlay(pawn, false, kind == PetPlayKind.Canine))
            {
                return null;
            }

            Map map = pawn.Map;
            PetPlayMapComponent component = map?.GetComponent<PetPlayMapComponent>();
            List<Thing> candidates = component?.GetCandidatePets();
            if (candidates == null || candidates.Count == 0)
            {
                return null;
            }

            bool canine = kind == PetPlayKind.Canine;
            return GenClosest.ClosestThing_Global(
                pawn.Position,
                candidates,
                MaximumPetDistance,
                thing =>
                {
                    Pawn animal = thing as Pawn;
                    return CanAnimalPlay(pawn, animal, canine, false)
                        && pawn.CanReserveAndReach(animal, PathEndMode.ClosestTouch, Danger.None);
                }) as Pawn;
        }

        public static bool CanContinuePlaying(Pawn pawn, Pawn animal, bool canine, bool outdoors)
        {
            return Enabled
                && CanPawnPlay(pawn, true, outdoors)
                && CanAnimalPlay(pawn, animal, canine, true);
        }

        public static bool TryDevelopBondOnPlayStart(Pawn colonist, Pawn animal)
        {
            return colonist?.RaceProps?.Humanlike == true
                && colonist.story != null
                && animal != null
                && RelationsUtility.TryDevelopBondRelation(colonist, animal, BondChanceOnPlayStart);
        }

        public static bool TryFindOutdoorWalkingPath(Pawn pawn, Pawn animal, out List<LocalTargetInfo> path)
        {
            path = null;
            if (pawn?.Map == null || animal?.Map != pawn.Map)
            {
                return false;
            }

            IntVec3 destination;
            if (!TryFindOutdoorDestination(pawn, animal, out destination)
                || !WalkPathFinder.TryFindWalkPath(pawn, destination, out List<IntVec3> cells)
                || cells == null
                || cells.Count == 0)
            {
                return false;
            }

            path = new List<LocalTargetInfo>(cells.Count);
            for (int i = 0; i < cells.Count; i++)
            {
                path.Add(cells[i]);
            }

            return true;
        }

        public static bool TryFindToyCell(Pawn pawn, Pawn animal, out IntVec3 result)
        {
            result = IntVec3.Invalid;
            Map map = pawn?.Map;
            if (map == null || animal?.Map != map)
            {
                return false;
            }

            int count = GenRadial.NumCellsInRadius(ToyRadius);
            int start = Rand.Range(0, count);
            bool keepUnderRoof = pawn.Position.Roofed(map);

            for (int offset = 0; offset < count; offset++)
            {
                IntVec3 radialOffset = GenRadial.RadialPattern[(start + offset) % count];
                if (radialOffset.LengthHorizontalSquared < ToyMinRadiusSquared)
                {
                    continue;
                }

                IntVec3 candidate = pawn.Position + radialOffset;
                if (!candidate.InBounds(map)
                    || !candidate.Standable(map)
                    || candidate.IsForbidden(pawn)
                    || candidate.IsForbidden(animal)
                    || PawnUtility.KnownDangerAt(candidate, map, pawn)
                    || (keepUnderRoof && !candidate.Roofed(map))
                    || !GenSight.LineOfSight(pawn.Position, candidate, map)
                    || !animal.CanReach(candidate, PathEndMode.OnCell, Danger.None))
                {
                    continue;
                }

                result = candidate;
                return true;
            }

            return false;
        }

        private static bool CanPawnPlay(Pawn pawn, bool alreadyPlaying, bool outdoors)
        {
            if (!Enabled
                || pawn == null
                || pawn.Destroyed
                || pawn.Dead
                || pawn.Downed
                || !pawn.Spawned
                || pawn.Map == null
                || !pawn.IsColonist
                || pawn.Faction != Faction.OfPlayer
                || !HasRequiredCapacities(pawn))
            {
                return false;
            }

            if (outdoors && !JoyUtility.EnjoyableOutsideNow(pawn))
            {
                return false;
            }

            return alreadyPlaying || !PawnUtility.WillSoonHaveBasicNeed(pawn);
        }

        private static bool CanAnimalPlay(Pawn pawn, Pawn animal, bool canine, bool alreadyPlaying)
        {
            RaceProperties race = animal?.RaceProps;
            if (race == null
                || !race.Animal
                || race.petness <= 0f
                || (race.animalType == AnimalType.Canine) != canine
                || animal.def.GetStatValueAbstract(StatDefOf.Wildness) > MaximumWildness + 0.0001f
                || animal.Destroyed
                || animal.Dead
                || animal.Downed
                || !animal.Spawned
                || animal.Map == null
                || animal.Map != pawn?.Map
                || animal.Faction != pawn.Faction
                || animal.Faction != Faction.OfPlayer
                || animal.InMentalState
                || !HasRequiredCapacities(animal))
            {
                return false;
            }

            if (alreadyPlaying)
            {
                return true;
            }

            return !PawnUtility.WillSoonHaveBasicNeed(animal)
                && animal.GetTimeAssignment() == TimeAssignmentDefOf.Anything
                && animal.carryTracker?.CarriedThing == null
                && (animal.mindState?.IsIdle ?? false);
        }

        private static bool HasRequiredCapacities(Pawn pawn)
        {
            PawnCapacitiesHandler capacities = pawn?.health?.capacities;
            return capacities != null
                && capacities.GetLevel(PawnCapacityDefOf.Consciousness) >= MinimumConsciousness
                && capacities.GetLevel(PawnCapacityDefOf.Moving) >= MinimumMoving;
        }

        private static bool TryFindOutdoorDestination(Pawn pawn, Pawn animal, out IntVec3 destination)
        {
            destination = IntVec3.Invalid;
            Region startRegion = animal.GetRegion();
            if (startRegion == null)
            {
                return false;
            }

            IntVec3 potentialDestination = IntVec3.Invalid;
            Map map = animal.Map;

            bool CellGood(IntVec3 cell)
            {
                return !PawnUtility.KnownDangerAt(cell, map, pawn)
                    && !cell.GetTerrain(map).avoidWander
                    && cell.Standable(map)
                    && !cell.Roofed(map);
            }

            bool RegionGood(Region region)
            {
                return region.Room != null
                    && region.Room.PsychologicallyOutdoors
                    && !region.IsForbiddenEntirely(animal)
                    && !region.IsForbiddenEntirely(pawn)
                    && region.TryFindRandomCellInRegionUnforbidden(animal, CellGood, out potentialDestination)
                    && !potentialDestination.IsForbidden(pawn);
            }

            bool found = CellFinder.TryFindClosestRegionWith(
                startRegion,
                TraverseParms.For(animal),
                RegionGood,
                100,
                out Region _);
            destination = potentialDestination;
            return found && destination.IsValid;
        }
    }

    public abstract class JoyGiver_PetPlayBase : JoyGiver
    {
        protected Job TryMakeOutdoorJob(Pawn pawn, PetPlayKind kind)
        {
            Pawn animal = PetPlayUtility.FindAvailablePet(pawn, kind);
            if (animal == null
                || !PetPlayUtility.TryFindOutdoorWalkingPath(pawn, animal, out List<LocalTargetInfo> path))
            {
                return null;
            }

            Job job = JobMaker.MakeJob(def.jobDef, animal);
            job.targetQueueB = path;
            return job;
        }
    }

    public sealed class JoyGiver_WalkCanine : JoyGiver_PetPlayBase
    {
        public override Job TryGiveJob(Pawn pawn)
        {
            return TryMakeOutdoorJob(pawn, PetPlayKind.Canine);
        }
    }

    public sealed class JoyGiver_PlayFetchCanine : JoyGiver_PetPlayBase
    {
        public override Job TryGiveJob(Pawn pawn)
        {
            return TryMakeOutdoorJob(pawn, PetPlayKind.Canine);
        }
    }

    public sealed class JoyGiver_PlayWithPet : JoyGiver
    {
        public override Job TryGiveJob(Pawn pawn)
        {
            Pawn animal = PetPlayUtility.FindAvailablePet(pawn, PetPlayKind.OtherPet);
            return animal == null ? null : JobMaker.MakeJob(def.jobDef, animal);
        }
    }
}
