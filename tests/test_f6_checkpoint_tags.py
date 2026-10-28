"""A source checkpoint must not weaken the managed release gate."""
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

class CheckpointTagPolicyTests(unittest.TestCase):
    def test_regular_release_tags_remain_gated(self):
        yml = (ROOT / ".github/workflows/quality.yml").read_text(encoding="utf-8")
        self.assertIn("!startsWith(github.ref, 'refs/tags/checkpoint/')", yml)
        self.assertIn("run: python scripts/f6_alm_preflight.py --release", yml)
        self.assertIn("startsWith(github.ref, 'refs/tags/')", yml)

    def test_checkpoint_tags_still_validate_source(self):
        yml = (ROOT / ".github/workflows/quality.yml").read_text(encoding="utf-8")
        self.assertIn("if: startsWith(github.ref, 'refs/tags/checkpoint/')", yml)
        self.assertIn("python scripts/f6_dependencies.py", yml)
        self.assertIn("python scripts/f6_alm_preflight.py", yml)

if __name__ == "__main__":
    unittest.main()
