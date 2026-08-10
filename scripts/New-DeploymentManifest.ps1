[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackagePath)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $PackagePath).Path
$manifestPath = Join-Path $root 'deployment-manifest.json'

$entries = Get-ChildItem -LiteralPath $root -Recurse -File |
    Where-Object FullName -ne $manifestPath |
    Sort-Object FullName |
    ForEach-Object {
        [pscustomobject]@{
            path = $_.FullName.Substring($root.Length + 1).Replace('\', '/')
            length = $_.Length
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }

[pscustomobject]@{
    schemaVersion = 1
    application = 'Sigotest'
    generatedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    files = @($entries)
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

Write-Output "Manifest: $manifestPath ($($entries.Count) archivos)"
