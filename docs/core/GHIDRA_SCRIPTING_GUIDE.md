# Ghidra Scripting Guide — gangsters.exe Reverse Engineering

**Created**: August 6, 2026
**Status**: Active
**Toolchain**: Ghidra 12.1.2, JDK 21 (Eclipse Adoptium)
**Script Location**: `C:\Tools\ghidra_scripts\`
**Output Location**: `SteelCityMobSim/ghidra_*.txt`

---

## Purpose

Standardized methodology for writing Ghidra Java scripts to decompile and analyze
the Gangsters: Organized Crime binary (`gangsters.exe`, GOG release, ~1998 Hothouse
Creations). All scripts follow the same patterns for consistency and reusability.

---

## 1. Script Skeleton

Every script extends `GhidraScript` and implements `run()`. The header **must** include the `@category` annotation:

```java
// Description of what the script does
//@category Analysis
//@author Cascade

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.FunctionManager;
import ghidra.program.model.symbol.Reference;
import ghidra.program.model.symbol.ReferenceIterator;
import ghidra.program.model.symbol.ReferenceManager;
import ghidra.util.task.TaskMonitor;

import java.io.FileWriter;
import java.io.PrintWriter;
import java.util.LinkedHashSet;
import java.util.Set;

public class ScriptName extends GhidraScript {

    private String outputPath = "C:/Users/NADECC/ATSTradingDashboard Project/Cursor Workshop/SteelCityMobSim/ghidra_output.txt";

