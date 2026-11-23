# ScreenQuill installer: downloads the latest release from GitHub and installs it for the current user.
# No administrator rights needed. Run with:
#   powershell -NoProfile -ExecutionPolicy Bypass -Command "irm https://raw.githubusercontent.com/Fleekshot/ScreenQuill/main/installer/install.ps1 | iex"

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # Invoke-WebRequest is far slower with the progress bar on
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$Repo       = 'Fleekshot/ScreenQuill'
$AssetName  = 'ScreenQuill-win-x64.zip'
$InstallDir = Join-Path $env:LOCALAPPDATA 'Programs\ScreenQuill'
$Exe        = Join-Path $InstallDir 'ScreenQuill.exe'
$Shortcut   = Join-Path ([Environment]::GetFolderPath('Programs')) 'ScreenQuill.lnk'
$UninstKey  = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\ScreenQuill'

Write-Host 'ScreenQuill installer' -ForegroundColor Cyan

# 1. Find the latest release and its zip asset.
$release = Invoke-RestMethod "https://api.github.com/repos/$Repo/releases/latest" -Headers @{ 'User-Agent' = 'ScreenQuill-Installer' }
$asset = $release.assets | Where-Object name -eq $AssetName | Select-Object -First 1
if (-not $asset) { throw "Release $($release.tag_name) has no $AssetName asset." }
$hashAsset = $release.assets | Where-Object name -eq "$AssetName.sha256" | Select-Object -First 1
Write-Host "Downloading $($release.tag_name) ($([math]::Round($asset.size / 1MB, 1)) MB)..."

$tmp = Join-Path ([IO.Path]::GetTempPath()) ("ScreenQuill-setup-" + [guid]::NewGuid())
New-Item -ItemType Directory $tmp | Out-Null
try {
    $zip = Join-Path $tmp $AssetName
    Invoke-WebRequest $asset.browser_download_url -OutFile $zip -UseBasicParsing

    # 2. Verify the checksum published alongside the zip.
    if ($hashAsset) {
        $expected = ((Invoke-WebRequest $hashAsset.browser_download_url -UseBasicParsing).Content -split '\s+')[0].Trim()
        $actual = (Get-FileHash $zip -Algorithm SHA256).Hash
        if ($expected -ne $actual) { throw "Checksum mismatch (expected $expected, got $actual). Download aborted." }
        Write-Host 'Checksum verified.'
    }

    # 3. Close a running copy, then replace the install folder.
    Get-Process ScreenQuill -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500
    $extract = Join-Path $tmp 'files'
    Expand-Archive $zip -DestinationPath $extract
    if (Test-Path $InstallDir) { Remove-Item $InstallDir -Recurse -Force }
    New-Item -ItemType Directory $InstallDir -Force | Out-Null
    Copy-Item (Join-Path $extract '*') $InstallDir -Recurse -Force
}
finally {
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}

# 4. Start Menu shortcut.
$shell = New-Object -ComObject WScript.Shell
$lnk = $shell.CreateShortcut($Shortcut)
$lnk.TargetPath = $Exe
$lnk.WorkingDirectory = $InstallDir
$lnk.Description = 'ScreenQuill screen annotation'
$lnk.Save()

# 5. Uninstaller, registered so ScreenQuill appears in Settings > Apps.
$uninstall = @'
$ErrorActionPreference = 'SilentlyContinue'
Get-Process ScreenQuill | Stop-Process -Force
Start-Sleep -Milliseconds 500
Remove-Item (Join-Path ([Environment]::GetFolderPath('Programs')) 'ScreenQuill.lnk') -Force
Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name ScreenQuill
Remove-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\ScreenQuill' -Recurse -Force
Set-Location $env:TEMP
Remove-Item (Join-Path $env:LOCALAPPDATA 'Programs\ScreenQuill') -Recurse -Force
Write-Host 'ScreenQuill was uninstalled. Your settings remain in %APPDATA%\ScreenQuill.'
'@
Set-Content (Join-Path $InstallDir 'uninstall.ps1') $uninstall -Encoding UTF8

New-Item $UninstKey -Force | Out-Null
$version = $release.tag_name.TrimStart('v')
$props = @{
    DisplayName     = 'ScreenQuill'
    DisplayVersion  = $version
    Publisher       = 'ScreenQuill'
    DisplayIcon     = $Exe
    InstallLocation = $InstallDir
    UninstallString = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$InstallDir\uninstall.ps1`""
    NoModify        = 1
    NoRepair        = 1
}
foreach ($k in $props.Keys) {
    $type = if ($props[$k] -is [int]) { 'DWord' } else { 'String' }
    New-ItemProperty $UninstKey -Name $k -Value $props[$k] -PropertyType $type -Force | Out-Null
}

Write-Host "Installed ScreenQuill $version to $InstallDir" -ForegroundColor Green
Start-Process $Exe
