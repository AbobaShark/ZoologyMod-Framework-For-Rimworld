using System.Runtime.CompilerServices;
using Verse;

namespace ZoologyMod
{
    public class Hediff_GrowthArrest : HediffWithComps
    {
        private static readonly ConditionalWeakTable<Pawn, Hediff_GrowthArrest> Active =
            new ConditionalWeakTable<Pawn, Hediff_GrowthArrest>();
        private static volatile bool used;

        internal static bool IsActive(Pawn pawn) =>
            used && pawn != null && Active.TryGetValue(pawn, out _);

        private void Register()
        {
            if (pawn == null) return;
            Active.GetValue(pawn, _ => this);
            used = true;
        }

        public override void PostAdd(DamageInfo? dinfo)
        {
            base.PostAdd(dinfo);
            Register();
        }

        public override void PostRemoved()
        {
            if (pawn != null && Active.TryGetValue(pawn, out var current)
                && ReferenceEquals(current, this))
                Active.Remove(pawn);
            base.PostRemoved();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                Register();
        }
    }
}
