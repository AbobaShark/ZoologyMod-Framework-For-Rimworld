"""Def-local XPath evaluation with mutation-aware indexes for simulation trees."""
from collections import OrderedDict, defaultdict
from contextlib import contextmanager
from contextvars import ContextVar
from functools import lru_cache

from lxml import etree as E

from core.reference_projection import local_target


_scope = ContextVar("indexed_xpath_scope", default=None)


@contextmanager
def xpath_scope():
    token = _scope.set(OrderedDict())
    try:
        yield
    finally:
        _scope.reset(token)


def _build(root):
    result = defaultdict(list)
    for child in root:
        if not isinstance(child.tag, str):
            continue
        keys = set()
        if child.get("Name") is not None:
            keys.add((child.tag, "@Name", child.get("Name")))
        for name in child.findall("defName"):
            keys.add((child.tag, "defName", name.xpath("string(.)")))
        for key in keys:
            result[key].append(child)
    return result


@lru_cache(maxsize=8192)
def _relative_query(tail):
    return E.XPath("." + tail)


def indexed_xpath(root, text):
    """Return native XPath results, narrowing only proven Def-local paths."""
    cache = _scope.get()
    parsed = local_target(text) if cache is not None and root.tag == "Defs" and root.getparent() is None else None
    if parsed is None:
        return E.ElementTree(root).xpath(text)
    # Both call sites normalize Defs/... to /Defs/... before evaluation.
    # Do not reinterpret an unnormalized relative path.
    if not text.startswith("/Defs/"):
        return E.ElementTree(root).xpath(text)
    if root not in cache:
        cache[root] = _build(root)
        if len(cache) > 8:
            cache.popitem(last=False)
    cache.move_to_end(root)
    key, tail = parsed
    candidates = cache[root].get(key, ())
    if not tail:
        return list(candidates)
    query = _relative_query(tail)
    return [match for child in candidates for match in query(child)]


def before_mutation(root, targets, operation, *, attribute=None, additions=()):
    """Invalidate only if a mutation can alter the root's Def identity index.

    XPath results themselves are never cached. Nested values/predicates always
    see the live tree. Unknown or global XPath is safe because the actual
    mutation targets, not the expression's spelling, determine invalidation.
    """
    cache = _scope.get()
    if cache is None or root not in cache:
        return
    for target in targets:
        if not isinstance(target, E._Element):
            continue
        if target is root:
            cache.pop(root, None)
            return
        parent = target.getparent()
        if parent is root:
            if (operation in ("remove", "replace")
                    or operation == "attribute" and attribute == "Name"
                    or operation == "add" and any(x.tag == "defName" for x in additions)):
                cache.pop(root, None)
                return
            continue
        # Only the string-value of a direct defName child can change an
        # indexed identity. Removing or adding its descendants matters too.
        current = target
        while parent is not None and parent is not root:
            if parent.getparent() is root:
                if current.tag == "defName":
                    cache.pop(root, None)
                    return
                break
            current, parent = parent, parent.getparent()
