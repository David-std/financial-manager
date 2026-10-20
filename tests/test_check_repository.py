"""Tests for repository validation rules."""

import tempfile
import unittest
from pathlib import Path

from scripts.check_repository import check_repository, validate_links, validate_text


class TextValidationTests(unittest.TestCase):
    def test_accepts_clean_text(self):
        self.assertEqual(validate_text("valid text\n"), [])

    def test_rejects_missing_newline(self):
        self.assertIn("missing final newline", validate_text("valid text"))

    def test_rejects_trailing_whitespace(self):
        self.assertIn("trailing whitespace on line 1", validate_text("bad  \n"))

    def test_rejects_windows_line_endings(self):
        self.assertIn("CR line endings are not allowed", validate_text("line\r\n"))



    def test_does_not_reject_ignored_local_environment_files(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            import subprocess

            subprocess.run(["git", "init", "-q", str(root)], check=True)
            (root / ".env").write_text("LOCAL_ONLY=test\\n", encoding="utf-8")
            issues = check_repository(root)
            self.assertFalse(
                any("local or binary artifact" in issue for issue in issues)
            )

class LinkValidationTests(unittest.TestCase):
    def test_resolves_relative_link(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "target.md").write_text("content\n", encoding="utf-8")
            self.assertEqual(
                validate_links(root / "README.md", "[Target](target.md)\n"),
                [],
            )

    def test_detects_missing_relative_link(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self.assertEqual(
                validate_links(root / "README.md", "[Target](missing.md)\n"),
                ["broken local link: missing.md"],
            )

    def test_ignores_external_and_fragment_links(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self.assertEqual(
                validate_links(
                    root / "README.md",
                    "[External](https://example.com) [Section](#overview)\n",
                ),
                [],
            )


if __name__ == "__main__":
    unittest.main()
