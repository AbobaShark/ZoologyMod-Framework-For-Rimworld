"""Regression tests for pasting Google Sheet sources into Tk entries."""
import tkinter as tk
from tkinter import ttk
import unittest

from core.checker_clipboard import enable_paste


class ClipboardEntryTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        try:
            cls.root = tk.Tk()
            cls.root.withdraw()
        except tk.TclError as exc:
            raise unittest.SkipTest(f'Tk display unavailable: {exc}')

    @classmethod
    def tearDownClass(cls):
        cls.root.destroy()

    def setUp(self):
        self.entry = ttk.Entry(self.root)
        self.entry.pack()
        self.calls = []
        self.paste = enable_paste(self.entry,
                                  on_change=lambda: self.calls.append(self.entry.get()))

    def tearDown(self):
        self.entry.destroy()

    def test_paste_google_url_and_persist_value(self):
        url = 'https://docs.google.com/spreadsheets/d/abcdef123456/edit'
        self.root.clipboard_clear()
        self.root.clipboard_append(url + '\r\n')
        self.assertEqual(self.paste(), 'break')
        self.assertEqual(self.entry.get(), url)
        self.assertEqual(self.calls, [url])

    def test_replace_selection_with_google_url(self):
        self.entry.insert(0, 'old-url')
        self.entry.selection_range(0, tk.END)
        self.root.clipboard_clear()
        self.root.clipboard_append('gsheet:new-source')
        self.paste()
        self.assertEqual(self.entry.get(), 'gsheet:new-source')

    def test_control_v_is_bound(self):
        self.assertIn('paste', self.entry.bind('<Control-v>'))
        self.assertIn('paste', self.entry.bind('<Shift-Insert>'))


if __name__ == '__main__':
    unittest.main()
