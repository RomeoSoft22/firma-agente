# Genera instalador\salida\AgenteFirmaSetup.exe:
#   1) publica el agente como un único .exe autocontenido (no requiere .NET en la PC del usuario)
#   2) compila el instalador con Inno Setup 6 (winget install JRSoftware.InnoSetup)
# Uso: powershell -ExecutionPolicy Bypass -File instalador\construir.ps1
$ErrorActionPreference = 'Stop'
$raiz = Split-Path $PSScriptRoot -Parent
$publicacion = Join-Path $raiz 'publish'

Write-Host '== Publicando el agente' -ForegroundColor Cyan
dotnet publish $raiz -c Release -nologo -o $publicacion
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish falló' }

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'No se encontró Inno Setup 6. Instálelo con: winget install JRSoftware.InnoSetup' }

Write-Host '== Compilando el instalador' -ForegroundColor Cyan
& $iscc "/DPublicacion=$publicacion" (Join-Path $PSScriptRoot 'AgenteFirma.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup falló' }

$setup = Join-Path $PSScriptRoot 'salida\AgenteFirmaSetup.exe'
Write-Host "`nListo: $setup ($([math]::Round((Get-Item $setup).Length / 1MB, 1)) MB)" -ForegroundColor Green
