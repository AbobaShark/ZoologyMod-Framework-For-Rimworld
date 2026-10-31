"""Conservative optional post-generation optimization for any <Patch> output.

The existing OriginalXML-aware optimizer can simplify recognized ThingDef and
PawnKindDef operations. For other Def kinds it preserves unknown operations;
structural compaction is applied only when it reduces operation/XPath counts.
Original <Defs> files are never optimized as patches.
"""
from __future__ import annotations

from lxml import etree as E
from core.rimworld_original_xml import OriginalXmlIndex, parse_target
from core.rimworld_patch_optimizer import PatchOptimizer
from core.rimworld_patch_compactor import PatchCompactor, patch_metrics


class PatchPostprocessor:
    def __init__(self, *, original_xml, original_patches, preserve_guards=True, preloaded_index=None):
        self.original_xml=original_xml
        self.original_patches=original_patches
        self.preserve_guards=preserve_guards
        self.index=preloaded_index
        self.stats=[]

    def _index(self):
        if self.index is None:
            self.index=OriginalXmlIndex(str(self.original_xml),str(self.original_patches) if self.original_patches else None).load()
        return self.index

    def process(self, payload, *, destination):
        root=E.fromstring(payload)
        if root.tag!='Patch':return payload
        original=patch_metrics(root)
        index=self._index()
        # An incomplete reference collection is NOT a proof that an operation
        # is redundant. Require every identifiable Def target to be indexed;
        # otherwise allow only structural compaction, never closed-world edits.
        xpaths=[(node.text or '').strip() for node in root.xpath('//xpath') if (node.text or '').strip()]
        subjects=[parse_target(xpath) for xpath in xpaths]
        fully_covered=bool(subjects) and all(
            subject is not None and index.has_def(*subject) for subject in subjects)
        if index.enabled and fully_covered and not (index.parse_errors or index.patch_errors):
            optimized=PatchOptimizer(index,preserve_missing_target_guards=True).optimize(root)
            if len(optimized)<=len(root):root=optimized
        after_semantic=patch_metrics(root)
        compacted=PatchCompactor().compact(root)
        compressed=patch_metrics(compacted)
        if (compressed['operations']<after_semantic['operations'] or
                compressed['xpaths']<after_semantic['xpaths']):
            root=compacted
        result=patch_metrics(root)
        self.stats.append({'destination':destination,'operations_before':original['operations'],
                           'operations_after':result['operations'],'xpaths_before':original['xpaths'],
                           'xpaths_after':result['xpaths']})
        if (result['operations']==original['operations'] and result['xpaths']==original['xpaths']):
            # Avoid rewriting hand-authored XML solely to remove comments or
            # reformat whitespace when there was no actual simplification.
            return payload
        data=E.tostring(root,pretty_print=True,encoding='utf-8',xml_declaration=True)
        if not data.endswith(b'\n'):data+=b'\n'
        return data
