# BuildRelease.ps1
# Builds the next version, packs it with Velopack and publishes it as a GitHub release.
#   .\BuildRelease.ps1             build, pack and upload
#   .\BuildRelease.ps1 -LocalOnly  build and pack into .\Releases only
#
# Requires the vpk tool, matching the Velopack package version in the csproj:
#   dotnet tool install -g vpk --version 1.2.161
# and, unless -LocalOnly, the GitHub CLI logged in with write access to the repo (gh auth login).
param([switch]$LocalOnly)

Set-Location -Path $PSScriptRoot
$ErrorActionPreference = 'Stop'

$AppName = "ClipboardCollector"
$RepoUrl = "https://github.com/kreaton/ClipboardCollector"
$PublishDir = ".\publish"
$ReleasesDir = ".\Releases"

if (-not $LocalOnly) {
    # Fetch the latest published release so the version continues from it and vpk can build a delta
    $token = gh auth token
    if ($LASTEXITCODE -ne 0) { throw "Not logged in to GitHub CLI (run: gh auth login)" }
    vpk download github --repoUrl $RepoUrl --token $token -o $ReleasesDir
    if ($LASTEXITCODE -ne 0) { throw "vpk download failed" }
}

# Find the highest version already packed (releases.win.json is written by vpk)
$releasesFile = Join-Path $ReleasesDir "releases.win.json"
if (Test-Path $releasesFile) {
    $largestVersion = (Get-Content -Path $releasesFile -Raw | ConvertFrom-Json).Assets
        | Where-Object { $_.Type -eq 'Full' }
        | ForEach-Object { [version]$_.Version }
        | Sort-Object -Descending
        | Select-Object -First 1
    # Increment the patch number by 1
    $newVersion = "{0}.{1}.{2}" -f $largestVersion.Major, $largestVersion.Minor, ($largestVersion.Build + 1)
}
else {
    $newVersion = "1.0.0"
}

Write-Host "Publishing $AppName version $newVersion (framework-dependent, win-x64)"
if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
dotnet publish -c Release -r win-x64 --self-contained false -p:Version=$newVersion -o $PublishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Write-Host "Packing $AppName version $newVersion with Velopack"
# --framework makes Setup.exe install the .NET 10 Desktop Runtime if it is missing
vpk pack -u $AppName -v $newVersion -p $PublishDir -e "$AppName.exe" -o $ReleasesDir --icon .\app.ico --framework net10.0-x64-desktop
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed" }

if (-not $LocalOnly) {
    Write-Host "Uploading $AppName version $newVersion to GitHub"
    vpk upload github --repoUrl $RepoUrl --token $token -o $ReleasesDir --publish --tag "v$newVersion" --releaseName "$AppName $newVersion"
    if ($LASTEXITCODE -ne 0) { throw "vpk upload failed" }
}

Write-Host "Built $AppName version $newVersion"
