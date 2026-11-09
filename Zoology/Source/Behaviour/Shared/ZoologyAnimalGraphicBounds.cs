using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace ZoologyMod
{
    // Restrict the *rendered* width and height only when the next stage
    // switches body artwork. All values are in world units, not PNG pixels.
    // No changes to the original same-texture growth interpolation.
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

        private static readonly Dictionary<Texture, Size> TextureBounds =
            new Dictionary<Texture, Size>();
        private static readonly Dictionary<PawnKindDef, Transition[][]> KindBounds =
            new Dictionary<PawnKindDef, Transition[][]>();

        internal static Vector2 Clamp(PawnKindDef kind, int index, bool female, Vector2 scale)
        {
            Transition transition = GetTransition(kind, index, female);
            if (transition == null || transition.views == 0)
                return scale;

            // The next stage starts with its authored graphic at root scale 1.
            // Compare against the actual size of the current stage *after*
            // the daily draw ratio has been applied. Never stretch artwork:
            // a single correction is shared by width and height.
            float limit = 1f;
            for (int i = 0; i < 4; i++)
            {
                Size oldSize = transition.from[i];
                Size newSize = transition.to[i];
                if (!oldSize.Valid || !newSize.Valid)
                    continue;
                if (scale.x > 0f)
                    limit = Mathf.Min(limit, newSize.width / (oldSize.width * scale.x));
                if (scale.y > 0f)
                    limit = Mathf.Min(limit, newSize.height / (oldSize.height * scale.y));
            }
            return scale * Mathf.Clamp01(limit);
        }

        // Called only from the existing developer inspection action.
        internal static void AppendDiagnostics(PawnKindDef kind, int index,
            bool female, Vector2 rawScale, StringBuilder output)
        {
            Transition transition = GetTransition(kind, index, female);
            if (transition == null)
            {
                output.AppendLine("Graphic switch: none (original growth scale).");
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
            Vector2 bounded = Clamp(kind, index, female, rawScale);
            output.Append("  final root scale (")
                .Append(bounded.x.ToString("0.####")).Append(", ")
                .Append(bounded.y.ToString("0.####")).AppendLine(")");
        }

        private static Transition GetTransition(PawnKindDef kind, int index, bool female)
        {
            var stages = kind?.lifeStages;
            if (stages == null || index < 0 || index + 1 >= stages.Count)
                return null;

            Transition[][] variants;
            if (!KindBounds.TryGetValue(kind, out variants))
            {
                variants = new Transition[2][];
                KindBounds.Add(kind, variants);
            }
            int variant = female ? 1 : 0;
            if (variants[variant] == null)
                variants[variant] = Build(kind, female);
            return variants[variant][index];
        }

        private static Transition[] Build(PawnKindDef kind, bool female)
        {
            var stages = kind.lifeStages;
            var transitions = new Transition[stages.Count];
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
            return transitions;
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