    @Override
    public void run() throws Exception {
        FunctionManager funcMgr = currentProgram.getFunctionManager();
        ReferenceManager refMgr = currentProgram.getReferenceManager();
        TaskMonitor monitor = getMonitor();

        DecompInterface decomp = new DecompInterface();
        decomp.openProgram(currentProgram);

        PrintWriter pw = new PrintWriter(new FileWriter(outputPath));

        // ... analysis logic ...

        pw.close();
        decomp.dispose();
        println("Done! Output: " + outputPath);
    }
}
```

### Key Variables Available in GhidraScript

| Variable | Type | Purpose |
|----------|------|---------|
| `currentProgram` | `Program` | The loaded binary — access to memory, functions, symbols, references |
| `monitor` | `TaskMonitor` | Progress tracking + cancellation checking |
| `toAddr(hexString)` | `Address` | Convert hex string to Address object |

---

## 2. Decompilation

### Standard Function Decompilation

```java
private void decompileFunctionAt(PrintWriter pw, FunctionManager funcMgr,
        DecompInterface decomp, String addrStr, int timeout, TaskMonitor monitor) {
    try {
        long addrLong = Long.parseLong(addrStr.replace("0x", ""), 16);
        Address addr = currentProgram.getAddressFactory().getDefaultAddressSpace().getAddress(addrLong);
        Function func = funcMgr.getFunctionAt(addr);
        if (func == null) {
            func = funcMgr.getFunctionContaining(addr);
        }
        if (func == null) {
            pw.println("// NO FUNCTION FOUND AT 0x" + addrStr);
            return;
        }

        // Handle thunks — decompile the actual target, not the thunk stub
        if (func.isThunk()) {
            Function thunked = func.getThunkedFunction(true);
            if (thunked != null) {
                pw.println("// THUNK OF: " + thunked.getName() + " @ " + thunked.getEntryPoint());
                func = thunked;
            }
        }

        DecompileResults results = decomp.decompileFunction(func, timeout, monitor);
        if (results != null && results.decompileCompleted()) {
            pw.println(results.getDecompiledFunction().getC());
        } else {
            pw.println("// DECOMPILATION FAILED");
            if (results != null) pw.println("// Error: " + results.getErrorMessage());
        }
    } catch (Exception e) {
        pw.println("// ERROR: " + e.getMessage());
    }
}
```

### Timeout Guidelines

| Function Size | Timeout (seconds) | Example |
|--------------|-------------------|---------|
| < 1,000 bytes | 120 | Most thunks, street crossing (1312 bytes) |
| 1,000–5,000 bytes | 180 | Vehicle decision, entity search |
| 5,000–10,000 bytes | 300 | AI state machine |
| > 10,000 bytes | 600 | SIM_TICK (16,980 bytes) |

**Critical**: Always use extended timeouts for SIM_TICK (`FUN_005d2740`). Previous
scripts used 120s which may have truncated the driving cases (4, 8, 10).

### Thunk Handling

Ghidra generates thunk functions for cross-segment calls. A thunk is a small stub
that jumps to the real function. Always resolve thunks before decompiling:

```java
if (func.isThunk()) {
    Function thunked = func.getThunkedFunction(true);
    if (thunked != null) {
        func = thunked;  // Decompile the actual function
    }
}
```

**Naming convention**: Thunks are named `thunk_FUN_XXXXXXXX`. The actual function
is `FUN_XXXXXXXX` at a different address.

---

## 3. Caller Tracing

### Trace All Callers of a Function

```java
private void traceCallers(PrintWriter pw, FunctionManager funcMgr,
        ReferenceManager refMgr, DecompInterface decomp,
        String targetAddrStr, String label, TaskMonitor monitor) {

    long addrLong = Long.parseLong(targetAddrStr, 16);
    Address targetAddr = currentProgram.getAddressFactory().getDefaultAddressSpace().getAddress(addrLong);

    ReferenceIterator refIter = refMgr.getReferencesTo(targetAddr);
    Set<String> callerFuncAddrs = new LinkedHashSet<>();

    while (refIter.hasNext()) {
        Reference ref = refIter.next();
        Function callerFunc = funcMgr.getFunctionContaining(ref.getFromAddress());
        if (callerFunc != null) {
            callerFuncAddrs.add(callerFunc.getEntryPoint().toString());
        }
    }

    // If no direct refs, try thunk resolution
    if (callerFuncAddrs.isEmpty()) {
        Function targetFunc = funcMgr.getFunctionAt(targetAddr);
        if (targetFunc != null && targetFunc.isThunk()) {
            Function thunked = targetFunc.getThunkedFunction(true);
            if (thunked != null) {
                ReferenceIterator refIter2 = refMgr.getReferencesTo(thunked.getEntryPoint());
                while (refIter2.hasNext()) {
                    Reference ref = refIter2.next();
                    Function callerFunc = funcMgr.getFunctionContaining(ref.getFromAddress());
                    if (callerFunc != null) {
                        callerFuncAddrs.add(callerFunc.getEntryPoint().toString());
                    }
                }
            }
        }
    }

    // Decompile each caller
    for (String funcAddrStr : callerFuncAddrs) {
        if (monitor.isCancelled()) break;
        decompileFunctionAt(pw, funcMgr, decomp, funcAddrStr.replace("0x", ""), 120, monitor);
    }
}
```

### Important: Vtable Calls Have Zero Direct References

Functions called via vtable (indirect calls through function pointer tables) will
show **0 direct references** in Ghidra's reference manager. This is expected. To
find vtable-called functions, use binary pattern scanning (Section 5) or vtable
table resolution (Section 6).

Known vtable-called functions (from RE findings):
- Pathfinding thunks (`thunk_FUN_005642c0`, `thunk_FUN_00565120`, `thunk_FUN_0060c3c0`)
- All vtable method calls (`+0x20`, `+0x58`, `+0x80`, `+0xC8`, etc.)

---

## 4. String Search

Search for strings in the binary and find what references them:

```java
String[] searchStrings = {"collision", "traffic", "yield", "tram", "train"};

