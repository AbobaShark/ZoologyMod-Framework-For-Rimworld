using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    [HarmonyPatch(typeof(JobGiver_AnimalFlee), "TryGiveJob")]
    public static class Patch_SmallPetFleeFromRaiders
    {
        public static bool Prepare()
        {
            var s = ZoologyModSettings.Instance;
            return s == null || (s.EnableIgnoreSmallPetsByRaiders && s.EnableSmallPetFleeFromRaiders);
        }

        public static void Postfix(JobGiver_AnimalFlee __instance, Pawn pawn, ref Job __result)
        {
            
            var settings = ModConstants.Settings;
            if (settings == null || !settings.EnableIgnoreSmallPetsByRaiders || !settings.EnableSmallPetFleeFromRaiders)
                return;

            if (__result != null || !pawn.RaceProps.Animal || pawn.Faction != Faction.OfPlayer)
                return;

            
            bool isSmallPet = pawn.RaceProps.baseBodySize < ModConstants.SmallPetBodySizeThreshold;

            if (!isSmallPet)
                return;

            
            if (pawn.Roamer)
                return;

            const float MaxThreatDist = 18f;

            
            Pawn threat = GenClosest.ClosestThingReachable(
                pawn.Position,
                pawn.Map,
                ThingRequest.ForGroup(ThingRequestGroup.Pawn),
                PathEndMode.OnCell,
                TraverseParms.For(pawn, Danger.Deadly, TraverseMode.ByPawn),
                MaxThreatDist,
                (Thing t) => t is Pawn p && p.RaceProps.Humanlike && p.HostileTo(Faction.OfPlayer) && p != pawn && !p.Downed
            ) as Pawn;

            if (threat != null && FleeUtility.ShouldAnimalFleeDanger(pawn))
            {
                __result = FleeUtility.FleeJob(pawn, threat, 24);
            }
        }
    }
}
