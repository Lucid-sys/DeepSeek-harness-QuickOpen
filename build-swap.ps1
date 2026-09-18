# Swap a freshly published launcher into place, even while the launcher is
# RUNNING and serving the web GUI.
#
# A running executable's file cannot be overwritten, but Windows does allow it
# to be RENAMED. So the strategy is:
#   1. fast path  - the canonical StartDSH.exe is not the running image (because
#                   an earlier update already pushed it aside): just overwrite.
#   2. slow path  - it is the running image: rename it aside under a UNIQUE
#                   timestamped name, then copy the new build into place.
#                   The unique name matters: a plain "StartDSH.old.exe" collides
#                   with the previous update's still-locked file, and Move-Item
#                   refuses to overwrite, which is exactly how the first version
#                   of this script silently did nothing.
#
# ASCII only: Windows PowerShell 5.1 reads .ps1 with the system ANSI code page
# unless the file carries a BOM, so non-ASCII text here could be misread.
#
# Usage: powershell -File build-swap.ps1 -Dist <dist dir> -New <new exe>

param(
    [Parameter(Mandatory = $true)][string]$Dist,
    [Parameter(Mandatory = $true)][string]$New
)

$ErrorActionPreference = 'Stop'

$live = Join-Path $Dist 'StartDSH.exe'
if (-not (Test-Path -LiteralPath $New)) { Write-Error "new build not found: $New"; exit 2 }
if (-not (Test-Path -LiteralPath $Dist)) { New-Item -ItemType Directory -Path $Dist -Force | Out-Null }

$swapped = $false

# 1) fast path
try {
    Copy-Item -LiteralPath $New -Destination $live -Force -ErrorAction Stop
    Write-Output "swapped in place: $live"
    $swapped = $true
} catch {
    Write-Output "in-place overwrite refused ($($_.Exception.Message)); trying rename"
}

# 2) rename the running image aside, then copy
if (-not $swapped) {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $aside = Join-Path $Dist "StartDSH.old-$stamp.exe"
    try {
        Move-Item -LiteralPath $live -Destination $aside -Force -ErrorAction Stop
        Write-Output "running image renamed aside: $(Split-Path -Leaf $aside)"
    } catch {
        Write-Warning "could not move the running image aside: $($_.Exception.Message)"
    }

    try {
        Copy-Item -LiteralPath $New -Destination $live -Force -ErrorAction Stop
        Write-Output "swapped after rename: $live"
        $swapped = $true
    } catch {
        Write-Warning "still could not place the new build: $($_.Exception.Message)"
    }
}

if (-not $swapped) {
    $next = Join-Path $Dist 'StartDSH.next.exe'
    Copy-Item -LiteralPath $New -Destination $next -Force
    Write-Output "left the new build at: $next"
    Write-Output "close the running launcher, then rename it to StartDSH.exe"
    exit 1
}

# 3) best effort: clean aside files that are no longer locked
Get-ChildItem -LiteralPath $Dist -Filter 'StartDSH.old*.exe' -ErrorAction SilentlyContinue | ForEach-Object {
    # Capture the name first: inside `catch`, $_ is the error record, not the file.
    $name = $_.Name
    $path = $_.FullName
    try {
        Remove-Item -LiteralPath $path -Force -ErrorAction Stop
        Write-Output "removed stale $name"
    } catch {
        Write-Output "kept $name (still locked by a running process)"
    }
}

exit 0
