using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace ZoologyMod
{
    public sealed partial class Building_AnimalNursery
    {
        private Mesh canopyMesh;
        private Plant canopyMeshTree;
        private int canopyMeshPhase = -1;
        private Material fertilizedEggWindMaterial;
        private Material originalEggMaterial;

        public override Vector3 DrawPos
        {
            get
            {
                if (!TreeNest || anchorTree?.Spawned != true)
                    return base.DrawPos;

                Vector3 p = anchorTree.DrawPos + AnimalTreeCanopy.DrawOffset(
                    anchorTree, canopySlot < 0 ? 0 : canopySlot, CanopyFootprint);
                p.y = AltitudeLayer.MoteOverhead.AltitudeFor();
                return p;
            }
        }

        // Selection is based on the visible sprite, not the construction
        // cell. The margin covers the small tree-wind vertex displacement.
        internal bool CanopyHitTest(Vector3 click)
        {
            if (!Spawned || !TreeNest || anchorTree?.Spawned != true)
                return false;
            Vector3 p = DrawPos;
            Vector2 size = def.graphicData?.drawSize ?? new Vector2(0.42f, 0.42f);
            return Mathf.Abs(click.x - p.x) <= size.x * 0.5f + 0.14f
                && Mathf.Abs(click.z - p.z) <= size.y * 0.5f + 0.14f;
        }

        private Mesh WindMesh()
        {
            if (anchorTree?.Spawned != true)
                return null;
            int phase = anchorTree.HashOffset() % 1024;
            if (canopyMesh != null && canopyMeshTree == anchorTree
                && canopyMeshPhase == phase)
                return canopyMesh;
            DisposeCanopyMesh();

            // Trees use the plant shader's vertex-alpha wind exposure and
            // the HashOffset phase in UV.z. All four nest vertices receive
            // the tree's TOP-vertex exposure so the whole nest moves with
            // the branch rather than stretching about its own bottom edge.
            byte wind = PlantUtility.GetWindExposure(anchorTree);
            Mesh mesh = new Mesh { name = "Zoology tree-nest wind mesh" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f),
                new Vector3(-0.5f, 0f,  0.5f),
                new Vector3( 0.5f, 0f,  0.5f),
                new Vector3( 0.5f, 0f, -0.5f)
            };
            mesh.SetUVs(0, new List<Vector3>(4)
            {
                new Vector3(0f, 0f, phase),
                new Vector3(0f, 1f, phase),
                new Vector3(1f, 1f, phase),
                new Vector3(1f, 0f, phase)
            });
            mesh.colors32 = new[]
            {
                new Color32(0, 0, 0, wind),
                new Color32(0, 0, 0, wind),
                new Color32(0, 0, 0, wind),
                new Color32(0, 0, 0, wind)
            };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            canopyMesh = mesh;
            canopyMeshTree = anchorTree;
            canopyMeshPhase = phase;
            return mesh;
        }

        private void DisposeCanopyMesh()
        {
            if (canopyMesh != null)
                UnityEngine.Object.Destroy(canopyMesh);
            canopyMesh = null;
            canopyMeshTree = null;
            canopyMeshPhase = -1;
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            Vector3 pos = DrawPos;
            Mesh wind = TreeNest ? WindMesh() : null;
            if (wind != null)
            {
                Vector2 size = def.graphicData.drawSize;
                Graphics.DrawMesh(wind, Matrix4x4.TRS(pos, Quaternion.identity,
                    new Vector3(size.x, 1f, size.y)), Graphic.MatSingle, 0);
            }
            else if (TreeNest)
                base.DrawAt(pos, flip);
            // Ground nests are printed into the map mesh; their realtime
            // pass draws only the contained eggs/chicks, not a second copy
            // of the nest texture.
            DrawNestContents(pos, wind);
        }

        private void DrawNestContents(Vector3 origin, Mesh wind)
        {
            Thing egg = this.TryGetComp<CompEggContainer>()?.ContainedThing;
            if (egg != null && !egg.Destroyed)
            {
                Vector3 p = origin + new Vector3(0f, 0.027f, 0.025f);
                if (wind != null && egg.Graphic.MatSingle.mainTexture is Texture2D tex)
                {
                    Material eggMaterial = egg.Graphic.MatSingle;
                    if (fertilizedEggWindMaterial == null
                        || originalEggMaterial != eggMaterial)
                    {
                        originalEggMaterial = eggMaterial;
                        fertilizedEggWindMaterial = MaterialPool.MatFrom(
                            tex, ShaderDatabase.CutoutPlant, egg.DrawColor);
                    }
                    Graphics.DrawMesh(wind, Matrix4x4.TRS(p, Quaternion.identity,
                        new Vector3(0.20f, 1f, 0.20f)),
                        fertilizedEggWindMaterial, 0);
                }
                else
                    egg.Graphic.Draw(p, Rot4.North, egg);
            }

            // Render genuine held chicks with the same pawn render tree used
            // by Zoology's animal barns. Display count is bounded; the owner
            // and physiological tick still contain every real chick.
            int shown = 0;
            for (int i = 0; i < young.Count && shown < 4; i++)
            {
                if (young[i] is not Pawn baby || baby.Dead || baby.Destroyed)
                    continue;

                float x = shown % 2 == 0 ? -0.09f : 0.09f;
                float z = shown / 2 == 0 ? -0.035f : 0.045f;
                Vector3 location = origin + new Vector3(x, 0.04f + shown * 0.005f, z);
#if RIMWORLD17
                PawnRenderer renderer = baby.Drawer.renderer;
                renderer.EnsureGraphicsInitialized();
                var parms = new PawnDrawParms
                {
                    pawn = baby,
                    matrix = Matrix4x4.TRS(
                        location + baby.ageTracker.CurLifeStage.bodyDrawOffset,
                        Quaternion.identity, Vector3.one),
                    facing = Rot4.South,
                    rotDrawMode = renderer.CurRotDrawMode,
                    posture = baby.GetPosture(),
                    flags = PawnRenderFlags.NeverAimWeapon
                        | (baby.IsPsychologicallyInvisible()
                            ? PawnRenderFlags.Invisible : PawnRenderFlags.None)
                        | (!baby.health.hediffSet.HasHead
                            ? PawnRenderFlags.HeadStump : PawnRenderFlags.None),
                    tint = renderer.flasher.CurColor.ToTransparent(
                        InvisibilityUtility.GetAlpha(baby)),
                    coveredInFoam = renderer.FirefoamOverlays.coveredInFoam,
                    dead = false
                };
                renderer.renderTree.ParallelPreDraw(parms);
                renderer.renderTree.Draw(parms);
#else
                baby.DrawAt(location);
#endif
                shown++;
            }
        }
    }
}
