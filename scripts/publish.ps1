<#
.SYNOPSIS
    Publica el hub y el cliente listos para copiar a cualquiera de las dos PCs.

.DESCRIPTION
    El cliente sale autocontenido: la máquina de destino no necesita tener
    instalado .NET. El hub sale dependiente del runtime, porque vive en la
    máquina donde ya lo tienes.

.EXAMPLE
    ./scripts/publish.ps1
    ./scripts/publish.ps1 -SelfContainedHub
#>
[CmdletBinding()]
param(
    [string]$Runtime = 'win-x64',
    [string]$Output = 'publish',
    [switch]$SelfContainedHub
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root

try {
    $hubOut = Join-Path $Output 'hub'
    $cliOut = Join-Path $Output 'cli'

    Write-Host "Publicando el hub en $hubOut ..." -ForegroundColor Cyan
    $hubArgs = @('publish', 'src/Arc.Hub/Arc.Hub.csproj', '-c', 'Release', '-o', $hubOut, '--nologo')
    if ($SelfContainedHub) {
        $hubArgs += @('-r', $Runtime, '--self-contained', 'true')
    }
    & dotnet @hubArgs
    if ($LASTEXITCODE -ne 0) { throw "Falló la publicación del hub (código $LASTEXITCODE)." }

    Write-Host "Publicando el cliente en $cliOut ..." -ForegroundColor Cyan
    & dotnet publish src/Arc.Cli/Arc.Cli.csproj -c Release -r $Runtime --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -o $cliOut --nologo
    if ($LASTEXITCODE -ne 0) { throw "Falló la publicación del cliente (código $LASTEXITCODE)." }

    $arc = Join-Path $cliOut 'arc.exe'
    Write-Host ''
    Write-Host 'Listo.' -ForegroundColor Green
    Write-Host "  hub      $hubOut"
    Write-Host "  cliente  $arc"
    Write-Host ''
    Write-Host 'Copia la carpeta del cliente a la otra PC y añádela al PATH.'
    Write-Host 'El cliente no necesita .NET instalado allí; el hub sí, salvo que uses -SelfContainedHub.'
}
finally {
    Pop-Location
}
