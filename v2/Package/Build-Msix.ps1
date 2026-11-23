# Builds the MSIX package for Sync Two Files.
#   -Mode Test  : signs with a self-signed certificate (for installing on your own PC).
#   -Mode Store : leaves it unsigned and creates the .msixupload for Partner Center (Microsoft signs it).
# For the Store, pass the identity from Partner Center > Product identity.
param(
    [ValidateSet('Test', 'Store')][string]$Mode = 'Test',
    [string]$IdentityName = 'Stem.SynctwoFile',
    [string]$Publisher = 'CN=CCECE44A-7E72-4252-ACEA-59D29765B443',
    [string]$PublisherDisplayName = 'Stem',
    [string]$OutDir = (Join-Path $PSScriptRoot '..\dist')
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
if ($Mode -eq 'Store') { $OutDir = Join-Path $OutDir 'store' }
New-Item -ItemType Directory -Force $OutDir | Out-Null
$OutDir = Resolve-Path $OutDir

function Find-SdkTool([string]$name) {
    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $tool = Get-ChildItem $kits -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^\d+\.' } | Sort-Object { [version]$_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName "x64\$name" } | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $tool) { throw "$name not found. Install the Windows SDK: winget install Microsoft.WindowsSDK.10.0.26100" }
    $tool
}
function Invoke-Tool([string]$exe, [string[]]$arguments) {
    & $exe @arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "$(Split-Path $exe -Leaf) failed ($LASTEXITCODE)" }
}

$makeappx = Find-SdkTool 'makeappx.exe'
$makepri = Find-SdkTool 'makepri.exe'
$signtool = Find-SdkTool 'signtool.exe'

$props = [xml](Get-Content (Join-Path $root 'Directory.Build.props') -Raw)
$version = "$($props.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1).0"
Write-Host "Sync Two Files $version ($Mode)"

$work = Join-Path $root 'obj\msix'
$layout = Join-Path $work 'layout'
$pri = Join-Path $work 'pri'
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory -Force $layout, $pri | Out-Null

Write-Host 'Publishing...'
Invoke-Tool 'dotnet' @('publish', (Join-Path $root 'SyncTwoFiles.App\SyncTwoFiles.App.csproj'),
    '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:DebugType=none', '-o', $layout, '-v', 'q', '-nologo')

Write-Host 'Logos and manifest...'
& (Join-Path $PSScriptRoot 'Make-Logos.ps1') -OutDir (Join-Path $pri 'Assets') | Out-Null
$manifest = (Get-Content (Join-Path $PSScriptRoot 'AppxManifest.xml') -Raw).
    Replace('$IdentityName$', $IdentityName).
    Replace('$Publisher$', [Security.SecurityElement]::Escape($Publisher)).
    Replace('$PublisherDisplayName$', [Security.SecurityElement]::Escape($PublisherDisplayName)).
    Replace('$Version$', $version)
[IO.File]::WriteAllText((Join-Path $pri 'AppxManifest.xml'), $manifest, (New-Object Text.UTF8Encoding $false))

Push-Location $pri
try {
    Invoke-Tool $makepri @('createconfig', '/cf', 'priconfig.xml', '/dq', 'en-US', '/pv', '10.0.0', '/o')
    # A single package (no bundle), so every scale goes into one resources.pri.
    $cfg = [xml](Get-Content 'priconfig.xml' -Raw)
    $cfg.SelectNodes('//packaging') | ForEach-Object { [void]$_.ParentNode.RemoveChild($_) }
    $cfg.Save((Join-Path $pri 'priconfig.xml'))
    Invoke-Tool $makepri @('new', '/pr', $pri, '/cf', 'priconfig.xml', '/mn', 'AppxManifest.xml', '/of', 'resources.pri', '/o')
}
finally { Pop-Location }
Copy-Item (Join-Path $pri 'Assets') $layout -Recurse
Copy-Item (Join-Path $pri 'AppxManifest.xml'), (Join-Path $pri 'resources.pri') $layout

$msix = Join-Path $OutDir "SyncTwoFiles_${version}_x64.msix"
Write-Host 'Packing...'
Invoke-Tool $makeappx @('pack', '/d', $layout, '/p', $msix, '/o')

if ($Mode -eq 'Test') {
    $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -ceq $Publisher -and $_.FriendlyName -eq 'Sync Two Files (test)' -and $_.NotAfter -gt (Get-Date) -and $_.HasPrivateKey } | Select-Object -First 1
    if (-not $cert) {
        $cert = New-SelfSignedCertificate -Type Custom -Subject $Publisher -KeyUsage DigitalSignature `
            -FriendlyName 'Sync Two Files (test)' -CertStoreLocation Cert:\CurrentUser\My `
            -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
    }
    $cer = Join-Path $OutDir 'SyncTwoFiles-test.cer'
    Export-Certificate -Cert $cert -FilePath $cer | Out-Null
    Invoke-Tool $signtool @('sign', '/fd', 'SHA256', '/sha1', $cert.Thumbprint, $msix)
    Write-Host ''
    Write-Host "Signed: $msix"
    Write-Host 'To install it on this PC, trust the certificate once (as administrator):'
    Write-Host "  Import-Certificate -FilePath `"$cer`" -CertStoreLocation Cert:\LocalMachine\TrustedPeople"
    Write-Host "  Add-AppxPackage `"$msix`""
}
else {
    $upload = Join-Path $OutDir "SyncTwoFiles_${version}_x64.msixupload"
    $zip = "$upload.zip"
    Compress-Archive -Path $msix -DestinationPath $zip -Force
    Move-Item $zip $upload -Force
    Write-Host ''
    Write-Host "For Partner Center: $upload"
}
