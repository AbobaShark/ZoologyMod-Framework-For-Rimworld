"""Explicit paste support for Tk/ttk source fields.

Tk's Entry class bindings are platform-dependent, and ttk entries do not
provide a built-in right-click context menu on all platforms. Keep paste
available through a button, context menu, Ctrl+V, Shift+Insert, and Cmd+V.
"""
from __future__ import annotations

import tkinter as tk
from tkinter import messagebox


def enable_paste(entry: tk.Entry, *, on_change=None):
    """Install accessible paste actions on an editable Entry.

    Return a callback suitable for a 'Paste URL' button. This does not
    validate URLs: typing and pasting share the same source semantics.
    """
    if on_change is None:
        on_change = lambda: None

    def paste(_event=None):
        try:
            text = entry.clipboard_get()
        except tk.TclError:
            messagebox.showwarning('Paste unavailable',
                                   'The clipboard does not contain text.', parent=entry.winfo_toplevel())
            return 'break'
        if not isinstance(text, str) or not text:
            return 'break'
        # A copied Google Sheets URL may include a trailing newline.
        text = text.strip()
        if '\n' in text or '\r' in text:
            messagebox.showwarning('Invalid clipboard text',
                                   'Please copy a single URL or source path.', parent=entry.winfo_toplevel())
            return 'break'
        if not text:
            return 'break'
        try:
            if entry.selection_present():
                entry.delete('sel.first', 'sel.last')
            entry.insert(tk.INSERT, text)
        except tk.TclError:
            messagebox.showerror('Paste failed', 'This field cannot be edited.', parent=entry.winfo_toplevel())
            return 'break'
        on_change()
        return 'break'

    menu = tk.Menu(entry, tearoff=False)
    menu.add_command(label='Cut', command=lambda: entry.event_generate('<<Cut>>'))
    menu.add_command(label='Copy', command=lambda: entry.event_generate('<<Copy>>'))
    menu.add_command(label='Paste', command=paste)
    menu.add_separator()
    menu.add_command(label='Select All', command=lambda: (entry.selection_range(0, tk.END), entry.icursor(tk.END)))

    def popup(event):
        entry.focus_set()
        try:
            menu.tk_popup(event.x_root, event.y_root)
        finally:
            menu.grab_release()
        return 'break'

    entry.bind('<Button-3>', popup, add='+')
    # These bindings implement paste directly rather than delegating to
    # platform-specific virtual events. Return 'break' to avoid double-paste.
    for sequence in ('<Control-v>', '<Control-V>', '<Shift-Insert>', '<Command-v>', '<Command-V>'):
        entry.bind(sequence, paste, add='+')
    # Holding focus or pressing Enter should save a URL typed manually too.
    entry.bind('<FocusOut>', lambda event: on_change(), add='+')
    entry.bind('<Return>', lambda event: on_change(), add='+')
    return paste
