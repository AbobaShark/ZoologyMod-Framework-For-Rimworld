using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;
using Verse.AI;
using HarmonyLib;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace ZoologyValidation
{
    // No reference to ZoologyMod, so the identical probe runs with the baseline DLL.
    public class PerformanceProbe : GameComponent
    {
        private static bool loaded, done, warmed;
        private static int samples;
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentThread();
        [DllImport("kernel32.dll")] private static extern UIntPtr SetThreadAffinityMask(IntPtr thread, UIntPtr mask);
        [DllImport("kernel32.dll")] private static extern bool GetThreadTimes(IntPtr thread, out long created, out long exited, out long kernel, out long user);
        private static long SimulationCpuTime()
        {
            if (!GetThreadTimes(GetCurrentThread(), out _, out _, out long kernel, out long user))
                throw new InvalidOperationException("GetThreadTimes failed");
            return kernel + user; // FILETIME units: 100 ns, excludes descheduling by other applications.
        }
        private static readonly string Folder = GenFilePaths.SaveDataFolderPath;
        private static readonly string Mode = File.ReadAllText(Path.Combine(Folder, "benchmark-mode.txt")).Trim();
        private static readonly Dictionary<MethodBase, (long ticks, int calls)> timings = new Dictionary<MethodBase, (long, int)>();
        public PerformanceProbe(Game game) { }
        private static Pawn Spawn(string kind, Map map)
        {
            Pawn pawn = PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed(kind));
            GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(map.Center, map, 85), map);
            return pawn;
        }
        public override void GameComponentUpdate()
        {
            if (done || Find.CurrentMap == null) return;
            try
            {
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                if (!loaded)
                {
                    loaded = true;
                    if (!File.Exists(GenFilePaths.FilePathForSavedGame("ZoologyPerfStart")))
                    {
                        Rand.PushState(1729);
                        for (int i = 0; i < 400; i++)
                            Spawn(i % 10 == 0 ? "Wolf_Timber" : i % 10 == 1 ? "Cobra" : i % 3 == 0 ? "Rat" : i % 3 == 1 ? "Squirrel" : "Hare", Find.CurrentMap);
                        Rand.PopState();
                        GameDataSaveLoader.SaveGame("ZoologyPerfStart");
                    }
                    GameDataSaveLoader.LoadGame("ZoologyPerfStart");
                    return;
                }
                Action tick = (Action)Delegate.CreateDelegate(typeof(Action), Find.TickManager,
                    typeof(TickManager).GetMethod("DoSingleTick", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
                if (!warmed)
                {
                    // Warm JIT with a disposable pass, then restore the exact original world.
                    warmed = true; Rand.PushState(1729);
                    for (int i = 0; i < 3000; i++) tick();
                    Rand.PopState(); GameDataSaveLoader.LoadGame("ZoologyPerfStart"); return;
                }
                done = true;
                if (File.Exists(Path.Combine(Folder, "profile-methods.txt")))
                {
                    var harmony = new Harmony("local.zoology.validation.profiler");
                    foreach (string name in File.ReadAllLines(Path.Combine(Folder, "profile-methods.txt")))
                    {
                        string[] parts = name.Split(':');
                        Type type = AccessTools.TypeByName("ZoologyMod." + parts[0]);
                        MethodInfo method = AccessTools.Method(type, parts[1]);
                        if (method != null) harmony.Patch(method, new HarmonyMethod(typeof(PerformanceProbe), nameof(ProfileBefore)), new HarmonyMethod(typeof(PerformanceProbe), nameof(ProfileAfter)));
                    }
                }
                Rand.PushState(1729);
                for (int i = 0; i < 300; i++) tick();
                Measure(tick, "normal", 2000);
                foreach (var entry in timings.OrderByDescending(e => e.Value.ticks))
                    File.AppendAllText(Path.Combine(Folder, "performance.txt"), $"PROFILE {entry.Key.DeclaringType.Name}.{entry.Key.Name}: ms={1000d*entry.Value.ticks/Stopwatch.Frequency:F3}, calls={entry.Value.calls}" + Environment.NewLine);
                int repeats = File.Exists(Path.Combine(Folder, "repeats.txt")) ? int.Parse(File.ReadAllText(Path.Combine(Folder, "repeats.txt"))) : 1;
                if (++samples < repeats)
                {
                    Rand.PopState(); done = false; GameDataSaveLoader.LoadGame("ZoologyPerfStart"); return;
                }
                if (Mode == "updated")
                {
                    Map map = Find.CurrentMap;
                    foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned.Where(p => p.RaceProps.Animal).Take(100).ToList())
                        pawn.health.PostApplyDamage(new DamageInfo(DefDatabase<DamageDef>.GetNamed("ZVenom_Damage_CobraBite"), .02f), .02f);
                    for (int i = 0; i < 20; i++)
                    {
                        Pawn holder = Spawn("Zoology_ValidationConstrictor", map), prey = Spawn("Colonist", map);
                        prey.health.AddHediff(HediffDefOf.Anesthetic);
                        holder.Position = prey.Position + IntVec3.North;
                        holder.jobs.StartJob(JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Zoology_Constrict"), prey), JobCondition.InterruptForced);
                    }
                    int occupants = 0;
                    foreach (Pawn rat in map.mapPawns.AllPawnsSpawned.Where(p => p.def.defName == "Rat").Take(40).ToList())
                    {
                        rat.needs.rest.CurLevelPercentage = .1f;
                        Thing refuge = GenSpawn.Spawn(DefDatabase<ThingDef>.GetNamed("Zoology_AnimalBurrow"), rat.Position, map);
                        refuge.GetType().GetMethod("InitializeFor").Invoke(refuge, new object[] { rat });
                        if ((bool)refuge.GetType().GetMethod("TryAcceptThing").Invoke(refuge, new object[] { rat, false })) occupants++;
                    }
                    File.AppendAllText(Path.Combine(Folder, "performance.txt"), "Stress contained rats=" + occupants + Environment.NewLine);
                    Measure(tick, "100venoms_20holds_40burrows", 1000);
                }
                Rand.PopState();
                File.AppendAllText(Path.Combine(Folder, "performance.txt"), "COMPLETE " + Mode + Environment.NewLine);
            }
            catch (Exception ex)
            {
                done = true;
                File.AppendAllText(Path.Combine(Folder, "performance.txt"), "EXCEPTION " + ex + Environment.NewLine);
            }
        }
        private static void ProfileBefore(out long __state) => __state = Stopwatch.GetTimestamp();
        private static void ProfileAfter(MethodBase __originalMethod, long __state)
        {
            timings.TryGetValue(__originalMethod, out var value);
            timings[__originalMethod] = (value.ticks + Stopwatch.GetTimestamp() - __state, value.calls + 1);
        }
        private static void Measure(Action tick, string scenario, int count)
        {
            Map map = Find.CurrentMap;
            int pawns = map.mapPawns.AllPawnsSpawned.Count;
            int animals = map.mapPawns.AllPawnsSpawned.Count(p => p.RaceProps.Animal);
            int trees = map.listerThings.AllThings.Count(t => t is Plant p && p.def.plant.IsTree);
            int burrows = map.listerThings.AllThings.Count(t => t.def.defName == "Zoology_AnimalBurrow");
            bool pin = File.Exists(Path.Combine(Folder, "pin-benchmark-thread.txt"));
            UIntPtr previousAffinity = UIntPtr.Zero;
            ThreadPriority previousPriority = Thread.CurrentThread.Priority;
            if (pin)
            {
                previousAffinity = SetThreadAffinityMask(GetCurrentThread(), (UIntPtr)(1UL << Math.Max(0, Environment.ProcessorCount - 2)));
                Thread.CurrentThread.Priority = ThreadPriority.Highest;
            }
            long cpuStart = SimulationCpuTime();
            var timer = Stopwatch.StartNew();
            int startTick = GenTicks.TicksGame;
            for (int i = 0; i < count; i++) tick();
            timer.Stop();
            double cpuMs = (SimulationCpuTime() - cpuStart) / 10000d;
            if (pin)
            {
                Thread.CurrentThread.Priority = previousPriority;
                if (previousAffinity != UIntPtr.Zero) SetThreadAffinityMask(GetCurrentThread(), previousAffinity);
            }
            int totalAnimals = map.mapPawns.AllPawnsSpawned.Count(p => p.RaceProps.Animal) + map.listerThings.AllThings.Where(t => t.def.defName == "Zoology_AnimalBurrow")
                .OfType<Building_Casket>().Sum(b => b.GetDirectlyHeldThings().OfType<Pawn>().Count(p => p.RaceProps.Animal));
            File.AppendAllText(Path.Combine(Folder, "performance.txt"),
                $"{Mode} {scenario}: sample={samples+1}, pinned={pin}, version={VersionControl.CurrentVersionStringWithRev}, ticks={count}, tickDelta={GenTicks.TicksGame-startTick}, ms={timer.Elapsed.TotalMilliseconds:F3}, ms/tick={timer.Elapsed.TotalMilliseconds/count:F4}, uncappedTPS={count/timer.Elapsed.TotalSeconds:F2}, cpuMs={cpuMs:F3}, cpuMs/tick={cpuMs/count:F4}, cpuTPS={count*1000/cpuMs:F2}, pawnsStart={pawns}, spawnedAnimalsStart={animals}, animalsWithContained={totalAnimals}, trees={trees}, burrows={burrows}, spawnedAnimalsEnd={map.mapPawns.AllPawnsSpawned.Count(p=>p.RaceProps.Animal)}" + Environment.NewLine);
            Pawn hunter = map.mapPawns.AllPawnsSpawned.First(p => p.def.defName == "Wolf_Timber");
            Pawn target = map.mapPawns.AllPawnsSpawned.First(p => p.def.defName == "Hare");
            timer.Restart();
            for (int i = 0; i < 10000; i++) FoodUtility.GetPreyScoreFor(hunter, target);
            timer.Stop();
            File.AppendAllText(Path.Combine(Folder, "performance.txt"), $"{Mode} {scenario} preyScore 10000={timer.Elapsed.TotalMilliseconds:F3}ms" + Environment.NewLine);
        }
    }
}
