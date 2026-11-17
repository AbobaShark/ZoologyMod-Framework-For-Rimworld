using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    public static class ZoologyCeDamageTrace
    {
        private const string HarmonyId = "com.abobashark.zoology.debug.ce-damage";
        private static readonly Harmony Harmony = new Harmony(HarmonyId);
        private static readonly Stack<DamageFrame> DamageFrames = new Stack<DamageFrame>();
        private static readonly Stack<ArmorStep> ActiveArmorSteps = new Stack<ArmorStep>();
        private static readonly HashSet<int> ExplainedAttackers = new HashSet<int>();

        private static WeakReference<Pawn> watched;
        private static MethodBase takeDamageMethod;
        private static MethodBase armorMethod;
        private static MethodBase penetrationMethod;
        private static MethodBase meleeMethod;
        private static MethodInfo partialPawnMethod;
        private static MethodInfo partialApparelMethod;
        private static PropertyInfo lastAttackVerbProperty;
        private static PropertyInfo sharpApProperty;
        private static PropertyInfo bluntApProperty;

        private sealed class DamageFrame
        {
            internal Pawn victim;
            internal DamageInfo input;
            internal readonly List<string> armorSteps = new List<string>(2);
            internal string weapon;
        }

        private sealed class ArmorStep
        {
            internal DamageFrame frame;
            internal StringBuilder details;
            internal float before;
            internal float penetration;
        }

        private sealed class PenetrationStep
        {
            internal ArmorStep owner;
            internal string damageType;
            internal string source;
            internal float armor;
            internal float initialPenetration;
            internal float initialDamage;
            internal float density;
        }

        [DebugAction("Zoology", "Trace CE damage to animal (toggle)",
            actionType = DebugActionType.ToolMapForPawns,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void Toggle(Pawn pawn)
        {
            if (pawn?.RaceProps?.Animal != true)
            {
                Log.Warning("[Zoology CE trace] Select an animal.");
                return;
            }
            if (!CEChecker.IsCEInstalled())
            {
                Log.Warning("[Zoology CE trace] Combat Extended is not loaded.");
                return;
            }
            if (ZoologyMod.Settings?.AreAllRuntimeTogglesDisabled() == true)
            {
                Log.Warning("[Zoology CE trace] Zoology runtime patches are disabled.");
                return;
            }

            Pawn old = WatchedPawn();
            if (ReferenceEquals(old, pawn))
            {
                Stop();
                Log.Message("[Zoology CE trace] Stopped.");
                return;
            }

            Stop();
            try
            {
                Type ceArmor = AccessTools.TypeByName("CombatExtended.ArmorUtilityCE");
                Type ceVerb = AccessTools.TypeByName("CombatExtended.Verb_MeleeAttackCE");
                if (ceArmor == null || ceVerb == null)
                    throw new InvalidOperationException("CE armor utility or melee verb not found");

                takeDamageMethod = AccessTools.DeclaredMethod(typeof(Pawn), "TakeDamage",
                    new[] { typeof(DamageInfo) })
                    ?? AccessTools.Method(typeof(Thing), "TakeDamage",
                        new[] { typeof(DamageInfo) });
                armorMethod = AccessTools.Method(ceArmor, "GetAfterArmorDamage",
                    new[]
                    {
                        typeof(DamageInfo), typeof(Pawn), typeof(BodyPartRecord),
                        typeof(bool).MakeByRefType(), typeof(bool).MakeByRefType(),
                        typeof(bool).MakeByRefType()
                    });
                penetrationMethod = AccessTools.Method(ceArmor, "TryPenetrateArmor",
                    new[]
                    {
                        typeof(DamageDef), typeof(float),
                        typeof(float).MakeByRefType(), typeof(float).MakeByRefType(),
                        typeof(Thing), typeof(float)
                    });
                meleeMethod = AccessTools.Method(ceVerb, "TryCastShot");
                partialPawnMethod = AccessTools.Method(ceArmor, "PartialStat",
                    new[] { typeof(Pawn), typeof(StatDef), typeof(BodyPartRecord) });
                partialApparelMethod = AccessTools.Method(ceArmor, "PartialStat",
                    new[] { typeof(Apparel), typeof(StatDef), typeof(BodyPartRecord) });
                lastAttackVerbProperty = AccessTools.Property(ceVerb, "LastAttackVerb");
                sharpApProperty = AccessTools.Property(ceVerb, "ArmorPenetrationSharp");
                bluntApProperty = AccessTools.Property(ceVerb, "ArmorPenetrationBlunt");

                if (takeDamageMethod == null || armorMethod == null || meleeMethod == null
                    || penetrationMethod == null || partialPawnMethod == null
                    || partialApparelMethod == null)
                    throw new InvalidOperationException("A required CE/RimWorld combat method was not found");

                Harmony.Patch(takeDamageMethod,
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(ZoologyCeDamageTrace),
                        nameof(DamagePrefix))) { priority = Priority.Last },
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(ZoologyCeDamageTrace),
                        nameof(DamagePostfix))) { priority = Priority.Last },
                    finalizer: new HarmonyMethod(AccessTools.Method(typeof(ZoologyCeDamageTrace),
                        nameof(DamageFinalizer))));
                Harmony.Patch(armorMethod,
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(ZoologyCeDamageTrace),
                        nameof(ArmorPrefix))),
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(ZoologyCeDamageTrace),
                        nameof(ArmorPostfix))),
                    finalizer: new HarmonyMethod(AccessTools.Method(typeof(ZoologyCeDamageTrace),
                        nameof(ArmorFinalizer))));
                Harmony.Patch(penetrationMethod,
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(ZoologyCeDamageTrace),
                        nameof(PenetrationPrefix))),
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(ZoologyCeDamageTrace),
                        nameof(PenetrationPostfix))));
                Harmony.Patch(meleeMethod,
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(ZoologyCeDamageTrace),
                        nameof(MeleePrefix))),
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(ZoologyCeDamageTrace),
                        nameof(MeleePostfix))));

                watched = new WeakReference<Pawn>(pawn);
                Log.Message("[Zoology CE trace] Monitoring " + pawn.LabelShortCap
                    + " (" + pawn.def.defName + "). Select the same pawn again to stop."
                    + " Every received damage event reports CE armor processing and health damage.");
                LogArmorSummary(pawn);
            }
            catch (Exception ex)
            {
                Stop();
                Log.Error("[Zoology CE trace] Cannot start: " + ex);
            }
        }

        [DebugAction("Zoology", "Stop CE damage trace",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void StopFromDevMenu()
        {
            Stop();
            Log.Message("[Zoology CE trace] Stopped.");
        }

        public static void Stop()
        {
            watched = null;
            DamageFrames.Clear();
            ActiveArmorSteps.Clear();
            ExplainedAttackers.Clear();
            Unpatch(takeDamageMethod, nameof(DamagePrefix), nameof(DamagePostfix));
            if (takeDamageMethod != null)
            {
                try
                {
                    Harmony.Unpatch(takeDamageMethod,
                        AccessTools.Method(typeof(ZoologyCeDamageTrace), nameof(DamageFinalizer)));
                }
                catch (Exception ex)
                {
                    Log.ErrorOnce("[Zoology CE trace] Finalizer unpatch failed: " + ex, 2026111809);
                }
            }
            Unpatch(armorMethod, nameof(ArmorPrefix), nameof(ArmorPostfix));
            if (armorMethod != null)
            {
                try
                {
                    Harmony.Unpatch(armorMethod,
                        AccessTools.Method(typeof(ZoologyCeDamageTrace), nameof(ArmorFinalizer)));
                }
                catch (Exception ex)
                {
                    Log.ErrorOnce("[Zoology CE trace] Armor finalizer unpatch failed: " + ex, 2026111810);
                }
            }
            Unpatch(penetrationMethod, nameof(PenetrationPrefix), nameof(PenetrationPostfix));
            Unpatch(meleeMethod, nameof(MeleePrefix), nameof(MeleePostfix));
            takeDamageMethod = armorMethod = penetrationMethod = meleeMethod = null;
        }

        private static void Unpatch(MethodBase target, string prefix, string postfix)
        {
            if (target == null) return;
            try
            {
                Harmony.Unpatch(target, AccessTools.Method(typeof(ZoologyCeDamageTrace), prefix));
                Harmony.Unpatch(target, AccessTools.Method(typeof(ZoologyCeDamageTrace), postfix));
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[Zoology CE trace] Unpatch failed: " + ex, 2026111803);
            }
        }

        private static Pawn WatchedPawn()
        {
            Pawn pawn;
            return watched != null && watched.TryGetTarget(out pawn) ? pawn : null;
        }

        private static bool IsWatched(Pawn pawn)
        {
            return pawn != null && ReferenceEquals(pawn, WatchedPawn());
        }

        private static DamageFrame ActiveFrame(Pawn pawn)
        {
            if (!IsWatched(pawn)) return null;
            return DamageFrames.Count > 0 ? DamageFrames.Peek() : null;
        }

        private static void DamagePrefix(Thing __instance, DamageInfo dinfo, out DamageFrame __state)
        {
            __state = null;
            Pawn victim = __instance as Pawn;
            if (!IsWatched(victim)) return;
            try
            {
                var frame = new DamageFrame
                {
                    victim = victim,
                    input = dinfo,
                    weapon = dinfo.Tool?.label ?? dinfo.Weapon?.label ?? "(unspecified)"
                };
                DamageFrames.Push(frame);
                __state = frame;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[Zoology CE trace] Damage entry error: " + ex, 2026111804);
            }
        }

        private static void DamagePostfix(DamageWorker.DamageResult __result, DamageFrame __state)
        {
            if (__state == null) return;
            try
            {
                if (DamageFrames.Count > 0 && ReferenceEquals(DamageFrames.Peek(), __state))
                    DamageFrames.Pop();
                var data = __state.input;
                var text = new StringBuilder(512);
                text.Append("[Zoology CE trace] TICK ")
                    .Append(Find.TickManager?.TicksGame ?? -1)
                    .Append(" | target=").Append(__state.victim.LabelShortCap)
                    .Append(" | attacker=").Append(data.Instigator?.LabelShortCap ?? "(none)")
                    .Append(" | weapon/tool=").Append(__state.weapon)
                    .Append(" | type=").Append(data.Def?.defName ?? "(none)")
                    .Append(" | pre-armor damage=").Append(N(data.Amount))
                    .Append(" | AP=").Append(N(data.ArmorPenetrationInt))
                    .Append(" | total health damage=").Append(N(__result.totalDamageDealt))
                    .AppendLine();
                if (__state.armorSteps.Count == 0)
                    text.AppendLine("  No CE armor calculation observed for this damage event.");
                else
                    foreach (string step in __state.armorSteps)
                        text.AppendLine(step);
                if (__result.totalDamageDealt <= 0f)
                    text.AppendLine("  Result: no health damage. See CE armor result and attack metadata above.");
                Log.Message(text.ToString());
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[Zoology CE trace] Damage report error: " + ex, 2026111805);
            }
        }

        private static Exception DamageFinalizer(Exception __exception, DamageFrame __state)
        {
            if (__exception != null && __state != null
                && DamageFrames.Count > 0 && ReferenceEquals(DamageFrames.Peek(), __state))
                DamageFrames.Pop();
            return __exception;
        }

        private static void ArmorPrefix(DamageInfo originalDinfo, Pawn pawn,
            BodyPartRecord hitPart, out ArmorStep __state)
        {
            __state = null;
            DamageFrame frame = ActiveFrame(pawn);
            if (frame == null) return;
            try
            {
                var step = new ArmorStep
                {
                    frame = frame,
                    before = originalDinfo.Amount,
                    penetration = originalDinfo.ArmorPenetrationInt,
                    details = new StringBuilder(320)
                };
                var sb = step.details;
                var stat = originalDinfo.Def?.armorCategory?.armorRatingStat;
                sb.Append("  CE: part=").Append(hitPart?.def?.defName ?? "(unspecified)")
                    .Append("; category=").Append(stat?.defName ?? "(none)")
                    .Append("; incoming damage=").Append(N(step.before))
                    .Append("; incoming AP=").Append(N(step.penetration))
                    .AppendLine();

                if (stat != null && hitPart != null)
                {
                    sb.Append("    Natural armor: UI/general=")
                        .Append(N(pawn.GetStatValue(stat)))
                        .Append("; CE part-specific=").Append(N(PartialPawnArmor(pawn, stat, hitPart)))
                        .Append("; anatomical density=");
                    string densityDefName = stat == StatDefOf.ArmorRating_Sharp
                        ? "BodyPartSharpArmor"
                        : stat == StatDefOf.ArmorRating_Blunt
                            ? "BodyPartBluntArmor" : null;
                    StatDef densityStat = densityDefName == null ? null
                        : DefDatabase<StatDef>.GetNamedSilentFail(densityDefName);
                    sb.Append(densityStat == null ? "n/a" : N(pawn.GetStatValue(densityStat)));
                    AppendDurability(pawn, sb);
                    sb.AppendLine();
                    if (stat == StatDefOf.ArmorRating_Sharp)
                    {
                        sb.Append("    Blunt armor for possible CE sharp deflection: general=")
                            .Append(N(pawn.GetStatValue(StatDefOf.ArmorRating_Blunt)))
                            .Append("; part-specific=")
                            .Append(N(PartialPawnArmor(pawn, StatDefOf.ArmorRating_Blunt, hitPart)))
                            .AppendLine();
                    }

                    var apparel = pawn.apparel?.WornApparel;
                    if (apparel != null)
                    {
                        for (int i = apparel.Count - 1; i >= 0; i--)
                        {
                            Apparel item = apparel[i];
                            if (item?.def?.apparel?.CoversBodyPart(hitPart) != true)
                                continue;
                            sb.Append("    Worn armor ").Append(item.LabelShortCap)
                                .Append(": CE part-specific=")
                                .Append(N(PartialApparelArmor(item, stat, hitPart)))
                                .AppendLine();
                        }
                    }

                    if (originalDinfo.Def.harmAllLayersUntilOutside)
                    {
                        BodyPartRecord layer = hitPart;
                        while (layer.parent != null && layer.depth == BodyPartDepth.Inside)
                        {
                            layer = layer.parent;
                            sb.Append("    Outer body layer ").Append(layer.def.defName)
                                .Append(": CE armor=").Append(N(PartialPawnArmor(pawn, stat, layer)))
                                .AppendLine();
                        }
                    }
                }
                AppendAttackerPenetration(originalDinfo, sb);
                ActiveArmorSteps.Push(step);
                __state = step;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[Zoology CE trace] Armor entry error: " + ex, 2026111806);
            }
        }

        private static void ArmorPostfix(DamageInfo __result, object[] __args, ArmorStep __state)
        {
            if (__state == null) return;
            try
            {
                if (ActiveArmorSteps.Count > 0 && ReferenceEquals(ActiveArmorSteps.Peek(), __state))
                    ActiveArmorSteps.Pop();
                bool deflected = __args.Length > 3 && __args[3] is bool a && a;
                bool reduced = __args.Length > 4 && __args[4] is bool b && b;
                bool shield = __args.Length > 5 && __args[5] is bool c && c;
                __state.details.Append("    After CE armor: damage=")
                    .Append(N(__result.Amount))
                    .Append(" (net delta ").Append(N(__state.before - __result.Amount))
                    .Append("); resulting type=")
                    .Append(__result.Def?.defName ?? "(none)")
                    .Append("; hit part=").Append(__result.HitPart?.def?.defName ?? "(unspecified)")
                    .Append("; deflected=").Append(deflected)
                    .Append("; CE armorReduced flag=").Append(reduced)
                    .Append("; shield absorbed=").Append(shield)
                    .AppendLine();
                __state.frame.armorSteps.Add(__state.details.ToString());
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[Zoology CE trace] Armor result error: " + ex, 2026111807);
            }
        }

        private static Exception ArmorFinalizer(Exception __exception, ArmorStep __state)
        {
            if (__exception != null && __state != null
                && ActiveArmorSteps.Count > 0 && ReferenceEquals(ActiveArmorSteps.Peek(), __state))
                ActiveArmorSteps.Pop();
            return __exception;
        }

        private static void PenetrationPrefix(DamageDef def, float armorAmount,
            ref float penAmount, ref float dmgAmount, Thing armor, float partDensity,
            out PenetrationStep __state)
        {
            __state = null;
            if (ActiveArmorSteps.Count == 0 || DamageFrames.Count == 0)
                return;
            ArmorStep owner = ActiveArmorSteps.Peek();
            if (!ReferenceEquals(owner.frame, DamageFrames.Peek())
                || !IsWatched(owner.frame.victim))
                return;

            __state = new PenetrationStep
            {
                owner = owner,
                damageType = def?.defName ?? "(unknown)",
                source = armor?.LabelShortCap ?? "natural body armor",
                armor = armorAmount,
                initialPenetration = penAmount,
                initialDamage = dmgAmount,
                density = partDensity
            };
        }

        private static void PenetrationPostfix(bool __result,
            ref float penAmount, ref float dmgAmount, PenetrationStep __state)
        {
            if (__state == null) return;
            try
            {
                __state.owner.details.Append("    CE armor layer [")
                    .Append(__state.source).Append("] type=").Append(__state.damageType)
                    .Append("; armor=").Append(N(__state.armor))
                    .Append("; AP ").Append(N(__state.initialPenetration))
                    .Append(" -> ").Append(N(penAmount))
                    .Append("; damage ").Append(N(__state.initialDamage))
                    .Append(" -> ").Append(N(dmgAmount))
                    .Append("; part density=").Append(N(__state.density))
                    .Append("; penetrated=").Append(__result)
                    .AppendLine();
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[Zoology CE trace] Penetration step error: " + ex, 2026111811);
            }
        }

        private static void MeleePrefix(Verb __instance, out bool __state)
        {
            __state = IsWatched(__instance?.CurrentTarget.Thing as Pawn);
        }

        private static void MeleePostfix(Verb __instance, bool __result, bool __state)
        {
            if (!__state || __result) return;
            try
            {
                Log.Message("[Zoology CE trace] TICK "
                    + (Find.TickManager?.TicksGame ?? -1)
                    + " | melee attempt by " + (__instance.CasterPawn?.LabelShortCap ?? "(none)")
                    + " did not connect with " + (__instance.CurrentTarget.Thing?.LabelShortCap ?? "(none)")
                    + " (miss, dodge, parry or failed cast). No armor calculation for that attempt.");
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[Zoology CE trace] Melee attempt error: " + ex, 2026111808);
            }
        }

        private static float PartialPawnArmor(Pawn pawn, StatDef stat, BodyPartRecord part)
        {
            return (float)partialPawnMethod.Invoke(null, new object[] { pawn, stat, part });
        }

        private static float PartialApparelArmor(Apparel apparel, StatDef stat, BodyPartRecord part)
        {
            return (float)partialApparelMethod.Invoke(null, new object[] { apparel, stat, part });
        }

        private static void AppendDurability(Pawn pawn, StringBuilder sb)
        {
            var all = pawn.AllComps;
            if (all == null) return;
            foreach (ThingComp comp in all)
            {
                if (comp?.GetType().FullName != "CombatExtended.CompArmorDurability")
                    continue;
                Type t = comp.GetType();
                object current = AccessTools.Field(t, "curDurability")?.GetValue(comp);
                object maximum = AccessTools.Property(t, "maxDurability")?.GetValue(comp, null);
                object fraction = AccessTools.Property(t, "curDurabilityPercent")?.GetValue(comp, null);
                sb.Append("; CE durability=").Append(current ?? "?")
                    .Append("/").Append(maximum ?? "?")
                    .Append(" (fraction=").Append(fraction ?? "?").Append(")");
                break;
            }
        }

        private static void AppendAttackerPenetration(DamageInfo info, StringBuilder sb)
        {
            var attacker = info.Instigator as Pawn;
            if (attacker == null) return;
            object verb = lastAttackVerbProperty?.GetValue(null, null);
            if (verb != null && ReferenceEquals(CEReflectionUtility.GetCasterPawn(verb), attacker))
            {
                sb.Append("    Active CE attack verb: sharp AP=")
                    .Append(sharpApProperty == null ? "n/a" : N((float)sharpApProperty.GetValue(verb, null)))
                    .Append("; blunt AP=")
                    .Append(bluntApProperty == null ? "n/a" : N((float)bluntApProperty.GetValue(verb, null)));
                object tool;
                if (CEReflectionUtility.TryGetVerbTool(verb, out tool) && tool is Tool selected)
                    sb.Append("; selected tool=").Append(selected.label);
                sb.AppendLine();
            }

            if (!ExplainedAttackers.Add(attacker.thingIDNumber))
                return;

            StatDef displayed = DefDatabase<StatDef>.GetNamedSilentFail("MeleeArmorPenetration");
            if (attacker.RaceProps?.Animal == true && displayed != null
                && AnimalLifeStageCeApStatDisplay.TryCalculate(displayed.Worker,
                    StatRequest.For(attacker), out string label, out string explanation))
            {
                Log.Message("[Zoology CE trace] Attacker " + attacker.LabelShortCap
                    + " | CE UI weighted average: " + label
                    + " | individual natural tools:\n" + explanation);
            }
        }

        private static void LogArmorSummary(Pawn pawn)
        {
            var output = new StringBuilder(256);
            output.Append("[Zoology CE trace] Target's displayed armor: sharp=")
                .Append(N(pawn.GetStatValue(StatDefOf.ArmorRating_Sharp)))
                .Append(" mm RHA; blunt=")
                .Append(N(pawn.GetStatValue(StatDefOf.ArmorRating_Blunt)))
                .Append(" MPa; life stage=")
                .Append(pawn.ageTracker?.CurLifeStage?.defName ?? "(none)");
            AppendDurability(pawn, output);
            MethodBase ceDamageWorker = AccessTools.Method(typeof(DamageWorker_AddInjury),
                "ApplyDamageToPart");
            var patches = ceDamageWorker == null ? null : HarmonyLib.Harmony.GetPatchInfo(ceDamageWorker);
            bool ceArmorTranspiler = false;
            if (patches?.Transpilers != null)
                foreach (var patch in patches.Transpilers)
                    if (patch.PatchMethod?.DeclaringType?.FullName?.Contains(
                            "CombatExtended.HarmonyCE.Harmony_DamageWorker_AddInjury_ApplyDamageToPart") == true)
                        ceArmorTranspiler = true;
            output.Append("; CE damage-worker armor transpiler=")
                .Append(ceArmorTranspiler ? "installed" : "not detected");
            Log.Message(output.ToString());
        }

        private static string N(float value)
        {
            return value.ToString("0.######");
        }
    }
}
