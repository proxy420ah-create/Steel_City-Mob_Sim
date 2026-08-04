# C# Porting Notes — Python to Unity

**Created**: August 2, 2026
**Status**: Active

---

## Porting Gotchas Encountered

### 1. Static Class with Instance Fields (CS0708)
**File**: `JSONParser.cs`
**Issue**: Class was declared `static` but used instance fields (`_json`, `_pos`) and instance methods (`ParseValue`, `SkipWhitespace`, etc.).
**Fix**: Remove `static` from the class declaration. Keep the `Parse` method static as a factory that creates an instance internally.
```csharp
// WRONG: public static class JSONParser { private string _json; ... }
// RIGHT: public class JSONParser { private string _json; ... }
```

### 2. Missing `using System.Collections.Generic` (CS0246)
**File**: `DataLoader.cs`
**Issue**: `KeyValuePair<string, JSONNode>` used in `foreach` loops but `System.Collections.Generic` was not imported.
**Fix**: Add `using System.Collections.Generic;` at the top of the file.

### 3. JSONNode Does Not Implement GetEnumerator (CS1579)
**File**: `DataLoader.cs`
**Issue**: `foreach` loops iterated over `JSONNode` (abstract base class), but only `JSONObject` implements `GetEnumerator`. The base `JSONNode` does not.
**Fix**: Cast to `JSONObject` before iterating:
```csharp
// WRONG: foreach (KeyValuePair<string, JSONNode> kv in fearBase)
// RIGHT: foreach (KeyValuePair<string, JSONNode> kv in (JSONObject)fearBase)
```
This applies to all dictionary-style JSON nodes being iterated.

### 4. JSON Field Name Mismatch — "name" vs "id" (NRE)
**File**: `DataLoader.cs` → `LoadArchetypes`
**Issue**: Python code used `c["id"]` to find crime definitions, but `archetypes.json` uses `"name"` not `"id"` for the archetype identifier. Accessing `node["id"].Value` returned null → NullReferenceException.
**Fix**: Use `node["name"]?.Value ?? ""` and add null-safe access (`?.`) to all JSON node field reads.
```csharp
// WRONG: id = node["id"].Value
// RIGHT: id = node["name"]?.Value ?? ""
```
**Lesson**: Always cross-reference the actual JSON file structure when porting field access. Python's `dict.get()` silently returns None; C# indexer returns null which throws on `.Value` or `.AsInt`.

---

## Architecture Decisions

### JSON Parsing
Unity's built-in `JsonUtility` does not support `Dictionary` types or dynamic keys. We wrote a minimal `JSONParser` class that handles:
- `JSONObject` — dictionary-style access with `GetEnumerator`
- `JSONArray` — indexed access
- `JSONString`, `JSONNumber`, `JSONBool`, `JSONNull` — leaf values

All JSON loading goes through `DataLoader` which uses `JSONParser.Parse()` and manual node traversal.

### Data File Location
JSON files are in `Assets/StreamingAssets/` so they're accessible at runtime via `Application.streamingAssetsPath`. Source copies are also in `Assets/Data/` for reference.

### Random Seed Control
Each system class (`CharacterGen`, `CityGen`, `CrimeSystem`, `EconomySystem`, `RivalAI`) has a `SetSeed(int)` method for reproducible testing. `GameBootstrap` exposes a `randomSeed` field in the Inspector.

---

## Module Mapping (Python → C#)

| Python File | C# File | Notes |
|---|---|---|
| `engine.py` | `GameEngine.cs` | Core loop, order resolution, weekly tick |
| `city.py` | `City.cs` | Block, Business, PoliceOfficer + CityGen |
| `character.py` | `NPC.cs` | Hood, NPC, CharacterGen (combined) |
| `crime.py` | `CrimeSystem.cs` | Extortion, intimidation, squeal, investigations |
| `economy.py` | `EconomySystem.cs` | Business income, protection, market share |
| `rival_ai.py` | `RivalAI.cs` | Rival gang AI decisions |
| `events.py` | `EventStream.cs` | GameEvent + EventStream |
| `loader.py` | `DataLoader.cs` | JSON loading from StreamingAssets |
| (new) | `DataModels.cs` | All JSON data model classes |
| (new) | `JSONParser.cs` | Minimal JSON parser for dict/array support |
| (new) | `GameBootstrap.cs` | MonoBehaviour entry point for Unity |

---

## Testing in Unity

1. Open project in Unity 6 (6000.3.18f1)
2. Create empty GameObject, add `GameBootstrap` component
3. Press Play — console outputs full 5-week simulation
4. Set `randomSeed` in Inspector for reproducible runs

---

## Verification Status

**✅ C# PORT VERIFIED** — August 2, 2026

5-week automated test runs in Unity console with matching output:
- City: 9 blocks, 16 businesses, 118 NPCs, 2 police officers
- Player: Moretti Family, $3000, 3 hoods
- Rival: Falcone Syndicate, $3000, 3 hoods
- All systems functional: extortion, squeal, investigations, rival AI, economy, territory
- Final state: $1724 treasury, 3 player blocks, 6 rival blocks, 8 investigations, 0 arrests
