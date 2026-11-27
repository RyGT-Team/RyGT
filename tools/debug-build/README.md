# Developer Debug build (FEAT-RT-040, shadGT FEAT-SGT-196)

A Debug renderer and runtime for us, installed into a local GT Sport/GT7 test folder for a debugger.
It is not for players: it needs the debug CRT, which only a Visual Studio install has, and it is slow.

| Part | Build | Symbols | Debugger |
|---|---|---|---|
| Renderer `libs\libSceVideoOut.prx` | shadGT preset `x64-Clang-Debug-RyGT`: `/Od /Ob0 /Zi /Oy-`, debug CRT `/MDd` (`_DEBUG`, `DEBUG_ASSERT`, STL checked iterators, debug heap), no `NDEBUG` | `libs\libSceVideoOut.pdb`, built with it | x64dbg, Visual Studio, WinDbg |
| Renderer, light | preset `x64-Clang-Debug-RyGT-Light`: as above at `/O1 /Ob1` | the same | the same; some locals optimized out |
| Runtime `libs\*.prx` | MinGW GCC, `CMAKE_BUILD_TYPE=Debug` (`-g`, `-O0`), asserts on | DWARF inside each `.prx` | gdb (x64dbg and VS do not read DWARF) |

The launcher, the MinGW DLLs and the renderer repository's other libraries (`libSceAjm`, `libSceNgs2`, ...)
stay as they are in the folder.

## Build and install

Close the game first. Run from a RyGT checkout. `-Renderer` is a shadGT checkout with its submodules.

```powershell
$env:LLVM_ROOT = 'C:\path\to\clang+llvm-23.1.3-x86_64-pc-windows-msvc'
tools\debug-build\Install-DebugBuild.ps1 -GameFolder <test folder> -Renderer <shadGT checkout> -LogLevel Debug
```

- `-Light` builds the `/O1` renderer, for when the `/Od` one is too slow to reach a race.
- `-SkipRuntime` or `-SkipRenderer` installs one half. `-NoBuild` installs what was built last.
  `-BuildOnly` builds and touches no folder.
- `-LogLevel Debug` sets `stdout_level=` in `CUSA03220.RyGT.ini` and `CUSA24769.RyGT.ini`. This is the same
  setting as the launcher's About > Logging > "stdout.log minimum level", where Trace and Debug can also be
  picked by hand.
- The runtime toolchain defaults to `..\tools\runtime-toolchain` beside the checkout (or `RYGT_RUNTIME_TOOLS`),
  and is installed there from `ci/runtime-toolchain.json` if missing.

The first install copies every file it replaces to `libs\.rygt-debug-backup\`. Later installs keep that first
backup, so you can rebuild and reinstall as often as you like.

## Put the normal build back

```powershell
tools\debug-build\Install-DebugBuild.ps1 -GameFolder <test folder> -Restore
```

This copies the backed-up files back, deletes the files the install added, sets `stdout_level` back, and
removes the backup folder.

## Debugging

- **x64dbg or Visual Studio:** start the game as the launcher does, then attach to `GTSport.exe` or `GT7.exe`.
  Alternatively, start it under the debugger with the launcher's environment. The renderer's PDB sits beside the
  `.prx`, so symbols load by themselves. In VS, use Debug > Attach to Process with Native code.
- **gdb, for the runtime:** `gdb -p <pid>` from the MinGW toolchain's `bin`. The `.prx` files carry their own DWARF.
- **Guest faults:** the runtime handles write faults on purpose, so set the debugger to pass first-chance
  access violations to the game (x64dbg: Options > Preferences > Exceptions).
- `/Od` frames are much larger, so stack overflows on small guest-thread stacks can show up here first.
