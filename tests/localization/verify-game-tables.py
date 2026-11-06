"""Read-only check of fixtures against installed official tables.
Run: uv run --with UnityPy==1.25.4 python tests/localization/verify-game-tables.py GAME_DATA
"""
import argparse
import json
from pathlib import Path

import UnityPy

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("game_data", type=Path)
args = parser.parse_args()
bundles = args.game_data / "StreamingAssets/aa/StandaloneWindows64"
root = Path(__file__).resolve().parents[2]


def tables(pattern):
    matches = list(bundles.glob(pattern))
    if len(matches) != 1:
        raise ValueError(f"Expected exactly one bundle for {pattern}, got {matches}")
    return matches[0], [obj.read_typetree() for obj in UnityPy.load(str(matches[0])).objects
                        if obj.type.name == "MonoBehaviour"]


_, shared_tables = tables("localization-assets-shared_assets_all.bundle")
shared = next(t for t in shared_tables if t.get("m_TableCollectionName") == "HUD")
ids = {e["m_Key"]: e["m_Id"] for e in shared["m_Entries"]}
fixtures = json.loads((root / "assets/monster-localization.json").read_text(encoding="utf-8"))
upgrades = json.loads((root / "tests/localization/upgrade-names.json").read_text(encoding="utf-8"))
fixtures.update({e["key"]: {lang: e[lang] for lang in ("en", "ru")} for e in upgrades.values()})
for language, pattern, locale in [("en", "*english*.bundle", "en-US"),
                                   ("ru", "*russian*.bundle", "ru-RU")]:
    bundle, localized_tables = tables(pattern)
    table = next(t for t in localized_tables if t.get("m_Name") == f"HUD_{locale}")
    values = {e["m_Id"]: e["m_Localized"] for e in table["m_TableData"]}
    mismatches = [key for key, expected in fixtures.items()
                  if values.get(ids.get(key)) != expected[language]]
    if mismatches:
        raise ValueError(f"Fixture mismatch in {bundle.name}: {mismatches}")
    print(f"{bundle.name}: all {len(fixtures)} HUD_{locale} entries match exactly")
