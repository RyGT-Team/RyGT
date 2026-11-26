RyGT: Gran Turismo Sport and Gran Turismo 7 as native Windows programs
{name} - github.com/RyGT-Team/RyGT

RyGT ports Gran Turismo Sport (CUSA03220, update 1.69) and Gran Turismo 7 (CUSA24769, update 1.71) from
PlayStation 4 to Windows. The games are not emulated: Set up converts each game's executable
into GTSport.exe or GT7.exe, the game code runs on your CPU, and RyGT's libraries stand in for
the PS4 system. Graphics go through the shadGT Vulkan renderer. One runtime, one renderer and
one launcher serve both games, and both can share this folder.

This is experimental software: crashes, rendering errors and shader compilation stutter
happen. Nothing in this archive comes from the games or a PS4: you provide your own decrypted
dump, firmware modules and fonts.

Quick start
  1. Extract this folder to a short, writable path such as C:\RyGT. Running from inside the
     zip, or from a deep folder, makes Set up fail on paths longer than Windows allows.
     For GT7, keep it on the SAME NTFS DRIVE as the GT7 dump: Set up links GT7's files into
     app0 instead of copying them, and on another drive Windows Developer Mode must be on.
  2. Run RyGTLauncher.exe and pick the game at the top left.
  3. On its Game page, pick your folders:
       Game                    your decrypted base game folder (eboot.bin, sce_sys\param.sfo,
                               sce_module, ...): GT Sport CUSA03220 or GT7 CUSA24769
       Update                  your decrypted update folder. GT Sport needs update 1.69, GT7
                               update 1.71. For GT7, leave it empty when update 1.71 is
                               already merged into the game folder; a separate update overrides
                               the base game file by file
       System modules          a folder with your PS4 firmware's decrypted modules (.sprx or
                               .prx, searched in subfolders too)
       Fonts                   a folder with your PS4 firmware's font and font2 folders
  4. Press Set up. It converts the game for Windows (a minute or two). Its log is
     logs\<serial>\setup.log (CUSA03220 for GT Sport, CUSA24769 for GT7).
  5. Press Play. Logs are written to logs\<serial>.
  Do the same for the other game to play both from this folder. Keep the game and update
  folders in place after Set up: the games read them where they are.

Required game updates
  GT Sport: CUSA03220 with update 1.69.
  GT7: CUSA24769 with update 1.71 (separate, or merged into the game folder). RyGT patches
  the game's code at 1.71's addresses, so other updates fail to start or crash; Set up
  refuses them.
  Set up says "decrypt your game dump first" when a file in the dump is still encrypted.

One folder for both games
  Everything a game writes is named after its serial, so GT Sport (CUSA03220) and GT7
  (CUSA24769) never touch each other's files: the executable (GTSport.exe, GT7.exe), the
  launcher settings (<serial>.RyGT.ini), the game settings (user\custom_configs\<serial>.json),
  the saves (user\savedata\<serial>), shaders\<serial>*, logs\<serial>, and Set up's work in
  convert\<serial>. Shared on purpose: libs, converter, dlss, the controller bindings and
  RyGT-Launcher.ini (the game picked last).

State of this build ({date})
  The release notes on github.com/RyGT-Team/RyGT/releases list what changed in this build.
  Both games boot, run their menus and races, with audio, controllers and steering wheels.
  Known problems: shaders compile the first time the game needs them, so the first races on
  a new track or car can stutter or pause (later visits are smooth; a driver update starts
  over); occasional crashes and GPU device losses, more often on AMD GPUs.

Options (RyGTLauncher.exe)
  Each game has its own pages: Display, Graphics (upscaling and frame generation: AMD FSR,
  NVIDIA DLSS and others; HDR), Shaders (what happens while new shaders compile), Controls
  (controllers and steering wheels), Advanced (renderer switches, saved per game), and for
  GT7 also Account and Online. NVIDIA's DLSS files are not included: the Graphics page's
  download button fetches NVIDIA's own signed files. Settings > About > Updates updates this
  folder for both games.

Crash reports
  Please attach logs\<serial>\stdout.log and stderr.log from the session that failed, and
  setup.log for Set up problems. If the game stops with a lost GPU device, turn on
  SHADGT_DEVICE_FAULT (set it to 1) on the launcher's Advanced page for that game and play
  until it happens again: stdout.log then ends with DIAG-243 lines (the driver's fault report
  and the last draws). Report issues at github.com/RyGT-Team/RyGT/issues.

Offline mode patch for GT7 (included)
  GT7's Used Cars and Legendary Cars dealerships need the community "Offline mode" patch
  (Proper Offline Patch v1.0, ps-patch-system XML for CUSA24769). This release includes
  patches\CUSA24769.xml: Set up automatically applies the file's Offline mode group, and no
  other group, to the converted GT7.exe. The dealerships' catalog is then written to
  data\gt7offline. Set up only applies it to the verified dump's executable and stops
  otherwise. Source and credits are in patches\README.md. Car thumbnails in the dealerships
  stay grey.

Photos (GT7)
  GT7 keeps Scapes and race photos in its own format. To get a picture you can open, pick the
  photo in the game's gallery and choose Export: it is written as a JPEG to the Photos folder
  next to RyGTLauncher.exe (About > Folders in the launcher opens it).

Files and licenses
  RyGTLauncher.exe is the launcher, converter\ryty.exe the converter, libs\ the runtime
  libraries including the shadGT renderer (libSceVideoOut.prx); the GCC runtime DLLs and AMD
  FidelityFX (amd_fidelityfx_vk.dll) are redistributed with their notices. GPL version 2
  applies to RyGT and shadGT: LICENSE and the licenses folder hold the notices of everything
  shipped, including the statically linked open-source dependencies. manifest.json lists
  every file with its SHA-256 and the source commits this build came from.
  Source: github.com/RyGT-Team/RyGT and github.com/RyGT-Team/shadGT (branch ryty-gpu).

  Gran Turismo is a trademark of Sony Interactive Entertainment. This project is independent
  of Sony and Polyphony Digital. No game data, firmware, fonts, saves or shader caches are
  included.
