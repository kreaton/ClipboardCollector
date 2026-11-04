# BuildRelease.ps1
# Builds the next version, packs it with Velopack and publishes it as a GitHub release.
# GitHub Actions runs this on every push to main (.github/workflows/release.yml).
#   .\BuildRelease.ps1             build, pack and upload
#   .\BuildRelease.ps1 -LocalOnly  build and pack into .\Releases only
#
# Requires the vpk tool, matching the Velopack package version in the csproj:
#   dotnet tool install -g vpk --version 1.2.161
# and, unless -LocalOnly, a GitHub token: $env:GH_TOKEN in CI, otherwise the GitHub CLI (gh auth login).
param([switch]$LocalOnly)

Set-Location -Path $PSScriptRoot
$ErrorActionPreference = 'Stop'

$AppName = "ClipboardCollector"
$RepoUrl = "https://github.com/kreaton/ClipboardCollector"
$PublishDir = ".\publish"
$ReleasesDir = ".\Releases"

if (-not $LocalOnly) {
    # Fetch the latest published release so the version continues from it and vpk can build a delta
    $token = if ($env:GH_TOKEN) { $env:GH_TOKEN } else { gh auth token }
    if (-not $token) { throw "No GitHub token (set GH_TOKEN or run: gh auth login)" }
    vpk download github --repoUrl $RepoUrl --token $token -o $ReleasesDir
    if ($LASTEXITCODE -ne 0) { throw "vpk download failed" }
}

# Major.Minor comes from Directory.Build.props; the patch is one higher than the last release
# with that Major.Minor, or 0 for the first one. It is read from the full package's file name
# because vpk download only fetches the package: a clean Releases folder has no releases.win.json.
$majorMinor = ([xml](Get-Content -Path "..\Directory.Build.props" -Raw)).Project.PropertyGroup.MajorMinor
$buildNumber = 0
$last = Get-ChildItem -Path $ReleasesDir -Filter "$AppName-*-full.nupkg" -ErrorAction SilentlyContinue
    | ForEach-Object { [version]($_.Name -replace "^$AppName-|-full\.nupkg$") }
    | Where-Object { "$($_.Major).$($_.Minor)" -eq $majorMinor }
    | Sort-Object -Descending
    | Select-Object -First 1
if ($last) { $buildNumber = $last.Build + 1 }
$newVersion = "$majorMinor.$buildNumber"

Write-Host "Publishing $AppName version $newVersion (framework-dependent, win-x64)"
if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
dotnet publish -c Release -r win-x64 --self-contained false -p:BuildNumber=$buildNumber -o $PublishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Write-Host "Packing $AppName version $newVersion with Velopack"
# --framework makes Setup.exe install the .NET 10 Desktop Runtime if it is missing
vpk pack -u $AppName -v $newVersion -p $PublishDir -e "$AppName.exe" -o $ReleasesDir --icon .\app.ico --runtime win-x64 --framework net10.0-x64-desktop
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed" }

if (-not $LocalOnly) {
    Write-Host "Uploading $AppName version $newVersion to GitHub"
    # In CI, tag the commit that was built rather than whatever the branch points at by now
    $commitish = if ($env:GITHUB_SHA) { @("--targetCommitish", $env:GITHUB_SHA) } else { @() }
    vpk upload github --repoUrl $RepoUrl --token $token -o $ReleasesDir --publish --tag "v$newVersion" --releaseName "$AppName $newVersion" @commitish
    if ($LASTEXITCODE -ne 0) { throw "vpk upload failed" }
}

Write-Host "Built $AppName version $newVersion"