for (String searchStr : searchStrings) {
    try {
        Address searchAddr = toAddr("0x0");
        while (true) {
            Address found = find(searchAddr, searchStr.getBytes());
            if (found == null) break;

            // Check if defined as a string data item
            Data data = currentProgram.getListing().getDataAt(found);
            String value = (data != null && data.getValue() != null)
                ? data.getValue().toString() : "";

            // Find what references this string
            ReferenceIterator strRefs = refMgr.getReferencesTo(found);
            Set<String> strCallers = new LinkedHashSet<>();
            while (strRefs.hasNext()) {
                Reference ref = strRefs.next();
                Function caller = funcMgr.getFunctionContaining(ref.getFromAddress());
                if (caller != null) {
                    strCallers.add(caller.getName() + " @ " + caller.getEntryPoint());
                }
            }

            pw.println("String '" + searchStr + "' at " + found);
            if (!value.isEmpty()) pw.println("  Value: " + value);
            if (!strCallers.isEmpty()) {
                pw.println("  Referenced by:");
                for (String c : strCallers) pw.println("    " + c);
            }

            searchAddr = found.add(1);  // Continue searching after this hit
        }
    } catch (Exception e) { /* continue */ }
}
```

### Case Sensitivity

The `find()` method is case-sensitive. Always search both lower and upper case
variants for strings: `{"collision", "Collision", "COLLISION"}`.

---

## 5. Binary Pattern Scanning

Scan raw bytes in the `.text` section to find indirect calls, comparisons, and
data references that Ghidra's reference manager can't track.

### Reading the .text Section

```java
long startAddr = Long.parseLong("00401000", 16);  // .text start
long endAddr = Long.parseLong("00780000", 16);     // .text end
long size = endAddr - startAddr;
byte[] data = new byte[(int)size];
Address start = currentProgram.getAddressFactory().getDefaultAddressSpace().getAddress(startAddr);
currentProgram.getMemory().getBytes(start, data);
```

### x86 Instruction Patterns

#### Indirect Call via Vtable: `call [reg+offset]`

| Offset Size | Pattern | Example |
|-------------|---------|---------|
| disp8 (offset ≤ 0x7F) | `FF 5X offset` | `call [eax+0x20]` = `FF 50 20` |
| disp32 (offset > 0x7F) | `FF 9X offset 00 00 00` | `call [eax+0xC8]` = `FF 90 C8 00 00 00` |

**Decoding**:
- `FF` = opcode for CALL/JMP indirect
- ModRM byte: `mod` (bits 7-6), `reg` (bits 5-3), `rm` (bits 2-0)
- `reg=2` means CALL (vs `reg=4` for JMP)
- `mod=01` = disp8, `mod=10` = disp32
- `rm=4` means SIB byte follows (skip — not a simple [reg] addressing)
- `rm=5` with `mod=00` means absolute address (skip)

```java
// disp8: call [reg+0xNN]
for (int i = 0; i < data.length - 2; i++) {
    if ((data[i] & 0xFF) == 0xFF) {
        int modrm = data[i+1] & 0xFF;
        int mod = (modrm >> 6) & 3;
        int reg = (modrm >> 3) & 7;
        int rm = modrm & 7;
        if (mod == 1 && reg == 2 && rm != 4) {  // CALL with disp8
            int disp = data[i+2] & 0xFF;
            if (disp == targetOffset) {
                // Found call [reg+targetOffset]
            }
        }
    }
}

