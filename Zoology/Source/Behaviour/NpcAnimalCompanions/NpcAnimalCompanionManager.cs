using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace ZoologyMod
{
    internal enum NpcAnimalCompanionState : byte
    {
        FollowingMaster,
        PanicFlee
    }

    internal sealed class NpcAnimalCompanionLink : IExposable
    {
        public Pawn Animal;
        public Pawn Master;
        public int GroupId;
        public NpcAnimalCompanionState State;
        public int MasterUnavailableSinceTick = -1;

        public void ExposeData()
        {
            Scribe_References.Look(ref Animal, "animal");
            Scribe_References.Look(ref Master, "master");
            Scribe_Values.Look(ref GroupId, "groupId", 0);
            Scribe_Values.Look(ref State, "state", NpcAnimalCompanionState.FollowingMaster);
            Scribe_Values.Look(ref MasterUnavailableSinceTick, "masterUnavailableSinceTick", -1);
        }

    }

    internal sealed class NpcAnimalCompanionGroup : IExposable
    {
        public int Id;
        public Faction Faction;
        public List<Pawn> HumanMembers = new List<Pawn>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id", 0);
            Scribe_References.Look(ref Faction, "faction");
            Scribe_Collections.Look(ref HumanMembers, "humanMembers", LookMode.Reference);
            HumanMembers ??= new List<Pawn>();
        }
    }

    internal sealed class NpcAnimalCompanionManager : GameComponent
    {
        // Drop-pod and shuttle arrivals can spawn members of one generated group at
        // different times. Give the assigned master enough time to enter the map
        // before treating a temporary absence as a permanent loss.
        private const int MasterDespawnGraceTicks = 600;
        internal const int MaxAnimalsPerMaster = 2;

        private List<NpcAnimalCompanionGroup> groups = new List<NpcAnimalCompanionGroup>();
        private List<NpcAnimalCompanionLink> links = new List<NpcAnimalCompanionLink>();
        private int nextGroupId = 1;

        [Unsaved(false)] private readonly Dictionary<Pawn, NpcAnimalCompanionLink> linkByAnimal =
            new Dictionary<Pawn, NpcAnimalCompanionLink>();
        [Unsaved(false)] private readonly Dictionary<Pawn, List<NpcAnimalCompanionLink>> linksByMaster =
            new Dictionary<Pawn, List<NpcAnimalCompanionLink>>();
        [Unsaved(false)] private readonly Dictionary<int, NpcAnimalCompanionGroup> groupById =
            new Dictionary<int, NpcAnimalCompanionGroup>();

        public static NpcAnimalCompanionManager Current => CurrentGame?.GetComponent<NpcAnimalCompanionManager>();

        private static Game CurrentGame => Verse.Current.Game;

        public NpcAnimalCompanionManager(Game game)
        {
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref groups, "npcAnimalCompanionGroups", LookMode.Deep);
            Scribe_Collections.Look(ref links, "npcAnimalCompanionLinks", LookMode.Deep);
            Scribe_Values.Look(ref nextGroupId, "nextNpcAnimalCompanionGroupId", 1);

            groups ??= new List<NpcAnimalCompanionGroup>();
            links ??= new List<NpcAnimalCompanionLink>();

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                RebuildIndexes();
            }
        }

        public bool TryGetLink(Pawn animal, out NpcAnimalCompanionLink link)
        {
            link = null;
            return animal != null && linkByAnimal.TryGetValue(animal, out link);
        }

        public bool IsCompanion(Pawn pawn)
        {
            return pawn != null && linkByAnimal.ContainsKey(pawn);
        }

        public int CountForMaster(Pawn master)
        {
            return master != null && linksByMaster.TryGetValue(master, out List<NpcAnimalCompanionLink> value)
                ? value.Count
                : 0;
        }

        public void RegisterGroup(
            Faction faction,
            List<Pawn> humanMembers,
            List<KeyValuePair<Pawn, Pawn>> animalMasterPairs)
        {
            if (!NpcAnimalCompanionUtility.SystemEnabled
                || faction == null
                || humanMembers == null
                || animalMasterPairs == null
                || animalMasterPairs.Count == 0)
            {
                return;
            }

            NpcAnimalCompanionGroup group = new NpcAnimalCompanionGroup
            {
                Id = nextGroupId++,
                Faction = faction
            };

            for (int i = 0; i < humanMembers.Count; i++)
            {
                Pawn member = humanMembers[i];
                if (member != null && !group.HumanMembers.Contains(member))
                {
                    group.HumanMembers.Add(member);
                }
            }

            groups.Add(group);
            groupById[group.Id] = group;

            for (int i = 0; i < animalMasterPairs.Count; i++)
            {
                Pawn animal = animalMasterPairs[i].Key;
                Pawn master = animalMasterPairs[i].Value;
                if (animal == null || master == null || linkByAnimal.ContainsKey(animal))
                {
                    continue;
                }

                NpcAnimalCompanionLink link = new NpcAnimalCompanionLink
                {
                    Animal = animal,
                    Master = master,
                    GroupId = group.Id,
                    State = NpcAnimalCompanionState.FollowingMaster
                };
                links.Add(link);
                IndexLink(link);
            }

            RemoveGroupIfEmpty(group.Id);
        }

        public void NotifyPawnDowned(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            if (linksByMaster.TryGetValue(pawn, out List<NpcAnimalCompanionLink> mastered))
            {
                List<NpcAnimalCompanionLink> copy = new List<NpcAnimalCompanionLink>(mastered);
                for (int i = 0; i < copy.Count; i++)
                {
                    InterruptForStateChange(copy[i].Animal);
                }
            }

            // The final mobile human need not own an animal. Downing is a rare event,
            // so scan only the compact saved companion groups and their links; never
            // scan map pawns. This also releases animals whose own masters went down
            // earlier while another group member was still fighting.
            PanicGroupsWithoutMobileHuman(pawn);
        }

        public void NotifyPawnKilledOrDestroyed(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            if (linkByAnimal.TryGetValue(pawn, out NpcAnimalCompanionLink animalLink))
            {
                RemoveLink(animalLink);
            }

            if (linksByMaster.TryGetValue(pawn, out List<NpcAnimalCompanionLink> mastered))
            {
                ResolveAllMasteredLinks(mastered);
            }
            PanicGroupsWithoutMobileHuman(pawn);
        }

        public void NotifyPawnDespawned(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            if (linkByAnimal.TryGetValue(pawn, out NpcAnimalCompanionLink animalLink))
            {
                RemoveLink(animalLink);
            }

            if (!linksByMaster.TryGetValue(pawn, out List<NpcAnimalCompanionLink> mastered))
            {
                return;
            }

            int tick = Find.TickManager?.TicksGame ?? 0;
            for (int i = 0; i < mastered.Count; i++)
            {
                mastered[i].MasterUnavailableSinceTick = tick;
                InterruptForStateChange(mastered[i].Animal);
            }
        }

        public void NotifyPawnExitingMap(Pawn pawn)
        {
            if (pawn == null || !linksByMaster.TryGetValue(pawn, out List<NpcAnimalCompanionLink> mastered))
            {
                return;
            }

            // ExitMap is the definitive vanilla lifecycle boundary. Companions are
            // normally already close to this pawn, so panic-flee gives them the same
            // inexpensive edge-exit machinery without keeping the finished group alive.
            List<NpcAnimalCompanionLink> copy = new List<NpcAnimalCompanionLink>(mastered);
            for (int i = 0; i < copy.Count; i++)
            {
                StartPanicFlee(copy[i], "master exited the map");
            }
        }

        public void NotifyPawnFactionChanged(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            bool leftCompanionGroup = false;

            if (linkByAnimal.TryGetValue(pawn, out NpcAnimalCompanionLink animalLink))
            {
                NpcAnimalCompanionGroup group = GetGroup(animalLink.GroupId);
                if (group == null || pawn.Faction != group.Faction)
                {
                    RemoveLink(animalLink);
                    leftCompanionGroup = true;
                }
            }

            if (linksByMaster.TryGetValue(pawn, out List<NpcAnimalCompanionLink> mastered))
            {
                // A pawn can theoretically be referenced by more than one generated
                // group. Resolve only links whose expected faction was actually lost.
                List<NpcAnimalCompanionLink> copy = new List<NpcAnimalCompanionLink>(mastered);
                for (int i = 0; i < copy.Count; i++)
                {
                    NpcAnimalCompanionGroup group = GetGroup(copy[i].GroupId);
                    if (group == null || pawn.Faction != group.Faction)
                    {
                        ResolveMasterLoss(copy[i], "master faction changed");
                        leftCompanionGroup = true;
                    }
                }
            }

            if (leftCompanionGroup)
            {
                PanicGroupsWithoutMobileHuman(pawn);
            }
        }

        public void NotifyLordToilChanged(Lord lord)
        {
            if (lord == null || links.Count == 0)
            {
                return;
            }

            // Lord transitions are rare. Scan only registered companions, never map
            // pawns, and interrupt them immediately when their humans begin leaving.
            for (int i = 0; i < links.Count; i++)
            {
                NpcAnimalCompanionLink link = links[i];
                if (link?.State == NpcAnimalCompanionState.FollowingMaster
                    && link.Master?.GetLord() == lord
                    && MasterIsLeavingGroup(link.Master))
                {
                    StartPanicFlee(link, "master's Lord began leaving the map");
                }
            }
        }

        public bool ValidateOrResolve(NpcAnimalCompanionLink link, bool allowDespawnGrace)
        {
            if (link == null || link.Animal == null || link.Animal.DestroyedOrNull() || link.Animal.Dead)
            {
                if (link != null)
                {
                    RemoveLink(link);
                }
                return false;
            }

            if (link.State == NpcAnimalCompanionState.PanicFlee)
            {
                EnsurePanicFleeMentalState(link);
                return true;
            }

            NpcAnimalCompanionGroup group = GetGroup(link.GroupId);
            Pawn master = link.Master;
            if (group == null || master == null || master.DestroyedOrNull() || master.Dead
                || !NpcAnimalCompanionUtility.IsEligibleMaster(
                    master,
                    link.Animal,
                    group.Faction,
                    requireSpawned: false))
            {
                ResolveMasterLoss(link, "master became invalid");
                return linkByAnimal.ContainsKey(link.Animal);
            }

            if (master.Spawned && link.Animal.Spawned && master.Map == link.Animal.Map)
            {
                link.MasterUnavailableSinceTick = -1;
                if (master.Downed && !GroupHasMobileHumanOnAnimalMap(group, link.Animal))
                {
                    StartPanicFlee(link, "no mobile human group member remained");
                    return linkByAnimal.ContainsKey(link.Animal);
                }
                if (MasterIsLeavingGroup(master))
                {
                    StartPanicFlee(link, "master received an exit-map duty");
                    return linkByAnimal.ContainsKey(link.Animal);
                }
                return true;
            }

            int tick = Find.TickManager?.TicksGame ?? 0;
            if (link.MasterUnavailableSinceTick < 0)
            {
                link.MasterUnavailableSinceTick = tick;
            }

            if (allowDespawnGrace && tick - link.MasterUnavailableSinceTick < MasterDespawnGraceTicks)
            {
                return true;
            }

            ResolveMasterLoss(link, "master remained unavailable after arrival grace");
            return linkByAnimal.ContainsKey(link.Animal);
        }

        public void DetachToOwnerLord(Pawn animal)
        {
            if (!TryGetLink(animal, out NpcAnimalCompanionLink link))
            {
                return;
            }

            Pawn master = link.Master;
            RemoveLink(link);
            if (animal.Spawned && master?.Spawned == true && animal.Map == master.Map)
            {
                Lord lord = master.GetLord();
                if (lord != null && animal.GetLord() == null && lord.CanAddPawn(animal))
                {
                    lord.AddPawn(animal);
                }
            }
        }

        public void DetachAllToOwnerLords()
        {
            // Called only when the user disables the global runtime layer. Work from
            // the end because DetachToOwnerLord removes the current saved link.
            for (int i = links.Count - 1; i >= 0; i--)
            {
                Pawn animal = links[i]?.Animal;
                if (animal != null)
                {
                    DetachToOwnerLord(animal);
                }
            }
        }

        private void ResolveAllMasteredLinks(List<NpcAnimalCompanionLink> mastered)
        {
            // Reassignment mutates linksByMaster, so work on a small local copy. A master
            // is capped at two animals; this never scales with map pawn count.
            List<NpcAnimalCompanionLink> copy = new List<NpcAnimalCompanionLink>(mastered);
            for (int i = 0; i < copy.Count; i++)
            {
                ResolveMasterLoss(copy[i], "master was killed or destroyed");
            }
        }

        private void ResolveMasterLoss(NpcAnimalCompanionLink link, string reason)
        {
            if (link == null || link.Animal == null || !linkByAnimal.ContainsKey(link.Animal))
            {
                return;
            }

            Pawn oldMaster = link.Master;
            Pawn replacement = FindReplacementMaster(link);
            if (replacement != null)
            {
                SetMaster(link, replacement);
                InterruptForStateChange(link.Animal);
                if (Prefs.DevMode)
                {
                    Log.Message($"[Zoology] Reassigned NPC companion {link.Animal} from {oldMaster} to {replacement}: {reason}.");
                }
                return;
            }

            ZoologyModSettings settings = ModConstants.Settings;
            if (settings != null && settings.OrphanedRaidAnimalsBecomeWild)
            {
                if (Prefs.DevMode)
                {
                    Log.Message($"[Zoology] NPC companion {link.Animal} became wild; no eligible replacement for {oldMaster}: {reason}.");
                }
                MakeWild(link);
            }
            else
            {
                if (Prefs.DevMode)
                {
                    Log.Message($"[Zoology] NPC companion {link.Animal} started panic flee; no eligible replacement for {oldMaster}: {reason}.");
                }
                StartPanicFlee(link);
            }
        }

        private Pawn FindReplacementMaster(NpcAnimalCompanionLink link)
        {
            NpcAnimalCompanionGroup group = GetGroup(link.GroupId);
            Pawn animal = link.Animal;
            if (group == null || animal == null || group.HumanMembers == null)
            {
                return null;
            }

            Pawn best = null;
            int bestCount = int.MaxValue;
            int bestSkill = int.MinValue;
            int bestId = int.MaxValue;

            for (int i = 0; i < group.HumanMembers.Count; i++)
            {
                Pawn candidate = group.HumanMembers[i];
                if (candidate?.Downed != false || candidate.InMentalState
                    || !NpcAnimalCompanionUtility.IsEligibleMaster(candidate, animal, group.Faction, requireSpawned: true)
                    || !animal.Spawned || candidate.Map != animal.Map)
                {
                    continue;
                }

                int count = CountForMaster(candidate);
                if (count >= MaxAnimalsPerMaster)
                {
                    continue;
                }

                int skill = candidate.skills.GetSkill(SkillDefOf.Animals).Level;
                int id = candidate.thingIDNumber;
                if (count < bestCount
                    || (count == bestCount && skill > bestSkill)
                    || (count == bestCount && skill == bestSkill && id < bestId))
                {
                    best = candidate;
                    bestCount = count;
                    bestSkill = skill;
                    bestId = id;
                }
            }

            return best;
        }

        private static bool MasterIsLeavingGroup(Pawn master)
        {
            if (master?.MentalStateDef == MentalStateDefOf.PanicFlee)
            {
                return true;
            }

            DutyDef duty = master?.mindState?.duty?.def;
            return duty == DutyDefOf.ExitMapRandom
                || duty == DutyDefOf.ExitMapBest
                || duty == DutyDefOf.ExitMapBestAndDefendSelf
                || duty == DutyDefOf.ExitMapNearDutyTarget;
        }

        private static bool GroupHasMobileHumanOnAnimalMap(
            NpcAnimalCompanionGroup group,
            Pawn animal)
        {
            if (group?.HumanMembers == null || animal?.Map == null)
            {
                return false;
            }

            for (int i = 0; i < group.HumanMembers.Count; i++)
            {
                Pawn human = group.HumanMembers[i];
                if (human != null && !human.DestroyedOrNull() && !human.Dead && !human.Downed
                    && human.Spawned && human.Map == animal.Map && human.Faction == group.Faction)
                {
                    return true;
                }
            }
            return false;
        }

        private void PanicGroupsWithoutMobileHuman(Pawn changedMember)
        {
            if (changedMember == null)
            {
                return;
            }

            for (int i = 0; i < groups.Count; i++)
            {
                NpcAnimalCompanionGroup group = groups[i];
                if (group?.HumanMembers == null || !group.HumanMembers.Contains(changedMember))
                {
                    continue;
                }

                for (int j = 0; j < links.Count; j++)
                {
                    NpcAnimalCompanionLink link = links[j];
                    if (link?.GroupId == group.Id
                        && link.State == NpcAnimalCompanionState.FollowingMaster
                        && !GroupHasMobileHumanOnAnimalMap(group, link.Animal))
                    {
                        StartPanicFlee(link, "no mobile human group member remained");
                    }
                }
            }
        }

        private void SetMaster(NpcAnimalCompanionLink link, Pawn newMaster)
        {
            UnindexMaster(link);
            link.Master = newMaster;
            link.State = NpcAnimalCompanionState.FollowingMaster;
            link.MasterUnavailableSinceTick = -1;
            IndexMaster(link);
        }

        private void StartPanicFlee(NpcAnimalCompanionLink link, string reason = null)
        {
            if (Prefs.DevMode && reason != null)
            {
                Log.Message($"[Zoology] NPC companion {link?.Animal} started panic flee: {reason}.");
            }

            UnindexMaster(link);
            link.Master = null;
            link.State = NpcAnimalCompanionState.PanicFlee;
            link.MasterUnavailableSinceTick = -1;

            Pawn animal = link.Animal;
            if (animal?.mindState != null)
            {
                animal.mindState.enemyTarget = null;
                EnsurePanicFleeMentalState(link);
            }
            InterruptForStateChange(animal);
        }

        private static void EnsurePanicFleeMentalState(NpcAnimalCompanionLink link)
        {
            Pawn animal = link?.Animal;
            MentalStateHandler handler = animal?.mindState?.mentalStateHandler;
            if (handler == null || handler.CurStateDef == MentalStateDefOf.PanicFlee)
            {
                return;
            }

            // Forced mental states bypass MentalStateWorker.StateCanOccur. In
            // particular, PanicFlee has downedCanDo=false, so forcing it from the
            // downed notification creates an invalid state that vanilla reports on
            // the next mental-state tick. Keep the saved companion state while the
            // pawn is downed and enter vanilla PanicFlee only after recovery.
            if (handler.InMentalState || !MentalStateDefOf.PanicFlee.Worker.StateCanOccur(animal))
            {
                return;
            }

            handler.TryStartMentalState(
                MentalStateDefOf.PanicFlee,
                forced: false,
                forceWake: true,
                transitionSilently: true);
        }

        private void MakeWild(NpcAnimalCompanionLink link)
        {
            Pawn animal = link.Animal;
            RemoveLink(link);
            if (animal == null || animal.DestroyedOrNull() || animal.Dead)
            {
                return;
            }

            if (animal.mindState != null)
            {
                animal.mindState.enemyTarget = null;
                if (animal.InMentalState)
                {
                    animal.mindState.mentalStateHandler.Reset();
                }
            }
            if (animal.Spawned && animal.CurJob != null)
            {
                animal.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            }
            if (animal.Faction != null)
            {
                animal.SetFaction(null);
            }
        }

        private void RebuildIndexes()
        {
            linkByAnimal.Clear();
            linksByMaster.Clear();
            groupById.Clear();

            for (int i = groups.Count - 1; i >= 0; i--)
            {
                NpcAnimalCompanionGroup group = groups[i];
                if (group == null || group.Id <= 0 || group.Faction == null)
                {
                    groups.RemoveAt(i);
                    continue;
                }

                group.HumanMembers ??= new List<Pawn>();
                group.HumanMembers.RemoveAll(p => p == null || p.DestroyedOrNull());
                groupById[group.Id] = group;
                if (group.Id >= nextGroupId)
                {
                    nextGroupId = group.Id + 1;
                }
            }

            for (int i = links.Count - 1; i >= 0; i--)
            {
                NpcAnimalCompanionLink link = links[i];
                bool missingGroup = link != null && !groupById.ContainsKey(link.GroupId);
                bool missingFollowingMaster = link?.State == NpcAnimalCompanionState.FollowingMaster
                    && link.Master == null;
                if ((missingGroup || missingFollowingMaster) && link?.Animal != null)
                {
                    Log.WarningOnce(
                        $"[Zoology] NPC animal companion {link.Animal} loaded with an invalid group/master link; it will be cleaned up or resolved.",
                        link.Animal.thingIDNumber ^ 0x5A00106);
                }

                if (link == null || link.Animal == null || link.Animal.DestroyedOrNull()
                    || missingGroup || linkByAnimal.ContainsKey(link.Animal))
                {
                    links.RemoveAt(i);
                    continue;
                }
                IndexLink(link);
            }

            for (int i = groups.Count - 1; i >= 0; i--)
            {
                RemoveGroupIfEmpty(groups[i].Id);
            }
        }

        private void IndexLink(NpcAnimalCompanionLink link)
        {
            linkByAnimal[link.Animal] = link;
            IndexMaster(link);
        }

        private void IndexMaster(NpcAnimalCompanionLink link)
        {
            if (link.Master == null)
            {
                return;
            }
            if (!linksByMaster.TryGetValue(link.Master, out List<NpcAnimalCompanionLink> mastered))
            {
                mastered = new List<NpcAnimalCompanionLink>(2);
                linksByMaster.Add(link.Master, mastered);
            }
            if (!mastered.Contains(link))
            {
                mastered.Add(link);
            }
        }

        private void UnindexMaster(NpcAnimalCompanionLink link)
        {
            if (link.Master == null || !linksByMaster.TryGetValue(link.Master, out List<NpcAnimalCompanionLink> mastered))
            {
                return;
            }
            mastered.Remove(link);
            if (mastered.Count == 0)
            {
                linksByMaster.Remove(link.Master);
            }
        }

        private void RemoveLink(NpcAnimalCompanionLink link)
        {
            if (link == null)
            {
                return;
            }
            UnindexMaster(link);
            if (link.Animal != null)
            {
                linkByAnimal.Remove(link.Animal);
            }
            links.Remove(link);
            RemoveGroupIfEmpty(link.GroupId);
        }

        private NpcAnimalCompanionGroup GetGroup(int groupId)
        {
            groupById.TryGetValue(groupId, out NpcAnimalCompanionGroup group);
            return group;
        }

        private void RemoveGroupIfEmpty(int groupId)
        {
            for (int i = 0; i < links.Count; i++)
            {
                if (links[i] != null && links[i].GroupId == groupId)
                {
                    return;
                }
            }

            if (groupById.TryGetValue(groupId, out NpcAnimalCompanionGroup group))
            {
                groupById.Remove(groupId);
                groups.Remove(group);
            }
        }

        private static void InterruptForStateChange(Pawn pawn)
        {
            if (pawn?.Spawned == true && pawn.CurJob != null)
            {
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
            }
        }
    }
}
