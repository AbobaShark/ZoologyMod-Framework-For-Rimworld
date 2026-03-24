using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace ZoologyMod
{
    internal sealed class Dialog_AnimalRuntimeFeatureSelector : Window
    {
        private const float SimpleRowHeight = 28f;
        private const float ParameterRowHeightFallback = 140f;
        private const float SearchHeight = 30f;
        private const float HeaderHeight = 58f;
        private const float FooterHeight = 30f;
        private const float ColumnGap = 16f;
        private const float RowIconSize = 24f;
        private const float RowInfoButtonSize = 24f;
        private const float RowHorizontalPadding = 6f;
        private const float SliderLabelHeight = 20f;
        private const float SliderHeight = 22f;
        private const float SliderGap = 6f;

        private readonly ZoologyModSettings settings;
        private readonly RuntimeAnimalFeatureDefinition feature;
        private readonly List<ThingDef> enabledAnimals = new List<ThingDef>(128);
        private readonly List<ThingDef> disabledAnimals = new List<ThingDef>(128);

        private Vector2 enabledScrollPosition = Vector2.zero;
        private Vector2 disabledScrollPosition = Vector2.zero;
        private string enabledSearch = string.Empty;
        private string disabledSearch = string.Empty;

        public Dialog_AnimalRuntimeFeatureSelector(ZoologyModSettings settings, RuntimeAnimalFeatureDefinition feature)
        {
            this.settings = settings;
            this.feature = feature;
            draggable = true;
            doCloseX = true;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = true;
        }

        public override Vector2 InitialSize => new Vector2(1080f, 720f);

        public override void DoWindowContents(Rect inRect)
        {
            BuildAnimalLists();

            Rect headerRect = new Rect(inRect.x, inRect.y, inRect.width, HeaderHeight);
            Widgets.Label(new Rect(headerRect.x, headerRect.y, headerRect.width - 170f, 26f), $"{feature.Label} species");
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(headerRect.x, headerRect.y + 24f, headerRect.width - 170f, 26f), "Click an animal to move it to the opposite list.");
            Text.Font = GameFont.Small;

            Rect resetRect = new Rect(headerRect.xMax - 160f, headerRect.y, 160f, 32f);
            if (Widgets.ButtonText(resetRect, "Reset defaults"))
            {
                settings.ResetAnimalFeatureOverrides(feature.Id);
                enabledScrollPosition = Vector2.zero;
                disabledScrollPosition = Vector2.zero;
            }

            float columnsTop = headerRect.yMax + 8f;
            float columnsHeight = inRect.height - HeaderHeight - FooterHeight - 8f;
            float columnWidth = (inRect.width - ColumnGap) / 2f;
            Rect leftRect = new Rect(inRect.x, columnsTop, columnWidth, columnsHeight);
            Rect rightRect = new Rect(leftRect.xMax + ColumnGap, columnsTop, columnWidth, columnsHeight);

            DrawColumn(leftRect, feature.EnabledColumnLabel, enabledAnimals, ref enabledSearch, ref enabledScrollPosition, false);
            DrawColumn(rightRect, feature.DisabledColumnLabel, disabledAnimals, ref disabledSearch, ref disabledScrollPosition, true);

            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(inRect.x, inRect.yMax - FooterHeight, inRect.width, FooterHeight), $"Animals: enabled {enabledAnimals.Count}, disabled {disabledAnimals.Count}");
            Text.Font = GameFont.Small;
        }

        private void BuildAnimalLists()
        {
            enabledAnimals.Clear();
            disabledAnimals.Clear();

            IReadOnlyList<ThingDef> defs = ZoologyRuntimeAnimalOverrides.GetAnimalDefs();
            for (int i = 0; i < defs.Count; i++)
            {
                ThingDef def = defs[i];
                if (settings.GetAnimalFeatureEnabled(feature.Id, def))
                {
                    enabledAnimals.Add(def);
                }
                else
                {
                    disabledAnimals.Add(def);
                }
            }
        }

        private void DrawColumn(Rect rect, string title, List<ThingDef> animals, ref string searchText, ref Vector2 scrollPosition, bool moveToEnabled)
        {
            Widgets.DrawMenuSection(rect);

            Rect innerRect = rect.ContractedBy(8f);
            Widgets.Label(new Rect(innerRect.x, innerRect.y, innerRect.width, 24f), $"{title} ({animals.Count})");

            Rect searchRect = new Rect(innerRect.x, innerRect.y + 28f, innerRect.width, SearchHeight);
            searchText = Widgets.TextField(searchRect, searchText ?? string.Empty);

            Rect listRect = new Rect(innerRect.x, searchRect.yMax + 8f, innerRect.width, innerRect.height - 72f);
            float rowHeight = GetRowHeight(moveToEnabled);
            int visibleCount = CountMatchingAnimals(animals, searchText);
            float viewHeight = Mathf.Max(listRect.height - 4f, visibleCount * rowHeight);
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

                Rect rowRect = new Rect(0f, y, viewRect.width, rowHeight - 2f);
                DrawAnimalRow(rowRect, def, visibleIndex, moveToEnabled);

                y += rowHeight;
                visibleIndex++;
            }

            if (visibleIndex == 0)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(0f, 0f, viewRect.width, rowHeight), "No animals matched the search.");
                Text.Anchor = TextAnchor.UpperLeft;
            }

            Widgets.EndScrollView();
        }

        private void DrawAnimalRow(Rect rowRect, ThingDef def, int visibleIndex, bool moveToEnabled)
        {
            if (visibleIndex % 2 == 1)
            {
                Widgets.DrawLightHighlight(rowRect);
            }

            bool showParameterControls = !moveToEnabled && HasParameterControls(feature.Id);
            Rect infoRect = new Rect(
                rowRect.xMax - RowInfoButtonSize,
                rowRect.y + 2f,
                RowInfoButtonSize,
                RowInfoButtonSize);
            Rect controlsRect = Rect.zero;
            Rect rowButtonRect;
            if (showParameterControls)
            {
                float controlsWidth = Mathf.Min(340f, rowRect.width * 0.48f);
                controlsRect = new Rect(
                    infoRect.x - controlsWidth - 8f,
                    rowRect.y + 4f,
                    controlsWidth,
                    rowRect.height - 8f);
                rowButtonRect = new Rect(rowRect.x, rowRect.y, controlsRect.x - rowRect.x - 4f, rowRect.height);
            }
            else
            {
                infoRect.y = rowRect.y + (rowRect.height - RowInfoButtonSize) * 0.5f;
                rowButtonRect = new Rect(rowRect.x, rowRect.y, infoRect.x - rowRect.x - 4f, rowRect.height);
            }

            if (Mouse.IsOver(rowButtonRect))
            {
                Widgets.DrawHighlight(rowButtonRect);
            }

            if (Widgets.ButtonInvisible(rowButtonRect))
            {
                settings.SetAnimalFeatureEnabled(feature.Id, def, moveToEnabled);
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

            if (showParameterControls)
            {
                DrawParameterControls(controlsRect, def);
            }

            TooltipHandler.TipRegion(rowButtonRect, $"{GetAnimalLabel(def)}\n{def.defName}");
            Widgets.InfoCardButton(infoRect, def);
        }

        private float GetRowHeight(bool moveToEnabled)
        {
            if (moveToEnabled || !HasParameterControls(feature.Id))
            {
                return SimpleRowHeight;
            }

            return GetParameterControlRowHeight(feature.Id);
        }

        private static bool HasParameterControls(string featureId)
        {
            return string.Equals(featureId, "modext_scavenger", StringComparison.Ordinal)
                || string.Equals(featureId, "modext_flee_from_carrier", StringComparison.Ordinal)
                || string.Equals(featureId, "comp_ageless", StringComparison.Ordinal)
                || string.Equals(featureId, "comp_drugs_immune", StringComparison.Ordinal)
                || string.Equals(featureId, "comp_animal_clotting", StringComparison.Ordinal);
        }

        private static float GetParameterControlRowHeight(string featureId)
        {
            if (string.Equals(featureId, "modext_scavenger", StringComparison.Ordinal))
            {
                return 40f;
            }

            if (string.Equals(featureId, "comp_ageless", StringComparison.Ordinal)
                || string.Equals(featureId, "comp_drugs_immune", StringComparison.Ordinal))
            {
                return 64f;
            }

            if (string.Equals(featureId, "modext_flee_from_carrier", StringComparison.Ordinal)
                || string.Equals(featureId, "comp_animal_clotting", StringComparison.Ordinal))
            {
                return 154f;
            }

            return ParameterRowHeightFallback;
        }

        private void DrawParameterControls(Rect controlsRect, ThingDef def)
        {
            float y = controlsRect.y + 2f;
            if (string.Equals(feature.Id, "modext_scavenger", StringComparison.Ordinal))
            {
                bool originalAllowVeryRotten = settings.GetScavengerAllowVeryRottenFor(def);
                bool allowVeryRotten = originalAllowVeryRotten;
                Rect checkboxRect = new Rect(controlsRect.x, y, controlsRect.width, 24f);
                Widgets.CheckboxLabeled(checkboxRect, "Allow very rotten", ref allowVeryRotten);
                if (allowVeryRotten != originalAllowVeryRotten)
                {
                    settings.SetScavengerAllowVeryRottenFor(def, allowVeryRotten);
                }

                return;
            }

            if (string.Equals(feature.Id, "modext_flee_from_carrier", StringComparison.Ordinal))
            {
                float radius = settings.GetFleeFromCarrierRadiusFor(def);
                float newRadius = DrawLabeledSlider(
                    controlsRect,
                    ref y,
                    "Radius",
                    radius,
                    1f,
                    60f,
                    "1",
                    "60",
                    0.1f,
                    1);
                if (ShouldCommitFloatSliderChange(radius, newRadius, 0.0001f))
                {
                    settings.SetFleeFromCarrierRadiusFor(def, newRadius);
                }

                float bodySizeLimit = settings.GetFleeFromCarrierBodySizeLimitFor(def);
                float newBodySizeLimit = DrawLabeledSlider(
                    controlsRect,
                    ref y,
                    "Body size limit",
                    bodySizeLimit,
                    0f,
                    20f,
                    "0",
                    "20",
                    0.1f,
                    1);
                if (ShouldCommitFloatSliderChange(bodySizeLimit, newBodySizeLimit, 0.0001f))
                {
                    settings.SetFleeFromCarrierBodySizeLimitFor(def, newBodySizeLimit);
                }

                int fleeDistance = settings.GetFleeFromCarrierDistanceFor(def);
                int newDistance = DrawLabeledIntSlider(
                    controlsRect,
                    ref y,
                    "Distance",
                    fleeDistance,
                    1,
                    80,
                    "1",
                    "80");
                if (ShouldCommitIntSliderChange(fleeDistance, newDistance))
                {
                    settings.SetFleeFromCarrierDistanceFor(def, newDistance);
                }

                return;
            }

            if (string.Equals(feature.Id, "comp_ageless", StringComparison.Ordinal))
            {
                int interval = settings.GetAgelessCleanupIntervalTicksFor(def);
                DrawSingleIntervalSlider(controlsRect, ref y, "Cleanup interval", interval, 60, 120000, settings.SetAgelessCleanupIntervalTicksFor, def);
                return;
            }

            if (string.Equals(feature.Id, "comp_drugs_immune", StringComparison.Ordinal))
            {
                int interval = settings.GetDrugsImmuneCleanupIntervalTicksFor(def);
                DrawSingleIntervalSlider(controlsRect, ref y, "Cleanup interval", interval, 60, 120000, settings.SetDrugsImmuneCleanupIntervalTicksFor, def);
                return;
            }

            if (string.Equals(feature.Id, "comp_animal_clotting", StringComparison.Ordinal))
            {
                int checkInterval = settings.GetAnimalClottingCheckIntervalFor(def);
                int newCheckInterval = DrawLabeledIntSlider(
                    controlsRect,
                    ref y,
                    "Check interval",
                    checkInterval,
                    60,
                    120000,
                    "60",
                    "120000");
                if (ShouldCommitIntSliderChange(checkInterval, newCheckInterval))
                {
                    settings.SetAnimalClottingCheckIntervalFor(def, newCheckInterval);
                }

                float minQuality = settings.GetAnimalClottingTendingMinFor(def);
                float newMinQuality = DrawLabeledSlider(
                    controlsRect,
                    ref y,
                    "Tend min",
                    minQuality,
                    0f,
                    2f,
                    "0",
                    "2",
                    0.01f,
                    2);
                if (ShouldCommitFloatSliderChange(minQuality, newMinQuality, 0.00001f))
                {
                    settings.SetAnimalClottingTendingMinFor(def, newMinQuality);
                }

                float maxQuality = settings.GetAnimalClottingTendingMaxFor(def);
                float newMaxQuality = DrawLabeledSlider(
                    controlsRect,
                    ref y,
                    "Tend max",
                    maxQuality,
                    0f,
                    2f,
                    "0",
                    "2",
                    0.01f,
                    2);
                if (ShouldCommitFloatSliderChange(maxQuality, newMaxQuality, 0.00001f))
                {
                    settings.SetAnimalClottingTendingMaxFor(def, newMaxQuality);
                }
            }
        }

        private void DrawSingleIntervalSlider(
            Rect controlsRect,
            ref float y,
            string label,
            int value,
            int min,
            int max,
            Action<ThingDef, int> setter,
            ThingDef def)
        {
            int newValue = DrawLabeledIntSlider(
                controlsRect,
                ref y,
                label,
                value,
                min,
                max,
                min.ToString(),
                max.ToString());
            if (ShouldCommitIntSliderChange(value, newValue))
            {
                setter(def, newValue);
            }
        }

        private static float DrawLabeledSlider(
            Rect controlsRect,
            ref float y,
            string label,
            float value,
            float min,
            float max,
            string leftLabel,
            string rightLabel,
            float roundStep,
            int decimals)
        {
            Rect labelRect = new Rect(controlsRect.x, y, controlsRect.width, SliderLabelHeight);
            Widgets.Label(labelRect, $"{label}: {value.ToString($"F{decimals}")}");
            y += SliderLabelHeight;

            Rect sliderRect = new Rect(controlsRect.x, y, controlsRect.width, SliderHeight);
            float raw = Widgets.HorizontalSlider(sliderRect, value, min, max, false, null, leftLabel, rightLabel, roundStep);
            y += SliderHeight + SliderGap;

            return RoundToStep(raw, roundStep);
        }

        private static int DrawLabeledIntSlider(
            Rect controlsRect,
            ref float y,
            string label,
            int value,
            int min,
            int max,
            string leftLabel,
            string rightLabel)
        {
            Rect labelRect = new Rect(controlsRect.x, y, controlsRect.width, SliderLabelHeight);
            Widgets.Label(labelRect, $"{label}: {value}");
            y += SliderLabelHeight;

            Rect sliderRect = new Rect(controlsRect.x, y, controlsRect.width, SliderHeight);
            float raw = Widgets.HorizontalSlider(sliderRect, value, min, max, false, null, leftLabel, rightLabel, 1f);
            y += SliderHeight + SliderGap;

            return Mathf.RoundToInt(raw);
        }

        private static float RoundToStep(float value, float step)
        {
            if (step <= 0f)
            {
                return value;
            }

            return Mathf.Round(value / step) * step;
        }

        private static bool ShouldCommitFloatSliderChange(float currentValue, float newValue, float epsilon)
        {
            return Mathf.Abs(newValue - currentValue) > epsilon && !IsMousePressedOrDragging();
        }

        private static bool ShouldCommitIntSliderChange(int currentValue, int newValue)
        {
            return currentValue != newValue && !IsMousePressedOrDragging();
        }

        private static bool IsMousePressedOrDragging()
        {
            Event evt = Event.current;
            if (evt == null)
            {
                return false;
            }

            EventType type = evt.type;
            EventType rawType = evt.rawType;
            return type == EventType.MouseDown
                || rawType == EventType.MouseDown
                || type == EventType.MouseDrag
                || rawType == EventType.MouseDrag;
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
    }
}
