"""Validate maintained repository files and local documentation links."""

from __future__ import annotations

import re
from pathlib import Path
from urllib.parse import unquote

ROOT = Path(__file__).resolve().parents[1]

REQUIRED_PATHS = (
    "README.md",
    ".gitignore",
    ".editorconfig",
    ".gitattributes",
    ".github/workflows/quality.yml",
    "docs/scope.md",
    "docs/architecture.md",
    "docs/data-model.md",
    "docs/security.md",
    "docs/verification.md",
    "docs/development.md",
    "docs/decisions/0001-power-platform.md",
    "power-platform/README.md",
    "power-bi/README.md",
    "integrations/README.md",
)

MAINTAINED_DIRS = ("docs", "scripts", "tests", "integrations")
MAINTAINED_ROOT_FILES = (
    "README.md",
    ".gitignore",
    ".editorconfig",
    ".gitattributes",
    "power-platform/README.md",
    "power-bi/README.md",
    ".github/workflows/quality.yml",
)

LOCAL_LINK = re.compile(r"(?<!!)\[[^\]\n]+\]\(([^)]+)\)")
BLOCKED_FILENAMES = {".env", ".env.local", ".env.production", "localSettings.json", "cache.abf"}
BLOCKED_SUFFIXES = {".pbix"}


def validate_text(content: str) -> list[str]:
    issues: list[str] = []
    if content.startswith("\ufeff"):
        issues.append("UTF-8 BOM is not allowed")
    if "\r" in content:
        issues.append("CR line endings are not allowed")
    if not content.endswith("\n"):
        issues.append("missing final newline")
    for number, line in enumerate(content.splitlines(), 1):
        if line != line.rstrip(" \t"):
            issues.append(f"trailing whitespace on line {number}")
    return issues


def validate_links(path: Path, content: str) -> list[str]:
    issues: list[str] = []
    for match in LOCAL_LINK.finditer(content):
        destination = match.group(1).split("#", 1)[0].strip()
        if not destination or destination.startswith(("http://", "https://", "mailto:")):
            continue
        if destination.startswith("/"):
            issues.append(f"absolute local link: {destination}")
            continue
        local_path = (path.parent / unquote(destination)).resolve()
        if not local_path.exists():
            issues.append(f"broken local link: {destination}")
    return issues


def maintained_files(root: Path) -> list[Path]:
    paths = [root / name for name in MAINTAINED_ROOT_FILES]
    for directory in MAINTAINED_DIRS:
        paths.extend(
            path
            for path in (root / directory).rglob("*")
            if path.is_file()
            and "__pycache__" not in path.parts
            and path.suffix in {".md", ".py", ".yml", ".yaml", ".json"}
        )
    return sorted(set(paths))


def check_repository(root: Path) -> list[str]:
    issues: list[str] = []
    for name in REQUIRED_PATHS:
        if not (root / name).is_file():
            issues.append(f"{name}: required file missing")

    for path in root.rglob("*"):
        if not path.is_file() or ".git" in path.parts:
            continue
        relative = path.relative_to(root).as_posix()
        if path.name in BLOCKED_FILENAMES or path.suffix.lower() in BLOCKED_SUFFIXES:
            if "__pycache__" not in path.parts:
                issues.append(f"{relative}: local or binary artifact must not be committed")

    for path in maintained_files(root):
        if not path.exists():
            continue
        relative = path.relative_to(root).as_posix()
        try:
            content = path.read_text(encoding="utf-8")
        except UnicodeError:
            issues.append(f"{relative}: invalid UTF-8")
            continue
        issues.extend(f"{relative}: {issue}" for issue in validate_text(content))
        if path.suffix == ".md":
            issues.extend(f"{relative}: {issue}" for issue in validate_links(path, content))
    return issues


if __name__ == "__main__":
    failures = check_repository(ROOT)
    for failure in failures:
        print(failure)
    if failures:
        raise SystemExit(1)
    print("Repository validation passed.")