// disp32: call [reg+0xNNNNNNNN]
for (int i = 0; i < data.length - 5; i++) {
    if ((data[i] & 0xFF) == 0xFF) {
        int modrm = data[i+1] & 0xFF;
        int mod = (modrm >> 6) & 3;
        int reg = (modrm >> 3) & 7;
        int rm = modrm & 7;
        if (mod == 2 && reg == 2 && rm != 4) {  // CALL with disp32
            int disp = ((data[i+5] & 0xFF) << 24) | ((data[i+4] & 0xFF) << 16) |
                       ((data[i+3] & 0xFF) << 8) | (data[i+2] & 0xFF);
            if (disp == targetOffset) {
                // Found call [reg+targetOffset]
            }
        }
    }
}
```

#### CMP Register with Immediate: `CMP reg, imm8`

| Pattern | Instruction |
|---------|-------------|
| `83 F8 XX` | `CMP EAX, 0xXX` |
| `83 F9 XX` | `CMP ECX, 0xXX` |
| `3C XX` | `CMP AL, 0xXX` |
| `80 38 XX` | `CMP BYTE [EAX], 0xXX` |

**Decoding**: `83` opcode with ModRM `mod=11, reg=7` = CMP. `rm` field selects
register: 0=EAX, 1=ECX, 2=EDX, 3=EBX, 4=ESP, 5=EBP, 6=ESI, 7=EDI.

#### AND/TEST with Immediate: blockage flag checks

| Pattern | Instruction |
|---------|-------------|
| `81 E0 80 04 00 00` | `AND EAX, 0x480` |
| `F7 C0 80 04 00 00` | `TEST EAX, 0x480` |

#### MOV/CMP with disp32: struct field access

| Pattern | Instruction |
|---------|-------------|
| `C6 80 79 01 00 00 01` | `MOV BYTE [EAX+0x179], 1` |
| `80 B8 79 01 00 00 00` | `CMP BYTE [EAX+0x179], 0` |

### Byte Sign Handling

**Critical**: Java bytes are signed. Always mask with `& 0xFF`:

```java
// WRONG — negative bytes will break comparison
if (data[i] == 0xFF) { ... }

// CORRECT
if ((data[i] & 0xFF) == 0xFF) { ... }
```

### Little-Endian Int32 Reading

```java
int val = ((data[i+3] & 0xFF) << 24) | ((data[i+2] & 0xFF) << 16) |
          ((data[i+1] & 0xFF) << 8) | (data[i] & 0xFF);
```

---

## 6. Vtable Table Resolution

The most powerful technique for resolving indirect calls. Scan data sections for
arrays of consecutive function pointers.

### Scanning .rdata for Vtable Tables

```java
// .rdata/.data section: 0x00780000 - 0x007C0000
long dataStart = Long.parseLong("00780000", 16);
long dataEnd = Long.parseLong("007C0000", 16);
byte[] dataSection = new byte[(int)(dataEnd - dataStart)];
currentProgram.getMemory().getBytes(
    currentProgram.getAddressFactory().getDefaultAddressSpace().getAddress(dataStart),
    dataSection);

// Look for consecutive 4-byte values that are valid code addresses
// A vtable has >= 8 consecutive function pointers
int consecutiveCount = 0;
int vtableStart = -1;

