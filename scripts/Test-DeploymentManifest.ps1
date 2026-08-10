[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackagePath)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $PackagePath).Path
$manifestPath = Join-Path $root 'deployment-manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'No existe deployment-manifest.json.' }

$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1) { throw "Versión de manifiesto no soportada: $($manifest.schemaVersion)." }
if ($manifest.application -ne 'Sigotest') { throw "Aplicación inesperada: $($manifest.application)." }
if (-not $manifest.files -or $manifest.files.Count -eq 0) { throw 'El manifiesto no declara archivos.' }

$expected = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $manifest.files) {
    $relative = ([string]$entry.path).Replace('/', '\')
    if ([string]::IsNullOrWhiteSpace($relative) -or [IO.Path]::IsPathRooted($relative) -or $relative.Split('\') -contains '..') {
        throw "Ruta insegura en manifiesto: $relative"
    }
    if (-not $expected.Add($relative)) { throw "Ruta duplicada en manifiesto: $relative" }
    $path = Join-Path $root $relative
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Falta archivo declarado: $relative" }
    $file = Get-Item -LiteralPath $path
    if ($file.Length -ne [long]$entry.length) { throw "Tamaño inválido: $relative" }
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if ($hash -ne [string]$entry.sha256) { throw "Hash inválido: $relative" }
}

$actual = Get-ChildItem -LiteralPath $root -Recurse -File |
    Where-Object FullName -ne $manifestPath |
    ForEach-Object { $_.FullName.Substring($root.Length + 1) }
$unexpected = @($actual | Where-Object { -not $expected.Contains($_) })
if ($unexpected.Count) { throw "Archivos no declarados: $($unexpected -join ', ')" }

[pscustomobject]@{
    Application = $manifest.application
    Files = $expected.Count
    GeneratedUtc = $manifest.generatedUtc
    Valid = $true
}
