"""Execute production feeding decisions and toil wiring against instrumented game stubs.

Requires Python 3 and .NET 10 SDK. Run: python Zoology/Tests/test_handler_baby_feeding.py
This checks the mod's decisions and vanilla API contracts, not in-game pathfinding/UI.
"""
from pathlib import Path
import shutil
import re
import subprocess
import tempfile
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
source = root / 'Source'
lactation = source / 'Behaviour' / 'Lactation'
dotnet = shutil.which('dotnet')
if dotnet is None:
    raise SystemExit('Missing .NET SDK')


def class_source(path, signature):
    text = path.read_text(encoding='utf-8-sig')
    start = text.index(signature)
    brace = text.index('{', start)
    end, depth = brace + 1, 1
    while depth:
        depth += (text[end] == '{') - (text[end] == '}')
        end += 1
    return text[start:end]


wg = ET.parse(root / '1.6/Defs/WorkGiverDefs/FeedMammalBaby.xml').getroot()[0]
assert wg.findtext('workType') == 'Handling'
assert int(wg.findtext('priorityInType')) < 60  # Lowest core handler priority: rebalancing pens.
assert wg.findtext('directOrderable') == 'true'
assert wg.findtext('canBeDoneByMechs') == 'false'
assert wg.findtext('requiredCapacities/li') == 'Manipulation'
for language in ('English', 'Russian'):
    keyed = ET.parse(root / f'Languages/{language}/Keyed/Keys.xml').getroot()
    assert keyed.find('Zoology_EnableHandlerBabyFeeding_Label') is not None
    assert keyed.find('Zoology_EnableHandlerBabyFeeding_Desc') is not None

