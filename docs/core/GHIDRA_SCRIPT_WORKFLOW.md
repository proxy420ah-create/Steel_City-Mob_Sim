# Ghidra Script Workflow Guide

**Project**: SteelCityMobSim — Gangsters.exe Reverse Engineering  
**Ghidra Version**: 12.1.2 PUBLIC  
**Platform**: Windows 10 (PowerShell)

---

## Script Storage

| Location | Purpose | Status |
|----------|---------|--------|
| `C:\Tools\ghidra_scripts\` | All custom Java/Python scripts | **Enabled** in Bundle Manager |
| `C:\Users\NADECC\ghidra_scripts\` | Ghidra default user path | **Disabled** — leave unchecked |
| `C:\Tools\ghidra_12.1.2_PUBLIC\Ghidra\Features\*\ghidra_scripts\` | Built-in Ghidra scripts | Optional — re-enable after custom script work |

### Why Two Paths Cause Problems

Ghidra's Bundle Manager has a built-in `$USER_HOME/ghidra_scripts` path that maps to `C:\Users\NADECC\ghidra_scripts`. If this directory:
- **Doesn't exist** → Bundle Manager shows "file not found" error
- **Contains copies** → Scripts appear duplicated in Script Manager with red error icons

**Rule**: Only use `C:\Tools\ghidra_scripts\`. Never copy scripts to the user home directory.

---

## Script Creation Checklist

When creating a new Ghidra script:

1. **Write the file** to `C:\Tools\ghidra_scripts\ScriptName.java`
2. **Class name must match filename**: `public class ScriptName extends GhidraScript`
3. **Include category annotation**: `// @category Analysis` (or appropriate category)
4. **Verify single class declaration** and single closing brace at column 0
5. **Refresh Script Manager**: Click the green circular arrow icon
6. **Verify script appears** under the Analysis category in Script Manager tree

### Common Pitfalls

| Issue | Cause | Fix |
|-------|-------|-----|
| Script not in Script Manager | OSGi cache stale | Restart Ghidra (not just refresh) |
| Red error icon next to script | Duplicate file in `$USER_HOME/ghidra_scripts` | Delete copies from user home, uncheck `$USER_HOME` bundle |
| "file not found" in Bundle Manager | `$USER_HOME/ghidra_scripts` doesn't exist | Uncheck that bundle path |
| Script runs but finds 0 results | Program not analyzed | Run Analysis → Auto Analyze first |
| `NumberFormatException: "UNKNOWN"` | Script tried to parse non-hex address | Skip entries where function address is `"UNKNOWN"` |

---

## OSGi Cache Management

Ghidra 12 compiles scripts into OSGi bundles stored at:

```
C:\Users\NADECC\AppData\Roaming\ghidra\ghidra_12.1.2_PUBLIC\osgi\compiled-bundles\
```

### When to Clear the Cache

- After creating a new script that doesn't appear in Script Manager
- After fixing a compilation error in an existing script
- When scripts show red error icons that won't clear

### How to Clear

```powershell
Remove-Item "C:\Users\NADECC\AppData\Roaming\ghidra\ghidra_12.1.2_PUBLIC\osgi\compiled-bundles\*" -Force -Recurse
```

**After clearing**: Restart Ghidra completely. The Script Manager recompiles all scripts on launch.

---

## Pre-Run Requirements

Before running any analysis script:

1. **Open the project**: File → Open Project → `ghidra_project\GanstersToSteelCity2`
2. **Open the program**: Double-click `gangsters.exe` in the project tree
3. **Verify analysis is complete**: Code Browser should show disassembled instructions (`MOV`, `CALL`, `PUSH`), not raw bytes (`?? 00h`)
4. **If not analyzed**: Analysis → Auto Analyze → click Analyze → wait → File → Save

### Diagnostic Check

Run this quick check in the Script Manager console or look at script output:

```
Program: gangsters.exe
Total functions: should be >1000 (not 136)
Total instructions scanned: should be >0
```

If functions count is low (~136) or instructions is 0, the program needs analysis.

---

## Script Inventory

| Script | File | Purpose |
|--------|------|---------|
| `FindOrderLogic` | `FindOrderLogic.java` | String + constant search, batch decompile |
| `DecompileKeyFunctions` | `DecompileKeyFunctions.java` | Targeted decompilation of 19 functions |
| `DecompileTimeFunctions` | `DecompileTimeFunctions.java` | Time-constant function decompilation |
| `DecompileEngineCore` | `DecompileEngineCore.java` | Decompiles 47 core engine functions + traces callers |
| `TraceThunkCallers` | `TraceThunkCallers.java` | Traces callers of walk/drive/time thunk functions |
| `TraceOrderCreation` | `TraceOrderCreation.java` | Maps order names to type bytes and walk/drive dispatch |

