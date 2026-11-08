"""Convert RimWorld material RGB XML to comparable hexadecimal colors."""
from __future__ import annotations

import math
import re


def xml_color(value):
    """Return #RRGGBB for integer RGB or normalized float RGB; else None."""
    if value is None:
        return None
    text = str(value).strip()
    if re.fullmatch(r"#[0-9a-fA-F]{6}", text):
        return text.upper()
    if not (text.startswith("(") and text.endswith(")")):
        return None
    parts = [part.strip() for part in text[1:-1].split(",")]
    if len(parts) != 3:
        return None
    try:
        channels = [float(part) for part in parts]
    except ValueError:
        return None
    if any(not math.isfinite(channel) or channel < 0 for channel in channels):
        return None
    # (1,0,0) is byte RGB, while (1.0,0.0,0.0) is float RGB.
    fractional = any("." in part or "e" in part.lower() for part in parts)
    if fractional and all(channel <= 1 for channel in channels):
        channels = [round(channel * 255) for channel in channels]
    elif all(channel <= 255 for channel in channels):
        channels = [round(channel) for channel in channels]
    else:
        return None
    return "#" + "".join(f"{channel:02X}" for channel in channels)
