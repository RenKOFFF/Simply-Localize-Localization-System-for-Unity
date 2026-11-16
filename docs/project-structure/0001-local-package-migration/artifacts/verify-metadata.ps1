param([switch]$ExactBytes)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))
$baseline = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'metadata-baseline.json') -Raw | ConvertFrom-Json
$failures = @()
foreach ($entry in $baseline.files) {
    $path = [IO.Path]::GetFullPath((Join-Path $repo $entry.path))
    if (!$path.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Baseline path outside repository: $($entry.path)"
    }
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) {
        $failures += "Missing: $($entry.path)"
        continue
    }
    $guid = [regex]::Match([IO.File]::ReadAllText($path), '(?m)^guid: ([a-f0-9]{32})').Groups[1].Value
    if ($guid -ne $entry.guid) { $failures += "Changed GUID: $($entry.path)" }
    if ($ExactBytes -and (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.sha256) {
        $failures += "Changed bytes (including line endings): $($entry.path)"
    }
}
[pscustomobject]@{ checked = $baseline.files.Count; exactBytes = [bool]$ExactBytes; failures = $failures } | ConvertTo-Json
if ($failures.Count) { throw 'Metadata verification failed.' }
