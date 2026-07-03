import copy
from collections import Counter

from lxml import etree as LET


DIRECT_PATCH_CLASSES = {
    "PatchOperationAdd",
    "PatchOperationAddModExtension",
    "PatchOperationAttributeSet",
    "PatchOperationRemove",
    "PatchOperationReplace",
}


def normalize_patch_xpath(xpath):
    text = (xpath or "").strip()
    if text.startswith("Defs/"):
        return "/" + text
    return text


class PatchApplier:
    def __init__(self, stats=None, errors=None, apply_find_mod_match=True, record_missing=True, active_mods=None):
        self.stats = stats if stats is not None else Counter()
        self.errors = errors if errors is not None else []
        self.missing_targets = []
        self.apply_find_mod_match = apply_find_mod_match
        self.record_missing = record_missing
        self.active_mods = self._normalize_mod_set(active_mods) if active_mods is not None else None

    def apply_patch_root(self, patch_root, sim_root, source_path=None):
        if patch_root is None:
            return
        if self._is_operation(patch_root):
            self.apply_operation(patch_root, sim_root, source_path=source_path)
            return
        for node in patch_root:
            self.apply_operation(node, sim_root, source_path=source_path)

    def apply_operation(self, node, sim_root, source_path=None):
        if not self._is_operation(node):
            return

        cls = node.get("Class")
        self.stats["operations_seen"] += 1

        if cls == "PatchOperationSequence":
            self._apply_sequence(node, sim_root, source_path)
            return

        if cls == "PatchOperationConditional":
            self._apply_conditional(node, sim_root, source_path)
            return

        if cls == "PatchOperationFindMod":
            self._apply_find_mod(node, sim_root, source_path)
            return

        if cls in DIRECT_PATCH_CLASSES:
            self.apply_direct_operation(node, sim_root, source_path=source_path)
            return

        self.stats["unknown_operations"] += 1

    def apply_direct_operation(self, node, sim_root, source_path=None):
        cls = node.get("Class")
        xpath = self._child_text(node, "xpath")
        if not xpath:
            self.stats["operations_without_xpath"] += 1
            return

        if cls == "PatchOperationAttributeSet":
            self._apply_attribute_set(node, sim_root, xpath, source_path)
        elif cls == "PatchOperationRemove":
            self._apply_remove(node, sim_root, xpath, source_path)
        elif cls == "PatchOperationReplace":
            self._apply_replace(node, sim_root, xpath, source_path)
        elif cls == "PatchOperationAdd":
            self._apply_add(node, sim_root, xpath, source_path)
        elif cls == "PatchOperationAddModExtension":
            self._apply_add_mod_extension(node, sim_root, xpath, source_path)

    def _apply_sequence(self, node, sim_root, source_path):
        operations = node.find("operations")
        if operations is None:
            self.stats["empty_sequences"] += 1
            return
        for child in operations:
            self.apply_operation(child, sim_root, source_path=source_path)

    def _apply_conditional(self, node, sim_root, source_path):
        xpath = self._child_text(node, "xpath")
        if not xpath:
            self.stats["conditionals_without_xpath"] += 1
            return

        matched = bool(self._xpath(sim_root, xpath, source_path))
        branch_name = "match" if matched else "nomatch"
        branch = node.find(branch_name)
        self.stats["conditionals_resolved"] += 1
        self.stats[f"conditionals_{branch_name}"] += 1
        self._apply_branch(branch, sim_root, source_path)

    def _apply_find_mod(self, node, sim_root, source_path):
        branch_name = "match" if self._find_mod_matches(node) else "nomatch"
        branch = node.find(branch_name)
        self.stats[f"find_mod_{branch_name}_selected"] += 1
        self._apply_branch(branch, sim_root, source_path)

    def _find_mod_matches(self, node):
        if self.active_mods is None:
            return bool(self.apply_find_mod_match)
        mods = node.find("mods")
        if mods is None:
            return False
        wanted = {
            self._normalize_mod_name(li.text)
            for li in mods.findall("li")
            if li.text and li.text.strip()
        }
        return bool(wanted & self.active_mods)

    def _normalize_mod_set(self, values):
        return {self._normalize_mod_name(value) for value in values if str(value).strip()}

    def _normalize_mod_name(self, value):
        return " ".join(str(value).strip().lower().split())

    def _apply_branch(self, branch, sim_root, source_path):
        if branch is None or not isinstance(getattr(branch, "tag", None), str):
            self.stats["empty_branches"] += 1
            return
        if branch.get("Class"):
            self.apply_operation(branch, sim_root, source_path=source_path)
            return
        applied = 0
        for child in branch:
            if self._is_operation(child):
                self.apply_operation(child, sim_root, source_path=source_path)
                applied += 1
        if not applied:
            self.stats["empty_branches"] += 1

    def _apply_attribute_set(self, node, sim_root, xpath, source_path):
        attr_name = self._child_text(node, "attribute")
        value = self._child_text(node, "value")
        if not attr_name:
            self.stats["attribute_sets_without_attribute"] += 1
            return
        targets = self._xpath(sim_root, xpath, source_path)
        if not targets:
            self._record_missing("attribute_set", xpath, source_path)
            return
        for target in targets:
            if isinstance(getattr(target, "tag", None), str):
                target.set(attr_name, "" if value is None else str(value))
        self.stats["attribute_sets_applied"] += 1

    def _apply_remove(self, node, sim_root, xpath, source_path):
        targets = list(self._xpath(sim_root, xpath, source_path))
        if not targets:
            self._record_missing("remove", xpath, source_path)
            return
        removed = 0
        for target in targets:
            parent = target.getparent() if hasattr(target, "getparent") else None
            if parent is not None:
                parent.remove(target)
                removed += 1
        if removed:
            self.stats["removes_applied"] += 1

    def _apply_replace(self, node, sim_root, xpath, source_path):
        targets = list(self._xpath(sim_root, xpath, source_path))
        if not targets:
            self._record_missing("replace", xpath, source_path)
            return
        replacements = self._value_children(node)
        if not replacements:
            self.stats["replaces_without_value"] += 1
            return
        applied = 0
        for target in targets:
            parent = target.getparent() if hasattr(target, "getparent") else None
            if parent is None:
                continue
            insert_at = parent.index(target)
            parent.remove(target)
            for offset, repl in enumerate(replacements):
                parent.insert(insert_at + offset, copy.deepcopy(repl))
            applied += 1
        if applied:
            self.stats["replaces_applied"] += 1

    def _apply_add(self, node, sim_root, xpath, source_path):
        targets = self._xpath(sim_root, xpath, source_path)
        if not targets:
            self._record_missing("add", xpath, source_path)
            return
        additions = self._value_children(node)
        if not additions:
            self.stats["adds_without_value"] += 1
            return
        applied = 0
        for target in targets:
            if not isinstance(getattr(target, "tag", None), str):
                continue
            for addition in additions:
                target.append(copy.deepcopy(addition))
            applied += 1
        if applied:
            self.stats["adds_applied"] += 1

    def _apply_add_mod_extension(self, node, sim_root, xpath, source_path):
        targets = self._xpath(sim_root, xpath, source_path)
        if not targets:
            self._record_missing("add_mod_extension", xpath, source_path)
            return
        additions = self._value_children(node)
        if not additions:
            self.stats["add_mod_extensions_without_value"] += 1
            return
        applied = 0
        for target in targets:
            if not isinstance(getattr(target, "tag", None), str):
                continue
            if target.tag == "modExtensions":
                container = target
            else:
                container = target.find("modExtensions")
                if container is None:
                    container = LET.SubElement(target, "modExtensions")
                    self.stats["mod_extension_containers_created"] += 1
            for addition in additions:
                container.append(copy.deepcopy(addition))
            applied += 1
        if applied:
            self.stats["add_mod_extensions_applied"] += 1

    def _xpath(self, sim_root, xpath, source_path):
        text = normalize_patch_xpath(xpath)
        try:
            return LET.ElementTree(sim_root).xpath(text)
        except Exception as exc:
            self.stats["xpath_errors"] += 1
            if len(self.errors) < 50:
                self.errors.append((source_path, text, str(exc)))
            return []

    def _record_missing(self, op_name, xpath, source_path):
        if not self.record_missing:
            return
        self.stats[f"{op_name}_missing_targets"] += 1
        self.stats["missing_targets"] += 1
        if len(self.missing_targets) < 50:
            self.missing_targets.append((source_path, op_name, normalize_patch_xpath(xpath)))

    def _is_operation(self, node):
        return isinstance(getattr(node, "tag", None), str) and bool(node.get("Class"))

    def _child_text(self, node, child_tag):
        child = node.find(child_tag)
        if child is None or child.text is None:
            return ""
        return child.text.strip()

    def _value_children(self, node):
        value = node.find("value")
        if value is None:
            return []
        return [child for child in value if isinstance(getattr(child, "tag", None), str)]
