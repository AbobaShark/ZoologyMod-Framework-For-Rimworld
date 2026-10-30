# Migration from template-based Checker

1. Rename or back up the previous `checker/` directory. Do not overlay the new archive over an old one: this would retain obsolete entrypoints and `generated_patches` templates.
2. Extract the new `checker/` alongside `Zoology/`.
3. Copy only your ignored `checker.local.json` and OAuth credential files if needed; do not copy generated XML. Configuration has been reorganized, so inspect old local settings before reuse. New settings should point to `generation.json`, `OriginalXML` and `OriginalPatches`.
4. Launch `rimworld_patch_generator.py` instead of the removed Fixer. Generate into ignored `output/staged/`, review the manifest and preview the deployment.
5. Use the existing `Zoology/` working tree as the target; deploy will refuse to overwrite unrelated/unowned changes without explicit provenance.

The previous 81 patch templates are no longer required: the group membership, exact route, abstract names and small explicit XML exceptions have been migrated to `generation.json`. Original upstream reference XML remains in `OriginalXML/` and `OriginalPatches/`; these directories have not been replaced by generated results.

The separate `rimworld_xml_generator.py` animal Def generator is untouched.
