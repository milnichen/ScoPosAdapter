# Установка Windows-службы ScoPosAdapter (от администратора).
# Повторный запуск обновляет binPath. Не ставить, если VSS сам запускает этот exe.
$ErrorActionPreference = "Stop"

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Запустите от администратора: powershell -ExecutionPolicy Bypass -File install-service.ps1"
}

$exe = Join-Path $PSScriptRoot "ScoPosAdapter.exe"
if (-not (Test-Path $exe)) {
    $exe = Join-Path $PSScriptRoot "bin\Release\net48\ScoPosAdapter.exe"
}
if (-not (Test-Path $exe)) {
    throw "Не найден ScoPosAdapter.exe. Сначала: dotnet build -c Release"
}

$url = "http://127.0.0.1:15002/"
netsh http add urlacl url=$url user=Everyone | Out-Null

$existing = Get-Service -Name ScoPosAdapter -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -eq "Running") {
        sc.exe stop ScoPosAdapter | Out-Null
        Start-Sleep -Seconds 2
    }
    sc.exe delete ScoPosAdapter | Out-Null
    Start-Sleep -Seconds 1
}

sc.exe create ScoPosAdapter binPath= "`"$exe`" --service" start= auto DisplayName= "Ksisters SCO POS Adapter (VSS)"
if ($LASTEXITCODE -ne 0) { throw "sc create failed: $LASTEXITCODE" }
sc.exe description ScoPosAdapter "POS Adapter Diebold Vynamic Self-Service. Не занимает порты 15000/15001 (FisCat/FMSK)."
sc.exe failure ScoPosAdapter reset= 86400 actions= restart/5000/restart/15000/restart/60000
sc.exe start ScoPosAdapter
Write-Host "Service installed. Health: GET http://127.0.0.1:15002/health"
Write-Host "Не запускайте второй экземпляр через VSS, пока служба работает."
