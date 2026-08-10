[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$SourceRepository = (Split-Path $PSScriptRoot -Parent),
    [string]$DestinationRepository = 'C:\Dev\Sigotest.Deploy',
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $SourceRepository).Path
$destinationItem = Get-Item -LiteralPath $DestinationRepository -ErrorAction Stop
$destination = $destinationItem.FullName

if (-not $destinationItem.PSIsContainer) { throw 'El destino debe ser un directorio.' }
if ($source -eq $destination) { throw 'Origen y destino deben ser diferentes.' }
if ($destination.StartsWith($source + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'El repositorio de despliegue no puede estar dentro del repositorio de desarrollo.' }
if ((Get-ChildItem -LiteralPath $destination -Force -ErrorAction Stop) -and -not $Force) {
    throw 'El destino no está vacío. Revise su contenido o repita con -Force explícitamente.'
}

$requiredSourceFiles = @(
    'Sigotest.sln',
    'NuGet.Config',
    '.gitignore',
    '.gitattributes',
    'README.md',
    '.github\workflows\deploy.yml',
    'scripts\New-DeploymentManifest.ps1',
    'scripts\Test-DeploymentManifest.ps1',
    'templates\AGENTS.deploy.md'
)

foreach ($relativePath in $requiredSourceFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $source $relativePath) -PathType Leaf)) {
        throw "Falta archivo requerido en origen: $relativePath"
    }
}

$excludedDirectoryNames = @('bin', 'obj', 'artifacts', '.git', '.vs', '.vscode')
$excludedFileNames = @('.env', 'secrets.json', 'appsettings.Production.json', 'appsettings.Local.json')
$excludedExtensions = @('.pfx', '.p12', '.key', '.cer')

function Copy-ApprovedFile([string]$SourcePath, [string]$RelativeDestination) {
    $targetPath = Join-Path $destination $RelativeDestination
    if ((Test-Path -LiteralPath $targetPath) -and -not $Force) {
        throw "El destino ya existe: $RelativeDestination. Revíselo o use -Force explícitamente."
    }
    $targetDirectory = Split-Path $targetPath -Parent
    if (-not (Test-Path -LiteralPath $targetDirectory)) {
        if ($PSCmdlet.ShouldProcess($targetDirectory, 'Crear directorio')) {
            New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null
        }
    }
    if ($PSCmdlet.ShouldProcess($targetPath, 'Copiar archivo aprobado')) {
        Copy-Item -LiteralPath $SourcePath -Destination $targetPath -Force:$Force
    }
}

foreach ($relativePath in @('Sigotest.sln', 'NuGet.Config', '.gitignore', '.gitattributes', 'README.md')) {
    Copy-ApprovedFile (Join-Path $source $relativePath) $relativePath
}

Copy-ApprovedFile (Join-Path $source '.github\workflows\deploy.yml') '.github\workflows\deploy.yml'
Copy-ApprovedFile (Join-Path $source 'scripts\New-DeploymentManifest.ps1') 'scripts\New-DeploymentManifest.ps1'
Copy-ApprovedFile (Join-Path $source 'scripts\Test-DeploymentManifest.ps1') 'scripts\Test-DeploymentManifest.ps1'
Copy-ApprovedFile (Join-Path $source 'templates\AGENTS.deploy.md') 'AGENTS.md'

foreach ($rootName in @('src', 'tests')) {
    $rootPath = Join-Path $source $rootName
    Get-ChildItem -LiteralPath $rootPath -Recurse -File | Where-Object {
        $relativeParts = $_.FullName.Substring($rootPath.Length + 1).Split([IO.Path]::DirectorySeparatorChar)
        -not ($relativeParts | Where-Object { $_ -in $excludedDirectoryNames }) -and
        $_.Name -notin $excludedFileNames -and
        $_.Extension -notin $excludedExtensions
    } | ForEach-Object {
        $relativePath = $_.FullName.Substring($source.Length + 1)
        Copy-ApprovedFile $_.FullName $relativePath
    }
}

foreach ($documentName in @(
    '03-publicacion.md',
    '06-github-actions.md',
    '07-operacion-y-rollback.md',
    '11-arquitectura-github-free.md',
    '12-github-actions.md',
    '13-instalacion-runner.md',
    '15-preflight-github.md',
    '16-release-candidate.md',
    'DECISIONS.md'
)) {
    $documentPath = Join-Path $source "docs\$documentName"
    if (Test-Path -LiteralPath $documentPath) {
        Copy-ApprovedFile $documentPath "docs\$documentName"
    }
}

Write-Output "Workspace de despliegue preparado en: $destination"
Write-Output 'No se inicializó Git, no se configuró remote y no se realizó commit o push.'

if (Test-Path -LiteralPath (Join-Path $destination '.git')) {
    git -C $destination status --short
} else {
    Get-ChildItem -LiteralPath $destination -Force | Select-Object Name, Mode, Length
}
