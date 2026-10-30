"""Read-only environment preflight; never fetches Google data or writes XML."""
import argparse
import sys
from pathlib import Path
import core.checker_config as checker_config
from core.animalstats_source import source_available, is_google_sheet_source


def analyze(config=None):
    cfg=config if config is not None else checker_config.load('patch_generator')
    messages=[]
    def item(ok,label,detail=''):
        messages.append(f'{"OK" if ok else "CHECK"}: {label}'+(f' — {detail}' if detail else ''))
    item(sys.version_info >= (3,11),'Python 3.11+ ',sys.version.split()[0])
    root=checker_config.ROOT
    entrypoints={'rimworld_patch_generator.py', 'rimworld_xml_generator.py'}
    extras=sorted(p.name for p in root.glob('*.py') if p.name not in entrypoints)
    item(not extras,'Clean Checker root',', '.join(extras) if extras else '2 supported entrypoints')
    mod=root.parent/'Zoology'
    item((mod/'LoadFolders.xml').is_file(),'Sibling Zoology project','required for managed deployment')
    registry=root / (cfg.get('generation_config') or 'generation.json')
    item(registry.is_file(),'Generation registry',str(registry))
    source=cfg.get('vanilla_tsv') or ''
    item(bool(source) and source_available(source),'AnimalStats source',
         'Google URL/ID recognized, authentication not tested' if is_google_sheet_source(source) else str(source or 'not configured'))
    local=root/checker_config.LOCAL_NAME
    item(True,'Local settings',f'{local.name} (ignored by Git; {"exists" if local.exists() else "created on first save"})')
    for name in ('google_credentials.json','google_token.json'):
        if (root/name).exists():
            item(True,name,'local secret: keep outside Git and distribution archives')
    return messages


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--strict',action='store_true',help='Exit nonzero if preflight has missing requirements')
    args=parser.parse_args()
    messages=analyze()
    print('\n'.join(messages))
    if args.strict and any(m.startswith('CHECK:') for m in messages): raise SystemExit(1)


if __name__=='__main__': main()
