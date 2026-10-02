#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Post-generation compaction for RimWorld XML patches.

The compactor works on one patch file at a time.  It never combines files and
``compact_file`` overwrites the input file in place.  Its main optimization is
turning many per-Def patch operations into one operation with a shared XPath.
Only operations whose target Def can be proved distinct are reordered.
"""
from __future__ import annotations

import argparse
import copy
import os
import re
import heapq
from collections import Counter, defaultdict
from dataclasses import dataclass, asdict
from pathlib import Path
from typing import Iterable, Optional

from lxml import etree as LET


PATCH_PREFIX = "PatchOperation"
DIRECT_CLASSES = {
    "PatchOperationAdd",
    "PatchOperationAddModExtension",
    "PatchOperationAttributeSet",
    "PatchOperationRemove",
    "PatchOperationReplace",
}
ADD_CLASSES = {"PatchOperationAdd", "PatchOperationAddModExtension"}

_SUBJECT_RE = re.compile(
    r"^(?P<slash>/?)Defs/(?P<def_type>[A-Za-z_][A-Za-z0-9_.]*)"
    r"\[\s*(?P<selector>defName|@Name)\s*=\s*"
    r"(?P<quote>['\"])(?P<name>.*?)(?P=quote)\s*\](?P<tail>/.*)?$"
)


@dataclass(frozen=True)
class SubjectXPath:
    leading_slash: bool
    def_type: str
    selector: str
    name: str
    tail: str

    @property
    def subject(self):
        return (self.def_type, self.selector, self.name)


@dataclass
class CompactionReport:
    path: str
    bytes_before: int
    bytes_after: int
    operations_before: int
    operations_after: int
    xpaths_before: int
    xpaths_after: int
    conditionals_before: int
    conditionals_after: int
    findmods_before: int
    findmods_after: int
    passes: int = 0

    @property
    def bytes_saved(self):
        return self.bytes_before - self.bytes_after

    @property
    def operations_saved(self):
        return self.operations_before - self.operations_after

    @property
    def xpaths_saved(self):
        return self.xpaths_before - self.xpaths_after

    def to_dict(self):
        data = asdict(self)
        data.update(
            bytes_saved=self.bytes_saved,
            operations_saved=self.operations_saved,
            xpaths_saved=self.xpaths_saved,
        )
        return data


def _norm_xpath(text: Optional[str]) -> str:
    return " ".join((text or "").strip().split())


def _parse_subject_xpath(text: Optional[str]) -> Optional[SubjectXPath]:
    value = _norm_xpath(text)
    match = _SUBJECT_RE.match(value)
    if not match:
        return None
    return SubjectXPath(
        leading_slash=bool(match.group("slash")),
        def_type=match.group("def_type"),
        selector=match.group("selector"),
        name=match.group("name"),
        tail=match.group("tail") or "",
    )


def _xpath_literal(value: str) -> str:
    if '"' not in value:
        return f'"{value}"'
    if "'" not in value:
        return f"'{value}'"
    pieces = value.split('"')
    args = []
    for index, piece in enumerate(pieces):
        if piece:
            args.append(f'"{piece}"')
        if index != len(pieces) - 1:
            args.append("'\"'")
    return "concat(" + ",".join(args) + ")"


def _combine_xpaths(xpaths: Iterable[str]) -> str:
    values = []
    seen = set()
    for xpath in xpaths:
        value = _norm_xpath(xpath)
        if value and value not in seen:
            seen.add(value)
            values.append(value)
    if not values:
        return ""
    if len(values) == 1:
        return values[0]

    parsed = [_parse_subject_xpath(value) for value in values]
    if all(item is not None for item in parsed):
        first = parsed[0]
        if all(
            item.def_type == first.def_type
            and item.selector == first.selector
            and item.tail == first.tail
            and item.leading_slash == first.leading_slash
            for item in parsed[1:]
        ):
            names = []
            seen_names = set()
            for item in parsed:
                if item.name not in seen_names:
                    seen_names.add(item.name)
                    names.append(item.name)
            predicate = " or ".join(
                f"{first.selector}={_xpath_literal(name)}" for name in names
            )
            slash = "/" if first.leading_slash else ""
            return f"{slash}Defs/{first.def_type}[{predicate}]{first.tail}"

    return " | ".join(values)



def _payload_signature(node) -> bytes:
    clone = copy.deepcopy(node)
    for child in list(clone):
        if isinstance(getattr(child, "tag", None), str) and child.tag == "xpath":
            clone.remove(child)
    clone.tail = None
    return LET.tostring(clone, method="c14n", with_comments=False)


def _set_xpath(node, xpath: str):
    child = node.find("xpath")
    if child is None:
        child = LET.Element("xpath")
        node.insert(0, child)
    child.text = xpath


def _operation_class(node) -> str:
    return node.get("Class", "") if isinstance(getattr(node, "tag", None), str) else ""


def _is_patch_operation(node) -> bool:
    return _operation_class(node).startswith(PATCH_PREFIX)


def patch_metrics(root) -> dict:
    operations = [node for node in root.iter() if _is_patch_operation(node)]
    return {
        "operations": len(operations),
        "xpaths": sum(1 for node in operations for child in node if child.tag == "xpath"),
        "conditionals": sum(1 for node in operations if node.get("Class") == "PatchOperationConditional"),
        "findmods": sum(1 for node in operations if node.get("Class") == "PatchOperationFindMod"),
        "bytes": len(LET.tostring(root, encoding="utf-8", pretty_print=False, with_tail=False)),
    }


@dataclass
class _GroupSpec:
    node: object
    kind: str
    key: tuple
    subject: tuple
    target_xpath: str
    parsed_target: SubjectXPath
    add_xpath: str = ""
    add_class: str = ""


class PatchCompactor:
    """Compact a single parsed ``<Patch>`` tree.

    ``compact`` returns a compacted deep copy and reaches an internal fixed
    point before returning, so running it again is idempotent.
    """

    def __init__(self, max_passes: int = 12):
        self.max_passes = max(1, int(max_passes))
        self.passes = 0

    def compact(self, patch_root):
        root = copy.deepcopy(patch_root)
        self._strip_comments(root)
        previous = None
        for pass_number in range(1, self.max_passes + 1):
            self._compact_container(root)
            self._strip_comments(root)
            current = LET.tostring(root, method="c14n", with_comments=False)
            self.passes = pass_number
            if current == previous:
                break
            previous = current
        return root

    def _strip_comments(self, root):
        for comment in list(root.xpath("//comment()")):
            parent = comment.getparent()
            if parent is not None:
                parent.remove(comment)
        for elem in root.iter():
            if isinstance(getattr(elem, "tag", None), str):
                if elem.text is not None and not elem.text.strip() and len(elem):
                    elem.text = None
                elem.tail = None

    def _compact_container(self, container):
        # Compact only through semantic wrappers that do not introduce ordered
        # short-circuit execution.  PatchOperationSequence is an execution
        # barrier in RimWorld: it stops on the first failed child operation.
        # Reordering, merging or flattening operations inside/across a Sequence
        # can therefore change which later patches execute.
        for child in list(container):
            if _is_patch_operation(child):
                self._compact_operation(child)

        self._merge_adjacent_same_target_adds(container)

        # Coalesce subject-local operations.  Non-groupable operations are hard
        # barriers, so no operation is moved across a context we cannot prove
        # independent.
        children = [child for child in list(container) if _is_patch_operation(child)]
        if children and len(children) == len([c for c in list(container) if isinstance(getattr(c, 'tag', None), str)]):
            new_children = self._coalesce_segments(children)
            for child in list(container):
                container.remove(child)
            for child in new_children:
                container.append(child)

        # Do not merge adjacent PatchOperationFindMod wrappers.  The merge
        # implementation necessarily combines their branches into a Sequence;
        # a failed child would then short-circuit later formerly-independent
        # top-level FindMod operations.

        # Earlier merges can create new direct branches; compact those without
        # crossing Sequence barriers.
        for child in list(container):
            if _is_patch_operation(child):
                self._compact_operation(child)
        self._merge_adjacent_same_target_adds(container)

    def _compact_operation(self, node):
        cls = _operation_class(node)
        if cls == "PatchOperationSequence":
            # Opaque by design.  RimWorld PatchOperationSequence aborts on the
            # first child that returns false, so even transformations that are
            # final-XML-equivalent can change runtime patch semantics.
            return
        if cls in {"PatchOperationConditional", "PatchOperationFindMod"}:
            for branch_name in ("match", "nomatch"):
                branch = node.find(branch_name)
                if branch is not None and _is_patch_operation(branch):
                    self._compact_operation(branch)

    def _collapse_sequence_node(self, node):
        if _operation_class(node) != "PatchOperationSequence":
            return
        operations = node.find("operations")
        if operations is None:
            return
        children = [child for child in operations if _is_patch_operation(child)]
        if len(children) != 1:
            return
        only = copy.deepcopy(children[0])
        tag = node.tag
        tail = node.tail
        node.clear()
        node.tag = tag
        node.attrib.update(only.attrib)
        node.text = only.text
        for child in only:
            node.append(copy.deepcopy(child))
        node.tail = tail

    def _flatten_sequence_children(self, container):
        # Only <operations> and <Patch> containers have sibling semantics.
        if container.tag not in {"Patch", "operations"}:
            return
        output = []
        changed = False
        for child in list(container):
            if _operation_class(child) == "PatchOperationSequence":
                operations = child.find("operations")
                seq_children = [c for c in operations] if operations is not None else []
                seq_children = [c for c in seq_children if _is_patch_operation(c)]
                if seq_children:
                    for seq_child in seq_children:
                        clone = copy.deepcopy(seq_child)
                        clone.tag = child.tag
                        output.append(clone)
                    changed = True
                    continue
            output.append(child)
        if changed:
            for child in list(container):
                container.remove(child)
            for child in output:
                container.append(child)

    def _spec_for(self, node) -> Optional[_GroupSpec]:
        cls = _operation_class(node)
        if cls in DIRECT_CLASSES:
            xpath = _norm_xpath(node.findtext("xpath"))
            parsed = _parse_subject_xpath(xpath)
            if not parsed:
                return None
            key = ("direct", cls, parsed.def_type, parsed.selector, parsed.tail, _payload_signature(node))
            return _GroupSpec(node, "direct", key, parsed.subject, xpath, parsed)

        if cls != "PatchOperationConditional":
            return None

        condition_xpath = _norm_xpath(node.findtext("xpath"))
        parsed = _parse_subject_xpath(condition_xpath)
        if not parsed:
            return None
        match = node.find("match")
        nomatch = node.find("nomatch")
        match_cls = _operation_class(match) if match is not None else ""
        nomatch_cls = _operation_class(nomatch) if nomatch is not None else ""

        # Guarded remove: if any member exists, one union Remove removes exactly
        # the same existing nodes as N independent guards.
        if match_cls == "PatchOperationRemove" and not nomatch_cls:
            match_xpath = _norm_xpath(match.findtext("xpath"))
            if match_xpath != condition_xpath:
                return None
            key = ("remove_cond", parsed.def_type, parsed.selector, parsed.tail)
            return _GroupSpec(node, "remove_cond", key, parsed.subject, condition_xpath, parsed)

        # Replace-or-add upsert.  The aggregate form uses two guards: one for
        # existing targets and one selecting only parent nodes where the target
        # is absent, so mixed presence across Defs is preserved.
        if match_cls == "PatchOperationReplace" and nomatch_cls in ADD_CLASSES:
            match_xpath = _norm_xpath(match.findtext("xpath"))
            add_xpath = _norm_xpath(nomatch.findtext("xpath"))
            add_parsed = _parse_subject_xpath(add_xpath)
            if match_xpath != condition_xpath or not add_parsed:
                return None
            if add_parsed.subject != parsed.subject:
                return None
            relative = self._relative_target(add_xpath, condition_xpath)
            if not relative:
                return None
            if _payload_signature(match) != _payload_signature(nomatch):
                # Class differs by design, so compare values explicitly below.
                if self._value_signature(match) != self._value_signature(nomatch):
                    return None
            key = (
                "upsert",
                parsed.def_type,
                parsed.selector,
                parsed.tail,
                add_parsed.tail,
                nomatch_cls,
                self._value_signature(match),
            )
            return _GroupSpec(node, "upsert", key, parsed.subject, condition_xpath, parsed, add_xpath, nomatch_cls)

        # Add-only missing guard (common for mod extensions).  Convert N
        # independent nomatch guards into one guard over the union of parents
        # that are actually missing the target.
        if not match_cls and nomatch_cls in ADD_CLASSES:
            add_xpath = _norm_xpath(nomatch.findtext("xpath"))
            add_parsed = _parse_subject_xpath(add_xpath)
            if not add_parsed or add_parsed.subject != parsed.subject:
                return None
            relative = self._relative_target(add_xpath, condition_xpath)
            if not relative:
                return None
            key = (
                "missing_add",
                parsed.def_type,
                parsed.selector,
                parsed.tail,
                add_parsed.tail,
                nomatch_cls,
                self._value_signature(nomatch),
            )
            return _GroupSpec(node, "missing_add", key, parsed.subject, condition_xpath, parsed, add_xpath, nomatch_cls)

        return None

    def _value_signature(self, node) -> bytes:
        value = node.find("value")
        if value is None:
            return b""
        clone = copy.deepcopy(value)
        clone.tail = None
        return LET.tostring(clone, method="c14n", with_comments=False)

    def _relative_target(self, parent_xpath: str, target_xpath: str) -> str:
        parent = _norm_xpath(parent_xpath)
        target = _norm_xpath(target_xpath)
        prefix = parent.rstrip("/") + "/"
        if not target.startswith(prefix):
            return ""
        relative = target[len(prefix):]
        # A predicate may legitimately appear here; union/path operators would
        # make embedding it inside not(...) ambiguous, so leave those cases.
        if not relative or "|" in relative:
            return ""
        return relative

    def _coalesce_segments(self, nodes):
        output = []
        segment = []
        for node in nodes:
            spec = self._spec_for(node)
            if spec is None:
                if segment:
                    output.extend(self._coalesce_segment(segment))
                    segment = []
                output.append(node)
            else:
                segment.append(spec)
        if segment:
            output.extend(self._coalesce_segment(segment))
        return output

    def _simple_child_batch_info(self, spec):
        """Return (parent_xpath, child_name, value_node_or_none) for safe siblings."""
        if spec.kind == "upsert" and spec.add_class == "PatchOperationAdd":
            relative = self._relative_target(spec.add_xpath, spec.target_xpath)
            if not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_.-]*", relative or ""):
                return None
            nomatch = spec.node.find("nomatch")
            value = nomatch.find("value") if nomatch is not None else None
            children = [child for child in value] if value is not None else []
            children = [child for child in children if isinstance(getattr(child, "tag", None), str)]
            if len(children) != 1 or children[0].tag != relative:
                return None
            return spec.add_xpath, relative, copy.deepcopy(children[0])

        if spec.kind == "remove_cond":
            target = spec.target_xpath.rstrip("/")
            if "/" not in target:
                return None
            parent_xpath, child_name = target.rsplit("/", 1)
            if not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_.-]*", child_name or ""):
                return None
            parent_parsed = _parse_subject_xpath(parent_xpath)
            if parent_parsed is None or parent_parsed.subject != spec.subject:
                return None
            return parent_xpath, child_name, None
        return None

    def _batch_intra_subject(self, specs):
        """Collapse sibling upserts/removals for one Def parent into remove+add.

        ``Replace-or-Add`` is an unconditional final-value assignment.  For
        independent direct children of one parent, N such assignments are
        exactly equivalent to removing the affected existing children once and
        adding the desired children once.  Guarded removals can share the same
        removal set.
        """
        candidates = defaultdict(list)
        infos = {}
        for index, spec in enumerate(specs):
            info = self._simple_child_batch_info(spec)
            if info is None:
                continue
            parent_xpath, child_name, value_node = info
            key = (spec.subject, parent_xpath, spec.node.tag)
            candidates[key].append(index)
            infos[index] = (parent_xpath, child_name, value_node)

        consumed = set()
        replacements = {}
        for (subject, parent_xpath, tag), indexes in candidates.items():
            if len(indexes) < 2:
                continue
            index_set = set(indexes)
            target_tails = {specs[i].parsed_target.tail for i in indexes}
            parent_parsed = _parse_subject_xpath(parent_xpath)
            parent_tail = parent_parsed.tail if parent_parsed is not None else ""
            first, last = min(indexes), max(indexes)

            unsafe = False
            for pos in range(first, last + 1):
                if pos in index_set or specs[pos].subject != subject:
                    continue
                tail = specs[pos].parsed_target.tail
                if tail == parent_tail:
                    unsafe = True
                    break
                for target_tail in target_tails:
                    if (
                        tail == target_tail
                        or tail.startswith(target_tail.rstrip("/") + "/")
                        or target_tail.startswith(tail.rstrip("/") + "/")
                    ):
                        unsafe = True
                        break
                if unsafe:
                    break
            if unsafe:
                continue

            child_names = []
            add_values = []
            seen_children = set()
            for pos in sorted(indexes):
                _parent, child_name, value_node = infos[pos]
                if child_name not in seen_children:
                    seen_children.add(child_name)
                    child_names.append(child_name)
                if value_node is not None:
                    add_values.append(value_node)

            # Two removals become one guarded remove.  Upserts additionally
            # become one multi-value Add.  Both are a net operation reduction.
            if len(child_names) < 2 and len(add_values) < 2:
                continue

            selector = " or ".join(f"self::{name}" for name in child_names)
            remove_xpath = f"{parent_xpath}/*[{selector}]"
            remove_guard = LET.Element(tag, Class="PatchOperationConditional")
            LET.SubElement(remove_guard, "xpath").text = remove_xpath
            match = LET.SubElement(remove_guard, "match", Class="PatchOperationRemove")
            LET.SubElement(match, "xpath").text = remove_xpath
            batch_nodes = [remove_guard]

            if add_values:
                add = LET.Element(tag, Class="PatchOperationAdd")
                LET.SubElement(add, "xpath").text = parent_xpath
                value = LET.SubElement(add, "value")
                for child in add_values:
                    value.append(copy.deepcopy(child))
                batch_nodes.append(add)

            replacements[first] = batch_nodes
            consumed.update(indexes)

        if not consumed:
            return specs

        nodes = []
        for index, spec in enumerate(specs):
            if index in replacements:
                nodes.extend(replacements[index])
            if index in consumed:
                continue
            nodes.append(spec.node)

        rebuilt = []
        for node in nodes:
            rebuilt_spec = self._spec_for(node)
            if rebuilt_spec is None:
                # The generated batch should remain subject-local/groupable;
                # if an unforeseen XPath shape defeats parsing, keep it as a
                # unique pseudo-spec so ordering stays local and exact.
                xpath = _norm_xpath(node.findtext("xpath"))
                parsed = _parse_subject_xpath(xpath)
                if parsed is None:
                    return specs
                rebuilt_spec = _GroupSpec(
                    node=node,
                    kind="direct",
                    key=("unique_batch", id(node)),
                    subject=parsed.subject,
                    target_xpath=xpath,
                    parsed_target=parsed,
                )
            rebuilt.append(rebuilt_spec)
        return rebuilt

    def _coalesce_segment(self, specs):
        """Topologically transpose independent per-Def operations.

        The only ordering constraints that matter inside a groupable segment are
        the relative orders of operations touching the *same* Def.  Operations
        on distinct Def XML nodes commute.  Building those per-Def constraints
        as a DAG lets us turn row-major generation (all fields for A, then all
        fields for B) into field-major execution and then merge equal fields.
        """
        specs = self._batch_intra_subject(specs)
        counts = Counter((spec.subject, spec.key) for spec in specs)
        vertices = []
        groups = defaultdict(list)
        first_pos = {}
        for index, spec in enumerate(specs):
            # Repeated identical operations on one Def may be intentional; do
            # not collapse or identify those occurrences with each other.
            if counts[(spec.subject, spec.key)] == 1:
                vertex = ("group", spec.key)
            else:
                vertex = ("occurrence", index)
            vertices.append(vertex)
            groups[vertex].append(spec)
            first_pos.setdefault(vertex, index)

        adjacency = defaultdict(set)
        indegree = {vertex: 0 for vertex in groups}
        by_subject = defaultdict(list)
        for vertex, spec in zip(vertices, specs):
            by_subject[spec.subject].append(vertex)

        for ordered_vertices in by_subject.values():
            previous = None
            for vertex in ordered_vertices:
                if vertex == previous:
                    continue
                if previous is not None and vertex not in adjacency[previous]:
                    adjacency[previous].add(vertex)
                    indegree[vertex] += 1
                previous = vertex

        heap = []
        serial = 0
        for vertex, degree in indegree.items():
            if degree == 0:
                heapq.heappush(heap, (first_pos[vertex], serial, vertex))
                serial += 1

        ordered = []
        while heap:
            _pos, _serial, vertex = heapq.heappop(heap)
            ordered.append(vertex)
            for nxt in sorted(adjacency[vertex], key=lambda item: first_pos[item]):
                indegree[nxt] -= 1
                if indegree[nxt] == 0:
                    heapq.heappush(heap, (first_pos[nxt], serial, nxt))
                    serial += 1

        if len(ordered) != len(groups):
            # Conflicting field orders across Defs form a cycle.  The safe
            # fallback is the original conservative move-only-if-no-crossing
            # strategy rather than guessing an order.
            return self._coalesce_segment_conservative(specs)

        output = []
        for vertex in ordered:
            group = groups[vertex]
            if len(group) == 1:
                output.append(group[0].node)
            else:
                output.extend(self._merge_group(group))
        return output

    def _coalesce_segment_conservative(self, specs):
        consumed = set()
        output = []
        for i, anchor in enumerate(specs):
            if i in consumed:
                continue
            group = [anchor]
            group_subjects = {anchor.subject}
            for j in range(i + 1, len(specs)):
                if j in consumed:
                    continue
                candidate = specs[j]
                if candidate.key != anchor.key or candidate.subject in group_subjects:
                    continue
                if any(
                    k not in consumed and specs[k].subject == candidate.subject
                    for k in range(i + 1, j)
                ):
                    continue
                group.append(candidate)
                group_subjects.add(candidate.subject)
                consumed.add(j)
            if len(group) == 1:
                output.append(anchor.node)
            else:
                output.extend(self._merge_group(group))
        return output

    def _merge_group(self, group):
        kind = group[0].kind
        if kind == "direct":
            node = copy.deepcopy(group[0].node)
            _set_xpath(node, _combine_xpaths(item.target_xpath for item in group))
            return [node]

        if kind == "remove_cond":
            node = copy.deepcopy(group[0].node)
            combined = _combine_xpaths(item.target_xpath for item in group)
            _set_xpath(node, combined)
            _set_xpath(node.find("match"), combined)
            return [node]

        if kind in {"upsert", "missing_add"}:
            nodes = []
            if kind == "upsert":
                existing = self._make_guarded_existing_replace(group)
                nodes.append(existing)
            missing = self._make_guarded_missing_add(group)
            nodes.append(missing)
            return nodes

        return [copy.deepcopy(item.node) for item in group]

    def _clone_as_tag(self, source, tag):
        clone = copy.deepcopy(source)
        clone.tag = tag
        clone.tail = None
        return clone

    def _make_guarded_existing_replace(self, group):
        first = group[0]
        first_match = first.node.find("match")
        tag = first.node.tag
        node = LET.Element(tag, Class="PatchOperationConditional")
        combined = _combine_xpaths(item.target_xpath for item in group)
        LET.SubElement(node, "xpath").text = combined
        match = self._clone_as_tag(first_match, "match")
        _set_xpath(match, combined)
        node.append(match)
        return node

    def _make_guarded_missing_add(self, group):
        first = group[0]
        source_add = first.node.find("nomatch")
        tag = first.node.tag
        missing_xpaths = []
        for item in group:
            relative = self._relative_target(item.add_xpath, item.target_xpath)
            missing_xpaths.append(f"{item.add_xpath}[not({relative})]")
        combined = _combine_xpaths(missing_xpaths)
        node = LET.Element(tag, Class="PatchOperationConditional")
        LET.SubElement(node, "xpath").text = combined
        match = self._clone_as_tag(source_add, "match")
        _set_xpath(match, combined)
        node.append(match)
        return node

    def _merge_adjacent_same_target_adds(self, container):
        children = list(container)
        if not children:
            return
        output = []
        index = 0
        changed = False
        while index < len(children):
            current = children[index]
            cls = _operation_class(current)
            xpath = _norm_xpath(current.findtext("xpath")) if _is_patch_operation(current) else ""
            if cls not in ADD_CLASSES or not xpath:
                output.append(current)
                index += 1
                continue
            value = current.find("value")
            if value is None:
                output.append(current)
                index += 1
                continue
            merged = copy.deepcopy(current)
            merged_value = merged.find("value")
            cursor = index + 1
            while cursor < len(children):
                other = children[cursor]
                if _operation_class(other) != cls or _norm_xpath(other.findtext("xpath")) != xpath:
                    break
                other_value = other.find("value")
                if other_value is None:
                    break
                for child in other_value:
                    if isinstance(getattr(child, "tag", None), str):
                        merged_value.append(copy.deepcopy(child))
                changed = True
                cursor += 1
            output.append(merged if cursor > index + 1 else current)
            index = cursor
        if changed:
            for child in list(container):
                container.remove(child)
            for child in output:
                container.append(child)

    def _findmod_key(self, node):
        if _operation_class(node) != "PatchOperationFindMod":
            return None
        mods = node.find("mods")
        if mods is None:
            return None
        names = tuple(
            sorted(" ".join((li.text or "").split()).lower() for li in mods.findall("li") if (li.text or "").strip())
        )
        return names or None

    def _branch_operations(self, branch):
        if branch is None or not _is_patch_operation(branch):
            return []
        if _operation_class(branch) == "PatchOperationSequence":
            operations = branch.find("operations")
            if operations is None:
                return []
            return [copy.deepcopy(child) for child in operations if _is_patch_operation(child)]
        clone = copy.deepcopy(branch)
        clone.tag = "li"
        return [clone]

    def _set_findmod_branch(self, node, branch_name, operations):
        old = node.find(branch_name)
        if old is not None:
            node.remove(old)
        if not operations:
            return
        if len(operations) == 1:
            branch = copy.deepcopy(operations[0])
            branch.tag = branch_name
            node.append(branch)
            return
        branch = LET.Element(branch_name, Class="PatchOperationSequence")
        ops = LET.SubElement(branch, "operations")
        for operation in operations:
            clone = copy.deepcopy(operation)
            clone.tag = "li"
            ops.append(clone)
        node.append(branch)

    def _merge_adjacent_findmods(self, container):
        children = list(container)
        if not children:
            return
        output = []
        i = 0
        changed = False
        while i < len(children):
            current = children[i]
            key = self._findmod_key(current)
            if key is None:
                output.append(current)
                i += 1
                continue
            merged = copy.deepcopy(current)
            j = i + 1
            while j < len(children) and self._findmod_key(children[j]) == key:
                other = children[j]
                for branch_name in ("match", "nomatch"):
                    ops = self._branch_operations(merged.find(branch_name))
                    ops.extend(self._branch_operations(other.find(branch_name)))
                    self._set_findmod_branch(merged, branch_name, ops)
                changed = True
                j += 1
            output.append(merged)
            i = j
        if changed:
            for child in list(container):
                container.remove(child)
            for child in output:
                container.append(child)


def _parse_file(path):
    parser = LET.XMLParser(remove_blank_text=True, remove_comments=False, huge_tree=True)
    return LET.parse(str(path), parser)


def compact_file(path) -> CompactionReport:
    """Compact one patch XML and overwrite *path* atomically-ish in place.

    No backup or side-by-side copy is produced.  The temporary file exists only
    for the final replace, so an interrupted write cannot truncate the patch.
    """
    path = Path(path)
    before_bytes = path.stat().st_size
    tree = _parse_file(path)
    before = patch_metrics(tree.getroot())

    compactor = PatchCompactor()
    compacted = compactor.compact(tree.getroot())
    after = patch_metrics(compacted)

    payload = LET.tostring(
        compacted,
        encoding="utf-8",
        xml_declaration=True,
        pretty_print=True,
        with_tail=False,
    )
    if not payload.endswith(b"\n"):
        payload += b"\n"
    temp_path = path.with_name(path.name + ".compacting.tmp")
    try:
        with open(temp_path, "wb") as handle:
            handle.write(payload)
        os.replace(temp_path, path)
    finally:
        if temp_path.exists():
            temp_path.unlink()

    return CompactionReport(
        path=str(path),
        bytes_before=before_bytes,
        bytes_after=path.stat().st_size,
        operations_before=before["operations"],
        operations_after=after["operations"],
        xpaths_before=before["xpaths"],
        xpaths_after=after["xpaths"],
        conditionals_before=before["conditionals"],
        conditionals_after=after["conditionals"],
        findmods_before=before["findmods"],
        findmods_after=after["findmods"],
        passes=compactor.passes,
    )


def compact_files(paths: Iterable[str]) -> list[CompactionReport]:
    reports = []
    seen = set()
    for value in paths:
        path = os.path.abspath(os.fspath(value))
        if path in seen:
            continue
        seen.add(path)
        if not os.path.isfile(path) or not path.lower().endswith(".xml"):
            continue
        reports.append(compact_file(path))
    return reports


def _iter_xml_files(values):
    for value in values:
        path = Path(value)
        if path.is_dir():
            yield from sorted(path.rglob("*.xml"))
        elif path.is_file() and path.suffix.lower() == ".xml":
            yield path


def main(argv=None):
    parser = argparse.ArgumentParser(description="Compact generated RimWorld patch XML files in place.")
    parser.add_argument(
        "paths",
        nargs="*",
        help="Patch XML file(s) or folder(s). Default: generated_patches beside this script. Files are overwritten in place.",
    )
    args = parser.parse_args(argv)
    values = args.paths or [str(Path(__file__).resolve().parent / "generated_patches")]
    files = list(_iter_xml_files(values))
    reports = compact_files(files)
    before_bytes = sum(r.bytes_before for r in reports)
    after_bytes = sum(r.bytes_after for r in reports)
    before_ops = sum(r.operations_before for r in reports)
    after_ops = sum(r.operations_after for r in reports)
    before_xpath = sum(r.xpaths_before for r in reports)
    after_xpath = sum(r.xpaths_after for r in reports)
    print(f"Compacted {len(reports)} XML file(s) in place.")
    print(f"Bytes: {before_bytes} -> {after_bytes} ({before_bytes-after_bytes} saved)")
    print(f"PatchOperations: {before_ops} -> {after_ops} ({before_ops-after_ops} saved)")
    print(f"XPath: {before_xpath} -> {after_xpath} ({before_xpath-after_xpath} saved)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
