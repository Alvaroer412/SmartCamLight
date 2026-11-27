# Builds SmartCamLight.
#
#   .\build.ps1                    bin\SmartCamLight.exe        (AnyCPU, C# compiler that ships with Windows)
#   .\build.ps1 -Platform arm64    bin\SmartCamLight-arm64.exe  (native Windows on ARM)
#   .\build.ps1 -Platform x64      bin\SmartCamLight-x64.exe
#
# Every variant runs on the .NET Framework that is part of Windows, so there is no runtime to install.
# The AnyCPU build needs nothing but Windows; on ARM devices Windows runs it under x64 emulation.
# The platform-specific builds need the Roslyn compiler from the .NET SDK or Visual Studio, because
# the compiler that ships with Windows cannot target ARM64.
param(
    [ValidateSet('anycpu', 'arm64', 'x64')]
    [string]$Platform = 'anycpu'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src  = Join-Path $root 'src'
$bin  = Join-Path $root 'bin'
$net  = Join-Path $env:WINDIR 'Microsoft.NET'

# Returns the command line that starts Roslyn's csc: a single exe, or dotnet plus csc.dll.
function Find-Roslyn {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) {
        $vs = & $vswhere -latest -products * -property installationPath | Select-Object -First 1
        if ($vs) {
            $csc = Join-Path $vs 'MSBuild\Current\Bin\Roslyn\csc.exe'
            if (Test-Path $csc) { return @($csc) }
        }
    }
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($dotnet) {
        $sdks = Join-Path (Split-Path $dotnet.Source) 'sdk'
        $csc = Get-ChildItem $sdks -Directory -ErrorAction SilentlyContinue |
            Sort-Object { [version]($_.Name -replace '-.*$', '') } -Descending |
            ForEach-Object { Join-Path $_.FullName 'Roslyn\bincore\csc.dll' } |
            Where-Object { Test-Path $_ } | Select-Object -First 1
        if ($csc) { return @($dotnet.Source, $csc) }
    }
    throw "A $Platform build needs the Roslyn C# compiler. Install the .NET SDK (https://dot.net) or Visual Studio, or run .\build.ps1 without -Platform for the AnyCPU build."
}

# Folder with the .NET Framework assemblies to compile against.
function Find-References {
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8.1'),
        (Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'),
        (Join-Path $net 'FrameworkArm64\v4.0.30319'),
        (Join-Path $net 'Framework64\v4.0.30319')
    )
    if ($Platform -eq 'x64') { [array]::Reverse($candidates) }
    foreach ($dir in $candidates) {
        if (Test-Path (Join-Path $dir 'mscorlib.dll')) { return $dir }
    }
    throw 'No .NET Framework 4.8 assemblies found to compile against.'
}

New-Item -ItemType Directory -Force $bin | Out-Null
$files = (Get-ChildItem $src -Filter *.cs).FullName
$ico = Join-Path $src 'app.ico'
$libs = 'mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll'

if ($Platform -eq 'anycpu') {
    $out = Join-Path $bin 'SmartCamLight.exe'
    $compiler = @(Join-Path $net 'Framework64\v4.0.30319\csc.exe')
    $options = @('/platform:anycpu') + ($libs | Select-Object -Skip 1 | ForEach-Object { "/r:$_" })
} else {
    $out = Join-Path $bin "SmartCamLight-$Platform.exe"
    $compiler = @(Find-Roslyn)
    $refs = Find-References
    # /langversion:5 keeps the sources buildable with the compiler that ships with Windows.
    $options = @("/platform:$Platform", '/langversion:5', '/noconfig', '/nostdlib+', '/nowarn:8012') +
        ($libs | ForEach-Object { '/r:' + (Join-Path $refs $_) })
}

$exe = $compiler[0]
$pre = @($compiler | Select-Object -Skip 1)
$common = @('/nologo', '/target:winexe', '/optimize+', "/win32manifest:$src\app.manifest") + $options

if (-not (Test-Path $ico)) {
    # First pass without an icon, then let the app draw its own icon file.
    & $exe @pre @common "/out:$out" $files
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    & $out --write-icon $ico | Out-Null
}

& $exe @pre @common "/win32icon:$ico" "/out:$out" $files
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Write-Host "Built $out"
