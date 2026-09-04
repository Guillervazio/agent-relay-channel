<#
.SYNOPSIS
    Lo que start-hub.ps1 e install-hub.ps1 tienen que hacer igual.

.DESCRIPTION
    Dos funciones, y las dos están aquí por el mismo motivo: cada una se escribió
    corrigiendo un fallo, y el otro script se quedó con la versión rota durante un
    incremento entero. Una copia en cada uno es exactamente cómo volvería a pasar.

    No es un script que se ejecute: los dos lo cargan con dot-source.
#>

function New-ArcToken {
    <#
    .SYNOPSIS
        Un token de 24 bytes en base64, en cualquiera de las dos ediciones de PowerShell.
    #>
    # El GetBytes estático es de .NET Core en adelante; Windows PowerShell corre sobre
    # .NET Framework, donde no existe. Create() está en las dos ediciones.
    $bytes = New-Object byte[] 24
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    [Convert]::ToBase64String($bytes)
}

function Get-ArcLanAddress {
    <#
    .SYNOPSIS
        Las direcciones IPv4 por las que esta máquina sale a la red, en orden.

    .DESCRIPTION
        La dirección que hay que dar a la otra PC es la de la interfaz por la que se sale
        a la red, no la primera que no sea loopback: en una máquina con WSL, con una VPN o
        con Hyper-V, esa primera es casi siempre una interfaz virtual que desde fuera no
        existe.

        Devuelve todas las candidatas, y puede no devolver ninguna. Quién llama decide qué
        decir en cada caso: aquí no se inventa una dirección ni se calla que había varias.
    #>
    @(Get-NetIPConfiguration -ErrorAction SilentlyContinue |
        Where-Object { $_.IPv4DefaultGateway -and $_.NetAdapter.Status -eq 'Up' } |
        ForEach-Object { $_.IPv4Address.IPAddress })
}
