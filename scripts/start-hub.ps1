<#
.SYNOPSIS
    Arranca el hub en esta consola, sin instalar nada.

.DESCRIPTION
    Lo que hace cualquiera la primera vez, y lo único que install-hub.ps1 no cubre:
    dejarlo corriendo aquí delante, con Ctrl+C para pararlo y ningún servicio detrás.
    Para que sobreviva a cerrar la consola y arranque con la máquina está el otro.

    Por defecto sólo escucha en loopback, que es lo que hace falta para probarlo aquí.
    Con -Lan escucha en toda la red y comprueba antes lo que la LAN necesita y este
    script no puede dar: la regla de firewall la crea install-hub.ps1 -FirewallOnly,
    porque abrir un puerto pide elevación y arrancar el hub no. Lo dice al empezar en
    vez de dejar que falle desde la otra PC media hora después.

    El token no se inventa cada vez si ya hay uno: se usa el que pases, el de esta
    consola, o el que install-hub.ps1 dejó a nivel de máquina; sólo si no hay ninguno
    se genera, y entonces se muestra, porque los agentes lo necesitan.

.EXAMPLE
    ./scripts/start-hub.ps1
    ./scripts/start-hub.ps1 -Lan
    ./scripts/start-hub.ps1 -Lan -Token (Read-Host 'token')
#>
[CmdletBinding()]
param(
    [switch]$Lan,
    [string]$Token,
    [ValidateRange(1, 65535)]
    [int]$Port = 8765,
    [string]$DatabasePath
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

. "$PSScriptRoot/ArcHost.ps1"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'No encuentro dotnet en el PATH. El hub necesita el SDK de .NET 10.'
}

# ---------- El token ----------

$origen = 'el que has pasado por -Token'

if ([string]::IsNullOrWhiteSpace($Token)) {
    $Token = $env:ARC_TOKEN
    $origen = 'el de esta consola ($env:ARC_TOKEN)'
}
if ([string]::IsNullOrWhiteSpace($Token)) {
    $Token = [Environment]::GetEnvironmentVariable('ARC_TOKEN', 'Machine')
    $origen = 'el que install-hub.ps1 dejó a nivel de máquina'
}
if ([string]::IsNullOrWhiteSpace($Token)) {
    $Token = New-ArcToken
    $origen = 'generado ahora, y sólo vive mientras dure este arranque'
}

# ---------- Loopback o LAN ----------

if ($Lan) {
    $urls = "http://0.0.0.0:$Port"

    # Se dice lo que falta; no se crea nada. Comprobar si la regla existe ya necesita
    # elevación —Get-NetFirewallPortFilter contesta «acceso denegado» a un usuario
    # normal—, así que se nombra sin fingir que se ha mirado. La clasificación de la
    # red sí se puede leer desde aquí, y es la causa habitual de que la otra PC no
    # llegue con el puerto abierto.
    Write-Host ''
    Write-Host 'Para que la otra máquina llegue hace falta la regla de firewall, que este' -ForegroundColor Yellow
    Write-Host 'script no crea porque abrir un puerto pide elevación. Una sola vez, desde' -ForegroundColor Yellow
    Write-Host 'una consola de administrador:' -ForegroundColor Yellow
    Write-Host "      ./scripts/install-hub.ps1 -Port $Port -FirewallOnly" -ForegroundColor Yellow

    $publicas = @(Get-NetConnectionProfile -ErrorAction SilentlyContinue |
        Where-Object { $_.NetworkCategory -eq 'Public' })
    if ($publicas.Count -gt 0) {
        Write-Host ''
        Write-Host 'Y esa regla abre sólo el perfil privado, así que estas redes no valen tal cual:' -ForegroundColor Yellow
        foreach ($p in $publicas) {
            Write-Host "      $($p.Name) — $($p.NetworkCategory)" -ForegroundColor Yellow
        }
        Write-Host '  Set-NetConnectionProfile -Name <red> -NetworkCategory Private, también como administrador.' -ForegroundColor Yellow
    }
} else {
    $urls = "http://127.0.0.1:$Port"
}

# ---------- Dónde queda el buzón ----------

# Sin ARC_DB el hub la deja junto al ejecutable, que con `dotnet run` es un directorio
# de compilación: un `dotnet clean` se llevaría el canal por delante. Aquí, en la raíz.
if ([string]::IsNullOrWhiteSpace($DatabasePath)) {
    $DatabasePath = Join-Path $root 'arc.db'
}

$candidatas = @(Get-ArcLanAddress)

$visible = "http://127.0.0.1:$Port"
if ($Lan -and $candidatas.Count -gt 0) { $visible = "http://$($candidatas[0])`:$Port" }

Write-Host ''
Write-Host 'Arrancando el hub. Ctrl+C para pararlo.' -ForegroundColor Cyan
Write-Host "  escucha en   $urls"
Write-Host "  buzón        $DatabasePath"
Write-Host "  token        $Token"
Write-Host "               ($origen)"
Write-Host ''
Write-Host 'En la consola de cada agente:'
Write-Host "      `$env:ARC_URL = '$visible'"
Write-Host "      `$env:ARC_TOKEN = '$Token'"
Write-Host "      `$env:ARC_AGENT = '<nombre distinto en cada máquina>'"

if ($Lan) {
    if ($candidatas.Count -eq 0) {
        Write-Host ''
        Write-Host 'No he sabido por qué interfaz sales a la red: ninguna tiene puerta de enlace.' -ForegroundColor Yellow
        Write-Host 'Mira tu IP con Get-NetIPConfiguration y usa esa en ARC_URL.' -ForegroundColor Yellow
    } elseif ($candidatas.Count -gt 1) {
        Write-Host ''
        Write-Host "Hay más de una salida a la red; he puesto la primera. Las otras: $($candidatas[1..($candidatas.Count - 1)] -join ', ')" -ForegroundColor DarkGray
    }
}

Write-Host ''

$env:ARC_TOKEN = $Token
$env:ARC_URLS = $urls
$env:ARC_DB = $DatabasePath

# Un fallo antes de que dotnet llegue a devolver nada no debe salir como un cero.
$code = 1

Push-Location $root
try {
    & dotnet run --project src/Arc.Hub --no-launch-profile
    $code = $LASTEXITCODE
}
finally {
    Pop-Location
}

exit $code
