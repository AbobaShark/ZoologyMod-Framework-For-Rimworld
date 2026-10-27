// Loaded only in the isolated validation mod. Exercises the actual game trackers and save loader.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using ZoologyMod;

namespace ZoologyValidation
{
    public class RuntimeValidation : GameComponent
    {
        private int phase, phaseTick;
        private bool busy, loadPending;
        private Pawn burrowPawn, savedVenomPawn, savedHunter, treePawn, constrictor, heldPawn, diggingPawn, secondBurrowPawn;
        private Building_AnimalBurrow burrow;
        private long initialAge;
        private float initialFood, initialRest;
        private static readonly BindingFlags All = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly string ResultFile = Path.Combine(GenFilePaths.SaveDataFolderPath, "runtime-validation.txt");
        private static readonly FieldInfo TicksField = typeof(TickManager).GetField("ticksGameInt", All);
        public RuntimeValidation(Game game) { }
        public override void ExposeData()
        {
            Scribe_Values.Look(ref phase, "validationPhase"); Scribe_Values.Look(ref phaseTick, "validationPhaseTick");
            Scribe_References.Look(ref burrowPawn, "validationBurrowPawn"); Scribe_References.Look(ref burrow, "validationBurrow");
            Scribe_References.Look(ref savedVenomPawn, "validationVenomPawn"); Scribe_References.Look(ref savedHunter, "validationHunter");
            Scribe_References.Look(ref treePawn, "validationTreePawn"); Scribe_References.Look(ref constrictor, "validationConstrictor");
            Scribe_References.Look(ref heldPawn, "validationHeldPawn");
            Scribe_References.Look(ref diggingPawn, "validationDiggingPawn");
            Scribe_References.Look(ref secondBurrowPawn, "validationSecondBurrowPawn");
            Scribe_Values.Look(ref initialAge, "validationAge"); Scribe_Values.Look(ref initialFood, "validationFood");
            Scribe_Values.Look(ref initialRest, "validationRest");
        }
        private static void Write(string text)
        {
            File.AppendAllText(ResultFile, text + Environment.NewLine);
            Log.Message("[Zoology validation] " + text);
        }
        private static void Check(string name, bool result) => Write((result ? "PASS " : "FAIL ") + name);
        private static object Internal(string type, string method, params object[] args) => typeof(CompAnimalStrategyState).Assembly
            .GetType("ZoologyMod." + type).GetMethod(method, All).Invoke(null, args);
        private static Pawn Spawn(string kind, Map map)
        {
            PawnKindDef pawnKind = DefDatabase<PawnKindDef>.GetNamed(kind);
            Pawn p = pawnKind.race.race.Humanlike ? PawnGenerator.GeneratePawn(new PawnGenerationRequest(pawnKind,
                Faction.OfPlayer, forceGenerateNewPawn: true, mustBeCapableOfViolence: true,
                fixedBiologicalAge: 30f, allowAddictions: false)) : PawnGenerator.GeneratePawn(pawnKind);
            if (p.RaceProps.Humanlike) { p.ageTracker.AgeBiologicalTicks = 30 * 3600000L; p.SetFaction(Faction.OfPlayer); }
            IntVec3 c = CellFinder.RandomClosewalkCellNear(map.Center, map, 12);
            GenSpawn.Spawn(p, c, map);
            if (p.needs.food != null) p.needs.food.CurLevelPercentage = 1f;
            return p;
        }
        private static void Wait(Pawn p)
        {
            if (!p.Spawned || p.Dead) return;
            Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Zoology_ValidationWait"));
            job.expiryInterval = 200000; p.jobs.StartJob(job, JobCondition.InterruptForced);
        }
        private static Hediff_AnimalVenom Inject(Pawn target, float damage, Pawn hunter = null, string profile = "CobraBite")
        {
            target.health.PostApplyDamage(new DamageInfo(DefDatabase<DamageDef>.GetNamed("ZVenom_Damage_" + profile), damage, 0f, -1f, hunter), damage);
            return target.health.hediffSet.hediffs.OfType<Hediff_AnimalVenom>().FirstOrDefault();
        }
        private static void BeginHunt(Pawn hunter, Pawn target)
        {
            var state = hunter.TryGetComp<CompAnimalStrategyState>();
            state.ambushPrey = target; state.ambushPhase = 2; state.ambushUntil = GenTicks.TicksGame + 100000;
            hunter.jobs.StartJob(JobMaker.MakeJob(JobDefOf.PredatorHunt, target), JobCondition.InterruptForced);
        }
        private static void DisposePawn(Pawn pawn)
        {
            if (pawn.Corpse != null && !pawn.Corpse.Destroyed) pawn.Corpse.Destroy();
            else if (!pawn.Destroyed) pawn.Destroy();
        }
        private static void SimulateDisease(Pawn pawn, int ticks, float? tendQuality)
        {
            int savedTick = GenTicks.TicksGame;
            try
            {
                for (int i = 0; i < ticks && !pawn.Dead; i++)
                {
                    TicksField.SetValue(Find.TickManager, savedTick + i);
                    if (tendQuality.HasValue && i % 25000 == 0)
                        foreach (Hediff venom in pawn.health.hediffSet.hediffs.OfType<Hediff_AnimalVenom>()) venom.Tended(tendQuality.Value, 1f);
                    pawn.health.HealthTick(); pawn.health.HealthTickInterval(1);
                }
            }
            finally { TicksField.SetValue(Find.TickManager, savedTick); }
        }
        public override void GameComponentUpdate()
        {
            if (loadPending)
            {
                loadPending = false;
                GameDataSaveLoader.LoadGame("ZoologyValidationSave");
                return;
            }
            if (busy || Current.Game?.CurrentMap == null || phase == 99) return;
            busy = true;
            try
            {
                Map map = Find.CurrentMap;
                if (phase == 0)
                {
                    File.WriteAllText(ResultFile, "RimWorld " + VersionControl.CurrentVersionStringWithRev + Environment.NewLine);
                    string modAssembly = typeof(CompAnimalStrategyState).Assembly.Location.Replace('\\', '/');
                    Write("Loaded mod assembly: " + modAssembly);
                    Check("versioned 1.7 assembly overrides legacy root", modAssembly.Contains("/1.7/Assemblies/"));
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    if (File.Exists(Path.Combine(GenFilePaths.SaveDataFolderPath, "nuance-only.txt")))
                    { RunNewNuanceChecks(map); phase = 99; Write("COMPLETE nuance-only"); return; }
                    RunImmediate(map);
                    SetupSave(map);
                    phase = 1; phaseTick = GenTicks.TicksGame;
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
                }
                else if (phase == 1 && GenTicks.TicksGame - phaseTick >= 3400)
                {
                    Check("contained physiology: age", burrowPawn.ageTracker.AgeBiologicalTicks > initialAge);
                    Check("contained physiology: hunger", burrowPawn.needs.food.CurLevelPercentage < initialFood);
                    Check("contained physiology: rest", burrowPawn.needs.rest.CurLevelPercentage > initialRest);
                    phase = 2;
                    GameDataSaveLoader.SaveGame("ZoologyValidationSave");
                    loadPending = true;
                }
                else if (phase == 2)
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    Check("save/load contained pawn reference", burrow != null && burrow.ContainedThing == burrowPawn && !burrowPawn.Spawned);
                    Check("save/load shared occupants and capacity", burrow.Contains(secondBurrowPawn) && !secondBurrowPawn.Spawned
                        && burrow.OccupiedBodySize <= burrow.TotalBodyCapacity + .0001f);
                    Check("save/load keeps mother-child relation inside shared burrow", secondBurrowPawn.GetMother() == burrowPawn);
                    var venom = savedVenomPawn.health.hediffSet.hediffs.OfType<Hediff_AnimalVenom>().FirstOrDefault();
                    Check("save/load venom hunter reference", venom != null && venom.Hunter == savedHunter);
                    Check("save/load tree state", treePawn.jobs.curDriver is JobDriver_AnimalTreeRefuge driver && driver.AtHeight);
                    Check("save/load constriction state", heldPawn.health.hediffSet.hediffs.OfType<Hediff_AnimalConstriction>().Any(h => h.Active && h.holder == constrictor));
                    Check("dig job completes and remembers one-tile burrow", diggingPawn.TryGetComp<CompAnimalStrategyState>().familiarBurrows.Any(b => b.Spawned && b.def.size == IntVec2.One));
                    var mode = burrowPawn.TryGetComp<CompAnimalStrategyState>();
                    Check("save/load selected environment target", mode.environmentThreat == savedHunter && mode.chosenWater && mode.environmentTarget == burrow.Position);
                    burrow.Destroy();
                    Check("burrow destruction ejects live pawn", burrowPawn.Spawned && !burrowPawn.Dead);
                    Check("burrow destruction ejects all occupants", secondBurrowPawn.Spawned && !secondBurrowPawn.Dead);
                    SimulateDisease(savedVenomPawn, 120000, null);
                    Check("delayed venom death", savedVenomPawn.Dead);
                    Check("delayed kill pair after hunt ended and load", savedVenomPawn.Corpse != null
                        && PredatorPreyPairGameComponent.Instance.IsPaired(savedHunter, savedVenomPawn.Corpse));
                    Check("venom corpse remains fresh", savedVenomPawn.Corpse?.GetComp<CompRottable>()?.Stage == RotStage.Fresh);
                    RunAfterLoadChecks(map);
                    RunRefugeChecks(map);
                    RunSharedBurrowChecks(map);
                    RunContainedMedicalChecks(map);
                    RunNewNuanceChecks(map);
                    phase = 99; Write("COMPLETE");
                    GameDataSaveLoader.SaveGame("ZoologyValidationCompleted");
                    CameraJumper.TryJumpAndSelect(treePawn);
                }
            }
            catch (Exception ex) { Write("EXCEPTION " + ex); phase = 99; }
            finally { busy = false; }
        }
        private void RunImmediate(Map map)
        {
            bool ce = AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "CombatExtended");
            Write("CE loaded=" + ce);
            if (ce)
            {
                Check("CE cobra tools are ToolCE", DefDatabase<ThingDef>.GetNamed("Cobra").tools.All(t => t.GetType().FullName == "CombatExtended.ToolCE"));
                Check("CE venom maneuvers use CE melee verb", DefDatabase<ManeuverDef>.AllDefs.Where(m => m.defName.StartsWith("ZVenom_")).All(m => m.verb.verbClass.FullName == "CombatExtended.Verb_MeleeAttackCE"));
            }
            Check("12 source-used venom profiles", DefDatabase<ToolCapacityDef>.AllDefs.Count(d => d.defName.StartsWith("ZVenom_")) == 12);
            foreach (var capacity in DefDatabase<ToolCapacityDef>.AllDefs.Where(d => d.defName.StartsWith("ZVenom_")))
            {
                var maneuver = DefDatabase<ManeuverDef>.AllDefs.Single(m => m.requiredCapacity == capacity);
                var damage = maneuver.verb.meleeDamageDef;
                Check("chain " + capacity.defName, damage.Worker != null && damage.hediff != null
                    && damage.additionalHediffs.Single().hediff.hediffClass == typeof(Hediff_AnimalVenom));
            }
            foreach (string family in new[] { "Cobra", "Viper", "Hydra" })
                Check("Administer recipe " + family, DefDatabase<RecipeDef>.AllDefs.Any(r => r.defName.Contains("ZVenom_Drug_" + family)));
            Check("ambush 10 cells = 10%", Math.Abs((float)Internal("AnimalHuntStrategy", "NoReactionChance", 10f) - .1f) < .0001f);
            Check("ambush 4 cells = 70%", Math.Abs((float)Internal("AnimalHuntStrategy", "NoReactionChance", 4f) - .7f) < .0001f);
            Pawn human = Spawn("Colonist", map); Wait(human);
            Inject(human, 0); Check("zero damage no venom", !human.health.hediffSet.hediffs.OfType<Hediff_AnimalVenom>().Any());
            var first = Inject(human, 1.5f); float initial = first.Severity;
            var second = Inject(human, 1.5f);
            Check("two bites merge one hediff and double dose", ReferenceEquals(first, second) && Math.Abs(second.Severity - 2f * initial) < .0001f);
            var wrong = DefDatabase<ThingDef>.GetNamed("ZVenom_Drug_Viper").ingestible.outcomeDoers[0];
            wrong.DoIngestionOutcome(human, ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("ZVenom_Drug_Viper")), 1);
            Check("wrong antivenom leaves cobra venom", human.health.hediffSet.hediffs.Contains(first));
            var drug = DefDatabase<ThingDef>.GetNamed("ZVenom_Drug_Cobra");
            drug.ingestible.outcomeDoers[0].DoIngestionOutcome(human, ThingMaker.MakeThing(drug), 1);
            Check("matching antivenom removes venom", !human.health.hediffSet.hediffs.Contains(first));
            Inject(human, 1.5f); Check("antivenom grants no prophylaxis", human.health.hediffSet.hediffs.OfType<Hediff_AnimalVenom>().Any());
            DisposePawn(human);
            Pawn armored = Spawn("Colonist", map); Wait(armored);
            armored.apparel.DestroyAll();
            armored.apparel.Wear((Apparel)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Apparel_PowerArmor")));
            int stopped = 0, deflected = 0; bool stoppedVenom = true;
            for (int i = 0; i < 12 && !armored.Dead; i++)
            {
                var blockedHit = new DamageInfo(DefDatabase<DamageDef>.GetNamed("ZVenom_Damage_CobraBite"), 1f, 0f,
                    -1f, null, armored.RaceProps.body.corePart, DefDatabase<ThingDef>.GetNamed("Cobra"));
                blockedHit.SetHitPart(armored.RaceProps.body.corePart);
                var armorResult = armored.TakeDamage(blockedHit);
                if (i == 0) Write($"ARMOR context: worker={blockedHit.Def.Worker.GetType().Name}, weapon={blockedHit.Weapon?.defName}, torso={blockedHit.HitPart.depth}, worn={armored.apparel.WornApparel.Count}, protection={armored.apparel.WornApparel[0].GetStatValue(StatDefOf.ArmorRating_Sharp)}, received={armorResult.totalDamageDealt}, deflected={armorResult.deflected}");
                var active = armored.health.hediffSet.hediffs.OfType<Hediff_AnimalVenom>().ToList();
                if (armorResult.totalDamageDealt == 0f) { stopped++; stoppedVenom &= active.Count == 0; }
                if (armorResult.totalDamageDealt > 0f && active.Count == 0) deflected++;
                foreach (var toxin in active) armored.health.RemoveHediff(toxin);
            }
            Write("ARMOR fully stopped hits=" + stopped + ", non-envenoming deflections=" + deflected);
            Check("real armor stops or deflects biological venom", stopped + deflected > 0 && stoppedVenom);
            var zeroHit = new DamageInfo(DefDatabase<DamageDef>.GetNamed("ZVenom_Damage_CobraBite"), 0f, 0f);
            zeroHit.SetHitPart(armored.RaceProps.body.corePart);
            var zeroResult = armored.TakeDamage(zeroHit);
            Check("real TakeDamage zero damage injects no venom", zeroResult.totalDamageDealt == 0f
                && !armored.health.hediffSet.hediffs.OfType<Hediff_AnimalVenom>().Any());
            DisposePawn(armored);
            foreach (var scenario in new[] { ("weak", .5f, 1, (float?)null), ("one", 1.5f, 1, (float?)null),
                ("two", 1.5f, 2, (float?)null), ("one_tended_50", 1.5f, 1, (float?).5f), ("one_tended_100", 1.5f, 1, (float?)1f) })
            {
                Pawn p = Spawn("Colonist", map); Wait(p);
                for (int i = 0; i < scenario.Item3; i++) Inject(p, scenario.Item2);
                float gain = p.GetStatValue(StatDefOf.ImmunityGainSpeed);
                SimulateDisease(p, 140000, scenario.Item4);
                Write($"CALIBRATION {scenario.Item1}: body={p.BodySize:F3}, immunityGain={gain:F3}, dead={p.Dead}, severity={p.health.hediffSet.hediffs.OfType<Hediff_AnimalVenom>().FirstOrDefault()?.Severity:F4}");
                DisposePawn(p);
            }
            Pawn mech = Spawn("Mech_Scyther", map); Inject(mech, 100f);
            Check("non-flesh no venom", !mech.health.hediffSet.hediffs.OfType<Hediff_AnimalVenom>().Any()); DisposePawn(mech);
            Pawn snake = Spawn("Zoology_ValidationConstrictor", map); Pawn prey = Spawn("Colonist", map);
            Check("constrictor eligible vertebrate", (bool)Internal("AnimalConstriction", "Applicable", snake, prey));
            Pawn insect = Spawn("Megascarab", map);
            Check("invertebrate excluded", !(bool)Internal("AnimalConstriction", "Applicable", snake, insect));
            Pawn oversized = Spawn("Elephant", map);
            Check("oversized constriction prey excluded", !(bool)Internal("AnimalConstriction", "Applicable", snake, oversized));
            DisposePawn(oversized);
            DisposePawn(insect); DisposePawn(prey); DisposePawn(snake);
            var watch = Stopwatch.StartNew();
            Pawn a = Spawn("Wolf_Timber", map); Pawn b = Spawn("Hare", map);
            for (int i = 0; i < 10000; i++) FoodUtility.GetPreyScoreFor(a, b);
            watch.Stop(); Write("PERFORMANCE preyScore 10000 calls ms=" + watch.Elapsed.TotalMilliseconds.ToString("F3"));
            DisposePawn(a); DisposePawn(b);
            RunBehaviorChecks(map);
            float[] progression = new float[2]; int caseIndex = 0;
            foreach (string kind in new[] { "Colonist", "Cobra" })
            {
                Pawn holder = Spawn("Zoology_ValidationConstrictor", map), captive = Spawn(kind, map);
                captive.health.AddHediff(HediffDefOf.Anesthetic);
                holder.Position = captive.Position + IntVec3.North; holder.Notify_Teleported(false);
                holder.jobs.StartJob(JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Zoology_Constrict"), captive), JobCondition.InterruptForced);
                var hold = captive.health.hediffSet.hediffs.OfType<Hediff_AnimalConstriction>().Single();
                float before = hold.Severity;
                for (int i = 0; i < 120; i++) holder.jobs.curDriver.DriverTick();
                progression[caseIndex++] = hold.Severity - before;
                Wait(holder); DisposePawn(holder); DisposePawn(captive);
            }
            Check("ectotherm constriction progresses four times slower", progression[1] > 0
                && Math.Abs(progression[0] / progression[1] - 4f) < .02f);
        }
        private void RunBehaviorChecks(Map map)
        {
            Pawn wolf = Spawn("Wolf_Timber", map), hare = Spawn("Hare", map);
            Wait(wolf); Wait(hare);
            Check("Chasing declared on wolf", (PredationStrategy)Internal("AnimalHuntStrategy", "For", wolf) == PredationStrategy.Chasing);
            Pawn alternateHunter = Spawn("Wolf_Timber", map); Wait(alternateHunter);
            Internal("PredationDecisionCache", "StoreAcceptablePrey", wolf, hare, true);
            Internal("PredationDecisionCache", "StoreAcceptablePrey", alternateHunter, hare, false);
            object[] firstPair = { wolf, hare, false }, secondPair = { alternateHunter, hare, true };
            Check("pair cache retains independent hunters for one prey", (bool)Internal("PredationDecisionCache", "TryGetAcceptablePrey", firstPair)
                && (bool)firstPair[2] && (bool)Internal("PredationDecisionCache", "TryGetAcceptablePrey", secondPair) && !(bool)secondPair[2]);
            Internal("PredationDecisionCache", "InvalidatePrey", alternateHunter);
            Check("shelter invalidation preserves unrelated prey cache", (bool)Internal("PredationDecisionCache", "TryGetAcceptablePrey", firstPair));
            Internal("PredationDecisionCache", "InvalidatePrey", hare);
            Check("shelter invalidation clears affected prey cache", !(bool)Internal("PredationDecisionCache", "TryGetAcceptablePrey", firstPair));
            DisposePawn(alternateHunter);
            Check("non-predator strategy No", (PredationStrategy)Internal("AnimalHuntStrategy", "For", hare) == PredationStrategy.No);
            var wolfStrategy = wolf.def.GetModExtension<ModExtension_PredationStrategy>();
            wolfStrategy.strategy = PredationStrategy.No;
            float baseScore = FoodUtility.GetPreyScoreFor(wolf, hare);
            wolfStrategy.strategy = PredationStrategy.Chasing;
            float chaseScore = FoodUtility.GetPreyScoreFor(wolf, hare);
            float hunterSpeed = (float)Internal("AnimalLocomotion", "SpeedAt", wolf, hare.Position, true);
            float preySpeed = (float)Internal("AnimalLocomotion", "SpeedAt", hare, hare.Position, true);
            float expected = baseScore > 0 && preySpeed > hunterSpeed ? baseScore * hunterSpeed / preySpeed : baseScore;
            Check("Chasing dynamic proportional score outside cache", Math.Abs(chaseScore - expected) < .001f);
            hare.jobs.curDriver.asleep = true;
            wolfStrategy.strategy = PredationStrategy.No; float sleepBase = FoodUtility.GetPreyScoreFor(wolf, hare);
            wolfStrategy.strategy = PredationStrategy.Chasing;
            Check("Chasing does not penalize sleeping prey", Math.Abs(FoodUtility.GetPreyScoreFor(wolf, hare) - sleepBase) < .001f);
            hare.jobs.curDriver.asleep = false;
            // Compare normalized Jog against the engine's actual cardinal cost on both media.
            var costMethod = typeof(Pawn_PathFollower).GetMethod("CostToMoveIntoCell", All, null, new[] { typeof(Pawn), typeof(IntVec3) }, null);
            foreach (string terrain in new[] { "Soil", "WaterShallow" })
            {
                IntVec3 c = wolf.Position + IntVec3.North;
                TerrainDef previous = c.GetTerrain(map);
                map.terrainGrid.SetTerrain(c, DefDatabase<TerrainDef>.GetNamed(terrain));
                wolf.CurJob.locomotionUrgency = LocomotionUrgency.Jog;
                float nativeCost = (float)costMethod.Invoke(null, new object[] { wolf, c });
                float speed = (float)Internal("AnimalLocomotion", "SpeedAt", wolf, c, false);
                Check("locomotion matches native " + terrain, Math.Abs(speed - 60f / nativeCost) < .001f);
                map.terrainGrid.SetTerrain(c, previous);
            }
            Pawn cobra = Spawn("Cobra", map); Wait(cobra);
            var cobraStrategy = cobra.def.GetModExtension<ModExtension_PredationStrategy>();
            var originalStrategy = cobraStrategy.strategy;
            cobraStrategy.strategy = PredationStrategy.WaterAmbush;
            var changed = new Dictionary<IntVec3, TerrainDef>();
            foreach (IntVec3 c in GenRadial.RadialCellsAround(hare.Position, 3f, true))
            {
                changed[c] = c.GetTerrain(map); map.terrainGrid.SetTerrain(c, DefDatabase<TerrainDef>.GetNamed("Soil"));
            }
            Check("WaterAmbush fast land prey far from shore excluded", !(bool)Internal("AnimalHuntStrategy", "WaterPreyAllowed", cobra, hare));
            IntVec3 shore = hare.Position + IntVec3.North * 2;
            map.terrainGrid.SetTerrain(shore, DefDatabase<TerrainDef>.GetNamed("WaterShallow"));
            Check("WaterAmbush fast prey two cells from water accepted", (bool)Internal("AnimalHuntStrategy", "WaterPreyAllowed", cobra, hare));
            foreach (var pair in changed) map.terrainGrid.SetTerrain(pair.Key, pair.Value);
            cobraStrategy.strategy = originalStrategy;
            cobra.Position = hare.Position + IntVec3.North * 8; cobra.Notify_Teleported(false);
            var stalkState = cobra.TryGetComp<CompAnimalStrategyState>();
            stalkState.ambushUntil = 0;
            cobra.jobs.StartJob(JobMaker.MakeJob(JobDefOf.PredatorHunt, hare), JobCondition.InterruptForced);
            Check("Ambush starts stalking below Walk", cobra.jobs.curDriver is JobDriver_AnimalStalk && cobra.CurJob.locomotionUrgency == LocomotionUrgency.Amble);
            Check("Ambush camouflage covers target and bystanders", (bool)Internal("AnimalHuntStrategy", "HiddenFrom", cobra, hare)
                && (bool)Internal("AnimalHuntStrategy", "HiddenFrom", cobra, wolf));
            Check("Ambush half chase budget", (int)Internal("AnimalHuntStrategy", "ChaseLimit", cobra) * 2 == (int)Internal("AnimalHuntStrategy", "ChaseLimit", wolf));
            Wait(cobra);
            cobra.ageTracker.AgeBiologicalTicks = 3600000 * 5L;
            Check("adult butchery gives one venom sample", cobra.ButcherProducts(null, 1f).Count(t => t.def.defName == "ZVenom_Sample_Cobra") == 1);
            cobra.ageTracker.AgeBiologicalTicks = 0;
            Check("infant butchery gives no venom sample", !cobra.ButcherProducts(null, 1f).Any(t => t.def.defName.StartsWith("ZVenom_Sample_")));
            foreach (string family in new[] { "Cobra", "Viper", "Hydra" })
            {
                ThingDef sample = DefDatabase<ThingDef>.GetNamed("ZVenom_Sample_" + family);
                ThingDef drug = DefDatabase<ThingDef>.GetNamed("ZVenom_Drug_" + family);
                Check("craft ingredients " + family, DefDatabase<RecipeDef>.AllDefs.Any(r => r.products.Any(p => p.thingDef == drug)
                    && r.ingredients.Any(i => i.filter.Allows(sample)) && r.ingredients.Any(i => i.filter.Allows(ThingDefOf.MedicineHerbal))));
            }
            Pawn small = Spawn("Rat", map), large = Spawn("Elephant", map);
            var s = Inject(small, .05f); var l = Inject(large, .05f);
            Check("venom dose scales inversely with BodySize", s != null && l != null && s.Severity > l.Severity);
            foreach (Pawn p in new[] { wolf, hare, cobra, small, large }) DisposePawn(p);
            Pawn mother = Spawn("Husky", map), baby = Spawn("Husky", map);
            mother.gender = Gender.Female;
            mother.SetFaction(Faction.OfPlayer); baby.SetFaction(Faction.OfPlayer);
            mother.health.AddHediff(AnimalLactationUtility.LactatingHediffDef);
            var messages = (List<Message>)typeof(Messages).GetField("liveMessages", All).GetValue(null);
            int previousMessages = messages.Count;
            map.designationManager.AddDesignation(new Designation(mother, DesignationDefOf.Slaughter));
            Check("manual slaughter warns for lactating female", messages.Count > previousMessages
                && messages.Last().def == MessageTypeDefOf.CautionInput);
            map.designationManager.DesignationOn(mother, DesignationDefOf.Slaughter).Delete();
            baby.relations.AddDirectRelation(PawnRelationDefOf.Parent, mother);
            baby.ageTracker.AgeBiologicalTicks = 0;
            map.areaManager.TryMakeNewAllowed(out Area_Allowed maternalArea);
            map.areaManager.TryMakeNewAllowed(out Area_Allowed ownArea);
            maternalArea[mother.Position] = true; ownArea[baby.Position] = true;
            mother.playerSettings.AreaRestrictionInPawnCurrentMap = maternalArea;
            baby.playerSettings.AreaRestrictionInPawnCurrentMap = ownArea;
            Check("Baby uses mother effective area", baby.playerSettings.EffectiveAreaRestrictionInPawnCurrentMap == maternalArea);
            Check("Baby saved assignment unchanged", baby.playerSettings.AreaRestrictionInPawnCurrentMap == ownArea);
            LifeStageAge juvenile = baby.RaceProps.lifeStageAges.First(a => a.def.defName == "AnimalJuvenile");
            baby.ageTracker.AgeBiologicalTicks = (long)((juvenile.minAge + .01f) * 3600000);
            Check("Juvenile resumes own area", baby.playerSettings.EffectiveAreaRestrictionInPawnCurrentMap == ownArea);
            object root = typeof(JobGiver_WanderNearMother).GetMethod("GetWanderRoot", All).Invoke(new JobGiver_WanderNearMother(), new object[] { baby });
            Check("Juvenile does not follow mother outside own area", (IntVec3)root == baby.Position);
            DisposePawn(baby); DisposePawn(mother);
        }
        private void RunAfterLoadChecks(Map map)
        {
            // An unrelated killing blow must not claim the corpse for an earlier poisoner.
            Pawn victim = Spawn("Colonist", map); Wait(victim);
            BeginHunt(savedHunter, victim); Inject(victim, .05f, savedHunter); Wait(savedHunter);
            victim.Kill(new DamageInfo(DamageDefOf.Flame, 1000f));
            Check("external killing blow not assigned to venom hunter", victim.Corpse != null
                && !PredatorPreyPairGameComponent.Instance.IsPaired(savedHunter, victim.Corpse));
            DisposePawn(victim);
            Pawn killer = Spawn("Zoology_ValidationConstrictor", map), held = Spawn("Colonist", map);
            Wait(held); killer.Position = held.Position + IntVec3.North;
            killer.jobs.StartJob(JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Zoology_Constrict"), held), JobCondition.InterruptForced);
            var lethalHold = held.health.hediffSet.hediffs.OfType<Hediff_AnimalConstriction>().First();
            held.health.forceDowned = true;
            typeof(Pawn_HealthTracker).GetMethod("MakeDowned", All).Invoke(held.health, new object[] { null, lethalHold });
            Check("Downed prey remains held", held.Downed && lethalHold.Active);
            lethalHold.Severity = 1f;
            Check("constriction causes capacity death", held.Dead);
            Check("constriction corpse assigned to hunter", held.Corpse != null && PredatorPreyPairGameComponent.Instance.IsPaired(killer, held.Corpse));
            DisposePawn(held); DisposePawn(killer);
            if (heldPawn.health.hediffSet.hediffs.OfType<Hediff_AnimalConstriction>().FirstOrDefault() is Hediff_AnimalConstriction effect)
            {
                constrictor.jobs.EndCurrentJob(JobCondition.InterruptForced);
                Check("interrupted constriction removes impairment", !heldPawn.health.hediffSet.hediffs.Contains(effect));
            }
            // Successful ranged damage must remove height and start ground escape.
            var treeDriver = treePawn.jobs.curDriver as JobDriver_AnimalTreeRefuge;
            if (treeDriver != null)
            {
                Plant tree = treeDriver.job.targetA.Thing as Plant;
                DamageInfo hit = new DamageInfo(DamageDefOf.Bullet, 1f, instigator: savedHunter);
                treePawn.health.PostApplyDamage(hit, 1f);
                Check("ranged hit descends", !treeDriver.AtHeight && treePawn.jobs.curDriver is not JobDriver_AnimalTreeRefuge);
                Check("ranged source remembered", treePawn.TryGetComp<CompAnimalStrategyState>().lastRangedSource == savedHunter);
                // Restore a deterministic visual fixture after the behavioral checks.
                treePawn.Position = tree.Position; treePawn.Notify_Teleported(false);
                treePawn.jobs.StartJob(JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Zoology_TreeRefuge"), tree, savedHunter), JobCondition.InterruptForced);
                if (treePawn.jobs.curDriver is JobDriver_AnimalTreeRefuge visual) visual.AtHeight = true;
                IntVec3 c = CellFinder.RandomClosewalkCellNear(tree.Position + IntVec3.South * 3, map, 1);
                GenSpawn.Spawn(DefDatabase<ThingDef>.GetNamed("Zoology_AnimalBurrow"), c, map);
                int i = 0;
                foreach (string name in new[] { "ZVenom_Sample_Cobra", "ZVenom_Drug_Cobra", "ZVenom_Sample_Viper", "ZVenom_Drug_Viper", "ZVenom_Sample_Hydra", "ZVenom_Drug_Hydra" })
                    GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(name)), c + IntVec3.East * (++i), map);
            }
        }
        private void SetupSave(Map map)
        {
            burrowPawn = Spawn("Rat", map); Wait(burrowPawn);
            burrowPawn.gender = Gender.Female; burrowPawn.ageTracker.AgeBiologicalTicks = 5 * 3600000L;
            burrow = (Building_AnimalBurrow)GenSpawn.Spawn(DefDatabase<ThingDef>.GetNamed("Zoology_AnimalBurrow"), burrowPawn.Position, map);
            burrow.InitializeFor(burrowPawn);
            burrowPawn.needs.rest.CurLevelPercentage = .1f;
            initialAge = burrowPawn.ageTracker.AgeBiologicalTicks; initialFood = burrowPawn.needs.food.CurLevelPercentage;
            initialRest = burrowPawn.needs.rest.CurLevelPercentage;
            Check("burrow accepts burrower", burrow.TryAcceptThing(burrowPawn));
            secondBurrowPawn = Spawn("Rat", map); secondBurrowPawn.needs.rest.CurLevelPercentage = .1f;
            secondBurrowPawn.ageTracker.AgeBiologicalTicks = 0;
            secondBurrowPawn.relations.AddDirectRelation(PawnRelationDefOf.Parent, burrowPawn);
            Check("social burrow accepts a second rat", burrow.TryAcceptThing(secondBurrowPawn));
            savedHunter = Spawn("Cobra", map); savedVenomPawn = Spawn("Colonist", map); Wait(savedVenomPawn);
            var savedMode = burrowPawn.TryGetComp<CompAnimalStrategyState>();
            savedMode.environmentThreat = savedHunter; savedMode.chosenWater = true; savedMode.environmentTarget = burrow.Position;
            savedMode.environmentUntil = GenTicks.TicksGame + 10000;
            BeginHunt(savedHunter, savedVenomPawn); Inject(savedVenomPawn, 1.5f, savedHunter); Wait(savedHunter);
            treePawn = Spawn("Squirrel", map); Wait(treePawn);
            Plant tree = map.listerThings.AllThings.OfType<Plant>().Where(p => p.def.plant.IsTree && p.Growth >= .65f).OrderBy(p => (p.Position - map.Center).LengthHorizontalSquared).First();
            savedHunter.Position = tree.Position + IntVec3.West * 5;
            treePawn.Position = tree.Position;
            treePawn.Notify_Teleported(false);
            treePawn.jobs.StartJob(JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Zoology_TreeRefuge"), tree, savedHunter), JobCondition.InterruptForced);
            var driver = treePawn.jobs.curDriver as JobDriver_AnimalTreeRefuge;
            if (driver != null) driver.AtHeight = true;
            Check("tree stays spawned", treePawn.Spawned);
            Pawn groundHunter = Spawn("Wolf_Timber", map); Wait(groundHunter);
            var melee = groundHunter.meleeVerbs.TryGetMeleeVerb(null);
            Check("tree blocks melee", !melee.CanHitTargetFrom(tree.Position + IntVec3.North, treePawn));
            IntVec3 physicalCell = treePawn.Position;
            Check("tree visual offset keeps tile", driver != null && driver.ForcedBodyOffset.z >= .8f
                && treePawn.DrawPos.z > treePawn.Position.z + 1f && treePawn.Position == physicalCell);
            var menu = FloatMenuUtility.GetMeleeAttackAction(groundHunter, treePawn, out string reason, true);
            Check("tree melee menu unavailable with reason", menu == null && reason.Contains("tree", StringComparison.OrdinalIgnoreCase));
            Check("tree rejects ordered melee before approaching", !groundHunter.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.AttackMelee, treePawn)));
            groundHunter.jobs.StartJob(JobMaker.MakeJob(JobDefOf.AttackMelee, treePawn), JobCondition.InterruptForced);
            Check("tree rejects AI melee job", groundHunter.CurJob.def != JobDefOf.AttackMelee);
            Check("climber can attack tree prey", savedHunter.meleeVerbs.TryGetMeleeVerb(null).CanHitTargetFrom(tree.Position + IntVec3.North, treePawn));
            Check("idle climber is not direct climbing threat", !(bool)Internal("AnimalDefense", "DirectClimbingThreat", savedHunter, treePawn, tree));
            BeginHunt(savedHunter, treePawn);
            Check("climber targeting this prey is direct climbing threat", (bool)Internal("AnimalDefense", "DirectClimbingThreat", savedHunter, treePawn, tree));
            var chooseState = treePawn.TryGetComp<CompAnimalStrategyState>(); chooseState.nextDefensePlan = 0;
            Job defense = (Job)Internal("AnimalDefense", "Choose", treePawn, JobMaker.MakeJob(JobDefOf.Flee, tree.Position + IntVec3.East * 8, savedHunter));
            Check("no tree escape from directly targeting climber", defense?.def.defName != "Zoology_TreeRefuge");
            Wait(savedHunter); DisposePawn(groundHunter);
            Pawn shooter = Spawn("Colonist", map); Wait(shooter);
            shooter.equipment.DestroyAllEquipment();
            shooter.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Gun_BoltActionRifle")));
            Verb ranged = shooter.equipment.PrimaryEq.PrimaryVerb;
            // Compare against the same ground target so cover/line-of-sight cannot masquerade as a tree rule.
            bool elevatedTargeting = ranged.CanHitTargetFrom(tree.Position + IntVec3.North, treePawn);
            driver.AtHeight = false;
            bool groundTargeting = ranged.CanHitTargetFrom(tree.Position + IntVec3.North, treePawn);
            driver.AtHeight = true;
            Check("tree preserves ranged targeting", elevatedTargeting == groundTargeting && elevatedTargeting);
            DisposePawn(shooter);
            constrictor = Spawn("Zoology_ValidationConstrictor", map); heldPawn = Spawn("Colonist", map); Wait(heldPawn);
            heldPawn.health.AddHediff(DefDatabase<HediffDef>.GetNamed("Anesthetic"));
            constrictor.Position = heldPawn.Position + IntVec3.North;
            constrictor.jobs.StartJob(JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Zoology_Constrict"), heldPawn), JobCondition.InterruptForced);
            Check("constriction starts linked hediff", heldPawn.health.hediffSet.hediffs.OfType<Hediff_AnimalConstriction>().Any());
            diggingPawn = Spawn("Rat", map);
            var dangers = map.mapPawns.AllPawnsSpawned.Where(p => p.RaceProps.predator || p.RaceProps.Humanlike).ToList();
            IntVec3 site = map.AllCells.First(c => c.x > 5 && c.z > 5 && c.Standable(map) && c.GetEdifice(map) == null
                && c.GetTerrain(map).affordances.Contains(Building_AnimalBurrow.Settings.diggableTerrainAffordance)
                && dangers.All(p => !p.Position.InHorDistOf(c, 30f)));
            diggingPawn.Position = site;
            diggingPawn.Notify_Teleported(false);
            diggingPawn.jobs.StartJob(JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Zoology_DigBurrow"), diggingPawn.Position), JobCondition.InterruptForced);
            Write("SAVE_SETUP_COMPLETE");
        }
        private static void TickDriverToil(Pawn pawn, int interval)
        {
            int saved = GenTicks.TicksGame;
            for (int i = 0; i < interval; i++)
            {
                TicksField.SetValue(Find.TickManager, saved + i);
                if (pawn.jobs.curDriver == null) break;
                var toil = (Toil)typeof(JobDriver).GetProperty("CurToil", All).GetValue(pawn.jobs.curDriver);
                toil?.tickAction?.Invoke();
            }
            TicksField.SetValue(Find.TickManager, saved);
        }
        private void RunRefugeChecks(Map map)
        {
            Plant tree = treePawn.CurJob.targetA.Thing as Plant;
            treePawn.needs.food.CurLevelPercentage = .1f;
            TickDriverToil(treePawn, 60);
            Check("hunger forces tree descent", treePawn.jobs.curDriver is not JobDriver_AnimalTreeRefuge);
            Check("hungry tree refugee cannot immediately hide again", !(bool)Internal("AnimalDefense", "CanUseShelter", treePawn)
                && treePawn.TryGetComp<CompAnimalStrategyState>().groundEscapeUntilFed);
            treePawn.needs.food.CurLevelPercentage = .6f;
            Check("feeding unlocks shelter", (bool)Internal("AnimalDefense", "CanUseShelter", treePawn));
            Pawn sleeper = Spawn("Squirrel", map); Wait(sleeper);
            sleeper.Position = tree.Position; sleeper.Notify_Teleported(false);
            sleeper.needs.rest.CurLevelPercentage = .1f;
            sleeper.jobs.StartJob(JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Zoology_SleepInTree"), tree), JobCondition.InterruptForced);
            float rest = sleeper.needs.rest.CurLevelPercentage;
            TickDriverToil(sleeper, 60);
            sleeper.needs.rest.NeedInterval();
            Check("tree sleep restores rest at height", sleeper.jobs.curDriver is JobDriver_AnimalTreeRefuge sleeping && sleeping.AtHeight
                && sleeping.asleep && sleeper.needs.rest.CurLevelPercentage > rest);
            DisposePawn(sleeper);
            Pawn rat = Spawn("Rat", map); Wait(rat);
            IntVec3 excavationSite = new IntVec3(30, 0, 40);
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(excavationSite, 3f, true))
            {
                foreach (Thing obstacle in cell.GetThingList(map).Where(t => t is not Pawn && t.def.passability == Traversability.Impassable).ToList())
                    obstacle.Destroy();
                map.terrainGrid.SetTerrain(cell, DefDatabase<TerrainDef>.GetNamed("Soil"));
            }
            map.pathing.RecalculateAllPerceivedPathCosts();
            rat.Position = excavationSite;
            rat.Notify_Teleported(false);
            var refuge = (Building_AnimalBurrow)GenSpawn.Spawn(DefDatabase<ThingDef>.GetNamed("Zoology_AnimalBurrow"), rat.Position, map);
            refuge.TryAcceptThing(rat);
            rat.needs.food.CurLevelPercentage = .1f;
            int tick = GenTicks.TicksGame;
            for (int i = 0; i < 250 && !rat.Spawned; i++)
            {
                TicksField.SetValue(Find.TickManager, tick + i);
                typeof(Building_AnimalBurrow).GetMethod("Tick", All).Invoke(refuge, null);
            }
            TicksField.SetValue(Find.TickManager, tick);
            Check("hunger forces burrow exit", rat.Spawned && rat.TryGetComp<CompAnimalStrategyState>().groundEscapeUntilFed);
            Check("hungry burrow refugee cannot reenter", !refuge.Accepts(rat));
            rat.needs.food.CurLevelPercentage = 1f;
            refuge.TryAcceptThing(rat); refuge.Open();
            Check("human opening prevents immediate reentry", rat.Spawned && !refuge.Accepts(rat)
                && rat.TryGetComp<CompAnimalStrategyState>().burrowReentryAfter > GenTicks.TicksGame);
            rat.TryGetComp<CompAnimalStrategyState>().burrowReentryAfter = 0;
            refuge.TryAcceptThing(rat);
            Pawn digger = Spawn("Zoology_ValidationConstrictor", map); Wait(digger);
            digger.Position = GenAdj.AdjacentCells.Select(offset => refuge.Position + offset).First(c => c.Standable(map));
            digger.Notify_Teleported(false);
            Check("digger recognizes suitable hidden prey", (bool)Internal("AnimalBurrowHunt", "Eligible", digger, refuge));
            digger.SetFaction(Faction.OfPlayer); rat.SetFaction(Faction.OfPlayer);
            Check("digger rejects friendly hidden prey", !(bool)Internal("AnimalBurrowHunt", "Eligible", digger, refuge));
            digger.SetFaction(null); rat.SetFaction(null);
            Write($"EXCAVATION prerequisites: spawned={digger.Spawned}, downed={digger.Downed}, walkable={digger.Position.Walkable(map)}, reserve={digger.CanReserve(refuge)}, reach={digger.CanReach(refuge, PathEndMode.Touch, Danger.Some)}");
            digger.jobs.StartJob(JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Zoology_ExcavateBurrow"), refuge, rat), JobCondition.InterruptForced);
            Write($"EXCAVATION start: job={digger.CurJob?.def.defName}, contents={refuge.ContainedThing}, eligible={Internal("AnimalBurrowHunt", "Eligible", digger, refuge)}");
            Action nativeTick = (Action)Delegate.CreateDelegate(typeof(Action), Find.TickManager, typeof(TickManager).GetMethod("DoSingleTick", All));
            for (int i = 0; i < 350; i++) nativeTick();
            Write($"EXCAVATION end: job={digger.CurJob?.def.defName}, contents={refuge.ContainedThing}, ratSpawned={rat.Spawned}, cooldown={rat.TryGetComp<CompAnimalStrategyState>().burrowReentryAfter}, tick={GenTicks.TicksGame}");
            Check("predator excavation exposes prey", !refuge.HasAnyContents && (rat.Spawned || rat.Dead));
            Check("predator excavation also prevents reentry", rat.TryGetComp<CompAnimalStrategyState>().burrowReentryAfter > GenTicks.TicksGame);
            DisposePawn(rat); DisposePawn(digger); refuge.Destroy();
        }
        private void RunNewNuanceChecks(Map map)
        {
            Action tick = (Action)Delegate.CreateDelegate(typeof(Action), Find.TickManager, typeof(TickManager).GetMethod("DoSingleTick", All));
            IntVec3 origin = CellFinder.RandomClosewalkCellNear(new IntVec3(70, 0, 35), map, 2);
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(origin, 22f, true))
            {
                if (!cell.InBounds(map)) continue;
                cell.GetEdifice(map)?.Destroy();
                map.terrainGrid.SetTerrain(cell, DefDatabase<TerrainDef>.GetNamed("Soil"));
            }
            for (int x = origin.x - 3; x <= Math.Min(map.Size.x - 2, origin.x + 145); x++)
                for (int z = origin.z - 3; z <= origin.z + 3; z++)
                {
                    IntVec3 cell = new IntVec3(x, 0, z);
                    foreach (Thing obstacle in cell.GetThingList(map).Where(t => t is not Pawn && t.def.passability == Traversability.Impassable).ToList()) obstacle.Destroy();
                    map.terrainGrid.SetTerrain(cell, DefDatabase<TerrainDef>.GetNamed("Soil"));
                }
            map.pathing.RecalculateAllPerceivedPathCosts();
            Pawn lynx = Spawn("Lynx", map), wolf = Spawn("Wolf_Timber", map), human = Spawn("Colonist", map);
            foreach (Pawn p in new[] { lynx, wolf, human }) Wait(p);
            human.equipment.DestroyAllEquipment();
            Rand.PushState(719); float expectedRandom = Rand.Value; Rand.PopState();
            Rand.PushState(719);
            for (int i = 0; i < 100; i++) Internal("AnimalDefense", "IsRanged", wolf);
            float observedRandom = Rand.Value; Rand.PopState();
            Check("refuge ranged capability query preserves combat RNG", observedRandom == expectedRandom);
            lynx.Position = origin; lynx.Notify_Teleported(false);
            wolf.Position = origin + IntVec3.West * 8; wolf.Notify_Teleported(false);
            human.Position = origin + IntVec3.West * 10; human.Notify_Teleported(false);
            Plant tree = (Plant)GenSpawn.Spawn(DefDatabase<ThingDef>.GetNamed("Plant_TreeOak"), origin + IntVec3.East * 2, map);
            tree.Growth = 1f;
            map.pathing.RecalculateAllPerceivedPathCosts();
            Write($"ARENA originWalkable={origin.Walkable(map)}, treeWalkable={tree.Position.Walkable(map)}, destinationWalkable={(origin + IntVec3.East * 140).Walkable(map)}");
            lynx.needs.rest.CurLevelPercentage = 1f;
            lynx.TryGetComp<CompAnimalStrategyState>().nextDefensePlan = 0;
            typeof(JobGiver_AnimalBurrow).GetMethod("TryGiveJob", All).Invoke(new JobGiver_AnimalBurrow(), new object[] { lynx });
            Check("ordinary rest planning cannot delay defensive climb", lynx.TryGetComp<CompAnimalStrategyState>().nextDefensePlan == 0);
            foreach (Pawn threat in new[] { human, wolf })
            {
                lynx.TryGetComp<CompAnimalStrategyState>().nextDefensePlan = 0;
                Job flee = JobMaker.MakeJob(JobDefOf.Flee, origin + IntVec3.East * 12, threat);
                Job selected = (Job)Internal("AnimalDefense", "Choose", lynx, flee);
                Write($"LYNX threat={threat.def.defName}, selected={selected?.def.defName}, target={selected?.targetA}, food={lynx.needs.food.CurLevelPercentage:F2}");
                Check("fed lynx chooses tree against " + threat.def.defName, selected?.def.defName == "Zoology_TreeRefuge");
            }
            DisposePawn(human); // Keep the sleep test free of unrelated human/ranged threats.
            lynx.Position = tree.Position; lynx.Notify_Teleported(false); lynx.needs.rest.CurLevelPercentage = .1f;
            lynx.jobs.StartJob(JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Zoology_SleepInTree"), tree), JobCondition.InterruptForced);
            for (int i = 0; i < 5 && !(bool)Internal("AnimalDefense", "IsInTree", lynx); i++) lynx.jobs.curDriver.DriverTick();
            float priorInjury = lynx.health.hediffSet.hediffs.OfType<Hediff_Injury>().Sum(h => h.Severity);
            wolf.Position = tree.Position + IntVec3.West; wolf.Notify_Teleported(false);
            wolf.jobs.StartJob(JobMaker.MakeJob(JobDefOf.AttackMelee, lynx), JobCondition.InterruptForced);
            for (int i = 0; i < 150; i++) tick();
            Write($"TREE SLEEP job={lynx.CurJob?.def.defName}, atHeight={Internal("AnimalDefense", "IsInTree", lynx)}, injuryBefore={priorInjury}, injuryAfter={lynx.health.hediffSet.hediffs.OfType<Hediff_Injury>().Sum(h=>h.Severity)}, wolfJob={wolf.CurJob?.def.defName}");
            Check("sleeping tree animal remains protected against ground melee", (bool)Internal("AnimalDefense", "IsInTree", lynx)
                && lynx.health.hediffSet.hediffs.OfType<Hediff_Injury>().Sum(h => h.Severity) <= priorInjury + .001f && wolf.CurJob?.def != JobDefOf.AttackMelee);
            Pawn gunner = Spawn("Colonist", map); Wait(gunner);
            gunner.Position = tree.Position + IntVec3.South * 4; gunner.Notify_Teleported(false);
            gunner.skills.GetSkill(SkillDefOf.Shooting).Level = 20;
            gunner.equipment.DestroyAllEquipment();
            var rifle = (ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Gun_BoltActionRifle"));
            gunner.equipment.AddEquipment(rifle);
            Verb shot = gunner.equipment.PrimaryEq.PrimaryVerb;
            ThingComp ammo = rifle.AllComps.FirstOrDefault(c => c.GetType().FullName == "CombatExtended.CompAmmoUser");
            object ammoDef = null;
            if (ammo != null)
            {
                object set = ammo.GetType().GetProperty("CurAmmoSet").GetValue(ammo);
                var links = (System.Collections.IList)set.GetType().GetField("ammoTypes").GetValue(set);
                ammoDef = links[0].GetType().GetField("ammo").GetValue(links[0]);
            }
            ammo?.GetType().GetMethod("ResetAmmoCount", All).Invoke(ammo, new object[] { ammoDef });
            IntVec3 firingCell = GenRadial.RadialCellsAround(tree.Position, 12f, false)
                .First(c => c.InBounds(map) && c.Standable(map) && (c - tree.Position).LengthHorizontalSquared >= 4
                    && shot.CanHitTargetFrom(c, lynx));
            gunner.Position = firingCell; gunner.Notify_Teleported(false);
            bool hitAtHeight = false; int launched = 0;
            for (int attempt = 0; attempt < 20 && !hitAtHeight && !lynx.Dead; attempt++)
            {
                ammo?.GetType().GetMethod("ResetAmmoCount", All).Invoke(ammo, new object[] { ammoDef });
                shot.Reset(); bool castStarted = shot.TryStartCastOn(lynx);
                if (attempt == 0) Write($"PROJECTILE context: verb={shot.GetType().Name}, started={castStarted}, available={shot.Available()}, canHit={shot.CanHitTarget(lynx)}, ammo={ammoDef}, mag={ammo?.GetType().GetProperty("CurMagCount").GetValue(ammo)}");
                if ((bool)shot.GetType().GetMethod("TryCastShot", All).Invoke(shot, null)) launched++;
                int priorTick = GenTicks.TicksGame;
                // Isolate projectile collision from the separately tested AI decision to descend.
                for (int flight = 0; flight < 60; flight++)
                {
                    TicksField.SetValue(Find.TickManager, priorTick + flight);
                    foreach (Thing projectile in map.listerThings.AllThings.Where(t => t.def.projectile != null).ToList())
                    {
                        projectile.GetType().GetMethod("Tick", All).Invoke(projectile, null);
                        if (!projectile.Destroyed) projectile.GetType().GetMethod("TickInterval", All).Invoke(projectile, new object[] { 1 });
                    }
                    if (lynx.Dead || lynx.health.hediffSet.hediffs.OfType<Hediff_Injury>().Sum(h => h.Severity) > priorInjury + .001f)
                    { hitAtHeight = true; break; }
                }
                TicksField.SetValue(Find.TickManager, priorTick);
            }
            Write($"PROJECTILE launched={launched}, elevatedHit={hitAtHeight}, dead={lynx.Dead}, atHeight={Internal("AnimalDefense", "IsInTree", lynx)}");
            Check("real projectile can hit elevated animal and end tree state", launched > 0 && hitAtHeight && !(bool)Internal("AnimalDefense", "IsInTree", lynx));
            DisposePawn(gunner);
            foreach (Pawn p in new[] { lynx, wolf }) DisposePawn(p); if (!tree.Destroyed) tree.Destroy();
            Pawn cat = Spawn("Cat", map), sparrow = Spawn("Sparrow", map);
            cat.Position = origin; cat.Notify_Teleported(false);
            sparrow.Position = origin + IntVec3.East * 12; sparrow.Notify_Teleported(false);
            BeginHunt(cat, sparrow);
            Job escape = JobMaker.MakeJob(JobDefOf.Flee, origin + IntVec3.East * 140, cat);
            escape.expiryInterval = 1500;
            int escapeStartedAt = GenTicks.TicksGame;
            sparrow.jobs.StartJob(escape, JobCondition.InterruptForced); sparrow.flight.StartFlying();
            for (int i = 0; i < 60 && !(bool)Internal("AnimalHuntStrategy", "HopelessPursuit", cat, sparrow); i++) tick();
            Check("flying sparrow is hopeless prey after escape starts", (bool)Internal("AnimalHuntStrategy", "HopelessPursuit", cat, sparrow));
            var strategy = cat.def.GetModExtension<ModExtension_PredationStrategy>();
            var prior = strategy.strategy; strategy.strategy = PredationStrategy.No;
            Check("No strategy retains old pursuit rule", !(bool)Internal("AnimalHuntStrategy", "HopelessPursuit", cat, sparrow));
            strategy.strategy = prior;
            int start = escapeStartedAt;
            Write($"CHASE initial: tick={start}, counter={typeof(ZoologyPursuitGameComponent).GetField("tickCounter", All).GetValue(ZoologyPursuitGameComponent.Instance)}, components={Current.Game.components.OfType<ZoologyPursuitGameComponent>().Count()}, enabled={ZoologyModSettings.Instance.EnablePreyFleeFromPredators}");
            for (int i = 0; GenTicks.TicksGame - start < 300 && !ZoologyPursuitGameComponent.Instance.IsPairBlockedNow(cat, sparrow); i++)
            {
                tick();
                if (i % 50 == 49) Write($"CHASE t={GenTicks.TicksGame-start}, tick={GenTicks.TicksGame}, counter={typeof(ZoologyPursuitGameComponent).GetField("tickCounter", All).GetValue(ZoologyPursuitGameComponent.Instance)}, hunter={cat.CurJob?.def.defName}, prey={sparrow.CurJob?.def.defName}, flying={sparrow.Flying}, since={sparrow.TryGetComp<CompAnimalStrategyState>().fleeingSince}, threat={sparrow.TryGetComp<CompAnimalStrategyState>().fleeThreat}, hopeless={Internal("AnimalHuntStrategy", "HopelessPursuit", cat, sparrow)}, runnerSpeed={Internal("AnimalLocomotion", "SpeedAt", sparrow, sparrow.Position, true)}, pursuerSpeed={Internal("AnimalLocomotion", "SpeedAt", cat, sparrow.Position, true)}, pursuerLocal={Internal("AnimalLocomotion", "SpeedAt", cat, cat.Position, true)}, separation={cat.Position.DistanceTo(sparrow.Position)}");
            }
            int elapsed = GenTicks.TicksGame - start;
            Write("HOPELESS pursuit elapsed=" + elapsed);
            Check("active strategy abandons escaped much faster prey in 200-300 ticks", elapsed >= 200 && elapsed <= 300
                && ZoologyPursuitGameComponent.Instance.IsPairBlockedNow(cat, sparrow) && !((bool)Internal("AnimalHuntStrategy", "IsHunt", cat, sparrow)));
            DisposePawn(cat); DisposePawn(sparrow);
            foreach (DamageDef damage in DefDatabase<DamageDef>.AllDefs.Where(d => d.defName.StartsWith("ZVenom_Damage_")).ToList())
            {
                foreach (float received in new[] { 1f, 8.65f })
                {
                    Pawn patient = Spawn("Colonist", map); Wait(patient);
                    DamageInfo info = new DamageInfo(damage, received, 100f);
                    var result = patient.TakeDamage(info);
                    var venom = patient.health.hediffSet.hediffs.OfType<Hediff_AnimalVenom>().FirstOrDefault();
                    float expected = damage.additionalHediffs.Single().severityPerDamageDealt * result.totalDamageDealt / patient.BodySize;
                    Check("real damage single injection " + damage.defName + " " + received,
                        result.totalDamageDealt > 0 && venom != null && Math.Abs(venom.Severity - Math.Min(expected, venom.def.maxSeverity)) < .003f);
                    float initial = venom?.Severity ?? 0, peak = initial;
                    // Verify real injection above, then isolate toxin progression from traumatic bleeding.
                    foreach (Hediff trauma in patient.health.hediffSet.hediffs.Where(h => h is not Hediff_AnimalVenom).ToList()) patient.health.RemoveHediff(trauma);
                    int savedTick = GenTicks.TicksGame;
                    for (int i = 0; i < 140000 && !patient.Dead && venom != null && patient.health.hediffSet.hediffs.Contains(venom); i++)
                    {
                        TicksField.SetValue(Find.TickManager, savedTick + i);
                        patient.health.HealthTick(); patient.health.HealthTickInterval(1);
                        peak = Math.Max(peak, venom.Severity);
                    }
                    TicksField.SetValue(Find.TickManager, savedTick);
                    Write($"PROFILE {damage.defName}: received={result.totalDamageDealt:F3}, initial={initial:F4}, peak={peak:F4}, dead={patient.Dead}, final={venom?.Severity:F4}, stage={venom?.CurStage?.label}, body={patient.BodySize:F3}");
                    if (damage.defName == "ZVenom_Damage_VaranidBite")
                        Check("varanid visible effects on human " + received, initial > 0 && venom.def.stages[0].totalBleedFactor > 1f && peak >= .12f);
                    DisposePawn(patient);
                }
            }
        }
        private void RunContainedMedicalChecks(Map map)
        {
            Pawn mother = Spawn("Rat", map); Wait(mother); mother.gender = Gender.Female;
            mother.ageTracker.AgeBiologicalTicks = 5 * 3600000L; mother.needs.rest.CurLevelPercentage = .1f;
            var pregnancy = (Hediff_Pregnant)HediffMaker.MakeHediff(HediffDefOf.Pregnant, mother);
            pregnancy.Severity = .2f; mother.health.AddHediff(pregnancy);
            var milk = mother.health.AddHediff(AnimalLactationUtility.LactatingHediffDef);
            var wound = HediffMaker.MakeHediff(HediffDefOf.Cut, mother, mother.RaceProps.body.corePart);
            float bleedingPerSeverity = wound.def.injuryProps.bleedRate * wound.Part.def.bleedRate * mother.RaceProps.bleedRateFactor / mother.HealthScale;
            wound.Severity = Math.Min(mother.health.hediffSet.GetPartHealth(wound.Part) * .25f, .6f / Math.Max(.0001f, bleedingPerSeverity));
            mother.health.AddHediff(wound);
            Write("MEDICAL initial bleed rate=" + mother.health.hediffSet.BleedRateTotal);
            var poison = Inject(mother, .0004f); float poisonStart = poison.Severity;
            var home = (Building_AnimalBurrow)GenSpawn.Spawn(DefDatabase<ThingDef>.GetNamed("Zoology_AnimalBurrow"), mother.Position, map);
            home.InitializeFor(mother);
            Pawn threat = Spawn("Zoology_ValidationWeakBurrowPredator", map); Wait(threat);
            threat.Position = home.Position + IntVec3.West * 5; threat.Notify_Teleported(false);
            home.SetThreat(threat); Check("medical fixture enters before late pregnancy", home.TryAcceptThing(mother));
            int savedTick = GenTicks.TicksGame;
            MethodInfo tickBurrow = typeof(Building_AnimalBurrow).GetMethod("Tick", All);
            for (int i = 0; i < 500; i++) { TicksField.SetValue(Find.TickManager, savedTick + i); tickBurrow.Invoke(home, null); }
            TicksField.SetValue(Find.TickManager, savedTick);
            Check("pregnancy progresses inside burrow", home.Contains(mother) && pregnancy.Severity > .2f);
            Check("lactation tracker progresses inside burrow", milk.ageTicks >= 500);
            Check("disease progresses inside burrow", poison.Severity > poisonStart);
            Check("bleeding progresses inside burrow", mother.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss)?.Severity > 0f);
            pregnancy.Severity = .95f; tickBurrow.Invoke(home, null);
            Check("late pregnancy exits before off-map birth", mother.Spawned && !home.Contains(mother) && !home.Accepts(mother)
                && mother.CurJob?.def != JobDefOf.Carried);
            DisposePawn(mother); DisposePawn(threat);
            Pawn occupant = Spawn("Rat", map); Wait(occupant); occupant.needs.rest.CurLevelPercentage = .1f;
            home.InitializeFor(occupant); home.TryAcceptThing(occupant); occupant.Kill(null);
            for (int i = 0; i < 250; i++) { TicksField.SetValue(Find.TickManager, savedTick + i); tickBurrow.Invoke(home, null); }
            TicksField.SetValue(Find.TickManager, savedTick);
            Check("death inside burrow preserves and ejects corpse", occupant.Corpse?.Spawned == true && !home.HasAnyContents);
            DisposePawn(occupant);
            home.HitPoints = home.MaxHitPoints - 5; int damaged = home.HitPoints;
            for (int i = 0; i < 250; i++) { TicksField.SetValue(Find.TickManager, 60000 + i); tickBurrow.Invoke(home, null); }
            TicksField.SetValue(Find.TickManager, savedTick);
            Check("unused burrow decays one hit point per day", home.HitPoints == damaged - 1);
            occupant = Spawn("Rat", map); Wait(occupant); occupant.needs.rest.CurLevelPercentage = .1f;
            home.InitializeFor(occupant); home.TryAcceptThing(occupant); damaged = home.HitPoints;
            for (int i = 0; i < 250; i++) { TicksField.SetValue(Find.TickManager, 62500 + i); tickBurrow.Invoke(home, null); }
            TicksField.SetValue(Find.TickManager, savedTick);
            Check("occupied burrow repairs hit points", home.HitPoints == damaged + 1);
            home.Destroy(); DisposePawn(occupant);
        }
        private void RunSharedBurrowChecks(Map map)
        {
            // Native predators elsewhere on a random quicktest map must not eat test children.
            foreach (Pawn external in map.mapPawns.AllPawnsSpawned.Where(p => p.RaceProps.predator).ToList()) Wait(external);
            var def = DefDatabase<ThingDef>.GetNamed("Zoology_AnimalBurrow");
            Pawn mother = Spawn("Zoology_ValidationFamily", map); mother.gender = Gender.Female; Wait(mother);
            mother.Position = CellFinder.RandomClosewalkCellNear(new IntVec3(35, 0, 65), map, 3); mother.Notify_Teleported(false);
            mother.needs.rest.CurLevelPercentage = .1f;
            Pawn a = Spawn("Zoology_ValidationFamily", map), b = Spawn("Zoology_ValidationFamily", map);
            foreach (Pawn child in new[] { a, b })
            {
                child.relations.AddDirectRelation(PawnRelationDefOf.Parent, mother); child.ageTracker.AgeBiologicalTicks = 0;
                child.Position = mother.Position + (child == a ? IntVec3.North : IntVec3.East); child.Notify_Teleported(false);
                child.needs.rest.CurLevelPercentage = .1f;
            }
            var home = (Building_AnimalBurrow)GenSpawn.Spawn(def, mother.Position, map); home.InitializeFor(mother);
            float oldCapacity = home.TotalBodyCapacity;
            Check("mother enters and expands for following children", home.TryAcceptThing(mother) && home.TotalBodyCapacity > oldCapacity
                && home.TotalBodyCapacity + .0001f >= mother.BodySize + a.BodySize + b.BodySize);
            Action tick = (Action)Delegate.CreateDelegate(typeof(Action), Find.TickManager, typeof(TickManager).GetMethod("DoSingleTick", All));
            for (int i = 0; i < 200; i++) tick();
            foreach (Pawn member in new[] { mother, a, b })
                Write($"FAMILY member={member}, spawned={member.Spawned}, dead={member.Dead}, holder={member.ParentHolder}, job={member.CurJob?.def.defName}, food={member.needs.food.CurLevelPercentage}, accepts={home.Accepts(member)}, expected={home.TotalBodyCapacity}, occupied={home.OccupiedBodySize}");
            Check("children follow mother into shared burrow", home.Contains(mother) && home.Contains(a) && home.Contains(b));
            Pawn weak = Spawn("Zoology_ValidationWeakBurrowPredator", map); Wait(weak);
            weak.Position = home.Position + IntVec3.South; weak.Notify_Teleported(false);
            bool childThreat = (bool)Internal("Patch_IsAcceptablePreyForPredator", "IsSuitableWithoutYoungProtection", weak, a);
            bool motherThreat = (bool)Internal("Patch_IsAcceptablePreyForPredator", "IsSuitableWithoutYoungProtection", weak, mother);
            Check("family safety fixture: predator threatens child but not mother", childThreat && !motherThreat);
            Check("resident mother blocks weaker predator without ejecting babies", !home.TryAcceptThing(weak) && home.Contains(mother) && home.Contains(a) && home.Contains(b));
            Check("burrow cannot be claimed", !home.ClaimableBy(Faction.OfPlayer).Accepted);
            a.needs.food.CurLevelPercentage = .1f;
            int savedTick = GenTicks.TicksGame;
            for (int i = 0; i < 250 && home.HasAnyContents; i++)
            {
                TicksField.SetValue(Find.TickManager, savedTick + i);
                typeof(Building_AnimalBurrow).GetMethod("Tick", All).Invoke(home, null);
            }
            TicksField.SetValue(Find.TickManager, savedTick);
            Check("hungry baby causes coherent family exit", mother.Spawned && a.Spawned && b.Spawned);
            Check("fed mother cannot hide again while baby remains hungry", !home.Accepts(mother) && !home.Accepts(b));
            a.needs.food.CurLevelPercentage = 1f;
            home.Destroy();
            Check("family survives shared burrow destruction", mother.Spawned && a.Spawned && b.Spawned);
            foreach (Pawn p in new[] { mother, a, b }) DisposePawn(p);
            Pawn rat = Spawn("Rat", map); Wait(rat); rat.ageTracker.AgeBiologicalTicks = 5 * 3600000L;
            rat.Position = weak.Position + IntVec3.West * 2; rat.Notify_Teleported(false);
            var communal = (Building_AnimalBurrow)GenSpawn.Spawn(def, rat.Position, map); communal.InitializeFor(rat);
            var rats = new List<Pawn> { rat }; communal.TryAcceptThing(rat);
            for (int i = 0; i < 3; i++)
            {
                Pawn other = Spawn("Rat", map); Wait(other); other.ageTracker.AgeBiologicalTicks = rat.ageTracker.AgeBiologicalTicks;
                rats.Add(other); communal.TryAcceptThing(other);
            }
            Pawn extra = Spawn("Rat", map); extra.ageTracker.AgeBiologicalTicks = rat.ageTracker.AgeBiologicalTicks;
            Check("social body capacity limits occupants", communal.Occupants.Count() == 4 && !communal.Accepts(extra));
            communal.Destroy(); foreach (Pawn p in rats) DisposePawn(p); DisposePawn(extra);
            var mixed = (Building_AnimalBurrow)GenSpawn.Spawn(def, weak.Position + IntVec3.East * 2, map);
            mixed.InitializeFor(weak);
            Pawn prey = Spawn("Rat", map); Wait(prey); mixed.TryAcceptThing(prey);
            Check("predator entering for shelter ejects threatened occupant", mixed.TryAcceptThing(weak) && mixed.Contains(weak) && prey.Spawned);
            Check("prey cannot enter predator occupied burrow", !mixed.Accepts(prey));
            mixed.Destroy(); DisposePawn(prey); DisposePawn(weak);
            Pawn tester = Spawn("Rat", map); Wait(tester);
            IntVec3 cell = GenRadial.RadialCellsAround(tester.Position, 5f, true).First(c => c.Standable(map) && c.GetEdifice(map) == null && c.GetFirstPawn(map) == null);
            TerrainDef previous = cell.GetTerrain(map);
            foreach (string terrainName in new[] { "Soil", "Marsh", "Concrete", "Granite_Rough" })
            {
                TerrainDef terrain = DefDatabase<TerrainDef>.GetNamed(terrainName); map.terrainGrid.SetTerrain(cell, terrain);
                bool expected = terrain.affordances.Contains(Building_AnimalBurrow.Settings.diggableTerrainAffordance);
                Check("dig ground matches grave affordance " + terrainName, (bool)Internal("JobGiver_AnimalBurrow", "Diggable", tester, cell) == expected);
            }
            map.terrainGrid.SetTerrain(cell, previous); DisposePawn(tester);
        }
    }
}
