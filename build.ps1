# Builds SmartCamLight.exe with the C# compiler that ships with Windows (.NET Framework 4.8).
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src  = Join-Path $root 'src'
$bin  = Join-Path $root 'bin'
$csc  = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Force $bin | Out-Null

$files = (Get-ChildItem $src -Filter *.cs).FullName
$common = @('/nologo', '/target:winexe', '/optimize+', '/platform:anycpu',
            '/r:System.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll',
            "/win32manifest:$src\app.manifest")

$ico = Join-Path $src 'app.ico'
if (-not (Test-Path $ico)) {
    # First pass without an icon, then let the app draw its own icon file.
    & $csc @common "/out:$bin\SmartCamLight.exe" $files
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    & "$bin\SmartCamLight.exe" --write-icon $ico | Out-Null
}

& $csc @common "/win32icon:$ico" "/out:$bin\SmartCamLight.exe" $files
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Write-Host "Built $bin\SmartCamLight.exe"
