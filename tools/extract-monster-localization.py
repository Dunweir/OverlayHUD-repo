"""Extract official enemy names. Run with: uv run --with UnityPy==1.25.4 python tools/extract-monster-localization.py GAME_DATA"""
import argparse
import json
from pathlib import Path
import UnityPy

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("game_data", type=Path)
args = parser.parse_args()
bundles = args.game_data / "StreamingAssets/aa/StandaloneWindows64"

def tables(pattern):
    matches = list(bundles.glob(pattern))
    if len(matches) != 1:
        raise ValueError(f"Expected exactly one bundle for {pattern}, got {matches}")
    return [obj.read_typetree() for obj in UnityPy.load(str(matches[0])).objects
            if obj.type.name == "MonoBehaviour"]

shared = next(t for t in tables("localization-assets-shared_assets_all.bundle")
              if t.get("m_TableCollectionName") == "HUD")
localized = {}
for language, pattern, locale in [("en", "*english*.bundle", "en-US"),
                                   ("ru", "*russian*.bundle", "ru-RU")]:
    table = next(t for t in tables(pattern) if t.get("m_Name") == f"HUD_{locale}")
    localized[language] = {e["m_Id"]: e["m_Localized"] for e in table["m_TableData"]}
names = {e["m_Key"]: {lang: values[e["m_Id"]] for lang, values in localized.items()}
         for e in shared["m_Entries"] if e["m_Key"].startswith("ENEMY.")}
if not names or any(not value.strip() for entry in names.values() for value in entry.values()):
    raise ValueError("Missing enemy translations")
destination = Path(__file__).resolve().parents[1] / "assets/monster-localization.json"
destination.write_text(json.dumps(names, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(f"Extracted {len(names)} enemy names in ru/en to {destination}")
