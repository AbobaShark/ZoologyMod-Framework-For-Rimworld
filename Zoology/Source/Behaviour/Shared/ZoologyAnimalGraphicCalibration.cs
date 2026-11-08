using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ZoologyMod
{
    // Graphic identity changes need a separate calibration: drawSize measures
    // the textured quad, not the visible animal. A uniform correction per run
    // of identical artwork retains the existing daily growth curve.
    internal static class ZoologyAnimalGraphicCalibration
    {
        private const int SampleAxis = 192;
        private const byte AlphaThreshold = 32;
        private const float TrimmedAlphaFraction = 0.01f;

        // Only kinds actually rendered while growing are examined. Neither
        // the texture nor the kind is rescanned for subsequent pawns/days.
        private static readonly Dictionary<PawnKindDef, float[]> ByKind =
            new Dictionary<PawnKindDef, float[]>();
        private static readonly Dictionary<Texture, float> ByTexture =
            new Dictionary<Texture, float>();

        internal static float StageFactor(PawnKindDef kind, int stageIndex)
        {
            if (kind?.lifeStages == null || stageIndex < 0
                || stageIndex >= kind.lifeStages.Count)
                return 1f;

            float[] factors;
            if (!ByKind.TryGetValue(kind, out factors))
            {
                factors = Build(kind);
                ByKind.Add(kind, factors);
            }
            return factors[stageIndex];
        }

        private static float[] Build(PawnKindDef kind)
        {
            var stages = kind.lifeStages;
            int count = stages.Count;
            var factors = new float[count];
            for (int i = 0; i < count; i++)
                factors[i] = 1f;

            // Keep the last/adult stage at its authored scale. Work backwards:
            // on a graphic change, the outgoing artwork at the next stage's
            // drawSize must occupy the same visible area as the incoming art.
            // Constant factors for contiguous stages with the same texPath
            // preserve their original relative sizes and interpolation.
            for (int i = count - 2; i >= 0; i--)
            {
                factors[i] = factors[i + 1];
                GraphicData from = stages[i]?.bodyGraphicData;
                GraphicData to = stages[i + 1]?.bodyGraphicData;
                if (from == null || to == null
                    || string.IsNullOrEmpty(from.texPath)
                    || string.IsNullOrEmpty(to.texPath)
                    || string.Equals(from.texPath, to.texPath, StringComparison.Ordinal))
                    continue;

                float sourceFootprint;
                float targetFootprint;
                if (!TryVisibleFootprint(from, out sourceFootprint)
                    || !TryVisibleFootprint(to, out targetFootprint))
                    continue;

                float factor = factors[i + 1] * targetFootprint / sourceFootprint;
                if (factor > 0f && !float.IsNaN(factor) && !float.IsInfinity(factor))
                    factors[i] = factor;
            }
            return factors;
        }

        private static bool TryVisibleFootprint(GraphicData data, out float footprint)
        {
            footprint = 0f;
            try
            {
                Graphic graphic = data.Graphic;
                if (graphic == null)
                    return false;

                // Use the median of the four facings. A curled tail or an
                // unusual posture in one direction must not set the scale of
                // the entire pawn and cause the size to change on turning.
                var directions = new[] { Rot4.North, Rot4.East, Rot4.South, Rot4.West };
                var measures = new float[4];
                int count = 0;
                for (int i = 0; i < directions.Length; i++)
                {
                    Material material = graphic.MatAt(directions[i]);
                    Texture texture = material?.mainTexture;
                    float measured;
                    if (texture != null && TryTextureFootprint(texture, out measured))
                        measures[count++] = measured;
                }

                if (count == 0)
                    return false;
                Array.Sort(measures, 0, count);
                footprint = (count & 1) == 0
                    ? (measures[count / 2 - 1] + measures[count / 2]) * 0.5f
                    : measures[count / 2];
                return footprint > 0f;
            }
            catch (Exception ex)
            {
                Log.Warning("[Zoology] Cannot calibrate animal graphic "
                    + data.texPath + ": " + ex.GetType().Name
                    + ". Using its authored drawSize.");
                return false;
            }
        }

        private static bool TryTextureFootprint(Texture texture, out float value)
        {
            if (ByTexture.TryGetValue(texture, out value))
                return value > 0f;

            value = 0f;
            try
            {
                Texture2D readable = texture as Texture2D;
                value = readable != null && readable.isReadable
                    ? Measure(readable.GetPixels32(), readable.width, readable.height)
                    : MeasureGpuCopy(texture);
            }
            catch (Exception ex)
            {
                Log.Warning("[Zoology] Cannot inspect alpha of animal texture "
                    + texture.name + ": " + ex.GetType().Name
                    + ". Falling back to uncalibrated rendering.");
            }

            if (!(value > 0f) || float.IsNaN(value) || float.IsInfinity(value))
                value = 0f;
            ByTexture.Add(texture, value);
            return value > 0f;
        }

        private static float MeasureGpuCopy(Texture source)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture target = null;
            Texture2D copy = null;
            try
            {
                // Many RimWorld textures are not CPU-readable. Perform a
                // single bounded GPU readback, never once per frame or day.
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

        private static float Measure(Color32[] pixels, int width, int height)
        {
            if (pixels == null || width <= 0 || height <= 0
                || pixels.Length < (long)width * height)
                return 0f;

            int step = Math.Max(1, (Math.Max(width, height) + SampleAxis - 1) / SampleAxis);
            int cols = (width + step - 1) / step;
            int rows = (height + step - 1) / step;
            var xWeights = new long[cols];
            var yWeights = new long[rows];
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
                return 0f;

            // Trim one percent of alpha mass at each edge to reduce the
            // influence of stray pixels, tails and antialiased fringes.
            long trim = Math.Max(1L, (long)(total * TrimmedAlphaFraction));
            int minX = PercentileFromStart(xWeights, trim);
            int maxX = PercentileFromEnd(xWeights, trim);
            int minY = PercentileFromStart(yWeights, trim);
            int maxY = PercentileFromEnd(yWeights, trim);
            if (maxX < minX || maxY < minY)
                return 0f;

            float fractionX = Mathf.Min(1f, (maxX - minX + 1f) * step / width);
            float fractionY = Mathf.Min(1f, (maxY - minY + 1f) * step / height);
            // Geometric mean is invariant under X/Y swaps due to orientation
            // and supplies one uniform (non-distorting) root scale factor.
            return Mathf.Sqrt(fractionX * fractionY);
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
