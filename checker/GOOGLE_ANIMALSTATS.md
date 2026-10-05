# AnimalStats data sources

The checker accepts three source families through the common data-loading layer:

1. Local Excel: `.xlsx`, `.xlsm`, `.xls`.
2. Local TSV: `.tsv` (one table only).
3. Google Sheets: a spreadsheet URL, `gsheet:<spreadsheet-id>`, a raw spreadsheet ID, or a local `.gsheet` pointer created by Google Drive for desktop.

For this project the production workbook is `AnimalStats — WORKING`:

`https://docs.google.com/spreadsheets/d/1BsPzRPFLFx2HL4UdlVo058kryub3C4a9ezQ54CnGEB4/edit`

The standard animal-generation paths read the calculated `Animals` and `Animals CE` sheets. Google reads request calculated cell values. Local XLSX/TSV sources use the same downstream generation logic.

## Fastest local workflow

Google Sheets can be downloaded as **Microsoft Excel (.xlsx)** and used directly by both scripts, so TSV conversion is unnecessary for the normal local workflow. Point the generator/fixer source field at the downloaded workbook.

## Direct LIVE Google Sheets workflow

Direct access to a private Google Sheet needs Google authorization. The implementation uses a **read-only Google Sheets scope**.

### 1. Install the optional authentication packages

```bat
py -m pip install google-auth google-auth-oauthlib
```

These packages are only needed for Google Sheets mode. Local XLSX/TSV still works without them.

### 2. Create one Desktop OAuth client

In Google Cloud Console:

1. Create/select a project.
2. Enable **Google Sheets API**.
3. Configure the OAuth consent screen for your account.
4. Create an OAuth client of type **Desktop app**.
5. Download the client JSON and save it beside the Python scripts as:

`google_credentials.json`

You can instead set `ANIMALSTATS_GOOGLE_CREDENTIALS` to the JSON path.

### 3. Paste the Google Sheet URL into the existing source field

`rimworld_xml_generator.py`:

- put the URL in **AnimalStats source**.

`rimworld_patch_fixer.py`:

- put the URL in **AnimalStats source (TSV/XLSX/Google)**;
- leave **CE source** empty to read `Animals CE` from the same Google spreadsheet.

The first Google read opens the browser once for authorization. The resulting read-only refresh token is cached as:

`google_token.json`

You can instead set `ANIMALSTATS_GOOGLE_TOKEN` to a different token-cache path.

### CLI example

```bat
py rimworld_xml_generator.py ^
  --xlsx "https://docs.google.com/spreadsheets/d/1BsPzRPFLFx2HL4UdlVo058kryub3C4a9ezQ54CnGEB4/edit" ^
  --mode update ^
  --input-path "D:\path\to\xmls"
```

## Google Drive for desktop `.gsheet`

A `.gsheet` file stores a spreadsheet ID/URL pointer and no cell data. The checker resolves the pointer and uses the same read-only Google Sheets authorization described above.

## Service account (optional)

A service-account JSON is also accepted through `google_credentials.json` or `ANIMALSTATS_GOOGLE_CREDENTIALS`. Share the spreadsheet with that JSON's `client_email`. This avoids interactive browser login and is useful for automation/CI.

## Security

Do not commit or share `google_credentials.json` or `google_token.json`. They are authentication material. The checker requests only `spreadsheets.readonly`; it cannot edit AnimalStats through this integration.