for (int i = 0; i < dataSection.length - 4; i += 4) {
    int val = readInt32LE(dataSection, i);
    if (val >= 0x00401000 && val <= 0x00780000) {
        if (consecutiveCount == 0) vtableStart = i;
        consecutiveCount++;
    } else {
        if (consecutiveCount >= 8) {
            // Found a vtable — resolve entries
            long vtableAddr = dataStart + vtableStart;
            for (int j = 0; j < consecutiveCount; j++) {
                int entryVal = readInt32LE(dataSection, vtableStart + j * 4);
                Address funcAddr = toAddr(String.format("0x%08X", entryVal));
                Function f = funcMgr.getFunctionAt(funcAddr);
                int offset = j * 4;
                pw.println(String.format("  +0x%02X: %s", offset,
                    f != null ? f.getName() : "UNKNOWN"));
            }
        }
        consecutiveCount = 0;
    }
}
```

### Vtable Offset to Function Mapping

Once a vtable is found, specific offsets resolve to specific functions:

| Offset | Known Purpose | Used By |
|--------|--------------|---------|
| `+0x20` | Passability check | STREET_CROSS, STREET_ACCESS |
| `+0x58` | Direction check (4 cardinal) | Arrest execution |
| `+0x80` | AI brain tick | Vehicle decision, order dispatch |
| `+0x84` | Idle/update | SIM_TICK state 0 |
| `+0x88` | Animation trigger (extort/torch) | Order execution |
| `+0x8C` | Animation trigger (arrest/melee) | Combat functions |
| `+0xA0` | Animation frame update | SIM_TICK per-tick |
| `+0xC8` | Occupancy check | STREET_ACCESS ("not occupied") |
| `+0xD0` | Line-of-sight check | Combat approach |
| `+0xDC` | Arrest success check | Arrest execution |
| `+0x14C` | Immediate execution | High-priority orders |

---

## 7. Global State Access

The game uses a global state structure at `DAT_007c0024`. Key offsets:

| Offset | Purpose | Used In |
|--------|---------|---------|
| `+0x24` | Entity active pool (linked list) | SIM_TICK |
| `+0xE4` | Update queue | SIM_TICK |
| `+0x104` | Movement queue | SIM_TICK |
| `+0x124` | Combat/action queue | SIM_TICK |
| `+0x144` | Secondary action queue | SIM_TICK |
| `+0x1220` | Traffic signal data (4 bytes/road: W/E/N/S open) | STREET_CROSS, STREET_ACCESS |
| `+0x16D8` | Global speed value (timer countdown rate) | WAYPOINT_FOLLOW, COMBAT_3 |
| `+0x1B18` | Default speed (stored as short) | WAYPOINT_FOLLOW |
| `+0x210` | Current player ID | Entity ownership checks |

### Tracing Global State References

```java
Address globalAddr = toAddr("0x007c0024");
ReferenceIterator refs = refMgr.getReferencesTo(globalAddr);
while (refs.hasNext()) {
    Reference ref = refs.next();
    Function f = funcMgr.getFunctionContaining(ref.getFromAddress());
    // Log and optionally decompile each accessor
}
```

For specific offsets like traffic signals (`0x007c0024 + 0x1220 = 0x007c1244`):

```java
Address trafficAddr = toAddr("0x007c1244");
ReferenceIterator refs = refMgr.getReferencesTo(trafficAddr);
// These are the functions that read/write traffic light state
```

---

## 8. Output File Conventions

### Naming

| Script Type | Output Pattern | Example |
|-------------|---------------|---------|
| General analysis | `ghidra_analysis_output.txt` | FindOrderLogic.java |
| Key functions | `ghidra_key_functions.txt` | DecompileKeyFunctions.java |
| Time system | `ghidra_time_functions.txt` | DecompileTimeFunctions.java |
| Engine core | `ghidra_engine_core.txt` | DecompileEngineCore.java |
| Pathfinding/combat | `ghidra_pathfinding_combat.txt` | DecompilePathfindingAndCombat.java |
| Vtable calls | `ghidra_vtable_calls.txt` | SearchVtableCalls.java |
| Vehicle/ped interaction | `ghidra_vehicle_ped_interaction.txt` | DecompileVehiclePedInteraction.java |

### Output Structure

Every output file should have:

```
================================================================================
TITLE - GANGSTERS.EXE
Brief description
================================================================================

################################################################################
# PART N: SECTION NAME
# Description
################################################################################

============================================================
FUNCTION: LABEL
ADDRESS: 0xXXXXXXXX
DESCRIPTION: ...
============================================================

// Decompiled C code...

