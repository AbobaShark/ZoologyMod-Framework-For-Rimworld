#if RIMWORLD17
using RimWorld;
using UnityEngine;
using Verse;

namespace ZoologyMod
{
    // Use the same ThingFilter interface, category tree and male/female
    // special filters as the vanilla animal pen marker.
    public sealed class ITab_BarnAnimals : ITab
    {
        private static readonly Vector2 WindowSize = new Vector2(300f, 480f);
        private readonly ThingFilterUI.UIState filterState = new ThingFilterUI.UIState();

        private Building_AnimalBarn SelectedBarn => SelThing as Building_AnimalBarn;

        public ITab_BarnAnimals()
        {
            size = WindowSize;
            labelKey = "Zoology_BarnAnimalTab";
        }

        public override bool IsVisible => SelectedBarn?.Faction == Faction.OfPlayer;

        public override void OnOpen()
        {
            base.OnOpen();
            filterState.quickSearch.Reset();
        }

        protected override void FillTab()
        {
            Building_AnimalBarn barn = SelectedBarn;
            if (barn == null)
            {
                return;
            }

            Rect rect = GenUI.ContractedBy(new Rect(0f, 0f, WindowSize.x, WindowSize.y), 10f);
            ThingFilterUI.DoThingFilterConfigWindow(rect, filterState,
                barn.AnimalFilter, AnimalPenUtility.GetFixedAnimalFilter(),
                openMask: 1,
                forceHiddenFilters: AnimalFilterVisibility.HiddenAdultFemaleFilters(barn.AnimalFilter),
                map: barn.Map);
        }
    }
}
#endif
