#Requires -Version 5.1
<#
.SYNOPSIS
    Đặt database demo của CinemaBooking.Smart về đúng trạng thái seed.

.DESCRIPTION
    Script làm bốn việc, theo thứ tự:
      1. Dừng Web/Worker đang chạy (chúng giữ kết nối tới database nên không drop được).
      2. Drop database (mặc định LocalDB\CinemaBookingSmart).
      3. Build solution.
      4. Chạy Web MỘT LẦN để Program.cs áp migration rồi CatalogSeeder + IdentitySeeder, đợi trang
         chủ trả về đủ phim, rồi tắt.

    Vì sao phải chạy Web mới seed được: phần seed nằm trong `Program.cs` (migration → catalog →
    identity), không có lệnh CLI nào làm hộ. Đợi trang chủ render ra thẻ phim là cách duy nhất chắc
    chắn cả ba bước đã chạy xong.

.PARAMETER StartWeb
    Để Web chạy tiếp sau khi seed xong (mặc định là tắt). Dùng khi chuẩn bị demo.

.PARAMETER SkipBuild
    Bỏ bước build, dùng lại output đã có trong bin/.

.PARAMETER KeepLog
    Giữ file log của lần chạy Web để lại chỗ khác (mặc định xoá ở cuối).

.PARAMETER TimeoutSeconds
    Thời gian tối đa chờ Web lên và seed xong. Mặc định 180 giây.

.PARAMETER Port
    Cổng http dùng cho lần chạy Web để seed. Mặc định 5005 (trùng profile `http`).

.PARAMETER ConnectionString
    Chuỗi kết nối ghi đè appsettings. Script đặt nó vào biến môi trường `ConnectionStrings__Default`,
    nên cả EF tool lẫn Web đều dùng chuỗi này. Chỉ cần khi máy dùng database khác mặc định; tên
    database trong chuỗi cũng là tên script in ra trong thông báo.

.PARAMETER Force
    Không hỏi xác nhận trước khi drop database.

.EXAMPLE
    .\scripts\reset-demo.ps1 -WhatIf
    In ra việc sẽ làm, không đụng database, không dừng tiến trình nào.

.EXAMPLE
    .\scripts\reset-demo.ps1
    Reset + reseed rồi tắt Web.

.EXAMPLE
    .\scripts\reset-demo.ps1 -StartWeb
    Reset + reseed rồi để Web chạy cho buổi demo.

.NOTES
    Dev A · Sprint 4 (A4.10). Chạy từ bất kỳ thư mục nào — script tự tìm gốc repo theo vị trí của nó.
    Bỏ câu hỏi xác nhận bằng -Force (hoặc -Confirm:$false), xem trước bằng -WhatIf.
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [switch] $StartWeb,
    [switch] $SkipBuild,
    [switch] $KeepLog,
    [int]    $TimeoutSeconds = 180,
    [int]    $Port = 5005,
    [string] $ConnectionString,
    [switch] $Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Console mặc định của Windows không phải UTF-8; không đặt lại thì thông báo tiếng Việt bị vỡ.
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$webProject      = 'src/CinemaBooking.Web'
$infraProject    = 'src/CinemaBooking.Infrastructure'
$workerProcesses = @('CinemaBooking.Web', 'CinemaBooking.Worker')

function Write-Step {
    param([string] $Text)
    Write-Host ''
    Write-Host "==> $Text" -ForegroundColor Cyan
}

function Write-Info {
    param([string] $Text)
    Write-Host "    $Text"
}

function Write-Skip {
    param([string] $Text)
    Write-Host "    [WhatIf] sẽ làm: $Text" -ForegroundColor DarkYellow
}

function Stop-ProcessTree {
    param([int] $ProcessId)

    $children = Get-CimInstance -ClassName Win32_Process -Filter "ParentProcessId = $ProcessId" -ErrorAction SilentlyContinue
    foreach ($child in $children) { Stop-ProcessTree -ProcessId ([int]$child.ProcessId) }
    Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue
}

# ─────────────────────────────────────────────────────────────────────────────
# 0. Gốc repo + công cụ
# ─────────────────────────────────────────────────────────────────────────────
$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot 'CinemaBooking.Smart.sln'
if (-not (Test-Path -LiteralPath $solution)) {
    throw "Không tìm thấy $solution. Chạy script từ trong repo: .\scripts\reset-demo.ps1"
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'Không có `dotnet` trong PATH. Cài .NET SDK 9.0 trở lên.'
}

try {
    $null = & dotnet ef --version 2>&1
} catch {
    throw 'Thiếu công cụ `dotnet ef`. Cài bằng: dotnet tool install --global dotnet-ef'
}

