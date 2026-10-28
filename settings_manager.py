import os
import sys
import json
from typing import Dict, Any

def get_app_dir() -> str:
    """Get the true directory containing the executable or main script."""
    if getattr(sys, "frozen", False):
        return os.path.dirname(sys.executable)
    return os.path.dirname(os.path.abspath(__file__))

def get_config_path() -> str:
    return os.path.join(get_app_dir(), "config.json")

DEFAULT_SETTINGS = {
    "printer": "",
    "color": "bw",
    "paper": "A4",
    "duplex": "simplex",
    "nup": 1,
    "dpi": 600,
    "copies": 1,
    "orientation": "auto",
    "draw_borders": True
}

def load_settings() -> Dict[str, Any]:
    """Load settings from config.json if present, else return defaults."""
    config_file = get_config_path()
    if os.path.exists(config_file):
        try:
            with open(config_file, "r", encoding="utf-8") as f:
                data = json.load(f)
                merged = DEFAULT_SETTINGS.copy()
                merged.update(data)
                return merged
        except Exception as e:
            print(f"Warning: Could not read config.json: {e}")
    return DEFAULT_SETTINGS.copy()

def save_settings(new_settings: Dict[str, Any]) -> bool:
    """Save user settings to config.json next to executable."""
    config_file = get_config_path()
    try:
        current = load_settings()
        current.update(new_settings)
        with open(config_file, "w", encoding="utf-8") as f:
            json.dump(current, f, indent=4, ensure_ascii=False)
        return True
    except Exception as e:
        print(f"Error saving config.json: {e}")
        return False
