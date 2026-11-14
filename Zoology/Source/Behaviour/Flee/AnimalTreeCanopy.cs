using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    // A tree's graphic is a single transparent quad. Measure the widest
    // foliage band once per texture, not per tree, pawn or rendered frame.
    internal static class AnimalTreeCanopy
    {
        private struct Profile
        {
            internal float centerX;
            internal float height;
            internal float width;
        }

        private static readonly Dictionary<Texture, Profile> Profiles =
            new Dictionary<Texture, Profile>();

        private static Profile DefaultProfile => new Profile
        {
            centerX = 0f,
            height = 0.73f,
            width = 0.70f
        };

        private static Profile GetProfile(Plant tree)
        {
            try
            {
                Texture texture = tree.Graphic?.MatAt(Rot4.North, tree)?.mainTexture;
                if (texture == null)
                    return DefaultProfile;
                if (Profiles.TryGetValue(texture, out Profile cached))
                    return cached;

                Profile profile = DefaultProfile;
                if (texture is Texture2D image && image.isReadable)
                    profile = Measure(image.GetPixels32(), image.width, image.height);
                // GPU readback on the game thread would cause a noticeable
                // hitch; fall back to the tree's declared size instead.
                Profiles[texture] = profile;
                return profile;
            }
            catch
            {
                return DefaultProfile;
            }
        }

        private static Profile Measure(Color32[] pixels, int width, int height)
        {
            if (pixels == null || width < 1 || height < 1
                || pixels.Length < (long)width * height)
                return DefaultProfile;

            const byte threshold = 12;
            int lowest = height;
            int highest = -1;
            for (int y = 0; y < height; y++)
            {
                int start = y * width;
                for (int x = 0; x < width; x++)
                    if (pixels[start + x].a >= threshold)
                    {
                        if (y < lowest) lowest = y;
                        if (y > highest) highest = y;
                    }
            }
            if (highest <= lowest)
                return DefaultProfile;

            // Only the upper half of the visible silhouette is considered.
            // The bottom of the texture is usually the trunk.
            int bottom = lowest + (int)((highest - lowest) * 0.50f);
            int top = lowest + (int)((highest - lowest) * 0.94f);
            int bestCount = 0, bestLeft = 0, bestRight = width - 1, bestY = top;
            for (int y = bottom; y <= top; y++)
            {
                int left = width, right = -1, count = 0;
                int row = y * width;
                for (int x = 0; x < width; x++)
                    if (pixels[row + x].a >= threshold)
                    {
                        count++;
                        if (x < left) left = x;
                        if (x > right) right = x;
                    }
                if (count > bestCount)
                {
                    bestCount = count;
                    bestLeft = left;
                    bestRight = right;
                    bestY = y;
                }
            }
            if (bestCount <= 0)
                return DefaultProfile;

            return new Profile
            {
                centerX = ((bestLeft + bestRight + 1f) * 0.5f / width) - 0.5f,
                height = (bestY + 0.5f) / height,
                width = Mathf.Clamp((float)(bestRight - bestLeft + 1) / width, 0.15f, 1f)
            };
        }

        private static float VisualScale(Plant tree)
        {
            var growthRange = tree.def.plant.visualSizeRange;
            float value = Mathf.Lerp(growthRange.min, growthRange.max, tree.Growth);
            return Mathf.Max(0.1f, value);
        }

        internal static float Width(Plant tree)
        {
            if (tree?.def?.graphicData == null)
                return 0f;
            return tree.def.graphicData.drawSize.x
                * VisualScale(tree) * GetProfile(tree).width;
        }

        internal static Vector3 DrawOffset(Plant tree, int slot, float bodySize)
        {
            if (tree?.def?.graphicData == null)
                return Vector3.zero;

            Profile profile = GetProfile(tree);
            float scale = VisualScale(tree);
            float width = Mathf.Max(0.3f, tree.def.graphicData.drawSize.x * scale * profile.width);
            float height = Mathf.Max(0.5f, tree.def.graphicData.drawSize.y * scale);
            // Alternate branch sides. The first two occupants get opposite
            // halves of the canopy instead of occupying the trunk axis.
            float branch = slot == 0 ? -0.375f : slot == 1 ? 0.375f
                : slot == 2 ? -0.125f : 0.125f;
            float lateral = (tree.thingIDNumber & 1) == 0 ? branch : -branch;

            float x = profile.centerX * tree.def.graphicData.drawSize.x * scale
                + lateral * width;
            // Whole render trees have their own depth band. A 0.075 step
            // keeps body/head/limb nodes of two pawns from interleaving.
            float depth = 0.14f + slot * 0.075f;
            // Plant graphics are bottom-anchored when maxMeshCount == 1;
            // the fallback still uses this convention.
            float z = Mathf.Clamp(height * profile.height
                + Mathf.Min(0.4f, Mathf.Sqrt(Mathf.Max(0f, bodySize)) * 0.12f),
                0.5f, 6f);
            return new Vector3(x, depth, z);
        }
    }

    // A single allocator serves animals at height, completed nests and
    // nest construction claims. Every entry uses the same four branch slots
    // and the same width/area capacity calculation.
    public partial class AnimalRefugeMapComponent
    {
        private const int MaxTreeRefugeOccupants = 4;
        private const int PendingTreeClaimTicks = 90;

        private sealed class TreeClaim
        {
            internal Pawn pawn;
            internal Building_AnimalNursery nursery;
            internal IntVec3 buildCell = IntVec3.Invalid;
            internal int slot;
            internal int claimedAt;
        }

        private sealed class NurserySiteClaim
        {
            internal Pawn builder;
            internal Plant tree;
            internal int slot;
            internal int claimedAt;
        }

        private readonly Dictionary<Plant, List<TreeClaim>> treeClaims =
            new Dictionary<Plant, List<TreeClaim>>();
        private readonly Dictionary<IntVec3, NurserySiteClaim> nurserySites =
            new Dictionary<IntVec3, NurserySiteClaim>();
        private static readonly List<TreeClaim> NoClaims = new List<TreeClaim>(0);

        private static bool IsTreeRefugeJob(Pawn pawn, Plant tree)
        {
            if (pawn?.CurJob?.targetA.Thing != tree)
                return false;
            string name = pawn.CurJob.def?.defName;
            return name == "Zoology_TreeRefuge" || name == "Zoology_SleepInTree";
        }

        private static bool IsNestBuildJob(Pawn pawn, Plant tree, IntVec3 cell)
        {
            return pawn?.CurJob?.def?.defName == "Zoology_BuildAnimalNursery"
                && pawn.CurJob.targetA.Cell == cell
                && pawn.CurJob.targetB.Thing == tree;
        }

        private static float NestFootprint()
        {
            ThingDef def = Building_AnimalNursery.NurseryDef(OffspringShelterMode.TREE_NEST_ALTRICIAL);
            Vector2 draw = def?.graphicData?.drawSize ?? new Vector2(1f, 1f);
            return Mathf.Max(0.01f, draw.x * draw.y);
        }

        private static float ClaimFootprint(TreeClaim claim)
        {
            if (claim.nursery != null)
                return Mathf.Max(0.01f, claim.nursery.CanopyFootprint);
            if (claim.buildCell.IsValid)
                return NestFootprint();
            return Mathf.Max(0.01f, claim.pawn?.BodySize ?? 0.01f);
        }

        private static bool SlotOccupied(List<TreeClaim> claims, int slot)
        {
            for (int i = 0; i < claims.Count; i++)
                if (claims[i].slot == slot) return true;
            return false;
        }

        private static int AvailableSlot(List<TreeClaim> claims)
        {
            for (int slot = 0; slot < MaxTreeRefugeOccupants; slot++)
                if (!SlotOccupied(claims, slot)) return slot;
            return -1;
        }

        private List<TreeClaim> GetCurrentTreeClaims(Plant tree)
        {
            treeClaims.TryGetValue(tree, out List<TreeClaim> claims);
            int now = GenTicks.TicksGame;
            if (claims != null)
            {
                for (int i = claims.Count - 1; i >= 0; i--)
                {
                    TreeClaim claim = claims[i];
                    if (claim.nursery != null)
                    {
                        if (claim.nursery.Spawned && claim.nursery.AnchorTree == tree)
                            continue;
                    }
                    else
                    {
                        Pawn p = claim.pawn;
                        if (p?.Spawned == true && !p.Dead && !p.Destroyed && p.Map == map
                            && (claim.buildCell.IsValid
                                ? IsNestBuildJob(p, tree, claim.buildCell)
                                    || now - claim.claimedAt <= PendingTreeClaimTicks
                                : IsTreeRefugeJob(p, tree)
                                    || now - claim.claimedAt <= PendingTreeClaimTicks))
                            continue;
                    }
                    claims.RemoveAt(i);
                }
                if (claims.Count == 0)
                {
                    treeClaims.Remove(tree);
                    claims = null;
                }
            }

            // Rebuild real occupants on load from indexed nearby buildings and
            // from the tree's own cell, without map-wide searches.
            List<Thing> atTree = tree.Position.GetThingList(map);
            for (int i = 0; i < atTree.Count; i++)
            {
                if (atTree[i] is not Pawn occupant || !IsTreeRefugeJob(occupant, tree))
                    continue;
                bool known = false;
                if (claims != null)
                    for (int j = 0; j < claims.Count; j++)
                        if (claims[j].pawn == occupant) { known = true; break; }
                if (known) continue;
                if (claims == null)
                {
                    claims = new List<TreeClaim>(MaxTreeRefugeOccupants);
                    treeClaims.Add(tree, claims);
                }
                int slot = AvailableSlot(claims);
                if (slot >= 0)
                    claims.Add(new TreeClaim { pawn = occupant, slot = slot, claimedAt = now });
            }

            foreach (Thing thing in Nearby(tree.Position, 2))
            {
                if (thing is not Building_AnimalNursery nest
                    || !nest.TreeNest || nest.AnchorTree != tree)
                    continue;
                bool known = false;
                if (claims != null)
                    for (int j = 0; j < claims.Count; j++)
                        if (claims[j].nursery == nest) { known = true; break; }
                if (known) continue;
                if (claims == null)
                {
                    claims = new List<TreeClaim>(MaxTreeRefugeOccupants);
                    treeClaims.Add(tree, claims);
                }
                int preferred = nest.CanopySlot;
                int slot = preferred >= 0 && preferred < MaxTreeRefugeOccupants
                    && !SlotOccupied(claims, preferred)
                    ? preferred : AvailableSlot(claims);
                if (slot >= 0)
                {
                    nest.SetCanopySlot(slot);
                    claims.Add(new TreeClaim { nursery = nest, slot = slot, claimedAt = now });
                }
            }
            return claims ?? NoClaims;
        }

        // Queried only while the tree is selected. Reuse the existing
        // per-tree claims instead of adding a component to every tree or
        // scanning all map pawns. Claims are reconstructed from occupants
        // and nearby nests on save load by GetCurrentTreeClaims.
        internal IEnumerable<Thing> SelectableTreeOccupants(Plant tree)
        {
            if (tree?.Spawned != true || tree.Map != map)
                yield break;

            List<TreeClaim> claims = GetCurrentTreeClaims(tree);
            for (int i = 0; i < claims.Count; i++)
            {
                TreeClaim claim = claims[i];
                Building_AnimalNursery nest = claim.nursery;
                if (nest != null)
                {
                    if (nest.Spawned && nest.Map == map
                        && nest.TreeNest && nest.AnchorTree == tree)
                        yield return nest;
                    continue;
                }

                Pawn occupant = claim.pawn;
                if (occupant?.Spawned == true && !occupant.Dead
                    && !occupant.Destroyed && occupant.Map == map
                    && occupant.jobs?.curDriver is JobDriver_AnimalTreeRefuge driver
                    && driver.AtHeight && occupant.CurJob?.targetA.Thing == tree)
                    yield return occupant;
            }
        }

        internal bool TreeHasRoom(Plant tree, Pawn candidate)
            => CheckTreeRoom(tree, candidate, false, out _);

        internal bool TreeHasRoomForNest(Plant tree)
            => CheckTreeRoom(tree, null, true, out _);

        private bool CheckTreeRoom(Plant tree, Pawn candidate, bool nest, out int slot)
        {
            slot = -1;
            if (tree?.Spawned != true || tree.Map != map
                || (!nest && (candidate?.Spawned != true || candidate.Map != map)))
                return false;
            float width = AnimalTreeCanopy.Width(tree);
            if (width <= 0f) return false;
            List<TreeClaim> claims = GetCurrentTreeClaims(tree);
            // A completed tree nest is now a building on the anchor cell.
            // Keep at most one nest per tree so another build never tries to
            // overlap an existing edifice. Animal perches may share slots.
            if (nest)
                for (int i = 0; i < claims.Count; i++)
                    if (claims[i].nursery != null || claims[i].buildCell.IsValid)
                        return false;
            if (candidate != null)
                for (int i = 0; i < claims.Count; i++)
                    if (claims[i].pawn == candidate)
                    {
                        slot = claims[i].slot;
                        return true;
                    }
            slot = AvailableSlot(claims);
            if (slot < 0) return false;
            float usedArea = 0f, usedWidth = 0f;
            for (int i = 0; i < claims.Count; i++)
            {
                float area = ClaimFootprint(claims[i]);
                usedArea += area;
                usedWidth += Mathf.Sqrt(area);
            }
            float nextArea = nest ? NestFootprint()
                : Mathf.Max(0.01f, candidate.BodySize);
            return usedArea + nextArea <= width * width
                && usedWidth + Mathf.Sqrt(nextArea) <= width;
        }

        internal bool TryClaimTree(Plant tree, Pawn pawn, out int slot)
        {
            if (!CheckTreeRoom(tree, pawn, false, out slot))
                return false;
            List<TreeClaim> claims = GetCurrentTreeClaims(tree);
            for (int i = 0; i < claims.Count; i++)
                if (claims[i].pawn == pawn) return true;
            if (ReferenceEquals(claims, NoClaims))
            {
                claims = new List<TreeClaim>(MaxTreeRefugeOccupants);
                treeClaims.Add(tree, claims);
            }
            claims.Add(new TreeClaim { pawn = pawn, slot = slot,
                claimedAt = GenTicks.TicksGame });
            return true;
        }

        private bool SiteClaimValid(IntVec3 cell, NurserySiteClaim claim)
        {
            Pawn p = claim.builder;
            return p?.Spawned == true && !p.Dead && !p.Destroyed && p.Map == map
                && (IsNestBuildJob(p, claim.tree, cell)
                    || GenTicks.TicksGame - claim.claimedAt <= PendingTreeClaimTicks);
        }

        internal bool NurserySiteAvailable(IntVec3 cell, Pawn builder, Plant tree = null)
        {
            if (builder?.Spawned != true || builder.Map != map
                || !cell.IsValid || !cell.InBounds(map)
                || cell.GetEdifice(map) != null || !builder.CanReserve(cell))
                return false;

            if (tree != null)
            {
                // A tree's own cell may be PassThroughOnly and therefore not
                // Standable. This must not prohibit a nest in its canopy:
                // the bird builds and lays while standing beside the trunk.
                if (!tree.Spawned || tree.Map != map || tree.Position != cell
                    || !tree.def.plant.IsTree || tree.IsBurning()
                    || !builder.CanReach(tree, PathEndMode.Touch, Danger.Some))
                    return false;
            }
            else if (!cell.Standable(map))
                return false;

            List<Thing> things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
                if (things[i] is Building_AnimalNursery) return false;
            if (nurserySites.TryGetValue(cell, out NurserySiteClaim claim))
            {
                if (!SiteClaimValid(cell, claim))
                {
                    nurserySites.Remove(cell);
                    if (claim.tree != null) ReleaseTreeClaim(claim.tree, claim.builder);
                }
                else if (claim.builder != builder) return false;
            }
            return true;
        }

        internal bool TryClaimNurserySite(Plant tree, Pawn builder, IntVec3 cell, out int slot)
        {
            slot = -1;
            if (builder?.Spawned != true || builder.Map != map
                || !NurserySiteAvailable(cell, builder, tree))
                return false;
            if (nurserySites.TryGetValue(cell, out NurserySiteClaim current)
                && current.builder == builder)
            {
                slot = current.slot;
                return true;
            }
            if (tree != null)
            {
                if (!CheckTreeRoom(tree, builder, true, out slot)) return false;
                List<TreeClaim> claims = GetCurrentTreeClaims(tree);
                if (ReferenceEquals(claims, NoClaims))
                {
                    claims = new List<TreeClaim>(MaxTreeRefugeOccupants);
                    treeClaims.Add(tree, claims);
                }
                claims.Add(new TreeClaim { pawn = builder, buildCell = cell,
                    slot = slot, claimedAt = GenTicks.TicksGame });
            }
            nurserySites.Add(cell, new NurserySiteClaim { builder = builder,
                tree = tree, slot = slot, claimedAt = GenTicks.TicksGame });
            return true;
        }

        internal void ReleaseNurserySite(IntVec3 cell, Pawn builder)
        {
            if (nurserySites.TryGetValue(cell, out NurserySiteClaim claim)
                && claim.builder == builder)
            {
                nurserySites.Remove(cell);
                if (claim.tree != null) ReleaseTreeClaim(claim.tree, builder);
            }
        }

        internal void ReleaseTreeClaim(Plant tree, Pawn pawn)
        {
            if (tree == null || pawn == null
                || !treeClaims.TryGetValue(tree, out List<TreeClaim> claims))
                return;
            for (int i = claims.Count - 1; i >= 0; i--)
                if (claims[i].pawn == pawn) claims.RemoveAt(i);
            if (claims.Count == 0) treeClaims.Remove(tree);
        }

        internal void ForgetTreeClaims(Plant tree)
        {
            if (tree != null) treeClaims.Remove(tree);
        }

        internal void ClearTreeClaims()
        {
            treeClaims.Clear();
            nurserySites.Clear();
        }
    }
}