$dbName = '(theo appsettings)'
if ($ConnectionString) {
    if ($ConnectionString -match 'Database\s*=\s*([^;]+)') { $dbName = $Matches[1].Trim() }
} else {
    $appSettings = Join-Path $repoRoot "$webProject/appsettings.json"
    if (Test-Path -LiteralPath $appSettings) {
        $json = Get-Content -LiteralPath $appSettings -Raw | ConvertFrom-Json
        # Đọc qua PSObject.Properties để không nổ dưới Set-StrictMode khi thiếu khoá.
        $cs = $null
        $hasSection = $json.PSObject.Properties['ConnectionStrings']
        $hasDefault = $hasSection -and $json.ConnectionStrings.PSObject.Properties['Default']
        if ($hasDefault) { $cs = $json.ConnectionStrings.Default }
        if ($cs -and $cs -match 'Database\s*=\s*([^;]+)') { $dbName = $Matches[1].Trim() }
    }
}

Write-Host ''
Write-Host 'reset-demo — đưa database demo về trạng thái seed' -ForegroundColor White
Write-Info "Gốc repo  : $repoRoot"
Write-Info "Database  : $dbName"
Write-Info "Web       : http://localhost:$Port"
if ($WhatIfPreference) { Write-Info 'Chế độ    : -WhatIf (không thay đổi gì)' }

if (-not $Force -and -not $WhatIfPreference) {
    if ([Console]::IsInputRedirected) {
        throw 'Chạy không tương tác nên không hỏi xác nhận được. Thêm -Force để chạy thật, hoặc -WhatIf để xem trước.'
    }
    $answer = Read-Host "Drop database '$dbName' của máy này rồi seed lại? (y/N)"
    if ($answer -notmatch '^(y|yes)$') {
        Write-Host 'Đã hủy, chưa đụng gì.' -ForegroundColor Yellow
        return
    }
}

# ─────────────────────────────────────────────────────────────────────────────
# 1. Dừng Web/Worker đang chạy
# ─────────────────────────────────────────────────────────────────────────────
Write-Step '1/4 · Dừng Web/Worker đang chạy'

$running = @(Get-Process -Name $workerProcesses -ErrorAction SilentlyContinue)
if ($running.Count -eq 0) {
    Write-Info 'Không có tiến trình nào đang chạy.'
} else {
    foreach ($process in $running) {
        $description = "dừng $($process.ProcessName) (PID $($process.Id))"
        if ($PSCmdlet.ShouldProcess($process.ProcessName, 'Dừng tiến trình')) {
            Stop-ProcessTree -ProcessId $process.Id
            Write-Info "Đã dừng $($process.ProcessName) (PID $($process.Id))."
        } else {
            Write-Skip $description
        }
    }
}

# ─────────────────────────────────────────────────────────────────────────────
# 2. Drop database
# ─────────────────────────────────────────────────────────────────────────────
Write-Step "2/4 · Drop database '$dbName'"

$dropArgs = @(
    'ef', 'database', 'drop', '--force',
    '--project', $infraProject,
    '--startup-project', $webProject,
    '--context', 'CinemaDbContext'
)

if ($PSCmdlet.ShouldProcess($dbName, 'Drop database')) {
    # `dotnet ef database drop` không có tham số --connection: chuỗi kết nối riêng phải đi qua
    # biến môi trường đúng tên khoá cấu hình để appsettings đọc được.
    if ($ConnectionString) { $env:ConnectionStrings__Default = $ConnectionString }

    Push-Location $repoRoot
    try {
        & dotnet @dropArgs
        if ($LASTEXITCODE -ne 0) {
            throw "``dotnet ef database drop`` trả mã $LASTEXITCODE. Xem thông báo ngay trên để biết database còn bị giữ bởi tiến trình nào."
        }
    } finally {
        Pop-Location
    }
    Write-Info 'Đã drop database.'
} else {
    Write-Skip "dotnet $($dropArgs -join ' ')"
}

# ─────────────────────────────────────────────────────────────────────────────
# 3. Build
# ─────────────────────────────────────────────────────────────────────────────
Write-Step '3/4 · Build'

if ($SkipBuild) {
    $webDll = Join-Path $repoRoot "$webProject/bin/Debug/net9.0/CinemaBooking.Web.dll"
    if (-not (Test-Path -LiteralPath $webDll)) {
        throw "Không thấy $webDll — chưa build lần nào. Bỏ -SkipBuild, hoặc chạy `dotnet build` trước."
    }
    Write-Info 'Bỏ qua (-SkipBuild), dùng bản build có sẵn.'
} elseif ($PSCmdlet.ShouldProcess('CinemaBooking.Smart.sln', 'Build')) {
    Push-Location $repoRoot
    try {
        & dotnet build $solution --nologo
        if ($LASTEXITCODE -ne 0) { throw "`dotnet build` trả mã $LASTEXITCODE." }
    } finally {
        Pop-Location
    }
    Write-Info 'Build xong.'
} else {
    Write-Skip "dotnet build $solution"
}

