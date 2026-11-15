"""Conservative per-patch reference projection for exact, Def-local XPath."""
import copy
import re
from collections import defaultdict
from functools import lru_cache

from lxml import etree as E


_NAME = r"[A-Za-z_][A-Za-z0-9_.-]*"
_LITERAL = r"""(?:"[^"]*"|'[^']*')"""
_COMPARE = rf"(?:{_NAME}|@{_NAME}|text\(\)|\.)\s*=\s*{_LITERAL}"
_PREDICATE = rf"\[\s*(?:[0-9]+|not\(\s*{_NAME}\s*\)|{_COMPARE}(?:\s+(?:or|and)\s+{_COMPARE})*)\s*\]"
_TARGET = re.compile(
    rf"""^/?Defs/(?P<kind>ThingDef|PawnKindDef|BiomeDef|LifeStageDef|HediffDef)"""
    rf"""\[\s*(?P<selector>defName|@Name)\s*=\s*(?P<literal>{_LITERAL})\s*\]"""
    rf"(?P<tail>(?:/{_NAME}(?:{_PREDICATE})*)*)$"
)
_DIRECT = {
    "PatchOperationAdd", "PatchOperationRemove", "PatchOperationReplace",
    "PatchOperationAttributeSet", "PatchOperationAddModExtension",
}
_WRAPPERS = {"PatchOperationConditional", "PatchOperationSequence", "PatchOperationFindMod"}


@lru_cache(maxsize=32768)
def local_target(xpath):
    """Return an exact selector and local suffix; reject all other XPath."""
    match = _TARGET.fullmatch((xpath or "").strip())
    if match is None:
        return None
    literal = match["literal"]
    return (match["kind"], match["selector"], literal[1:-1]), match["tail"]


def patch_targets(patch):
    """All selectors read or written by a patch, or None for unsafe projection."""
    targets = set()
    for xpath in patch.iter("xpath"):
        parsed = local_target(xpath.text)
        if parsed is None:
            return None
        targets.add(parsed[0])
    for node in patch.iter():
        cls = node.get("Class", "") if isinstance(node.tag, str) else ""
        if not cls.startswith("PatchOperation"):
            continue
        if cls in _WRAPPERS:
            continue
        if cls not in _DIRECT:
            return None
        parsed = local_target(node.findtext("xpath"))
        if parsed is None:
            return None
        _, tail = parsed
        # Def identity and inheritance must remain stable in the projection.
        if re.match(r"^/defName(?:/|\[|$)", tail):
            return None
        if not tail:
            if cls not in {"PatchOperationAdd", "PatchOperationAddModExtension"}:
                return None
            value = node.find("value")
            if value is not None and value.find("defName") is not None:
                return None
    return targets or None


class ReferenceProjection:
    """Index an immutable reference document; clone only selected Def closure."""

    def __init__(self, root):
        self.root = root
        self.children = list(root)
        self.positions = defaultdict(list)
        for pos, node in enumerate(self.children):
            if not isinstance(node.tag, str):
                continue
            if node.get("Name") is not None:
                self.positions[(node.tag, "@Name", node.get("Name"))].append(pos)
            # XPath node-set equality matches any direct defName child, not
            # merely the first. Retain document order and duplicate matches.
            for name in node.findall("defName"):
                self.positions[(node.tag, "defName", name.xpath("string(.)"))].append(pos)

    def clone(self, targets):
        pending = list(targets)
        seen = set()
        positions = set()
        while pending:
            key = pending.pop()
            if key in seen:
                continue
            seen.add(key)
            for pos in self.positions.get(key, ()):
                positions.add(pos)
                node = self.children[pos]
                parent = (node.get("ParentName") or "").strip()
                if parent:
                    pending.append((node.tag, "@Name", parent))
        projected = E.Element(self.root.tag, attrib=self.root.attrib, nsmap=self.root.nsmap)
        projected.text = self.root.text
        for pos in sorted(positions):
            projected.append(copy.deepcopy(self.children[pos]))
        return projected
