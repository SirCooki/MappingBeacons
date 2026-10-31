# Stores your Thunderstore and Hexium upload tokens on this PC, encrypted for your Windows account (DPAPI), in
# %APPDATA%\SirCookieMods\. Run once per PC:  .\set-upload-tokens.ps1
# Paste each token when asked (it isn't shown). Press Enter without pasting to keep the token that is already stored.
# The tokens never go into this folder or to GitHub; publish.ps1 reads them from here.

$dir = Join-Path $env:APPDATA 'SirCookieMods'
New-Item -ItemType Directory -Force $dir | Out-Null

foreach ($site in 'Thunderstore', 'Hexium') {
    $file = Join-Path $dir ("{0}_token.txt" -f $site.ToLower())
    $have = if (Test-Path $file) { ' (Enter = keep the stored one)' } else { '' }
    $secure = Read-Host "$site upload token$have" -AsSecureString
    if ($secure.Length -eq 0) { Write-Host "  $site unchanged"; continue }
    $secure | ConvertFrom-SecureString | Set-Content $file
    Write-Host "  $site token saved to $file" -ForegroundColor Green
}
