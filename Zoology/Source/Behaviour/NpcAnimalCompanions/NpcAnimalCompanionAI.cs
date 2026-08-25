using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    /// <summary>
    /// Adapts Zoology's saved NPC master link to the same small set of values which
    /// vanilla's trained-animal ThinkTree reads from Pawn_PlayerSettings. We cannot
    /// use Pawn_PlayerSettings.Master directly: RespectedMaster deliberately ignores
    /// masters for non-player factions in RimWorld 1.6.
    /// </summary>
    internal static class NpcAnimalCompanionVanillaAdapter
    {
        public static bool TryGetFollowingLink(Pawn pawn, out NpcAnimalCompanionLink link)
        {
            link = null;
            return NpcAnimalCompanionUtility.SystemEnabled
                && NpcAnimalCompanionManager.Current?.TryGetLink(pawn, out link) == true
                && link.State == NpcAnimalCompanionState.FollowingMaster
                && link.Master != null;
        }

        public static bool ShouldFollowMaster(Pawn animal)
        {
            if (!animal.Spawned || !TryGetFollowingLink(animal, out NpcAnimalCompanionLink link))
            {
                return false;
            }

            Pawn master = link.Master;
            if (master.DestroyedOrNull() || master.Dead)
            {
                return false;
            }

            if (master.Spawned)
            {
                return master.Map == animal.Map
                    && animal.CanReach(master, PathEndMode.OnCell, Danger.Deadly);
            }

            Pawn carriedBy = master.CarriedBy;
            return carriedBy != null
                && carriedBy.HostileTo(master)
                && animal.CanReach(carriedBy, PathEndMode.OnCell, Danger.Deadly);
        }

        public static bool IsReleased(Pawn animal)
        {
            if (!TryGetFollowingLink(animal, out NpcAnimalCompanionLink link))
            {
                return false;
            }

            Pawn master = link.Master;
            // NPCs have no player-controlled Release toggle. Treat an actively
            // fighting master as the vanilla toggle being on. Target selection is
            // still entirely JobGiver_AIDefendPawn/AttackTargetFinder's responsibility.
            return master.Spawned
                && !master.Downed
                && !master.InMentalState
                && master.IsFighting();
        }
    }

    public sealed class ThinkNode_ConditionalNpcAnimalCompanion : ThinkNode_Conditional
    {
        protected override bool Satisfied(Pawn pawn)
        {
            NpcAnimalCompanionManager manager = NpcAnimalCompanionManager.Current;
            if (manager == null || !manager.TryGetLink(pawn, out NpcAnimalCompanionLink link))
            {
                return false;
            }

            if (!NpcAnimalCompanionUtility.SystemEnabled)
            {
                // The XML insertion remains loaded when the global Harmony switch is
                // changed at runtime. Detach once and restore the owner's ordinary Lord.
                manager.DetachToOwnerLord(pawn);
                return false;
            }

            return manager.ValidateOrResolve(link, allowDespawnGrace: true);
        }
    }

    public sealed class ThinkNode_ConditionalNpcAnimalPanicFlee : ThinkNode_Conditional
    {
        protected override bool Satisfied(Pawn pawn)
        {
            return NpcAnimalCompanionManager.Current?.TryGetLink(pawn, out NpcAnimalCompanionLink link) == true
                && link.State == NpcAnimalCompanionState.PanicFlee;
        }
    }

    public sealed class JobGiver_NpcAnimalDefendMaster : JobGiver_AIDefendPawn
    {
        private const float GuardRadius = 5f;
        private const float ReleasedRadius = 50f;

        protected override Pawn GetDefendee(Pawn pawn)
        {
            return NpcAnimalCompanionVanillaAdapter.TryGetFollowingLink(
                pawn,
                out NpcAnimalCompanionLink link)
                ? link.Master
                : null;
        }

        protected override float GetFlagRadius(Pawn pawn)
        {
            return NpcAnimalCompanionVanillaAdapter.IsReleased(pawn)
                ? ReleasedRadius
                : GuardRadius;
        }
    }

    public sealed class JobGiver_NpcAnimalFollowMaster : JobGiver_AIFollowPawn
    {
        protected override int FollowJobExpireInterval => 200;

        protected override Pawn GetFollowee(Pawn pawn)
        {
            return NpcAnimalCompanionManager.Current?.TryGetLink(pawn, out NpcAnimalCompanionLink link) == true
                && link.State == NpcAnimalCompanionState.FollowingMaster
                ? link.Master
                : null;
        }

        protected override float GetRadius(Pawn pawn)
        {
            return NpcAnimalCompanionVanillaAdapter.IsReleased(pawn) ? 50f : 3f;
        }
    }

    public sealed class JobGiver_NpcAnimalWanderNearMaster : JobGiver_Wander
    {
        public JobGiver_NpcAnimalWanderNearMaster()
        {
            wanderRadius = 3f;
            ticksBetweenWandersRange = new IntRange(125, 200);
            wanderDestValidator = (pawn, cell, root) =>
                NpcAnimalCompanionVanillaAdapter.IsReleased(pawn)
                || root.GetRoom(pawn.Map) == null
                || WanderRoomUtility.IsValidWanderDest(pawn, cell, root);
        }

        protected override IntVec3 GetWanderRoot(Pawn pawn)
        {
            if (NpcAnimalCompanionManager.Current?.TryGetLink(pawn, out NpcAnimalCompanionLink link) != true
                || link.State != NpcAnimalCompanionState.FollowingMaster
                || link.Master == null
                || !link.Master.Spawned
                || link.Master.Map != pawn.Map)
            {
                return IntVec3.Invalid;
            }

            return WanderUtility.BestCloseWanderRoot(link.Master.PositionHeld, pawn);
        }
    }

}
