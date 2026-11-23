<#
.SYNOPSIS
  PIURiseCustom 릴리즈 패키징·게시 (게임이 설치된 PC에서 실행 — 빌드에 게임 interop DLL이 필요해 클라우드/CI에서는 불가).

.DESCRIPTION
  1) 버전 일치 확인: mod\PIURiseCustom\Mod.cs 의 Version == CHANGELOG.md 최상단 "## [버전]"
  2) Release 빌드 (경고를 오류로 취급, 게임 폴더 배포 안 함)
  3) dist\PIURiseCustom-v<버전>.zip (DLL·CHANGELOG·README) + SHA256 + 릴리즈 노트 초안(dist\release-notes-v<버전>.md)
  -Publish: 위 + main 브랜치·깨끗한 작업 트리·origin/main과 같은지 확인 → 태그 v<버전> 푸시 → gh release create

.EXAMPLE
  .\tools\release.ps1
  .\tools\release.ps1 -Publish
  .\tools\release.ps1 -GamePath "D:\SteamLibrary\steamapps\common\PUMP IT UP RISE"
#>
param(
    [switch]$Publish,
    [string]$GamePath = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Fail($msg) { Write-Host "[release] $msg" -ForegroundColor Red; exit 1 }

# 1. 버전
$modCs = Get-Content "mod\PIURiseCustom\Mod.cs" -Raw -Encoding UTF8
if ($modCs -notmatch 'public const string Version = "([0-9]+\.[0-9]+\.[0-9]+)"') { Fail "Mod.cs에서 Version을 찾지 못했습니다." }
$version = $Matches[1]

$changelog = Get-Content "CHANGELOG.md" -Raw -Encoding UTF8
if ($changelog -notmatch '(?m)^## \[([0-9]+\.[0-9]+\.[0-9]+)\]') { Fail "CHANGELOG.md에서 '## [버전]' 항목을 찾지 못했습니다." }
$clVersion = $Matches[1]
if ($clVersion -ne $version) { Fail "버전 불일치: Mod.cs=$version, CHANGELOG.md 최상단=$clVersion" }
$tag = "v$version"
Write-Host "[release] 버전 $version"

# 같은 태그가 이미 있으면 중단 (버전을 먼저 올릴 것)
git fetch origin --tags --quiet
if (git tag --list $tag) { Fail "태그 $tag 가 이미 있습니다. Mod.cs와 CHANGELOG.md의 버전을 먼저 올리세요." }

if ($Publish) {
    $branch = (git rev-parse --abbrev-ref HEAD).Trim()
    if ($branch -ne "main") { Fail "-Publish는 main 브랜치에서만 실행합니다(현재 $branch)." }
    if (git status --porcelain) { Fail "작업 트리가 깨끗하지 않습니다. 커밋하거나 되돌린 뒤 다시 실행하세요." }
    git fetch origin main --quiet
    $local = (git rev-parse HEAD).Trim(); $remote = (git rev-parse origin/main).Trim()
    if ($local -ne $remote) { Fail "main이 origin/main과 다릅니다(pull 또는 push 먼저)." }
}

# 2. 빌드
$buildArgs = @("build", "mod\PIURiseCustom\PIURiseCustom.csproj", "-c", "Release", "-p:Deploy=false", "-warnaserror")
if ($GamePath) { $buildArgs += "-p:GamePath=$GamePath" }
& dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { Fail "빌드 실패 (경고도 오류로 취급합니다)." }

$dll = "mod\PIURiseCustom\bin\Release\PIURiseCustom.dll"
if (-not (Test-Path $dll)) { Fail "빌드 산출물이 없습니다: $dll" }

# 3. 패키지
New-Item -ItemType Directory -Force -Path "dist" | Out-Null
$zip = "dist\PIURiseCustom-$tag.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $dll, "CHANGELOG.md", "README.md" -DestinationPath $zip
$sha = (Get-FileHash $zip -Algorithm SHA256).Hash

# 검증 빌드는 SafePatcher.cs 상수에서 읽는다 (문서와 어긋나지 않게)
$sp = Get-Content "mod\PIURiseCustom\SafePatcher.cs" -Raw -Encoding UTF8
$buildId = if ($sp -match 'VerifiedSteamBuildId = "(\d+)"') { $Matches[1] } else { "?" }
$gameVer = if ($sp -match 'VerifiedGameVersion = "([^"]+)"') { $Matches[1] } else { "?" }

# CHANGELOG 해당 항목 본문
$section = [regex]::Match($changelog, "(?ms)^## \[$([regex]::Escape($version))\][^\n]*\n(.*?)(?=^## \[|\z)").Groups[1].Value.Trim()

$notes = @"
$section

### 설치
PIURiseCustom.dll을 ``<게임>\Mods\``에 복사 (MelonLoader 0.7.3, 검증 빌드 Steam $buildId / 게임 $gameVer)

SHA256 (zip): $sha
"@
$notesPath = "dist\release-notes-$tag.md"
Set-Content -Path $notesPath -Value $notes -Encoding UTF8
Write-Host "[release] $zip"
Write-Host "[release] SHA256 $sha"
Write-Host "[release] 노트 초안 $notesPath"

if (-not $Publish) {
    Write-Host "[release] 패키지만 만들었습니다. 게시하려면 main에서 -Publish로 다시 실행하세요." -ForegroundColor Yellow
    exit 0
}

# 4. 게시
git tag -a $tag -m "PIURiseCustom $tag"
git push origin $tag
if ($LASTEXITCODE -ne 0) { Fail "태그 푸시 실패" }
gh release create $tag $zip --title "PIURiseCustom $tag" --notes-file $notesPath
if ($LASTEXITCODE -ne 0) { Fail "gh release create 실패 (태그 $tag 는 이미 푸시됨)" }
Write-Host "[release] 게시 완료: $tag" -ForegroundColor Green
