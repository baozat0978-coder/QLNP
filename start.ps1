<#
.SYNOPSIS
    Khởi động nhanh ứng dụng Quản lý nghỉ phép (Blazor Server .NET 8).

.EXAMPLE
    .\start.ps1                # chạy HTTPS (https://localhost:7187), tự mở trình duyệt
    .\start.ps1 -Watch         # chạy dotnet watch (hot reload khi sửa code)
    .\start.ps1 -Http          # chỉ chạy HTTP (http://localhost:5187)
    .\start.ps1 -Release       # chạy cấu hình Release
    .\start.ps1 -NoBrowser     # không tự mở trình duyệt
    .\start.ps1 -Restore       # restore NuGet trước khi chạy
#>
[CmdletBinding()]
param(
    [switch]$Watch,
    [switch]$Http,
    [switch]$Release,
    [switch]$NoBrowser,
    [switch]$Restore
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$Root        = $PSScriptRoot
$ProjectDir  = Join-Path $Root 'src\QuanLyNghiPhep'
$ProjectFile = Join-Path $ProjectDir 'QuanLyNghiPhep.csproj'
$LaunchProfile     = if ($Http) { 'http' } else { 'https' }
$Url         = if ($Http) { 'http://localhost:5187' } else { 'https://localhost:7187' }
$Port        = if ($Http) { 5187 } else { 7187 }
$Config      = if ($Release) { 'Release' } else { 'Debug' }

function Write-Step($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }
function Write-Warn($msg) { Write-Host "[!] $msg" -ForegroundColor Yellow }

# 1. Kiểm tra .NET SDK
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host 'Không tìm thấy dotnet. Cài .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0' -ForegroundColor Red
    exit 1
}
$sdks = & dotnet --list-sdks
if (-not ($sdks -match '^(8|9|10)\.')) {
    Write-Warn 'Không thấy .NET SDK 8 trở lên. Dự án cần net8.0.'
}

# 2. Dừng instance cũ đang chạy (tránh lỗi khóa file khi build)
$old = Get-Process -Name 'QuanLyNghiPhep' -ErrorAction SilentlyContinue
if ($old) {
    Write-Step "Dừng instance cũ đang chạy (PID: $($old.Id -join ', '))"
    $old | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

# 3. Kiểm tra cổng
$busy = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
if ($busy) {
    $owner = Get-Process -Id $busy[0].OwningProcess -ErrorAction SilentlyContinue
    Write-Warn "Cổng $Port đang bị chiếm bởi '$($owner.ProcessName)' (PID $($busy[0].OwningProcess)). Hãy tắt tiến trình đó rồi chạy lại."
    exit 1
}

# 4. Chứng chỉ HTTPS dev
if (-not $Http) {
    & dotnet dev-certs https --check --trust *> $null
    if ($LASTEXITCODE -ne 0) {
        Write-Step 'Chưa có chứng chỉ HTTPS dev tin cậy, đang tạo (có thể hiện hộp thoại xác nhận)...'
        & dotnet dev-certs https --trust
    }
}

# 5. Restore nếu được yêu cầu hoặc chưa từng restore
if ($Restore -or -not (Test-Path (Join-Path $ProjectDir 'obj\project.assets.json'))) {
    Write-Step 'Restore NuGet packages'
    & dotnet restore (Join-Path $Root 'QuanLyNghiPhep.sln')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

# 6. Tự mở trình duyệt khi server sẵn sàng (dotnet run không tự mở)
if (-not $NoBrowser -and -not $Watch) {
    Start-Job -Name 'QLNP-OpenBrowser' -ArgumentList $Port, $Url -ScriptBlock {
        param($Port, $Url)
        for ($i = 0; $i -lt 180; $i++) {
            try {
                $c = New-Object System.Net.Sockets.TcpClient
                $c.Connect('127.0.0.1', $Port); $c.Close()
                Start-Process $Url
                return
            } catch { Start-Sleep -Seconds 1 }
        }
    } | Out-Null
}

# 7. Nhắc tệp tài khoản ban đầu
$accFile = Join-Path $ProjectDir 'AppData\tai-khoan-ban-dau.txt'
if (Test-Path $accFile) {
    Write-Host "Tài khoản ban đầu: $accFile" -ForegroundColor DarkGray
}

# 8. Chạy
Write-Step "Khởi động ($Config, profile $LaunchProfile) -> $Url"
Write-Host 'Nhấn Ctrl+C để dừng.' -ForegroundColor DarkGray

Push-Location $ProjectDir
try {
    if ($Watch) {
        & dotnet watch --project $ProjectFile run -c $Config --launch-profile $LaunchProfile
    } else {
        & dotnet run --project $ProjectFile -c $Config --launch-profile $LaunchProfile
    }
} finally {
    Pop-Location
    Get-Job -Name 'QLNP-OpenBrowser' -ErrorAction SilentlyContinue | Remove-Job -Force
}
