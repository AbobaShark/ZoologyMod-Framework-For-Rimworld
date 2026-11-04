#if RIMWORLD17
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    // The barn holds individual pawns and real food in a saved ThingOwner.
    public sealed partial class Building_AnimalBarn : Building, IThingHolderTickable, IStoreSettingsParent
    {
        // Storage capacity is based on the race's fully grown base body size.
        // Life-stage growth must not increase the reserved capacity.
        public static float MaximumAnimalBodySize =>
            ModConstants.Settings?.AnimalBarnMaximumBodySize
                ?? ModConstants.DefaultAnimalBarnMaximumBodySize;

        private static float BodySizeOf(Pawn pawn)
        {
            if (pawn == null) return 0f;
            float value = pawn.def?.race?.baseBodySize ?? 0f;
            return float.IsNaN(value) || float.IsInfinity(value) || value <= 0f
                ? float.PositiveInfinity : value;
        }
        // Three vanilla 2x1 shelves, six stacks on each shelf.
        public const int MaximumFoodStacks = 18;
        private const int FeedingIntervalTicks = 250;
        private const int ServiceCheckIntervalTicks = 250;
        private const int ServiceWaitTicks = 15000;
        private const int ServiceRetryTicks = 60000;
        private const int MaximumOutsideForService = 2;

        public static ThingDef BarnDef => DefDatabase<ThingDef>.GetNamedSilentFail("Zoology_AnimalBarn");
        public static bool Enabled => ModConstants.Settings != null
            && !ModConstants.Settings.DisableAllRuntimePatches
            && ModConstants.Settings.EnableAnimalBarn;

        private ThingOwner<Thing> contents;
        private ThingFilter animalFilter = new ThingFilter();
        private StorageSettings nutritionSettings;
        private bool filtersInitialized;
        private List<Pawn> manuallyRequestedAnimals = new List<Pawn>();
        private readonly List<Pawn> newlyBorn = new List<Pawn>(8);
        private bool recoverServiceAfterLoad;
        private Dictionary<Pawn, int> releasedForService = new Dictionary<Pawn, int>();
        // Numeric IDs are per def; the full ThingID avoids cross-species cooldowns.
        private Dictionary<string, int> nextServiceTickByPawnId = new Dictionary<string, int>();

        public Building_AnimalBarn()
        {
            contents = new ThingOwner<Thing>(this, false, LookMode.Deep);
        }

        public override void PostMake()
        {
            base.PostMake();
            // ThingMaker assigns def after the constructor and before PostMake.
            // StorageSettings(this) consults GetParentStoreSettings(), so it
            // must never be constructed while def is still null.
            nutritionSettings = new StorageSettings(this);
        }

        public ThingFilter AnimalFilter => animalFilter;
        public StorageSettings GetStoreSettings() => nutritionSettings;
        // A fixed *available* food list keeps corpses and nonfood out of the
        // storage tab while allowing the player to enable any real feed.
        // Construct it once after DefDatabase initialization.
        private static StorageSettings availableFoodSettings;

        public StorageSettings GetParentStoreSettings()
        {
            if (availableFoodSettings != null) return availableFoodSettings;
            var allowed = new StorageSettings();
            allowed.filter.SetDisallowAll();
            foreach (ThingDef item in DefDatabase<ThingDef>.AllDefs)
                if (IsPermittedFoodDef(item))
                    allowed.filter.SetAllow(item, true);
            availableFoodSettings = allowed;
            return allowed;
        }

        private static bool IsPermittedFoodDef(ThingDef item)
        {
            return item != null && item.category == ThingCategory.Item
                && item.IsNutritionGivingIngestible && !item.IsDrug
                && item.thingClass != null
                && !typeof(Corpse).IsAssignableFrom(item.thingClass)
                && !item.HasComp(typeof(CompHatcher));
        }
        public bool StorageTabVisible => true;
        public void Notify_SettingsChanged() { }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);

            // Developer-spawned barns may lack an owner; treat them as colony
            // infrastructure on the player's home map.
            if (Faction == null && map.IsPlayerHome && def.CanHaveFaction)
            {
                SetFaction(Faction.OfPlayer);
            }

            RefreshDisplayedAnimals();
            recoverServiceAfterLoad = respawningAfterLoad;

            if (filtersInitialized)
            {
                return;
            }

            animalFilter ??= new ThingFilter();
            animalFilter.CopyAllowancesFrom(AnimalPenUtility.GetFixedAnimalFilter());
            nutritionSettings ??= new StorageSettings(this);
            // Only hay and kibble are selected by default. All other
            // eligible feed remains selectable in the vanilla storage tab.
            nutritionSettings.filter.SetDisallowAll();
            nutritionSettings.filter.SetAllow(ThingDefOf.Hay, true);
            nutritionSettings.filter.SetAllow(ThingDefOf.Kibble, true);

            filtersInitialized = true;
        }

        // Expose the real contents to the vanilla-style cargo inspector.
        internal IList<Thing> StoredContents => contents;
        internal IReadOnlyList<Pawn> PendingManualIntake => manuallyRequestedAnimals;

        // 1.7 Thing.DoTick otherwise ticks all holders recursively after Tick,
        // causing duplicate physiology, food rot and unwanted Pawn AI ticks.
        public bool ShouldTickContents => false;
        public ThingOwner GetDirectlyHeldThings() => contents;

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, contents);
        }

        public int AnimalCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < contents.Count; i++)
                {
                    if (contents[i] is Pawn) count++;
                }

                return count;
            }
        }

        public float OccupiedAnimalBodySize
        {
            get
            {
                float total = 0f;
                for (int i = 0; i < contents.Count; i++)
                    if (contents[i] is Pawn pawn) total += BodySizeOf(pawn);
                return total;
            }
        }

        // Temporarily serviced residents still reserve their race-size space
        // until readmitted or explicitly removed from service tracking.
        private float ServiceBodySizeReservedForOthers(Pawn incoming)
        {
            float total = 0f;
            foreach (Pawn animal in releasedForService.Keys)
                if (animal != incoming && animal != null && animal.Spawned
                    && animal.Map == Map && !animal.Destroyed && !animal.Dead)
                    total += BodySizeOf(animal);
            return total;
        }

        public IEnumerable<Pawn> Animals
        {
            get
            {
                for (int i = 0; i < contents.Count; i++)
                {
                    if (contents[i] is Pawn pawn) yield return pawn;
                }
            }
        }

        // Animals are kept in the same ThingOwner but do not occupy food
        // storage. Every other stored item occupies one stack slot.
        public int StoredFoodStacks
        {
            get
            {
                int stacks = 0;
                for (int i = 0; i < contents.Count; i++)
                {
                    if (!(contents[i] is Pawn) && !contents[i].Destroyed)
                    {
                        stacks++;
                    }
                }

                return stacks;
            }
        }

        public bool ContainsAnimal(Pawn animal) => animal != null && contents.Contains(animal);

        public bool IsManualIntakeRequested(Pawn animal)
        {
            return animal != null && manuallyRequestedAnimals.Contains(animal);
        }

        public bool RequestManualIntake(Pawn animal)
        {
            if (!CanAcceptAnimal(animal, ignoreAutomaticRules: true)
                || manuallyRequestedAnimals.Contains(animal))
                return false;

            float planned = OccupiedAnimalBodySize + ServiceBodySizeReservedForOthers(null)
                + BodySizeOf(animal);
            foreach (Pawn requested in manuallyRequestedAnimals)
                if (requested != null && !requested.Destroyed && requested.Spawned)
                    planned += BodySizeOf(requested);
            if (planned > MaximumAnimalBodySize + 0.0001f)
                return false;

            manuallyRequestedAnimals.Add(animal);
            return true;
        }

        public void CancelManualIntake(Pawn animal)
        {
            manuallyRequestedAnimals.Remove(animal);
        }


        public void RegisterNewborn(Pawn baby)
        {
            if (baby != null && !baby.Dead && !baby.Destroyed && !newlyBorn.Contains(baby))
            {
                newlyBorn.Add(baby);
            }
        }

        private void StoreNewbornLitter(Pawn mother)
        {
            if (newlyBorn.Count == 0)
            {
                return;
            }

            // Preserve the family as a unit. A litter that would overflow
            // the barn, or includes an animal excluded by the player filter,
            // stays on the map together with its mother.
            float planned = OccupiedAnimalBodySize + ServiceBodySizeReservedForOthers(null);
            bool canStoreAll = true;
            for (int i = 0; i < newlyBorn.Count; i++)
            {
                Pawn baby = newlyBorn[i];
                if (baby == null || baby.Dead || !CanAcceptAnimal(baby)
                    || !baby.Position.InHorDistOf(InteractionCell, 2f))
                {
                    canStoreAll = false;
                    break;
                }
                planned += BodySizeOf(baby);
                if (planned > MaximumAnimalBodySize + 0.0001f)
                {
                    canStoreAll = false;
                    break;
                }
            }

            if (canStoreAll)
            {
                for (int i = 0; i < newlyBorn.Count; i++)
                {
                    if (!TryStoreAnimal(newlyBorn[i]))
                    {
                        canStoreAll = false;
                        break;
                    }
                }
            }

            if (!canStoreAll)
            {
                foreach (Pawn baby in newlyBorn)
                {
                    if (ContainsAnimal(baby))
                    {
                        TryReleaseAnimal(baby);
                    }
                }
                if (ContainsAnimal(mother))
                {
                    TryReleaseAnimal(mother);
                }
            }

            newlyBorn.Clear();
        }

        public bool HasSuitableLactatingFemale(Pawn young)
        {
            if (young == null || !young.IsMammal())
            {
                return false;
            }

            foreach (Pawn female in Animals)
            {
                if (female != young && female.gender == Gender.Female
                    && AnimalLactationUtility.IsCrossBreedCompatible(female, young)
                    && AnimalLactationUtility.CanMotherFeed(female))
                {
                    return true;
                }
            }

            return false;
        }

        public bool IsAwaitingService(Pawn animal) => animal != null
            && (releasedForService.ContainsKey(animal)
                || recoverServiceAfterLoad && animal.CurJob?.def == JobDefOf.Wait_MaintainPosture
                    && animal.CurJob.targetA.Thing == this);

        // Food is independent of the animals currently housed here. A
        // partially filled existing stack can still be topped up even when
        // all 18 stack slots are occupied.
        public bool NeedsFeed
        {
            get
            {
                if (!Enabled || !Spawned)
                {
                    return false;
                }

                int occupied = 0;
                bool canTopUp = false;
                for (int i = 0; i < contents.Count; i++)
                {
                    Thing item = contents[i];
                    if (item is Pawn || item.Destroyed)
                    {
                        continue;
                    }

                    occupied++;
                    if (item.stackCount < item.def.stackLimit
                        && FoodPermitted(item))
                    {
                        canTopUp = true;
                    }
                }

                return occupied < MaximumFoodStacks || canTopUp;
            }
        }

        private static bool IsNearEntrance(Pawn pawn, Building_AnimalBarn barn)
        {
            return pawn != null && pawn.Spawned && barn.Spawned
                && pawn.Map == barn.Map
                && pawn.Position.InHorDistOf(barn.InteractionCell, 2f);
        }

        private bool FoodPermitted(Thing food)
        {
            // Loading depends only on the player's vanilla food filter.
            // Whether a particular resident will eat it is decided later
            // by FoodUtility.WillEat when the animal is actually fed.
            // The fixed parent filter was built once from eligible food
            // defs. Avoid rechecking ThingDef components for every haul scan.
            return food != null && !food.Destroyed && food is not Corpse
                && nutritionSettings?.AllowedToAccept(food) == true;
        }

        public int FoodStacksToAccept(Thing food)
        {
            if (!Enabled || !Spawned || !FoodPermitted(food))
            {
                return 0;
            }

            int freeSlots = MaximumFoodStacks;
            int canMerge = 0;
            for (int i = 0; i < contents.Count; i++)
            {
                Thing held = contents[i];
                if (held is Pawn || held.Destroyed)
                {
                    continue;
                }

                freeSlots--;
                if (held.CanStackWith(food))
                {
                    canMerge += Math.Max(0, held.def.stackLimit - held.stackCount);
                }
            }

            // One incoming Thing creates at most one new stack; any remaining
            // quantity merges into existing stacks via vanilla ThingOwner.
            int room = canMerge + (freeSlots > 0 ? food.def.stackLimit : 0);
            return Math.Min(food.stackCount, Math.Max(0, room));
        }

        public bool CanAcceptFood(Thing food)
        {
            return food != null && food.Spawned && food.Map == Map
                && FoodStacksToAccept(food) > 0;
        }

        public bool TryAcceptCarriedFood(Pawn worker)
        {
            if (!Enabled || worker == null || worker.Map != Map || !Spawned)
            {
                return false;
            }

            Thing food = worker.carryTracker?.CarriedThing;
            if (food?.holdingOwner == null)
            {
                return false;
            }

            int count = FoodStacksToAccept(food);
            int moved = count > 0
                ? food.holdingOwner.TryTransferToContainer(
                    food, contents, count, true) : 0;
            if (moved > 0) InvalidateFoodRestockPriority();
            return moved > 0;
        }

        internal bool AllowsResident(Pawn animal)
        {
            return animal != null && animalFilter?.Allows(animal) == true;
        }

        // Shared admission policy for normal rope handling and transfers
        // from another barn. A held animal has MapHeld but is not Spawned.
        private bool CanAcceptAnimalCore(Pawn animal, bool ignoreAutomaticRules)
        {
            ZoologyModSettings settings = ModConstants.Settings;
            return Spawned
                && Faction == Faction.OfPlayer
                && settings != null
                && Enabled
                && animal != null
                && animal.Faction == Faction.OfPlayer
                && animal.RaceProps?.Animal == true
                && settings.GetRoamerFor(animal.def)
                && (ignoreAutomaticRules || AllowsResident(animal))
                && !animal.Dead
                && !animal.Destroyed
                && !animal.Downed
                && !animal.InMentalState
                && !animal.IsQuestLodger()
                && !NeedsOutsideMedicalCare(animal)
                && !contents.Contains(animal)
                && Map.designationManager.DesignationOn(animal, DesignationDefOf.Slaughter) == null
                && OccupiedAnimalBodySize + ServiceBodySizeReservedForOthers(animal)
                    + BodySizeOf(animal) <= MaximumAnimalBodySize + 0.0001f;
        }

        // Procedures cannot be performed on a pawn held by a ThingOwner.
        // Keep animals needing medical rest or an active operation on the map,
        // where vanilla medical beds and doctors can operate normally.
        private static bool NeedsOutsideMedicalCare(Pawn animal)
        {
            if (animal?.health == null || animal.Dead)
            {
                return false;
            }

            if (animal.health.surgeryBills?.AnyShouldDoNow == true)
            {
                return true;
            }

            return HealthAIUtility.ShouldEverReceiveMedicalCareFromPlayer(animal)
                && HealthAIUtility.ShouldSeekMedicalRest(animal);
        }

        private bool HasAvailableAnimalPatientBed(Pawn animal)
        {
            // Look only when a resident actually needs care. A patient can
            // use a medical bed or an ordinary animal bed if no medical bed
            // is available. Vanilla RestUtility.FindBedFor applies its
            // normal medical-first priorities after release.
            List<Thing> beds = Map.listerThings.ThingsInGroup(ThingRequestGroup.Bed);
            for (int i = 0; i < beds.Count; i++)
            {
                if (!(beds[i] is Building_Bed bed)
                    || !bed.AnyUnoccupiedSleepingSlot
                    || bed.Faction != Faction.OfPlayer || bed.IsForbidden(Faction.OfPlayer)
                    || bed.ForPrisoners || bed.ForSlaves || bed.IsBurning()
                    || !RestUtility.CanUseBedEver(animal, bed.def)
                    || !bed.Medical && !RestUtility.CanUseBedNow(
                        bed, animal, checkSocialProperness: false)
                    || !Map.reachability.CanReach(InteractionCell, bed,
                        PathEndMode.OnCell, TraverseParms.For(TraverseMode.PassDoors)
                            .WithFenceblockedOf(animal)))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private void ReleasePatientsForVanillaCare()
        {
            for (int i = contents.Count - 1; i >= 0; i--)
            {
                if (contents[i] is Pawn patient && NeedsOutsideMedicalCare(patient)
                    && HasAvailableAnimalPatientBed(patient))
                {
                    // A real spawned pawn can seek a medical bed, be rescued,
                    // receive treatment or undergo surgery. No such jobs can
                    // ever run against a pawn held inside this barn.
                    TryReleaseAnimal(patient);
                }
            }
        }

        public bool CanAcceptAnimal(Pawn animal, bool ignoreAutomaticRules = false)
        {
            return animal?.Spawned == true && animal.Map == Map
                && CanAcceptAnimalCore(animal, ignoreAutomaticRules);
        }

        internal bool CanAcceptTransferredResident(Pawn animal)
        {
            return animal?.MapHeld == Map && CanAcceptAnimalCore(animal, false);
        }

        public bool TryStoreAnimal(Pawn animal, bool ignoreAutomaticRules = false)
        {
            return TryStoreAnimalInternal(animal, ignoreAutomaticRules, fromAdjacentEggBox: false);
        }

        internal bool TryStoreHatchedAnimal(Pawn animal, Thing eggBox)
        {
            // The egg box is recorded by the vanilla CompHatcher at hatch
            // time. This is the only permitted alternative to rope delivery
            // at the entrance; all normal admission rules still apply.
            return AnimalBarnEggUtility.IsAdjacentEggBox(this, eggBox)
                && TryStoreAnimalInternal(animal, ignoreAutomaticRules: false,
                    fromAdjacentEggBox: true);
        }

        private bool TryStoreAnimalInternal(Pawn animal, bool ignoreAutomaticRules,
            bool fromAdjacentEggBox)
        {
            if (!CanAcceptAnimal(animal, ignoreAutomaticRules)
                || !fromAdjacentEggBox && !IsNearEntrance(animal, this))
            {
                return false;
            }

            IntVec3 previousPosition = animal.Position;
            Map previousMap = animal.Map;
            animal.DeSpawn();
            if (contents.TryAdd(animal, false))
            {
                // A successful loading request is complete, even when the
                // animal was queued for multiple barns before a worker arrived.
                foreach (Building_AnimalBarn barn in AnimalBarnUtility.BarnsOnMap(previousMap))
                {
                    barn.CancelManualIntake(animal);
                }
                InvalidateFoodRestockPriority();
                return true;
            }

            if (!animal.Destroyed && animal.holdingOwner == null)
            {
                GenSpawn.Spawn(animal, previousPosition, previousMap);
            }

            return false;
        }

        public bool TryReleaseAnimal(Pawn animal)
        {
            if (!Spawned || !ContainsAnimal(animal)
                || !contents.TryDrop(animal, InteractionCell, Map, ThingPlaceMode.Near, out _))
                return false;
            InvalidateFoodRestockPriority();
            return true;
        }

        // Use ThingOwner.TryDrop for cargo ejection. It handles partial
        // stacks and restores the remainder if placement is impossible.
        public bool TryDropStoredItem(Thing item, int count)
        {
            bool dropped = Spawned && item != null && !(item is Pawn)
                && !item.Destroyed && count > 0 && contents.Contains(item)
                && contents.TryDrop(item, InteractionCell, Map,
                    ThingPlaceMode.Near, count, out _);
            if (dropped) InvalidateFoodRestockPriority();
            return dropped;
        }

        public bool TryStoreFood(Thing item)
        {
            if (!CanAcceptFood(item) || FoodStacksToAccept(item) < item.stackCount)
            {
                return false;
            }

            IntVec3 previousPosition = item.Position;
            Map previousMap = item.Map;
            item.DeSpawn();
            if (contents.TryAdd(item, true))
            {
                InvalidateFoodRestockPriority();
                return true;
            }

            if (!item.Destroyed && item.holdingOwner == null)
            {
                GenSpawn.Spawn(item, previousPosition, previousMap);
            }

            return false;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref contents, "barnContents", this);
            Scribe_Deep.Look(ref animalFilter, "barnAnimalFilter");
            Scribe_Deep.Look(ref nutritionSettings, "barnNutritionSettings");
            Scribe_Values.Look(ref filtersInitialized, "barnFiltersInitialized", false);
            Scribe_Collections.Look(ref manuallyRequestedAnimals, "barnManuallyRequestedAnimals", LookMode.Reference);
            Scribe_Collections.Look(ref nextServiceTickByPawnId, "barnServiceCooldown",
                LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                contents ??= new ThingOwner<Thing>(this, false, LookMode.Deep);
                animalFilter ??= new ThingFilter();
                nutritionSettings ??= new StorageSettings(this);
                // Scribe_Deep can restore StorageSettings with its parameterless
                // constructor. Restore the owning building so filter changes
                // notify the correct parent after loading a save.
                nutritionSettings.owner = this;
                manuallyRequestedAnimals ??= new List<Pawn>();
                manuallyRequestedAnimals.RemoveAll(p => p == null || p.Destroyed);
                // Released animals already exist on the map. Rebuild service
                // tracking after loading rather than serializing a transient
                // Pawn-keyed dictionary (which requires two cross-ref lists).
                releasedForService = new Dictionary<Pawn, int>();
                nextServiceTickByPawnId ??= new Dictionary<string, int>();
            }
        }

        // A lowered slider or changed race def can leave a barn over capacity.
        // Evict the most recently stored residents first, retaining a stable
        // core herd. Use real ThingOwner drops; never destroy a pawn just to
        // satisfy the setting. Failed drops are retried on the next interval.
        private void EnforceAnimalCapacity()
        {
            float capacity = MaximumAnimalBodySize;
            float reserved = ServiceBodySizeReservedForOthers(null);
            float occupied = OccupiedAnimalBodySize;

            // Animals released for milking are already outside the barn.
            // Relinquish excess reservations before physically ejecting any
            // remaining residents. They may then use ordinary map handling.
            if (occupied + reserved > capacity + 0.0001f)
            {
                var reservations = new List<Pawn>(releasedForService.Keys);
                for (int i = reservations.Count - 1;
                    i >= 0 && occupied + reserved > capacity + 0.0001f; i--)
                {
                    Pawn pawn = reservations[i];
                    if (!releasedForService.Remove(pawn)) continue;
                    reserved = ServiceBodySizeReservedForOthers(null);
                }
            }

            for (int i = contents.Count - 1;
                i >= 0 && occupied + reserved > capacity + 0.0001f; i--)
            {
                if (contents[i] is Pawn animal && TryReleaseAnimal(animal))
                    occupied = OccupiedAnimalBodySize;
            }

            // Cancel only outstanding requests that cannot fit in the
            // remaining reservation budget. Queued animals remain spawned.
            float planned = occupied + Math.Max(0f, reserved);
            for (int i = 0; i < manuallyRequestedAnimals.Count;)
            {
                Pawn request = manuallyRequestedAnimals[i];
                float size = BodySizeOf(request);
                if (request == null || request.Destroyed || !request.Spawned
                    || request.Map != Map || planned + size > capacity + 0.0001f)
                {
                    manuallyRequestedAnimals.RemoveAt(i);
                    continue;
                }
                planned += size;
                i++;
            }
        }

        protected override void Tick()
        {
            // Do not call ThingOwnerTick: that would resume Pawn.Tick and its job tree.
            base.Tick();

            if (!Enabled)
            {
                // Retain the buildings but release live contents when disabled.
                EjectContents();
                return;
            }

            for (int i = contents.Count - 1; i >= 0; i--)
            {
                if (i >= contents.Count)
                {
                    continue;
                }

                Thing thing = contents[i];
                if (thing is Pawn animal)
                {
                    if (animal.Destroyed || animal.Dead)
                    {
                        TryReleaseAnimal(animal);
                        continue;
                    }

                    // The original pregnancy tracker runs inside the holder.
                    // Newborns are generated by vanilla and placed outside the
                    // footprint, then admitted together after the birth event.
                    AnimalGameApi.TickContainedPhysiology(animal);
                    if (newlyBorn.Count > 0)
                    {
                        StoreNewbornLitter(animal);
                    }
                    if (animal.Dead)
                    {
                        TryReleaseAnimal(animal);
                        continue;
                    }

                    TickAnimalProducts(animal);
                }
                else if (thing is Corpse corpse)
                {
                    // Vanilla Pawn.Kill replaces a held animal with its real
                    // corpse. Corpses do not belong in feed storage; drop
                    // them onto the map as soon as the barn ticks. A failed
                    // placement leaves the corpse intact for a later retry.
                    if (Spawned && contents.TryDrop(corpse, InteractionCell,
                        Map, ThingPlaceMode.Near, out _))
                    {
                        InvalidateFoodRestockPriority();
                    }
                    else
                    {
                        // Keep normal decomposition running while a blocked
                        // map prevents the corpse from being placed outside.
                        corpse.DoTick();
                        if (corpse.Destroyed) contents.Remove(corpse);
                    }
                }
                else if (thing.Destroyed)
                {
                    contents.Remove(thing);
                }
                else
                {
                    // Public vanilla tick dispatcher selects normal, rare or
                    // long ticks as required, including rot and other comps.
                    // Pawn AI is never ticked through this branch.
                    thing.DoTick();
                    if (thing.Destroyed)
                    {
                        contents.Remove(thing);
                    }
                }
            }

            if (this.IsHashIntervalTick(150))
            {
                EnforceAnimalCapacity();
                RefreshDisplayedAnimals();
                TickBarnFire();
            }

            if (this.IsHashIntervalTick(FeedingIntervalTicks))
            {
                TickFeeding();
            }

            if (this.IsHashIntervalTick(ServiceCheckIntervalTicks))
            {
                ReleasePatientsForVanillaCare();
                TickService();
            }

            if (this.IsHashIntervalTick(2500))
            {
                AnimalBarnReproduction.TryMateResidents(this);
                TickAutoSlaughter();
            }

            if (this.IsHashIntervalTick(60000))
            {
                AnimalBarnCompatibility.TickMapDependentAsexualReproduction(this);
            }
        }

        private void TickAutoSlaughter()
        {
            if (Map?.autoSlaughterManager == null)
            {
                return;
            }

            List<Pawn> targets = Map.autoSlaughterManager.AnimalsToSlaughter;
            if (targets == null || targets.Count == 0)
            {
                return;
            }

            int released = releasedForService.Count;
            var candidates = new List<Pawn>();
            foreach (Pawn animal in Animals)
            {
                if (released + candidates.Count >= MaximumOutsideForService)
                {
                    break;
                }

                if (targets.Contains(animal))
                {
                    candidates.Add(animal);
                }
            }

            foreach (Pawn animal in candidates)
            {
                // Slaughter is performed by the vanilla worker after the
                // animal has been released to the map.
                TryReleaseAnimal(animal);
            }
        }

        private static bool NeedsAnimalService(Pawn animal)
        {
            if (animal?.AllComps == null || animal.Dead)
            {
                return false;
            }

            foreach (ThingComp comp in animal.AllComps)
            {
                if (comp is CompHasGatherableBodyResource resource && resource.ActiveAndFull)
                {
                    return true;
                }
            }

            return false;
        }

        private void TickService()
        {
            int now = GenTicks.TicksGame;
            if (recoverServiceAfterLoad)
            {
                recoverServiceAfterLoad = false;
                // All map pawns have completed SpawnSetup by the first tick.
                // The saved native wait job identifies its barn, without a
                // second serialized collection of pawn references.
                foreach (Pawn pawn in Map.mapPawns.AllPawnsSpawned)
                    if (pawn.CurJob?.def == JobDefOf.Wait_MaintainPosture
                        && pawn.CurJob.targetA.Thing == this && pawn.RaceProps.Animal)
                        releasedForService[pawn] = now;
            }
            // Never teleport service animals: re-admission requires them to be at
            // the barn entrance and still be eligible for this particular barn.
            var finished = new List<Pawn>();
            foreach (KeyValuePair<Pawn, int> entry in releasedForService)
            {
                Pawn animal = entry.Key;
                if (animal == null || animal.Destroyed || animal.Dead
                    || animal.Map != Map || !animal.Spawned)
                {
                    finished.Add(animal);
                    continue;
                }

                bool overdue = now - entry.Value >= ServiceWaitTicks;
                if (!overdue && NeedsAnimalService(animal))
                {
                    continue;
                }

                if (CanAcceptAnimal(animal) && TryStoreAnimal(animal))
                {
                    nextServiceTickByPawnId[animal.ThingID] = now + ServiceRetryTicks;
                    finished.Add(animal);
                }
                else if (overdue)
                {
                    // The ordinary admission job can bring a wandering animal back.
                    finished.Add(animal);
                }
            }

            foreach (Pawn pawn in finished)
            {
                releasedForService.Remove(pawn);
            }

            if (releasedForService.Count >= MaximumOutsideForService)
            {
                return;
            }

            var ready = new List<Pawn>();
            foreach (Pawn animal in Animals)
            {
                if (!NeedsAnimalService(animal) || animal.Downed)
                {
                    continue;
                }

                if (nextServiceTickByPawnId.TryGetValue(animal.ThingID, out int eligibleTick)
                    && now < eligibleTick)
                {
                    continue;
                }

                ready.Add(animal);
                if (ready.Count + releasedForService.Count >= MaximumOutsideForService)
                {
                    break;
                }
            }

            foreach (Pawn animal in ready)
            {
                if (!TryReleaseAnimal(animal))
                {
                    continue;
                }

                releasedForService[animal] = now;
                // Vanilla and modded gatherers will interact with a real spawned
                // pawn. The short waiting job is interrupted by their own jobs.
                PawnUtility.ForceWait(animal, ServiceWaitTicks, this, true);
            }
        }

        // Eggs produced by residents are real items. A neighboring vanilla
        // egg box receives them first; food storage is only the fallback.
        public bool TryStoreProducedEggAsFood(Thing egg)
        {
            return egg != null && !egg.Destroyed
                && egg.TryGetComp<CompHatcher>() == null
                && !AnimalBarnEggUtility.IsExplosiveHatchingEgg(egg)
                && FoodStacksToAccept(egg) >= egg.stackCount
                && contents.TryAdd(egg, true);
        }

        // Emergency preservation when no valid cell is available for a laid
        // egg. Fertilized eggs should normally live in a real egg box or on
        // the map so temperature and hatching remain correct.
        public bool TryHoldProducedEgg(Thing egg)
        {
            return egg != null && !egg.Destroyed && contents.TryAdd(egg, true);
        }

        private void TickAnimalProducts(Pawn animal)
        {
            List<ThingComp> comps = animal.AllComps;
            if (comps == null)
            {
                return;
            }

            for (int i = 0; i < comps.Count; i++)
            {
                ThingComp comp = comps[i];
                if (comp is CompHasGatherableBodyResource)
                {
                    // Includes Vanilla Expanded Framework's CompAnimalProduct.
                    comp.CompTick();
                }
                else if (comp is CompEggLayer eggLayer)
                {
                    // Keep vanilla's normal egg-progress logic (including
                    // VEF's ProduceEgg crossbreeding patch) intact.
                    eggLayer.CompTick();
                    if (eggLayer.CanLayNow)
                    {
                        AnimalBarnEggUtility.DepositLaidEgg(this, eggLayer.ProduceEgg());
                    }
                }
                else
                {
                    // Alpha Animals supplies several egg and production comps
                    // through VEF; handle its non-CompEggLayer egg type.
                    AnimalBarnCompatibility.TickAdditionalEggLayers(this, animal, comp);
                }
            }
        }

        private void TickFeeding()
        {
            InvalidateFoodRestockPriority();
            // Food is stored as real Things and ingested through the vanilla API.
            // Each animal is checked once per interval, not through a map-wide search.
            for (int i = contents.Count - 1; i >= 0; i--)
            {
                if (i >= contents.Count || !(contents[i] is Pawn animal)
                    || animal.Dead || animal.needs?.food == null)
                {
                    continue;
                }

                bool nursed = TryFeedFromMother(animal);
                if (nursed || animal.needs.food.CurLevelPercentage >= animal.RaceProps.FoodLevelPercentageWantEat)
                {
                    continue;
                }

                for (int j = contents.Count - 1; j >= 0; j--)
                {
                    if (j >= contents.Count)
                    {
                        continue;
                    }

                    Thing food = contents[j];
                    try
                    {
                        if (food is Pawn || food.Destroyed || !food.IngestibleNow
                            || !food.def.IsNutritionGivingIngestible
                            || food.TryGetComp<CompHatcher>() != null
                            || !FoodUtility.WillEat(animal, food, animal, false, false))
                        {
                            continue;
                        }

                        float perItem = FoodUtility.NutritionForEater(animal, food);
                        if (perItem <= 0f || float.IsNaN(perItem) || float.IsInfinity(perItem)) continue;
                        float nutrition = food.Ingested(animal, animal.needs.food.NutritionWanted);
                        if (animal.Dead || animal.Destroyed) break;
                        animal.needs.food.CurLevel += nutrition;
                        // Match Toils_Ingest: Ingested removes whole real units
                        // and returns eater-specific nutrition. The need setter
                        // applies the normal maximum (including mod patches).
                        if (nutrition > 0f
                            && animal.needs.food.CurLevelPercentage >= animal.RaceProps.FoodLevelPercentageWantEat)
                        {
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.WarningOnce("[Zoology] Barn feeding failed for " + animal + ": " + ex,
                            animal.thingIDNumber ^ 0x6B41524E);
                    }

                }
            }
        }

        private bool TryFeedFromMother(Pawn baby)
        {
            if (!LactationSettingsGate.Enabled()
                || !AnimalLactationUtility.ChildWantsSuckle(baby))
            {
                return false;
            }

            // A known biological mother must be allowed to resume lactation
            // before searching for existing nursing females. This is the
            // same hunger-triggered repair used by on-map feeding and caravans.
            AnimalLactationUtility.MaintainKnownMotherLactationForBaby(baby);

            Pawn mother = null;
            if (ChildcareUtility.TryGetKnownBiologicalMother(baby, out Pawn biological)
                && ContainsAnimal(biological)
                && AnimalLactationUtility.CanMotherFeed(biological)
                && AnimalLactationUtility.IsCrossBreedCompatible(biological, baby))
            {
                mother = biological;
            }
            else
            {
                // In a shared barn, an appropriate lactating female can nurse
                // another mother's newborn (the same species rules apply).
                foreach (Pawn female in Animals)
                {
                    if (female.gender == Gender.Female
                        && AnimalLactationUtility.CanMotherFeed(female)
                        && AnimalLactationUtility.IsCrossBreedCompatible(female, baby))
                    {
                        mother = female;
                        break;
                    }
                }
            }

            if (mother == null)
            {
                return false;
            }

            float before = baby.needs.food.CurLevel;
            AnimalLactationUtility.SuckleFromLactatingPawn(baby, mother, FeedingIntervalTicks);
            return baby.needs.food.CurLevel > before;
        }

        public void EjectContents()
        {
            if (Spawned)
            {
                contents.TryDropAll(InteractionCell, Map, ThingPlaceMode.Near);
            }
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            EjectContents();

            // No animal may be destroyed simply because there was no valid
            // cell for unloading the container when the building collapsed.
            for (int i = contents.Count - 1; i >= 0; i--)
            {
                Thing held = contents[i];
                contents.Remove(held);
                if (held is Pawn animal)
                {
                    Find.WorldPawns.PassToWorld(animal,
                        RimWorld.Planet.PawnDiscardDecideMode.KeepForever);
                }
                else if (!held.Destroyed)
                {
                    held.Destroy(DestroyMode.Vanish);
                }
            }

            releasedForService.Clear();
            nextServiceTickByPawnId.Clear();
            base.Destroy(mode);
        }

        public override string GetInspectString()
        {
            string text = base.GetInspectString();
            if (!text.NullOrEmpty())
            {
                text += "\n";
            }

            return text + "Zoology_BarnPopulation".Translate(AnimalCount)
                + "\n" + "Zoology_BarnBodySize".Translate(
                    OccupiedAnimalBodySize.ToString("0.##"), MaximumAnimalBodySize.ToString("0.##"))
                + "\n" + "Zoology_BarnFoodStacks".Translate(StoredFoodStacks, MaximumFoodStacks);
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }

            if (Faction != Faction.OfPlayer || !Enabled)
            {
                yield break;
            }

            yield return new Command_Action
            {
                defaultLabel = "Zoology_BarnManualLoad".Translate(),
                defaultDesc = "Zoology_BarnManualLoadDesc".Translate(),
                icon = CompTransporter.LoadCommandTex,
                action = ShowManualIntakeMenu
            };

            if (manuallyRequestedAnimals.Count > 0)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Zoology_BarnCancelManualLoad".Translate(),
                    defaultDesc = "Zoology_BarnCancelManualLoadDesc".Translate(),
                    icon = CompTransporter.CancelLoadCommandTex,
                    action = () => manuallyRequestedAnimals.Clear()
                };
            }

            if (!DebugSettings.ShowDevGizmos)
            {
                yield break;
            }

            yield return new Command_Action
            {
                defaultLabel = "DEV: Barn intake",
                action = ShowDebugIntakeMenu
            };
            yield return new Command_Action
            {
                defaultLabel = "DEV: Load barn food",
                action = ShowDebugFoodMenu
            };
        }

        private void ShowManualIntakeMenu()
        {
            var options = new List<FloatMenuOption>();
            List<Pawn> candidates = Map?.mapPawns?.SpawnedPawnsInFaction(Faction.OfPlayer);
            if (candidates != null)
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    Pawn animal = candidates[i];
                    if (animal?.RaceProps?.Animal != true
                        || !CanAcceptAnimal(animal, ignoreAutomaticRules: true)
                        || IsManualIntakeRequested(animal)
                        || !AnimalPenUtility.NeedsToBeManagedByRope(animal))
                    {
                        continue;
                    }

                    Pawn chosen = animal;
                    options.Add(new FloatMenuOption(chosen.LabelShortCap,
                        () => RequestManualIntake(chosen)));
                }
            }

            if (options.Count == 0)
            {
                options.Add(new FloatMenuOption("Zoology_BarnNoEligible".Translate(), null));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void ShowDebugFoodMenu()
        {
            var options = new List<FloatMenuOption>();
            if (Map != null)
            {
                IReadOnlyList<Thing> things = Map.listerThings.AllThings;
                for (int i = 0; i < things.Count; i++)
                {
                    Thing item = things[i];
                    if (item != null && item.Spawned
                        && item.def.category == ThingCategory.Item && item.def.EverHaulable
                        && item.def.IsNutritionGivingIngestible && item.IngestibleNow
                        && CanAcceptFood(item))
                    {
                        Thing selected = item;
                        options.Add(new FloatMenuOption(selected.LabelCap,
                            () => TryStoreFood(selected)));
                    }
                }
            }

            if (options.Count == 0)
            {
                options.Add(new FloatMenuOption("No edible items on this map", null));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void ShowDebugIntakeMenu()
        {
            var options = new List<FloatMenuOption>();
            IReadOnlyList<Pawn> mapPawns = Map?.mapPawns?.AllPawnsSpawned;
            if (mapPawns != null)
            {
                for (int i = 0; i < mapPawns.Count; i++)
                {
                    Pawn pawn = mapPawns[i];
                    if (CanAcceptAnimal(pawn))
                    {
                        Pawn selected = pawn;
                        options.Add(new FloatMenuOption(selected.LabelShortCap,
                            () => TryStoreAnimal(selected)));
                    }
                }
            }

            if (options.Count == 0)
            {
                options.Add(new FloatMenuOption("Zoology_BarnNoEligible".Translate(), null));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }
    }
}
#endif
