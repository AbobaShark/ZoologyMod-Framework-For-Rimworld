"""Reviewed auto-sync GUI for the existing Checker upstream reference corpus."""
from __future__ import annotations
import json
import os
import shutil
import tempfile
from pathlib import Path
import queue
import threading
import tkinter as tk
from tkinter import filedialog, messagebox, ttk

from tools import reference_sync_auto as sync

ROOT = Path(__file__).resolve().parent
DEFAULT = ROOT / 'reference_sources.auto.json'
SETTINGS = ROOT / 'checker.local.json'

def _remove_obsolete_files():
    """Discard abandoned generation and reference state, without migration."""
    for relative in ('output/reference_sources.local.json',
                     'reference_sources.local.json', 'OriginalPatches',
                     'generated_patches'):
        path = ROOT / relative
        if path.is_symlink() or path.is_file():
            path.unlink()
        elif path.is_dir():
            shutil.rmtree(path)


def _load_reference_config():
    """Always use current versioned source paths, never local copies of them."""
    config = sync.read_config(DEFAULT)
    if not SETTINGS.is_file():
        return config
    document = json.loads(SETTINGS.read_text(encoding='utf-8-sig'))
    local = document.get('reference_sync', {})
    if not isinstance(local, dict) or not isinstance(local.get('mods', {}), dict):
        raise ValueError('checker.local.json: invalid reference_sync settings')
    config['steam_root'] = local.get('steam_root', '')
    edits = local.get('mods', {})
    for spec in config['mods']:
        override = edits.get(spec['id'], {})
        if not isinstance(override, dict):
            raise ValueError('checker.local.json: invalid reference_sync mod settings')
        if 'enabled' in override:
            spec['enabled'] = bool(override['enabled'])
        if 'github' in override:
            spec['github'] = override['github']
    return config


def _save_reference_config(config):
    """Persist only preferences in shared local config, never reference paths."""
    baseline = sync.read_config(DEFAULT)
    originals = {spec['id']: spec for spec in baseline['mods']}
    overrides = {}
    for spec in config['mods']:
        original = originals[spec['id']]
        changed = {}
        for key in ('enabled', 'github'):
            if spec.get(key) != original.get(key):
                changed[key] = spec.get(key)
        if changed:
            overrides[spec['id']] = changed
    document = json.loads(SETTINGS.read_text(encoding='utf-8-sig')) if SETTINGS.is_file() else {}
    state = {'mods': overrides}
    if config.get('steam_root', '').strip():
        state['steam_root'] = config['steam_root'].strip()
    if overrides or 'steam_root' in state:
        document['reference_sync'] = state
    else:
        document.pop('reference_sync', None)
    SETTINGS.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile('w', encoding='utf-8', dir=SETTINGS.parent,
                                     prefix='.checker-sync-', suffix='.tmp', delete=False) as output:
        temporary = Path(output.name)
        json.dump(document, output, ensure_ascii=False, indent=2)
        output.write('\n')
    try:
        os.replace(temporary, SETTINGS)
    finally:
        temporary.unlink(missing_ok=True)



