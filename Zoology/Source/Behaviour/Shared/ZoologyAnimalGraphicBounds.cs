using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace ZoologyMod
{
    // The only graphic-growth endpoints are the current and next stage's
    // authored footprints. All expensive texture measurements happen once
    // per PawnKind and sex, never per biological day or rendering frame.
    internal static class ZoologyAnimalGraphicBounds
    {
        private const byte VisibleAlpha = 12;
        private const int ReadbackSize = 256;

        private struct Size
        {
            internal float width;
            internal float height;
            internal bool Valid => width > 0f && height > 0f;
        }

        private sealed class Transition
        {
            internal string fromPath;
            internal string toPath;
            internal Size[] from = new Size[4];
            internal Size[] to = new Size[4];
            internal int measuredViews;
        }

        private sealed class KindBoundsData
        {
            internal Vector2[] targetRatios;
            internal Transition[] transitions;
        }

        private static readonly Dictionary<Texture, Size> TextureBounds =
            new Dictionary<Texture, Size>();
        private static readonly Dictionary<PawnKindDef, KindBoundsData[]> KindBounds =
            new Dictionary<PawnKindDef, KindBoundsData[]>();
        private static readonly HashSet<string> ReportedDrawSizeErrors =
            new HashSet<string>(StringComparer.Ordinal);

        // The size at the first biological day is exactly the current
        // stage's drawSize. The last sample is exactly the next stage's
        // visible size. Neither BodySize nor any following stage may modify
        // this interval, and interpolation cannot overshoot its endpoints.
        internal static Vector2 Interpolate(PawnKindDef kind, int index,
            bool female, float progress)
        {
            KindBoundsData data = GetOrBuild(kind, female);
            if (data == null || index < 0 || index >= data.targetRatios.Length)
                return Vector2.one;
            Vector2 target = data.targetRatios[index];
            return new Vector2(1f + (target.x - 1f) * progress,
                1f + (target.y - 1f) * progress);
        }

        internal static void ClearCache()
        {
            KindBounds.Clear();
        }

        // Convert a biological linear scale into a drawSize independent of
        // the original juvenile quad. Use the tightest visible comparison
        // across both axes and all four rotations for different textures.
        internal static Vector2 NormalizedDrawSize(GraphicData adult,
            GraphicData juvenile, float scale)
        {
            if (adult == null || juvenile == null || !(scale > 0f))
                return Vector2.one;
            float factor = 1f;
            if (adult.texPath != juvenile.texPath)
            {
                Graphic first = ResolveGraphic(adult);
                Graphic second = ResolveGraphic(juvenile);
                float limit = float.PositiveInfinity;
                int measured = 0;
                Rot4[] rotations = { Rot4.North, Rot4.East, Rot4.South, Rot4.West };
                if (first != null && second != null)
                    foreach (Rot4 rotation in rotations)
                    {
                        try
                        {
                            Size a, b;
                            Texture ta = first.MatAt(rotation)?.mainTexture;
                            Texture tb = second.MatAt(rotation)?.mainTexture;
                            if (ta == null || tb == null
                                || !TryBounds(ta, out a) || !TryBounds(tb, out b))
                                continue;
                            limit = Mathf.Min(limit, a.width / b.width);
                            limit = Mathf.Min(limit, a.height / b.height);
                            measured++;
                        }
                        catch (Exception ex)
                        {
                            Log.Warning("[Zoology] Juvenile alpha readback failed: "
                                + juvenile.texPath + ": " + ex.GetType().Name);
                        }
                    }
                if (measured > 0)
                    factor = limit;
                else
                    Log.Warning("[Zoology] Juvenile alpha calibration unavailable: "
                        + juvenile.texPath + " relative to " + adult.texPath);
            }
            return adult.drawSize * (scale * factor);
        }

        // Ensure the juvenile has no larger visible footprint in any view
        // than a mass-scaled adult, independently of transparent margins.
        // Same-texture comparisons are exact and need no GPU alpha readback.
        internal static float VisibleScaleLimit(GraphicData adult,
            GraphicData juvenile, float scale)
        {
            if (adult == null || juvenile == null || !(scale > 0f)
                || adult.drawSize.x <= 0f || adult.drawSize.y <= 0f
                || juvenile.drawSize.x <= 0f || juvenile.drawSize.y <= 0f)
                return 1f;

            float quadLimit = Mathf.Min(
                scale * adult.drawSize.x / juvenile.drawSize.x,
                scale * adult.drawSize.y / juvenile.drawSize.y);
            if (adult.texPath == juvenile.texPath)
                return Mathf.Min(1f, quadLimit);

            Graphic adultGraphic = ResolveGraphic(adult);
            Graphic juvenileGraphic = ResolveGraphic(juvenile);
            float measuredLimit = float.PositiveInfinity;
            int measurements = 0;
            Rot4[] rotations = { Rot4.North, Rot4.East, Rot4.South, Rot4.West };
            if (adultGraphic != null && juvenileGraphic != null)
                foreach (Rot4 rotation in rotations)
                {
                    try
                    {
                        Texture a = adultGraphic.MatAt(rotation)?.mainTexture;
                        Texture b = juvenileGraphic.MatAt(rotation)?.mainTexture;
                        Size adultBounds, juvenileBounds;
                        if (a == null || b == null
                            || !TryBounds(a, out adultBounds)
                            || !TryBounds(b, out juvenileBounds))
                            continue;
                        measuredLimit = Mathf.Min(measuredLimit,
                            scale * adultBounds.width * adult.drawSize.x
                                / (juvenileBounds.width * juvenile.drawSize.x));
                        measuredLimit = Mathf.Min(measuredLimit,
                            scale * adultBounds.height * adult.drawSize.y
                                / (juvenileBounds.height * juvenile.drawSize.y));
                        measurements++;
                    }
                    catch (Exception ex)
                    {
                        Log.Warning("[Zoology] Juvenile adult-bound check failed: "
                            + juvenile.texPath + ": " + ex.GetType().Name);
                    }
                }
            return Mathf.Min(1f, measurements > 0 ? measuredLimit : quadLimit);
        }

        // Distinct mature female artwork supplies the female geometric
        // reference, but its authored sex-size contrast is removed from
        // the juvenile endpoint. The largest visible dimension is common.
        internal static float AdultReferenceNormalizer(GraphicData common,
            GraphicData female)
        {
            float a = MaxVisible(common);
            float b = MaxVisible(female);
            return a > 0f && b > 0f ? a / b : 1f;
        }

        // Normalize the *overall visible linear extent* of alternative
        // juvenile artwork. Their different silhouettes remain intact.
        // Transition growth still uses separate width/height comparisons
        // for every rotation, not this envelope measurement.
        internal static Vector2 MatchVisibleLinearSize(GraphicData target,
            GraphicData alternative)
        {
            if (target == null || alternative == null)
                return Vector2.one;
            float reference = MaxVisible(target);
            float current = MaxVisible(alternative);
            return reference > 0f && current > 0f
                ? alternative.drawSize * (reference / current)
                : alternative.drawSize;
        }

        private static float MaxVisible(GraphicData data)
        {
            if (data == null)
                return 0f;
            Graphic graphic = ResolveGraphic(data);
            float maximum = 0f;
            Rot4[] rotations = { Rot4.North, Rot4.East, Rot4.South, Rot4.West };
            if (graphic != null)
                foreach (Rot4 rotation in rotations)
                {
                    try
                    {
                        Texture texture = graphic.MatAt(rotation)?.mainTexture;
                        Size bounds;
                        if (texture != null && TryBounds(texture, out bounds))
                            maximum = Mathf.Max(maximum,
                                bounds.width * data.drawSize.x,
                                bounds.height * data.drawSize.y);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning("[Zoology] Adult alpha calibration failed: "
                            + data.texPath + ": " + ex.GetType().Name);
                    }
                }
            return maximum > 0f ? maximum
                : Mathf.Max(data.drawSize.x, data.drawSize.y);
        }

        internal static void AppendVisibleDimensions(GraphicData data,
            StringBuilder output)
        {
            if (data == null)
                return;
            Graphic graphic = ResolveGraphic(data);
            if (graphic == null)
                return;
            Rot4[] rotations = { Rot4.North, Rot4.East, Rot4.South, Rot4.West };
            foreach (Rot4 rotation in rotations)
            {
                try
                {
                    Texture texture = graphic.MatAt(rotation)?.mainTexture;
                    Size bounds;
                    if (texture != null && TryBounds(texture, out bounds))
                        output.Append("    ").Append(rotation)
                            .Append(" visible (")
                            .Append((bounds.width * data.drawSize.x).ToString("0.######"))
                            .Append(", ")
                            .Append((bounds.height * data.drawSize.y).ToString("0.######"))
                            .AppendLine(")");
                }
                catch (Exception ex)
                {
                    output.Append("    ").Append(rotation)
                        .Append(" unavailable: ").Append(ex.GetType().Name)
                        .AppendLine();
                }
            }
        }

        // Check declared same-art sizes during initialization, independently
        // of whether a player has spawned any of these animal species.
        // This does not inspect pixels or run in the rendering hot path.
        internal static void ValidateDrawSizeOrder()
        {
            foreach (var kind in DefDatabase<PawnKindDef>.AllDefsListForReading)
            {
                var stages = kind?.lifeStages;
                if (stages == null || stages.Count < 2
                    || !ZoologyAnimalGrowth.HandlesRace(kind.race))
                    continue;
                for (int i = 0; i + 1 < stages.Count; i++)
                {
                    CheckSameArtOrder(kind, stages, i, false);
                    CheckSameArtOrder(kind, stages, i, true);
                }
            }
        }

        private static void CheckSameArtOrder(PawnKindDef kind,
            List<PawnKindLifeStage> stages, int index, bool female)
        {
            GraphicData from = StageGraphic(stages[index], female);
            GraphicData to = StageGraphic(stages[index + 1], female);
            if (from == null || to == null
                || string.IsNullOrEmpty(from.texPath)
                || from.texPath != to.texPath
                || (to.drawSize.x >= from.drawSize.x
                    && to.drawSize.y >= from.drawSize.y))
                return;

            string key = kind.defName + "/" + index + "/" + female;
            if (!ReportedDrawSizeErrors.Add(key))
                return;
            Log.Warning("[Zoology] Decreasing drawSize between stages with the "
                + "same sprite: " + kind.defName + ", "
                + (female ? "female" : "male") + ", stage " + index
                + " -> " + (index + 1) + ", texture " + from.texPath
                + ", drawSize (" + from.drawSize.x + ", " + from.drawSize.y
                + ") -> (" + to.drawSize.x + ", " + to.drawSize.y
                + "). Correct the source graphics; no runtime size correction "
                + "is applied.");
        }

        private static KindBoundsData GetOrBuild(PawnKindDef kind, bool female)
        {
            if (kind?.lifeStages == null || kind.lifeStages.Count < 2)
                return null;

            KindBoundsData[] variants;
            if (!KindBounds.TryGetValue(kind, out variants))
            {
                variants = new KindBoundsData[2];
                KindBounds.Add(kind, variants);
            }

            int variant = female ? 1 : 0;
            if (variants[variant] == null)
                variants[variant] = Build(kind, female);
            return variants[variant];
        }

        private static KindBoundsData Build(PawnKindDef kind, bool female)
        {
            var stages = kind.lifeStages;
            var data = new KindBoundsData
            {
                targetRatios = new Vector2[stages.Count],
                transitions = new Transition[stages.Count]
            };
            for (int i = 0; i < data.targetRatios.Length; i++)
                data.targetRatios[i] = Vector2.one;

            for (int i = 0; i + 1 < stages.Count; i++)
            {
                GraphicData from = StageGraphic(stages[i], female);
                GraphicData to = StageGraphic(stages[i + 1], female);
                if (from == null || to == null
                    || from.drawSize.x <= 0f || from.drawSize.y <= 0f
                    || to.drawSize.x <= 0f || to.drawSize.y <= 0f
                    || string.IsNullOrEmpty(from.texPath)
                    || string.IsNullOrEmpty(to.texPath))
                    continue;

                if (from.texPath == to.texPath)
                {
                    // Identical art: the authored drawSize of the next
                    // stage is the exact endpoint, independently per axis.
                    data.targetRatios[i] = new Vector2(
                        to.drawSize.x / from.drawSize.x,
                        to.drawSize.y / from.drawSize.y);
                    continue;
                }

                // Different art: use the original, per-rotation alpha-bound
                // rule. The limiting endpoint is the smallest ratio of next
                // visible dimension to current visible dimension, comparing
                // width AND height for each orientation separately. This
                // guarantees the old sprite fits the new one in every view;
                // ratios of global maxima do not provide that guarantee.
                var transition = new Transition
                {
                    fromPath = from.texPath,
                    toPath = to.texPath
                };
                data.transitions[i] = transition;
                Graphic fromGraphic = ResolveGraphic(from);
                Graphic toGraphic = ResolveGraphic(to);
                float endpoint = float.PositiveInfinity;
                Rot4[] rotations = { Rot4.North, Rot4.East, Rot4.South, Rot4.West };
                if (fromGraphic != null && toGraphic != null)
                {
                    for (int view = 0; view < rotations.Length; view++)
                    {
                        try
                        {
                            Texture first = fromGraphic.MatAt(rotations[view])?.mainTexture;
                            Texture second = toGraphic.MatAt(rotations[view])?.mainTexture;
                            Size firstBounds, secondBounds;
                            if (first == null || second == null
                                || !TryBounds(first, out firstBounds)
                                || !TryBounds(second, out secondBounds))
                                continue;

                            transition.from[view] = new Size
                            {
                                width = firstBounds.width * from.drawSize.x,
                                height = firstBounds.height * from.drawSize.y
                            };
                            transition.to[view] = new Size
                            {
                                width = secondBounds.width * to.drawSize.x,
                                height = secondBounds.height * to.drawSize.y
                            };
                            endpoint = Mathf.Min(endpoint,
                                transition.to[view].width
                                    / transition.from[view].width);
                            endpoint = Mathf.Min(endpoint,
                                transition.to[view].height
                                    / transition.from[view].height);
                            transition.measuredViews++;
                        }
                        catch (Exception ex)
                        {
                            Log.Warning("[Zoology] Sprite-bound measurement failed: "
                                + from.texPath + " -> " + to.texPath + ": "
                                + ex.GetType().Name);
                        }
                    }
                }

                if (transition.measuredViews == 0)
                {
                    // The declared quads are the only geometric information
                    // available when alpha readback fails. Preserve the same
                    // per-axis limiting rule rather than switching to a
                    // non-conservative global-maximum ratio.
                    endpoint = Mathf.Min(to.drawSize.x / from.drawSize.x,
                        to.drawSize.y / from.drawSize.y);
                    Log.Warning("[Zoology] Graphic alpha bounds unavailable for "
                        + kind.defName + ": " + from.texPath + " -> "
                        + to.texPath + ". Using limiting drawSize axis.");
                }


                data.targetRatios[i] = new Vector2(endpoint, endpoint);
                if (endpoint < 1f)
                    Log.Warning("[Zoology] The next stage has a smaller visible "
                        + "silhouette than the preceding stage: " + kind.defName
                        + ", " + (female ? "female" : "male") + ", stage "
                        + i + " -> " + (i + 1) + ", "
                        + from.texPath + " -> " + to.texPath
                        + ". Stage graphics must be corrected at their source.");
            }
            return data;
        }

        // Called only by the explicit development-mode inspection command.
        internal static void AppendDiagnostics(PawnKindDef kind, int index,
            bool female, float progress, StringBuilder output)
        {
            KindBoundsData data = GetOrBuild(kind, female);
            if (data == null || index < 0 || index >= data.targetRatios.Length)
                return;

            Vector2 target = data.targetRatios[index];
            Vector2 current = Interpolate(kind, index, female, progress);
            output.Append("Graphic growth: from (1, 1) to (")
                .Append(target.x.ToString("0.####")).Append(", ")
                .Append(target.y.ToString("0.####"))
                .Append("), progress ").Append(progress.ToString("0.####"))
                .Append("; current root scale (")
                .Append(current.x.ToString("0.####")).Append(", ")
                .Append(current.y.ToString("0.####")).AppendLine(")");

            Transition transition = data.transitions[index];
            if (transition == null)
                return;
            output.Append("  Texture transition: ")
                .Append(transition.fromPath).Append(" -> ")
                .Append(transition.toPath).AppendLine();
            string[] names = { "North", "East", "South", "West" };
            for (int i = 0; i < transition.from.Length; i++)
            {
                Size first = transition.from[i];
                Size second = transition.to[i];
                if (!first.Valid || !second.Valid)
                    continue;
                output.Append("  ").Append(names[i])
                    .Append(" visible size (")
                    .Append(first.width.ToString("0.###")).Append(", ")
                    .Append(first.height.ToString("0.###"))
                    .Append(") -> (")
                    .Append(second.width.ToString("0.###")).Append(", ")
                    .Append(second.height.ToString("0.###"))
                    .AppendLine(")");
            }
        }

        private static GraphicData StageGraphic(PawnKindLifeStage stage, bool female)
        {
            if (stage == null)
                return null;
            return female ? stage.femaleGraphicData ?? stage.bodyGraphicData
                : stage.bodyGraphicData;
        }

        private static Graphic ResolveGraphic(GraphicData data)
        {
            try
            {
                return data.graphicClass == null
                    ? GraphicDatabase.Get<Graphic_Multi>(
                        data.texPath, ShaderDatabase.Cutout, data.drawSize, data.color)
                    : data.Graphic;
            }
            catch (Exception ex)
            {
                Log.Warning("[Zoology] Could not resolve animal body texture "
                    + data.texPath + ": " + ex.GetType().Name);
                return null;
            }
        }

        private static bool TryBounds(Texture texture, out Size bounds)
        {
            if (TextureBounds.TryGetValue(texture, out bounds))
                return bounds.Valid;

            bounds = default(Size);
            try
            {
                Texture2D readable = texture as Texture2D;
                bounds = readable != null && readable.isReadable
                    ? Measure(readable.GetPixels32(), readable.width, readable.height)
                    : ReadAndMeasure(texture);
            }
            catch (Exception ex)
            {
                Log.Warning("[Zoology] Could not read alpha bounds for texture "
                    + texture.name + ": " + ex.GetType().Name);
            }

            TextureBounds.Add(texture, bounds);
            return bounds.Valid;
        }

        private static Size ReadAndMeasure(Texture texture)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture target = null;
            Texture2D readable = null;
            try
            {
                target = RenderTexture.GetTemporary(
                    ReadbackSize, ReadbackSize, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(texture, target);
                RenderTexture.active = target;
                readable = new Texture2D(ReadbackSize, ReadbackSize,
                    TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0, 0, ReadbackSize, ReadbackSize), 0, 0, false);
                readable.Apply(false, false);
                return Measure(readable.GetPixels32(), ReadbackSize, ReadbackSize);
            }
            finally
            {
                RenderTexture.active = previous;
                if (target != null)
                    RenderTexture.ReleaseTemporary(target);
                if (readable != null)
                    UnityEngine.Object.Destroy(readable);
            }
        }

        private static Size Measure(Color32[] pixels, int width, int height)
        {
            if (pixels == null || width <= 0 || height <= 0
                || pixels.Length < (long)width * height)
                return default(Size);

            int left = width, right = -1, bottom = height, top = -1;
            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    if (pixels[row + x].a < VisibleAlpha)
                        continue;
                    if (x < left) left = x;
                    if (x > right) right = x;
                    if (y < bottom) bottom = y;
                    if (y > top) top = y;
                }
            }
            return right < left ? default(Size) : new Size
            {
                width = (float)(right - left + 1) / width,
                height = (float)(top - bottom + 1) / height
            };
        }
    }
}
