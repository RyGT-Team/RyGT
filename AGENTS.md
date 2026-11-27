# Notes for agents working on RyGTS

## Ticket IDs: RT for both games, GTS or GT7 for one

Every change carries a ticket ID of the form `<KIND>-<GAME>-<NNN>` in its commit subject and in
the code comments next to it.

| Part | Values |
|---|---|
| Kind | `FIX`, `PERF`, `DIAG`, `FEAT` |
| Game | `RT` when the change applies to both games (the runtime, the launcher, packaging, CI), `GTS` for GT Sport only, `GT7` for GT7 only |
| Number | three digits |

- **RT has its own count, started at 001 on 2026-11-17** (`FIX-RT-001`, `FEAT-RT-002`, ...).
  GTS and GT7 keep the counts they had; numbers are never reused within one tag. The renderer
  (shadGT) uses `SGT` for its shared work, with a count of its own.
- **Use RT for anything shared**: runtime modules, the unified launcher, switches, updater,
  packages, CI. Use GTS or GT7 only when the code is gated to one game (a game hook, a per-game
  default, one game's serial).
- **Before taking a number**, check it is free: `git log --all` subjects, `git grep` of the tree and
  the open and closed PRs' titles (`gh pr list --state all`). Another contributor may have used
  the next number on a branch you have not fetched.
- **Old tickets keep their IDs.** Do not rename `FEAT-GTS-…` comments of changes that are already
  pushed; new work on them takes a new RT number and may name the old one.

## Switch names: SHADGT_, RYGT_ or SHARED_ (FEAT-GTS-275)

From FEAT-GTS-275 on, an environment switch is named after where it takes effect:

| Prefix | Read by | Example |
|---|---|---|
| `SHADGT_` | only the renderer (shadGT: `src/`, `ryty_gpu/`) | `SHADGT_PARALLEL_WORKERS` |
| `RYGT_` | only the runtime (RyGTS: `core/`, `app/`, `plugins/`) | `RYGT_LOG_DIRECTORY` |
| `SHARED_` | both the renderer and the runtime | `SHARED_DIAG` |

- **A new switch** gets the prefix of the code that reads it. Never add a new `RYGT_` switch to the
  renderer or a `SHADGT_` one to the runtime.
- **A switch that starts being read on the other side too** is renamed to `SHARED_` in every
  repository (renderer, runtime, launcher, docs), and added to `SHARED` in the renderer's
  `tools/switch_prefix.py`.
- **The rename is a hard one**: the old names are not read any more. At start the launchers list
  switches saved under an old name in their obsolete-settings question and rename them on Yes
  (`SwitchWalker.FindRenames`), so players keep their settings.
- **A branch from before the rename** brings old names back on merge. Run the tool there after
  merging; a second run changes nothing:

  ```
  python tools/switch_prefix.py --game gts --write-map switch-map.json .   # in shadGT
  python <shadGT>/tools/switch_prefix.py --apply-map switch-map.json <RyGTS>   # the launcher and runtime
  ```

  It renames exact switch names only (not macros such as `RYGT_SWITCH_EXPORT`), in code, comments
  and docs. `CHANGES.MD` and `CHANGELOG.md` keep the names of their time.

## Renderer switches on the launcher's Advanced page

The launcher (`loader/`) does not keep a list of the renderer's and runtime's `SHADGT_*`, `RYGT_*` and `SHARED_*` switches. `SwitchWalker`
in `loader/AdvancedPage.cs` reads `libs\libSceVideoOut.prx` (the copy the game loads) as a file (it never loads it) and lists every
`"SHADGT_..."`, `"RYGT_..."` and `"SHARED_..."` string the renderer passes to `getenv`. Descriptions come from the same DLL:
`"RYGTSW1|NAME|kind|group|default|values|description"` strings in shadGT's
`src/video_core/rygt_switches.cpp`. See `loader/ADVANCED-PAGE.md`.

- **Adding a renderer switch:** add its description line in shadGT's `src/video_core/rygt_switches.cpp`
  (format in shadGT's `AGENTS.md`). Do not add switch names, tooltips or defaults to the launcher.
- **Adding a switch read by the RyGTS runtime itself** (`core/`, `app/`, ...): the walker reads every
  `libs\*.prx`, the runtime modules included (ASCII and UTF-16 names, so `_wgetenv(L"RYGT_...")` is
  found), so it is listed. Describe it in shadGT's `src/video_core/rygt_switches.cpp` like a renderer
  switch (group `Runtime` or the one that fits); the walker takes descriptions from any of the files.
- **A variable the launcher sets itself** (`RYGT_LOG_DIRECTORY`, `RYGT_HDD_STREAMING`,
  `SHADGT_ANTI_LAG`, the render-scale ones in `loader/RenderResolution.cs`): add it to
  `SwitchWalker.LauncherManaged` so the page does not offer it.
- **Changing the format:** bump the `RYGTSW1` tag on both sides together. The walker ignores strings
  with a tag it does not know.

## Releases and the launcher's updater (FIX-GTS-286)

The launcher's updater (`loader/Updater.cs`) finds releases on GitHub by their names, so a release
has to be published like this:

| What | Convention | Example |
|---|---|---|
| Tag | `rygt-<YYYY-MM-DD>` (`gt-sport-<YYYY-MM-DD>` before), a letter for a same-day hotfix | `rygt-2026-11-23`, `rygt-2026-11-23b` |
| Package | `RyGT-<YYYY-MM-DD>.zip` (`GTSport-Ryty-shadGT-<YYYY-MM-DD>.zip` before) | `RyGT-2026-11-23.zip` |
| Checksum | the package's SHA-256 as an asset beside it: `<package>.sha256` | `RyGT-2026-11-23.zip.sha256` |
| Contents | the CI package as built: one top folder holding `BUILD_INFO.txt` (with `commit`) and `SHA256SUMS.txt` | `RyGTS-ci-eb155892/` |
| Pre-release | GitHub's "Set as a pre-release" box; the Prerelease channel offers it, Stable does not | |

- **The version is the tag without its prefix** (`2026-11-23`; FIX-RT-044 added `rygt-`). Versions are compared as text, so
  keep the date format, and add a letter for a hotfix rather than a different shape.
- **Don't rename the tag, the package or the checksum asset.** Without them the updater says no
  update is available (that was FIX-GTS-286: tags `gts-` and packages `RyGTS-*.zip` were expected).
  A new naming scheme means changing `TagPrefixes` and `PackagePrefixes` in `loader/Updater.cs`,
  in the same change, and keeping the old names in them for older launchers' sake.
- **The tag must point at the commit the package was built from** (its `BUILD_INFO.txt` commit).
  The updater reads the tag's commit to skip a release that is the same build as the installed
  one, for example the newest nightly.
- **Nightlies come in two parts (FEAT-RT-046):** a push to RyGT `development` runs `ci-package-release.yml`
  and uploads `runtime-<RyGT commit>`: everything but the renderer's files. shadGT's `ci-package.yml` runs
  the same workflow for each `ryty-gpu` push (with `renderer_sha`) and uploads `renderer-<shadGT commit>`:
  only `libs/libSceVideoOut.prx`, the renderer's runtime libraries, `tools/ryty-feel-test.exe`, the DLSS plugin
  and its licence, listed in `RENDERER-SHA256SUMS.txt` with `RENDERER-BUILD_INFO.txt` (so unpacking it over a
  folder keeps the folder's own lists). The updater installs the newest of each that differs from the folder's,
  in one update with one backup. A manual run of `ci-package-release.yml` (workflow_dispatch) still packages
  everything (`ci-<RyGT commit>-renderer-<shadGT commit>`), for releases; the diagnostics package stays complete.
  The installed renderer's commit is read from the renderer itself (its `RYGTREV1` string). Keep these names.
- **A release installs its own launcher.** Publish one whose `RyGTLauncher.exe` has the updater you want
  players to keep.

## Launcher build

`loader\build.cmd [output folder]` builds `RyGTLauncher.exe` (RyGT Launcher, for both games) with the .NET Framework 4 `csc`, which only
accepts **C# 5**: no local functions, `out var`, tuples or string interpolation. Add new `.cs`
files to the list in `build.cmd` and to `SOURCES` in `loader\test-launcher.cmd`, which builds the
launcher and runs its checks (`loader\test-launcher.cmd <output folder>`).

The launcher is for both GT Sport and GT7 (FEAT-GTS-290, `loader/UNIFIED-LAUNCHER.md`): what both games
share is in `RyGTLoader.cs`, what only one has in `GtsPages.cs` or `Gt7Pages.cs`. Each game keeps its own
settings file (`<serial>.RyGT.ini`) and launch environment, and every name it writes in a folder follows its serial,
so both games may share one folder (FEAT-GTS-291); change one game's launch only in its own file.

## Launcher version

The launcher is versioned on its own: `LauncherVersion.Value` in `loader/Version.cs`, with a
matching section in `loader/CHANGELOG.md`.

- **Bump only the patch number:** 0.1.0, 0.1.1, 0.1.2, and so on. Launcher changes count as minor
  updates, new features and new looks included. Do not raise the minor or major number unless the
  maintainer says so.
- Change `Version.cs` and the changelog's newest `## x.y.z - date` heading together:
  `ci/tests/test_launcher_version.py` fails when they disagree.
