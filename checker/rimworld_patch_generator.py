#!/usr/bin/env python3
"""Zoology Patch Generator: table-driven generation from an explicit group registry."""
from __future__ import annotations
import argparse
import sys
import queue
import threading
import traceback
from pathlib import Path
import tkinter as tk
from tkinter import ttk,filedialog,messagebox
from core.checker_clipboard import enable_paste
from core.checker_config import ROOT,load,save,resolve_path
from core.pipeline import stage_pipeline,CATEGORIES,STAGE
from core.generation_registry import load_registry,audit_routes
from core.xml_routing import plan,deploy,find_project_root

SHEETS=('Animals','Animals CE','Bionics','LifeStages','EggMilkWool','Venom profiles','Biomes','Leathers','Animal textiles')
LABELS={'animals':'Animal patches (Vanilla + CE)','venom':'Venom, antivenoms and recipes',
        'biomes':'Biome animal populations','products':'Eggs, hatchers, milk and wool',
        'bionics':'Bionic implants (Vanilla + CE)','life-stages':'Life-stage factors (Vanilla + CE)',
        'leathers':'Leather stats and custom defs (Vanilla + CE)',
        'textiles':'Wool and animal silk (Vanilla + CE)'}

class PatchGeneratorApp(tk.Tk):
    def __init__(self):
        super().__init__()
        self.title('Zoology Patch Generator — RimWorld 1.7')
        self.geometry('1080x830');self.minsize(810,600)
        self.cfg=load('patch_generator')
        self.source=tk.StringVar(value=self.cfg.get('vanilla_tsv',''))
        self.registry=tk.StringVar(value=self.cfg.get('generation_config') or str(ROOT/'generation.json'))
        self.orig_xml=tk.StringVar(value=self.cfg.get('original_xml_dir') or str(ROOT/'OriginalXML'))
        self.orig_patch=tk.StringVar(value=self.cfg.get('ce_patches_dir') or str(ROOT/'OriginalXML/CEPatches'))
        self.ce=tk.BooleanVar(value=bool(self.cfg.get('enable_ce',True)))
        self.optimize=tk.BooleanVar(value=bool(self.cfg.get('optimize',True)))
        self.sheets={name:tk.StringVar(value=self.cfg.get('sheet_sources',{}).get(name,'')) for name in SHEETS}
        if self.cfg.get('ce_tsv') and not self.sheets['Animals CE'].get():self.sheets['Animals CE'].set(self.cfg['ce_tsv'])
        if self.cfg.get('egg_milk_tsv') and not self.sheets['EggMilkWool'].get():self.sheets['EggMilkWool'].set(self.cfg['egg_milk_tsv'])
        if self.cfg.get('venom_profiles_source') and not self.sheets['Venom profiles'].get():self.sheets['Venom profiles'].set(self.cfg['venom_profiles_source'])
        self.groups=tk.StringVar(value='')
        self.messages=queue.Queue();self.busy=False
        self._build()
        self.after(150,self._poll)

    def _path(self,parent,label,var,row,hint=None):
        ttk.Label(parent,text=label).grid(row=row,column=0,sticky='w',padx=5,pady=4)
        entry=ttk.Entry(parent,textvariable=var)
        entry.grid(row=row,column=1,sticky='ew',padx=5,pady=4)
        enable_paste(entry)
        ttk.Button(parent,text='Browse',command=lambda:self._browse(var)).grid(row=row,column=2,padx=5)
        return entry

    def _browse(self,var):
        path=filedialog.askopenfilename(title='Select JSON, XML, TSV or Excel file',filetypes=[('Data files','*.json *.tsv *.xlsx *.xlsm *.xls *.xml'),('All files','*.*')])
        if path:var.set(path)

    def _folder(self,var):
        path=filedialog.askdirectory(title='Select reference XML folder')
        if path:var.set(path)

    def _build(self):
        outer=ttk.Frame(self);outer.pack(fill='both',expand=True)
        canvas=tk.Canvas(outer,highlightthickness=0)
        scrollbar=ttk.Scrollbar(outer,orient='vertical',command=canvas.yview)
        canvas.configure(yscrollcommand=scrollbar.set)
        scrollbar.pack(side='right',fill='y');canvas.pack(side='left',fill='both',expand=True)
        panel=ttk.Frame(canvas,padding=12)
        cwin=canvas.create_window((0,0),window=panel,anchor='nw')
        panel.bind('<Configure>',lambda event:canvas.configure(scrollregion=canvas.bbox('all')))
        canvas.bind('<Configure>',lambda event:canvas.itemconfigure(cwin,width=event.width))
        ttk.Label(panel,text='Generate patches from AnimalStats and generation.json',font=('TkDefaultFont',13,'bold')).pack(anchor='w',pady=(0,7))
        source=ttk.LabelFrame(panel,text='1 · Sources');source.pack(fill='x',pady=5);source.columnconfigure(1,weight=1)
        self._path(source,'AnimalStats workbook / Google Sheets URL',self.source,0)
        self._path(source,'Generation registry JSON',self.registry,1)
        ttk.Label(source,text='Optional individual sheet sources (TSV, XLSX or Google Sheets; blank = main workbook)').grid(row=2,column=0,columnspan=3,sticky='w',padx=5,pady=(10,2))
        for i,name in enumerate(SHEETS,3):self._path(source,name,self.sheets[name],i)
        originals=ttk.LabelFrame(panel,text='2 · Upstream reference XML and patch sets');originals.pack(fill='x',pady=5);originals.columnconfigure(1,weight=1)
        for row,(label,var) in enumerate([('OriginalXML directory',self.orig_xml),('CE patches directory (OriginalXML/CEPatches)',self.orig_patch)]):
            ttk.Label(originals,text=label).grid(row=row,column=0,sticky='w',padx=5,pady=4)
            entry=ttk.Entry(originals,textvariable=var);entry.grid(row=row,column=1,sticky='ew',padx=5,pady=4);enable_paste(entry)
            ttk.Button(originals,text='Browse',command=lambda var=var:self._folder(var)).grid(row=row,column=2,padx=5)
        opt=ttk.Frame(panel);opt.pack(fill='x',pady=8)
        ttk.Checkbutton(opt,text='Generate Combat Extended patches',variable=self.ce).pack(side='left',padx=(0,20))
        ttk.Checkbutton(opt,text='Optimize patches against OriginalXML',variable=self.optimize).pack(side='left')
        frame=ttk.LabelFrame(panel,text='3 · Generate (stage only, no files in Zoology are modified)');frame.pack(fill='x',pady=5)
        for idx,key in enumerate(CATEGORIES):
            ttk.Button(frame,text=LABELS[key],command=lambda key=key:self._stage((key,))).grid(row=idx//2,column=idx%2,sticky='ew',padx=6,pady=6)
        frame.columnconfigure(0,weight=1);frame.columnconfigure(1,weight=1)
        ttk.Label(frame,text='Optional animal group IDs (comma-separated; empty = all 81 groups)').grid(row=5,column=0,sticky='w',padx=6,pady=(10,4))
        entry=ttk.Entry(frame,textvariable=self.groups);entry.grid(row=5,column=1,sticky='ew',padx=6);enable_paste(entry)
        ttk.Button(frame,text='GENERATE ALL',command=lambda:self._stage(CATEGORIES)).grid(row=6,column=0,columnspan=2,sticky='ew',padx=6,pady=8)
        actions=ttk.Frame(panel);actions.pack(fill='x',pady=7)
        ttk.Button(actions,text='Check registry / load destinations',command=self._check).pack(side='left',padx=3)
        ttk.Button(actions,text='Preview staged deployment',command=self._preview).pack(side='left',padx=3)
        ttk.Button(actions,text='Deploy reviewed XML',command=self._deploy).pack(side='left',padx=3)
        ttk.Button(actions,text='Save local settings',command=self._save).pack(side='right',padx=3)
        ttk.Label(panel,text='4 · Results and diagnostics').pack(anchor='w',pady=4)
        self.log=tk.Text(panel,height=11,wrap='word',state='disabled');self.log.pack(fill='both',expand=True)
        self._note(f'Output: {STAGE}  (ignored by Git; never required as generation input)')

    def _note(self,message):
        self.log.configure(state='normal');self.log.insert('end',str(message)+'\n');self.log.see('end');self.log.configure(state='disabled')

    def _settings(self):
        self.cfg.update({'vanilla_tsv':self.source.get().strip(),'generation_config':self.registry.get().strip(),
            'original_xml_dir':self.orig_xml.get().strip(),'ce_patches_dir':self.orig_patch.get().strip(),
            'enable_ce':self.ce.get(),'optimize':self.optimize.get(),
            'sheet_sources':{name:v.get().strip() for name,v in self.sheets.items() if v.get().strip()},
            'ce_tsv':'','venom_profiles_source':'','egg_milk_tsv':''})
        return self.cfg

    def _save(self):
        try:save('patch_generator',self._settings());self._note('Settings saved to checker.local.json (ignored by Git)')
        except Exception as e:messagebox.showerror('Save settings',str(e))

    def _check(self):
        try:
            root=find_project_root(ROOT)
            report=audit_routes(resolve_path(self.registry.get()),root/'Zoology',require_static_sources=True)
            self._note(f'VALID: {report["destinations"]} XML destinations. All registered paths agree with LoadFolders.xml.')
        except Exception as e:messagebox.showerror('Registry invalid',str(e))

    def _stage(self,categories):
        if self.busy:return
        self._save()
        try:
            root=find_project_root(ROOT)
            ids=[x.strip() for x in self.groups.get().split(',') if x.strip()]
            args=dict(source=resolve_path(self.source.get().strip()),root=root,
                registry_path=resolve_path(self.registry.get()),
                sheet_sources={name:resolve_path(var.get().strip()) for name,var in self.sheets.items() if var.get().strip()},
                categories=categories,groups=ids or None,ce=self.ce.get(),optimize=self.optimize.get(),
                original_xml=resolve_path(self.orig_xml.get()),original_patches=resolve_path(self.orig_patch.get()))
            self.busy=True;self._note('Generating '+', '.join(categories)+' ...')
            threading.Thread(target=self._worker,args=(args,),daemon=True).start()
        except Exception as exc:messagebox.showerror('Cannot generate',str(exc))

    def _worker(self,args):
        try:
            self.messages.put(('ok',stage_pipeline(**args),None))
        except Exception as exc:
            # The full traceback is essential: GUI message boxes previously
            # hid the crashing file and line, making unrelated guesses likely.
            self.messages.put(('error',f'{type(exc).__name__}: {exc}',
                               traceback.format_exc()))

    def _poll(self):
        try:
            while True:
                kind,result,details=self.messages.get_nowait()
                self.busy=False
                if kind=='ok':
                    self._note(f'Staged successfully: {result}. Preview before deploying.')
                else:
                    log_path=ROOT/'output'/'generation-error.log'
                    self._note('ERROR: '+result)
                    self._note(details or '(No traceback available)')
                    try:
                        log_path.parent.mkdir(parents=True,exist_ok=True)
                        log_path.write_text(details or result,encoding='utf-8')
                        location=f'Full traceback: {log_path}'
                    except OSError as logging_error:
                        location=f'Could not save traceback: {logging_error}'
                    messagebox.showerror('Generation failed',
                                         result+'\n\n'+location)
        except queue.Empty:
            pass
        self.after(150,self._poll)

    def _preview(self):
        try:
            ops,_=plan(STAGE/'generation-manifest.json',find_project_root(ROOT))
            counts={k:sum(o[0]==k for o in ops) for k in ('copy','replace','skip','delete','rmdir')}
            self._note('Validated deployment plan: '+str(counts))
            for action,_,_,dest in ops:
                if action!='skip':self._note(f' {action.upper()}: {dest}')
        except Exception as e:messagebox.showerror('Preview failed',str(e))

    def _deploy(self):
        if self.busy:return
        try:
            root=find_project_root(ROOT);path=STAGE/'generation-manifest.json'
            ops,_=plan(path,root)
            changes=[op for op in ops if op[0]!='skip']
            if not messagebox.askyesno('Deploy XML',f'Apply {len(changes)} changes to Zoology? Includes {sum(op[0]=="delete" for op in changes)} deletions.\n\nStaged checksums and destination paths will be revalidated. Existing XML may be overwritten.'):
                return
            deploy(path,root);self._note(f'Deployed {len(changes)} modifications successfully')
        except Exception as e:messagebox.showerror('Deployment failed',str(e))

# The GUI and command-line interface share the same staging and routing engine.
# Supplying any CLI argument selects CLI mode; with no arguments the Tk GUI opens.
def cli_main(argv=None):
    p=argparse.ArgumentParser(description='Zoology Patch Generator CLI (no arguments opens GUI)')
    p.add_argument('--source',help='Multisheet AnimalStats workbook or Google Sheet URL/ID')
    p.add_argument('--sheet',action='append',default=[],metavar='TAB=SOURCE',help='Per-tab TSV/XLSX/Google override; repeat')
    p.add_argument('--registry',help='Generation groups and exact destination JSON')
    p.add_argument('--original-xml',help='OriginalXML reference directory')
    p.add_argument('--ce-patches',help='CE reference patches directory (inside OriginalXML)')
    p.add_argument('--project-root')
    p.add_argument('--group',action='append',help='Specific group ID (repeat)')
    p.add_argument('--category',action='append',choices=CATEGORIES,help='Generate selected category (repeat)')
    p.add_argument('--all',action='store_true',help='Generate all categories')
    ce=p.add_mutually_exclusive_group();ce.add_argument('--ce',dest='ce',action='store_true');ce.add_argument('--no-ce',dest='ce',action='store_false');p.set_defaults(ce=None)
    opt=p.add_mutually_exclusive_group();opt.add_argument('--optimize',dest='opt',action='store_true');opt.add_argument('--no-optimize',dest='opt',action='store_false');p.set_defaults(opt=None)
    p.add_argument('--check',action='store_true',help='Audit registered destinations and required installed static XML')
    p.add_argument('--preview',action='store_true',help='Validate staged manifest and print deployment plan')
    p.add_argument('--deploy',action='store_true',help='Deploy validated staged files to Zoology')
    p.add_argument('--yes',action='store_true',help='Required confirmation for --deploy')
    args=p.parse_args(argv)
    # Validate potentially destructive actions before inspecting the project.
    if args.preview and args.deploy:
        p.error('--preview and --deploy conflict')
    if args.deploy and not args.yes:
        p.error('--deploy requires --yes')
    cfg=load('patch_generator')
    root=Path(args.project_root).resolve() if args.project_root else find_project_root(ROOT)
    output=ROOT/'output/staged/generation-manifest.json'
    if args.check:
        if args.preview or args.deploy or args.all or args.category:
            p.error('--check cannot be combined with generation or deployment actions')
        result=audit_routes(resolve_path(args.registry) if args.registry else cfg.get('generation_config') or ROOT/'generation.json',
                            root/'Zoology',require_static_sources=True)
        print('Routing and static sources verified:', result)
        return
    if args.preview or args.deploy:
        deploy(output,root,dry_run=not args.deploy)
        return
    overrides=dict(cfg.get('sheet_sources',{}))
    for pair in args.sheet:
        if '=' not in pair:p.error(f'--sheet expects TAB=SOURCE: {pair}')
        name,_,location=pair.partition('=')
        if not name or not location:p.error(f'Invalid sheet override: {pair}')
        overrides[name]=resolve_path(location)
    if cfg.get('ce_tsv') and 'Animals CE' not in overrides:overrides['Animals CE']=cfg['ce_tsv']
    if cfg.get('egg_milk_tsv') and 'EggMilkWool' not in overrides:overrides['EggMilkWool']=cfg['egg_milk_tsv']
    cats=CATEGORIES if args.all else args.category or ('animals',)
    result=stage_pipeline(resolve_path(args.source) if args.source else cfg.get('vanilla_tsv'),root=root,
       registry_path=resolve_path(args.registry) if args.registry else cfg.get('generation_config') or ROOT/'generation.json',
       sheet_sources=overrides,categories=cats,groups=args.group,
       ce=cfg.get('enable_ce',True) if args.ce is None else args.ce,
       optimize=cfg.get('optimize',True) if args.opt is None else args.opt,
       original_xml=resolve_path(args.original_xml) if args.original_xml else cfg.get('original_xml_dir') or ROOT/'OriginalXML',
       original_patches=resolve_path(args.ce_patches) if args.ce_patches else cfg.get('ce_patches_dir') or ROOT/'OriginalXML/CEPatches',
       venom_source=cfg.get('venom_profiles_source') or None,
       preserve_runtime_preconditions=cfg.get('preserve_runtime_preconditions',True))
    print('Generated files staged, not deployed:',result)
    print('Inspect with --preview, then apply with --deploy --yes.')


def main(argv=None):
    if argv is None:
        argv = sys.argv[1:]
    if argv:
        try:
            return cli_main(argv)
        except Exception:
            # CLI previously printed tracebacks to stderr only, whereas the
            # GUI saved them. Keep both entry points equally diagnosable.
            try:
                error_file = ROOT/'output'/'generation-error.log'
                error_file.parent.mkdir(parents=True,exist_ok=True)
                error_file.write_text(traceback.format_exc(),encoding='utf-8')
            except OSError:
                pass  # Preserve the original exception/traceback.
            raise
    return PatchGeneratorApp().mainloop()


if __name__ == '__main__':
    main()
