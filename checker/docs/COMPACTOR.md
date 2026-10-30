# XML compactor (optional maintenance tool)

The pipeline's primary optimization mode uses `OriginalXML`/`OriginalPatches` and `PatchOptimizer` while generating patches. The standalone compactor is for explicit post-generation maintenance of individual XML files; it is not part of the verified group-to-group parity contract.

```powershell
py -m core.rimworld_patch_compactor "output/staged/files/Core/Patches/Core/ThingDefs_Races/Races_Animal_Birds.xml"
```

Without arguments, the tool operates on runtime `output/staged/files/` only. This directory is created after first generation; it does not exist in the distributed package. Do not compact XML after staging without regenerating its SHA-256 manifest: the deployment validator intentionally rejects any modified staged output.
