using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    // The engine keeps the original DamageInfo for PostApplyDamage even if armor converts
    // a sharp injury to Blunt. Track actual penetrating injury damage within this one hit.
    internal sealed class VenomHitFrame
    {
        [ThreadStatic] internal static VenomHitFrame active;
        internal VenomHitFrame previous;
        internal Pawn victim;
        internal DamageDef damage;
        internal float penetratingDamage;
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    internal static class Patch_VenomDamageScope
    {
        static void Prefix(Thing __instance, DamageInfo dinfo, out VenomHitFrame __state)
        {
            __state = null;
            if (!(__instance is Pawn victim) || dinfo.Def.additionalHediffs == null) return;
            foreach (DamageDefAdditionalHediff effect in dinfo.Def.additionalHediffs)
                if (effect.hediff?.hediffClass == typeof(Hediff_AnimalVenom))
                {
                    __state = new VenomHitFrame { previous = VenomHitFrame.active, victim = victim, damage = dinfo.Def };
                    VenomHitFrame.active = __state;
                    return;
                }
        }
        static void Finalizer(VenomHitFrame __state)
        {
            if (__state != null) VenomHitFrame.active = __state.previous;
        }
    }

    [HarmonyPatch(typeof(DamageWorker_AddInjury), "FinalizeAndAddInjury",
        new[] { typeof(Pawn), typeof(float), typeof(DamageInfo), typeof(DamageWorker.DamageResult) })]
    internal static class Patch_VenomPenetratingDamage
    {
        static void Postfix(Pawn pawn, DamageInfo dinfo, float __result)
        {
            VenomHitFrame frame = VenomHitFrame.active;
            if (__result > 0f && frame?.victim == pawn && dinfo.Def == frame.damage)
                frame.penetratingDamage += __result;
        }
    }

    internal interface IDelayedPredationSource
    {
        Pawn Hunter { get; }
        int SourceTick { get; }
    }

    // One shared class for all profiles; severity, tending and immunity remain vanilla.
    public class Hediff_AnimalVenom : HediffWithComps, IDelayedPredationSource
    {
        private Pawn hunter;
        private int sourceTick;
        public Pawn Hunter => hunter;
        public int SourceTick => sourceTick;
        public void RecordSource(DamageInfo? dinfo)
        {
            Pawn source = dinfo?.Instigator as Pawn;
            hunter = source != null && AnimalHuntStrategy.IsHunt(source, pawn) ? source : null;
            sourceTick = GenTicks.TicksGame;
        }
        public override bool TryMergeWith(Hediff other)
        {
            if (!(other is Hediff_AnimalVenom venom) || other.def != def || other.Part != Part) return false;
            // Last successful injection wins, including clearing hunting attribution after a non-hunt injection.
            hunter = venom.hunter; sourceTick = venom.sourceTick;
            return base.TryMergeWith(other);
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref hunter, "venomHunter");
            Scribe_Values.Look(ref sourceTick, "venomSourceTick");
        }
    }

    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.AddHediff),
        new[] { typeof(Hediff), typeof(BodyPartRecord), typeof(DamageInfo?), typeof(DamageWorker.DamageResult) })]
    internal static class Patch_AnimalVenomApplicability
    {
        static void Postfix() => AnimalLocomotion.Invalidate();
        static bool Prefix(Pawn ___pawn, Hediff hediff, DamageInfo? dinfo)
        {
            if (!(hediff is Hediff_AnimalVenom venom)) return true;
            VenomHitFrame hit = VenomHitFrame.active;
            if (hit?.victim == ___pawn && dinfo?.Def == hit.damage)
            {
                if (hit.penetratingDamage <= 0f) return false;
                foreach (DamageDefAdditionalHediff effect in hit.damage.additionalHediffs)
                    if (effect.hediff == venom.def)
                    {
                        float dose = effect.severityFixed > 0f ? effect.severityFixed : hit.penetratingDamage * effect.severityPerDamageDealt;
                        if (effect.victimSeverityScalingByInvBodySize) dose /= ___pawn.BodySize;
                        if (effect.victimSeverityScaling != null)
                        {
                            float factor = ___pawn.GetStatValue(effect.victimSeverityScaling);
                            dose *= effect.inverseStatScaling ? Math.Max(1f - factor, 0f) : factor;
                        }
                        venom.Severity = dose;
                        break;
                    }
            }
            if (___pawn.Dead || !___pawn.RaceProps.IsFlesh || ___pawn.RaceProps.IsMechanoid || venom.Severity <= 0f) return false;
            // Vital organ effects require that capacity in this body's actual schema.
            // Moving alone must not make a respiratory or circulatory toxin universally lethal.
            foreach (HediffStage stage in hediff.def.stages)
                if (stage.capMods != null)
                    foreach (PawnCapacityModifier mod in stage.capMods)
                        if ((mod.capacity == PawnCapacityDefOf.Breathing || mod.capacity == PawnCapacityDefOf.BloodPumping)
                            && !mod.capacity.Worker.CanHaveCapacity(___pawn.RaceProps.body)) return false;
            bool applicable = hediff.def.stages.Any(s => s.capMods == null || s.capMods.Count == 0
                ? false : s.capMods.Any(c => c.capacity.Worker.CanHaveCapacity(___pawn.RaceProps.body)));
            if (!applicable) return false;
            venom.pawn = ___pawn;
            venom.RecordSource(dinfo);
            return true;
        }
    }

    public class IngestionOutcomeDoer_AnimalAntivenom : IngestionOutcomeDoer
    {
        public HediffDef venom;
        protected override void DoIngestionOutcomeSpecial(Pawn pawn, Thing ingested, int ingestedCount)
        {
            Hediff active = pawn.health.hediffSet.GetFirstHediffOfDef(venom);
            if (active is Hediff_AnimalVenom) pawn.health.RemoveHediff(active);
        }
    }
    public class ModExtension_VenomSample : DefModExtension { public ThingDef sample; }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.ButcherProducts))]
    internal static class Patch_AnimalVenomSample
    {
        static IEnumerable<Thing> Postfix(IEnumerable<Thing> __result, Pawn __instance, float efficiency)
        {
            foreach (Thing product in __result) yield return product;
            var extension = DefModExtensionCache<ModExtension_VenomSample>.Get(__instance.def);
            if (extension?.sample != null && efficiency > 0f && __instance.ageTracker.CurLifeStage.reproductive)
                yield return ThingMaker.MakeThing(extension.sample);
        }
    }

    internal static class DelayedPredationAttribution
    {
        public static Pawn CausalHunter(Pawn victim, object[] args)
        {
            // Any external killing blow takes precedence. Do not attribute hunger, blood loss, etc.
            if (args == null || (args.Length > 0 && args[0] != null)) return null;
            Hediff exact = args.Length > 1 ? args[1] as Hediff : null;
            if (exact != null && !(exact is IDelayedPredationSource)) return null;
            Hediff causal = exact ?? victim.health.hediffSet.hediffs
                .Where(h => h is IDelayedPredationSource && h.CauseDeathNow())
                .OrderByDescending(h => ((IDelayedPredationSource)h).SourceTick).FirstOrDefault();
            if (!(causal is IDelayedPredationSource source)) return null;
            bool lethal = causal.CauseDeathNow();
            if (!lethal && exact == causal && (causal is Hediff_AnimalConstriction || causal is Hediff_AnimalVenom))
            {
                PawnCapacityDef failed = victim.health.ShouldBeDeadFromRequiredCapacity();
                lethal = failed != null && causal.CurStage?.capMods?.Any(c => c.capacity == failed && c.offset < 0f) == true;
            }
            return lethal ? source.Hunter : null;
        }
        public static bool TryRegister(Pawn victim, Pawn hunter)
        {
            Corpse corpse = victim.Corpse;
            if (hunter == null || hunter.Dead || !hunter.Spawned || corpse?.Spawned != true || hunter.Map != corpse.Map) return false;
            PredatorPreyPairGameComponent.Instance?.RegisterPairFromKill(hunter, corpse);
            return true;
        }
    }
}
