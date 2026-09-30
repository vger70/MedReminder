import sys
from pathlib import Path

# The feed scripts import `common` as a sibling module (they run as
# `python scripts/feeds/<feed>.py`), so the tests put that folder on
# the import path the same way.
FEEDS_DIR = Path(__file__).resolve().parent.parent
REPO_ROOT = FEEDS_DIR.parent.parent
FIXTURES = REPO_ROOT / "tests" / "fixtures" / "catalogue"

sys.path.insert(0, str(FEEDS_DIR))