class App:
    def __init__(self, root):
        self.root = root
        root.title('Checker — Upstream Reference Sync')
        root.geometry('1190x770')
        _remove_obsolete_files()
        self.config = _load_reference_config()
        self.steam = tk.StringVar(value=self.config.get('steam_root', ''))
        self.github = tk.BooleanVar(value=True)
        self.mod_enabled = tk.BooleanVar()
        self.url = tk.StringVar()
        self.branch = tk.StringVar()
        self.revision = tk.StringVar()
        self.entries = None
        self.warnings = []
        self.q = queue.Queue()
        self.busy = False
        self._create()
        self._refresh_sources()
        root.after(150, self._poll)

    def _create(self):
        r = self.root
        outer = ttk.Frame(r, padding=10); outer.pack(fill='both', expand=True)
        top = ttk.LabelFrame(outer, text='Local Steam library / RimWorld / workshop folder', padding=8)
        top.pack(fill='x')
        ttk.Entry(top, textvariable=self.steam).pack(side='left', fill='x', expand=True)
        ttk.Button(top, text='Browse…', command=self._browse).pack(side='left', padx=6)
        ttk.Checkbutton(top, text='Prefer GitHub', variable=self.github).pack(side='left')
        ttk.Label(outer, text='Configured mods: select one to edit. Enabled is saved immediately; GitHub fields use Update selected. Blank branch uses the latest default branch.').pack(anchor='w', pady=(8,2))
        upper = ttk.Frame(outer); upper.pack(fill='both', expand=False)
        self.mods = ttk.Treeview(upper, columns=('github','workshop'), show='tree headings', height=10)
        self.mods.heading('#0', text='Mod / reference group')
        self.mods.heading('github', text='GitHub URL (optional)')
        self.mods.heading('workshop', text='Workshop ID')
        self.mods.column('#0', width=290)
        self.mods.column('github', width=670)
        self.mods.column('workshop', width=120, anchor='center')
        self.mods.pack(side='left', fill='both', expand=True)
        sy = ttk.Scrollbar(upper, orient='vertical', command=self.mods.yview); sy.pack(side='left', fill='y')
        self.mods.configure(yscrollcommand=sy.set)
        self.mods.bind('<<TreeviewSelect>>', self._selected)

        editor = ttk.LabelFrame(outer, text='Selected source (saved to local config, not Git)', padding=8)
        editor.pack(fill='x', pady=8)
        ttk.Checkbutton(editor, text='Enabled', variable=self.mod_enabled, command=self._toggle_enabled).grid(row=0, column=0, sticky='w')
        ttk.Label(editor, text='GitHub URL').grid(row=0,column=1, sticky='e', padx=(12,4))
        ttk.Entry(editor, textvariable=self.url).grid(row=0,column=2, sticky='ew')
        ttk.Label(editor, text='Branch').grid(row=1,column=1, sticky='e', padx=(12,4))
        ttk.Entry(editor, textvariable=self.branch, width=23).grid(row=1,column=2, sticky='w')
        ttk.Label(editor, text='Pinned SHA (optional)').grid(row=2,column=1, sticky='e', padx=(12,4))
        ttk.Entry(editor, textvariable=self.revision, width=46).grid(row=2,column=2, sticky='w')
        ttk.Button(editor, text='Update selected', command=self._save_selected).grid(row=1,column=3, padx=10)
        ttk.Button(editor, text='Save configuration', command=self._save_config).grid(row=2,column=3, padx=10)
        editor.columnconfigure(2, weight=1)

        actions = ttk.Frame(outer); actions.pack(fill='x', pady=(0,6))
        self.preview_button = ttk.Button(actions, text='Preview (read only)', command=self._preview)
        self.preview_button.pack(side='left')
        self.apply_button = ttk.Button(actions, text='Apply reviewed changes', command=self._apply, state='disabled')
        self.apply_button.pack(side='left', padx=10)
        ttk.Button(actions, text='Open backup folder', command=self._open_backups).pack(side='left')
        self.status = tk.StringVar(value='Ready. Preview never writes into OriginalXML / CEPatches.')
        ttk.Label(actions, textvariable=self.status).pack(side='left', padx=12)
        ttk.Label(outer, text='COPY = new references to import; REPLACE = updated references. MISSING checks only existing references.').pack(anchor='w')
        box = ttk.Frame(outer); box.pack(fill='both', expand=True)
        cols = ('status','reference','source')
        self.results = ttk.Treeview(box, columns=cols, show='headings', height=14)
        for key, title, width in [('status','Status',110), ('reference','Reference',490), ('source','Chosen upstream XML',520)]:
            self.results.heading(key, text=title); self.results.column(key, width=width)
        self.results.pack(side='left', expand=True, fill='both')
        sy2 = ttk.Scrollbar(box, orient='vertical', command=self.results.yview); sy2.pack(side='left',fill='y')
        self.results.configure(yscrollcommand=sy2.set)
        self.results.bind('<<TreeviewSelect>>', self._detail)
        self.info = tk.StringVar(value='Select a reference row for details.')
        ttk.Label(outer, textvariable=self.info, wraplength=1100).pack(fill='x', pady=4)

    def _browse(self):
        p = filedialog.askdirectory(title='Choose Steam library, RimWorld, or workshop folder')
        if p: self.steam.set(p); self._invalidate()

    def _refresh_sources(self):
        self.mods.delete(*self.mods.get_children())
        for i, spec in enumerate(self.config['mods']):
            gh = spec.get('github') or {}
            text = ('[on] ' if spec.get('enabled',True) else '[off] ') + spec['id']
            self.mods.insert('', 'end', iid=str(i), text=text, values=(gh.get('url',''), ', '.join(spec.get('workshop_ids',[]))))
        if self.config['mods']:
            self.mods.selection_set('0');self._selected()

    def _selected(self, *_):
        selected = self.mods.selection()
        if not selected: return
        sp = self.config['mods'][int(selected[0])]
        gh = sp.get('github') or {}
        self.mod_enabled.set(sp.get('enabled',True))
        self.url.set(gh.get('url',''))
        self.branch.set(gh.get('branch',''))
        self.revision.set(gh.get('revision',''))

    def _toggle_enabled(self):
        """Apply and persist the checkbox immediately, without an extra button click."""
        if self.busy:
            self._selected()  # Restore the actual state while sync is running.
            return
        if not self.mods.selection():
            return
        self._save_config()  # Also refreshes the [on]/[off] label via _save_selected.

    def _save_selected(self, *_):
        if self.busy: return False
        selected = self.mods.selection()
        if not selected: return True
        spec = self.config['mods'][int(selected[0])]
        spec['enabled'] = bool(self.mod_enabled.get())
        spec['github'] = {'url':self.url.get().strip(), 'branch':self.branch.get().strip(), 'revision':self.revision.get().strip()}
        idx = selected[0]
        gh = spec.get('github') or {}
        self.mods.item(idx,
                       text=('[on] ' if spec.get('enabled', True) else '[off] ') + spec['id'],
                       values=(gh.get('url', ''), ', '.join(spec.get('workshop_ids', []))))
        self._invalidate()
        return True

    def _save_config(self):
        if not self._save_selected():return
        self.config['steam_root'] = self.steam.get().strip()
        _save_reference_config(self.config)
        self.status.set('Saved: '+str(SETTINGS))

    def _invalidate(self):
        self.entries = None
        self.apply_button.configure(state='disabled')

    def _preview(self):
        if self.busy:return
        self._save_config()
        self._invalidate()
        self.results.delete(*self.results.get_children())
        self._set_busy(True)
        cfg = json.loads(json.dumps(self.config))
        steam, github = self.steam.get().strip(), self.github.get()
        def worker():
            try:
                results, warnings = sync.discover(cfg, steam=steam, github=github,
                                                   progress=lambda msg:self.q.put(('status', msg)))
                self.q.put(('preview', (results, warnings)))
            except Exception as ex:
                self.q.put(('error', str(ex)))
        threading.Thread(target=worker, daemon=True).start()

    def _apply(self):
        if self.busy or not self.entries:return
        changes = [x for x in self.entries if x['status'] in ('REPLACE','COPY')]
        if not changes:return
        new_files = sum(x['status'] == 'COPY' for x in changes)
        updated_files = len(changes) - new_files
        if not messagebox.askyesno('Apply changes',
                f'Import {new_files} new reference file(s) and update '
                f'{updated_files} existing file(s)?\n'
                'Existing files will be backed up.\n'
                'Unmatched and historical references remain untouched.'):
            return
        self._set_busy(True)
        try:
            backup = sync.apply(self.entries)
            self.status.set(f'Imported {new_files} new and updated {updated_files} existing references. Backup: {backup}')
            messagebox.showinfo('Completed', f'Imported {new_files} new reference files, '
                f'updated {updated_files} existing files.\nBackup: {backup}\n'
                'Material definitions are now available for the generator. '
                'Regenerate and validate Zoology patches before deployment.')
            self._invalidate()
        except Exception as ex:
            messagebox.showerror('Apply failed', str(ex))
        finally:
            self._set_busy(False)

    def _set_busy(self, state):
        self.busy = state
        self.preview_button.configure(state='disabled' if state else 'normal')
        self.apply_button.configure(state='disabled' if state or not self.entries or not any(i['status'] in ('COPY','REPLACE') for i in self.entries) else 'normal')

    def _poll(self):
        try:
            while True:
                kind, data = self.q.get_nowait()
                if kind == 'status':
                    self.status.set(data)
                elif kind == 'preview':
                    self.entries, self.warnings = data
                    priority = {'COPY': 0, 'REPLACE': 1, 'MISSING': 2,
                                'REVIEW': 3, 'SKIP': 4, 'HISTORICAL': 5}
                    ordered = sorted(enumerate(self.entries),
                                     key=lambda e: (priority.get(e[1]['status'], 6),
                                                    e[1]['destination'].casefold()))
                    for i, ent in ordered:
                        self.results.insert('', 'end', iid=str(i),
                                            values=(ent['status'], ent['destination'],
                                                    ent.get('source_file','')))
                    counts = {k: sum(x['status'] == k for x in self.entries)
                              for k in ('COPY','REPLACE','SKIP','MISSING','REVIEW','HISTORICAL')}
                    material_new = sum(x['status'] == 'COPY' and '/_Leathers/' in x['destination']
                                       for x in self.entries)
                    pending = counts['COPY'] + counts['REPLACE']
                    self.status.set('Preview: '+', '.join(f'{k} {v}' for k,v in counts.items())
                                    + f'; warnings: {len(self.warnings)}')
                    if pending:
                        message = (f'Not yet synchronized: {counts["COPY"]} new and '
                                   f'{counts["REPLACE"]} updated reference file(s); '
                                   f'{material_new} new leather/wool/CE material XML. '
                                   'Click Apply reviewed changes to import them.')
                    else:
                        message = ('No new or updated upstream files found. '
                                   'MISSING 0 alone does not validate the complete '
                                   'material reference corpus.')
                    if self.warnings:
                        message += ' Warnings: ' + ' | '.join(self.warnings[:3])
                    self.info.set(message)
                    self.apply_button.configure(text=f'Apply ({counts["COPY"]} new, '
                                                         f'{counts["REPLACE"]} updated)')
                    self._set_busy(False)
                elif kind == 'error':
                    self._set_busy(False)
                    messagebox.showerror('Preview failed', data)
        except queue.Empty:
            pass
        self.root.after(150, self._poll)

    def _detail(self, *_):
        sel = self.results.selection()
        if not sel or not self.entries: return
        item = self.entries[int(sel[0])]
        self.info.set(' | '.join([item['destination'], item['status'],
            'Origin: '+item.get('provenance', '(not found)'),
            'File: '+item.get('source_file', ''), item.get('detail','')]))

    def _open_backups(self):
        import os, subprocess, sys
        path = ROOT / 'output' / 'reference-backups'
        path.mkdir(parents=True, exist_ok=True)
        if sys.platform == 'win32': os.startfile(path)
        elif sys.platform == 'darwin': subprocess.Popen(['open', str(path)])
        else: subprocess.Popen(['xdg-open', str(path)])


if __name__ == '__main__':
    root = tk.Tk()
    App(root)
    root.mainloop()