with tempfile.TemporaryDirectory(prefix='zoology-handler-feeding-') as directory:
    out = Path(directory)
    (out / 'Tests.csproj').write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
        '<TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>')
    for name in ('WorkGiver_FeedMammalBaby.cs', 'JobDriver_FeedMammalBaby.cs'):
        shutil.copyfile(lactation / name, out / name)
    (out / 'Cache.cs').write_text('''
using System; using System.Collections.Generic; using System.Runtime.CompilerServices;
using Verse; using RimWorld;
namespace ZoologyMod {
''' + class_source(lactation / 'Patch_FoodUtility_FoodIsSuitable.cs', 'internal static class MammalBabyCache')
        + '\n' + class_source(source / 'CompsAndModExtensions/ModExtensions/ModExtensiom_IsMammal.cs',
                             'internal static class LactationSettingsGate')
        + '\ninternal static class BabyFoodRulesAdapter {\n'
        + class_source(lactation / 'Patch_FoodUtility_FoodIsSuitable.cs',
                       'private static bool TryOverrideFoodIsSuitable(').replace('private static', 'public static', 1)
        + '\n}}')
    stubs_and_tests = r'''
using System; using System.Collections.Generic; using System.Linq;
using Verse; using Verse.AI; using RimWorld; using ZoologyMod;

namespace Verse {
class DefOfAttribute : Attribute {} static class DefOfHelper { public static void EnsureInitializedInCtor(Type t) {} }
class ThingDef {
    public bool Mammal, IsDrug;
    public bool Edible=true, IsNutritionGivingIngestible=true;
    public Type thingClass=typeof(Thing); public IngestibleProperties ingestible=new();
}
class PawnKindDef { public bool Mammal; }
enum DevelopmentalStage { Adult, Baby } class LifeStageDef { public DevelopmentalStage developmentalStage; }
class RaceProperties {
    public bool Animal=true; public List<int> lifeStageAges=new() { 0, 1, 2 };
    public bool CanEverEat(ThingDef d) => d.Edible;
}
class AgeTracker { public LifeStageDef CurLifeStage = new(); public int CurLifeStageIndex; }
class NeedFood { public float NutritionWanted = 0.6f, CurLevelPercentage = 0.2f; public bool Starving; }
class Needs { public NeedFood food = new(); }
class Faction { public static Faction OfPlayer = new(); }
class Game {} static class Current { public static Game Game = new(); }
class MapPawns { public List<Pawn> Pawns = new(); public List<Pawn> PawnsInFaction(Faction f) => Pawns; }
class Map { public MapPawns mapPawns = new(); }
class Thing { public ThingDef def = new(); }
class Corpse : Thing {}
class Pawn : Thing {
    public PawnKindDef kindDef = new(); public RaceProperties RaceProps = new();
    public AgeTracker ageTracker = new(); public Needs needs = new(); public Map Map = new();
    public Map MapHeld => Map;
    public bool Spawned = true, Dead, Destroyed, InMentalState, Reservable = true, LastForced, CanManipulate = true;
    public int thingIDNumber = 1, ReserveCalls; public Faction Faction = Faction.OfPlayer;
    public Inventory inventory = new();
    public bool CanReserve(Pawn baby, int count, int stack, object layer, bool forced) {
        ReserveCalls++; LastForced = forced; return Reservable;
    }
    public Danger NormalMaxDanger() => Danger.Some;
}
class Inventory { public Thing Food; public bool Contains(Thing food) => food != null && Food == food; }
class PawnCapacityDef { public string label = "manipulation"; }
enum Danger { Some, Deadly }
static class Translation { public static string Translate(this string s, params object[] args) => s; }
}
namespace Verse.AI {
enum TargetIndex { A, B } enum PathEndMode { Touch, ClosestTouch }
class JobDef {} class Job { public JobDef def; public Thing A; public Pawn B; public int count; }
static class JobMaker { public static Job MakeJob(JobDef d, Thing a, Pawn b) => new() { def=d, A=a, B=b }; }
static class JobFailReason { public static string Reason; public static void Is(string s) => Reason=s; }
class Toil { public string Name; public Action PreInit; public Func<bool> JumpIf; public Func<object> activeSkill;
    public void AddPreInitAction(Action action) => PreInit=action; }
class Driver { public Pawn pawn; public Job job; public List<Func<bool>> Failures=new(); }
static class ToilExtensions {
    public static void FailOnDespawnedNullOrForbidden(this Driver d, TargetIndex i) {}
    public static void FailOnDestroyedOrNull(this Driver d, TargetIndex i) {}
    public static void FailOn(this Driver d, Func<bool> condition) => d.Failures.Add(condition);
    public static Toil FailOnForbidden(this Toil t, TargetIndex i) => t;
    public static Toil FailOnCannotTouch(this Toil t, TargetIndex i, PathEndMode m) => t;
}
static class Toils_Misc { public static Toil TakeItemFromInventoryToCarrier(Pawn p, TargetIndex i) => new() { Name="inventory" }; }
static class Toils_Goto { public static Toil GotoThing(TargetIndex i, PathEndMode m) => new() { Name="goto"+i }; }
static class Toils_Jump {
    public static Toil JumpIf(Toil target, Func<bool> condition) => new() { Name="jumpIf", JumpIf=condition };
    public static Toil Jump(Toil target) => new() { Name="jump" };
}
}
namespace RimWorld {
enum FoodPreferability { DesperateOnly, MealLavish }
class WorkGiver_FeedPatient {
    public virtual bool ShouldSkip(Pawn p, bool forced=false) => false;
    public virtual Danger MaxPathDanger(Pawn p) => Danger.Deadly;
    public virtual IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn p) => null;
    public virtual bool HasJobOnThing(Pawn p, Thing t, bool forced=false) => false;
    public virtual Job JobOnThing(Pawn p, Thing t, bool forced=false) => null;
    public PawnCapacityDef MissingRequiredCapacity(Pawn p) => p.CanManipulate ? null : new();
}
class JobDriver_FoodFeedPatient : Driver {
    protected Thing Food => job.A; protected Pawn Deliveree => job.B;
    protected virtual IEnumerable<Toil> MakeNewToils() => Array.Empty<Toil>();
    public List<Toil> GetToils() => MakeNewToils().ToList();
}
static class FeedPatientUtility { public static bool IsHungry(Pawn p) => p.needs?.food?.CurLevelPercentage <= 0.32f; }
static class FoodUtility {
    public static Thing InventoryFood, MapFood; public static int InventoryCalls, MapCalls, Counts;
    public static Pawn Eater, Getter; public static bool Desperate;
    static Thing Suitable(Pawn baby, Thing food) => food == null ? null
        : BabyFoodRulesAdapter.TryOverrideFoodIsSuitable(baby, food.def, out bool result) && !result ? null : food;
    public static Thing BestFoodInInventory(Pawn getter, Pawn eater, FoodPreferability min,
        FoodPreferability max, bool allowDrug=true) {
        InventoryCalls++; Eater=eater; Getter=getter;
        if (allowDrug) throw new Exception("Inventory drugs allowed");
        return Suitable(eater, InventoryFood);
    }
    public static Thing BestFoodSourceOnMap(Pawn getter, Pawn eater, bool desperate, out ThingDef foodDef,
        bool allowPlant=true, bool allowDrug=true, bool allowCorpse=true, bool allowDispenserFull=true,
        bool allowDispenserEmpty=true, bool allowHarvest=true) {
        MapCalls++; Eater=eater; Getter=getter; Desperate=desperate;
        if (allowPlant || allowDrug || allowCorpse || allowDispenserFull || allowDispenserEmpty || allowHarvest)
            throw new Exception("Unsupported feeding source allowed");
        Thing chosen=Suitable(eater, MapFood); foodDef=chosen?.def; return chosen;
    }
    public static float GetNutrition(Pawn baby, Thing food, ThingDef def) => 0.05f;
    public static int WillIngestStackCountOf(Pawn baby, ThingDef def, float nutrition) { Counts++; return 12; }
}
static class PawnUtility {
    public static Pawn Baby, Handler; public static int WaitTicks; public static bool Posture, Sleep;
    public static void ForceWait(Pawn baby, int ticks, Thing target, bool maintainPosture, bool maintainSleep) {
        Baby=baby; Handler=(Pawn)target; WaitTicks=ticks; Posture=maintainPosture; Sleep=maintainSleep;
    }
}
static class SkillDefOf { public static object Animals=new(); }
static class Toils_Ingest {
    public static Pawn Chewer, Ingester;
    public static Toil PickupIngestible(TargetIndex i, Pawn p) => new() { Name="pickup" };
    public static Toil ChewIngestible(Pawn p, float mult, TargetIndex i) {
        Chewer=p; if (mult != 1.5f) throw new Exception("Nonvanilla duration"); return new() { Name="chew" };
    }
    public static Toil FinalizeIngest(Pawn p, TargetIndex i) { Ingester=p; return new() { Name="ingest" }; }
}
}
namespace ZoologyMod {
class ZoologyModSettings {
    public static ZoologyModSettings Instance=new(); public static bool EnableMammalLactation=true;
    public bool DisableAllRuntimePatches, EnableHandlerBabyFeeding=true;
}
static class ModConstants { public const bool DefaultEnableHandlerBabyFeeding=true; }
static class ZoologyCacheUtility {
    public static bool HasMammalExtension(ThingDef d) => d?.Mammal == true;
    public static bool HasMammalExtension(PawnKindDef d) => d?.Mammal == true;
}
static class AnimalLactationUtility { public static bool IsAnimalBabyLifeStage(LifeStageDef s) => s?.developmentalStage == DevelopmentalStage.Baby; }
}
namespace UnityEngine { static class Mathf { public static int RoundToInt(float f) => (int)Math.Round(f); } }
namespace Verse {
enum DrugCategory { None, Medical }
class IngestibleProperties {
    public int baseIngestTicks=300; public bool babiesCanIngest=true; public DrugCategory drugCategory;
}
}

class Program {
    static int checks;
    static void Check(bool value, string label) { checks++; if (!value) throw new Exception(label); }
    static Pawn Baby() => new() { def=new() { Mammal=true } };
    static void Main() {
        ZoologyBabyFeedingDefOf.Zoology_FeedMammalBaby=new();
        var giver=new WorkGiver_FeedMammalBaby(); var handler=new Pawn(); var baby=Baby();
        FoodUtility.MapFood=new();
        Check(giver.HasJobOnThing(handler, baby), "Hungry healthy baby can be fed without a bed");
        Check(FoodUtility.Eater == baby && FoodUtility.Getter == handler, "Food chosen for recipient");
        var job=giver.JobOnThing(handler, baby);
        Check(job.A == FoodUtility.MapFood && job.B == baby && job.count == 12, "Vanilla nutrition/stack calculation");
        Check(job.def == ZoologyBabyFeedingDefOf.Zoology_FeedMammalBaby, "Correct job driver");
        Check(giver.MaxPathDanger(handler) == Danger.Some, "Uses handler's normal danger limit");
        baby.needs.food.CurLevelPercentage=0.8f;
        Check(!giver.HasJobOnThing(handler, baby), "Automatic feeding waits for hunger");
        Check(giver.HasJobOnThing(handler, baby, true) && handler.LastForced, "Manual order overrides hunger and passes forced reservation");
        baby.needs.food.NutritionWanted=0;
        Check(!giver.HasJobOnThing(handler, baby, true), "Cannot manually feed a full baby");
        baby.needs.food=new();
        var cases=new Action<Pawn>[] {
            p=>p.Dead=true, p=>p.Destroyed=true, p=>p.Spawned=false, p=>p.InMentalState=true,
            p=>p.Faction=new(), p=>p.needs.food=null, p=>p.ageTracker.CurLifeStageIndex=2,
            p=>p.RaceProps.Animal=false, p=>p.def.Mammal=false
        };
        foreach (var change in cases) {
            var invalid=Baby(); change(invalid);
            int calls=FoodUtility.MapCalls+FoodUtility.InventoryCalls;
            Check(!giver.HasJobOnThing(handler, invalid, true), "Invalid target rejected");
            Check(calls == FoodUtility.MapCalls+FoodUtility.InventoryCalls, "Invalid target incurs no food search");
        }
        Check(!HandlerBabyFeedingUtility.CanFeed(null), "Null target");
        var kindBaby=Baby(); kindBaby.def.Mammal=false; kindBaby.kindDef.Mammal=true;
        Check(HandlerBabyFeedingUtility.CanFeed(kindBaby), "Mammal extension on pawn kind");
        baby.ageTracker.CurLifeStageIndex=2;
        Check(!HandlerBabyFeedingUtility.CanFeed(baby), "Cached infant becoming adult");
        baby.ageTracker.CurLifeStage.developmentalStage=DevelopmentalStage.Baby;
        // LifeStageDefs are immutable at runtime; use a new def to model a stage transition.
        baby.ageTracker.CurLifeStage=new() { developmentalStage=DevelopmentalStage.Baby };
        Check(HandlerBabyFeedingUtility.CanFeed(baby), "Explicit baby developmental stage");
        baby=Baby(); Current.Game=new();
        Check(HandlerBabyFeedingUtility.CanFeed(baby), "New game clears baby cache");
        handler.Reservable=false;
        int scans=FoodUtility.MapCalls;
        Check(!giver.HasJobOnThing(handler, baby) && scans == FoodUtility.MapCalls, "Reservation failure avoids food search");
        handler.Reservable=true; handler.CanManipulate=false;
        Check(!giver.HasJobOnThing(handler, baby) && scans == FoodUtility.MapCalls, "Incapable handler avoids food search");
        Check(JobFailReason.Reason == "CannotMissingHealthActivities", "Capacity failure reason");
        handler.CanManipulate=true;
        foreach (int gate in new[] { 0, 1, 2 }) {
            if (gate == 0) ZoologyModSettings.Instance.EnableHandlerBabyFeeding=false;
            if (gate == 1) ZoologyModSettings.EnableMammalLactation=false;
            if (gate == 2) ZoologyModSettings.Instance.DisableAllRuntimePatches=true;
            scans=FoodUtility.MapCalls+FoodUtility.InventoryCalls;
            Check(giver.ShouldSkip(handler, true) && !giver.HasJobOnThing(handler, baby, true), "Settings disable manual and automatic work");
            Check(giver.JobOnThing(handler, baby, true) == null && !giver.PotentialWorkThingsGlobal(handler).Any(), "Disabled work produces no job/candidates");
            Check(scans == FoodUtility.MapCalls+FoodUtility.InventoryCalls, "Disabled work incurs no search");
            ZoologyModSettings.Instance.EnableHandlerBabyFeeding=true;
            ZoologyModSettings.EnableMammalLactation=true;
            ZoologyModSettings.Instance.DisableAllRuntimePatches=false;
        }
        FoodUtility.InventoryFood=new(); scans=FoodUtility.MapCalls;
        Check(giver.JobOnThing(handler, baby).A == FoodUtility.InventoryFood && scans == FoodUtility.MapCalls,
            "Usable inventory food avoids map scan");
        FoodUtility.InventoryFood=null; FoodUtility.MapFood=null;
        Check(!giver.HasJobOnThing(handler, baby) && JobFailReason.Reason == "NoFood", "No food yields disabled order");
        FoodUtility.MapFood=new() { def=new() { ingestible=new() { babiesCanIngest=false } } };
        Check(!giver.HasJobOnThing(handler, baby), "Existing food filter rejects adult food");
        FoodUtility.MapFood=new() { def=new() { Edible=false } };
        Check(!giver.HasJobOnThing(handler, baby), "Existing food filter respects species diet");
        FoodUtility.MapFood=new(); baby.needs.food.Starving=true;
        Check(giver.HasJobOnThing(handler, baby) && FoodUtility.Desperate, "Starving recipient uses desperate search");
        handler.Map.mapPawns.Pawns.AddRange(new[] { baby, Baby(), new Pawn(), kindBaby });
        handler.Map.mapPawns.Pawns[1].needs.food.CurLevelPercentage=0.9f;
        Check(giver.PotentialWorkThingsGlobal(handler).Count() == 2, "Automatic candidates filter hunger and species");
        job=giver.JobOnThing(handler, baby);
        var driver=new JobDriver_FeedMammalBaby { pawn=handler, job=job };
        var toils=driver.GetToils();
        Check(toils.Select(t=>t.Name).SequenceEqual(new[] { "jumpIf", "gotoA", "pickup", "jump", "inventory", "gotoB", "chew", "ingest" }), "Uses vanilla pickup, travel and ingestion toils");
        Check(!toils[0].JumpIf(), "Map food follows pickup branch");
        handler.inventory.Food=job.A;
        Check(toils[0].JumpIf(), "Inventory food follows inventory branch");
        Check(Toils_Ingest.Chewer == baby && Toils_Ingest.Ingester == baby, "Baby receives vanilla ingestion effects/nutrition");
        toils[6].PreInit();
        Check(PawnUtility.Baby == baby && PawnUtility.Handler == handler && PawnUtility.WaitTicks == 451
            && PawnUtility.Posture && PawnUtility.Sleep, "Baby waits through feeding and preserves posture/sleep");
        Check(!driver.Failures.Any(f=>f()), "Healthy infant can continue feeding");
        ZoologyModSettings.Instance.EnableHandlerBabyFeeding=false;
        Check(driver.Failures.Any(f=>f()), "Disabling feature aborts an existing job");
        ZoologyModSettings.Instance.EnableHandlerBabyFeeding=true;
        baby.ageTracker.CurLifeStageIndex=2;
        Check(driver.Failures.Any(f=>f()), "Growing up aborts an existing feeding job");
        Console.WriteLine($"Passed {checks} handler baby feeding checks.");
    }
}
'''
    # Match the visibility of game API types without modifying production source.
    stubs_and_tests = re.sub(r'\b((?:static )?(?:class|enum)) ', r'public \1 ', stubs_and_tests)
    (out / 'Program.cs').write_text(stubs_and_tests)
    subprocess.run([dotnet, 'run', '--project', str(out / 'Tests.csproj'), '-c', 'Release'], check=True)
