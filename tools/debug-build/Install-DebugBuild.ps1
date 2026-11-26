<#
.SYNOPSIS
FEAT-RT-040: build a developer Debug renderer (shadGT) and runtime (this repository) and install them
into a GT Sport/GT7 test folder, or put the folder's own files back.

.DESCRIPTION
Renderer: shadGT's CMake preset x64-Clang-Debug-RyGT (FEAT-SGT-196). It uses /Od /Ob0 /Zi /Oy-, the debug CRT
(/MDd, so _DEBUG, DEBUG_ASSERT and the STL's checked iterators are on), and a matching libSceVideoOut.pdb.
-Light builds x64-Clang-Debug-RyGT-Light instead (/O1 /Ob1) for when /Od is too slow to reach a race.
Runtime: libs\*.prx built with the pinned MinGW GCC at -O0 -g (CMAKE_BUILD_TYPE=Debug). Its symbols are
DWARF inside each .prx: use gdb for them. x64dbg and Visual Studio read the renderer's PDB only.

Install backs up every file it replaces to <folder>\libs\.rygt-debug-backup\ (once: a second install keeps
the first backup) and lists what it added. -Restore puts all of that back and deletes the backup.
-LogLevel sets stdout_level= in both games' <serial>.RyGT.ini (the launcher's About > Logging setting);
-Restore sets it back.

.EXAMPLE
tools\debug-build\Install-DebugBuild.ps1 -GameFolder D:\GTSport-test -Renderer D:\shadGT -LogLevel Debug
.EXAMPLE
tools\debug-build\Install-DebugBuild.ps1 -GameFolder D:\GTSport-test -Restore
#>
param(
    [Parameter(Mandatory = $true)][string]$GameFolder,
    [string]$Renderer,
    [string]$Runtime,
    [string]$LlvmRoot = $env:LLVM_ROOT,
    [string]$Toolchain = $env:RYGT_RUNTIME_TOOLS,
    [switch]$Light,
    [switch]$SkipRenderer,
    [switch]$SkipRuntime,
    [switch]$NoBuild,
    [switch]$BuildOnly,
    [ValidateSet('', 'Trace', 'Debug', 'Info', 'Warning', 'Error')][string]$LogLevel = '',
    [switch]$Restore
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-Checked([string]$Program, [string[]]$Arguments) {
    & $Program @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Program failed with exit code $LASTEXITCODE" }
}

# Windows PowerShell 5.1 has no $PSScriptRoot in parameter defaults.
if (!$Runtime) { $Runtime = Split-Path (Split-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) -Parent) -Parent }
$GameFolder = (Resolve-Path $GameFolder).Path
$libs = Join-Path $GameFolder 'libs'
if (!(Test-Path (Join-Path $libs 'libSceVideoOut.prx'))) { throw "$GameFolder has no libs\libSceVideoOut.prx: not a RyGT folder" }
$backup = Join-Path $libs '.rygt-debug-backup'
$state = Join-Path $backup 'state.json'
$serials = @('CUSA03220', 'CUSA24769')  # GT Sport, GT7: <serial>.RyGT.ini (FEAT-GTS-291)

function Set-StdoutLevel([string]$Ini, [string]$Level) {
    $lines = @()
    if (Test-Path $Ini) { $lines = @(Get-Content -LiteralPath $Ini) }
    $found = $false
    $lines = @($lines | ForEach-Object {
        if ($_ -match '^\s*stdout_level\s*=') { $found = $true; if ($Level) { "stdout_level=$Level" } } else { $_ }
    })
    if (!$found -and $Level) { $lines += "stdout_level=$Level" }
    Set-Content -LiteralPath $Ini -Value $lines -Encoding ASCII
}

function Get-StdoutLevel([string]$Ini) {
    if (!(Test-Path $Ini)) { return $null }
    foreach ($line in Get-Content -LiteralPath $Ini) {
        if ($line -match '^\s*stdout_level\s*=\s*(.*?)\s*$') { return $Matches[1] }
    }
    return ''
}

