#if RIMWORLD17
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace ZoologyMod
{
    // The native transporter cargo inspect tab, backed by the barn's own
    // ThingOwner and manual loading requests instead of a CompTransporter.
    // Reuses the vanilla cargo rows, item information and drop controls.
    public sealed class ITab_BarnContents : ITab_ContentsBase
    {
        private static readonly IList<Thing> EmptyContents = new List<Thing>();

        private readonly List<Pawn> queuedSnapshot = new List<Pawn>();
        private readonly List<Thing> singleThing = new List<Thing>(1);

        private Building_AnimalBarn Barn => SelThing as Building_AnimalBarn;

        public override IList<Thing> container => Barn?.StoredContents ?? EmptyContents;

        public override bool IsVisible => Barn?.Faction == Faction.OfPlayer;

        public override bool UseDiscardMessage => false;

        public ITab_BarnContents()
        {
            labelKey = "TabTransporterContents";
            containedItemsKey = "ContainedItems";
        }

        protected override void DoItemsLists(Rect inRect, ref float curY)
        {
            Building_AnimalBarn barn = Barn;
            if (barn == null)
            {
                return;
            }

            // Match the two-column layout of ITab_ContentsTransporter:
            // pending loading orders on the left, real contents on the right.
            float width = (inRect.width - 10f) / 2f;
            Rect pendingRect = new Rect(0f, curY, width, inRect.height);
            float pendingY = 0f;

            queuedSnapshot.Clear();
            foreach (Pawn pawn in barn.PendingManualIntake)
            {
                if (pawn != null && !pawn.Destroyed)
                {
                    queuedSnapshot.Add(pawn);
                }
            }

            Widgets.BeginGroup(pendingRect);
            Widgets.ListSeparator(ref pendingY, width, "ItemsToLoad".Translate());

            if (queuedSnapshot.Count == 0)
            {
                Widgets.NoneLabel(ref pendingY, width);
            }
            else
            {
                for (int i = 0; i < queuedSnapshot.Count; i++)
                {
                    Pawn selected = queuedSnapshot[i];
                    singleThing.Clear();
                    singleThing.Add(selected);
                    DoThingRow(selected.def, 1, singleThing, width, ref pendingY,
                        _ => barn.CancelManualIntake(selected));
                }
                singleThing.Clear();
            }

            Widgets.EndGroup();

            Rect contentsRect = new Rect((inRect.width + 10f) / 2f,
                curY, width, inRect.height);
            float contentsY = 0f;
            base.DoItemsLists(contentsRect, ref contentsY);
            curY += Mathf.Max(pendingY, contentsY);
        }

        protected override void OnDropThing(Thing thing, int count)
        {
            Building_AnimalBarn barn = Barn;
            if (barn == null || thing == null || count <= 0)
            {
                return;
            }

            if (thing is Pawn animal)
            {
                // The barn releases the animal normally. Vanilla and
                // Zoology handlers decide where it should be kept.
                barn.TryReleaseAnimal(animal);
            }
            else
            {
                barn.TryDropStoredItem(thing, count);
            }
        }
    }
}
#endif