---
```

---

## 9. Existing Script Inventory

| Script | Purpose | Output |
|--------|---------|--------|
| `FindOrderLogic.java` | String + constant search, batch decompile | `ghidra_analysis_output.txt` |
| `FindOrderLogic.py` | Python equivalent (legacy) | — |
| `DecompileKeyFunctions.java` | 19 high-priority functions | `ghidra_key_functions.txt` |
| `DecompileTimeFunctions.java` | Functions referencing 12000 constant | `ghidra_time_functions.txt` |
| `DecompileEngineCore.java` | 47 core engine functions + caller traces | `ghidra_engine_core.txt` |
| `DecompilePathfindingAndCombat.java` | SIM_TICK, combat, pathfinding, street crossing | `ghidra_pathfinding_combat.txt` |
| `DecompileDecisionAndPortraits.java` | Walk/drive decision + portrait system | `ghidra_decision_portraits.txt` |
| `DecompileDecisionFunctions.java` | Decision function decompilation | — |
| `DecompileOrderFunctions.java` | Order processing functions | — |
| `DecompileHoodAI.java` | Hood AI behavior | — |
| `DecompileStreetOrderAI.java` | Street order AI | — |
| `TraceThunkCallers.java` | Walk/drive/time thunk caller traces | — |
| `FindVehicleFlagSetters.java` | 0x80000/0x38000000 flag search | `ghidra_vehicle_flags.txt` |
| `FindVehicleStateSetters.java` | Individual vehicle bit setters | `ghidra_vehicle_state_setters.txt` |
| `FindPortraitSystem.java` | Portrait/character generation | `ghidra_portrait_system.txt` |
| `FindWalkDriveCallers.java` | Walk/drive dispatcher callers | — |
| `FindAIBrain.java` | AI brain function search | — |
| `ResolveAIBrain.java` | AI brain resolution | — |
| `SearchVtableCalls.java` | Binary pattern scan for vtable calls | `ghidra_vtable_calls.txt` |
| `TraceMovementSetup.java` | Movement setup tracing | — |
| `TraceOrderCreation.java` | Order creation tracing | — |
| `TraceStreetOrders.java` | Street order tracing | — |
| `TraceWalkDriveDecision.java` | Walk/drive decision tracing | — |
| `DecompileVehiclePedInteraction.java` | Vehicle/ped interaction, vtable resolution, entity type search, traffic signal access | `ghidra_vehicle_ped_interaction.txt` |
| `SearchTrafficSignalWrites.java` | Search for writes to road access flags (`DAT_007c0024 + 0x1220`) | `ghidra_traffic_signal_writes.txt` |
| `DecompileRoadAccessInit.java` | Decompile `FUN_00650ee0` (city constructor — road access flag init) | `ghidra_road_access_init.txt` |
| `DecompileTrafficInteractions.java` | **NEW** — Comprehensive ped/vehicle/tram interaction: entity awareness, blocked crossing, reroute, post-processing, tram type-8 dispatch, occupancy vtable callers | `ghidra_traffic_interactions.txt` |

---

## 10. Key Addresses Quick Reference

### Functions

| Address | Name | Size | Purpose |
|---------|------|------|---------|
| `0x005d2740` | `FUN_005d2740` | 16,980 | SIM_TICK — master per-tick simulation |
| `0x005dc8c0` | `FUN_005dc8c0` | 1,312 | Street crossing (4-directional, 6-cell max) |
| `0x00609cf0` | `FUN_00609cf0` | 571 | Street access check (4 cardinal directions) |
| `0x004cb0c0` | `FUN_004cb0c0` | — | State 1: vehicle decision (walk=12000, drive=32) |
| `0x00583dc0` | `FUN_00583dc0` | 1,652 | State 0 init + state 3 arrived |
| `0x005844a0` | `FUN_005844a0` | — | Waypoint following with countdown timer |
| `0x00568870` | `thunk_FUN_00568870` | — | Micro-movement (in-block adjustment) |
| `0x005dd9d0` | `FUN_005dd9d0` | — | Entity search in rectangular area |
| `0x00462f30` | `FUN_00462f30` | — | Distance-based walk/drive decision (threshold 0x40) |
| `0x0048a750` | `thunk_FUN_0048a750` | — | Animation lookup + walk/drive mode setter |
| `0x005dc080` | `FUN_005dc080` | 557 | Drive state transition (steal/find vehicle) |
| `0x00660e60` | `FUN_00660e60` | 765 | Vehicle assignment for street orders (25% random) |
| `0x00664d50` | `thunk_FUN_00664d50` | — | Get map cell at (x,y) — most-called function |
| `0x00565c30` | `thunk_FUN_00565c30` | — | Time consumption (156 refs, 67 callers) |
| `0x00565790` | `thunk_FUN_00565790` | — | Time budget allocation |

### Global State

| Address | Offset | Purpose |
|---------|--------|---------|
| `0x007c0024` | — | Global state structure base |
| `0x007c1244` | `+0x1220` | Traffic signal data (4 bytes/road segment) |
| `0x007c16FC` | `+0x16D8` | Global speed value (timer countdown rate) |
| `0x007c1B3C` | `+0x1B18` | Default speed (short) |

### Entity Types

| Type ID | Entity | SIM_TICK Cases |
|---------|--------|----------------|
| 8 | Tram | Unknown (vtable dispatch?) |
| 9 | Train | Unknown (vtable dispatch?) |
| 0xC | Trucks | 4, 8, 10 (driving) |
| 0xD | Cars (civilian/roadster/police) | 4, 8, 10 (driving) |
| 0x10–0x24 | People (hoods/civilians) | 0-3, 9 (walking/pathfinding) |

### Memory Layout

| Section | Range | Content |
|---------|-------|---------|
| `.text` | `0x00401000`–`0x00780000` | Code (executable instructions) |
| `.rdata` | `0x00780000`–`0x007C0000` | Read-only data (vtables, strings, constants) |

---

## 11. Common Pitfalls

1. **Signed bytes**: Java `byte` is signed. Always use `& 0xFF` when comparing
   against hex values.

2. **Thunk resolution**: Functions at known addresses may be thunks. Always check
   `func.isThunk()` and resolve to the thunked function before decompiling.

3. **Vtable calls have 0 direct references**: Don't rely on `refMgr.getReferencesTo()`
   for vtable-dispatched functions. Use binary pattern scanning instead.

4. **Decompilation timeout**: Large functions (SIM_TICK = 16,980 bytes) need 600s.
   Default 120s will truncate or fail.

5. **Output path**: Always use forward slashes in output paths:
   `"C:/Users/..."` not `"C:\Users\..."`.

6. **SIB bytes**: When scanning for `call [reg+offset]`, skip `rm=4` (SIB byte
   follows, different addressing mode). Also skip `rm=5` with `mod=00` (absolute
   address, not register indirect).

7. **Little-endian**: x86 is little-endian. Multi-byte values are stored
   least-significant byte first.

8. **Case-sensitive string search**: `find()` is case-sensitive. Search both
   cases for strings.

9. **`@category` annotation controls Script Manager folder**: Ghidra groups scripts
   by their `//@category` annotation in the Script Manager. **Always use `//@category Analysis`**
   for all gangsters.exe RE scripts. Using a different category (e.g. `Gangsters`) causes the
   script to appear in a separate folder in the Script Manager, making it hard to find alongside
   other scripts. The annotation must be `//@category` (no space after `//`) — Ghidra parses
   this as a script metadata directive, not a regular comment.

---

## 12. Workflow: Adding a New Script

1. **Identify target**: What functions/patterns/data do you need to extract?
2. **Choose pattern**:
   - Decompile known functions → Part 1 pattern
   - Find callers of a function → `traceCallers()` helper
   - Find vtable call sites → Binary pattern scan (Section 5)
   - Resolve vtable tables → Data section scan (Section 6)
   - Search for strings → `find()` loop (Section 4)
   - Search for constants/immediates → Binary pattern scan
3. **Copy skeleton**: Start from the script skeleton (Section 1)
4. **Add analysis parts**: Each part gets a `# PART N` header
5. **Test**: Run in Ghidra Script Manager, check output file
6. **Document**: Add to script inventory (Section 9) and update `docs/core/DOCUMENTATION_INDEX.md`
7. **Update RE findings**: Add discoveries to `REVERSE_ENGINEERING_FINDINGS.md`
