import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import pandas as pd

from core.animalstats_source import generation_input_snapshot, read_excel_cached, read_google_sheet


class InputSnapshotTests(unittest.TestCase):
    def test_google_reads_are_scoped_to_generation(self):
        source = "gsheet:abcdefghijklmnopqrstuv"

        @generation_input_snapshot
        def run():
            first = read_google_sheet(source, "Animals")
            first.iloc[0, 0] = "modified"
            second = read_google_sheet(source, "Animals")
            self.assertEqual(second.iloc[0, 0], "A")
            return second

        with patch("core.animalstats_source._fetch_google_values", return_value=[["DefName"], ["A"]]) as fetch:
            run()
            self.assertEqual(fetch.call_count, 1)
            run()
            self.assertEqual(fetch.call_count, 2)
            read_google_sheet(source, "Animals")
            self.assertEqual(fetch.call_count, 3)

    def test_excel_cached_frames_are_independent(self):
        with tempfile.TemporaryDirectory() as td:
            source = Path(td) / "test.xlsx"
            pd.DataFrame({"DefName": ["A"]}).to_excel(source, index=False)

            @generation_input_snapshot
            def run():
                a = read_excel_cached(source, dtype=str)
                a.iloc[0, 0] = "modified"
                b = read_excel_cached(source, dtype=str)
                self.assertEqual(b.iloc[0, 0], "A")

            run()
            run()

    def test_snapshot_reset_after_error(self):
        source = "gsheet:abcdefghijklmnopqrstuv"

        @generation_input_snapshot
        def run():
            read_google_sheet(source, "Animals")
            raise ValueError("generation failed")

        with patch("core.animalstats_source._fetch_google_values", return_value=[["DefName"], ["A"]]) as fetch:
            with self.assertRaises(ValueError):
                run()
            read_google_sheet(source, "Animals")
            self.assertEqual(fetch.call_count, 2)


if __name__ == "__main__":
    unittest.main()
