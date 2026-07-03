import copy
import re
from collections import Counter

from lxml import etree as LET

from rimworld_patch_apply import PatchApplier
from rimworld_original_xml import normalize_xpath, parse_target, rel_path_from_target, xpath_literal
from rimworld_patch_defaults import PAWN_KIND_FIELD_DEFAULTS, scalar_values_equal


DIRECT_PATCH_CLASSES = {
    "PatchOperationAdd",
    "PatchOperationAddModExtension",
    "PatchOperationAttributeSet",
    "PatchOperationRemove",
    "PatchOperationReplace",
}

SCALAR_CONTAINER_TAGS = {"race", "statBases"}


class PatchOptimizer:
    _TEXT_PREDICATE_RE = re.compile(r"(?:text\(\)|\.)\s*=\s*(['\"])(.*?)\1")

    def __init__(self, original_index):
        self.index = original_index
        self.stats = Counter()
        self._kept_conditional_applier = PatchApplier(record_missing=False)

    def optimize(self, patch_root):
        if not self.index or not self.index.enabled:
            return patch_root

        sim_root = self.index.clone_base_root() if hasattr(self.index, "clone_base_root") else self.index.clone_root()
        patched_sim_root = self.index.clone_root() if getattr(self.index, "patches_enabled", False) else None
        optimized = LET.Element(patch_root.tag)
        for key, value in patch_root.attrib.items():
            optimized.set(key, value)

        children = self._optimize_nodes(list(patch_root), sim_root, patched_sim_root)
        children = self._combine_remove_add_replaces(children)
        for child in children:
            optimized.append(child)
        return optimized

    def _optimize_nodes(self, nodes, sim_root, patched_sim_root=None):
        out = []
        idx = 0
        while idx < len(nodes):
            if idx + 1 < len(nodes):
                list_ops = self._try_optimize_list_cleanup_pair(
                    nodes[idx],
                    nodes[idx + 1],
                    sim_root,
                    patched_sim_root,
                )
                if list_ops is not None:
                    for op in list_ops:
                        out.append(op)
                        self._apply_operation_to_sim(op, sim_root)
                        if patched_sim_root is not None:
                            self._apply_operation_to_sim(op, patched_sim_root)
                    idx += 2
                    continue

            node = nodes[idx]
            optimized = self._optimize_node(node, sim_root, patched_sim_root)
            if optimized is None:
                idx += 1
                continue
            if isinstance(optimized, list):
                out.extend(optimized)
            else:
                out.append(optimized)
            idx += 1
        return out

    def _optimize_node(self, node, sim_root, patched_sim_root=None):
        if not isinstance(getattr(node, "tag", None), str):
            return copy.deepcopy(node)

        cls = node.get("Class")
        if cls == "PatchOperationConditional":
            return self._resolve_conditional(node, sim_root, patched_sim_root)
        if cls == "PatchOperationSequence":
            return self._optimize_sequence(node, sim_root, patched_sim_root)
        if cls == "PatchOperationFindMod":
            return self._optimize_find_mod(node, sim_root, patched_sim_root)
        if cls in DIRECT_PATCH_CLASSES:
            return self._optimize_direct_operation(node, sim_root, patched_sim_root)

        return copy.deepcopy(node)

    def _resolve_conditional(self, node, sim_root, patched_sim_root=None):
        xpath = self._child_text(node, "xpath")
        if not xpath:
            return copy.deepcopy(node)

        base_known = self.index.target_known_from_xpath(xpath, root=sim_root)
        patched_known = (
            patched_sim_root is not None
            and self.index.target_known_from_xpath(xpath, root=patched_sim_root)
        )
        if not base_known and not patched_known:
            wrapped = self._wrap_unknown_target_operation(node, xpath)
            if wrapped is not None:
                self.stats["unknown_target_ops_wrapped"] += 1
                return wrapped
            return copy.deepcopy(node)

        matched = bool(self.index.direct_nodes(xpath, root=sim_root)) if base_known else False
        if patched_sim_root is not None:
            patched_matched = bool(self.index.direct_nodes(xpath, root=patched_sim_root)) if patched_known else False
            if base_known != patched_known or matched != patched_matched:
                self.stats["conditionals_kept_context_dependent"] += 1
                self._apply_kept_operation_to_sim(node, sim_root)
                self._apply_kept_operation_to_sim(node, patched_sim_root)
                return copy.deepcopy(node)

        branch_name = "match" if matched else "nomatch"
        branch = node.find(branch_name)
        self.stats["conditionals_resolved"] += 1

        if branch is None or not branch.get("Class"):
            self.stats["conditionals_removed_empty_branch"] += 1
            return None

        direct = self._branch_to_operation(branch, node.tag)
        return self._optimize_node(direct, sim_root, patched_sim_root)

    def _optimize_sequence(self, node, sim_root, patched_sim_root=None):
        copied = self._shallow_copy(node)
        for child in node:
            if child.tag == "operations":
                ops_node = LET.SubElement(copied, "operations")
                optimized_children = self._optimize_nodes(list(child), sim_root, patched_sim_root)
                optimized_children = self._combine_remove_add_replaces(optimized_children)
                for optimized in optimized_children:
                    ops_node.append(optimized)
            else:
                copied.append(copy.deepcopy(child))
        return copied

    def _optimize_find_mod(self, node, sim_root, patched_sim_root=None):
        copied = self._shallow_copy(node)
        for child in node:
            if child.tag in ("match", "nomatch") and child.get("Class") == "PatchOperationSequence":
                if child.tag == "match" and patched_sim_root is not None:
                    branch_sim = copy.deepcopy(patched_sim_root)
                else:
                    branch_sim = copy.deepcopy(sim_root)
                copied.append(self._optimize_sequence(child, branch_sim))
            else:
                copied.append(copy.deepcopy(child))
        if patched_sim_root is not None:
            PatchApplier(record_missing=False, apply_find_mod_match=False).apply_operation(node, sim_root)
            PatchApplier(record_missing=False, apply_find_mod_match=True).apply_operation(node, patched_sim_root)
        return copied

    def _optimize_direct_operation(self, node, sim_root, patched_sim_root=None):
        cls = node.get("Class")
        xpath = self._child_text(node, "xpath")
        if xpath and self._target_missing_in_all_known_states(xpath, sim_root, patched_sim_root):
            wrapped = self._wrap_unknown_target_operation(node, xpath)
            if wrapped is not None:
                self.stats["unknown_target_ops_wrapped"] += 1
                self._apply_kept_operation_to_sim(wrapped, sim_root)
                self._apply_kept_operation_to_sim(wrapped, patched_sim_root)
                return wrapped

        if cls == "PatchOperationAttributeSet":
            optimized = self._optimize_attribute_set(node, sim_root, xpath)
        elif cls == "PatchOperationReplace":
            optimized = self._optimize_replace(node, sim_root, xpath)
        elif cls == "PatchOperationRemove":
            optimized = self._optimize_remove(node, sim_root, xpath)
        elif cls == "PatchOperationAdd":
            optimized = self._optimize_add(node, sim_root, xpath)
        elif cls == "PatchOperationAddModExtension":
            optimized = self._optimize_add_mod_extension(node, sim_root, xpath)
        else:
            optimized = copy.deepcopy(node)

        if (
            optimized is None
            and patched_sim_root is not None
            and not self._direct_operation_is_noop(node, patched_sim_root)
        ):
            self.stats["noop_kept_due_original_patches"] += 1
            optimized = copy.deepcopy(node)

        if optimized is not None:
            self._apply_operation_to_sim(optimized, sim_root)
            if patched_sim_root is not None:
                self._apply_operation_to_sim(optimized, patched_sim_root)
        return optimized

    def _optimize_attribute_set(self, node, sim_root, xpath):
        if not xpath or not self.index.target_known_from_xpath(xpath, root=sim_root):
            return copy.deepcopy(node)

        attr_name = self._child_text(node, "attribute")
        value = self._child_text(node, "value")
        if not attr_name:
            return copy.deepcopy(node)
        nodes = self.index.direct_nodes(xpath, root=sim_root)
        if nodes and all(str(target.get(attr_name, "")).strip() == str(value).strip() for target in nodes):
            self.stats["attribute_sets_removed_noop"] += 1
            return None
        return copy.deepcopy(node)

    def _optimize_replace(self, node, sim_root, xpath):
        if not xpath or not self.index.target_known_from_xpath(xpath, root=sim_root):
            return copy.deepcopy(node)

        targets = self.index.direct_nodes(xpath, root=sim_root)
        if not targets:
            self.stats["replaces_without_target_kept"] += 1
            return copy.deepcopy(node)

        value_children = self._value_children(node)
        if len(value_children) == 1 and len(targets) == 1:
            target = targets[0]
            replacement = value_children[0]
            if self._elements_equal_scalar_or_xml(target, replacement):
                self.stats["replaces_removed_noop"] += 1
                return None

        return copy.deepcopy(node)

    def _optimize_remove(self, node, sim_root, xpath):
        if not xpath or not self.index.target_known_from_xpath(xpath, root=sim_root):
            return copy.deepcopy(node)

        targets = self.index.direct_nodes(xpath, root=sim_root)
        if not targets:
            self.stats["removes_removed_noop"] += 1
            return None
        return copy.deepcopy(node)

    def _optimize_add(self, node, sim_root, xpath):
        if not xpath:
            return copy.deepcopy(node)

        target = parse_target(xpath)
        if not target or not self.index.has_def(*target, root=sim_root):
            return copy.deepcopy(node)

        value_children = self._value_children(node)
        if len(value_children) == 1 and self._is_scalar_add_noop(xpath, value_children[0], sim_root):
            self.stats["adds_removed_effective_noop"] += 1
            return None

        return copy.deepcopy(node)

    def _optimize_add_mod_extension(self, node, sim_root, xpath):
        if not xpath:
            return copy.deepcopy(node)

        target = parse_target(xpath)
        if not target or not self.index.has_def(*target, root=sim_root):
            return copy.deepcopy(node)

        return copy.deepcopy(node)

    def _is_scalar_add_noop(self, add_xpath, value_child, sim_root):
        rel_parent = rel_path_from_target(add_xpath)
        if rel_parent is None:
            return False
        parent_parts = [part for part in rel_parent.split("/") if part]
        target = parse_target(add_xpath)
        if not target:
            return False
        if value_child.attrib or any(
            isinstance(getattr(child, "tag", None), str)
            for child in value_child
        ):
            return False
        kind = target[0]
        if value_child.tag in SCALAR_CONTAINER_TAGS:
            return False
        if parent_parts:
            if parent_parts[-1] not in SCALAR_CONTAINER_TAGS:
                return False
        elif kind != "PawnKindDef" or value_child.tag not in PAWN_KIND_FIELD_DEFAULTS:
            return False
        if "[" in rel_parent or value_child.tag == "li":
            return False

        full_xpath = normalize_xpath(add_xpath).rstrip("/") + "/" + value_child.tag
        direct = self.index.direct_nodes(full_xpath, root=sim_root)
        if direct:
            return False
        effective = self.index.effective_text_for_xpath(full_xpath, root=sim_root)
        if effective is None:
            return False
        return scalar_values_equal(effective, value_child.text or "")

    def _direct_operation_is_noop(self, node, sim_root):
        cls = node.get("Class")
        xpath = self._child_text(node, "xpath")
        if not xpath:
            return False

        if cls == "PatchOperationAttributeSet":
            attr_name = self._child_text(node, "attribute")
            value = self._child_text(node, "value")
            if not attr_name:
                return False
            targets = self.index.direct_nodes(xpath, root=sim_root)
            return bool(targets) and all(str(target.get(attr_name, "")).strip() == str(value).strip() for target in targets)

        if cls == "PatchOperationReplace":
            targets = self.index.direct_nodes(xpath, root=sim_root)
            value_children = self._value_children(node)
            if len(value_children) != 1 or len(targets) != 1:
                return False
            return self._elements_equal_scalar_or_xml(targets[0], value_children[0])

        if cls == "PatchOperationRemove":
            return not self.index.direct_nodes(xpath, root=sim_root)

        if cls == "PatchOperationAdd":
            value_children = self._value_children(node)
            return len(value_children) == 1 and self._is_scalar_add_noop(xpath, value_children[0], sim_root)

        return False

    def _apply_kept_operation_to_sim(self, node, sim_root):
        if sim_root is None:
            return
        self._kept_conditional_applier.apply_operation(copy.deepcopy(node), sim_root)

    def _target_missing_in_all_known_states(self, xpath, sim_root, patched_sim_root=None):
        target = parse_target(xpath)
        if not target:
            return False
        if self.index.has_def(*target, root=sim_root):
            return False
        if patched_sim_root is not None and self.index.has_def(*target, root=patched_sim_root):
            return False
        return True

    def _wrap_unknown_target_operation(self, node, xpath):
        target = parse_target(xpath)
        if not target:
            return None
        root_xpath = self._root_xpath_for_target(target)
        wrapper = LET.Element(node.tag, Class="PatchOperationConditional")
        LET.SubElement(wrapper, "xpath").text = root_xpath
        match = LET.SubElement(wrapper, "match", Class="PatchOperationSequence")
        operations = LET.SubElement(match, "operations")
        operations.append(self._as_sequence_item(node))
        return wrapper

    def _root_xpath_for_target(self, target):
        kind, attr, name = target
        if attr == "@Name":
            return f"/Defs/{kind}[@Name={xpath_literal(name)}]"
        return f"/Defs/{kind}[defName={xpath_literal(name)}]"

    def _as_sequence_item(self, node):
        copied = copy.deepcopy(node)
        if copied.tag == "li":
            return copied
        item = LET.Element("li")
        for key, value in copied.attrib.items():
            item.set(key, value)
        item.text = copied.text
        item.tail = copied.tail
        for child in copied:
            item.append(copy.deepcopy(child))
        return item

    def _combine_remove_add_replaces(self, nodes):
        combined = []
        idx = 0
        while idx < len(nodes):
            current = nodes[idx]
            nxt = nodes[idx + 1] if idx + 1 < len(nodes) else None
            replacement = self._try_combine_remove_add(current, nxt)
            if replacement is not None:
                combined.append(replacement)
                self.stats["remove_add_combined_to_replace"] += 1
                idx += 2
                continue
            combined.append(current)
            idx += 1
        return combined

    def _try_combine_remove_add(self, remove_node, add_node):
        if (
            remove_node is None
            or add_node is None
            or not isinstance(getattr(remove_node, "tag", None), str)
            or not isinstance(getattr(add_node, "tag", None), str)
            or remove_node.get("Class") != "PatchOperationRemove"
            or add_node.get("Class") != "PatchOperationAdd"
        ):
            return None

        remove_xpath = self._child_text(remove_node, "xpath")
        add_xpath = self._child_text(add_node, "xpath")
        if not remove_xpath or not add_xpath:
            return None

        remove_norm = normalize_xpath(remove_xpath).rstrip("/")
        add_norm = normalize_xpath(add_xpath).rstrip("/")
        if "/" not in remove_norm:
            return None
        parent_xpath, child_tag = remove_norm.rsplit("/", 1)
        if "[" in child_tag or parent_xpath != add_norm:
            return None

        value_children = self._value_children(add_node)
        if len(value_children) != 1 or value_children[0].tag != child_tag:
            return None

        repl = LET.Element(remove_node.tag, Class="PatchOperationReplace")
        LET.SubElement(repl, "xpath").text = remove_xpath
        value = LET.SubElement(repl, "value")
        value.append(copy.deepcopy(value_children[0]))
        return repl

    def _try_optimize_list_cleanup_pair(self, remove_node, add_node, sim_root, patched_sim_root=None):
        base_ops = self._list_cleanup_ops_for_state(remove_node, add_node, sim_root)
        if base_ops is None:
            return None

        if patched_sim_root is not None:
            patched_ops = self._list_cleanup_ops_for_state(remove_node, add_node, patched_sim_root)
            if patched_ops is None:
                return None
            if self._ops_signature(base_ops) != self._ops_signature(patched_ops):
                return None

        self.stats["list_cleanup_pairs_optimized"] += 1
        if not base_ops:
            self.stats["list_cleanup_pairs_removed_noop"] += 1
        return base_ops

    def _list_cleanup_ops_for_state(self, remove_node, add_node, sim_root):
        cleanup = self._extract_list_cleanup_remove(remove_node)
        if cleanup is None:
            return None
        parent_xpath, removable_texts = cleanup
        add_info = self._extract_list_cleanup_add(add_node, parent_xpath)
        if add_info is None:
            return None
        desired_items = add_info
        if len({self._li_key(item) for item in desired_items}) != len(desired_items):
            return None

        containers = self.index.direct_nodes(parent_xpath, root=sim_root)
        if len(containers) != 1:
            return None

        container = containers[0]
        existing_items = [
            child for child in container
            if isinstance(getattr(child, "tag", None), str)
            and child.tag == "li"
            and self._li_key(child) in removable_texts
        ]
        if len({self._li_key(item) for item in existing_items}) != len(existing_items):
            return None

        desired_by_key = {self._li_key(item): item for item in desired_items}
        existing_by_key = {self._li_key(item): item for item in existing_items}
        ops = []

        for key, existing in existing_by_key.items():
            desired = desired_by_key.get(key)
            item_xpath = self._list_item_xpath(parent_xpath, key)
            if desired is None:
                ops.append(self._make_remove_op(remove_node.tag, item_xpath))
            elif not self._elements_equal_scalar_or_xml(existing, desired):
                ops.append(self._make_replace_op(remove_node.tag, item_xpath, desired))

        missing_items = [
            desired for key, desired in desired_by_key.items()
            if key not in existing_by_key
        ]
        if missing_items:
            ops.append(self._make_add_op(add_node.tag, parent_xpath, missing_items))

        return ops

    def _extract_list_cleanup_remove(self, node):
        if not isinstance(getattr(node, "tag", None), str):
            return None
        cls = node.get("Class")
        xpath = None
        if cls == "PatchOperationRemove":
            xpath = self._child_text(node, "xpath")
        elif cls == "PatchOperationConditional":
            match = node.find("match")
            if match is None or match.get("Class") != "PatchOperationRemove":
                return None
            xpath = self._child_text(match, "xpath") or self._child_text(node, "xpath")
        else:
            return None

        parent_xpath, predicate = self._split_list_item_xpath(xpath)
        if parent_xpath is None:
            return None
        if not (
            parent_xpath.endswith("/race/specialTrainables")
            or parent_xpath.endswith("/tradeTags")
        ):
            return None
        values = self._predicate_text_values(predicate)
        if not values:
            return None
        return parent_xpath, set(values)

    def _extract_list_cleanup_add(self, node, expected_parent_xpath):
        if not isinstance(getattr(node, "tag", None), str):
            return None
        cls = node.get("Class")
        if cls == "PatchOperationAdd":
            xpath = normalize_xpath(self._child_text(node, "xpath")).rstrip("/")
            if xpath != expected_parent_xpath:
                return None
            return self._list_value_items(node)

        if cls != "PatchOperationConditional":
            return None
        match = node.find("match")
        if match is None or match.get("Class") != "PatchOperationAdd":
            return None
        xpath = normalize_xpath(self._child_text(match, "xpath")).rstrip("/")
        if xpath != expected_parent_xpath:
            return None
        return self._list_value_items(match)

    def _split_list_item_xpath(self, xpath):
        text = normalize_xpath(xpath).strip().rstrip("/")
        marker = "/li["
        if marker not in text or not text.endswith("]"):
            return None, None
        parent, predicate = text.rsplit(marker, 1)
        return parent, predicate[:-1]

    def _predicate_text_values(self, predicate):
        values = []
        idx = 0
        text = predicate or ""
        while idx < len(text):
            match = self._TEXT_PREDICATE_RE.search(text, idx)
            if not match:
                break
            values.append(match.group(2))
            idx = match.end()
        return values

    def _list_value_items(self, node):
        items = self._value_children(node)
        if not items or any(item.tag != "li" for item in items):
            return None
        return [copy.deepcopy(item) for item in items]

    def _li_key(self, item):
        return (item.text or "").strip()

    def _list_item_xpath(self, parent_xpath, text):
        return f"{parent_xpath}/li[text()={xpath_literal(text)}]"

    def _make_remove_op(self, tag, xpath):
        op = LET.Element(tag, Class="PatchOperationRemove")
        LET.SubElement(op, "xpath").text = xpath
        return op

    def _make_add_op(self, tag, xpath, items):
        op = LET.Element(tag, Class="PatchOperationAdd")
        LET.SubElement(op, "xpath").text = xpath
        value = LET.SubElement(op, "value")
        for item in items:
            value.append(copy.deepcopy(item))
        return op

    def _make_replace_op(self, tag, xpath, item):
        op = LET.Element(tag, Class="PatchOperationReplace")
        LET.SubElement(op, "xpath").text = xpath
        value = LET.SubElement(op, "value")
        value.append(copy.deepcopy(item))
        return op

    def _ops_signature(self, ops):
        return [
            LET.tostring(op, encoding="utf-8")
            for op in ops
        ]

    def _apply_operation_to_sim(self, node, sim_root):
        cls = node.get("Class")
        xpath = self._child_text(node, "xpath")
        if not xpath:
            return

        if cls == "PatchOperationAttributeSet":
            attr_name = self._child_text(node, "attribute")
            value = self._child_text(node, "value")
            if not attr_name:
                return
            for target in self.index.direct_nodes(xpath, root=sim_root):
                target.set(attr_name, "" if value is None else str(value))
            return

        if cls == "PatchOperationRemove":
            for target in list(self.index.direct_nodes(xpath, root=sim_root)):
                parent = target.getparent()
                if parent is not None:
                    parent.remove(target)
            return

        if cls == "PatchOperationReplace":
            replacements = self._value_children(node)
            if not replacements:
                return
            for target in list(self.index.direct_nodes(xpath, root=sim_root)):
                parent = target.getparent()
                if parent is None:
                    continue
                insert_at = parent.index(target)
                parent.remove(target)
                for offset, repl in enumerate(replacements):
                    parent.insert(insert_at + offset, copy.deepcopy(repl))
            return

        if cls == "PatchOperationAdd":
            additions = self._value_children(node)
            if not additions:
                return
            for target in self.index.direct_nodes(xpath, root=sim_root):
                if not isinstance(getattr(target, "tag", None), str):
                    continue
                for addition in additions:
                    target.append(copy.deepcopy(addition))

        if cls == "PatchOperationAddModExtension":
            additions = self._value_children(node)
            if not additions:
                return
            for target in self.index.direct_nodes(xpath, root=sim_root):
                if not isinstance(getattr(target, "tag", None), str):
                    continue
                if target.tag == "modExtensions":
                    container = target
                else:
                    container = target.find("modExtensions")
                    if container is None:
                        container = LET.SubElement(target, "modExtensions")
                for addition in additions:
                    container.append(copy.deepcopy(addition))

    def _elements_equal_scalar_or_xml(self, left, right):
        if left.tag != right.tag:
            return False
        left_children = [child for child in left if isinstance(getattr(child, "tag", None), str)]
        right_children = [child for child in right if isinstance(getattr(child, "tag", None), str)]
        if not left_children and not right_children:
            return scalar_values_equal(left.text or "", right.text or "") and dict(left.attrib) == dict(right.attrib)
        return self._canonical_xml(left) == self._canonical_xml(right)

    def _canonical_xml(self, element):
        copied = copy.deepcopy(element)
        self._strip_whitespace(copied)
        return LET.tostring(copied, encoding="utf-8")

    def _strip_whitespace(self, element):
        if element.text is not None and element.text.strip() == "":
            element.text = None
        if element.tail is not None and element.tail.strip() == "":
            element.tail = None
        for child in element:
            if isinstance(getattr(child, "tag", None), str):
                self._strip_whitespace(child)

    def _branch_to_operation(self, branch, tag):
        direct = LET.Element(tag)
        for key, value in branch.attrib.items():
            direct.set(key, value)
        direct.text = branch.text
        for child in branch:
            direct.append(copy.deepcopy(child))
        return direct

    def _shallow_copy(self, node):
        copied = LET.Element(node.tag)
        for key, value in node.attrib.items():
            copied.set(key, value)
        copied.text = node.text
        copied.tail = node.tail
        return copied

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
