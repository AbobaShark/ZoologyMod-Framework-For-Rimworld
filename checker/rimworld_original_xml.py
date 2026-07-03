import copy
import os
import re
from collections import Counter
from dataclasses import dataclass

from lxml import etree as LET

from rimworld_patch_apply import PatchApplier
from rimworld_patch_defaults import (
    PAWN_KIND_FIELD_DEFAULTS,
    RACE_FIELD_DEFAULTS,
    STAT_BASE_DEFAULTS,
)


TARGET_RE = re.compile(
    r"(?:^|/)Defs/(?P<kind>ThingDef|PawnKindDef)\s*"
    r"\[\s*(?P<attr>defName|@Name)\s*=\s*['\"](?P<name>[^'\"]+)['\"]\s*\]"
)


@dataclass(frozen=True)
class DefSource:
    kind: str
    attr: str
    name: str
    path: str


def normalize_xpath(xpath):
    text = (xpath or "").strip()
    if text.startswith("Defs/"):
        return "/" + text
    return text


def parse_target(xpath):
    match = TARGET_RE.search(normalize_xpath(xpath))
    if not match:
        return None
    return match.group("kind"), match.group("attr"), match.group("name")


def rel_path_from_target(xpath):
    text = normalize_xpath(xpath)
    match = TARGET_RE.search(text)
    if not match:
        return None
    rel = text[match.end():]
    if rel.startswith("/"):
        rel = rel[1:]
    return rel or ""


