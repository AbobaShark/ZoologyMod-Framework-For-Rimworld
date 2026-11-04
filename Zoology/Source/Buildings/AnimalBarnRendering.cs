#if RIMWORLD17
using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace ZoologyMod
{
    public sealed partial class Building_AnimalBarn
    {
        private const int DisplayTilesX = 3;
        private const int DisplayTilesZ = 6;
        private const float BodySizePerTile = 1.5f;
        // Four visible residents per tile is a rendering safety ceiling,
        // not an admission limit; graphic overlap and body-size budgets
        // usually restrict the actual number much earlier.
        private const int MaximumDisplayedAnimals = DisplayTilesX * DisplayTilesZ * 4;
        // Six side storage bays with three stacks in each bay, as on a
        // vanilla shelf tile. The inventory itself remains authoritative.
        private static readonly Vector2[] FoodDisplayOffsets = CreateFoodDisplayOffsets();
        // Visible interior of the 3 x 6 barn. Only current pawn BodySize,
        // never texture dimensions, determines each visual footprint.
        // Inset the virtual placement area from the inner railings.
        // The inset affects positions, never pawn texture size or scaling.
        private const float DisplayHalfWidth = 1.15f;
        private const float DisplayHalfLength = 2.40f;
        // Reserve some of the next altitude layer for the foreground fence
        // so every node belonging to one animal stays beneath that fence.
        private const float DisplayForegroundDepthReserve = 0.08f;
        private const float DisplayAnimalLayerStride = 0.065f;
        private const float DisplaySpacing = 0.10f;
        private const float DisplayStep = 0.23f;
        private static readonly Vector2[] CandidatePositions = CreateCandidatePositions();

        private readonly Pawn[] displayedAnimals = new Pawn[MaximumDisplayedAnimals];
        private readonly Vector2[] displayedAnimalOffsets = new Vector2[MaximumDisplayedAnimals];
        private readonly Vector2[] displayedAnimalFootprints = new Vector2[MaximumDisplayedAnimals];
        private readonly int[] displayedAnimalDepthRanks = new int[MaximumDisplayedAnimals];
        private readonly float[] displayedAnimalWorldZ = new float[MaximumDisplayedAnimals];
        private readonly float[] displayedTileBodySizes = new float[DisplayTilesX * DisplayTilesZ];
        private readonly Thing[] displayedFood = new Thing[MaximumFoodStacks];
        private readonly List<Pawn> displayCandidates = new List<Pawn>();
        private readonly HashSet<ThingDef> displayedSpecies = new HashSet<ThingDef>();
        private readonly HashSet<float> failedDisplayPlacements = new HashSet<float>();
        private int displayedAnimalCount;
        private int displayedFoodCount;
        private Graphic frontGraphic;
        private Color frontColor;

        private void RefreshDisplayedAnimals()
        {
            Array.Clear(displayedAnimals, 0, displayedAnimals.Length);
            Array.Clear(displayedFood, 0, displayedFood.Length);
            Array.Clear(displayedTileBodySizes, 0, displayedTileBodySizes.Length);
            displayedAnimalCount = displayedFoodCount = 0;
            displayCandidates.Clear();
            displayedSpecies.Clear();
            failedDisplayPlacements.Clear();

            // Run the population scan on the existing 150-tick refresh, not
            // per frame. Render only original contained pawns and food items.
            for (int i = 0; i < contents.Count; i++)
            {
                Thing thing = contents[i];
                if (thing is Pawn pawn)
                {
                    if (!pawn.Dead && !pawn.Destroyed) displayCandidates.Add(pawn);
                }
                else if (!thing.Destroyed && !(thing is Corpse)
                    && thing.def.category == ThingCategory.Item
                    && thing.def.IsNutritionGivingIngestible
                    && thing.TryGetComp<CompHatcher>() == null
                    && displayedFoodCount < displayedFood.Length)
                {
                    displayedFood[displayedFoodCount++] = thing;
                }
            }

            // First show distinct species, then use remaining visual space
            // for other residents. Sort is deterministic across save/load.
            displayCandidates.Sort(CompareDisplayOrder);
            for (int pass = 0; pass < 2 && displayedAnimalCount < displayedAnimals.Length; pass++)
            {
                for (int i = 0; i < displayCandidates.Count
                    && displayedAnimalCount < displayedAnimals.Length; i++)
                {
                    Pawn pawn = displayCandidates[i];
                    if (pass == 0 && !displayedSpecies.Add(pawn.def)) continue;
                    if (pass == 1 && Array.IndexOf(displayedAnimals, pawn) >= 0) continue;
                    TryPlaceDisplayedAnimal(pawn);
                }
            }

            // Positions were selected center-first. Do not recenter after
            // assigning tiles: that would invalidate local body-size budgets.
            // Give whole render trees distinct depths, farthest animals
            // first. Individual nodes within one pawn keep vanilla offsets.
            RebuildDisplayedAnimalDepthRanks();
        }

        private bool TryPlaceDisplayedAnimal(Pawn pawn)
        {
            float sizeNow = pawn.BodySize;
            if (float.IsNaN(sizeNow) || float.IsInfinity(sizeNow)) return false;
            sizeNow = Mathf.Max(0.01f, sizeNow);

            // A virtual footprint is derived solely from current BodySize.
            // It is used for spatial packing, never to resize or clip the
            // pawn's original render tree (including oversized graphics).
            Vector2 footprint = BodySizeFootprint(sizeNow);
            // Placement only adds occupants during a refresh. If this
            // BodySize cannot fit now, it cannot fit later in the same pass.
            if (failedDisplayPlacements.Contains(sizeNow)) return false;

            for (int n = 0; n < CandidatePositions.Length; n++)
            {
                Vector2 center = CandidatePositions[n];
                if (Mathf.Abs(center.x) + footprint.x * .5f > DisplayHalfWidth
                    || Mathf.Abs(center.y) + footprint.y * .5f > DisplayHalfLength)
                    continue;

                bool overlaps = false;
                for (int i = 0; i < displayedAnimalCount; i++)
                {
                    Vector2 p = displayedAnimalOffsets[i];
                    Vector2 s = displayedAnimalFootprints[i];
                    if (Mathf.Abs(center.x - p.x) < (footprint.x + s.x) * .5f + DisplaySpacing
                        && Mathf.Abs(center.y - p.y) < (footprint.y + s.y) * .5f + DisplaySpacing)
                    {
                        overlaps = true;
                        break;
                    }
                }

                if (overlaps || !ApplyDisplayTileBudget(center, footprint, sizeNow, false))
                    continue;

                ApplyDisplayTileBudget(center, footprint, sizeNow, true);
                AddDisplayedAnimal(pawn, center, footprint);
                return true;
            }
            failedDisplayPlacements.Add(sizeNow);
            return false;
        }

        // The virtual footprint area is proportional to current BodySize.
        // It is independent of graphic drawSize, texture padding and age-
        // dependent sprite art. Very large animals extend lengthwise within
        // the 3 x 6 interior instead of being rejected for their width.
        private static Vector2 BodySizeFootprint(float bodySize)
        {
            float tileArea = (DisplayHalfWidth * 2f / DisplayTilesX)
                * (DisplayHalfLength * 2f / DisplayTilesZ);
            float area = bodySize / BodySizePerTile * tileArea;
            float width = Mathf.Min(DisplayHalfWidth * 2f, Mathf.Sqrt(area));
            return new Vector2(width, area / width);
        }

        // Distribute current BodySize across the virtual footprint.
        // No tile can hold more than 1.5 body-size units. Graphic dimensions
        // never determine which pawns can be displayed.
        private bool ApplyDisplayTileBudget(
            Vector2 center, Vector2 size, float bodySize, bool commit)
        {
            float tileWidth = DisplayHalfWidth * 2f / DisplayTilesX;
            float tileLength = DisplayHalfLength * 2f / DisplayTilesZ;
            float left = center.x - size.x * .5f;
            float right = center.x + size.x * .5f;
            float bottom = center.y - size.y * .5f;
            float top = center.y + size.y * .5f;
            float area = size.x * size.y;

            for (int z = 0; z < DisplayTilesZ; z++)
            {
                float zMin = -DisplayHalfLength + z * tileLength;
                float overlapZ = Mathf.Min(top, zMin + tileLength)
                    - Mathf.Max(bottom, zMin);
                if (overlapZ <= 0f) continue;

                for (int x = 0; x < DisplayTilesX; x++)
                {
                    float xMin = -DisplayHalfWidth + x * tileWidth;
                    float overlapX = Mathf.Min(right, xMin + tileWidth)
                        - Mathf.Max(left, xMin);
                    if (overlapX <= 0f) continue;

                    int index = z * DisplayTilesX + x;
                    float additional = bodySize * overlapX * overlapZ / area;
                    if (!commit && displayedTileBodySizes[index] + additional
                            > BodySizePerTile + 0.0001f)
                        return false;
                    if (commit) displayedTileBodySizes[index] += additional;
                }
            }
            return true;
        }

        private static Vector2[] CreateCandidatePositions()
        {
            var positions = new List<Vector2>();
            // A fixed small grid, ranked from the center outwards. It is
            // created once, not for every animal or frame.
            for (int z = -11; z <= 11; z++)
                for (int x = -6; x <= 6; x++)
                    positions.Add(new Vector2(x * DisplayStep, z * DisplayStep));
            positions.Sort((a, b) =>
            {
                int result = a.sqrMagnitude.CompareTo(b.sqrMagnitude);
                if (result != 0) return result;
                result = a.y.CompareTo(b.y);
                return result != 0 ? result : a.x.CompareTo(b.x);
            });
            return positions.ToArray();
        }

        private void AddDisplayedAnimal(Pawn pawn, Vector2 offset,
            Vector2 footprint)
        {
            int index = displayedAnimalCount++;
            displayedAnimals[index] = pawn;
            displayedAnimalOffsets[index] = offset;
            displayedAnimalFootprints[index] = footprint;
        }

        private void RebuildDisplayedAnimalDepthRanks()
        {
            Quaternion rotation = Rotation.AsQuat;
            for (int i = 0; i < displayedAnimalCount; i++)
            {
                Vector2 local = displayedAnimalOffsets[i];
                displayedAnimalWorldZ[i] =
                    (rotation * new Vector3(local.x, 0f, local.y)).z;
            }

            for (int i = 0; i < displayedAnimalCount; i++)
            {
                int rank = 0;
                float z = displayedAnimalWorldZ[i];
                for (int j = 0; j < displayedAnimalCount; j++)
                {
                    // Higher world Z is farther from the camera and draws
                    // below animals closer to it. Ties remain deterministic.
                    if (displayedAnimalWorldZ[j] > z
                        || displayedAnimalWorldZ[j] == z && j < i)
                        rank++;
                }
                displayedAnimalDepthRanks[i] = rank;
            }
        }

        private float DisplayedAnimalAltitude(int index)
        {
            float bottom = AltitudeLayer.BuildingBelowTop.AltitudeFor() + 0.005f;
            float top = AltitudeLayer.BuildingOnTop.AltitudeFor()
                - DisplayForegroundDepthReserve;
            float stride = Mathf.Min(DisplayAnimalLayerStride,
                Mathf.Max(0f, top - bottom) / Mathf.Max(1, displayedAnimalCount - 1));
            return bottom + displayedAnimalDepthRanks[index] * stride;
        }

        private static Vector2[] CreateFoodDisplayOffsets()
        {
            var result = new Vector2[MaximumFoodStacks];
            for (int i = 0; i < result.Length; i++)
            {
                // Three separate stacks per bay, the familiar triangular
                // shelf arrangement. Six bays run down the two outer sides.
                int bay = i / 3;
                int slot = i % 3;
                float sideX = bay % 2 == 0 ? -1.08f : 1.08f;
                float rowZ = (bay / 2 - 1) * 1.8f;
                float x = slot == 0 ? -0.20f : slot == 1 ? 0.20f : 0f;
                float z = slot == 2 ? 0.20f : -0.20f;
                result[i] = new Vector2(sideX + x, rowZ + z);
            }
            return result;
        }

        private static uint DisplayKey(Pawn pawn)
        {
            unchecked
            {
                uint key = (uint)Gen.HashCombineInt(pawn.def.shortHash, pawn.thingIDNumber);
                key = (key ^ (key >> 16)) * 0x7feb352d;
                key = (key ^ (key >> 15)) * 0x846ca68b;
                return key ^ (key >> 16);
            }
        }

        private static int CompareDisplayOrder(Pawn a, Pawn b)
        {
            int result = b.BodySize.CompareTo(a.BodySize);
            if (result != 0) return result;
            uint ah = DisplayKey(a), bh = DisplayKey(b);
            result = ah.CompareTo(bh);
            if (result != 0) return result;
            result = string.CompareOrdinal(a.def.defName, b.def.defName);
            return result != 0 ? result : a.thingIDNumber.CompareTo(b.thingIDNumber);
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            // The static frame is printed into the map mesh, alongside
            // ordinary columns and walls. Only contents and the foreground
            // remain in the realtime pass.
            base.DrawAt(drawLoc, flip);
            Quaternion rotation = Rotation.AsQuat;

            // Render every real stored stack in its own side-shelf slot.
            // Positions are calculated once; drawing never scans the owner,
            // creates imitation stacks or performs additional simulation.
            for (int i = 0; i < displayedFoodCount; i++)
            {
                Thing food = displayedFood[i];
                if (food == null || food.Destroyed || food.ParentHolder != this) continue;
                Vector2 local = FoodDisplayOffsets[i];
                Vector3 position = drawLoc + rotation * new Vector3(local.x, 0f, local.y);
                position.y = AltitudeLayer.BuildingBelowTop.AltitudeFor() - .02f
                    + i % 3 * .001f;
                // Local shelf positions follow the barn rotation, but item
                // graphics must remain world-upright, like stockpile items.
                // Using the barn's Rotation flipped single-texture food.
                food.Graphic.Draw(position, Rot4.North, food);
            }

            // Do not hide otherwise valid residents at distant zoom. Their
            // vanilla render trees and the fixed 72-slot cache bound cost.
            for (int i = 0; i < displayedAnimalCount; i++)
            {
                Pawn pawn = displayedAnimals[i];
                if (pawn == null || pawn.Dead || pawn.Destroyed || pawn.ParentHolder != this) continue;
                PawnRenderer renderer = pawn.Drawer.renderer;
                renderer.EnsureGraphicsInitialized();
                Vector2 local = displayedAnimalOffsets[i];
                Vector3 position = drawLoc + rotation * new Vector3(local.x, 0f, local.y);
                // A whole pawn occupies one depth band. Without this,
                // its head/body nodes can interleave with another pawn's
                // nodes, leaving fragments of a hidden animal on top.
                position.y = DisplayedAnimalAltitude(i);
                var parms = new PawnDrawParms
                {
                    pawn = pawn,
                    matrix = Matrix4x4.TRS(
                        position + pawn.ageTracker.CurLifeStage.bodyDrawOffset,
                        Quaternion.identity, Vector3.one),
                    facing = new Rot4((Rotation.AsInt + (i % 2 == 0 ? 1 : 3)) % 4),
                    rotDrawMode = renderer.CurRotDrawMode,
                    posture = pawn.GetPosture(),
                    flags = PawnRenderFlags.Clothes | PawnRenderFlags.Headgear | PawnRenderFlags.NeverAimWeapon
                        | (pawn.IsPsychologicallyInvisible() ? PawnRenderFlags.Invisible : PawnRenderFlags.None)
                        | (!pawn.health.hediffSet.HasHead ? PawnRenderFlags.HeadStump : PawnRenderFlags.None),
                    tint = renderer.flasher.CurColor.ToTransparent(InvisibilityUtility.GetAlpha(pawn)),
                    coveredInFoam = renderer.FirefoamOverlays.coveredInFoam,
                    dead = false
                };
                renderer.renderTree.ParallelPreDraw(parms);
                renderer.renderTree.Draw(parms);
            }
            if (frontGraphic == null || frontColor != DrawColor)
            {
                frontColor = DrawColor;
                frontGraphic = GraphicDatabase.Get<Graphic_Single>("Things/Building/Zoology/AnimalBarnFront",
                    ShaderDatabase.Cutout, def.graphicData.drawSize, frontColor);
            }
            Vector3 frontPos = drawLoc;
            // Only one building layer above the animals, not the global
            // PawnState overlay. The latter obscured columns in front.
            frontPos.y = AltitudeLayer.BuildingOnTop.AltitudeFor();
            frontGraphic.Draw(frontPos, Rotation, this);
        }
    }
}
#endif
