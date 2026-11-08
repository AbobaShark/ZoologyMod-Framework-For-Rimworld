using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace ZoologyMod
{
    // Calibrate only transitions to a different body texture. A visible
    // silhouette, rather than the whole drawSize quad, sets their size.
    // Identical-texture stages keep the original daily interpolation.
    internal static class ZoologyAnimalGraphicCalibration
    {
        private const int SampleAxis = 192;
        private const byte AlphaThreshold = 32;
        private const float TrimmedAlphaFraction = 0.01f;

        private struct Footprint
        {
            internal float width;
            internal float height;
            internal float areaDiameter;

            internal bool Valid => width > 0f && height > 0f
                && areaDiameter > 0f;
        }

        private sealed class KindCalibration
        {
            internal float[] factors;
            internal string[] transitions;
        }

        // Both caches are shared by all pawns. Neither PNG inspection nor GPU
        // readback occurs inside the daily growth calculation or every draw.
        private static readonly Dictionary<PawnKindDef, KindCalibration[]> ByKind =
            new Dictionary<PawnKindDef, KindCalibration[]>();
        private static readonly Dictionary<Texture, Footprint> ByTexture =
            new Dictionary<Texture, Footprint>();

        internal static float StageFactor(PawnKindDef kind, int stageIndex, bool female)
        {
            if (kind?.lifeStages == null || stageIndex < 0
                || stageIndex >= kind.lifeStages.Count)
                return 1f;

            return GetOrBuild(kind, female).factors[stageIndex];
        }

        internal static void AppendDiagnostics(PawnKindDef kind, bool female, StringBuilder output)
        {
            if (kind?.lifeStages == null || output == null)
                return;
            KindCalibration calibration = GetOrBuild(kind, female);
            output.Append("Graphic calibration (")
                .Append(female ? "female" : "default")
                .AppendLine("):");
            for (int i = 0; i < calibration.factors.Length; i++)
            {
                output.Append("  stage ").Append(i).Append(": x")
                    .Append(calibration.factors[i].ToString("0.####"));
                if (!string.IsNullOrEmpty(calibration.transitions[i]))
                    output.Append("; ").Append(calibration.transitions[i]);
                output.AppendLine();
            }
        }

        private static KindCalibration GetOrBuild(PawnKindDef kind, bool female)
        {
            KindCalibration[] variants;
            if (!ByKind.TryGetValue(kind, out variants))
            {
                variants = new KindCalibration[2];
                ByKind.Add(kind, variants);
            }

            int variant = female ? 1 : 0;
            if (variants[variant] == null)
                variants[variant] = Build(kind, female);
            return variants[variant];
        }

        private static KindCalibration Build(PawnKindDef kind, bool female)
        {
            var stages = kind.lifeStages;
            int count = stages.Count;
            var result = new KindCalibration
            {
                factors = new float[count],
                transitions = new string[count]
            };
            for (int i = 0; i < count; i++)
                result.factors[i] = 1f;

            // Adult artwork remains at its authored scale. Work backwards.
            // All adjacent stages with the same artwork inherit one common
            // factor and therefore keep their original relative growth.
            for (int i = count - 2; i >= 0; i--)
            {
                result.factors[i] = result.factors[i + 1];
                GraphicData from = GraphicForStage(stages[i], female);
                GraphicData to = GraphicForStage(stages[i + 1], female);
                if (from == null || to == null
                    || string.IsNullOrEmpty(from.texPath)
                    || string.IsNullOrEmpty(to.texPath)
                    || string.Equals(from.texPath, to.texPath, StringComparison.Ordinal))
                    continue;

                Footprint[] source;
                Footprint[] target;
                if (!TryFacings(from, out source) || !TryFacings(to, out target))
                {
                    result.transitions[i] = from.texPath + " -> " + to.texPath
                        + ": cannot inspect both silhouettes; native scale retained";
                    continue;
                }

                // Preserve size in every supported cardinal view, instead of
                // comparing median bounding boxes. The minimum of the width,
                // height and sqrt(alpha area) ratios is conservative: an
                // outgoing animal cannot be larger on any of these measures
                // immediately before the incoming texture appears.
                float ratio = float.PositiveInfinity;
                int matched = 0;
                for (int direction = 0; direction < 4; direction++)
                {
                    if (!source[direction].Valid || !target[direction].Valid)
                        continue;
                    ratio = Mathf.Min(ratio, target[direction].width / source[direction].width);
                    ratio = Mathf.Min(ratio, target[direction].height / source[direction].height);
                    ratio = Mathf.Min(ratio, target[direction].areaDiameter
                        / source[direction].areaDiameter);
                    matched++;
                }

                if (matched == 0 || !(ratio > 0f)
                    || float.IsNaN(ratio) || float.IsInfinity(ratio))
                {
                    result.transitions[i] = from.texPath + " -> " + to.texPath
                        + ": no comparable facings; native scale retained";
                    continue;
                }

                // A transition that is already increasing must not enlarge
                // the previous artwork and make its other directions oversized.
                // Upward size jumps are preferable to reversal of growth.
                float correction = Mathf.Min(1f, ratio);
                result.factors[i] = result.factors[i + 1] * correction;
                result.transitions[i] = from.texPath + " -> " + to.texPath
                    + "; silhouette ratio " + ratio.ToString("0.####")
                    + "; applied x" + correction.ToString("0.####")
                    + "; directions " + matched;
            }
            return result;
        }

        private static GraphicData GraphicForStage(PawnKindLifeStage stage, bool female)
        {
            if (stage == null)
                return null;
            return female ? stage.femaleGraphicData ?? stage.bodyGraphicData
                : stage.bodyGraphicData;
        }

        private static bool TryFacings(GraphicData data, out Footprint[] measures)
        {
            measures = new Footprint[4];
            try
            {
                // GraphicData.Graphic is null when graphicClass is unset. In
                // animal stages the renderer uses a directional Graphic_Multi.
                Graphic graphic = data.graphicClass == null
                    ? GraphicDatabase.Get<Graphic_Multi>(
                        data.texPath, ShaderDatabase.Cutout, data.drawSize, data.color)
                    : data.Graphic;
                if (graphic == null)
                    return false;

                Rot4[] directions = { Rot4.North, Rot4.East, Rot4.South, Rot4.West };
                int valid = 0;
                for (int i = 0; i < directions.Length; i++)
                {
                    Texture texture = graphic.MatAt(directions[i])?.mainTexture;
                    Footprint measure;
                    if (texture != null && TryTextureFootprint(texture, out measure))
                    {
                        measures[i] = measure;
                        valid++;
                    }
                }
                return valid > 0;
            }
            catch (Exception ex)
            {
                Log.Warning("[Zoology] Cannot inspect animal graphic " + data.texPath
                    + ": " + ex.GetType().Name + "; using original draw scale.");
                return false;
            }
        }

        private static bool TryTextureFootprint(Texture texture, out Footprint value)
        {
            if (ByTexture.TryGetValue(texture, out value))
                return value.Valid;

            value = default(Footprint);
            try
            {
                Texture2D readable = texture as Texture2D;
                value = readable != null && readable.isReadable
                    ? Measure(readable.GetPixels32(), readable.width, readable.height)
                    : MeasureGpuCopy(texture);
            }
            catch (Exception ex)
            {
                Log.Warning("[Zoology] Cannot read animal texture alpha "
                    + texture.name + ": " + ex.GetType().Name);
            }

            if (!value.Valid)
                value = default(Footprint);
            ByTexture.Add(texture, value);
            return value.Valid;
        }

        private static Footprint MeasureGpuCopy(Texture source)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture target = null;
            Texture2D copy = null;
            try
            {
                target = RenderTexture.GetTemporary(
                    SampleAxis, SampleAxis, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                copy = new Texture2D(SampleAxis, SampleAxis, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0, 0, SampleAxis, SampleAxis), 0, 0, false);
                copy.Apply(false, false);
                return Measure(copy.GetPixels32(), SampleAxis, SampleAxis);
            }
            finally
            {
                RenderTexture.active = previous;
                if (target != null)
                    RenderTexture.ReleaseTemporary(target);
                if (copy != null)
                    UnityEngine.Object.Destroy(copy);
            }
        }

        private static Footprint Measure(Color32[] pixels, int width, int height)
        {
            Footprint empty = default(Footprint);
            if (pixels == null || width <= 0 || height <= 0
                || pixels.Length < (long)width * height)
                return empty;

            int step = Math.Max(1, (Math.Max(width, height) + SampleAxis - 1) / SampleAxis);
            int cols = (width + step - 1) / step;
            int rows = (height + step - 1) / step;
            long[] xWeights = new long[cols];
            long[] yWeights = new long[rows];
            long total = 0;
            for (int y = 0, iy = 0; y < height; y += step, iy++)
            {
                int offset = y * width;
                for (int x = 0, ix = 0; x < width; x += step, ix++)
                {
                    byte alpha = pixels[offset + x].a;
                    if (alpha < AlphaThreshold)
                        continue;
                    xWeights[ix] += alpha;
                    yWeights[iy] += alpha;
                    total += alpha;
                }
            }

            if (total == 0)
                return empty;

            long trim = Math.Max(1L, (long)(total * TrimmedAlphaFraction));
            int minX = PercentileFromStart(xWeights, trim);
            int maxX = PercentileFromEnd(xWeights, trim);
            int minY = PercentileFromStart(yWeights, trim);
            int maxY = PercentileFromEnd(yWeights, trim);
            if (maxX < minX || maxY < minY)
                return empty;

            return new Footprint
            {
                width = Mathf.Min(1f, (maxX - minX + 1f) * step / width),
                height = Mathf.Min(1f, (maxY - minY + 1f) * step / height),
                // Square root turns occupied area into a linear size. Unlike
                // a bounding rectangle it reacts to a solid, round chick
                // versus a sparse silhouette with a long feathered tail.
                areaDiameter = Mathf.Sqrt((float)total / (255f * cols * rows))
            };
        }

        private static int PercentileFromStart(long[] weights, long threshold)
        {
            long cumulative = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                cumulative += weights[i];
                if (cumulative >= threshold)
                    return i;
            }
            return weights.Length - 1;
        }

        private static int PercentileFromEnd(long[] weights, long threshold)
        {
            long cumulative = 0;
            for (int i = weights.Length - 1; i >= 0; i--)
            {
                cumulative += weights[i];
                if (cumulative >= threshold)
                    return i;
            }
            return 0;
        }
    }
}