# ─────────────────────────────────────────────────────────────────────────────
# 4. Chạy Web một lần để migrate + seed
# ─────────────────────────────────────────────────────────────────────────────
Write-Step '4/4 · Migrate + seed (chạy Web một lần)'

$url          = "http://localhost:$Port"
$logFile      = Join-Path ([System.IO.Path]::GetTempPath()) ("cinema-reset-demo-{0}.log" -f ([guid]::NewGuid().ToString('N').Substring(0, 8)))
$errorLogFile = "$logFile.err"
$webProcess   = $null
$seedOk       = $false
$movieCards   = 0

function Start-SeedRun {
    $runArgs = @(
        'run', '--project', $webProject,
        '--configuration', 'Debug',
        '--no-launch-profile',
        '--urls', $url
    )
    if ($SkipBuild) { $runArgs += '--no-build' }
    if ($ConnectionString) {
        $env:ConnectionStrings__Default = $ConnectionString
    }
    # Không dùng launchSettings nên phải tự đặt môi trường Development cho khớp lúc dev.
    $env:ASPNETCORE_ENVIRONMENT = 'Development'

    return Start-Process -FilePath 'dotnet' -ArgumentList $runArgs -WorkingDirectory $repoRoot `
        -RedirectStandardOutput $logFile -RedirectStandardError $errorLogFile `
        -PassThru -WindowStyle Hidden
}

if ($PSCmdlet.ShouldProcess($url, 'Chạy Web để migrate + seed')) {
    $webProcess = Start-SeedRun
    Write-Info "Web đang khởi động (PID $($webProcess.Id)), log: $logFile"

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 750

        if ($webProcess.HasExited) {
            Write-Host ''
            Write-Host "Web thoát trước khi lên được (mã $($webProcess.ExitCode)). 40 dòng cuối của log:" -ForegroundColor Red
            Get-Content -LiteralPath $logFile -Tail 40 -ErrorAction SilentlyContinue
            break
        }

        try {
            $response = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 10
        } catch {
            continue
        }

        if ($response.StatusCode -ne 200) { continue }

        $movieCards = ([regex]::Matches($response.Content, 'movie-title')).Count
        if ($movieCards -gt 0) { $seedOk = $true }
        break
    }

    if ($seedOk) {
        Write-Info "Trang chủ đã trả về $movieCards thẻ phim — seed xong."
    } elseif (-not $webProcess.HasExited) {
        Write-Host ''
        Write-Host "Hết $TimeoutSeconds giây mà trang chủ chưa trả về phim nào. Kiểm tra log: $logFile" -ForegroundColor Yellow
        Get-Content -LiteralPath $logFile -Tail 20 -ErrorAction SilentlyContinue
    }
} else {
    Write-Skip "dotnet run --project $webProject --no-launch-profile --urls $url   # áp migration + seed"
}

if ($StartWeb -and $null -ne $webProcess -and -not $webProcess.HasExited) {
    Write-Info "Giữ Web chạy tiếp theo yêu cầu -StartWeb (PID $($webProcess.Id))."
    $webProcess = $null   # không tắt ở bước dọn dẹp
} elseif ($null -ne $webProcess -and -not $webProcess.HasExited) {
    if ($PSCmdlet.ShouldProcess('Web', 'Tắt tiến trình chạy để seed')) {
        Stop-ProcessTree -ProcessId $webProcess.Id
        Write-Info 'Đã tắt Web.'
    }
}

# ─────────────────────────────────────────────────────────────────────────────
# Dọn dẹp + nhắc việc kế tiếp
# ─────────────────────────────────────────────────────────────────────────────
if (-not $KeepLog) {
    Remove-Item -LiteralPath $logFile, $errorLogFile -ErrorAction SilentlyContinue
} else {
    Write-Info "Log giữ lại: $logFile"
}

Remove-Item Env:\ASPNETCORE_ENVIRONMENT -ErrorAction SilentlyContinue
Remove-Item Env:\ConnectionStrings__Default -ErrorAction SilentlyContinue

Write-Step 'Xong'
if ($WhatIfPreference) {
    Write-Info 'Đây là -WhatIf: chưa có gì được thực hiện. Bỏ -WhatIf để chạy thật.'
    return
}

Write-Info 'Việc kế tiếp:'
Write-Info "  dotnet run --project $webProject      # http://localhost:$Port"
Write-Info '  dotnet run --project src/CinemaBooking.Worker   # email xác nhận + báo cáo ngày (cửa sổ khác)'
Write-Info 'Tài khoản admin: admin@cinemabooking.local / Admin@12345'
Write-Info 'Hướng dẫn demo: documentation/user-guide.md · checklist ảnh: documentation/screenshots/CHECKLIST.md'

if (-not $seedOk) { exit 1 }
