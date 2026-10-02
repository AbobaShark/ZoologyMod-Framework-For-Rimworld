"""Regression checks for real draft utility methods with isolated game/mod stubs.
Requires Python 3 and .NET 10 SDK; does not replace an in-game compatibility test.
Run: python Zoology/Tests/test_animal_draft_compatibility.py
"""
from pathlib import Path
import subprocess
import shutil
import tempfile

root = Path(__file__).resolve().parents[1] / 'Source' / 'Behaviour' / 'Pets'
temporary = tempfile.TemporaryDirectory(prefix='zoology-draft-tests-')
out = Path(temporary.name)
out.mkdir(exist_ok=True)
source = (root / 'AnimalDraftControl.cs').read_text(encoding='utf-8-sig')

def method(signature):
    start = source.index(signature)
    brace = source.index('{', start)
    depth = 1
    end = brace + 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]

methods = [
    'internal static bool HasDraftControlAccess(Pawn pawn, TrainableDef draftControl)',
    'internal static bool HasDraftControlAccess(Pawn pawn, ThingDef raceDef, TrainableDef draftControl)',
    'internal static bool IsDraftControlCandidate(Pawn pawn)',
    'internal static bool IsDraftControlDraftedPawn(Pawn pawn)',
    'internal static bool ShouldUndraftForMasterState(Pawn pawn)',
    'internal static void UndraftIfMasterUnavailable(Pawn pawn)',
    'internal static void UndraftMasteredAnimalsLeftOnMap(Pawn master, Map previousMap)',
    'internal static bool ShouldLinkTrainables(Pawn pawn)',
    'private static bool HasAnyDraftControlTrainable(ThingDef raceDef)',
]
utility = '''using System; using System.Collections.Generic; using Verse; using RimWorld;
namespace ZoologyMod { internal static class AnimalDraftControlUtility {
internal static bool Enabled=true;
internal static TrainableDef DraftControlTrainable=new(), LegacyDraftControlTrainable=new(), VefDraftControlTrainable=new();
internal static bool IsFeatureEnabledNow()=>Enabled;
internal static bool HasSentienceCatalyst(Pawn p)=>p.Catalyst;
internal static bool IsDormant(Pawn p)=>p.Dormant;
internal static bool TryGetLinkedTrainables(out TrainableDef a,out TrainableDef b){a=TrainableDefOf.AttackTarget;b=DraftControlTrainable;return true;}
''' + '\n'.join(method(m) for m in methods) + '\n}}'
(out / 'Utility.cs').write_text(utility)
for name in ['AnimalDraftCompatibility.cs', 'AnimalDraftControlDefNormalizer.cs']:
    (out / name).write_text((root / name).read_text(encoding='utf-8-sig'))
