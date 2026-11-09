using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace ZoologyMod
{
    // For artwork switches, interpolate between a stage's starting scale
    // and the largest scale that fits the next sprite's visible dimensions.
    // Measure only once per texture; growth itself performs no pixel work.
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
            internal int views;
        }

        private sealed class KindBoundsData
        {
            internal Transition[] transitions;
            internal float[] startScales;
            internal float[] endScales;
        }

        private static readonly Dictionary<Texture, Size> TextureBounds =
            new Dictionary<Texture, Size>();
        private static readonly Dictionary<PawnKindDef, KindBoundsData[]> KindBounds =
            new Dictionary<PawnKindDef, KindBoundsData[]>();

        internal static Vector2 Interpolate(PawnKindDef kind, int index, bool female,
            Vector2 originalScale, float progress)
        {
            KindBoundsData data = GetOrBuild(kind, female);
            if (data == null || index < 0 || index >= data.startScales.Length)
                return originalScale;

            Transition transition = data.transitions[index];
            if (transition == null || transition.views == 0)
            {
                // Identical artwork retains the original growth curve.
                // A shared group multiplier is only needed if a later
                // artwork switch requires a smaller starting size.
                return originalScale * data.startScales[index];
            }

            // No per-day clamping: the bound is an endpoint. Reach it on
            // the final biological day, with a change on every earlier day.
            float scale = Mathf.Lerp(data.startScales[index],
                data.endScales[index], Mathf.Clamp01(progress));
            return new Vector2(scale, scale);
        }

        private static KindBoundsData GetOrBuild(PawnKindDef kind, bool female)
        {
            if (kind?.lifeStages == null || kind.lifeStages.Count == 0)
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

        // Called only from the existing developer inspection action.
        internal static void AppendDiagnostics(PawnKindDef kind, int index,
            bool female, Vector2 rawScale, float progress, StringBuilder output)
        {
            KindBoundsData data = GetOrBuild(kind, female);
            if (data == null || index < 0 || index >= data.startScales.Length)
                return;

            Transition transition = data.transitions[index];
            if (transition == null)
            {
                output.Append("Graphic switch: none (same artwork); group factor x")
                    .Append(data.startScales[index].ToString("0.####")).AppendLine();
                return;
            }
            output.Append("Graphic switch: ").Append(transition.fromPath)
                .Append(" -> ").Append(transition.toPath).AppendLine();
            output.Append("  dimensions in world units; raw scale (")
                .Append(rawScale.x.ToString("0.####")).Append(", ")
                .Append(rawScale.y.ToString("0.####")).AppendLine(")");
            if (transition.views == 0)
                output.AppendLine("  Alpha bounds unavailable; original scale retained.");

            string[] names = { "North", "East", "South", "West" };
            for (int i = 0; i < 4; i++)
            {
                Size oldSize = transition.from[i];
                Size newSize = transition.to[i];
                if (!oldSize.Valid || !newSize.Valid)
                    continue;
                output.Append("  ").Append(names[i])
                    .Append(" current (").Append(oldSize.width.ToString("0.###"))
                    .Append(", ").Append(oldSize.height.ToString("0.###"))
                    .Append(") next (").Append(newSize.width.ToString("0.###"))
                    .Append(", ").Append(newSize.height.ToString("0.###"))
                    .AppendLine(")");
            }
            Vector2 result = Interpolate(kind, index, female, rawScale, progress);
            output.Append("  start x").Append(data.startScales[index].ToString("0.####"))
                .Append("; end x").Append(data.endScales[index].ToString("0.####"))
                .Append("; last-day progress ").Append(progress.ToString("0.####"))
                .AppendLine();
            output.Append("  next-stage starting scale x")
                .Append(data.startScales[index + 1].ToString("0.####"))
                .AppendLine();
            output.Append("  interpolated root scale (")
                .Append(result.x.ToString("0.####")).Append(", ")
                .Append(result.y.ToString("0.####")).AppendLine(")");
        }

        private static KindBoundsData Build(PawnKindDef kind, bool female)
        {
            var stages = kind.lifeStages;
            var data = new KindBoundsData
            {
                transitions = new Transition[stages.Count],
                startScales = new float[stages.Count],
                endScales = new float[stages.Count]
            };
            var transitions = data.transitions;
            for (int i = 0; i + 1 < stages.Count; i++)
            {
                GraphicData from = StageGraphic(stages[i], female);
                GraphicData to = StageGraphic(stages[i + 1], female);
                if (from == null || to == null
                    || string.IsNullOrEmpty(from.texPath)
                    || string.IsNullOrEmpty(to.texPath)
                    || string.Equals(from.texPath, to.texPath, StringComparison.Ordinal))
                    continue;

                var entry = new Transition { fromPath = from.texPath, toPath = to.texPath };
                transitions[i] = entry;
                Graphic fromGraphic = ResolveGraphic(from);
                Graphic toGraphic = ResolveGraphic(to);
                if (fromGraphic == null || toGraphic == null)
                    continue;

                Rot4[] rotations = { Rot4.North, Rot4.East, Rot4.South, Rot4.West };
                for (int direction = 0; direction < rotations.Length; direction++)
                {
                    try
                    {
                        Texture oldTexture = fromGraphic.MatAt(rotations[direction])?.mainTexture;
                        Texture newTexture = toGraphic.MatAt(rotations[direction])?.mainTexture;
                        Size oldBounds;
                        Size newBounds;
                        if (oldTexture == null || newTexture == null
                            || !TryBounds(oldTexture, out oldBounds)
                            || !TryBounds(newTexture, out newBounds))
                            continue;

                        // A visible pixel fraction becomes an absolute size
                        // only after multiplying by the authored quad size.
                        entry.from[direction] = new Size
                        {
                            width = oldBounds.width * from.drawSize.x,
                            height = oldBounds.height * from.drawSize.y
                        };
                        entry.to[direction] = new Size
                        {
                            width = newBounds.width * to.drawSize.x,
                            height = newBounds.height * to.drawSize.y
                        };
                        if (entry.from[direction].Valid && entry.to[direction].Valid)
                            entry.views++;
                    }
                    catch (Exception ex)
                    {
                        Log.Warning("[Zoology] Animal sprite bounds unavailable for "
                            + from.texPath + " -> " + to.texPath + ": " + ex.GetType().Name);
                    }
                }
            }
            // The final artwork is never rescaled. Earlier stages use the
            // start of their successor as the actual visible-size target.
            // This also handles several consecutive artwork changes.
            data.startScales[stages.Count - 1] = 1f;
            data.endScales[stages.Count - 1] = 1f;
            var ages = kind.race?.race?.lifeStageAges;
            for (int i = stages.Count - 2; i >= 0; i--)
            {
                Transition transition = transitions[i];
                GraphicData current = StageGraphic(stages[i], female);
                GraphicData next = StageGraphic(stages[i + 1], female);
                bool sameArtwork = current != null && next != null
                    && !string.IsNullOrEmpty(current.texPath)
                    && string.Equals(current.texPath, next.texPath, StringComparison.Ordinal);

                if (sameArtwork)
                {
                    // The original interpolation already joins two stages
                    // sharing an image. Preserve its shape and, when needed,
                    // carry the same uniform multiplier across their boundary.
                    data.startScales[i] = data.startScales[i + 1];
                    data.endScales[i] = data.startScales[i + 1];
                    continue;
                }

                if (transition == null || transition.views == 0)
                {
                    data.startScales[i] = 1f;
                    data.endScales[i] = 1f;
                    continue;
                }

                float nextStart = data.startScales[i + 1];
                float endpoint = float.PositiveInfinity;
                for (int direction = 0; direction < 4; direction++)
                {
                    Size oldSize = transition.from[direction];
                    Size newSize = transition.to[direction];
                    if (!oldSize.Valid || !newSize.Valid)
                        continue;
                    endpoint = Mathf.Min(endpoint,
                        nextStart * newSize.width / oldSize.width);
                    endpoint = Mathf.Min(endpoint,
                        nextStart * newSize.height / oldSize.height);
                }

                if (!(endpoint > 0f) || float.IsNaN(endpoint)
                    || float.IsInfinity(endpoint))
                {
                    // An unreadable transition must not introduce an
                    // arbitrary graphical correction.
                    transition.views = 0;
                    data.startScales[i] = 1f;
                    data.endScales[i] = 1f;
                    continue;
                }

                data.endScales[i] = endpoint;
                if (endpoint > 1f)
                {
                    data.startScales[i] = 1f;
                }
                else
                {
                    // The next picture can be smaller even than the old
                    // picture at the *beginning* of this stage. Rescale the
                    // start as well; otherwise the image would shrink during
                    // the interval. The ratio comes from the actual
                    // biological stage factors, not a guessed constant.
                    float growth = 1f;
                    if (ages != null && i + 1 < ages.Count)
                    {
                        float fromBody = ages[i]?.def?.bodySizeFactor ?? 0f;
                        float toBody = ages[i + 1]?.def?.bodySizeFactor ?? 0f;
                        if (fromBody > 0f && toBody > fromBody)
                            growth = Mathf.Sqrt(fromBody / toBody);
                    }
                    data.startScales[i] = endpoint * growth;
                }
            }
            return data;
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
