[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)][string]$DevelopmentRepository,
    [Parameter(Mandatory)][string]$DeploymentRepository
)

$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $DevelopmentRepository).Path
$target = (Resolve-Path -LiteralPath $DeploymentRepository).Path
if ($source -eq $target) { throw 'Los repositorios deben ser diferentes.' }
if (-not (Test-Path (Join-Path $source '.git'))) { throw 'La ruta de desarrollo no es un repositorio Git.' }
if (-not (Test-Path (Join-Path $target '.git'))) { throw 'La ruta de despliegue no es un repositorio Git.' }

$allowedFiles = @('Sigotest.sln', 'README.md', 'NuGet.Config', '.gitignore')
$allowedDirectories = @('src', 'tests', 'docs')
$excludedNames = @('.git', '.github', 'artifacts', 'bin', 'obj', '.vs', '.vscode', '.env', 'secrets.json', 'appsettings.Production.json', 'appsettings.Local.json')
$excludedExtensions = @('.pfx', '.p12', '.key', '.cer')

foreach ($name in $allowedFiles) {
    $item = Join-Path $source $name
    if (Test-Path -LiteralPath $item) { Copy-Item -LiteralPath $item -Destination (Join-Path $target $name) -Force -WhatIf:$WhatIfPreference }
}

foreach ($directory in $allowedDirectories) {
    $root = Join-Path $source $directory
    if (-not (Test-Path $root)) { continue }
    Get-ChildItem $root -Recurse -File | Where-Object {
        $segments = $_.FullName.Substring($root.Length).Split([IO.Path]::DirectorySeparatorChar, [StringSplitOptions]::RemoveEmptyEntries)
        -not ($segments | Where-Object { $_ -in $excludedNames }) -and $_.Extension -notin $excludedExtensions
    } | ForEach-Object {
        $relative = $_.FullName.Substring($source.Length + 1)
        $destination = Join-Path $target $relative
        $destinationDirectory = Split-Path $destination -Parent
        if (-not (Test-Path $destinationDirectory)) { New-Item -ItemType Directory -Path $destinationDirectory -Force -WhatIf:$WhatIfPreference | Out-Null }
        Copy-Item -LiteralPath $_.FullName -Destination $destination -Force -WhatIf:$WhatIfPreference
    }
}

git -C $target diff -- .
git -C $target status --short
