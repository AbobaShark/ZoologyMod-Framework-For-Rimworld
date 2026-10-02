import json

import pandas as pd

import animalstats_source as src
import rimworld_xml_generator as generator
from rimworld_patch_fixer import PatchGenerator


SHEET_ID = "1BsPzRPFLFx2HL4UdlVo058kryub3C4a9ezQ54CnGEB4"
SHEET_URL = f"https://docs.google.com/spreadsheets/d/{SHEET_ID}/edit#gid=0"


def test_google_sheet_url_and_gsheet_id_are_recognized():
    assert src.google_spreadsheet_id(SHEET_URL) == SHEET_ID
    assert src.google_spreadsheet_id(f"gsheet:{SHEET_ID}") == SHEET_ID
    assert src.is_google_sheet_source(SHEET_URL)
    assert src.is_multisheet_source(SHEET_URL)
    assert src.source_available(SHEET_URL)


def test_drive_for_desktop_pointer_is_recognized(tmp_path):
    pointer = tmp_path / "AnimalStats.gsheet"
    pointer.write_text(json.dumps({"url": SHEET_URL, "doc_id": SHEET_ID}), encoding="utf-8")
    assert src.google_spreadsheet_id(pointer) == SHEET_ID
    assert src.source_available(pointer)


def test_values_to_dataframe_trims_empty_columns_and_names_blank_headers():
    values = [
        ["Common name", "", "XML name", ""],
        ["Rat", "Rattus norvegicus", "Rat", ""],
    ]
    df = src._values_to_dataframe(values)
    assert list(df.columns) == ["Common name", "Column B", "XML name"]
    assert df.iloc[0].tolist() == ["Rat", "Rattus norvegicus", "Rat"]


def test_generator_reads_google_source_via_common_loader(monkeypatch):
    expected = pd.DataFrame(
        [["Rat", "Rattus norvegicus", "<li>Rat</li>"]],
        columns=["Common name", "Latin name", "XML name"],
    )
    monkeypatch.setattr(generator, "read_google_sheet", lambda source, sheet_name: expected.copy())
    actual = generator.read_table(SHEET_URL, sheet_name="Animals")
    assert actual.to_dict("records") == expected.to_dict("records")


def test_patch_generator_loads_google_source_via_common_loader(monkeypatch):
    expected = pd.DataFrame(
        [["Rat", "Rattus norvegicus", "<li>Rat</li>"]],
        columns=["Common name", "Latin name", "XML name"],
    )
    import rimworld_patch_fixer as fixer

    monkeypatch.setattr(fixer, "read_google_sheet", lambda source, sheet_name: expected.copy())
    loader = PatchGenerator.__new__(PatchGenerator)
    actual = loader._load_table(SHEET_URL, sheet_name="Animals", required=True)
    assert actual.to_dict("records") == expected.to_dict("records")
