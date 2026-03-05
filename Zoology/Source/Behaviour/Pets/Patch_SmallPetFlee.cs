// Patch_SmallPetFlee.cs

using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    [HarmonyPatch(typeof(JobGiver_AnimalFlee), "TryGiveJob")]
    public static class Patch_SmallPetFleeFromRaiders
    {
        public static void Postfix(JobGiver_AnimalFlee __instance, Pawn pawn, ref Job __result)
        {
            // Если функция отключена, ничего не делаем
            if (!ModConstants.Settings.EnableSmallPetFleeFromRaiders)
                return;

            if (__result != null || !pawn.RaceProps.Animal || pawn.Faction != Faction.OfPlayer)
                return;

            // Определяем small pet: размер < ModConstants.SmallPetBodySizeThreshold, животное колониста
            bool isSmallPet = pawn.RaceProps.baseBodySize < ModConstants.SmallPetBodySizeThreshold;

            if (!isSmallPet)
                return;

            // Проверяем, не Roamer ли (для roamers поведение как сейчас)
            if (pawn.Roamer)
                return;

            const float MaxThreatDist = 18f;

            // Ищем ближайшего humanlike враждебного pawn (рейдера) в пределах дистанции
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