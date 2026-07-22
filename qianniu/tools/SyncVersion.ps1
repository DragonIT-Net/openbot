$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$issPath = Join-Path $repoRoot 'installer\QianNiuBot.iss'
$assemblyInfoPath = Join-Path $repoRoot 'src\Bot\Properties\AssemblyInfo.cs'
$paramsPath = Join-Path $repoRoot 'src\Bot\StartUp\Params.cs'

$issContent = Get-Content -LiteralPath $issPath -Raw
$match = [regex]::Match($issContent, '#define AppVersion "(\d+)\.(\d+)\.(\d+)"')
if (-not $match.Success) {
    throw "AppVersion X.Y.Z was not found in $issPath."
}

$major = [int]$match.Groups[1].Value
$minor = [int]$match.Groups[2].Value
$patch = [int]$match.Groups[3].Value
if ($minor -gt 99 -or $patch -gt 99) {
    throw "AppVersion $major.$minor.$patch requires minor and patch values between 0 and 99."
}

$paramsVersion = $major * 10000 + $minor * 100 + $patch
$assemblyVersion = "$major.$minor.$patch.0"

$assemblyInfoContent = Get-Content -LiteralPath $assemblyInfoPath -Raw
$assemblyInfoContent = [regex]::Replace($assemblyInfoContent, '\[assembly: AssemblyVersion\("[\d\.]+"\)\]', "[assembly: AssemblyVersion(`"$assemblyVersion`")]")
$assemblyInfoContent = [regex]::Replace($assemblyInfoContent, '\[assembly: AssemblyFileVersion\("[\d\.]+"\)\]', "[assembly: AssemblyFileVersion(`"$assemblyVersion`")]")
Set-Content -LiteralPath $assemblyInfoPath -Value $assemblyInfoContent -NoNewline -Encoding UTF8

$paramsContent = Get-Content -LiteralPath $paramsPath -Raw
$paramsContent = [regex]::Replace($paramsContent, 'public const int Version = \d+;', "public const int Version = $paramsVersion;")
Set-Content -LiteralPath $paramsPath -Value $paramsContent -NoNewline -Encoding UTF8

Write-Host "Version synchronized: $major.$minor.$patch -> Params.Version=$paramsVersion, AssemblyVersion=$assemblyVersion"
