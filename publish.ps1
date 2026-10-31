# Mapping Beacons - build the Thunderstore package and upload it to Thunderstore and Hexium.
# Run from this folder:  .\publish.ps1      (deploy.ps1 runs it at the end; -NoUpload only builds the zip)
#   1. builds the Release DLL
#   2. builds package\build\SirCookie-MappingBeacons-<version>.zip with tcli (manifest, icon, README, CHANGELOG, DLL)
#   3. uploads it to Thunderstore, then to Hexium (each one on its own: a failure on one doesn't stop the other)
# The version comes from Plugin.cs (DLL and package versions are always equal). Add a CHANGELOG entry first.
# Tokens: stored encrypted by set-upload-tokens.ps1 in %APPDATA%\SirCookieMods\ (never in this folder).

param([switch] $NoUpload, [switch] $NoBuild)

$ErrorActionPreference = 'Stop'
$Root    = $PSScriptRoot
$Package = Join-Path $Root 'package'
$TokenDir = Join-Path $env:APPDATA 'SirCookieMods'
$Tcli    = Join-Path $env:USERPROFILE '.dotnet\tools\tcli.exe'
$env:DOTNET_ROLL_FORWARD = 'Major' # tcli targets an older .NET; this PC has .NET 8
$Sites = [ordered]@{ Thunderstore = 'https://thunderstore.io'; Hexium = 'https://valheim.hexium.gg' }

function Step($text) { Write-Host "`n== $text" -ForegroundColor Cyan }

function Get-Token([string] $site) {
    $file = Join-Path $TokenDir ("{0}_token.txt" -f $site.ToLower())
    if (-not (Test-Path $file)) { return $null }
    $secure = Get-Content $file | ConvertTo-SecureString
    return [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
}

if (-not (Test-Path $Tcli)) { throw 'Thunderstore CLI missing: run  dotnet tool install -g tcli' }

$pluginCs = Get-Content (Join-Path $Root 'Plugin.cs') -Raw
if ($pluginCs -notmatch 'PluginVersion\s*=\s*"([^"]+)"') { throw 'PluginVersion not found in Plugin.cs' }
$Version = $Matches[1]
Write-Host "Mapping Beacons $Version" -ForegroundColor Green
if (-not (Select-String -Path (Join-Path $Package 'CHANGELOG.md') -SimpleMatch "## $Version" -Quiet)) {
    throw "package\CHANGELOG.md has no '## $Version' entry yet"
}

if (-not $NoBuild) {
    Step 'Building (Release)'
    Push-Location $Root
    try { dotnet build -c Release --nologo -v q '-clp:ErrorsOnly'; if ($LASTEXITCODE -ne 0) { throw 'Build failed (is the game still running?)' } }
    finally { Pop-Location }
}

Step 'Building the package'
Push-Location $Package
try {
    & $Tcli build --package-version $Version
    if ($LASTEXITCODE -ne 0) { throw 'tcli build failed' }
}
finally { Pop-Location }
$toml = Get-Content (Join-Path $Package 'thunderstore.toml') -Raw
if ($toml -notmatch '(?m)^namespace\s*=\s*"([^"]+)"') { throw 'namespace not found in package\thunderstore.toml' }
$Zip = Join-Path $Package "build\$($Matches[1])-MappingBeacons-$Version.zip"
if (-not (Test-Path $Zip)) { throw "Package missing: $Zip" }
Write-Host "Package: $Zip"
if ($NoUpload) { return }

$failed = @()
foreach ($site in $Sites.Keys) {
    Step "Uploading to $site"
    $token = Get-Token $site
    if (-not $token) { Write-Host "No $site token stored: run .\set-upload-tokens.ps1" -ForegroundColor Yellow; $failed += $site; continue }
    Push-Location $Package
    try {
        & $Tcli publish --file $Zip --token $token --repository $Sites[$site] # version is inside the zip
        if ($LASTEXITCODE -ne 0) { $failed += $site; Write-Host "$site upload failed" -ForegroundColor Red }
        else { Write-Host "Uploaded $Version to $site" -ForegroundColor Green }
    }
    finally { Pop-Location; $token = $null }
}
if ($failed) { Write-Host "`nNot uploaded to: $($failed -join ', ')" -ForegroundColor Yellow }
