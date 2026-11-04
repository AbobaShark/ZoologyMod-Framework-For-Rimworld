// Isolated actual-game validation. Enabled only by barn-only.txt in the test profile.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using UnityEngine;
using ZoologyMod;

namespace ZoologyValidation
{
    public class BarnThrowingFood : ThingWithComps
    {
        protected override void IngestedCalculateAmounts(Pawn p,float wanted,out int taken,out float nutrition)
        { throw new InvalidOperationException("Intentional bad food fixture"); }
    }
    public class BarnValidation : GameComponent
    {
        static readonly BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        static string Report => Path.Combine(GenFilePaths.SaveDataFolderPath, "barn-validation.txt");
        static readonly FieldInfo Ticks = typeof(TickManager).GetField("ticksGameInt", All);
        int phase;
        bool busy, load;
        Building_AnimalBarn savedBarn, emptyBarn;
        Pawn savedMother, savedBaby, savedPregnant, savedService, savedRequest;
        int savedCount, savedFood;
        float savedLevel, savedBodySize;
        string savedDisplayOrder;
        static string DisplayOrder(Building_AnimalBarn b) => string.Join(",", ((Pawn[])typeof(Building_AnimalBarn).GetField("displayedAnimals",All).GetValue(b)).Where(p=>p!=null).Select(p=>p.ThingID));
        public BarnValidation(Game game) { }
        static void Write(string s) { File.AppendAllText(Report, s + Environment.NewLine); Log.Message("[Barn validation] " + s); }
        static void Check(string name, bool pass) => Write((pass ? "PASS " : "FAIL ") + name);
        static object Call(object obj, string method, params object[] args) => obj.GetType().GetMethod(method, All).Invoke(obj, args);
        static ThingOwner Owner(Building_AnimalBarn b) => b.GetDirectlyHeldThings();
        static Building_AnimalBarn Barn(Map map, ThingDef stuff = null)
        {
            var b = (Building_AnimalBarn)ThingMaker.MakeThing(Building_AnimalBarn.BarnDef, stuff ?? ThingDefOf.WoodLog);
            IntVec3 c = IntVec3.Invalid;
            for(int attempt=0;attempt<1000;attempt++)
            {
                var candidate=CellFinder.RandomClosewalkCellNear(map.Center,map,65);
                if(GenAdj.OccupiedRect(candidate,Rot4.North,b.def.size).ExpandedBy(2).All(cell=>cell.InBounds(map)
                    && cell.Standable(map) && !map.thingGrid.ThingsListAt(cell).Any(t=>t is Building || t is Pawn)))
                { c=candidate; break; }
            }
            if(!c.IsValid)throw new Exception("No empty test-barn footprint");
            GenSpawn.Spawn(b, c, map); b.SetFaction(Faction.OfPlayer);
            return b;
        }
        static Pawn Animal(Map map, string kind = "Cow", float age = 3f, Gender gender = Gender.Female)
        {
            var p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDef.Named(kind), Faction.OfPlayer,
                forceGenerateNewPawn:true, fixedBiologicalAge:age, fixedGender:gender));
            ModConstants.Settings.SetRoamerFor(p.def, true);
            GenSpawn.Spawn(p, CellFinder.RandomClosewalkCellNear(map.Center, map, 10), map);
            if (p.needs.rest != null) p.needs.rest.CurLevelPercentage = 1f;
            p.needs.food.CurLevelPercentage = 1f;
            return p;
        }
        static void Hold(Building_AnimalBarn b, Pawn p)
        {
            if (p.Spawned) p.DeSpawn();
            if (!Owner(b).TryAdd(p, false)) throw new Exception("Could not hold " + p);
        }
        static Thing Food(Building_AnimalBarn b, string name, int count)
        {
            var t = ThingMaker.MakeThing(ThingDef.Named(name)); t.stackCount = count; Owner(b).TryAdd(t, false); return t;
        }
        static int Count(Building_AnimalBarn b) => Owner(b).Cast<Thing>().Where(t => !(t is Pawn)).Sum(t => t.stackCount);
        static void ClearFood(Building_AnimalBarn b)
        { foreach (Thing t in Owner(b).Cast<Thing>().ToArray()) if (!(t is Pawn)) t.Destroy(); }
        static void Feed(Building_AnimalBarn b) => Call(b, "TickFeeding");
        static bool LactationEnabled()
        {
            Type gate=typeof(AnimalLactationUtility).Assembly.GetType(
                "ZoologyMod.LactationSettingsGate");
            return (bool)(gate?.GetMethod("Enabled",All)
                ?.Invoke(null,null) ?? false);
        }
        public override void ExposeData()
        {
            Scribe_Values.Look(ref phase,"barnTestPhase");
            Scribe_References.Look(ref savedBarn,"barnTestBarn"); Scribe_References.Look(ref emptyBarn,"barnTestEmpty");
            Scribe_References.Look(ref savedMother,"barnTestMother"); Scribe_References.Look(ref savedBaby,"barnTestBaby");
            Scribe_References.Look(ref savedPregnant,"barnTestPregnant"); Scribe_References.Look(ref savedService,"barnTestService");
            Scribe_References.Look(ref savedRequest,"barnTestRequest");
            Scribe_Values.Look(ref savedCount,"barnTestCount"); Scribe_Values.Look(ref savedFood,"barnTestFood");
            Scribe_Values.Look(ref savedLevel,"barnTestLevel"); Scribe_Values.Look(ref savedBodySize,"barnTestBodySize");
            Scribe_Values.Look(ref savedDisplayOrder,"barnTestDisplay");
        }
        public override void GameComponentUpdate()
        {
            if (!File.Exists(Path.Combine(GenFilePaths.SaveDataFolderPath,"barn-only.txt")) || busy || phase == 99 || Find.CurrentMap == null) return;
            if (load) { load = false; GameDataSaveLoader.LoadGame("ZoologyBarnValidation"); return; }
            busy = true;
            try
            {
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                Map map = Find.CurrentMap;
                if (phase == 0)
                {
                    File.WriteAllText(Report, "RimWorld " + VersionControl.CurrentVersionStringWithRev + Environment.NewLine);
                    Write("Assembly " + typeof(Building_AnimalBarn).Assembly.Location);
                    Feeding(map); Physiology(map); HungerStates(map);
                    TemperatureExposure(map); RotChecks(map); FireChecks(map);
                    Capacity(map); MixedRendering(map); FeedDistribution(map);
                    AnimalFiltersAndFeedDefaults(map);
                    NursingBalance(map); IdeologyRanching(map);
                    PrepareSave(map);
                    phase = 1;
                    GameDataSaveLoader.SaveGame("ZoologyBarnValidation");
                    Check("save written", File.Exists(Path.Combine(GenFilePaths.SaveDataFolderPath,"Saves/ZoologyBarnValidation.rws")));
                    load = true;
                }
                else if (phase == 1)
                {
                    Check("loaded owner, animals and food unchanged", savedBarn.AnimalCount == savedCount && Count(savedBarn) == savedFood);
                    Check("body-size capacity persists across load", Math.Abs(savedBarn.OccupiedAnimalBodySize - savedBodySize) < .0001f);
                    Check("empty barn survives load", emptyBarn.AnimalCount == 0 && Count(emptyBarn) == 0);
                    Check("residents unspawned with correct owner", savedMother.ParentHolder == savedBarn && !savedMother.Spawned && !savedBaby.Spawned && !savedPregnant.Spawned);
                    Check("loaded residents still inherit real room temperature",
                        savedMother.MapHeld==map
                        && Math.Abs(savedMother.AmbientTemperature
                            -GenTemperature.GetTemperatureForCell(savedBarn.Position,map))<.001f);
                    Check("display order stable across load",DisplayOrder(savedBarn)==savedDisplayOrder);
                    var cooldown=(System.Collections.Generic.Dictionary<string,int>)typeof(Building_AnimalBarn).GetField("nextServiceTickByPawnId",All).GetValue(savedBarn);
                    Check("full-ID service cooldown survives",cooldown.ContainsKey(savedMother.ThingID));
                    Check("food level unchanged by save/load", Math.Abs(savedMother.needs.food.CurLevel - savedLevel) < 0.00001f);
                    Check("pregnancy survives", savedPregnant.health.hediffSet.HasHediff(HediffDefOf.Pregnant));
                    Check("lactation survives", savedMother.health.hediffSet.HasHediff(AnimalLactationUtility.LactatingHediffDef));
                    Check("manual assignment survives", savedBarn.IsManualIntakeRequested(savedRequest));
                    Check("animal filter survives", !savedBarn.AnimalFilter.Allows(ThingDef.Named("Chicken")));
                    Check("juvenile filter setting survives save and load",
                        !savedBarn.AnimalFilter.Allows(
                            SpecialThingFilterDef.Named("Zoology_AllowJuveniles")));
                    Check("milk-dependent young setting survives save and load",
                        !savedBarn.AnimalFilter.Allows(
                            SpecialThingFilterDef.Named("Zoology_AllowMilkDependentYoung")));
                    Check("non-lactating female filter survives save and load",
                        !savedBarn.AnimalFilter.Allows(
                            SpecialThingFilterDef.Named("Zoology_AllowNonLactatingFemales")));
                    Check("nutrition filter survives", !savedBarn.GetStoreSettings().filter.Allows(ThingDefOf.Hay));
                    Check("service wait job survives", savedService.Spawned && savedService.CurJob?.targetA.Thing == savedBarn && savedBarn.IsAwaitingService(savedService));
                    Call(savedBarn,"TickService");
                    Check("service tracking rebuilt", savedBarn.IsAwaitingService(savedService));
                    typeof(CompHasGatherableBodyResource).GetField("fullness",All).SetValue(savedService.TryGetComp<CompMilkable>(),0f);
                    Call(savedBarn,"TickService");
                    Check("completed service returns animal once",savedBarn.ContainsAnimal(savedService)&&!savedService.Spawned&&!savedBarn.IsAwaitingService(savedService));
                    savedCount=savedBarn.AnimalCount;
                    CameraJumper.TryJumpAndSelect(savedMother);
                    Check("Animals camera selects barn", Find.Selector.SingleSelectedThing == savedBarn);
                    CameraJumper.TryJumpAndSelect(savedRequest);
                    Check("normal map animal selection unchanged", Find.Selector.SingleSelectedThing == savedRequest);
                    Check("contained pawns not selectable by map hit test", !savedBarn.def.containedPawnsSelectable);
                    float before = savedBaby.needs.food.CurLevel; Feed(savedBarn);
                    Check("nursing continues after load", savedBaby.needs.food.CurLevel > before);
                    phase = 2; GameDataSaveLoader.SaveGame("ZoologyBarnValidation"); load = true;
                }
                else
                {
                    Check("second load preserves population and food", savedBarn.AnimalCount == savedCount && Count(savedBarn) == savedFood);
                    Drawing(map); phase = 99; Write("COMPLETE barn");
                }
            }
            catch(Exception ex) { Write("FAIL exception " + ex); phase = 99; }
            finally { busy = false; }
        }
        static void AnimalFiltersAndFeedDefaults(Map map)
        {
            var barn=Barn(map);
            var adult=Animal(map,"Cow",4f,Gender.Female);
            var nonLactatingAdult=Animal(map,"Cow",4f,Gender.Female);
            var adultMale=Animal(map,"Cow",4f,Gender.Male);
            var young=Animal(map,"Cow",.1f,Gender.Female);
            var adultDef=SpecialThingFilterDef.Named("Zoology_AllowReproductiveAdults");
            var youngDef=SpecialThingFilterDef.Named("Zoology_AllowJuveniles");
            var milkDef=SpecialThingFilterDef.Named("Zoology_AllowMilkDependentYoung");
            var lactatingDef=SpecialThingFilterDef.Named("Zoology_AllowLactatingFemales");
            var nonLactatingDef=SpecialThingFilterDef.Named("Zoology_AllowNonLactatingFemales");
            var filter=barn.AnimalFilter;
            var foods=barn.GetStoreSettings().filter;
            try
            {
                Check("new barn defaults permit hay and kibble",
                    foods.Allows(ThingDefOf.Hay) && foods.Allows(ThingDefOf.Kibble));
                ThingDef potatoes=ThingDef.Named("RawPotatoes");
                Check("other foods initially disabled but manually selectable",
                    !foods.Allows(potatoes)
                    && barn.GetParentStoreSettings().filter.Allows(potatoes));
                foods.SetAllow(potatoes,true);
                Check("manually enabled extra food accepted by both filters",
                    foods.Allows(potatoes)
                    && barn.GetParentStoreSettings().AllowedToAccept(potatoes));
                foods.SetAllow(potatoes,false);
                Check("all corpse definitions excluded from barn feed options",
                    DefDatabase<ThingDef>.AllDefs.Where(
                        d=>d.thingClass!=null
                            && typeof(Corpse).IsAssignableFrom(d.thingClass))
                        .All(d=>!barn.GetParentStoreSettings().filter.Allows(d)));
                var chick=Animal(map,"Chicken",.1f,Gender.Female);
                Check("three age groups distinguish milk dependence from youth",
                    adult.ageTracker.CurLifeStage.reproductive
                    && !young.ageTracker.CurLifeStage.reproductive
                    && adultDef.Worker.Matches(adult) && !adultDef.Worker.Matches(young)
                    && (LactationEnabled()
                        ? milkDef.Worker.Matches(young)
                            && !youngDef.Worker.Matches(young)
                        : !milkDef.Worker.Matches(young)
                            && youngDef.Worker.Matches(young))
                    && youngDef.Worker.Matches(chick)
                    && !milkDef.Worker.Matches(chick)
                    && !youngDef.Worker.Matches(adult));

                filter.SetAllow(adultDef,false);
                Check("barn adult toggle prevents admission of reproductive animal",
                    !filter.Allows(adult) && filter.Allows(young));
                filter.SetAllow(adultDef,true);
                filter.SetAllow(youngDef,false);
                Check("juvenile filter excludes independently feeding chick",
                    filter.Allows(adult)
                    && (!LactationEnabled() || filter.Allows(young))
                    && !filter.Allows(chick));
                filter.SetAllow(youngDef,true);
                filter.SetAllow(milkDef,false);
                Check("milk-dependent filter excludes suckling calf only when enabled",
                    (LactationEnabled()
                        ? !filter.Allows(young) : filter.Allows(young))
                    && filter.Allows(chick));
                filter.SetAllow(milkDef,true);

                HediffDef lactation=AnimalLactationUtility.LactatingHediffDef;
                Check("lactation special filter recognizes existing hediff",
                    lactation!=null);
                Check("non-lactating filter matches adult females only",
                    nonLactatingDef.Worker.Matches(nonLactatingAdult)
                    && !nonLactatingDef.Worker.Matches(adultMale)
                    && !nonLactatingDef.Worker.Matches(young)
                    && !nonLactatingDef.Worker.Matches(chick)
                    && !lactatingDef.Worker.Matches(nonLactatingAdult));
                if(lactation!=null)
                {
                    adult.health.AddHediff(lactation);
                    Check("lactation filters distinguish adult nursing states",
                        lactatingDef.Worker.Matches(adult)
                        && !nonLactatingDef.Worker.Matches(adult)
                        && nonLactatingDef.Worker.Matches(nonLactatingAdult)
                        && !lactatingDef.Worker.Matches(nonLactatingAdult)
                        && !nonLactatingDef.Worker.Matches(young)
                        && !nonLactatingDef.Worker.Matches(chick));
                    filter.SetAllow(adultDef,false);
                    Check("excluding adults still permits nursing females and young",
                        filter.Allows(adult) && !filter.Allows(nonLactatingAdult)
                        && !filter.Allows(adultMale)
                        && filter.Allows(chick) && filter.Allows(young));
                    filter.SetAllow(adultDef,true);
                    filter.SetAllow(lactatingDef,false);
                    Check("excluding lactating females leaves other ages and adults",
                        !filter.Allows(adult) && filter.Allows(nonLactatingAdult)
                        && filter.Allows(adultMale)
                        && filter.Allows(young) && filter.Allows(chick));
                    filter.SetAllow(lactatingDef,true);
                    filter.SetAllow(nonLactatingDef,false);
                    Check("excluding non-lactating adults preserves nursing females and young",
                        filter.Allows(adult) && !filter.Allows(nonLactatingAdult)
                        && filter.Allows(adultMale)
                        && filter.Allows(young) && filter.Allows(chick));
                    filter.SetAllow(nonLactatingDef,true);
                }

                var femaleDef=DefDatabase<SpecialThingFilterDef>.AllDefsListForReading
                    .FirstOrDefault(d=>d.Worker is SpecialThingFilterWorker_AllowFemales);
                Check("vanilla female special filter exists",femaleDef!=null);
                if(femaleDef!=null)
                {
                    Type ui=typeof(Building_AnimalBarn).Assembly.GetType(
                        "ZoologyMod.AnimalFilterVisibility");
                    MethodInfo helper=ui?.GetMethod(
                        "HideLactationWhenFemalesExcluded",All);
                    filter.SetAllow(femaleDef,false);
                    var hidden=helper?.Invoke(null,new object[]{filter})
                        as System.Collections.Generic.IEnumerable<SpecialThingFilterDef>;
                    Check("both lactation options hidden when females disabled",
                        hidden!=null && hidden.Contains(lactatingDef)
                        && hidden.Contains(nonLactatingDef));
                    filter.SetAllow(femaleDef,true);
                    hidden=helper?.Invoke(null,new object[]{filter})
                        as System.Collections.Generic.IEnumerable<SpecialThingFilterDef>;
                    Check("both lactation options visible when females enabled",
                        hidden==null);
                }

                // The native pen marker must consume the same special filters.
                var markerThing=ThingMaker.MakeThing(ThingDef.Named("PenMarker"),
                    ThingDefOf.WoodLog) as ThingWithComps;
                var pen=markerThing?.TryGetComp<CompAnimalPenMarker>();
                Check("vanilla pen marker supports same animal filters",pen!=null);
                if(pen!=null)
                {
                    pen.AnimalFilter.CopyAllowancesFrom(AnimalPenUtility.GetFixedAnimalFilter());
                    pen.AnimalFilter.SetAllow(youngDef,false);
                    Check("vanilla pen excludes independent juveniles, not milk young",
                        pen.AcceptsToPen(adult)
                        && !pen.AcceptsToPen(chick)
                        && (LactationEnabled() ? pen.AcceptsToPen(young)
                            : !pen.AcceptsToPen(young)));
                    pen.AnimalFilter.SetAllow(youngDef,true);
                    pen.AnimalFilter.SetAllow(nonLactatingDef,false);
                    Check("vanilla pen excludes only non-lactating adult females",
                        pen.AcceptsToPen(adult)
                        && !pen.AcceptsToPen(nonLactatingAdult)
                        && pen.AcceptsToPen(adultMale)
                        && pen.AcceptsToPen(young)
                        && pen.AcceptsToPen(chick));
                }

                // Killed residents become real corpses in the owner. They
                // must be placed outside immediately after the next barn tick.
                var doomed=Animal(map,"Cow",4f);
                Hold(barn,doomed);
                doomed.Kill(null);
                var corpse=doomed.Corpse;
                Check("death initially creates a real held corpse",
                    corpse!=null && corpse.ParentHolder==barn);
                if(corpse!=null)
                {
                    Check("corpse always rejected as feed",
                        !barn.GetParentStoreSettings().AllowedToAccept(corpse));
                    barn.DoTick();
                    Check("dead resident's corpse expelled intact",
                        corpse.Spawned && corpse.Map==map
                        && !Owner(barn).Contains(corpse));
                }
            }
            finally
            {
                barn.Destroy();
            }
        }

        static void NursingBalance(Map map)
        {
            if (!LactationEnabled())
            {
                Write("SKIP nursing transfer tests: lactation disabled");
                return;
            }

            var source=Barn(map);
            var dest=Barn(map);
            var settings=ModConstants.Settings;
            float oldCapacity=settings.AnimalBarnMaximumBodySize;
            try
            {
                settings.AnimalBarnMaximumBodySize=100f;
                var sourceYoung=new System.Collections.Generic.List<Pawn>();
                var destYoung=new System.Collections.Generic.List<Pawn>();
                for(int i=0;i<10;i++)
                {
                    Pawn a=Animal(map,"Pig",.02f);
                    Pawn b=Animal(map,"Pig",.02f);
                    Hold(source,a);
                    Hold(dest,b);
                    sourceYoung.Add(a);
                    destYoung.Add(b);
                }

                Pawn first=Animal(map,"Pig",3f);
                Pawn second=Animal(map,"Pig",3f);
                HediffDef lactation=AnimalLactationUtility.LactatingHediffDef;
                if(lactation==null) throw new Exception("No lactation hediff");
                first.health.AddHediff(lactation);
                second.health.AddHediff(lactation);
                Hold(source,first);
                Hold(source,second);

                var mothers=new System.Collections.Generic.List<Pawn>
                    {first,second};
                var empty=new System.Collections.Generic.List<Pawn>();

                Type engine=typeof(Building_AnimalBarn).Assembly
                    .GetType("ZoologyMod.AnimalNursingBalance");
                MethodInfo improves=engine?.GetMethod("BalancingImproves",All);
                Check("nursing balance evaluation available",improves!=null);
                if(improves==null)return;

                bool balance=(bool)improves.Invoke(null,
                    new object[]{sourceYoung,mothers,destYoung,empty,second});
                Check("ten piglets with two mothers send one to unserved ten",
                    balance);

                bool orphan=(bool)improves.Invoke(null,
                    new object[]{sourceYoung,
                        new System.Collections.Generic.List<Pawn>{first},
                        destYoung,empty,first});
                Check("last compatible mother is never taken from her young",
                    !orphan);

                // All age filters except milk-dependent young are excluded,
                // but a nursing adult must remain admissible.
                SpecialThingFilterDef adult=SpecialThingFilterDef.Named(
                    "Zoology_AllowReproductiveAdults");
                SpecialThingFilterDef juvenile=SpecialThingFilterDef.Named(
                    "Zoology_AllowJuveniles");
                SpecialThingFilterDef milk=SpecialThingFilterDef.Named(
                    "Zoology_AllowMilkDependentYoung");
                dest.AnimalFilter.SetAllow(adult,false);
                dest.AnimalFilter.SetAllow(juvenile,false);
                dest.AnimalFilter.SetAllow(milk,true);
                Check("lactating adult fits a milk-only barn's filters",
                    dest.AnimalFilter.Allows(first)
                    && (bool)Call(dest,"CanAcceptTransferredResident",first));

                MethodInfo refresh=engine.GetMethod("Get",All);
                refresh?.Invoke(null,new object[]{map,true});
                var sources=(System.Collections.Generic.IEnumerable<Thing>)
                    engine.GetMethod("WorkSources",All).Invoke(null,new object[]{map});
                Check("cached nursing work targets milk-deficient destination",
                    sources.Contains(source));

                // Once the two herds have one mother each, moving her
                // back would make the target's children unsupported.
                mothers.Remove(second);
                var destinationMothers=new System.Collections.Generic.List<Pawn>
                    {second};
                bool undo=(bool)improves.Invoke(null,
                    new object[]{sourceYoung,mothers,destYoung,destinationMothers,first});
                Check("nursing transfer does not oscillate back to source",
                    !undo);

                Check("nursing worker uses ordinary Handling work type",
                    DefDatabase<WorkGiverDef>.GetNamed("Zoology_NursingBalance")
                        .workType == WorkTypeDefOf.Handling
                    && DefDatabase<JobDef>.GetNamed(
                        "Zoology_NursingRelocateFromBarn").driverClass
                        == typeof(JobDriver_BarnRelocate));
            }
            finally
            {
                settings.AnimalBarnMaximumBodySize=oldCapacity;
                source.Destroy();
                dest.Destroy();
            }
        }

        static void FeedDistribution(Map map)
        {
            var nearby=Barn(map);
            var distant=Barn(map);
            var empty=Barn(map);
            try
            {
                var work=new WorkGiver_BarnFeed();
                Check("barn feed work giver uses vanilla prioritized scan",
                    work.Prioritized);
                Hold(nearby,Animal(map,"Cow",4f));
                Hold(distant,Animal(map,"Cow",4f));
                Thing hay=Food(nearby,"Hay",75);
                float stocked=work.GetPriority(null,new TargetInfo(nearby));
                float hungry=work.GetPriority(null,new TargetInfo(distant));
                float unused=work.GetPriority(null,new TargetInfo(empty));
                Check("empty occupied barn ranks above stocked one",
                    hungry>stocked && stocked>unused);

                // Both barns have the same food and diet, but two residents
                // consume twice as much as one. Nutrition per animal (not
                // raw stack count or travel distance) must determine rank.
                Food(distant,"Hay",75);
                Call(distant,"InvalidateFoodRestockPriority");
                float oneCow=work.GetPriority(null,new TargetInfo(distant));
                Hold(distant,Animal(map,"Cow",4f));
                Call(distant,"InvalidateFoodRestockPriority");
                float twoCows=work.GetPriority(null,new TargetInfo(distant));
                Check("food demand rises with additional residents",
                    twoCows>oneCow);

                // The existing stockpile filter should not count meat as
                // nutrition for herbivorous cows.
                Thing meat=Food(distant,"Meat_Cow",10);
                Call(distant,"InvalidateFoodRestockPriority");
                float withMeat=work.GetPriority(null,new TargetInfo(distant));
                Check("unusable meat does not improve herbivore feed coverage",
                    Math.Abs(withMeat-twoCows)<.0001f);
                Check("diet compatibility for available food",
                    (bool)Call(nearby,"HasResidentEater",hay)
                    && !(bool)Call(distant,"HasResidentEater",meat));

                // Dropping actual stored food invalidates the cached supply.
                float beforeDrop=work.GetPriority(null,new TargetInfo(nearby));
                Check("stored food can be dropped normally",
                    nearby.TryDropStoredItem(hay,1));
                float afterDrop=work.GetPriority(null,new TargetInfo(nearby));
                Check("food unloading immediately increases barn restock priority",
                    afterDrop>beforeDrop);
            }
            finally
            {
                nearby.Destroy();
                distant.Destroy();
                empty.Destroy();
            }
        }

        static void Feeding(Map map)
        {
            var b = Barn(map); var p = Animal(map); Hold(b,p);
            foreach (string name in new[]{"Hay","Kibble","RawPotatoes"})
            {
                ClearFood(b); var food = Food(b,name,75); p.needs.food.CurLevelPercentage = p.RaceProps.FoodLevelPercentageWantEat * .7f;
                float start = p.needs.food.CurLevel, per = FoodUtility.NutritionForEater(p,food), wanted = p.needs.food.NutritionWanted;
                int expected = Math.Min(75,Math.Max(1,Mathf.CeilToInt(wanted/per)));
                if(food.def.ingestible.maxNumToIngestAtOnce > 0) expected = Math.Min(expected,food.def.ingestible.maxNumToIngestAtOnce);
                Feed(b);
                Write($"RATION {name} perItem={per:R} start={start:R} end={p.needs.food.CurLevel:R} used={75-Count(b)} expected={expected}");
                Check(name+" real units match vanilla",75-Count(b)==expected);
            }
            ClearFood(b); Food(b,"Hay",1); p.needs.food.CurLevelPercentage=0f; float n=p.needs.food.CurLevel; Feed(b);
            Check("partial stack consumed without invented nutrition", Count(b)==0 && p.needs.food.CurLevel>n && p.needs.food.CurLevelPercentage<.2f);
            ClearFood(b); Food(b,"Hay",75);
            var broken = new BarnThrowingFood { def=ThingDefOf.Hay, stackCount=10 }; broken.PostMake(); Owner(b).TryAdd(broken,false);
            p.needs.food.CurLevel=0f; Feed(b);
            Check("bad ingestible does not block another real stack",p.needs.food.CurLevel>0f && broken.stackCount==10); ClearFood(b);
            // Modify the actual Nutrition stat, as food/stat mods do.
            var modified=Food(b,"Kibble",75); var stat=modified.def.statBases.First(x=>x.stat==StatDefOf.Nutrition);
            float original=stat.value;
            try { stat.value=.137f; StatDefOf.Nutrition.Worker.ClearCacheForThing(modified);
                p.needs.food.CurLevel=0f; float wanted=p.needs.food.NutritionWanted, per=FoodUtility.NutritionForEater(p,modified);
                Feed(b); int used=75-Count(b);
                Write($"MODIFIED perItem={per:R} wanted={wanted:R} used={used} end={p.needs.food.CurLevel:R}");
                Check("modified nutrition determines actual consumed units",used==Math.Min(75,Mathf.CeilToInt(wanted/per)));
            } finally { stat.value=original; StatDefOf.Nutrition.Worker.ClearCacheForThing(modified); }
            ClearFood(b); var meat=Food(b,"Meat_Cow",10); p.needs.food.CurLevel=0f; Feed(b);
            Check("herbivore refuses meat",Count(b)==10 && p.needs.food.CurLevel==0f);
            var pig=Animal(map,"Pig"); Hold(b,pig); pig.needs.food.CurLevelPercentage=.2f; Feed(b);
            Check("mixed species retain diets",p.needs.food.CurLevel==0f && pig.needs.food.CurLevelPercentage>.2f && Count(b)<10);
            ClearFood(b); p.needs.food.CurLevel=0f; Feed(b); Check("empty barn gives no nutrition",p.needs.food.CurLevel==0f);
            var spoiled=Food(b,"Hay",6); b.TryDropStoredItem(spoiled,2);
            Check("partial unloading conserves items",Count(b)==4 && map.listerThings.ThingsOfDef(ThingDefOf.Hay).Any(t=>t.stackCount==2));
            b.Destroy();
        }
        static void Physiology(Map map)
        {
            foreach(string kind in new[]{"Cow","Chicken","Pig"})
            {
                var b=Barn(map); var inside=Animal(map,kind); var outside=Animal(map,kind); Hold(b,inside); PawnUtility.ForceWait(outside,200000,null,true);
                inside.needs.food.CurLevelPercentage=outside.needs.food.CurLevelPercentage=1f;
                int tick=GenTicks.TicksGame; float start=inside.needs.food.CurLevel;
                try { for(int i=1;i<=6000;i++) { Ticks.SetValue(Find.TickManager,tick+i); b.DoTick(); outside.DoTick(); } }
                finally { Ticks.SetValue(Find.TickManager,tick); }
                Write($"FAST {kind} ticks=6000 age=3 start={start:R} barn={inside.needs.food.CurLevel:R} map={outside.needs.food.CurLevel:R} rateBarn={inside.needs.food.FoodFallPerTick:R} rateMap={outside.needs.food.FoodFallPerTick:R}");
                Check(kind+" vanilla physiological fasting",Math.Abs(inside.needs.food.CurLevel-outside.needs.food.CurLevel)<.00001f);
                // Equal ration, eating schedule and no movement/pathfinding cost.
                ClearFood(b); Food(b,"Kibble",500); var ration=ThingMaker.MakeThing(ThingDefOf.Kibble); ration.stackCount=500;
                inside.needs.food.CurLevelPercentage=outside.needs.food.CurLevelPercentage=.5f;
                try { for(int i=1;i<=30000;i++) { Ticks.SetValue(Find.TickManager,tick+i); b.DoTick(); outside.DoTick();
                    if(b.IsHashIntervalTick(250) && outside.needs.food.CurLevelPercentage<outside.RaceProps.FoodLevelPercentageWantEat)
                        outside.needs.food.CurLevel+=ration.Ingested(outside,outside.needs.food.NutritionWanted); } }
                finally { Ticks.SetValue(Find.TickManager,tick); }
                Write($"FED {kind} ticks=30000 kibble=500 usedBarn={500-Count(b)} usedMap={500-ration.stackCount} barn={inside.needs.food.CurLevel:R} map={outside.needs.food.CurLevel:R}");
                Check(kind+" controlled ration comparison (same units; residual below one real food unit)",Count(b)==ration.stackCount && Math.Abs(inside.needs.food.CurLevel-outside.needs.food.CurLevel)<=FoodUtility.NutritionForEater(outside,ration)+.00001f);
                b.Destroy();
            }
        }
        static void HungerStates(Map map)
        {
            foreach(string state in new[]{"juvenile","old","sick","starving"})
            {
                float age=state=="juvenile"?.1f:state=="old"?12f:3f;
                var b=Barn(map);var inside=Animal(map,"Cow",age);var outside=Animal(map,"Cow",age);
                Hold(b,inside);PawnUtility.ForceWait(outside,200000,null,true);
                inside.needs.food.CurLevelPercentage=outside.needs.food.CurLevelPercentage=state=="starving"?0f:.8f;
                if(state=="sick") {
                    // Vanilla recovery varies by seeded pawn ID. Stay away
                    // from the .2 stage boundary so distinct IDs remain in
                    // the same physiological stage throughout this comparison.
                    inside.health.AddHediff(HediffDefOf.Malnutrition).Severity=.35f;
                    outside.health.AddHediff(HediffDefOf.Malnutrition).Severity=.35f;
                }
                int tick=GenTicks.TicksGame;long ageBefore=inside.ageTracker.AgeBiologicalTicks;
                try { for(int i=1;i<=6000;i++) { Ticks.SetValue(Find.TickManager,tick+i);b.DoTick();outside.DoTick(); } }
                finally { Ticks.SetValue(Find.TickManager,tick); }
                Write($"STATE {state} age={age} maxBarn={inside.needs.food.MaxLevel:R} maxMap={outside.needs.food.MaxLevel:R} barn={inside.needs.food.CurLevel:R} map={outside.needs.food.CurLevel:R} rateBarn={inside.needs.food.FoodFallPerTick:R} rateMap={outside.needs.food.FoodFallPerTick:R}");
                Check(state+" needs follow real state",Math.Abs(inside.needs.food.CurLevel-outside.needs.food.CurLevel)<=.01f && Math.Abs(inside.needs.food.FoodFallPerTick-outside.needs.food.FoodFallPerTick)<.000001f);
                Check(state+" age advances once",inside.ageTracker.AgeBiologicalTicks-ageBefore==6000);
                if(state=="starving")Check("unfed resident develops real malnutrition",inside.health.hediffSet.HasHediff(HediffDefOf.Malnutrition));
                b.Destroy();
            }
        }
        static void TemperatureExposure(Map map)
        {
            // A barn is only a ThingOwner; it must neither impose a fixed
            // temperature nor suspend the vanilla temperature hediff givers.
            var barn=Barn(map);
            Pawn animal=Animal(map,"Cow",4f);
            Hold(barn,animal);
            int startTick=GenTicks.TicksGame;
            Room room=barn.Position.GetRoom(map);
            float originalTemperature=room?.Temperature ?? 0f;
            try
            {
                Check("contained pawn inherits live map and building cell",
                    animal.MapHeld==map && animal.PositionHeld==barn.Position
                    && animal.SpawnedOrAnyParentSpawned && !animal.Spawned);

                float actual=animal.AmbientTemperature;
                float expected=GenTemperature.GetTemperatureForCell(barn.Position,map);
                Check("barn resident ambient equals real map-cell air temperature",
                    Math.Abs(actual-expected)<.001f);
                Write("BARN_TEMPERATURE ambient="+actual.ToString("R")
                    +" outdoors="+map.mapTemperature.OutdoorTemp.ToString("R")
                    +" room="+(room==null?"null":room.Temperature.ToString("R"))
                    +" roomUsesOutdoor="+(room?.UsesOutdoorTemperature.ToString() ?? "unknown"));

                if(room==null)
                {
                    Check("barn temperature test has valid game room",false);
                    return;
                }

                HediffDef cold=DefDatabase<HediffDef>.GetNamedSilentFail("Hypothermia");
                HediffDef hot=DefDatabase<HediffDef>.GetNamedSilentFail("Heatstroke");
                Check("vanilla temperature hediff definitions exist",
                    cold!=null && hot!=null);
                if(cold==null || hot==null) return;

                // Force the temperature of the ACTUAL room containing the
                // barn, never the held pawn's health or a synthetic stat.
                // Restore the room and global test clock in finally.
                room.Temperature=Math.Min(-80f,animal.SafeTemperatureRange().min-60f);
                Check("cold room is seen immediately through ThingOwner",
                    Math.Abs(animal.AmbientTemperature-room.Temperature)<.001f);
                float beforeCold=animal.health.hediffSet.GetFirstHediffOfDef(cold)?.Severity ?? 0f;
                for(int i=1;i<=180;i++)
                {
                    Ticks.SetValue(Find.TickManager,startTick+i);
                    barn.DoTick();
                }
                float afterCold=animal.health.hediffSet.GetFirstHediffOfDef(cold)?.Severity ?? 0f;
                Check("contained animal develops vanilla hypothermia",
                    !animal.Dead && afterCold>beforeCold+.0001f);

                room.Temperature=Math.Max(110f,animal.SafeTemperatureRange().max+60f);
                Check("hot room is seen immediately through ThingOwner",
                    Math.Abs(animal.AmbientTemperature-room.Temperature)<.001f);
                float beforeHeat=animal.health.hediffSet.GetFirstHediffOfDef(hot)?.Severity ?? 0f;
                for(int i=181;i<=360;i++)
                {
                    Ticks.SetValue(Find.TickManager,startTick+i);
                    barn.DoTick();
                }
                float afterHeat=animal.health.hediffSet.GetFirstHediffOfDef(hot)?.Severity ?? 0f;
                Check("contained animal develops vanilla heatstroke",
                    !animal.Dead && afterHeat>beforeHeat+.0001f);
                Write("BARN_TEMPERATURE coldSeverity="+afterCold.ToString("R")
                    +" heatSeverity="+afterHeat.ToString("R"));
            }
            finally
            {
                Ticks.SetValue(Find.TickManager,startTick);
                if(room!=null) room.Temperature=originalTemperature;
                barn.Destroy();
            }
        }

        static void RotChecks(Map map)
        {
            var b=Barn(map);var food=Food(b,"RawPotatoes",10);
            var outside=ThingMaker.MakeThing(food.def);outside.stackCount=10;GenSpawn.Spawn(outside,CellFinder.RandomClosewalkCellNear(b.Position,map,5),map);
            // The shelter is outdoors and has no cooling. Match initial rot.
            food.TryGetComp<CompRottable>().RotProgress=100f;outside.TryGetComp<CompRottable>().RotProgress=100f;
            int tick=GenTicks.TicksGame;
            try{for(int i=1;i<=3000;i++){Ticks.SetValue(Find.TickManager,tick+i);b.DoTick();
                // A map's TickList dispatches rare items only every 250 ticks.
                if(outside.def.tickerType==TickerType.Normal || outside.IsHashIntervalTick(250))outside.DoTick();}}
            finally{Ticks.SetValue(Find.TickManager,tick);}
            float a=food.TryGetComp<CompRottable>().RotProgress,c=outside.TryGetComp<CompRottable>().RotProgress;
            Write($"ROT barn={a:R} map={c:R}");Check("food rot advances once at ambient temperature",Math.Abs(a-c)<1f);
            var duplicate=ThingMaker.MakeThing(food.def);duplicate.stackCount=7;GenSpawn.Spawn(duplicate,b.InteractionCell,map);
            Check("actual food transfers and merges",b.TryStoreFood(duplicate)&&Count(b)==17&&duplicate.Destroyed);
            b.EjectContents();Check("ejecting food empties owner",Count(b)==0);b.Destroy();
        }
        static void FireChecks(Map map)
        {
            var wood=Barn(map);var p=Animal(map);Hold(wood,p);
            Fire f=(Fire)GenSpawn.Spawn(ThingDefOf.Fire,wood.Position,map); f.fireSize=1.5f;
            float hp=p.health.summaryHealth.SummaryHealthPercent; Call(wood,"TickBarnFire");
            Check("live wood fire burns contained animal",p.health.summaryHealth.SummaryHealthPercent<hp && !p.Spawned);
            f.Destroy(); hp=p.health.summaryHealth.SummaryHealthPercent; Call(wood,"TickBarnFire");
            Check("extinguishing stops burns",Math.Abs(p.health.summaryHealth.SummaryHealthPercent-hp)<.00001f);
            var stone=Barn(map,ThingDef.Named("BlocksGranite"));var safe=Animal(map);Hold(stone,safe);
            Check("stuff changes durability and color",wood.MaxHitPoints!=stone.MaxHitPoints && wood.DrawColor!=stone.DrawColor);
            Write($"STUFF woodHP={wood.MaxHitPoints} stoneHP={stone.MaxHitPoints} woodFlammability={wood.GetStatValue(StatDefOf.Flammability)} stoneFlammability={stone.GetStatValue(StatDefOf.Flammability)}");
            Fire sf=(Fire)GenSpawn.Spawn(ThingDefOf.Fire,stone.Position,map);sf.fireSize=1.5f;hp=safe.health.summaryHealth.SummaryHealthPercent;
            Call(stone,"TickBarnFire");Check("nonflammable stone excludes barn-fire burns",safe.health.summaryHealth.SummaryHealthPercent==hp);sf.Destroy();
            var doomed=Animal(map);Hold(wood,doomed);doomed.Kill(null);
            Check("contained death creates real corpse",doomed.Corpse!=null && doomed.Corpse.ParentHolder==wood && !Owner(wood).Contains(doomed));
            wood.Destroy();Check("collapse releases survivor",!p.Destroyed && p.Spawned);stone.Destroy();
            var timed=Barn(map);var resident=Animal(map);Hold(timed,resident);
            var live=(Fire)GenSpawn.Spawn(ThingDefOf.Fire,timed.Position,map);live.fireSize=.5f;
            int tick=GenTicks.TicksGame;
            float BurnTotal()=>resident.health.hediffSet.hediffs.OfType<Hediff_Injury>().Where(h=>h.def==DamageDefOf.Flame.hediff).Sum(h=>h.Severity);
            try
            {
                for(int i=1;i<=450;i++) { Ticks.SetValue(Find.TickManager,tick+i); if(i==301)live.fireSize=1.6f;timed.DoTick();live.DoTick(); }
                float burning=BurnTotal();
                Check("timed live fire with changing intensity causes burns",burning>0f&&!resident.Spawned);
                foreach(var cell in timed.OccupiedRect())foreach(var fire in map.thingGrid.ThingsListAt(cell).OfType<Fire>().ToArray())fire.Destroy();
                for(int i=451;i<=900;i++) { Ticks.SetValue(Find.TickManager,tick+i);timed.DoTick(); }
                Check("timed extinguishing causes no further burns",BurnTotal()<=burning+.00001f);
                Write($"FIRE ticksBurning=450 sizes=0.5,1.6 burnsBeforeExtinguish={burning:R} after450Extinguished={BurnTotal():R}");
            }
            finally { Ticks.SetValue(Find.TickManager,tick); }
            timed.Destroy();
        }
        void PrepareSave(Map map)
        {
            savedBarn=Barn(map);emptyBarn=Barn(map);
            savedMother=Animal(map);savedBaby=Animal(map,"Cow",.001f);savedPregnant=Animal(map);
            savedBaby.relations.AddDirectRelation(PawnRelationDefOf.Parent,savedMother);
            Hold(savedBarn,savedMother);Hold(savedBarn,savedBaby);Hold(savedBarn,savedPregnant);
            savedPregnant.health.AddHediff(HediffDefOf.Pregnant);
            savedBaby.needs.food.CurLevelPercentage=.05f;savedMother.needs.food.CurLevelPercentage=.9f;
            float m=savedMother.needs.food.CurLevel,b=savedBaby.needs.food.CurLevel;
            Feed(savedBarn);
            Check("hungry newborn restores maternal lactation",savedMother.health.hediffSet.HasHediff(AnimalLactationUtility.LactatingHediffDef));
            Check("milk transfers actual maternal reserves",savedMother.needs.food.CurLevel<m && Math.Abs((m-savedMother.needs.food.CurLevel)-(savedBaby.needs.food.CurLevel-b))<.00001f);
            Food(savedBarn,"Kibble",37);
            savedBarn.AnimalFilter.SetAllow(ThingDef.Named("Chicken"),false);
            savedBarn.AnimalFilter.SetAllow(
                SpecialThingFilterDef.Named("Zoology_AllowJuveniles"),false);
            savedBarn.AnimalFilter.SetAllow(
                SpecialThingFilterDef.Named("Zoology_AllowMilkDependentYoung"),false);
            savedBarn.AnimalFilter.SetAllow(
                SpecialThingFilterDef.Named("Zoology_AllowNonLactatingFemales"),false);
            savedBarn.GetStoreSettings().filter.SetAllow(ThingDefOf.Hay,false);
            savedRequest=Animal(map);Check("manual request queued",savedBarn.RequestManualIntake(savedRequest));
            savedService=Animal(map);
            if(savedService.Spawned) { savedService.DeSpawn();GenSpawn.Spawn(savedService,savedBarn.InteractionCell,map); }
            var resource=savedService.TryGetComp<CompMilkable>();
            typeof(CompHasGatherableBodyResource).GetField("fullness",All).SetValue(resource,1f);
            PawnUtility.ForceWait(savedService,15000,savedBarn,true);
            var cooldown=(System.Collections.Generic.Dictionary<string,int>)typeof(Building_AnimalBarn).GetField("nextServiceTickByPawnId",All).GetValue(savedBarn);
            cooldown[savedMother.ThingID]=GenTicks.TicksGame+60000;
            Call(savedBarn,"RefreshDisplayedAnimals");savedDisplayOrder=DisplayOrder(savedBarn);
            if(!savedBarn.Spawned || !emptyBarn.Spawned)throw new Exception("Test setup lost a barn before save");
            savedCount=savedBarn.AnimalCount;savedFood=Count(savedBarn);savedLevel=savedMother.needs.food.CurLevel;
            savedBodySize=savedBarn.OccupiedAnimalBodySize;
        }
        static void Benchmark(Building_AnimalBarn b,string population)
        {
            var draw=(Action<Vector3,bool>)Delegate.CreateDelegate(typeof(Action<Vector3,bool>),b,typeof(Building_AnimalBarn).GetMethod("DrawAt",All));
            for(int i=0;i<10;i++)draw(b.DrawPos,false);
            long alloc=GC.GetAllocatedBytesForCurrentThread();var clock=Stopwatch.StartNew();
            for(int i=0;i<300;i++)draw(b.DrawPos,false);
            clock.Stop();Write($"DRAW population={population} frames=300 ms={clock.Elapsed.TotalMilliseconds:F2} threadAlloc={GC.GetAllocatedBytesForCurrentThread()-alloc} (vanilla render tree included)");
        }
        // The production admission path is deliberately used here, rather
        // than directly inserting into the ThingOwner as the stress tests do.
        static void IdeologyRanching(Map map)
        {
            if (!ModsConfig.IdeologyActive)
            {
                Write("SKIP ranching thought tests: Ideology not active");
                return;
            }

            Type patch=typeof(Building_AnimalBarn).Assembly.GetType(
                "ZoologyMod.Patch_BarnRanchingAnimalMass");
            Check("Ideology ranching compatibility patch exists",patch!=null);
            var target=AccessTools.Method(typeof(PawnUtility),
                "PlayerAnimalBodySizePerCapita");
            Check("Ideology ranching patch targets actual vanilla density calculation",
                target!=null && patch!=null);
            if(target==null || patch==null) return;

            var playerPawns=PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_OfPlayerFaction;
            int colonists=playerPawns.Count(p=>p.IsFreeColonist && !p.IsQuestLodger());
            if(colonists==0)
            {
                Write("SKIP density delta: fixture has no free colonists");
                return;
            }

            var barn=Barn(map);
            var resident=Animal(map,"Cow",4f);
            float before=PawnUtility.PlayerAnimalBodySizePerCapita();
            try
            {
                Hold(barn,resident);
                float inside=PawnUtility.PlayerAnimalBodySizePerCapita();
                Check("housing a tame animal preserves the vanilla ranching density",
                    !resident.Spawned && resident.ParentHolder==barn
                    && Math.Abs(inside-before)<.0001f);

                bool previous=ModConstants.Settings.EnableAnimalBarn;
                try
                {
                    ModConstants.Settings.EnableAnimalBarn=false;
                    float disabled=PawnUtility.PlayerAnimalBodySizePerCapita();
                    Check("disabling barn support excludes its contained animal mass",
                        Math.Abs(inside-disabled-resident.BodySize/colonists)<.0001f);
                }
                finally
                {
                    ModConstants.Settings.EnableAnimalBarn=previous;
                }

                Check("barn animal contributes again when enabled",
                    Math.Abs(PawnUtility.PlayerAnimalBodySizePerCapita()-inside)<.0001f);

                Check("unloading resident succeeds",barn.TryReleaseAnimal(resident));
                float outside=PawnUtility.PlayerAnimalBodySizePerCapita();
                Check("released animal is counted only once",
                    resident.Spawned && Math.Abs(outside-before)<.0001f);
            }
            finally { barn.Destroy(); }
        }

        static void Capacity(Map map)
        {
            var b=Barn(map);
            Check("barn is available in Misc construction tab",
                b.def.designationCategory?.defName=="Misc");
            Check("barn front beam included in construction ghost",
                b.def.PlaceWorkers.Any(p=>p is PlaceWorker_AnimalBarnFront));
            var settings=ModConstants.Settings;
            float previousCapacity=settings.AnimalBarnMaximumBodySize;
            bool previousEnabled=settings.EnableAnimalBarn;
            try
            {
                Check("default barn capacity is 32.5",Math.Abs(
                    Building_AnimalBarn.MaximumAnimalBodySize-32.5f)<.0001f);
                settings.AnimalBarnMaximumBodySize=10f;
                Check("barn capacity responds to lower slider setting",
                    Math.Abs(Building_AnimalBarn.MaximumAnimalBodySize-10f)<.0001f);
                settings.AnimalBarnMaximumBodySize=100f;
                Check("barn capacity responds to upper slider setting",
                    Math.Abs(Building_AnimalBarn.MaximumAnimalBodySize-100f)<.0001f);
                settings.AnimalBarnMaximumBodySize=999f;
                Check("barn capacity clamps values over 100",
                    Math.Abs(Building_AnimalBarn.MaximumAnimalBodySize-100f)<.0001f);

                settings.EnableAnimalBarn=false;
                typeof(Building_AnimalBarn).GetMethod("SyncConstructionAvailability",All).Invoke(null,null);
                var misc=DefDatabase<DesignationCategoryDef>.GetNamed("Misc");
                Check("disabled barn removed from Misc and runtime jobs",
                    !Building_AnimalBarn.Enabled && b.def.designationCategory==null
                    && !misc.AllResolvedDesignators.Any(d=>d is Designator_Build builder
                        && builder.PlacingDef==b.def));
                settings.EnableAnimalBarn=true;
                typeof(Building_AnimalBarn).GetMethod("SyncConstructionAvailability",All).Invoke(null,null);
                Check("re-enabled barn restores Misc construction entry",
                    Building_AnimalBarn.Enabled && b.def.designationCategory==misc
                    && misc.AllResolvedDesignators.Any(d=>d is Designator_Build builder
                        && builder.PlacingDef==b.def));
            }
            finally
            {
                settings.AnimalBarnMaximumBodySize=previousCapacity;
                settings.EnableAnimalBarn=previousEnabled;
                typeof(Building_AnimalBarn).GetMethod("SyncConstructionAvailability",All).Invoke(null,null);
            }
            Check("buildable low-cover barn uses pass-through-only traversal",
                b.def.passability==Traversability.PassThroughOnly && b.def.fillPercent<1f);
            var cows=new Pawn[10];
            bool allAccepted=true;
            for(int i=0;i<cows.Length;i++)
            {
                cows[i]=Animal(map,"Cow",4f);
                if(!b.CanAcceptAnimal(cows[i])) { allAccepted=false; break; }
                Hold(b,cows[i]);
            }
            Check("ten adult cows fit race-based capacity",allAccepted && b.AnimalCount==10);
            if(cows[0]!=null)
                Check("capacity equals ten adult cow race baseBodySizes",
                    Math.Abs(Building_AnimalBarn.MaximumAnimalBodySize-
                        10f*cows[0].def.race.baseBodySize)<.0001f);
            var eleventh=Animal(map,"Cow",4f);
            Check("additional cow refused by body-size limit",!b.CanAcceptAnimal(eleventh)
                && !b.RequestManualIntake(eleventh));
            Check("occupied body size never exceeds capacity on intake",
                b.OccupiedAnimalBodySize<=Building_AnimalBarn.MaximumAnimalBodySize+.0001f);
            b.Destroy();

            var growing=Barn(map);
            var calf=Animal(map,"Cow",0.1f);
            var matureCow=Animal(map,"Cow",4f);
            Hold(growing,calf);
            Check("calf reserves full race size despite smaller current body",
                calf.BodySize<matureCow.BodySize
                && Math.Abs(growing.OccupiedAnimalBodySize-
                    matureCow.def.race.baseBodySize)<.0001f);
            growing.Destroy();

            var overflow=Barn(map);
            var storedCows=new Pawn[5];
            for(int i=0;i<storedCows.Length;i++)
            {
                storedCows[i]=Animal(map,"Cow",4f);
                Hold(overflow,storedCows[i]);
            }
            float oldLimit=settings.AnimalBarnMaximumBodySize;
            try
            {
                settings.AnimalBarnMaximumBodySize=10f;
                Call(overflow,"EnforceAnimalCapacity");
                Check("lowered capacity ejects most recently loaded residents",
                    overflow.AnimalCount==3
                    && Math.Abs(overflow.OccupiedAnimalBodySize-
                        3f*storedCows[0].def.race.baseBodySize)<.0001f
                    && storedCows[0].ParentHolder==overflow
                    && storedCows[1].ParentHolder==overflow
                    && storedCows[2].ParentHolder==overflow
                    && storedCows[3].Spawned && storedCows[4].Spawned);
                Check("overflow unload keeps every animal alive and accessible",
                    storedCows.All(p=>!p.Destroyed && !p.Dead
                        && (p.Spawned || p.ParentHolder==overflow)));
            }
            finally
            {
                settings.AnimalBarnMaximumBodySize=oldLimit;
                overflow.Destroy();
            }

            var mixed=Barn(map);
            var mother=Animal(map,"Cow",4f);
            var chick=Animal(map,"Chicken",1f);
            Hold(mixed,mother);
            Check("small chicken fits beside cow",chick.BodySize<mother.BodySize
                && mixed.CanAcceptAnimal(chick));
            Hold(mixed,chick);
            Food(mixed,"Hay",25);
            Call(mixed,"RefreshDisplayedAnimals");
            var food=(Thing[])typeof(Building_AnimalBarn).GetField("displayedFood",All).GetValue(mixed);
            Check("real stored food appears in draw cache",
                food.Any(t=>t!=null && t.def==ThingDefOf.Hay));
            mixed.Destroy();
        }

        static void MixedRendering(Map map)
        {
            FoodRendering(map);
            DrawCase(map,"cow and four chickens",new[]{"Cow","Chicken","Chicken","Chicken","Chicken"},5);
            DrawCase(map,"three adult cows",new[]{"Cow","Cow","Cow"},3);
            DrawCase(map,"two cows and two chickens",new[]{"Cow","Cow","Chicken","Chicken"},4);
            DrawCase(map,"twelve chickens",Enumerable.Repeat("Chicken",12).ToArray(),12);
            int adultPigs=DisplayedGroupCount(map,"Pig",4f,24);
            int piglets=DisplayedGroupCount(map,"Pig",.1f,24);
            Check("smaller current-size piglets permit more visible residents",
                piglets>adultPigs);
            Write("GROWTH_VISUAL adultPigSprites="+adultPigs+" pigletSprites="+piglets);
            if(DefDatabase<PawnKindDef>.GetNamedSilentFail("Paraceratherium")!=null)
            {
                var giantBarn=Barn(map);
                var giant=Animal(map,"Paraceratherium",20f);
                Hold(giantBarn,giant);
                Hold(giantBarn,Animal(map,"Chicken",4f));
                Call(giantBarn,"RefreshDisplayedAnimals");
                int count=(int)typeof(Building_AnimalBarn)
                    .GetField("displayedAnimalCount",All).GetValue(giantBarn);
                var visible=(Pawn[])typeof(Building_AnimalBarn)
                    .GetField("displayedAnimals",All).GetValue(giantBarn);
                float graphicWidth=giant.Drawer.renderer.BodyGraphic.drawSize.x;
                Check("oversized paraceratherium renders alone",
                    graphicWidth<=2.76f || count==1 && visible[0]==giant);
                giantBarn.Destroy();
            }
        }

        static int DisplayedGroupCount(Map map,string kind,float age,int number)
        {
            var b=Barn(map);
            for(int i=0;i<number;i++) Hold(b,Animal(map,kind,age));
            Call(b,"RefreshDisplayedAnimals");
            int count=(int)typeof(Building_AnimalBarn)
                .GetField("displayedAnimalCount",All).GetValue(b);
            Check("placement cache preserves "+kind+" age "+age,BarnPerformance.PlacementEquivalent(b));
            b.Destroy();
            return count;
        }

        static void FoodRendering(Map map)
        {
            var barn=Barn(map);
            string[] foods={"Hay","Kibble","RawPotatoes"};
            for(int i=0;i<Building_AnimalBarn.MaximumFoodStacks;i++)
                Food(barn,foods[i%foods.Length],1);
            Call(barn,"RefreshDisplayedAnimals");
            int count=(int)typeof(Building_AnimalBarn)
                .GetField("displayedFoodCount",All).GetValue(barn);
            var visible=(Thing[])typeof(Building_AnimalBarn)
                .GetField("displayedFood",All).GetValue(barn);
            var slots=(Vector2[])typeof(Building_AnimalBarn)
                .GetField("FoodDisplayOffsets",All).GetValue(null);
            bool slotsUnique=true;
            for(int i=0;i<slots.Length;i++)
                for(int j=i+1;j<slots.Length;j++)
                    if(Vector2.Distance(slots[i],slots[j])<.001f)
                        slotsUnique=false;
            Check("eighteen real food stacks cached for drawing",
                barn.StoredFoodStacks==18 && count==18
                && visible.Take(count).All(t=>t!=null && t.ParentHolder==barn));
            Check("eighteen distinct side-shelf positions",
                slots.Length==18 && slotsUnique
                && slots.All(v=>Math.Abs(v.x)>=.7f && Math.Abs(v.x)<1.5f
                    && Math.Abs(v.y)<3f));
            Check("different food types retain individual graphics",
                visible.Take(count).Select(t=>t.def).Distinct().Count()==foods.Length);
            // Rotate the building while retaining real inventory and verify
            // every shelf slot remains populated. Visual orientation of the
            // individual graphics must remain north-facing in-game.
            foreach(Rot4 rotation in new[]{Rot4.North,Rot4.East,Rot4.South,Rot4.West})
            {
                IntVec3 cell=barn.Position;
                barn.DeSpawn();
                GenSpawn.Spawn(barn,cell,map,rotation);
                Call(barn,"DrawAt",barn.DrawPos,false);
                Check("food stacks retained at rotation "+rotation,
                    barn.StoredFoodStacks==18
                    && visible.Take(count).All(t=>t!=null && t.ParentHolder==barn));
            }
            barn.Destroy();
        }

        static void DrawCase(Map map,string label,string[] kinds,int expected)
        {
            var b=Barn(map);
            foreach(string kind in kinds) Hold(b,Animal(map,kind,4f));
            Call(b,"RefreshDisplayedAnimals");
            int count=(int)typeof(Building_AnimalBarn).GetField("displayedAnimalCount",All).GetValue(b);
            var shown=(Pawn[])typeof(Building_AnimalBarn).GetField("displayedAnimals",All).GetValue(b);
            var offsets=(Vector2[])typeof(Building_AnimalBarn).GetField("displayedAnimalOffsets",All).GetValue(b);
            var footprints=(Vector2[])typeof(Building_AnimalBarn).GetField("displayedAnimalFootprints",All).GetValue(b);
            bool nonoverlap=true;
            for(int i=0;i<count;i++)
                for(int j=i+1;j<count;j++)
                    if(Math.Abs(offsets[i].x-offsets[j].x)<(footprints[i].x+footprints[j].x)*.5f
                        && Math.Abs(offsets[i].y-offsets[j].y)<(footprints[i].y+footprints[j].y)*.5f)
                        nonoverlap=false;
            var tileLoads=(float[])typeof(Building_AnimalBarn)
                .GetField("displayedTileBodySizes",All).GetValue(b);
            Check("display "+label,count>=expected && shown.Take(count).All(p=>p!=null)
                && nonoverlap);
            Check("per-tile visual BodySize stays within 1.5",
                tileLoads.Length==18 && tileLoads.All(load=>load>=0f && load<=1.5001f));
            Write("DISPLAY_CASE "+label+" drawn="+count+" bodySize="+b.OccupiedAnimalBodySize
                +" native="+string.Join(",",shown.Take(count).Select(p=>p.def.defName+":"+p.Drawer.renderer.BodyGraphic.drawSize)));
            Check("placement cache preserves "+label,BarnPerformance.PlacementEquivalent(b));
            b.Destroy();
        }

        static void Drawing(Map map)
        {
            var b=Barn(map);var p=Animal(map);Hold(b,p);Call(b,"RefreshDisplayedAnimals");
            Check("barn frame uses map mesh alongside normal adjacent columns",
                b.def.drawerType==DrawerType.MapMeshAndRealTime);
            // Vanilla supplies null to PlaceWorker.DrawGhost during placement.
            // Both Architect construction and Build Copy must use the same
            // null-safe draw path. Do not turn an unexecuted check into PASS.
            var ghostWorker=b.def.PlaceWorkers.OfType<PlaceWorker_AnimalBarnFront>()
                .Single();
            foreach(Rot4 rot in new[]{Rot4.North,Rot4.East,Rot4.South,Rot4.West})
            {
                bool worked=true;
                try
                {
                    ghostWorker.DrawGhost(b.def,b.Position,rot,Color.green,null);
                    ghostWorker.DrawGhost(b.def,b.Position,rot,Color.green,b);
                    GenDraw.DrawInteractionCells(b.def,b.Position,rot);
                }
                catch(Exception ex)
                {
                    worked=false;
                    Write("PREVIEW "+rot+" failed: "+ex);
                }
                Check("ghost and interaction draw calls succeed without exceptions "+rot,
                    worked && b.def.hasInteractionCell
                    && !GenAdj.OccupiedRect(b.Position,rot,b.def.size)
                        .Contains(ThingUtility.InteractionCell(
                            b.def.interactionCellOffset,b.Position,rot)));
            }

            // Warm render tree and material cache before timed frames.
            foreach(Rot4 rot in new[]{Rot4.North,Rot4.East,Rot4.South,Rot4.West}) { var position=b.Position; b.DeSpawn(); GenSpawn.Spawn(b,position,map,rot); Call(b,"DrawAt",b.DrawPos,false); }
            Check("unspawned render at four rotations",!p.Spawned && p.ParentHolder==b);
            Benchmark(b,"1");
            for(int i=1;i<100;i++) {
                Hold(b,Animal(map,i%2==0?"Cow":"Chicken"));
                if(i==5) { Call(b,"RefreshDisplayedAnimals"); Benchmark(b,"6"); }
            }
            Call(b,"RefreshDisplayedAnimals");
            var shown=(Pawn[])typeof(Building_AnimalBarn).GetField("displayedAnimals",All).GetValue(b);
            Check("draw cache never exceeds fixed 72-renderer safety ceiling",
                shown.Count(x=>x!=null)<=72);
            int drawn=(int)typeof(Building_AnimalBarn).GetField("displayedAnimalCount",All).GetValue(b);
            Check("body-size aware renderer displays residents within tile budget",
                drawn>0 && drawn<=72);
            Write("DISPLAY actualDrawCount="+drawn+" cowNativeSize="+p.Drawer.renderer.BodyGraphic.drawSize);
            Write("DISPLAY " + string.Join(",",shown.Where(x=>x!=null).Select(x=>x.ThingID)));
            Check("mixed species visual sample",
                shown.Where(x=>x!=null).Select(x=>x.def).Distinct().Count()>1);
            for(int i=0;i<5;i++)Call(b,"DrawAt",b.DrawPos,false);
            Benchmark(b,"100");
            Check("drawing never spawns residents",b.Animals.All(x=>!x.Spawned));
            foreach(var cell in b.OccupiedRect().ExpandedBy(12)) if(cell.InBounds(map)) map.fogGrid.Unfog(cell);
            CameraJumper.TryJumpAndSelect(b, CameraJumper.MovementMode.Cut);
        }
    }
}
