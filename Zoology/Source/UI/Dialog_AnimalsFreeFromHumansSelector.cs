using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace ZoologyMod
{
    internal sealed class Dialog_AnimalsFreeFromHumansSelector : Window
    {
        private const float RowHeight = 28f;
        private const float SearchHeight = 30f;
        private const float HeaderHeight = 54f;
        private const float FooterHeight = 30f;
        private const float ColumnGap = 16f;
        private const float RowIconSize = 24f;
        private const float RowInfoButtonSize = 24f;
        private const float RowHorizontalPadding = 6f;

        private static List<ThingDef> cachedAnimalDefs;

        private readonly ZoologyModSettings settings;
        private readonly List<ThingDef> fleeAnimals = new List<ThingDef>(128);
        private readonly List<ThingDef> noFleeAnimals = new List<ThingDef>(64);

        private Vector2 fleeScrollPosition = Vector2.zero;
        private Vector2 noFleeScrollPosition = Vector2.zero;
        private string fleeSearch = string.Empty;
        private string noFleeSearch = string.Empty;

        public Dialog_AnimalsFreeFromHumansSelector(ZoologyModSettings settings)
        {
            this.settings = settings;
            draggable = true;
            doCloseX = true;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = true;
        }

        public override Vector2 InitialSize => new Vector2(1080f, 720f);

        public override void PostClose()
        {
            base.PostClose();
            settings?.Write();
        }

        public override void DoWindowContents(Rect inRect)
        {
            BuildAnimalLists();

            Rect headerRect = new Rect(inRect.x, inRect.y, inRect.width, HeaderHeight);
            Widgets.Label(new Rect(headerRect.x, headerRect.y, headerRect.width - 170f, 26f), "Animals fleeing from humans");
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(headerRect.x, headerRect.y + 24f, headerRect.width - 170f, 26f), "Click an animal to move it to the opposite list. By default, insectoids, photonozoa, thrumbo-like, no-flee, and carrier-flee animals stay on the right.");
            Text.Font = GameFont.Small;

            Rect resetRect = new Rect(headerRect.xMax - 160f, headerRect.y, 160f, 32f);
            if (Widgets.ButtonText(resetRect, "Reset defaults"))
            {
                settings.ResetAnimalsFreeFromHumansOverrides();
                fleeScrollPosition = Vector2.zero;
                noFleeScrollPosition = Vector2.zero;
            }

            float columnsTop = headerRect.yMax + 8f;
            float columnsHeight = inRect.height - HeaderHeight - FooterHeight - 8f;
            float columnWidth = (inRect.width - ColumnGap) / 2f;
            Rect leftRect = new Rect(inRect.x, columnsTop, columnWidth, columnsHeight);
            Rect rightRect = new Rect(leftRect.xMax + ColumnGap, columnsTop, columnWidth, columnsHeight);

            DrawColumn(leftRect, "Will flee", fleeAnimals, ref fleeSearch, ref fleeScrollPosition, false);
            DrawColumn(rightRect, "Will not flee", noFleeAnimals, ref noFleeSearch, ref noFleeScrollPosition, true);

            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(inRect.x, inRect.yMax - FooterHeight, inRect.width, FooterHeight), $"Animals: flee {fleeAnimals.Count}, do not flee {noFleeAnimals.Count}");
            Text.Font = GameFont.Small;
        }

        private void BuildAnimalLists()
        {
            fleeAnimals.Clear();
            noFleeAnimals.Clear();

            List<ThingDef> defs = GetAnimalDefs();
            for (int i = 0; i < defs.Count; i++)
            {
                ThingDef def = defs[i];
                if (settings.GetAnimalsFreeFromHumansFor(def))
                {
                    fleeAnimals.Add(def);
                }
                else
                {
                    noFleeAnimals.Add(def);
                }
            }
        }

        private void DrawColumn(Rect rect, string title, List<ThingDef> animals, ref string searchText, ref Vector2 scrollPosition, bool moveToFlee)
        {
            Widgets.DrawMenuSection(rect);

            Rect innerRect = rect.ContractedBy(8f);
            Widgets.Label(new Rect(innerRect.x, innerRect.y, innerRect.width, 24f), $"{title} ({animals.Count})");

            Rect searchRect = new Rect(innerRect.x, innerRect.y + 28f, innerRect.width, SearchHeight);
            searchText = Widgets.TextField(searchRect, searchText ?? string.Empty);

            Rect listRect = new Rect(innerRect.x, searchRect.yMax + 8f, innerRect.width, innerRect.height - 72f);
            int visibleCount = CountMatchingAnimals(animals, searchText);
            float viewHeight = Mathf.Max(listRect.height - 4f, visibleCount * RowHeight);
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, viewHeight);

            Widgets.BeginScrollView(listRect, ref scrollPosition, viewRect);

            float y = 0f;
            int visibleIndex = 0;
            for (int i = 0; i < animals.Count; i++)
            {
                ThingDef def = animals[i];
                if (!MatchesSearch(def, searchText))
                {
                    continue;
                }

                Rect rowRect = new Rect(0f, y, viewRect.width, RowHeight - 2f);
                DrawAnimalRow(rowRect, def, visibleIndex, moveToFlee);

                y += RowHeight;
                visibleIndex++;
            }

            if (visibleIndex == 0)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(0f, 0f, viewRect.width, RowHeight), "No animals matched the search.");
                Text.Anchor = TextAnchor.UpperLeft;
            }

            Widgets.EndScrollView();
        }

        private void DrawAnimalRow(Rect rowRect, ThingDef def, int visibleIndex, bool moveToFlee)
        {
            if (visibleIndex % 2 == 1)
            {
                Widgets.DrawLightHighlight(rowRect);
            }

            Rect infoRect = new Rect(
                rowRect.xMax - RowInfoButtonSize,
                rowRect.y + (rowRect.height - RowInfoButtonSize) * 0.5f,
                RowInfoButtonSize,
                RowInfoButtonSize);
            Rect rowButtonRect = new Rect(rowRect.x, rowRect.y, infoRect.x - rowRect.x - 4f, rowRect.height);

            if (Mouse.IsOver(rowButtonRect))
            {
                Widgets.DrawHighlight(rowButtonRect);
            }

            if (Widgets.ButtonInvisible(rowButtonRect))
            {
                settings.SetAnimalsFreeFromHumansFor(def, moveToFlee);
            }

            Rect iconRect = new Rect(
                rowButtonRect.x + RowHorizontalPadding,
                rowButtonRect.y + (rowButtonRect.height - RowIconSize) * 0.5f,
                RowIconSize,
                RowIconSize);
            Widgets.DefIcon(iconRect, def, null, 1f, null, false, null, null, null, 1f);

            Rect labelRect = new Rect(
                iconRect.xMax + 8f,
                rowButtonRect.y,
                Mathf.Max(0f, rowButtonRect.xMax - iconRect.xMax - 12f),
                rowButtonRect.height);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(labelRect, GetAnimalLabel(def));
            Text.Anchor = TextAnchor.UpperLeft;

            TooltipHandler.TipRegion(rowButtonRect, $"{GetAnimalLabel(def)}\n{def.defName}");
            Widgets.InfoCardButton(infoRect, def);
        }

        private static int CountMatchingAnimals(List<ThingDef> animals, string searchText)
        {
            if (animals == null || animals.Count == 0)
            {
                return 0;
            }

            if (string.IsNullOrWhiteSpace(searchText))
            {
                return animals.Count;
            }

            int count = 0;
            for (int i = 0; i < animals.Count; i++)
            {
                if (MatchesSearch(animals[i], searchText))
                {
                    count++;
                }
            }

            return count;
        }

        private static bool MatchesSearch(ThingDef def, string searchText)
        {
            if (def == null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(searchText))
            {
                return true;
            }

            string label = GetAnimalLabel(def);
            return label.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0
                || def.defName.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetAnimalLabel(ThingDef def)
        {
            if (def == null)
            {
                return "Unknown";
            }

            return def.label.NullOrEmpty() ? def.defName : def.LabelCap.RawText;
        }

        private static List<ThingDef> GetAnimalDefs()
        {
            if (cachedAnimalDefs != null)
            {
                return cachedAnimalDefs;
            }

            cachedAnimalDefs = new List<ThingDef>();
            List<ThingDef> allDefs = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < allDefs.Count; i++)
            {
                ThingDef def = allDefs[i];
                if (ZoologyCacheUtility.IsAnimalThingDef(def))
                {
                    cachedAnimalDefs.Add(def);
                }
            }

            cachedAnimalDefs.Sort((left, right) => string.Compare(GetAnimalLabel(left), GetAnimalLabel(right), StringComparison.OrdinalIgnoreCase));
            return cachedAnimalDefs;
        }
    }
}
