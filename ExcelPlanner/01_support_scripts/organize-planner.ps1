param([Parameter(Mandatory)][string]$PlanningRoot,[switch]$Apply)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# This one-off reorganization refuses conflicts and verifies every moved file.
$planning = (Resolve-Path -LiteralPath $PlanningRoot).Path.TrimEnd('\')
$target = Join-Path $planning 'Excel prototypes'
$source = Join-Path $planning 'Planner_disponibilita'
$support = Join-Path $target '01_support_scripts'
$archive = 'planner_authoring_sources_2026-09-24'
function Assert-InPlanning([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith($planning + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Outside intended Planning directory: $full"
    }
    return $full
}
$moves = @(
    @{From=(Join-Path $source 'Planner_Availability_A4.xlsx'); To=(Join-Path $target 'Planner_Availability_A4.xlsx')},
    @{From=(Join-Path $source 'Calendar data'); To=(Join-Path $target 'Calendar data')},
    @{From=(Join-Path $source 'GETTING_STARTED.md'); To=(Join-Path $support 'GETTING_STARTED.md')},
    @{From=(Join-Path $source 'Excel_Utility_Implementation_Plan.md'); To=(Join-Path $support 'Excel_Utility_Implementation_Plan.md')},
    @{From=(Join-Path $source 'Planner_Availability_A4.pdf'); To=(Join-Path $support 'Planner_Availability_A4.pdf')},
    @{From=(Join-Path $target $archive); To=(Join-Path $support $archive)},
    @{From=(Join-Path $target 'TRUE ORIGINAL.xlsx'); To=(Join-Path $support 'Originali\TRUE ORIGINAL.xlsx')},
    @{From=(Join-Path $target 'Planner_Riunioni_e_Giornata_2025-10-20.xlsx'); To=(Join-Path $support 'Originali\Planner_Riunioni_e_Giornata_2025-10-20.xlsx')}
)
$inventory = [Collections.Generic.List[object]]::new()
foreach ($move in $moves) {
    $move.From = Assert-InPlanning $move.From
    $move.To = Assert-InPlanning $move.To
    $item = Get-Item -LiteralPath $move.From -Force
    if ($item.LinkType) { throw "Refusing linked source: $($move.From)" }
    if (Test-Path -LiteralPath $move.To) { throw "Destination already exists: $($move.To)" }
    $files = if ($item.PSIsContainer) { @(Get-ChildItem -LiteralPath $move.From -File -Recurse -Force) } else { @($item) }
    foreach ($file in $files) {
        if ($file.LinkType) { throw "Refusing linked file: $($file.FullName)" }
        $destination = if ($item.PSIsContainer) { Join-Path $move.To ([IO.Path]::GetRelativePath($move.From, $file.FullName)) } else { $move.To }
        $inventory.Add([pscustomobject]@{From=$file.FullName; To=(Assert-InPlanning $destination); SHA256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash})
        if ($inventory.Count -gt 150) { throw 'Unexpectedly large inventory; review scope before proceeding.' }
    }
}
$guideTarget = Assert-InPlanning (Join-Path $support 'Data_Guide.md')
if (Test-Path -LiteralPath $guideTarget) { throw "Destination already exists: $guideTarget" }
$lock = [IO.File]::Open($moves[0].From, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
$lock.Dispose()
$moves | ForEach-Object { Write-Output "$($_.From) -> $($_.To)" }
Write-Output "Files inventoried: $($inventory.Count). Apply: $Apply"
if (-not $Apply) { return }

foreach ($move in $moves) {
    $parent = Assert-InPlanning (Split-Path -Parent $move.To)
    if (-not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent | Out-Null }
    Move-Item -LiteralPath $move.From -Destination $move.To -ErrorAction Stop
}
foreach ($entry in $inventory) {
    if ((Get-FileHash -LiteralPath $entry.To -Algorithm SHA256).Hash -ne $entry.SHA256) { throw "Hash mismatch: $($entry.To)" }
}
$guideSource = Assert-InPlanning (Join-Path $target 'Calendar data\GETTING_STARTED.md')
Move-Item -LiteralPath $guideSource -Destination $guideTarget
foreach ($entry in $inventory) { if ($entry.To -eq $guideSource) { $entry.To = $guideTarget } }

# A manifest is a generated report, not an edited source file.
$report = [ordered]@{Operation='Reorganize planner without deleting files'; Timestamp=(Get-Date -Format o); FilesVerified=$inventory.Count; Files=$inventory}
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $support 'relocation-manifest.json') -Encoding utf8
if (@(Get-ChildItem -LiteralPath $source -Force).Count -eq 0) {
    # Exact, validated, empty folder only: no recursive deletion.
    Remove-Item -LiteralPath (Assert-InPlanning $source) -Force
}
Write-Output "Moved and SHA-256 verified $($inventory.Count) files; no file deleted."
