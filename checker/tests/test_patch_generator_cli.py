"""One public Patch Generator entrypoint: GUI with no args, CLI with flags."""
from __future__ import annotations

import contextlib
import io
import subprocess
import sys
import unittest
from pathlib import Path
from unittest.mock import patch

import rimworld_patch_generator as entry


class UnifiedEntryPointTests(unittest.TestCase):
    def test_no_arguments_open_gui(self):
        with patch.object(entry, 'PatchGeneratorApp') as app, patch.object(entry, 'cli_main') as cli:
            entry.main([])
            app.assert_called_once_with()
            app.return_value.mainloop.assert_called_once_with()
            cli.assert_not_called()

    def test_worker_reports_full_traceback_instead_of_just_exception(self):
        import queue
        class Worker:
            def __init__(self):
                self.messages = queue.Queue()
        worker = Worker()
        failure = TypeError("'NoneType' object has no attribute 'startswith'")
        with patch.object(entry, 'stage_pipeline', side_effect=failure):
            entry.PatchGeneratorApp._worker(worker, {})
        kind, summary, details = worker.messages.get_nowait()
        self.assertEqual(kind, 'error')
        self.assertIn("startswith", summary)
        self.assertIn("Traceback (most recent call last)", details)
        self.assertIn("TypeError:", details)
        self.assertIn("_worker", details)

    def test_flags_open_cli_without_gui(self):
        with patch.object(entry, 'PatchGeneratorApp') as app, patch.object(entry, 'stage_pipeline', return_value=Path('manifest.json')) as stage, \
             patch.object(entry, 'load', return_value={}), \
             patch.object(entry, 'find_project_root', return_value=Path('/tmp/project')):
            with contextlib.redirect_stdout(io.StringIO()):
                entry.main(['--source', 'gsheet:animalstats', '--category', 'animals', '--no-ce', '--no-optimize',
                            '--sheet', 'Animals CE=gsheet:ce', '--group', 'core_birds'])
            app.assert_not_called()
            stage.assert_called_once()
            kwargs = stage.call_args.kwargs
            self.assertEqual(stage.call_args.args[0], 'gsheet:animalstats')
            self.assertEqual(kwargs['categories'], ['animals'])
            self.assertEqual(kwargs['groups'], ['core_birds'])
            self.assertEqual(kwargs['sheet_sources'], {'Animals CE': 'gsheet:ce'})
            self.assertIs(kwargs['ce'], False)
            self.assertIs(kwargs['optimize'], False)

    def test_all_uses_all_categories_and_defaults_from_config(self):
        cfg = {'vanilla_tsv': 'gsheet:main', 'sheet_sources': {'Bionics': 'gsheet:bionics'},
               'generation_config': '/tmp/generation.json', 'original_xml_dir': '/tmp/original',
               'original_patches_dir': '/tmp/patches', 'enable_ce': False, 'optimize': False}
        with patch.object(entry, 'stage_pipeline', return_value=Path('manifest.json')) as stage, \
             patch.object(entry, 'load', return_value=cfg), \
             patch.object(entry, 'find_project_root', return_value=Path('/tmp/project')):
            with contextlib.redirect_stdout(io.StringIO()):
                entry.main(['--all'])
            kwargs=stage.call_args.kwargs
            self.assertEqual(stage.call_args.args[0], 'gsheet:main')
            self.assertEqual(kwargs['categories'], entry.CATEGORIES)
            self.assertEqual(kwargs['sheet_sources'], {'Bionics': 'gsheet:bionics'})
            self.assertFalse(kwargs['ce'])
            self.assertFalse(kwargs['optimize'])

    def test_preview_is_dry_run_and_never_stages(self):
        with patch.object(entry, 'deploy') as deploy, patch.object(entry, 'stage_pipeline') as stage, \
             patch.object(entry, 'load', return_value={}), \
             patch.object(entry, 'find_project_root', return_value=Path('/tmp/project')):
            entry.main(['--preview'])
            deploy.assert_called_once_with(entry.ROOT/'output/staged/generation-manifest.json',
                                           Path('/tmp/project'), dry_run=True)
            stage.assert_not_called()

    def test_deploy_requires_confirmation_flag(self):
        with patch.object(entry, 'deploy') as deploy, patch.object(entry, 'load', return_value={}), \
             patch.object(entry, 'find_project_root', return_value=Path('/tmp/project')):
            with contextlib.redirect_stderr(io.StringIO()):
                with self.assertRaises(SystemExit) as raised:
                    entry.main(['--deploy'])
            self.assertEqual(raised.exception.code, 2)
            deploy.assert_not_called()
            entry.main(['--deploy', '--yes'])
            deploy.assert_called_once_with(entry.ROOT/'output/staged/generation-manifest.json',
                                           Path('/tmp/project'), dry_run=False)

    def test_check_audits_all_routes_without_generating_or_deploying(self):
        result = {'destinations':134, 'missing_static':[]}
        with patch.object(entry,'audit_routes',return_value=result) as audit, \
             patch.object(entry,'stage_pipeline') as stage, \
             patch.object(entry,'deploy') as deploy, \
             patch.object(entry,'load',return_value={}), \
             patch.object(entry,'find_project_root',return_value=Path('/tmp/project')):
            with contextlib.redirect_stdout(io.StringIO()) as output:
                entry.main(['--check'])
            self.assertIn('134',output.getvalue())
            audit.assert_called_once()
            self.assertTrue(audit.call_args.kwargs['require_static_sources'])
            stage.assert_not_called()
            deploy.assert_not_called()

    def test_cli_help_available_from_exact_public_script(self):
        proc=subprocess.run([sys.executable, str(Path(entry.__file__)), '--help'],
                            capture_output=True,text=True,check=True)
        self.assertIn('--all', proc.stdout)
        self.assertIn('--deploy', proc.stdout)
        self.assertIn('--sheet', proc.stdout)
        self.assertIn('--category', proc.stdout)


if __name__=='__main__':
    unittest.main()