class OriginalXmlIndex:
    def __init__(self, root_dir, patches_dir=None, extra_xml_paths=None):
        self.root_dir = root_dir
        self.patches_dir = patches_dir
        self.extra_xml_paths = list(extra_xml_paths or [])
        self.root = LET.Element("Defs")
        self.base_root = LET.Element("Defs")
        self.sources = {}
        self.duplicates = []
        self.files_loaded = 0
        self.patch_files_loaded = 0
        self.patch_apply_stats = Counter()
        self.parse_errors = []
        self.patch_errors = []
        self.loaded = False

    def load(self):
        if self.loaded:
            return self
        self.loaded = True
        has_root_dir = bool(self.root_dir and os.path.isdir(self.root_dir))
        has_extra_paths = any(path and os.path.isfile(path) for path in self.extra_xml_paths)
        if not has_root_dir and not has_extra_paths:
            return self

        parser = LET.XMLParser(remove_comments=False, recover=True)
        paths = []
        if has_root_dir:
            for root, _, files in os.walk(self.root_dir):
                for filename in files:
                    if filename.lower().endswith(".xml"):
                        paths.append(os.path.join(root, filename))
        for path in self.extra_xml_paths:
            if path and os.path.isfile(path) and path.lower().endswith(".xml"):
                paths.append(path)
        paths = list({os.path.normcase(os.path.abspath(path)): path for path in paths}.values())
        paths.sort(key=lambda p: os.path.normcase(os.path.abspath(p)))

        for path in paths:
            try:
                parsed = LET.parse(path, parser).getroot()
            except Exception as exc:
                self.parse_errors.append((path, str(exc)))
                continue
            self.files_loaded += 1
            candidates = parsed if parsed.tag == "Defs" else parsed.findall(".//Defs/*")
            if parsed.tag != "Defs":
                direct_defs = [
                    child for child in parsed
                    if isinstance(getattr(child, "tag", None), str)
                    and child.tag in ("ThingDef", "PawnKindDef", "StatDef")
                ]
                candidates = list(candidates) + direct_defs
            for child in candidates:
                if not isinstance(getattr(child, "tag", None), str):
                    continue
                copied = copy.deepcopy(child)
                self.root.append(copied)
                self._index_def(copied, path)
        self.base_root = copy.deepcopy(self.root)
        self._apply_original_patches(parser)
        return self

    @property
    def enabled(self):
        return self.files_loaded > 0

    @property
    def patches_enabled(self):
        return self.patch_files_loaded > 0

    def _apply_original_patches(self, parser):
        if not self.patches_dir or not os.path.isdir(self.patches_dir):
            return

        paths = []
        for root, _, files in os.walk(self.patches_dir):
            for filename in files:
                if filename.lower().endswith(".xml"):
                    paths.append(os.path.join(root, filename))
        paths.sort(key=lambda p: os.path.normcase(os.path.abspath(p)))
        if not paths:
            return

        applier = PatchApplier(
            stats=self.patch_apply_stats,
            errors=self.patch_errors,
            apply_find_mod_match=True,
            record_missing=True,
        )
        for path in paths:
            try:
                patch_root = LET.parse(path, parser).getroot()
            except Exception as exc:
                self.parse_errors.append((path, str(exc)))
                continue
            self.patch_files_loaded += 1
            applier.apply_patch_root(patch_root, self.root, source_path=path)

        self._rebuild_sources()

    def _rebuild_sources(self):
        old_paths = {}
        for key, source in self.sources.items():
            old_paths.setdefault((key[0], key[2]), source.path)

        self.sources = {}
        self.duplicates = []
        for child in self.root:
            if not isinstance(getattr(child, "tag", None), str):
                continue
            path = self._source_path_for_element(child, old_paths)
            self._index_def(child, path)

    def _source_path_for_element(self, element, old_paths):
        name_el = element.find("defName")
        if name_el is not None and name_el.text and name_el.text.strip():
            path = old_paths.get((element.tag, name_el.text.strip()))
            if path:
                return path
        attr_name = element.get("Name")
        if attr_name:
            path = old_paths.get((element.tag, attr_name.strip()))
            if path:
                return path
        return self.patches_dir or self.root_dir or "<OriginalPatches>"

    def _index_def(self, element, path):
        kind = element.tag
        if kind not in ("ThingDef", "PawnKindDef"):
            return

        name_el = element.find("defName")
        if name_el is not None and name_el.text and name_el.text.strip():
            key = (kind, "defName", name_el.text.strip())
            self._store_source(key, element, path)

        attr_name = element.get("Name")
        if attr_name:
            key = (kind, "@Name", attr_name.strip())
            self._store_source(key, element, path)

    def _store_source(self, key, element, path):
        if key in self.sources:
            self.duplicates.append((key, self.sources[key].path, path))
            return
        self.sources[key] = DefSource(key[0], key[1], key[2], path)

    def element_tree(self):
        return LET.ElementTree(self.root)

    def clone_root(self):
        return copy.deepcopy(self.root)

    def clone_base_root(self):
        return copy.deepcopy(self.base_root)

    def xpath(self, xpath, root=None):
        root = root if root is not None else self.root
        text = normalize_xpath(xpath)
        try:
            return LET.ElementTree(root).xpath(text)
        except Exception:
            return []

    def has_def(self, kind, attr, name, root=None):
        return self.find_def(kind, attr, name, root=root) is not None

    def find_def(self, kind, attr, name, root=None):
        root = root if root is not None else self.root
        if attr == "@Name":
            query = f"/Defs/{kind}[@Name={xpath_literal(name)}]"
        else:
            query = f"/Defs/{kind}[defName={xpath_literal(name)}]"
        found = self.xpath(query, root=root)
        return found[0] if found else None

    def target_known_from_xpath(self, xpath, root=None):
        target = parse_target(xpath)
        if not target:
            return False
        return self.has_def(*target, root=root)

    def direct_nodes(self, xpath, root=None):
        return self.xpath(xpath, root=root)

    def effective_text_for_xpath(self, xpath, root=None):
        target = parse_target(xpath)
        rel_path = rel_path_from_target(xpath)
        if not target or rel_path is None:
            return None
        if "[" in rel_path or rel_path == "":
            return None
        return self.effective_text(*target, rel_path=rel_path, root=root)

    def effective_text(self, kind, attr, name, rel_path, root=None):
        root = root if root is not None else self.root
        seen = set()
        current_attr = attr
        current_name = name

        while current_name and (kind, current_name) not in seen:
            seen.add((kind, current_name))
            element = self.find_def(kind, current_attr, current_name, root=root)
            if element is None:
                break
            found = element.xpath(rel_path)
            for node in found:
                if isinstance(getattr(node, "tag", None), str):
                    return (node.text or "").strip()

            parent_name = element.get("ParentName")
            if not parent_name:
                break
            current_attr = "@Name"
            current_name = parent_name.strip()

        return self.default_for_rel_path(kind, rel_path)

    def default_for_rel_path(self, kind, rel_path):
        parts = [part for part in rel_path.split("/") if part]
        if kind == "ThingDef" and len(parts) == 2 and parts[0] == "statBases":
            return STAT_BASE_DEFAULTS.get(parts[1])
        if kind == "ThingDef" and len(parts) == 2 and parts[0] == "race":
            return RACE_FIELD_DEFAULTS.get(parts[1])
        if kind == "PawnKindDef" and len(parts) == 1:
            return PAWN_KIND_FIELD_DEFAULTS.get(parts[0])
        return None


def xpath_literal(value):
    text = str(value)
    if "'" not in text:
        return f"'{text}'"
    if '"' not in text:
        return f'"{text}"'
    parts = text.split("'")
    return "concat(" + ", \"'\", ".join(f"'{part}'" for part in parts) + ")"
