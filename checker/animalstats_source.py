#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Input-source helpers for AnimalStats tables.

Supported sources:
- local .xlsx/.xlsm/.xls workbooks;
- local .tsv files;
- Google Sheets URLs (https://docs.google.com/spreadsheets/d/<id>/...);
- ``gsheet:<spreadsheet_id>`` references;
- local ``.gsheet`` pointer files created by Google Drive for desktop.

Google Sheets access is read-only and uses the official Sheets API.  For a
private sheet, put an OAuth Desktop client JSON next to this module as
``google_credentials.json`` (or set ANIMALSTATS_GOOGLE_CREDENTIALS).  The
first read opens a browser for authorization and caches a read-only token in
``google_token.json`` (or ANIMALSTATS_GOOGLE_TOKEN).

A service-account JSON is also supported.  In that case share the spreadsheet
with the service account's ``client_email``.
"""

from __future__ import annotations

import json
import os
import re
import time
import urllib.error
import urllib.parse
import urllib.request
from typing import Any, List, Optional

import pandas as pd


_GOOGLE_SHEETS_RE = re.compile(
    r"https?://docs\.google\.com/spreadsheets/(?:u/\d+/)?d/([A-Za-z0-9_-]+)",
    re.IGNORECASE,
)
_GOOGLE_ID_RE = re.compile(r"^[A-Za-z0-9_-]{20,}$")
_SCOPES = ["https://www.googleapis.com/auth/spreadsheets.readonly"]
_DEFAULT_CREDENTIALS_FILENAME = "google_credentials.json"
_DEFAULT_TOKEN_FILENAME = "google_token.json"


def _module_dir() -> str:
    return os.path.dirname(os.path.abspath(__file__))


def is_excel_source(source: Any) -> bool:
    if not source:
        return False
    return os.path.splitext(str(source))[1].lower() in (".xlsx", ".xlsm", ".xls")


def _read_gsheet_pointer(path: str) -> Optional[str]:
    try:
        with open(path, "r", encoding="utf-8-sig") as f:
            data = json.load(f)
    except Exception:
        return None

    if isinstance(data, dict):
        for key in ("doc_id", "spreadsheet_id", "id"):
            value = str(data.get(key, "")).strip()
            if _GOOGLE_ID_RE.fullmatch(value):
                return value
        for key in ("url", "document_url"):
            value = str(data.get(key, "")).strip()
            m = _GOOGLE_SHEETS_RE.search(value)
            if m:
                return m.group(1)
    return None


def google_spreadsheet_id(source: Any) -> Optional[str]:
    """Return a Google Sheets spreadsheet ID if *source* denotes one."""
    if not source:
        return None
    text = str(source).strip()
    if not text:
        return None

    if text.lower().startswith("gsheet:"):
        candidate = text.split(":", 1)[1].strip()
        return candidate if _GOOGLE_ID_RE.fullmatch(candidate) else None

    m = _GOOGLE_SHEETS_RE.search(text)
    if m:
        return m.group(1)

    # Raw spreadsheet IDs are convenient in CLI/config files.  Normal local
    # filenames contain a dot/path separator and therefore do not match this.
    if _GOOGLE_ID_RE.fullmatch(text):
        return text

    # A Drive-for-desktop pointer is a small local JSON file.  Do not treat an
    # arbitrary missing *.gsheet string as valid: this keeps typo diagnostics.
    if text.lower().endswith(".gsheet") and os.path.isfile(text):
        return _read_gsheet_pointer(text)

    return None


def is_google_sheet_source(source: Any) -> bool:
    return google_spreadsheet_id(source) is not None


def is_multisheet_source(source: Any) -> bool:
    """True for sources that can expose both Animals and Animals CE."""
    return is_excel_source(source) or is_google_sheet_source(source)


def source_available(source: Any) -> bool:
    """Whether a source is syntactically/locally available for reading."""
    if not source:
        return False
    if is_google_sheet_source(source):
        return True
    return os.path.exists(str(source))


def source_display_name(source: Any) -> str:
    sid = google_spreadsheet_id(source)
    if sid:
        return f"Google Sheets {sid}"
    return str(source)


def source_cache_key(source: Any):
    """Cache key for local files; None for cloud sources (always refresh)."""
    if not source:
        return None
    if is_google_sheet_source(source):
        return None
    path = os.path.abspath(str(source))
    try:
        return (os.path.normcase(path), os.path.getmtime(path))
    except OSError:
        return (os.path.normcase(path), None)


def _credentials_paths() -> tuple[str, str]:
    credentials_path = os.environ.get("ANIMALSTATS_GOOGLE_CREDENTIALS", "").strip()
    token_path = os.environ.get("ANIMALSTATS_GOOGLE_TOKEN", "").strip()
    if not credentials_path:
        credentials_path = os.path.join(_module_dir(), _DEFAULT_CREDENTIALS_FILENAME)
    if not token_path:
        token_path = os.path.join(_module_dir(), _DEFAULT_TOKEN_FILENAME)
    return credentials_path, token_path


def _load_google_credentials():
    """Load/refresh Google credentials, invoking Desktop OAuth when needed."""
    try:
        from google.auth.transport.requests import Request
        from google.oauth2.credentials import Credentials
        from google.oauth2 import service_account
    except ImportError as e:
        raise RuntimeError(
            "Google Sheets support requires optional packages. Install them with:\n"
            "  py -m pip install google-auth google-auth-oauthlib\n"
            "Local XLSX/TSV support does not require these packages."
        ) from e

    credentials_path, token_path = _credentials_paths()

    # Service account: non-interactive, useful for CI.  It must be explicitly
    # shared into the target spreadsheet.
    if os.path.isfile(credentials_path):
        try:
            with open(credentials_path, "r", encoding="utf-8-sig") as f:
                meta = json.load(f)
        except Exception as e:
            raise RuntimeError(f"Cannot read Google credentials JSON: {credentials_path}") from e

        if isinstance(meta, dict) and meta.get("type") == "service_account":
            return service_account.Credentials.from_service_account_file(
                credentials_path,
                scopes=_SCOPES,
            )

    creds = None
    if os.path.isfile(token_path):
        try:
            creds = Credentials.from_authorized_user_file(token_path, _SCOPES)
        except Exception:
            creds = None

    if creds and creds.expired and creds.refresh_token:
        try:
            creds.refresh(Request())
        except Exception:
            creds = None

    if not creds or not creds.valid:
        if not os.path.isfile(credentials_path):
            raise RuntimeError(
                "Google Sheets source detected, but OAuth credentials are not configured.\n\n"
                f"Expected Desktop OAuth client JSON:\n  {credentials_path}\n\n"
                "Create a Google Cloud Desktop OAuth client with the Google Sheets API enabled, "
                "save the downloaded JSON as google_credentials.json next to the scripts, then run again. "
                "The first run opens the browser once and stores a read-only token locally.\n\n"
                "Alternatively set ANIMALSTATS_GOOGLE_CREDENTIALS to a credentials JSON path."
            )
        try:
            from google_auth_oauthlib.flow import InstalledAppFlow
        except ImportError as e:
            raise RuntimeError(
                "Interactive Google login requires google-auth-oauthlib. Install with:\n"
                "  py -m pip install google-auth google-auth-oauthlib"
            ) from e

        try:
            flow = InstalledAppFlow.from_client_secrets_file(credentials_path, _SCOPES)
            creds = flow.run_local_server(port=0)
        except Exception as e:
            raise RuntimeError(f"Google OAuth authorization failed: {e}") from e

    # Do not write service-account credentials.  Authorized-user credentials
    # are safe to serialize into the dedicated token cache selected above.
    try:
        if creds and getattr(creds, "refresh_token", None):
            os.makedirs(os.path.dirname(os.path.abspath(token_path)), exist_ok=True)
            with open(token_path, "w", encoding="utf-8") as f:
                f.write(creds.to_json())
    except Exception as e:
        raise RuntimeError(f"Could not save Google OAuth token: {token_path}") from e

    return creds


def _sheet_api_url(spreadsheet_id: str, sheet_name: str) -> str:
    escaped_sheet = str(sheet_name).replace("'", "''")
    a1_range = f"'{escaped_sheet}'"
    encoded_range = urllib.parse.quote(a1_range, safe="")
    query = urllib.parse.urlencode(
        {
            "majorDimension": "ROWS",
            "valueRenderOption": "UNFORMATTED_VALUE",
            "dateTimeRenderOption": "FORMATTED_STRING",
        }
    )
    return (
        f"https://sheets.googleapis.com/v4/spreadsheets/{spreadsheet_id}/values/"
        f"{encoded_range}?{query}"
    )


def _fetch_google_values(spreadsheet_id: str, sheet_name: str) -> List[List[Any]]:
    creds = _load_google_credentials()
    token = getattr(creds, "token", None)
    if not token:
        raise RuntimeError("Google authorization succeeded but produced no access token.")

    req = urllib.request.Request(
        _sheet_api_url(spreadsheet_id, sheet_name),
        headers={
            "Authorization": f"Bearer {token}",
            "Accept": "application/json",
            "User-Agent": "AnimalStats-RimWorld-Checker/1.0",
        },
        method="GET",
    )
    try:
        with urllib.request.urlopen(req, timeout=60) as resp:
            payload = json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        try:
            detail = e.read().decode("utf-8", "replace")
            parsed = json.loads(detail)
            detail = parsed.get("error", {}).get("message", detail)
        except Exception:
            detail = str(e)
        if e.code in (401, 403):
            raise RuntimeError(
                f"Google Sheets denied access to spreadsheet {spreadsheet_id}. "
                "Make sure the authorized Google account can open it. "
                f"API response: {detail}"
            ) from e
        if e.code == 400 and "Unable to parse range" in detail:
            raise RuntimeError(f"Sheet '{sheet_name}' was not found in Google Sheets.") from e
        raise RuntimeError(f"Google Sheets API error {e.code}: {detail}") from e
    except urllib.error.URLError as e:
        raise RuntimeError(f"Could not reach Google Sheets API: {e.reason}") from e

    values = payload.get("values", []) if isinstance(payload, dict) else []
    if not isinstance(values, list):
        raise RuntimeError("Google Sheets API returned an unexpected response.")
    return values


def _column_label(index_zero_based: int) -> str:
    n = index_zero_based + 1
    chars = []
    while n:
        n, r = divmod(n - 1, 26)
        chars.append(chr(ord("A") + r))
    return "".join(reversed(chars))


def _values_to_dataframe(values: List[List[Any]]) -> pd.DataFrame:
    if not values:
        return pd.DataFrame()

    width = max((len(row) for row in values), default=0)
    if width == 0:
        return pd.DataFrame()

    padded = [list(row) + [""] * (width - len(row)) for row in values]

    # Trim columns that are wholly empty.  Sheets API normally does this
    # already, but doing it here avoids hundreds of meaningless blank headers
    # if the sheet once had formatting far to the right.
    while width > 0 and all(row[width - 1] in (None, "") for row in padded):
        width -= 1
    padded = [row[:width] for row in padded]
    if width == 0:
        return pd.DataFrame()

    raw_header = padded[0]
    header = []
    used = set()
    for i, value in enumerate(raw_header):
        name = "" if value is None else str(value).strip()
        if not name:
            name = f"Column {_column_label(i)}"
        base = name
        suffix = 2
        while name in used:
            name = f"{base}.{suffix}"
            suffix += 1
        used.add(name)
        header.append(name)

    rows = padded[1:]
    return pd.DataFrame(rows, columns=header)


def read_google_sheet(source: Any, sheet_name: str) -> pd.DataFrame:
    sid = google_spreadsheet_id(source)
    if not sid:
        raise RuntimeError(f"Not a Google Sheets source: {source}")
    if not sheet_name:
        raise RuntimeError("A sheet name is required for a Google Sheets source.")
    values = _fetch_google_values(sid, sheet_name)
    return _values_to_dataframe(values)


__all__ = [
    "google_spreadsheet_id",
    "is_excel_source",
    "is_google_sheet_source",
    "is_multisheet_source",
    "read_google_sheet",
    "source_available",
    "source_cache_key",
    "source_display_name",
]
