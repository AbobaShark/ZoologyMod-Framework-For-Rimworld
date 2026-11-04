#if RIMWORLD17
using UnityEngine;
using Verse;

namespace ZoologyMod
{
    // The barn's foreground beam is deliberately drawn above its animals.
    // Its normal DrawAt overlay is not included in RimWorld's stock ghost.
    // Render that same layer for construction previews, without altering
    // the building's normal renderer or changing its textures.
    public sealed class PlaceWorker_AnimalBarnFront : PlaceWorker
    {
        private const string FrontPath = "Things/Building/Zoology/AnimalBarnFront";

        public override void DrawGhost(
            ThingDef def, IntVec3 center, Rot4 rot, Color ghostCol,
            Thing thing = null)
        {
            if (def?.graphicData == null)
                return;

            Graphic foreground = GraphicDatabase.Get<Graphic_Single>(
                FrontPath, ShaderDatabase.Cutout,
                def.graphicData.drawSize, Color.white);

            // Use vanilla's own ghost shader/tint so the extra wall has
            // the same valid/invalid colors as the rest of the outline.
            Graphic ghost = GhostUtility.GhostGraphicFor(
                foreground, def, ghostCol, thing?.Stuff);

            // Align with the occupied rectangle at all rotations,
            // including the half-cell offset of the even 3x6 footprint.
            CellRect occupied = GenAdj.OccupiedRect(center, rot, def.size);
            Vector3 drawPos = new Vector3(
                (occupied.minX + occupied.maxX + 1f) * 0.5f,
                AltitudeLayer.Blueprint.AltitudeFor() + 0.01f,
                (occupied.minZ + occupied.maxZ + 1f) * 0.5f);

            // Designator_Build supplies null for "build copy" and for the
            // normal Architect preview. Graphic.Draw dereferences thing.def
            // in vanilla; DrawFromDef is its null-safe counterpart.
            ghost.DrawFromDef(drawPos, rot, def);
        }
    }
}
#endif