(out / 'Tests.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>')
(out / 'Program.cs').write_text(r'''
using System; using System.Collections.Generic; using System.Linq; using System.Reflection; using Verse; using RimWorld; using ZoologyMod;
namespace HarmonyLib { static class AccessTools {
public static Type TypeByName(string n) => n.StartsWith("DraftableAnimals") && !Program.DaLoaded ? null : typeof(Program).Assembly.GetType(n);
public static MethodInfo Method(Type t,string n,Type[] args)=>t.GetMethod(n,args);
}}
namespace Verse {
class ThingDef {public ModContentPack modContentPack; public RaceProperties race=new(); public List<CompProperties> comps=new();}
class ModContentPack {public string PackageIdPlayerFacing;}
class RaceProperties {public bool Animal=true,Humanlike;public List<TrainableDef> specialTrainables=new();}
class CompProperties {public Type compClass;}
class Hediff{} class HediffComp{} class HediffWithComps:Hediff{public List<HediffComp> comps=new();}
class Health {public HediffSet hediffSet=new();} class HediffSet {public List<Hediff> hediffs=new();}
class Map {public MapPawns mapPawns=new();} class MapPawns {public List<Pawn> pawns=new();public List<Pawn> SpawnedPawnsInFaction(Faction f)=>pawns;}
class Pawn {public ThingDef def=new();public RaceProperties RaceProps=>def.race;public bool Destroyed,Dead,Downed,Dormant,Catalyst; public Faction Faction=Faction.OfPlayer;public Training training=new();public Drafter drafter=new();public bool Drafted=>drafter?.Drafted==true;public PlayerSettings playerSettings=new();public Health health=new();public Map MapHeld;}
class Drafter {public bool Drafted=true;} class PlayerSettings {public Pawn Master;} class Training {public bool Learned=true,Release;public bool HasLearned(TrainableDef t)=>Learned;}
class Faction {public static Faction OfPlayer=new();} static class Log {public static void Message(string s){}}
static class DefDatabase<T> {public static List<T> AllDefsListForReading=new();}
}
namespace RimWorld {class TrainableDef{} static class TrainableDefOf {public static TrainableDef AttackTarget=new();}}
namespace VEF.AnimalBehaviours {class CompDraftable{} class DerivedDraftable:CompDraftable{} class HediffComp_Draftable:HediffComp{}}
namespace DraftableAnimals {static class DraftableAnimalsUtility {public static bool RequireTraining=true;public static bool IsTrainedToAttack(Pawn p)=>p.RaceProps.Animal && (!RequireTraining || p.training?.Release==true);}}
class Program {
public static bool DaLoaded;static int count;
static void Check(bool v,string name){if(!v)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
static Pawn Own(){var p=new Pawn();p.def.race.specialTrainables.Add(AnimalDraftControlUtility.DraftControlTrainable);return p;}
static void Normalize(){typeof(AnimalDraftControlDefNormalizer).GetField("normalized",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,false);AnimalDraftControlDefNormalizer.NormalizeDefs();}
static void Main(string[] args){DaLoaded=args.Contains("da");
var p=Own();var master=new Pawn();var map=new Map();master.def.race.Humanlike=true;map.mapPawns.pawns.Add(p);p.MapHeld=map;p.playerSettings.Master=master;
if(DaLoaded){p.training.Release=true;
Check(!AnimalDraftControlUtility.IsDraftControlCandidate(p),"DA owns trained pawn even with Zoology training");
Check(!AnimalDraftControlUtility.ShouldLinkTrainables(p),"DA training untouched");p.playerSettings.Master=null;
AnimalDraftControlUtility.UndraftIfMasterUnavailable(p);Check(p.Drafted,"DA no-master draft survives");
p.training.Release=false;Check(AnimalDraftControlUtility.IsDraftControlCandidate(p),"DA ineligible pawn retains Zoology fallback");
DraftableAnimals.DraftableAnimalsUtility.RequireTraining=false;Check(!AnimalDraftControlUtility.IsDraftControlCandidate(p),"DA training toggle respected");
DefDatabase<ThingDef>.AllDefsListForReading.Add(p.def);var original=p.def.race.specialTrainables;Normalize();Check(ReferenceEquals(original,p.def.race.specialTrainables),"DA definitions untouched");
}else{
Check(AnimalDraftControlUtility.IsDraftControlCandidate(p),"own beastmastery candidate");p.playerSettings.Master=null;AnimalDraftControlUtility.UndraftIfMasterUnavailable(p);Check(!p.Drafted,"own no-master undraft preserved");
p=Own();p.playerSettings.Master=master;p.MapHeld=map;map.mapPawns.pawns.Clear();map.mapPawns.pawns.Add(p);AnimalDraftControlUtility.UndraftMasteredAnimalsLeftOnMap(master,map);Check(!p.Drafted,"own master despawn undraft preserved");
p.drafter.Drafted=true;AnimalDraftControlUtility.Enabled=false;p.playerSettings.Master=null;AnimalDraftControlUtility.UndraftIfMasterUnavailable(p);Check(p.Drafted,"disabled feature leaves draft intact");Check(!AnimalDraftControlUtility.ShouldLinkTrainables(p),"disabled training untouched");p.playerSettings.Master=master;AnimalDraftControlUtility.UndraftMasteredAnimalsLeftOnMap(master,map);Check(p.Drafted,"disabled master despawn leaves draft intact");AnimalDraftControlUtility.Enabled=true;
var foreign=new Pawn();AnimalDraftControlUtility.UndraftIfMasterUnavailable(foreign);Check(foreign.Drafted,"unknown external no-master draft survives");foreign.playerSettings.Master=master;foreign.MapHeld=map;map.mapPawns.pawns.Add(foreign);AnimalDraftControlUtility.UndraftMasteredAnimalsLeftOnMap(master,map);Check(foreign.Drafted,"unknown external master despawn draft survives");
var vef=Own();vef.def.comps.Add(new(){compClass=typeof(VEF.AnimalBehaviours.DerivedDraftable)});Check(!AnimalDraftControlUtility.IsDraftControlCandidate(vef),"VEF comp including subclasses wins");Check(!AnimalDraftControlUtility.ShouldLinkTrainables(vef),"VEF comp training untouched");AnimalDraftControlUtility.UndraftIfMasterUnavailable(vef);Check(vef.Drafted,"VEF no-master draft survives");
var hunter=Own();hunter.Catalyst=true;hunter.health.hediffSet.hediffs.Add(new HediffWithComps{comps=new(){new VEF.AnimalBehaviours.HediffComp_Draftable()}});Check(!AnimalDraftControlUtility.IsDraftControlCandidate(hunter),"hunter/defender hediff wins over catalyst and own training");AnimalDraftControlUtility.UndraftIfMasterUnavailable(hunter);Check(hunter.Drafted,"hunter/defender draft survives");hunter.health.hediffSet.hediffs.Clear();Check(AnimalDraftControlUtility.IsDraftControlCandidate(hunter),"removed external implant restores fallback");
var native=new ThingDef();native.race.specialTrainables.Add(AnimalDraftControlUtility.VefDraftControlTrainable);Check(!AnimalDraftControlUtility.HasDraftControlAccess(null,native,AnimalDraftControlUtility.DraftControlTrainable),"native VEF trainable is not Zoology ownership");
var alpha=new ThingDef{modContentPack=new(){PackageIdPlayerFacing="sarg.alphaanimals"}};alpha.race.specialTrainables.Add(AnimalDraftControlUtility.VefDraftControlTrainable);alpha.comps.Add(new(){compClass=typeof(VEF.AnimalBehaviours.CompDraftable)});Check(AnimalDraftControlUtility.HasDraftControlAccess(null,alpha,AnimalDraftControlUtility.DraftControlTrainable),"Alpha bridge preserved");
DefDatabase<ThingDef>.AllDefsListForReading.AddRange(new[]{native,vef.def,alpha});var origNative=native.race.specialTrainables;var origVef=vef.def.race.specialTrainables;Normalize();Check(ReferenceEquals(origNative,native.race.specialTrainables),"native VEF definitions preserved");Check(ReferenceEquals(origVef,vef.def.race.specialTrainables),"VEF comp definitions preserved");Check(alpha.race.specialTrainables.Contains(AnimalDraftControlUtility.DraftControlTrainable)&&!alpha.race.specialTrainables.Contains(AnimalDraftControlUtility.VefDraftControlTrainable),"Alpha normalization preserved");
}
Console.WriteLine($"{count} checks passed");}}
''')
dotnet = shutil.which('dotnet')
if dotnet is None:
    dotnet = str(Path('C:/Program Files/dotnet/dotnet.exe'))
subprocess.run([dotnet, 'run', '--project', str(out / 'Tests.csproj')], check=True)
subprocess.run([dotnet, 'run', '--no-build', '--project', str(out / 'Tests.csproj'), '--', 'da'], check=True)
