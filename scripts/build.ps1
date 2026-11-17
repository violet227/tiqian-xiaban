# 打包提钱下班，产物在 dist\：
#   .\scripts\build.ps1                 轻量版：单个 exe（约 0.5 MB），需要 .NET 8 桌面运行时
#   .\scripts\build.ps1 -SelfContained  独立版：单个 exe（约 60 MB），自带运行时，任何 Windows 10/11 直接运行
# 需要 .NET 8 SDK。独立版第一次打包要从 NuGet 下载运行时包；公司网络需要先设置 $env:HTTPS_PROXY。
param([switch]$SelfContained)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null

# 不让 SDK 在后台联网检查 workload 更新（没代理时会一直挂着）
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

$name = if ($SelfContained) { 'TiqianXiaban-standalone' } else { 'TiqianXiaban' }
$out = Join-Path $root "src\bin\publish\$name"
$args = @(
    'publish', (Join-Path $root 'src\TiqianXiaban.csproj'),
    '-c', 'Release', '-r', 'win-x64', '-o', $out, '-nologo',
    '--disable-build-servers', '-nr:false', '-p:UseSharedCompilation=false',
    '-p:PublishSingleFile=true', '-p:DebugType=none'
)
if ($SelfContained) {
    $args += @('--self-contained', 'true', '-p:EnableCompressionInSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true')
} else {
    $args += @('--self-contained', 'false')
}
& dotnet @args
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

$exe = Join-Path $dist "提钱下班$(if ($SelfContained) { '-独立版' }).exe"
try {
    Copy-Item (Join-Path $out 'TiqianXiaban.exe') $exe -Force
} catch {
    # 旧版本正在运行，exe 被占用：运行中的 exe 可以改名，先挪开再放新的，退出旧版后可删掉 .old
    $old = "$exe.old"
    Remove-Item $old -Force -ErrorAction SilentlyContinue
    if (Test-Path $old) { $old = "$exe.$(Get-Date -Format yyyyMMddHHmmss).old" }
    Rename-Item $exe (Split-Path -Leaf $old)
    Copy-Item (Join-Path $out 'TiqianXiaban.exe') $exe -Force
    Write-Host "正在运行的旧版本已改名为 $(Split-Path -Leaf $old)，退出旧版后可以删掉它"
}
Get-Item $exe | Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 2) } }
