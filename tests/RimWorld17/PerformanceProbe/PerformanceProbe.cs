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
        private static readonly string Scenario = File.Exists(Path.Combine(Folder, "scenario.txt"))
            ? File.ReadAllText(Path.Combine(Folder, "scenario.txt")).Trim() : "normal";
        private static readonly string[] Scenarios = Scenario == "all"
            ? new[] { "normal", "mass-scavenging", "mass-pets", "mass-roamers", "mass-families",
                "predator-prey", "mass-combat", "pig-farm", "combined-stress" }
            : new[] { Scenario };
        private static int scenarioIndex;
        private static float? originalPigRoamMtbDays;
        private static bool pigRoamOverrideActive;
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
                string activeScenario = Scenarios[scenarioIndex];
                if (File.Exists(Path.Combine(Folder, "profile-methods.txt")))
                {
                    var harmony = new Harmony("local.zoology.validation.profiler");
                    foreach (string name in File.ReadAllLines(Path.Combine(Folder, "profile-methods.txt")))
                    {
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        string[] parts = name.Split(':');
                        if (parts.Length != 2) throw new InvalidOperationException("Invalid profile method: " + name);
                        Type type = AccessTools.TypeByName("ZoologyMod." + parts[0]);
                        MethodInfo method = type == null ? null : AccessTools.Method(type, parts[1]);
                        if (method == null) throw new InvalidOperationException("Profile method not found: " + name);
                        harmony.Patch(method, new HarmonyMethod(typeof(PerformanceProbe), nameof(ProfileBefore)), new HarmonyMethod(typeof(PerformanceProbe), nameof(ProfileAfter)));
                    }
                }
                Rand.PushState(1729);
                for (int i = 0; i < 300; i++) tick();
                if (activeScenario != "normal")
                {
                    Rand.PushState(1729);
                    PrepareScenario(Find.CurrentMap, activeScenario);
                    Rand.PopState();
                    if (Mode == "updated" && File.Exists(Path.Combine(Folder, "patch-audit.txt")))
                        AuditThreatJobIndex(Find.CurrentMap);
                    for (int i = 0; i < 300; i++) tick();
                    if (Mode == "updated" && File.Exists(Path.Combine(Folder, "patch-audit.txt")))
                        AuditThreatJobIndex(Find.CurrentMap);
                }
                List<Pawn> scavengerRats = null;
                List<Corpse> scavengerCorpses = null;
                float[] scavengerFoodBefore = null;
                int[] scavengerMissingBefore = null;
                if (activeScenario == "mass-scavenging-ingest")
                {
                    Map map = Find.CurrentMap;
                    scavengerRats = map.mapPawns.AllPawnsSpawned.Where(p => p.def.defName == "Rat").Take(100).ToList();
                    scavengerCorpses = map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse).OfType<Corpse>()
                        .Where(c => c.InnerPawn?.def.defName == "Hare" && c.GetComp<CompRottable>()?.Stage == RotStage.Rotting)
                        .Take(45).ToList();
                    if (scavengerRats.Count != 100 || scavengerCorpses.Count != 45)
                        throw new InvalidOperationException($"Scavenger Ingest fixture incomplete: rats={scavengerRats.Count}, corpses={scavengerCorpses.Count}");
                    scavengerFoodBefore = scavengerRats.Select(p => p.needs.food.CurLevel).ToArray();
                    scavengerMissingBefore = scavengerCorpses.Select(c => c.InnerPawn.health.hediffSet.hediffs.OfType<Hediff_MissingPart>().Count()).ToArray();
                    for (int i = 0; i < scavengerRats.Count; i++)
                    {
                        SetHungry(scavengerRats[i]);
                        scavengerFoodBefore[i] = scavengerRats[i].needs.food.CurLevel;
                        scavengerRats[i].jobs.StartJob(JobMaker.MakeJob(JobDefOf.Ingest, scavengerCorpses[i % scavengerCorpses.Count]), JobCondition.InterruptForced);
                    }
                    File.AppendAllText(Path.Combine(Folder, "performance.txt"),
                        $"FIXTURE mass-scavenging-ingest assignedJobs={scavengerRats.Count} sharedCorpses={scavengerCorpses.Count}" + Environment.NewLine);
                }
                Measure(tick, activeScenario, 2000);
                if (File.Exists(Path.Combine(Folder, "patch-audit.txt")))
                {
                    foreach (MethodBase method in new MethodBase[] {
                        AccessTools.Method(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob)),
                        AccessTools.Method(typeof(Pawn_JobTracker), "CleanupCurrentJob"),
                        AccessTools.Method(typeof(MapPawns), nameof(MapPawns.RegisterPawn)),
                        AccessTools.Method(typeof(MapPawns), nameof(MapPawns.DeRegisterPawn)) })
                    {
                        var patches = Harmony.GetPatchInfo(method);
                        string owners = patches == null ? "none" : string.Join(",", patches.Postfixes.Select(p => p.owner + ":" + p.PatchMethod.DeclaringType.Name));
                        File.AppendAllText(Path.Combine(Folder, "performance.txt"), $"PATCHCHECK {method.DeclaringType.Name}.{method.Name}={owners}" + Environment.NewLine);
                    }
                    if (Mode == "updated") AuditThreatJobIndex(Find.CurrentMap);
                }
                if (scavengerRats != null)
                {
                    int fedRats = 0, missingPartsAdded = 0, destroyedCorpses = 0, ongoingIngest = 0;
                    for (int i = 0; i < scavengerRats.Count; i++)
                    {
                        Pawn rat = scavengerRats[i];
                        if (rat.needs?.food != null && rat.needs.food.CurLevel > scavengerFoodBefore[i] + .001f) fedRats++;
                        if (rat.CurJob?.def == JobDefOf.Ingest) ongoingIngest++;
                    }
                    for (int i = 0; i < scavengerCorpses.Count; i++)
                    {
                        Corpse corpse = scavengerCorpses[i];
                        if (corpse.Destroyed) destroyedCorpses++;
                        else missingPartsAdded += Math.Max(0, corpse.InnerPawn.health.hediffSet.hediffs.OfType<Hediff_MissingPart>().Count() - scavengerMissingBefore[i]);
                    }
                    File.AppendAllText(Path.Combine(Folder, "performance.txt"),
                        $"SCAV_STRESS fedRats={fedRats} missingPartsAdded={missingPartsAdded} destroyedCorpses={destroyedCorpses} ongoingIngest={ongoingIngest}" + Environment.NewLine);
                    if (Mode != "no-mod" && fedRats == 0 && missingPartsAdded == 0 && destroyedCorpses == 0)
                        throw new InvalidOperationException("Scavenger stress produced no real corpse consumption");
                    if (Mode != "no-mod")
                    {
                        var adjacentJobs = scavengerRats.Where(p => p.CurJob?.def == JobDefOf.Ingest
                            && p.CurJob.targetA.Thing is Corpse c && !c.Destroyed
                            && (p.Position - c.Position).LengthHorizontalSquared <= 4)
                            .Select(p => (rat: p, job: p.CurJob, corpse: (Corpse)p.CurJob.targetA.Thing,
                                food: p.needs.food.CurLevel,
                                missing: ((Corpse)p.CurJob.targetA.Thing).InnerPawn.health.hediffSet.hediffs.OfType<Hediff_MissingPart>().Count()))
                            .ToList();
                        for (int i = 0; i < 3000; i++) tick();
                        int stalled = 0, ended = 0, progressed = 0;
                        foreach (var entry in adjacentJobs)
                        {
                            if (!ReferenceEquals(entry.rat.CurJob, entry.job) || entry.corpse.Destroyed) { ended++; continue; }
                            int missingNow = entry.corpse.InnerPawn.health.hediffSet.hediffs.OfType<Hediff_MissingPart>().Count();
                            if (entry.rat.needs.food.CurLevel > entry.food + .001f || missingNow > entry.missing) progressed++;
                            else stalled++;
                        }
                        File.AppendAllText(Path.Combine(Folder, "performance.txt"),
                            $"SCAV_FOLLOWUP adjacentJobs={adjacentJobs.Count} ended={ended} progressed={progressed} stalled={stalled} ticks=3000" + Environment.NewLine);
                        if (adjacentJobs.Count == 0 || stalled != 0)
                            throw new InvalidOperationException("Scavenger Ingest stalled under map load");
                    }
                }
                if (pigRoamOverrideActive)
                {
                    DefDatabase<PawnKindDef>.GetNamed("Pig").race.race.roamMtbDays = originalPigRoamMtbDays;
                    pigRoamOverrideActive = false;
                }
                foreach (var entry in timings.OrderByDescending(e => e.Value.ticks))
                    File.AppendAllText(Path.Combine(Folder, "performance.txt"), $"PROFILE {entry.Key.DeclaringType.Name}.{entry.Key.Name}: ms={1000d*entry.Value.ticks/Stopwatch.Frequency:F3}, calls={entry.Value.calls}" + Environment.NewLine);
                int repeats = File.Exists(Path.Combine(Folder, "repeats.txt")) ? int.Parse(File.ReadAllText(Path.Combine(Folder, "repeats.txt"))) : 1;
                if (++samples < repeats)
                {
                    Rand.PopState(); done = false; GameDataSaveLoader.LoadGame("ZoologyPerfStart"); return;
                }
                if (++scenarioIndex < Scenarios.Length)
                {
                    samples = 0; Rand.PopState(); done = false;
                    GameDataSaveLoader.LoadGame("ZoologyPerfStart"); return;
                }
                if (Mode == "updated" && Scenario == "normal")
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
        private static void AuditThreatJobIndex(Map map)
        {
            Type owner = AccessTools.TypeByName("ZoologyMod.Patch_AnimalFleeFromPredators");
            MethodInfo get = AccessTools.Method(owner, "GetThreatJobPresence17");
            if (get == null) return;
            object index = get.Invoke(null, new object[] { map });
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var targeted = (IEnumerable<Pawn>)index.GetType().GetField("Targeted", flags).GetValue(index);
            var protect = (IEnumerable<Pawn>)index.GetType().GetField("ProtectYoung", flags).GetValue(index);
            MethodInfo threatJob = AccessTools.Method(owner, "IsThreatJob");
            Type protectType = AccessTools.TypeByName("ZoologyMod.ProtectYoungUtility");
            MethodInfo protectJob = AccessTools.Method(protectType, "IsProtectYoungJob", new[] { typeof(Pawn) });
            var expectedTargeted = new List<Pawn>();
            var expectedProtect = new List<Pawn>();
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn == null || pawn.Dead || pawn.Destroyed || pawn.Downed || !pawn.Spawned || pawn.CurJob == null) continue;
                if (pawn.IsAnimal && (pawn.jobs?.curDriver is JobDriver_PredatorHunt
                    || (bool)threatJob.Invoke(null, new object[] { pawn.CurJob, pawn.jobs?.curDriver }))) expectedTargeted.Add(pawn);
                if ((bool)protectJob.Invoke(null, new object[] { pawn })) expectedProtect.Add(pawn);
            }
            bool targetMatch = new HashSet<Pawn>(targeted).SetEquals(expectedTargeted);
            bool protectMatch = new HashSet<Pawn>(protect).SetEquals(expectedProtect);
            File.AppendAllText(Path.Combine(Folder, "performance.txt"),
                $"INDEXCHECK targeted={expectedTargeted.Count} match={targetMatch} protectYoung={expectedProtect.Count} match={protectMatch}" + Environment.NewLine);
            if (!targetMatch || !protectMatch) throw new InvalidOperationException("Threat job index diverged from map job state");
        }
        private static void ProfileAfter(MethodBase __originalMethod, long __state)
        {
            timings.TryGetValue(__originalMethod, out var value);
            timings[__originalMethod] = (value.ticks + Stopwatch.GetTimestamp() - __state, value.calls + 1);
        }
        private static Pawn SpawnNear(string kind, Map map, int radius)
        {
            Pawn pawn = Spawn(kind, map);
            pawn.Position = CellFinder.RandomClosewalkCellNear(map.Center, map, radius);
            pawn.Notify_Teleported(false);
            return pawn;
        }
        private static void SetHungry(Pawn pawn) { if (pawn.needs?.food != null) pawn.needs.food.CurLevelPercentage = .08f; }
        private static void PrepareScenario(Map map, string scenario)
        {
            // Every mode receives the same fixture generator and RNG seed. All scenarios
            // start from the same saved baseline world before these changes are applied.
            int corpses = 0, families = 0, pregnancies = 0, attackJobs = 0;
            if (scenario == "animals-1000")
            {
                int count = map.mapPawns.AllPawnsSpawned.Count(p => p.RaceProps.Animal);
                if (count > 1000) throw new InvalidOperationException("Starting map already has more than 1000 animals");
                while (count++ < 1000) SpawnNear("Rat", map, 60);
                if (Mode == "no-mod" && File.Exists(Path.Combine(Folder, "save-1000-fixture.txt")))
                    GameDataSaveLoader.SaveGame("ZoologyPerf1000");
            }
            if (scenario == "mass-scavenging" || scenario == "mass-scavenging-ingest" || scenario == "combined-stress")
            {
                foreach (Pawn rat in map.mapPawns.AllPawnsSpawned.Where(p => p.def.defName == "Rat").Take(100)) SetHungry(rat);
                for (int i = 0; i < 45; i++)
                {
                    Pawn prey = SpawnNear("Hare", map, 30);
                    prey.Kill(null);
                    CompRottable rot = prey.Corpse?.GetComp<CompRottable>();
                    for (int j = 0; j < 100 && rot?.Stage == RotStage.Fresh; j++) rot.RotProgress += 10000f;
                    if (rot?.Stage == RotStage.Rotting) corpses++;
                }
            }
            if (scenario == "mass-pets" || scenario == "combined-stress")
            {
                foreach (Pawn rat in map.mapPawns.AllPawnsSpawned.Where(p => p.def.defName == "Rat").Take(100).ToList())
                { rat.SetFaction(Faction.OfPlayer); SetHungry(rat); }
                for (int i = 0; i < 8; i++) SpawnNear("Colonist", map, 20).SetFaction(Faction.OfPlayer);
            }
            if (scenario == "mass-roamers" || scenario == "combined-stress")
            {
                // Pigs use the engine's roamer system in 1.7; the short MTB keeps the
                // roaming path exercised during this 2000-tick workload.
                RaceProperties pigRace = DefDatabase<PawnKindDef>.GetNamed("Pig").race.race;
                originalPigRoamMtbDays = pigRace.roamMtbDays;
                pigRoamOverrideActive = true;
                pigRace.roamMtbDays = .02f;
                for (int i = 0; i < 100; i++) SpawnNear("Pig", map, 30).SetFaction(Faction.OfPlayer);
            }
            if (scenario == "mass-families" || scenario == "combined-stress")
            {
                Pawn auditMother = null, auditChild = null;
                for (int i = 0; i < 35; i++)
                {
                    Pawn mother = SpawnNear("Rat", map, 25); mother.gender = Gender.Female;
                    mother.ageTracker.AgeBiologicalTicks = 5 * 3600000L;
                    if (i == 0) auditMother = mother;
                    for (int j = 0; j < 2; j++)
                    {
                        Pawn child = SpawnNear("Rat", map, 25);
                        child.ageTracker.AgeBiologicalTicks = 0;
                        child.relations.AddDirectRelation(PawnRelationDefOf.Parent, mother);
                        child.Position = CellFinder.RandomClosewalkCellNear(mother.Position, map, 2);
                        child.Notify_Teleported(false);
                        if (i == 0 && j == 0) auditChild = child;
                    }
                    families++;
                }
                if (Mode == "updated"
                    && File.Exists(Path.Combine(Folder, "patch-audit.txt")))
                {
                    Pawn aggressor = SpawnNear("Wolf_Timber", map, 20);
                    aggressor.Position = CellFinder.RandomClosewalkCellNear(auditMother.Position, map, 3);
                    aggressor.Notify_Teleported(false);
                    auditMother.jobs.StartJob(JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Zoology_ProtectYoung"), aggressor, auditChild), JobCondition.InterruptForced);
                    File.AppendAllText(Path.Combine(Folder, "performance.txt"), "FIXTURE index-audit forcedProtectYoung=1" + Environment.NewLine);
                }
            }
            if (scenario == "predator-prey" || scenario == "combined-stress")
            {
                for (int i = 0; i < 24; i++) SpawnNear("Wolf_Timber", map, 18);
                for (int i = 0; i < 90; i++) SpawnNear("Hare", map, 18);
            }
            if (scenario == "mass-combat" || scenario == "combined-stress")
            {
                for (int i = 0; i < 30; i++)
                {
                    Pawn a = SpawnNear("Wolf_Timber", map, 14), b = SpawnNear("WildBoar", map, 14);
                    a.jobs.StartJob(JobMaker.MakeJob(JobDefOf.AttackMelee, b), JobCondition.InterruptForced);
                    b.jobs.StartJob(JobMaker.MakeJob(JobDefOf.AttackMelee, a), JobCondition.InterruptForced);
                    attackJobs += 2;
                }
            }
            if (scenario == "pig-farm" || scenario == "combined-stress")
            {
                for (int i = 0; i < 48; i++)
                {
                    Pawn pig = SpawnNear("Pig", map, 22); pig.SetFaction(Faction.OfPlayer);
                    pig.gender = Gender.Female; pig.ageTracker.AgeBiologicalTicks = 5 * 3600000L;
                    Hediff pregnancy = HediffMaker.MakeHediff(HediffDefOf.Pregnant, pig);
                    pregnancy.Severity = .999f; pig.health.AddHediff(pregnancy); pregnancies++;
                }
            }
            if (scenario == "pig-farm-fixed")
            {
                for (int i = 0; i < 48; i++)
                {
                    Pawn mother = SpawnNear("Pig", map, 22); mother.SetFaction(Faction.OfPlayer);
                    mother.gender = Gender.Female; mother.ageTracker.AgeBiologicalTicks = 5 * 3600000L;
                    for (int j = 0; j < 10; j++)
                    {
                        Pawn piglet = SpawnNear("Pig", map, 22); piglet.SetFaction(Faction.OfPlayer);
                        piglet.ageTracker.AgeBiologicalTicks = 0;
                        piglet.relations.AddDirectRelation(PawnRelationDefOf.Parent, mother);
                        piglet.Position = CellFinder.RandomClosewalkCellNear(mother.Position, map, 2);
                        piglet.Notify_Teleported(false);
                    }
                    families++;
                }
            }
            if (scenario is not ("animals-1000" or "mass-scavenging" or "mass-scavenging-ingest" or "mass-pets" or "mass-roamers" or "mass-families" or "predator-prey" or "mass-combat" or "pig-farm" or "pig-farm-fixed" or "combined-stress"))
                throw new InvalidOperationException("Unknown scenario: " + scenario);
            File.AppendAllText(Path.Combine(Folder, "performance.txt"),
                $"FIXTURE {scenario} rottingCorpses={corpses} families={families} pregnancies={pregnancies} attackJobs={attackJobs} animals={map.mapPawns.AllPawnsSpawned.Count(p => p.RaceProps.Animal)}" + Environment.NewLine);
        }
        private static void Measure(Action tick, string scenario, int count)
        {
            Map map = Find.CurrentMap;
            int pawns = map.mapPawns.AllPawnsSpawned.Count;
            int animals = map.mapPawns.AllPawnsSpawned.Count(p => p.RaceProps.Animal);
            int trees = map.listerThings.AllThings.Count(t => t is Plant p && p.def.plant.IsTree);
            int burrows = map.listerThings.AllThings.Count(t => t.def.defName == "Zoology_AnimalBurrow");
            int rottingStart = map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse).OfType<Corpse>()
                .Count(c => c.GetComp<CompRottable>()?.Stage == RotStage.Rotting);
            int ingestStart = map.mapPawns.AllPawnsSpawned.Count(p => p.CurJob?.def == JobDefOf.Ingest);
            int pregnantStart = map.mapPawns.AllPawnsSpawned.Count(p => p.health?.hediffSet?.GetFirstHediffOfDef(HediffDefOf.Pregnant) != null);
            bool pin = File.Exists(Path.Combine(Folder, "pin-benchmark-thread.txt"));
            UIntPtr previousAffinity = UIntPtr.Zero;
            ThreadPriority previousPriority = Thread.CurrentThread.Priority;
            if (pin)
            {
                string cpuFile = Path.Combine(Folder, "benchmark-cpu.txt");
                int requestedCpu = File.Exists(cpuFile) ? int.Parse(File.ReadAllText(cpuFile).Trim()) : -1;
                int logicalCpu = requestedCpu >= 0 ? requestedCpu : Math.Max(0, Environment.ProcessorCount - 2);
                if (logicalCpu >= Environment.ProcessorCount || logicalCpu >= 64)
                    throw new InvalidOperationException("Invalid benchmark logical CPU: " + logicalCpu);
                previousAffinity = SetThreadAffinityMask(GetCurrentThread(), (UIntPtr)(1UL << logicalCpu));
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
            int rottingEnd = map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse).OfType<Corpse>()
                .Count(c => c.GetComp<CompRottable>()?.Stage == RotStage.Rotting);
            int ingestEnd = map.mapPawns.AllPawnsSpawned.Count(p => p.CurJob?.def == JobDefOf.Ingest);
            int pregnantEnd = map.mapPawns.AllPawnsSpawned.Count(p => p.health?.hediffSet?.GetFirstHediffOfDef(HediffDefOf.Pregnant) != null);
            File.AppendAllText(Path.Combine(Folder, "performance.txt"),
                $"{Mode} {scenario}: sample={samples+1}, pinned={pin}, version={VersionControl.CurrentVersionStringWithRev}, ticks={count}, tickDelta={GenTicks.TicksGame-startTick}, ms={timer.Elapsed.TotalMilliseconds:F3}, ms/tick={timer.Elapsed.TotalMilliseconds/count:F4}, uncappedTPS={count/timer.Elapsed.TotalSeconds:F2}, cpuMs={cpuMs:F3}, cpuMs/tick={cpuMs/count:F4}, cpuTPS={count*1000/cpuMs:F2}, pawnsStart={pawns}, spawnedAnimalsStart={animals}, animalsWithContained={totalAnimals}, trees={trees}, burrows={burrows}, spawnedAnimalsEnd={map.mapPawns.AllPawnsSpawned.Count(p=>p.RaceProps.Animal)}, rottingStart={rottingStart}, rottingEnd={rottingEnd}, ingestStart={ingestStart}, ingestEnd={ingestEnd}, pregnantStart={pregnantStart}, pregnantEnd={pregnantEnd}" + Environment.NewLine);
            Pawn hunter = map.mapPawns.AllPawnsSpawned.First(p => p.def.defName == "Wolf_Timber");
            Pawn target = map.mapPawns.AllPawnsSpawned.First(p => p.def.defName == "Hare");
            timer.Restart();
            for (int i = 0; i < 10000; i++) FoodUtility.GetPreyScoreFor(hunter, target);
            timer.Stop();
            File.AppendAllText(Path.Combine(Folder, "performance.txt"), $"{Mode} {scenario} preyScore 10000={timer.Elapsed.TotalMilliseconds:F3}ms" + Environment.NewLine);
        }
    }
}