### Output Files

All scripts write to:
```
C:\Users\NADECC\ATSTradingDashboard Project\Cursor Workshop\SteelCityMobSim\
```

| Script | Output File |
|--------|------------|
| `FindOrderLogic` | `ghidra_analysis_output.txt` |
| `DecompileKeyFunctions` | `ghidra_key_functions.txt` |
| `DecompileTimeFunctions` | `ghidra_time_functions.txt` |
| `DecompileEngineCore` | `ghidra_engine_core.txt` |
| `TraceThunkCallers` | `ghidra_thunk_callers.txt` |
| `TraceOrderCreation` | `ghidra_order_creation.txt` |

---

## Script Code Patterns

### Standard Script Structure

```java
// Description of what the script does
// @category Analysis

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.FunctionManager;
import ghidra.program.model.symbol.Reference;
import ghidra.program.model.symbol.ReferenceIterator;
import ghidra.program.model.symbol.ReferenceManager;
import ghidra.program.model.address.Address;
import ghidra.util.task.TaskMonitor;

import java.io.FileWriter;
import java.io.PrintWriter;
import java.util.*;

public class ScriptName extends GhidraScript {

    private String outputPath = "C:/Users/NADECC/ATSTradingDashboard Project/Cursor Workshop/SteelCityMobSim/output_file.txt";

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
    }

    private void decompileFunctionAt(PrintWriter pw, FunctionManager funcMgr,
            DecompInterface decomp, String addrStr, TaskMonitor monitor) {
        // Standard decompilation helper
    }
}
```

### Key APIs Used

| API | Purpose |
|-----|---------|
| `funcMgr.getFunctionAt(addr)` | Get function at exact address |
| `funcMgr.getFunctionContaining(addr)` | Get function containing an address |
| `refMgr.getReferencesTo(addr)` | Find all references to an address |
| `listing.getInstructions(true)` | Iterate all instructions |
| `listing.getDefinedData(true)` | Iterate all defined data (strings, etc.) |
| `func.isThunk()` / `func.getThunkedFunction(true)` | Trace thunk → real function |
| `decomp.decompileFunction(func, timeout, monitor)` | Decompile to C pseudocode |

### Thunk Tracing Pattern

Ghidra functions in this binary are frequently called via thunks (cross-segment call wrappers). To find callers of a function:

1. Find direct references to the function address
2. Iterate all functions, find thunks pointing to the target
3. Find references to each thunk address
4. Collect all caller functions from both paths

```java
// Find thunks of a target function
FunctionIterator fIter = funcMgr.getFunctions(true);
while (fIter.hasNext()) {
    Function f = fIter.next();
    if (f.isThunk()) {
        Function thunkedFunc = f.getThunkedFunction(true);
        if (thunkedFunc != null && thunkedFunc.getEntryPoint().equals(targetAddr)) {
            // Found a thunk — trace references to this thunk
            ReferenceIterator refIter = refMgr.getReferencesTo(f.getEntryPoint());
            // ...
        }
    }
}
```

---

## Troubleshooting Flowchart

```
Script not visible in Script Manager?
├── Check Bundle Manager: is C:/Tools/ghidra_scripts enabled?
│   ├── No → Enable it, click Refresh
│   └── Yes → Continue
├── Is $USER_HOME/ghidra_scripts causing errors?
│   ├── Yes → Uncheck it in Bundle Manager
│   └── No → Continue
├── Try: Refresh Script Manager (green circular arrow)
├── Still not visible? → Clear OSGi cache, restart Ghidra
└── Still not visible? → Check Java syntax errors in the script

Script runs but returns 0 results?
├── Check output for "Total functions" count
│   ├── ~136 → Program not analyzed → Run Auto Analyze
│   └── >1000 → Continue
├── Check "Total instructions scanned"
│   ├── 0 → Program not analyzed → Run Auto Analyze
│   └── >0 → Continue
└── Check address format matches program's address space

Script crashes with NumberFormatException?
└── Script parsing "UNKNOWN" as hex address
    → Add guard: if (addr.equals("UNKNOWN")) continue;
```
