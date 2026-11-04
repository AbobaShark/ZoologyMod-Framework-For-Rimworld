// Actual TickManager/world/map/AI benchmark; never part of the distributed mod.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using ZoologyMod;

namespace ZoologyValidation
{
    public class BarnPerformance : GameComponent
    {
        const int Population=1000, Warmup=1500, Window=500, Windows=12, DrawFrames=120;
        static string PathInProfile(string name)=>Path.Combine(GenFilePaths.SaveDataFolderPath,name);
        static string Report=>PathInProfile("barn-performance.txt");
        static readonly BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        static bool patched, profileActive, drawActive;
        static readonly List<double> drawMs=new List<double>();
        static readonly List<long> drawBytes=new List<long>();
        sealed class Sample { public long ticks; public int calls; }
        static readonly Dictionary<MethodBase,Sample> profiles=new Dictionary<MethodBase,Sample>();
        static FieldInfo placementCache;
        static int releaseTraces;
        static void TraceRelease(Building_AnimalBarn __instance,MethodBase __originalMethod) {
            if(releaseTraces++<5)Write("RELEASE_TRACE tick="+GenTicks.TicksGame+" barn="+__instance.ThingID+" method="+__originalMethod.Name+" "+Environment.StackTrace);
        }
        static void ReferencePlacementPrefix(Building_AnimalBarn __instance) {
            ((HashSet<float>)placementCache.GetValue(__instance)).Clear();
        }
        public static bool PlacementEquivalent(Building_AnimalBarn barn) {
            placementCache=typeof(Building_AnimalBarn).GetField("failedDisplayPlacements",All);
            if(placementCache==null)return true;
            var refresh=AccessTools.Method(typeof(Building_AnimalBarn),"RefreshDisplayedAnimals");
            string Layout() {
                var pawns=(Pawn[])typeof(Building_AnimalBarn).GetField("displayedAnimals",All).GetValue(barn);
                var offsets=(Vector2[])typeof(Building_AnimalBarn).GetField("displayedAnimalOffsets",All).GetValue(barn);
                var footprints=(Vector2[])typeof(Building_AnimalBarn).GetField("displayedAnimalFootprints",All).GetValue(barn);
                var budgets=(float[])typeof(Building_AnimalBarn).GetField("displayedTileBodySizes",All).GetValue(barn);
                var food=(Thing[])typeof(Building_AnimalBarn).GetField("displayedFood",All).GetValue(barn);
                int count=(int)typeof(Building_AnimalBarn).GetField("displayedAnimalCount",All).GetValue(barn);
                return count+"|"+string.Join(",",Enumerable.Range(0,count).Select(i=>pawns[i].ThingID+"@"+offsets[i].x.ToString("R")+"/"+offsets[i].y.ToString("R")+"/"+footprints[i].x.ToString("R")+"/"+footprints[i].y.ToString("R")))
                    +"|"+string.Join(",",budgets.Select(v=>v.ToString("R")))+"|"+string.Join(",",food.Select(t=>t?.ThingID??"null"));
            }
            refresh.Invoke(barn,null);string expected=Layout();
            var h=new Harmony("Zoology.BarnPerformance.LayoutEquivalence");
            h.Patch(AccessTools.Method(typeof(Building_AnimalBarn),"TryPlaceDisplayedAnimal"),new HarmonyMethod(typeof(BarnPerformance),nameof(ReferencePlacementPrefix)));
            try { refresh.Invoke(barn,null);return Layout()==expected; }
            finally {h.UnpatchAll(h.Id);}
        }
        List<Pawn> chickens;
        List<Building_AnimalBarn> barns;
        readonly List<double> tickWindows=new List<double>();
        readonly List<long> allocations=new List<long>();
        int state, warmTicks, measured;
        string scenario;
        bool active;
        public BarnPerformance(Game game) { }
        public override void LoadedGame() {
            if(File.Exists(PathInProfile("barn-performance-enabled.txt")))Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
        }
        static void Write(string s) { File.AppendAllText(Report,s+Environment.NewLine); Log.Message("[Barn performance] "+s); }
        static void Require(bool condition,string message) { if(!condition)throw new InvalidOperationException(message); }
        static void DrawPrefix(out (long,long) __state) { __state=drawActive?(Stopwatch.GetTimestamp(),GC.GetAllocatedBytesForCurrentThread()):default; }
        static void DrawPostfix((long,long) __state) {
            if(!drawActive)return;
            drawMs.Add(Stopwatch.GetElapsedTime(__state.Item1).TotalMilliseconds);
            drawBytes.Add(GC.GetAllocatedBytesForCurrentThread()-__state.Item2);
        }
        static void ProfilePrefix(out long __state) { __state=profileActive?Stopwatch.GetTimestamp():0; }
        static void ProfilePostfix(MethodBase __originalMethod,long __state) {
            if(__state==0)return;
            if(!profiles.TryGetValue(__originalMethod,out var s))profiles[__originalMethod]=s=new Sample();
            s.calls++;s.ticks+=Stopwatch.GetTimestamp()-__state;
        }
        static void InstallInstrumentation() {
            if(patched)return; patched=true;
            var h=new Harmony("Zoology.BarnPerformance.Isolated");
            h.Patch(AccessTools.Method(typeof(DynamicDrawManager),"DrawDynamicThings"),
                new HarmonyMethod(typeof(BarnPerformance),nameof(DrawPrefix)),new HarmonyMethod(typeof(BarnPerformance),nameof(DrawPostfix)));
            foreach(var name in new[]{"EjectContents","TryReleaseAnimal"})h.Patch(AccessTools.Method(typeof(Building_AnimalBarn),name),new HarmonyMethod(typeof(BarnPerformance),nameof(TraceRelease)));
        }
        static double Median(IEnumerable<double> values) { var a=values.OrderBy(x=>x).ToArray(); return (a[(a.Length-1)/2]+a[a.Length/2])/2; }
        public override void GameComponentUpdate() {
            if(!File.Exists(PathInProfile("barn-performance-enabled.txt")) || active || state==99 || Find.CurrentMap==null)return;
            active=true;
            try {
                Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
                DebugSettings.enableStoryteller=false;
                if(state==0) {
                    InstallInstrumentation();
                    string stageFile=PathInProfile("barn-performance-stage.txt");
                    scenario=File.Exists(stageFile)?File.ReadAllText(stageFile).Trim():"setup";
                    if(scenario=="complete") { state=99;return; }
                    if(scenario=="setup" || scenario=="load-base") {
                        File.WriteAllText(Report,"RimWorld "+VersionControl.CurrentVersionStringWithRev+Environment.NewLine);
                        Write("BUILD "+File.ReadAllText(PathInProfile("barn-performance-enabled.txt")).Trim());
                        Write("HOST "+Environment.ProcessorCount+" logical processors; "+System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
                        if(scenario=="load-base") {
                            File.WriteAllText(stageFile,"loose-0");state=99;
                            GameDataSaveLoader.LoadGame("ZoologyBarnPerformanceBase");return;
                        }
                        Prepare(Find.CurrentMap); scenario="loose-0";
                        File.WriteAllText(stageFile,scenario);
                        GameDataSaveLoader.SaveGame("ZoologyBarnPerformanceBase");
                    }
                    StartScenario(); state=1; return;
                }
                if(state==1) {
                    int count=Math.Min(Window,Warmup-warmTicks);
                    for(int i=0;i<count;i++)Find.TickManager.DoSingleTick();
                    warmTicks+=count;
                    if(warmTicks>=Warmup) { state=2; Write("WARM "+scenario+" ticks="+warmTicks); }
                    return;
                }
                if(state==2) {
                    long before=GC.GetAllocatedBytesForCurrentThread(),start=Stopwatch.GetTimestamp();
                    for(int i=0;i<Window;i++)Find.TickManager.DoSingleTick();
                    double ms=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    long bytes=GC.GetAllocatedBytesForCurrentThread()-before;
                    tickWindows.Add(ms/Window);allocations.Add(bytes);measured++;
                    Write($"TICK {scenario} window={measured} ticks={Window} msPerTick={ms/Window:F6} bytesPerTick={(double)bytes/Window:F1}");
                    if(measured==Windows) {
                        Write($"SUMMARY {scenario} ticks={Window*Windows} medianMsPerTick={Median(tickWindows):F6} meanMsPerTick={tickWindows.Average():F6} medianBytesPerTick={Median(allocations.Select(x=>(double)x/Window)):F1}");
                        ValidateScenario(); state=3;
                        Find.CameraDriver.SetRootPosAndSize(Find.CurrentMap.Center.ToVector3Shifted(),55f);
                        drawMs.Clear();drawBytes.Clear();drawActive=true;
                    }
                    return;
                }
                if(state==3) {
                    if(drawMs.Count<DrawFrames)return;
                    drawActive=false;
                    Write($"DRAW {scenario} frames={drawMs.Count} medianMs={Median(drawMs):F6} meanMs={drawMs.Average():F6} medianBytes={Median(drawBytes.Select(x=>(double)x)):F0} view={Find.CameraDriver.CurrentViewRect}");
                    if(scenario.StartsWith("barn"))ProfileBarn();
                    string next=scenario=="loose-0"?"barn-0":scenario=="barn-0"?"barn-1":scenario=="barn-1"?"loose-1":"complete";
                    File.WriteAllText(PathInProfile("barn-performance-stage.txt"),next);
                    if(next=="complete") { Write("COMPLETE performance");state=99; }
                    else { state=99;GameDataSaveLoader.LoadGame("ZoologyBarnPerformanceBase"); }
                }
            } catch(Exception ex) { drawActive=profileActive=false;state=99;Write("FAIL "+ex);Log.Error(ex.ToString()); }
            finally { active=false; }
        }
        void StartScenario() {
            var map=Find.CurrentMap;
            // Destroying ancient map structures during setup can spawn a
            // previously enclosed mech. Remove such late arrivals in both
            // branches before timing, including when reusing a base save.
            foreach(var p in map.mapPawns.AllPawnsSpawned.Where(p=>p.def.defName!="Chicken").ToArray())p.Destroy(DestroyMode.Vanish);
            chickens=map.mapPawns.AllPawnsSpawned.Where(p=>p.def.defName=="Chicken").OrderBy(p=>p.thingIDNumber).ToList();
            barns=map.listerThings.ThingsOfDef(Building_AnimalBarn.BarnDef).Cast<Building_AnimalBarn>().OrderBy(b=>b.thingIDNumber).ToList();
            Require(chickens.Count==Population,"Base population must be 1000");
            Require(map.mapPawns.AllPawnsSpawned.Count==Population,"Unexpected extra pawn");
            if(scenario.StartsWith("barn")) {
                int index=0;
                foreach(var p in chickens) {
                    while(index<barns.Count && !barns[index].CanAcceptAnimal(p))index++;
                    Require(index<barns.Count,"Insufficient real barn capacity");
                    p.DeSpawn();GenSpawn.Spawn(p,barns[index].InteractionCell,map);
                    Require(barns[index].TryStoreAnimal(p),"Admission failed");
                }
                var food=map.listerThings.ThingsOfDef(ThingDefOf.Kibble).OrderBy(t=>t.thingIDNumber).ToArray();
                int foodIndex=0;
                foreach(var b in barns)for(int j=0;j<Building_AnimalBarn.MaximumFoodStacks;j++) {
                    var t=food[foodIndex++]; t.DeSpawn();Require(b.GetDirectlyHeldThings().TryAdd(t,false),"Food transfer failed");
                }
            }
            Write($"START {scenario} population={chickens.Count} barns={barns.Count} capacity={Building_AnimalBarn.MaximumAnimalBodySize} chickenBaseSize={chickens[0].def.race.baseBodySize} startTick={GenTicks.TicksGame} foodUnits={FoodUnits()} startNutrition={chickens.Sum(p=>p.needs.food.CurLevel):R}");
            Find.CameraDriver.SetRootPosAndSize(map.Center.ToVector3Shifted(),55f);
        }
        int FoodUnits()=>Find.CurrentMap.listerThings.ThingsOfDef(ThingDefOf.Kibble).Sum(t=>t.stackCount)
            +barns.Sum(b=>b.GetDirectlyHeldThings().Cast<Thing>().Where(t=>t.def==ThingDefOf.Kibble).Sum(t=>t.stackCount));
        void ValidateScenario() {
            int spawned=chickens.Count(p=>p.Spawned),held=chickens.Count(p=>p.ParentHolder is Building_AnimalBarn);
            int grass=Find.CurrentMap.listerThings.ThingsOfDef(ThingDef.Named("Plant_Grass")).Count;
            var jobs=chickens.Where(p=>p.Spawned).GroupBy(p=>p.CurJob?.def.defName??"none").Select(g=>g.Key+":"+g.Count());
            int shown=barns.Sum(b=>(int)typeof(Building_AnimalBarn).GetField("displayedAnimalCount",All).GetValue(b));
            foreach(var b in barns)Write($"BARN {scenario} {b.ThingID} spawned={b.Spawned} destroyed={b.Destroyed} hp={b.HitPoints} animals={b.AnimalCount} body={b.OccupiedAnimalBodySize} position={b.Position}");
            Write($"STATE {scenario} living={chickens.Count(p=>!p.Dead&&!p.Destroyed)} spawned={spawned} held={held} foodUnits={FoodUnits()} nutrition={chickens.Sum(p=>p.needs.food.CurLevel):R} malnutrition={chickens.Count(p=>p.health.hediffSet.HasHediff(HediffDefOf.Malnutrition))} grassPlants={grass} displayed={shown} jobs={string.Join(",",jobs)}");
            string layout=string.Join(";",barns.Select(b=>{
                var pawns=(Pawn[])typeof(Building_AnimalBarn).GetField("displayedAnimals",All).GetValue(b);
                var offsets=(Vector2[])typeof(Building_AnimalBarn).GetField("displayedAnimalOffsets",All).GetValue(b);
                int count=(int)typeof(Building_AnimalBarn).GetField("displayedAnimalCount",All).GetValue(b);
                return b.ThingID+":"+string.Join(",",Enumerable.Range(0,count).Select(i=>pawns[i].ThingID+"@"+offsets[i].x.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"/"+offsets[i].y.ToString("R",System.Globalization.CultureInfo.InvariantCulture)));
            }));
            Write("LAYOUT "+scenario+" "+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(layout))));
            Require(chickens.All(p=>!p.Dead&&!p.Destroyed),"Lost chicken");
            Require(barns.All(b=>b.Spawned&&!b.Destroyed&&b.HitPoints==b.MaxHitPoints),"Damaged benchmark barn");
            var pen=CellRect.CenteredOn(Find.CurrentMap.Center,96,96);
            Require(chickens.All(p=>!p.Spawned||pen.Contains(p.Position)),"Chicken escaped fenced benchmark pen");
            Require(scenario.StartsWith("barn")?held==Population&&spawned==0:spawned==Population&&held==0,"Population moved unexpectedly");
            Require(chickens.All(p=>!p.health.hediffSet.HasHediff(HediffDefOf.Malnutrition)),"Hunger invalidates performance comparison");
        }
        void ProfileBarn() {
            var names=new[]{"Tick","RefreshDisplayedAnimals","TickFeeding","ReleasePatientsForVanillaCare","TickService","EnforceAnimalCapacity","TickBarnFire"};
            var h=new Harmony("Zoology.BarnPerformance.Sections");
            foreach(string name in names)h.Patch(AccessTools.Method(typeof(Building_AnimalBarn),name),new HarmonyMethod(typeof(BarnPerformance),nameof(ProfilePrefix)),new HarmonyMethod(typeof(BarnPerformance),nameof(ProfilePostfix)));
            profiles.Clear();profileActive=true;
            try { for(int i=0;i<1500;i++)Find.TickManager.DoSingleTick(); }
            finally { profileActive=false;h.UnpatchAll(h.Id); }
            foreach(var kv in profiles.OrderByDescending(kv=>kv.Value.ticks))Write($"PROFILE {scenario} {kv.Key.Name} calls={kv.Value.calls} inclusiveMs={1000.0*kv.Value.ticks/Stopwatch.Frequency:F3}");
            ValidatePlacementOptimization();
        }
        void ValidatePlacementOptimization() {
            placementCache=typeof(Building_AnimalBarn).GetField("failedDisplayPlacements",All);
            if(placementCache==null)return;
            var refresh=AccessTools.Method(typeof(Building_AnimalBarn),"RefreshDisplayedAnimals");
            string Layout()=>string.Join(";",barns.Select(b=>{
                var a=(Pawn[])typeof(Building_AnimalBarn).GetField("displayedAnimals",All).GetValue(b);
                var o=(Vector2[])typeof(Building_AnimalBarn).GetField("displayedAnimalOffsets",All).GetValue(b);
                int count=(int)typeof(Building_AnimalBarn).GetField("displayedAnimalCount",All).GetValue(b);
                return string.Join(",",Enumerable.Range(0,count).Select(i=>a[i].ThingID+"@"+o[i].x.ToString("R")+"/"+o[i].y.ToString("R")));
            }));
            for(int i=0;i<3;i++)foreach(var b in barns)refresh.Invoke(b,null);
            string expected=Layout();
            long start=Stopwatch.GetTimestamp();
            for(int i=0;i<10;i++)foreach(var b in barns)refresh.Invoke(b,null);
            double fast=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            var h=new Harmony("Zoology.BarnPerformance.ReferencePlacement");
            h.Patch(AccessTools.Method(typeof(Building_AnimalBarn),"TryPlaceDisplayedAnimal"),new HarmonyMethod(typeof(BarnPerformance),nameof(ReferencePlacementPrefix)));
            double reference;
            try {
                start=Stopwatch.GetTimestamp();
                for(int i=0;i<10;i++)foreach(var b in barns)refresh.Invoke(b,null);
                reference=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                Require(Layout()==expected,"Cached placement changed selected pawns or offsets");
            } finally {h.UnpatchAll(h.Id);}
            Write($"PLACEMENT {scenario} identicalLayout=True refreshesPerBarn=10 cachedMs={fast:F3} referenceMs={reference:F3} referenceIncludesCacheClearHook=True");
        }
        void Prepare(Map map) {
            // A single controlled outdoor pen, no handlers or other animals.
            foreach(var p in map.mapPawns.AllPawnsSpawned.ToArray())p.Destroy(DestroyMode.Vanish);
            foreach(var t in map.listerThings.AllThings.ToArray())
                if(t.def.destroyable && (t is Building || t is Plant || t is Mineable || t is Corpse || t.def.category==ThingCategory.Item))t.Destroy(DestroyMode.Vanish);
            foreach(var p in map.mapPawns.AllPawnsSpawned.ToArray())p.Destroy(DestroyMode.Vanish);
            Write("PREP cleared map");
            var rect=CellRect.CenteredOn(map.Center,96,96);
            var soil=TerrainDefOf.Soil;
            foreach(var c in rect.ExpandedBy(3))if(c.InBounds(map)) { map.terrainGrid.SetTerrain(c,soil);map.roofGrid.SetRoof(c,null);map.fogGrid.Unfog(c); }
            var fenceDef=ThingDef.Named("Fence");
            foreach(var c in rect.EdgeCells) { var f=ThingMaker.MakeThing(fenceDef,ThingDefOf.WoodLog);GenSpawn.Spawn(f,c,map);f.SetFaction(Faction.OfPlayer); }
            Write("PREP terrain and fence");
            var marker=ThingMaker.MakeThing(ThingDef.Named("PenMarker"),ThingDefOf.WoodLog);GenSpawn.Spawn(marker,map.Center,map);marker.SetFaction(Faction.OfPlayer);
            for(int z=rect.minZ+2;z<rect.maxZ-1;z+=2)for(int x=rect.minX+2;x<rect.maxX-1;x+=2) {
                var plant=(Plant)ThingMaker.MakeThing(ThingDef.Named("Plant_Grass"));plant.Growth=1f;GenSpawn.Spawn(plant,new IntVec3(x,0,z),map);
            }
            Rand.PushState(774129);
            try {
                float body=ThingDef.Named("Chicken").race.baseBodySize;
                int perBarn=(int)Math.Floor((Building_AnimalBarn.MaximumAnimalBodySize+.0001f)/body);
                int count=(Population+perBarn-1)/perBarn;
                for(int i=0;i<count;i++) {
                    var b=(Building_AnimalBarn)ThingMaker.MakeThing(Building_AnimalBarn.BarnDef,ThingDefOf.WoodLog);
                    var loc=new IntVec3(map.Center.x-30+(i%10)*7,0,map.Center.z-30+(i/10)*9);
                    GenSpawn.Spawn(b,loc,map);b.SetFaction(Faction.OfPlayer);
                    for(int j=0;j<Building_AnimalBarn.MaximumFoodStacks;j++) {
                        var food=ThingMaker.MakeThing(ThingDefOf.Kibble);food.stackCount=food.def.stackLimit;
                        GenPlace.TryPlaceThing(food,new IntVec3(map.Center.x-20+(i%10)*4,0,map.Center.z+20+(i/10)*4),map,ThingPlaceMode.Near);
                    }
                }
                for(int i=0;i<Population;i++) {
                    var p=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDef.Named("Chicken"),Faction.OfPlayer,forceGenerateNewPawn:true,fixedBiologicalAge:1f,fixedGender:Gender.Female));
                    GenSpawn.Spawn(p,new IntVec3(rect.minX+4+i%40*2,0,rect.minZ+5+i/40*3),map);
                    p.needs.food.CurLevelPercentage=Rand.Range(.35f,.85f);
                    if(p.needs.rest!=null)p.needs.rest.CurLevelPercentage=Rand.Range(.6f,1f);
                    if(i%100==99)Write("PREP generated="+(i+1));
                }
            } finally { Rand.PopState(); }
            Write($"SETUP map={map.Size} pen={rect} plants={map.listerThings.ThingsOfDef(ThingDef.Named("Plant_Grass")).Count} femaleChickens={Population} warmup={Warmup} windows={Windows} ticksPerWindow={Window}");
        }
    }
}
