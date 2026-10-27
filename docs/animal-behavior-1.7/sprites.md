# Спрайты Zoology

Созданы встроенным ImageGen, прозрачный фон. Финальная подготовка: RGBA, LANCZOS до игрового размера, PNG optimize; альфа сохранена. Просмотрены на светлом и тёмном фоне в `sprites-preview.png`.

| Назначение | Файл относительно Zoology | Размер |
|---|---|---|
| Образцы яда, три оттенка в Defs | `Textures/Things/Item/ZoologyVenom/VenomSample.png` | 64×64 |
| Противоядия, три оттенка в Defs | `Textures/Things/Item/ZoologyVenom/Antivenom.png` | 64×64 |
| Нора, footprint и drawSize 1×1 | `Textures/Things/Building/Zoology/AnimalBurrow.png` | 128×128 |

Промпты генерации (передают использованный набор требований):

1. Venom: flat vanilla RimWorld inventory sprite; squat sealed glass bottle, dark rubber stopper, a single amber drop; charcoal outline, muted two or three flat tones, orthographic slight top-down view; centered, occupies 70% of square; transparent background; no ground, text, glow or 3D render; readable at 64 px.
2. Antivenom: squat opaque off-white medicine ampoule, sage stripe, dark teal cap and green medical cross; same flat vanilla RimWorld inventory style, charcoal outline, very few muted colors; centered, readable at 64 px; transparent background, no text or cast shadow.
3. Burrow: natural oval entrance with charcoal hole, muted brown excavated rim and three clods; flat vanilla RimWorld object sprite, slight top-down/front view; fills about 75% width and 55% height; transparent background, readable at 128 px; no grass, rectangular ground tile, grave, coffin, cave scenery or text.

Большие исходники ImageGen сохранены в `C:/Users/User/.codex/generated_images/01a17876-e929-74c3-af22-67b82b088edf/`:
`exec-fb459535-2491-42a8-9dea-58c1dd7c0e26.png`, `exec-59da037d-366d-4c79-8b1a-f352316657ae.png`, `exec-dc74a19d-4360-47b1-9843-86de9f16b4e1.png`.
