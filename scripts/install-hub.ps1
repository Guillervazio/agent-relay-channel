<#
.SYNOPSIS
    Deja el hub funcionando en esta máquina: regla de firewall y servicio de Windows.

.DESCRIPTION
    Necesita consola de administrador. Abre el puerto SÓLO para el perfil de red
    privado — el canal es de tu LAN y no tiene por qué verse desde una red pública.

    El token es obligatorio: el hub acepta instrucciones entre agentes y no debe
    quedar accesible sin autenticar. Si no pasas -Token se genera uno y se muestra.

.EXAMPLE
    ./scripts/install-hub.ps1 -HubPath C:\arc\hub -Token (Read-Host 'token')
    ./scripts/install-hub.ps1 -HubPath C:\arc\hub -FirewallOnly
    ./scripts/install-hub.ps1 -Uninstall
#>
[CmdletBinding()]
param(
    [string]$HubPath,
    [string]$Token,
    [int]$Port = 8765,
    [string]$ServiceName = 'ArcHub',
    [string]$DatabasePath,
    [switch]$FirewallOnly,
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$ruleName = "ARC hub (puerto $Port)"

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    (New-Object Security.Principal.WindowsPrincipal $identity).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-Administrator)) {
    throw 'Ejecuta este script desde una consola de PowerShell como administrador.'
}

if ($Uninstall) {
    Write-Host 'Retirando el servicio y la regla de firewall...' -ForegroundColor Cyan
    if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
        & sc.exe stop $ServiceName | Out-Null
        & sc.exe delete $ServiceName | Out-Null
        Write-Host "  servicio $ServiceName eliminado"
    }
    Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue |
        Remove-NetFirewallRule -ErrorAction SilentlyContinue
    Write-Host '  regla de firewall eliminada'
    return
}

# ---------- Firewall ----------

if (Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue) {
    Write-Host "La regla de firewall ya existe: $ruleName" -ForegroundColor DarkGray
} else {
    New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Action Allow `
        -Protocol TCP -LocalPort $Port -Profile Private | Out-Null
    Write-Host "Regla de firewall creada para el puerto $Port (sólo perfil privado)." -ForegroundColor Green
}

if ($FirewallOnly) { return }

# ---------- Servicio ----------

if (-not $HubPath) { throw 'Indica -HubPath: la carpeta donde publicaste el hub.' }

$exe = Join-Path (Resolve-Path $HubPath) 'Arc.Hub.exe'
if (-not (Test-Path $exe)) {
    throw "No encuentro Arc.Hub.exe en $HubPath. Ejecuta antes ./scripts/publish.ps1."
}

if (-not $Token) {
    $Token = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
    Write-Host ''
    Write-Host 'Token generado (guárdalo, lo necesitan los dos agentes):' -ForegroundColor Yellow
    Write-Host "  $Token" -ForegroundColor Yellow
    Write-Host ''
}

if (-not $DatabasePath) { $DatabasePath = Join-Path (Resolve-Path $HubPath) 'arc.db' }

# El servicio no hereda tu entorno: las variables se fijan a nivel de máquina.
[Environment]::SetEnvironmentVariable('ARC_TOKEN', $Token, 'Machine')
[Environment]::SetEnvironmentVariable('ARC_DB', $DatabasePath, 'Machine')
[Environment]::SetEnvironmentVariable('ARC_URLS', "http://0.0.0.0:$Port", 'Machine')

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Write-Host "El servicio $ServiceName ya existe; lo recreo." -ForegroundColor DarkGray
    & sc.exe stop $ServiceName | Out-Null
    & sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 1
}

& sc.exe create $ServiceName binPath= "`"$exe`"" start= auto DisplayName= 'ARC hub' | Out-Null
& sc.exe description $ServiceName 'Canal de peticiones entre agentes de distintos proveedores.' | Out-Null
& sc.exe start $ServiceName | Out-Null

Start-Sleep -Seconds 2
$health = try { Invoke-RestMethod "http://127.0.0.1:$Port/healthz" -TimeoutSec 5 } catch { $null }

Write-Host ''
if ($health -and $health.status -eq 'ok') {
    Write-Host "Servicio $ServiceName en marcha y respondiendo." -ForegroundColor Green
} else {
    Write-Host "Servicio creado, pero /healthz aún no responde. Revisa: Get-Service $ServiceName" -ForegroundColor Yellow
}

$ip = (Get-NetIPAddress -AddressFamily IPv4 |
    Where-Object { $_.IPAddress -notlike '127.*' -and $_.PrefixOrigin -ne 'WellKnown' } |
    Select-Object -First 1).IPAddress

Write-Host ''
Write-Host 'Configura los agentes con:'
Write-Host "  ARC_URL   = http://$ip`:$Port"
Write-Host "  ARC_TOKEN = $Token"
Write-Host "  ARC_AGENT = <nombre distinto en cada máquina>"
Write-Host ''
Write-Host "Para ver la conversación en directo: http://$ip`:$Port/ui"
