# _ear_triage.ps1 — seat ear-layer triage for the commission machine.
# Usage: powershell -NoProfile -File _ear_triage.ps1 -Seat BUTTON
# Finds and severs every watcher + spawn-shell carrying your seat name,
# zeroes your presence stamp, then verifies the kill. Safe to rerun —
# it is idempotent and seat-scoped (it cannot touch other seats' ears).
#
# WHY THIS EXISTS (the zombie anatomy, learned the hard way):
#   - A dead watcher does NOT mean a dead subagent. The owner subagent
#     survives its child and re-arms a fresh one -> PID churn.
#   - Killing the watcher alone feeds the churn. You must also kill the
#     bash SHELL that spawned it — a shell-less subagent cannot exec
#     and dies on its next poll.
#   - Detached children (DETACHED_PROCESS) have no live parent; the
#     spawn-shell regex below catches the spawn command itself instead.
param(
  [Parameter(Mandatory = $true)]
  [ValidateSet('ARCHITECT','INFORMANT','AUDITOR','CHAIR','BUTTON','BOSS')]
  [string]$Seat
)

$ErrorActionPreference = 'SilentlyContinue'
# Carrier predicate — matches EITHER ear tool: commission_pulse.py (current)
# and the deprecated commission_watch.py (legacy zombies can still linger).
# The null-guard comes first: Win32_Process.CommandLine is $null on some
# system processes, and $null -match can throw even under SilentlyContinue.
$watchRe = "commission_(?:pulse|watch)\.py\s+$Seat\b"

Write-Output "=== EAR TRIAGE: $Seat ==="

# 1) Inventory before the kill — the carrier predicate (BUTTON's amendment):
#    count EVERY process whose CommandLine carries the seat's watcher string,
#    regardless of process name — python watchers, bash spawn-shells, cmd
#    wrappers, anything. Counting only python leaves the chain's other links
#    invisible (the "7 carriers behind a clean sweep" bug).
$carriers = Get-CimInstance Win32_Process |
            Where-Object { $_.CommandLine -and $_.CommandLine -match $watchRe }
$watchers = @($carriers | Where-Object { $_.Name -like 'python*' })
$shells   = @($carriers | Where-Object { $_.Name -notlike 'python*' })
Write-Output ("found: {0} watcher(s), {1} other carrier(s) — {2} total" -f $watchers.Count, $shells.Count, @($carriers).Count)

# 2) Kill the carriers (watchers first), then each one's parent shell —
#    a shell-less subagent cannot exec and dies on its next poll.
foreach ($w in $watchers) {
  Stop-Process -Id $w.ProcessId -Force
  Write-Output ("killed watcher {0} (ppid {1})" -f $w.ProcessId, $w.ParentProcessId)
}
foreach ($c in @($carriers)) {
  Stop-Process -Id $c.ParentProcessId -Force   # parent may already be gone
}
foreach ($s in $shells) {
  Stop-Process -Id $s.ProcessId -Force
  Write-Output ("killed carrier {0} ({1})" -f $s.ProcessId, $s.Name)
}

# 3) Darken the lamp honestly — a zeroed stamp expires instantly,
#    rather than lying lit for the 30s TTL.
$pfile = Join-Path $PSScriptRoot ("presence_{0}.js" -f $Seat)
if (Test-Path $pfile) {
  $body = 'window.DVM_PRESENCE_{0} = {{"seat": "{0}", "armed_at": 0, "ttl_ms": 0, "pulse_at": 0, "beat": false}};' -f $Seat
  Set-Content -Path $pfile -Value $body -Encoding UTF8
  Write-Output "zeroed $pfile"
}

# 4) Verify — recount carriers after the kill. The process list is the only
#    truth, and the recount uses the same any-name predicate.
Start-Sleep -Seconds 2
$left = Get-CimInstance Win32_Process |
        Where-Object { $_.CommandLine -and $_.CommandLine -match $watchRe }
Write-Output ("remaining: {0} carrier(s)" -f @($left).Count)

if (@($left).Count -eq 0) {
  Write-Output "TRIAGE PASS — $Seat ear layer severed and lamp dark"
} else {
  Write-Output "TRIAGE INCOMPLETE — rerun this script; if a watcher returns with"
  Write-Output "a fresh PID under a NEW parent, a stale subagent is still re-arming:"
  Write-Output "sever its spawn-shell next time it fires, or close the session."
}
