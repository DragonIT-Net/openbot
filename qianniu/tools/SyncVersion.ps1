# 版本号同步：以 installer/QianNiuBot.iss 里的 #define AppVersion 为唯一手动维护的版本号，
# 编译前自动把它换算写进 AssemblyInfo.cs（AssemblyVersion/AssemblyFileVersion）和
# StartUp/Params.cs（Version）。发布新版本时只需要改 QianNiuBot.iss 这一处。
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$issPath = Join-Path $repoRoot "installer\QianNiuBot.iss"
$assemblyInfoPath = Join-Path $repoRoot "src\Bot\Properties\AssemblyInfo.cs"
$paramsPath = Join-Path $repoRoot "src\Bot\StartUp\Params.cs"

$issContent = Get-Content -Path $issPath -Raw
$match = [regex]::Match($issContent, '#define AppVersion "(\d+)\.(\d+)\.(\d+)"')
if (-not $match.Success) {
    throw "在 $issPath 里没找到 #define AppVersion `"X.Y.Z`" 这一行，版本同步失败。"
}

$major = [int]$match.Groups[1].Value
$minor = [int]$match.Groups[2].Value
$patch = [int]$match.Groups[3].Value

if ($minor -gt 99 -or $patch -gt 99) {
    throw "QianNiuBot.iss 的 AppVersion=$major.$minor.$patch，次版本号/修订号必须在 0-99 之间（Params.Version 换算规则是 major*10000+minor*100+patch）。"
}

$paramsVersion = $major * 10000 + $minor * 100 + $patch
$assemblyVersion = "$major.$minor.$patch.0"

$assemblyInfoContent = Get-Content -Path $assemblyInfoPath -Raw
$assemblyInfoContent = [regex]::Replace($assemblyInfoContent, '\[assembly: AssemblyVersion\("[\d\.]+"\)\]', "[assembly: AssemblyVersion(`"$assemblyVersion`")]")
$assemblyInfoContent = [regex]::Replace($assemblyInfoContent, '\[assembly: AssemblyFileVersion\("[\d\.]+"\)\]', "[assembly: AssemblyFileVersion(`"$assemblyVersion`")]")
Set-Content -Path $assemblyInfoPath -Value $assemblyInfoContent -NoNewline -Encoding UTF8

$paramsContent = Get-Content -Path $paramsPath -Raw
$paramsContent = [regex]::Replace($paramsContent, 'public const int Version = \d+;', "public const int Version = $paramsVersion;")
Set-Content -Path $paramsPath -Value $paramsContent -NoNewline -Encoding UTF8

Write-Host "[版本同步] QianNiuBot.iss AppVersion=$major.$minor.$patch -> Params.Version=$paramsVersion, AssemblyVersion=$assemblyVersion"
