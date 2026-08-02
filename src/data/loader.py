"""Data loader — reads JSON game data files."""
import json
import os

DATA_DIR = os.path.join(os.path.dirname(__file__), "..", "..", "data")


def load(name):
    """Load a JSON data file by name (without .json extension)."""
    path = os.path.join(DATA_DIR, f"{name}.json")
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def load_all():
    """Load all game data files into a dict."""
    return {
        "constants": load("constants"),
        "archetypes": load("archetypes"),
        "crimes": load("crimes"),
        "weapons": load("weapons"),
        "businesses": load("businesses"),
        "city_template": load("city_template"),
    }
