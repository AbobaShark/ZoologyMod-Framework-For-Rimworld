# AnimalStats sources

The Patch Generator (`rimworld_patch_generator.py`, GUI without arguments or CLI with arguments) can read one common Google Sheets URL/ID or local workbook. Optional per-sheet overrides accept another URL, workbook or a TSV for each required tab. Use the tab names shown in the GUI (`Animals`, `Animals CE`, `Bionics`, `LifeStages`, `EggMilkWool`, `Venom profiles`, `Biomes`). The `rimworld_xml_generator.py` Def generator retains its original source behavior.

Google Sheets API uses read-only access. For desktop OAuth, place your own `google_credentials.json` in `checker/` or set `ANIMALSTATS_GOOGLE_CREDENTIALS`; the authorized token is stored in `google_token.json` or `ANIMALSTATS_GOOGLE_TOKEN`. Both filenames are ignored by Git and excluded from distributed packages. Service-account credentials also work when the spreadsheet is shared with that account. Local XLSX/TSV sources need no API credentials.

Versioned project configuration is in `rimworld_patch_generator_config.json` and `generation.json`. Machine-specific paths, source URLs and preferences are stored only in ignored `checker.local.json`.