if ($Restore) {
    if (!(Test-Path $state)) { throw "Nothing to restore: $state not found" }
    $saved = Get-Content -Raw $state | ConvertFrom-Json
    foreach ($name in @($saved.replaced)) { Copy-Item -LiteralPath (Join-Path $backup $name) (Join-Path $libs $name) -Force }
    foreach ($name in @($saved.added)) { Remove-Item -LiteralPath (Join-Path $libs $name) -Force -ErrorAction SilentlyContinue }
    foreach ($entry in @($saved.log_levels)) {
        $ini = Join-Path $GameFolder "$($entry.serial).RyGT.ini"
        if ($null -eq $entry.level) { continue }              # no ini before: leave the launcher's
        Set-StdoutLevel $ini $entry.level                      # '' removes the line again
    }
    Remove-Item -LiteralPath $backup -Recurse -Force
    Write-Host "Restored $(@($saved.replaced).Count) files, removed $(@($saved.added).Count) added files in $libs"
    return
}

# Visual Studio's developer shell: the MSVC libraries for the renderer, and CMake and Ninja for both builds.
function Enter-DevShell {
    if ($env:VCINSTALLDIR) { return }
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if ($LASTEXITCODE -ne 0 -or !$vs) { throw 'Visual Studio x64 C++ tools not found' }
    Import-Module (Join-Path $vs 'Common7/Tools/Microsoft.VisualStudio.DevShell.dll')
    Enter-VsDevShell -VsInstallPath $vs -SkipAutomaticLocation -DevCmdArguments '-arch=x64 -host_arch=x64'
}

# FIX-GTS-262: these come from the renderer repository in a package; the runtime's own versions are
# smaller stand-ins, so a runtime install must not replace them.
$rendererLibraries = @('libSceAjm.prx', 'libSceNgs2.prx', 'libSceAudio3d.prx', 'libSceZlib.prx',
                       'libSceDiscMap.prx', 'libSceScreenShot.prx', 'libSceNpAuth.prx', 'libSceVideoOut.prx')
$install = [ordered]@{}   # file name in libs\ -> built file

if (!$SkipRenderer) {
    if (!$Renderer) { throw 'Pass -Renderer <shadGT checkout> (or -SkipRenderer)' }
    $Renderer = (Resolve-Path $Renderer).Path
    $preset = if ($Light) { 'x64-Clang-Debug-RyGT-Light' } else { 'x64-Clang-Debug-RyGT' }
    $build = Join-Path $Renderer "Build/$preset"
    if (!$NoBuild) {
        if (!$LlvmRoot) { throw 'Set LLVM_ROOT (or -LlvmRoot) to the LLVM 23.1.3 distribution' }
        $llvm = (Resolve-Path $LlvmRoot).Path
        Enter-DevShell
        $env:PATH = "$llvm\bin;$env:PATH"
        $builtins = Get-ChildItem "$llvm/lib" -Recurse -Filter clang_rt.builtins-x86_64.lib | Select-Object -First 1
        if ($builtins) { $env:LIB = "$($builtins.DirectoryName);$env:LIB" }
        Push-Location $Renderer
        try {
            Invoke-Checked 'cmake' @('--preset', $preset)
            Invoke-Checked 'cmake' @('--build', $build, '--target', 'ryty_gpu')
        } finally { Pop-Location }
    }
    # The debug CRT is not redistributable; Visual Studio puts it in System32 (debug_nonredist otherwise).
    $debugCrt = @('ucrtbased.dll', 'vcruntime140d.dll', 'vcruntime140_1d.dll', 'msvcp140d.dll', 'msvcp140d_atomic_wait.dll')
    $missing = @($debugCrt | Where-Object { !(Test-Path (Join-Path $env:SystemRoot "System32\$_")) })
    if ($missing) { Write-Warning "Debug CRT not in System32: $($missing -join ', '). Install Visual Studio's C++ tools, or copy them from VC\Redist\MSVC\*\debug_nonredist\x64 beside the game exe." }
    foreach ($file in 'libSceVideoOut.prx', 'libSceVideoOut.pdb') {
        $built = @(Get-ChildItem $build -Recurse -Filter $file -File)
        if ($built.Count -ne 1) { throw "Expected one $file in $build (found $($built.Count))" }
        $install[$file] = $built[0].FullName
    }
}

if (!$SkipRuntime) {
    $runtimeBuild = Join-Path $Runtime 'build/debug-runtime'
    if (!$NoBuild) {
        if (!$Toolchain) { $Toolchain = Join-Path (Split-Path $Runtime -Parent) 'tools/runtime-toolchain' }
        if (!(Get-Command cmake -ErrorAction SilentlyContinue)) { Enter-DevShell }
        Push-Location $Runtime
        try {
            $gcc = & python ci/runtime_toolchain.py install $Toolchain | Select-Object -Last 1
            if ($LASTEXITCODE -or !$gcc -or !(Test-Path "$gcc/g++.exe")) { throw 'Pinned runtime toolchain unavailable' }
            $ffmpeg = & python ci/runtime_toolchain.py ffmpeg $Toolchain | Select-Object -Last 1
            if ($LASTEXITCODE -or !$ffmpeg) { throw 'Pinned FFmpeg package unavailable' }
            $env:PATH = "$gcc;$env:PATH"
            $submodules = @('LibAtrac9','SDL2','SPIRV-Headers','Vulkan-Headers','ffmpeg-core','freetype','glslang','stb') | ForEach-Object { "3rdparty/$_" }
            # A local clone may point its submodules at another local checkout (file transport).
            Invoke-Checked 'git' (@('-c', 'protocol.file.allow=always', 'submodule', 'update', '--init', '--depth', '1', '--') + $submodules)
            # CMake's Debug: -g and no -O, so -O0. NDEBUG stays undefined: assert() is on.
            Invoke-Checked 'cmake' @('-S', '.', '-B', $runtimeBuild, '-G', 'Ninja', '-DCMAKE_BUILD_TYPE=Debug', '-DBUILD_TESTING=OFF',
                '-DRTY_BUILD_GUI=OFF', '-DRTY_ENABLE_LTO=OFF', "-DCMAKE_C_COMPILER=$gcc/gcc.exe",
                "-DCMAKE_CXX_COMPILER=$gcc/g++.exe", "-DFFMPEG_PREBUILT_DIR=$ffmpeg")
            Invoke-Checked 'cmake' @('--build', $runtimeBuild, '--target', 'libs')
        } finally { Pop-Location }
    }
    $runtimeLibs = @(Get-ChildItem (Join-Path $runtimeBuild 'core/libs/libs') -Filter *.prx -File |
        Where-Object { $rendererLibraries -notcontains $_.Name })
    if (!($runtimeLibs | Where-Object Name -eq 'libkernel.prx')) { throw "No runtime build in $runtimeBuild" }
    foreach ($library in $runtimeLibs) { $install[$library.Name] = $library.FullName }
}

if ($BuildOnly) { $install.GetEnumerator() | ForEach-Object { Write-Host "Built $($_.Key): $($_.Value)" }; return }
if ($install.Count -eq 0 -and !$LogLevel) { throw 'Nothing to install' }

# Back up once: a second install over a Debug one keeps the folder's original files.
$saved = if (Test-Path $state) { Get-Content -Raw $state | ConvertFrom-Json } else { $null }
$replaced = [System.Collections.Generic.List[string]]::new()
$added = [System.Collections.Generic.List[string]]::new()
$levels = @()
if ($saved) {
    @($saved.replaced) | ForEach-Object { $replaced.Add($_) }
    @($saved.added) | ForEach-Object { $added.Add($_) }
    $levels = @($saved.log_levels)
}
New-Item -ItemType Directory -Force $backup | Out-Null
foreach ($name in $install.Keys) {
    $target = Join-Path $libs $name
    if ($replaced.Contains($name) -or $added.Contains($name)) { }
    elseif (Test-Path $target) { Copy-Item -LiteralPath $target (Join-Path $backup $name); $replaced.Add($name) }
    else { $added.Add($name) }
    Copy-Item -LiteralPath $install[$name] $target -Force
}
if ($LogLevel) {
    foreach ($serial in $serials) {
        $ini = Join-Path $GameFolder "$serial.RyGT.ini"
        if (!($levels | Where-Object { $_.serial -eq $serial })) { $levels += [pscustomobject]@{ serial = $serial; level = (Get-StdoutLevel $ini) } }
        if (Test-Path $ini) { Set-StdoutLevel $ini $LogLevel }
    }
}
[pscustomobject]@{
    schema = 1; renderer = if ($SkipRenderer) { $null } else { $Renderer }; light = [bool]$Light
    runtime = if ($SkipRuntime) { $null } else { $Runtime }; replaced = @($replaced); added = @($added); log_levels = @($levels)
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $state -Encoding UTF8
Write-Host "Installed $($install.Count) Debug files into $libs (backup: $backup)."
Write-Host "Put the folder's own files back with: $PSCommandPath -GameFolder `"$GameFolder`" -Restore"
