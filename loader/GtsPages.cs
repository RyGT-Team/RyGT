// FEAT-GTS-290: what only Gran Turismo Sport has in the unified launcher: its Display, Graphics and
// Controls pages, its Set up (offline livery scripts, shader packs), its updates (GitHub RyGT-Team/RyGT)
// and how it starts GTSport.exe, all as RyGTS's own launcher had them.
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace RyGT
{
    partial class LoaderForm
    {
        /// GT Sport's Display, Graphics and Controls pages (the Game, Shaders, About and Advanced pages are
        /// both games'; RyGTLoader.cs).
        void BuildGtsPages(TableLayoutPanel graphicsPage, TableLayoutPanel pro, TableLayoutPanel upscaling, TableLayoutPanel frameGen)
        {
            // Graphics.
            var graphics = Section(graphicsPage, "Graphics");
            AddExperimentalRenderScale(graphics);
            // Sparks, tire smoke and other effects are always on; an older launcher could have
            // turned them off in the config.
            if (!ReadSetting("GPU", "particle_effects", true))
            {
                try { WriteSetting("GPU", "particle_effects", true); } catch { }
            }
            // AMD FSR scales the game's 1920x1080 picture to the window or screen size: FSR 3.1 through
            // amd_fidelityfx_vk.dll (next to GTSport.exe, part of the release), or shadGT's FSR 1 pass.
            // AMD: FSR upscaling (FSR 3.1 through amd_fidelityfx_vk.dll, or shadGT's FSR 1) and
            // FSR 3.1 frame generation (any GPU; independent of the upscaling choice).
            var fsr = AddOption(graphics, "Upscale with AMD FSR or Intel XeSS", "GPU", "fsr_enabled", false);
            bool fsr3Installed = File.Exists(Path.Combine(root, "amd_fidelityfx_vk.dll"));
            var fsr3 = AddOption(graphics, fsr3Installed
                ? "Use the newest FSR (off: FSR 1)"
                : "Use the newest FSR (needs amd_fidelityfx_vk.dll)", "GPU", "fsr3_enabled", true);
            var rcas = AddOption(graphics, "FSR sharpening (RCAS)", "GPU", "rcas_enabled", true);
            var framegen = AddOption(graphics, fsr3Installed
                ? "FSR frame generation: about 2x the frames (any GPU)"
                : "FSR frame generation (needs amd_fidelityfx_vk.dll)", "GPU", "framegen_enabled", false);
            // FSR-200: FSR reconstructs the race scene from the game's depth, motion vectors and
            // camera jitter (the default renderer's temporal scene capture, like temporal DLSS)
            // instead of scaling the finished picture.
            var fsrTemporal = AddOption(graphics, "Temporal upscaling: replaces the game's anti-aliasing",
                "GPU", "fsr_temporal", true);
            // FSR-201: the upscaler: the FSR version inside amd_fidelityfx_vk.dll (FSR 4 is DirectX 12
            // SDK 2.4.0 Vulkan DLLs; without them the renderer uses FSR 3.1), or
            // XESS-200: Intel XeSS through libxess.dll next to GTSport.exe (temporal only; from Intel's
            // XeSS SDK), on any GPU with the Vulkan features it needs.
            bool xessInstalled = File.Exists(Path.Combine(root, "libxess.dll"));
            var versionRow = new FlowLayoutPanel { AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(22, 2, 0, 2), WrapContents = false };
            var versionLabel = new Label { Text = "Upscaler", AutoSize = false, Width = 174, Height = 20, BackColor = Color.Transparent, Margin = new Padding(0, 6, 8, 0) };
            var version = new DarkComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, FlatStyle = FlatStyle.Flat };
            var versionValues = new[] { "fsr3", "fsr4", "xess" };
            version.Items.AddRange(new object[] { "AMD FSR 3", "AMD FSR 4",
                xessInstalled ? "Intel XeSS" : "Intel XeSS (needs libxess.dll)" });
            version.SelectedIndex = Math.Max(0, Array.IndexOf(versionValues, ReadStringSetting("GPU", "fsr_upscaler", "fsr3")));
            version.SelectedIndexChanged += delegate
            {
                try { WriteSetting("GPU", "fsr_upscaler", versionValues[version.SelectedIndex]); }
                catch (Exception e) { MessageBox.Show(this, "Could not save the setting: " + e.Message); }
            };
            versionRow.Controls.Add(versionLabel);
            versionRow.Controls.Add(version);
            graphics.Controls.Add(versionRow);
            // XESS-201: fetches Intel's official SDK on request; only the upscaler and its licence
            // are kept. Intel's licence allows redistributing it unmodified.
            var xessDownload = new Button { Text = xessInstalled ? "Reinstall XeSS" : "Download XeSS (77 MB)", AutoSize = true, Margin = new Padding(22, 2, 0, 4) };
            xessDownload.Click += delegate
            {
                if (MessageBox.Show(this, "Download Intel XeSS SDK 3.0.2 from github.com/intel/xess and install libxess.dll next to GTSport.exe? It uses Intel's Simplified Software License.",
                                    "Intel XeSS", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
                xessDownload.Enabled = false;
                xessDownload.Text = "Downloading...";
                new System.Threading.Thread(delegate ()
                {
                    string error = null;
                    string zip = Path.Combine(Path.GetTempPath(), "RyGT-XeSS-3.0.2.zip");
                    try
                    {
                        System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
                        using (var web = new System.Net.WebClient())
                            web.DownloadFile("https://github.com/intel/xess/releases/download/v3.0.2/XeSS_SDK_3.0.2.zip", zip);
                        using (var archive = System.IO.Compression.ZipFile.OpenRead(zip))
                        {
                            System.IO.Compression.ZipFileExtensions.ExtractToFile(archive.GetEntry("bin/libxess.dll"), Path.Combine(root, "libxess.dll"), true);
                            System.IO.Compression.ZipFileExtensions.ExtractToFile(archive.GetEntry("LICENSE.txt"), Path.Combine(root, "libxess-LICENSE.txt"), true);
                            System.IO.Compression.ZipFileExtensions.ExtractToFile(archive.GetEntry("third-party-programs.txt"), Path.Combine(root, "libxess-third-party-programs.txt"), true);
                        }
                    }
                    catch (Exception e) { error = e.Message; }
                    try { File.Delete(zip); } catch { }
                    BeginInvoke((Action)delegate
                    {
                        xessDownload.Enabled = true;
                        if (error != null)
                        {
                            xessDownload.Text = "Download XeSS (77 MB)";
                            MessageBox.Show(this, "Could not install XeSS: " + error);
                            return;
                        }
                        xessDownload.Text = "Reinstall XeSS";
                        version.Items[2] = "Intel XeSS";
                    });
                }) { IsBackground = true }.Start();
            };
            graphics.Controls.Add(xessDownload);
            // FSR-212: AMD FSR 4 for Vulkan comes with AMD's FSR SDK 2.4 (FSR Upscaling 4.1.2, with
            // the INT8 model for RX 6000 and RX 7000). Fetches AMD's signed loader and upscaler DLLs
            // on request and installs them next to GTSport.exe only when AMD's signature is on them.
            bool fsr4Installed = File.Exists(Path.Combine(root, "amd_fidelityfx_loader_vk.dll")) &&
                                 File.Exists(Path.Combine(root, "amd_fidelityfx_upscaler_vk.dll"));
            const string fsr4Label = "Download AMD FSR 4 (35 MB)";
            var fsr4Download = new Button { Text = fsr4Installed ? "Reinstall AMD FSR 4" : fsr4Label, AutoSize = true, Margin = new Padding(22, 2, 0, 4) };
            fsr4Download.Click += delegate
            {
                if (MessageBox.Show(this, "Download AMD FSR SDK 2.4.0 (FSR 4.1.2) from github.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK and install amd_fidelityfx_loader_vk.dll and amd_fidelityfx_upscaler_vk.dll next to GTSport.exe? They use AMD's license terms. FSR 4 runs on Radeon RX 6000, RX 7000 and RX 9000 cards.",
                                    "AMD FSR 4", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
                fsr4Download.Enabled = false;
                fsr4Download.Text = "Downloading...";
                new System.Threading.Thread(delegate ()
                {
                    string error = null;
                    const string sdk = "https://raw.githubusercontent.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK/v2.4.0/";
                    string temp = Path.Combine(Path.GetTempPath(), "RyGT-FSR4-" + Guid.NewGuid().ToString("N"));
                    try
                    {
                        Directory.CreateDirectory(temp);
                        System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
                        var names = new[] { "amd_fidelityfx_loader_vk.dll", "amd_fidelityfx_upscaler_vk.dll" };
                        using (var web = new System.Net.WebClient())
                        {
                            foreach (var name in names)
                                web.DownloadFile(sdk + "Kits/FidelityFX/signedbin/" + name, Path.Combine(temp, name));
                            web.DownloadFile(sdk + "Kits/FidelityFX/docs/license.md", Path.Combine(temp, "amd_fidelityfx_sdk-LICENSE.md"));
                        }
                        foreach (var name in names)
                        {
                            string subject;
                            try { subject = System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(Path.Combine(temp, name)).Subject; }
                            catch { subject = ""; }
                            if (subject.IndexOf("Advanced Micro Devices", StringComparison.OrdinalIgnoreCase) < 0)
                                throw new Exception(name + " is not signed by AMD; nothing was installed.");
                        }
                        foreach (var name in names.Concat(new[] { "amd_fidelityfx_sdk-LICENSE.md" }))
                            File.Copy(Path.Combine(temp, name), Path.Combine(root, name), true);
                    }
                    catch (Exception e) { error = e.Message; }
                    try { Directory.Delete(temp, true); } catch { }
                    BeginInvoke((Action)delegate
                    {
                        fsr4Download.Enabled = true;
                        if (error != null)
                        {
                            fsr4Download.Text = fsr4Label;
                            MessageBox.Show(this, "Could not install AMD FSR 4: " + error);
                            return;
                        }
                        fsr4Download.Text = "Reinstall AMD FSR 4";
                    });
                }) { IsBackground = true }.Start();
            };
            graphics.Controls.Add(fsr4Download);
            // FSR-209: the upscaler's preset (GPU.upscale_ratio). GT Sport always renders the race at
            // 1920 x 1080, so a preset sets the output size as a multiple of it, which the picture
            // is then scaled to the window from (supersampling when the window is smaller). Each
            // upscaler lists its own presets; "Match the window" (0) keeps the window's size.
            var presetRow = new FlowLayoutPanel { AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(22, 2, 0, 2), WrapContents = false };
            var presetLabel = new Label { Text = "Output resolution", AutoSize = false, Width = 174, Height = 20, BackColor = Color.Transparent, Margin = new Padding(0, 6, 8, 0) };
            var preset = new DarkComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300, FlatStyle = FlatStyle.Flat };
            // The output size each preset gives from the race's 1920 x 1080, then the upscaler's own
            // name for that scale.
            var fsrPresets = new[] {
                new[] { "Same as the window / screen", "0" },
                new[] { "1920 x 1080 - anti-aliasing only (Native AA)", "1" },
                new[] { "2880 x 1620 - sharper (Quality, 1.5x)", "1.5" },
                new[] { "3264 x 1836 - sharper (Balanced, 1.7x)", "1.7" },
                new[] { "3840 x 2160 / 4K - sharpest (Performance, 2x)", "2" },
                new[] { "5760 x 3240 - very heavy (Ultra Performance, 3x)", "3" } };
            var xessPresets = new[] {
                new[] { "Same as the window / screen", "0" },
                new[] { "1920 x 1080 - anti-aliasing only (Native AA)", "1" },
                new[] { "2496 x 1404 - sharper (Ultra Quality Plus, 1.3x)", "1.3" },
                new[] { "2880 x 1620 - sharper (Ultra Quality, 1.5x)", "1.5" },
                new[] { "3264 x 1836 - sharper (Quality, 1.7x)", "1.7" },
                new[] { "3840 x 2160 / 4K - sharpest (Balanced, 2x)", "2" },
                new[] { "4416 x 2484 - very heavy (Performance, 2.3x)", "2.3" },
                new[] { "5760 x 3240 - very heavy (Ultra Performance, 3x)", "3" } };
            string[][] shownPresets = fsrPresets;
            bool loadingPresets = false;
            Action fillPresets = delegate
            {
                loadingPresets = true;
                shownPresets = versionValues[Math.Max(0, version.SelectedIndex)] == "xess" ? xessPresets : fsrPresets;
                string saved = ReadStringSetting("GPU", "upscale_ratio", "0");
                preset.Items.Clear();
                int selected = 0;
                for (int i = 0; i < shownPresets.Length; i++)
                {
                    preset.Items.Add(shownPresets[i][0]);
                    if (shownPresets[i][1] == saved) selected = i;
                }
                preset.SelectedIndex = selected;
                loadingPresets = false;
            };
            fillPresets();
            preset.SelectedIndexChanged += delegate
            {
                if (loadingPresets || preset.SelectedIndex < 0) return;
                try { WriteSetting("GPU", "upscale_ratio", shownPresets[preset.SelectedIndex][1]); }
                catch (Exception e) { MessageBox.Show(this, "Could not save the setting: " + e.Message); }
            };
            version.SelectedIndexChanged += delegate { fillPresets(); };
            presetRow.Controls.Add(presetLabel);
            presetRow.Controls.Add(preset);
            graphics.Controls.Add(presetRow);
            var presetNote = new Label
            {
                Text = "With the default 1920 x 1080 race scene, the upscaler rebuilds the picture at the " +
                       "output resolution, then it is fitted to your screen. On a 1080p screen, anything above " +
                       "1920 x 1080 is supersampling: smoother edges and finer detail, but more GPU work.",
                AutoSize = true, MaximumSize = new Size(420, 0), BackColor = Color.Transparent, ForeColor = Dark.DimText,
                Tag = "heading", Margin = new Padding(22, 2, 0, 6)
            };
            graphics.Controls.Add(presetNote);
            // Under "Use FSR 3.1.4", before sharpening and frame generation.
            graphics.Controls.SetChildIndex(fsrTemporal, graphics.Controls.GetChildIndex(fsr3) + 1);
            graphics.Controls.SetChildIndex(versionRow, graphics.Controls.GetChildIndex(fsrTemporal) + 1);
            graphics.Controls.SetChildIndex(presetRow, graphics.Controls.GetChildIndex(versionRow) + 1);
            graphics.Controls.SetChildIndex(presetNote, graphics.Controls.GetChildIndex(presetRow) + 1);
            foreach (var sub in new[] { fsr3, fsrTemporal, rcas, framegen }) sub.Margin = new Padding(22, 2, 0, 2);
            Action refreshFsr = delegate
            {
                fsr3.Enabled = fsr.Checked && fsr3Installed;
                fsrTemporal.Enabled = version.Enabled = preset.Enabled = fsr.Checked && fsr3Installed && fsr3.Checked;
                rcas.Enabled = fsr.Checked;
            };
            refreshFsr();
            framegen.Enabled = fsr3Installed;
            fsr.CheckedChanged += delegate { refreshFsr(); };
            fsr3.CheckedChanged += delegate { refreshFsr(); };
            // NVIDIA (the dlss folder): DLSS upscaling through ryGT_dlss.dll and nvngx_dlss.dll, and
            // DLSS frame generation through Streamline (sl.*.dll and nvngx_dlssg.dll; RTX 40 and
            // newer, Windows' hardware-accelerated GPU scheduling on). One frame generation at a time.
            // The release does not include NVIDIA's files: the player adds their own copies with the
            // button below (NVIDIA's DLSS SDK and Streamline SDK, or another game's copies).
            string dlssDir = Path.Combine(root, "dlss");
            var dlss = AddOption(graphics, "", "GPU", "dlss_enabled", false);
            // DLSS's input is the game's own picture, at most 1920 x 1080 (#61).
            graphics.Controls.Add(new Label
            {
                Text = "Internal resolution: up to 1920 x 1080.\nAt 4K output, DLSS corresponds to Performance mode.",
                AutoSize = true, MaximumSize = new Size(330, 0), BackColor = Color.Transparent, ForeColor = Dark.DimText,
                Tag = "heading", Margin = new Padding(22, 2, 0, 6)
            });
            var dlssg = AddOption(graphics, "", "GPU", "dlssg_enabled", false);
            dlssg.Margin = new Padding(22, 2, 0, 2);
            Action refreshDlss = delegate
            {
                bool dlssInstalled = File.Exists(Path.Combine(dlssDir, "ryGT_dlss.dll")) &&
                                     File.Exists(Path.Combine(dlssDir, "nvngx_dlss.dll"));
                bool dlssgInstalled = File.Exists(Path.Combine(dlssDir, "sl.interposer.dll")) &&
                                      File.Exists(Path.Combine(dlssDir, "nvngx_dlssg.dll"));
                dlss.Text = dlssInstalled ? "Upscale with NVIDIA DLSS (RTX cards; instead of FSR)"
                                          : "Upscale with NVIDIA DLSS (add the DLSS files first)";
                dlss.Enabled = dlssInstalled;
                dlssg.Text = dlssgInstalled ? "DLSS frame generation: about 2x the frames (RTX 40+)"
                                            : "DLSS frame generation (add the DLSS files first)";
                dlssg.Enabled = dlssgInstalled;
            };
            refreshDlss();
            addDlss = new Button { Text = "Add DLSS files...", AutoSize = true, Margin = new Padding(22, 2, 0, 4) };
            addDlss.Click += delegate
            {
                // The game keeps the DLSS files open while it runs (#61).
                var games = Process.GetProcessesByName("GTSport");
                bool gameRunning = games.Length != 0;
                foreach (var process in games) process.Dispose();
                if (gameRunning)
                {
                    MessageBox.Show(this, "Close GT Sport before adding or replacing the DLSS files.");
                    return;
                }
                using (var dialog = new FolderBrowserDialog
                {
                    Description = "Pick the folder with your NVIDIA DLSS files: nvngx_dlss.dll for upscaling; " +
                                  "for frame generation also nvngx_dlssg.dll and Streamline's sl.*.dll and " +
                                  "NvLowLatencyVk.dll (from NVIDIA's DLSS and Streamline SDKs)."
                })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    var names = new[] { "nvngx_dlss.dll", "nvngx_dlssg.dll", "sl.interposer.dll", "sl.common.dll",
                                        "sl.dlss_g.dll", "sl.reflex.dll", "sl.pcl.dll", "NvLowLatencyVk.dll" };
                    string report = CopyDlssFiles(dialog.SelectedPath, names, dlssDir);
                    refreshDlss();
                    MessageBox.Show(this, report);
                }
            };
            graphics.Controls.Add(addDlss);
            var dlssSharpen = AddDlssSharpening(graphics, dlss);
            // DLSS and FSR share the same reconstruction pass.
            fsr.CheckedChanged += delegate { if (fsr.Checked && dlss.Checked) dlss.Checked = false; };
            Action updateDlss = delegate
            {
                if (!dlss.Checked) return;
                if (fsr.Checked) fsr.Checked = false;
            };
            dlss.CheckedChanged += delegate { updateDlss(); };
            updateDlss();
            // DLSS-210: fetches NVIDIA's official DLSS and Streamline files into the dlss folder on
            // request; nothing NVIDIA ships with the build.
            var nvidiaDownload = new Button { Text = "Download NVIDIA DLSS and Streamline (about 340 MB)", AutoSize = true, Margin = new Padding(22, 2, 0, 4) };
            nvidiaDownload.Click += delegate
            {
                var games = Process.GetProcessesByName("GTSport");
                bool gameRunning = games.Length != 0;
                foreach (var process in games) process.Dispose();
                if (gameRunning)
                {
                    MessageBox.Show(this, "Close GT Sport before adding or replacing the DLSS files.");
                    return;
                }
                if (MessageBox.Show(this, "Download DLSS 310.9.16 and Streamline 2.14.16 from NVIDIA's GitHub pages into the dlss folder? They use NVIDIA's license terms. DLSS needs an NVIDIA RTX card.",
                                    "NVIDIA DLSS", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
                nvidiaDownload.Enabled = false;
                nvidiaDownload.Text = "Downloading...";
                new System.Threading.Thread(delegate ()
                {
                    string error = null;
                    string temp = Path.Combine(Path.GetTempPath(), "RyGT-NVIDIA");
                    try
                    {
                        Directory.CreateDirectory(temp);
                        Directory.CreateDirectory(dlssDir);
                        System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
                        const string dlssUrl = "https://github.com/NVIDIA/DLSS/raw/v310.9.16/";
                        string zip = Path.Combine(temp, "streamline.zip");
                        using (var web = new System.Net.WebClient())
                        {
                            foreach (var name in new[] { "nvngx_dlss.dll", "nvngx_dlssg.dll" })
                                web.DownloadFile(dlssUrl + "lib/Windows_x86_64/rel/" + name, Path.Combine(dlssDir, name));
                            web.DownloadFile(dlssUrl + "LICENSE.txt", Path.Combine(dlssDir, "NVIDIA-DLSS-LICENSE.txt"));
                            web.DownloadFile("https://github.com/NVIDIA-RTX/Streamline/releases/download/v2.14.16/streamline-sdk-v2.14.16.zip", zip);
                        }
                        var wanted = new[] { "sl.interposer.dll", "sl.common.dll", "sl.dlss_g.dll", "sl.reflex.dll", "sl.pcl.dll", "NvLowLatencyVk.dll" };
                        var missing = new List<string>(wanted);
                        using (var archive = System.IO.Compression.ZipFile.OpenRead(zip))
                            foreach (var entry in archive.Entries)
                            {
                                if (!entry.FullName.Replace('/', Path.DirectorySeparatorChar).EndsWith(Path.Combine("bin", "x64", entry.Name), StringComparison.OrdinalIgnoreCase)) continue;
                                if (!wanted.Contains(entry.Name, StringComparer.OrdinalIgnoreCase)) continue;
                                System.IO.Compression.ZipFileExtensions.ExtractToFile(entry, Path.Combine(dlssDir, entry.Name), true);
                                missing.RemoveAll(name => String.Equals(name, entry.Name, StringComparison.OrdinalIgnoreCase));
                            }
                        if (missing.Count != 0) error = "Not in the Streamline package: " + String.Join(", ", missing);
                    }
                    catch (Exception e) { error = e.Message; }
                    try { Directory.Delete(temp, true); } catch { }
                    BeginInvoke((Action)delegate
                    {
                        nvidiaDownload.Enabled = true;
                        nvidiaDownload.Text = "Download NVIDIA DLSS and Streamline (about 340 MB)";
                        refreshDlss();
                        if (error != null) MessageBox.Show(this, "Could not install the NVIDIA files: " + error);
                    });
                }) { IsBackground = true }.Start();
            };
            graphics.Controls.Add(nvidiaDownload);
            graphics.Controls.SetChildIndex(nvidiaDownload, graphics.Controls.GetChildIndex(addDlss) + 1);
            framegen.CheckedChanged += delegate { if (framegen.Checked && dlssg.Checked) dlssg.Checked = false; };
            dlssg.CheckedChanged += delegate { if (dlssg.Checked && framegen.Checked) framegen.Checked = false; };
            // GTS-007: in the rain the game renders the scene a second time for the wet track's
            // reflection (about a quarter of a rainy frame's draws). Off: about +30% fps in rain.
            var wet = AddOption(graphics, "Wet-road reflections in the rain (off: much faster in rain)", "GPU",
                                "wet_reflections", true);
            // GTS-008: the rear-view mirror is redrawn every second frame (its picture is kept).
            var mirror = AddOption(graphics, "Rear-view mirror at half rate (a little faster)", "GPU",
                                   "mirror_half_rate", false);
            // The half-rate mirror is off while temporal DLSS is on, to keep its motion data (#61).
            Action updateMirror = delegate
            {
                mirror.Enabled = !dlss.Checked;
                mirror.Text = dlss.Checked ? "Half-rate rear-view mirror (disabled by DLSS)"
                                          : "Rear-view mirror at half rate (a little faster)";
            };
            dlss.CheckedChanged += delegate { updateMirror(); };
            updateMirror();
            // The Graphics tab: one card per subject. The older controls stay hidden and hold the
            // settings' logic; the unified choices below drive them.
            var sceneBox = graphics.Controls.OfType<CheckBox>().First(c => c.Text.StartsWith("Scene"));
            var sceneCombo = graphics.Controls.OfType<DarkComboBox>().First();
            graphics.Visible = false;
            bool syncing = false;
            Func<TableLayoutPanel, string, Control, FlowLayoutPanel> row = delegate (TableLayoutPanel card, string name, Control control)
            {
                var line = new FlowLayoutPanel { AutoSize = true, BackColor = Color.Transparent, WrapContents = false, Margin = new Padding(0, 3, 0, 3) };
                line.Controls.Add(new Label { Text = name, AutoSize = false, Width = 190, Height = 20, BackColor = Color.Transparent, Margin = new Padding(0, 6, 8, 0) });
                line.Controls.Add(control);
                card.Controls.Add(line);
                return line;
            };
            Func<DarkComboBox> combo = () => new DarkComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 380, FlatStyle = FlatStyle.Flat };

            var upscaler = combo();
            upscaler.Name = "UpscalingMethod";
            upscaler.Items.AddRange(new object[] { "Off", "AMD FSR 4", "AMD FSR 3", "AMD FSR 1", "NVIDIA DLSS", "Intel XeSS" });
            upscaler.SelectedIndex = dlss.Checked ? 4 : !fsr.Checked ? 0 : !fsr3.Checked ? 3 : version.SelectedIndex == 1 ? 1 : version.SelectedIndex == 2 ? 5 : 2;
            int lastUpscaler = upscaler.SelectedIndex;
            CheckBox checkerboard = null;
            upscaler.SelectedIndexChanged += delegate
            {
                if (syncing) return;
                int pick = upscaler.SelectedIndex;
                if (pick == 4 && checkerboard != null && checkerboard.Checked)
                {
                    syncing = true;
                    upscaler.SelectedIndex = lastUpscaler;
                    syncing = false;
                    MessageBox.Show(this, CheckerboardWarning + " " + UseDlssGuide, "PS4 Pro and DLSS");
                    return;
                }
                if ((pick == 4 && !dlss.Enabled) || (pick == 5 && !File.Exists(Path.Combine(root, "libxess.dll"))))
                {
                    MessageBox.Show(this, "Download its files first, under Libraries.");
                    syncing = true;
                    upscaler.SelectedIndex = 0;
                    syncing = false;
                    pick = 0;
                }
                dlss.Checked = pick == 4;
                fsr.Checked = pick != 0 && pick != 4;
                if (pick != 0 && pick != 4)
                {
                    fsr3.Checked = pick != 3;
                    if (pick != 3) version.SelectedIndex = pick == 1 ? 1 : pick == 5 ? 2 : 0;
                }
                lastUpscaler = pick;
            };
            row(upscaling, "Method", upscaler);
            var methodProWarning = Hint("");
            methodProWarning.ForeColor = Dark.AccentText;
            upscaling.Controls.Add(methodProWarning);
            // Below 100% lowers the scene (render scale); above it the upscaler's output grows.
            var qualities = new[] {
                new[] { "Ultra Performance - 33%", "0", "" },
                new[] { "Performance - 50%", "1", "" },
                new[] { "Balanced - 59%", "2", "" },
                new[] { "Quality - 67%", "3", "" },
                new[] { "Ultra Quality - 77%", "4", "" },
                new[] { "Native - 100% (1920 x 1080)", "", "1" },
                new[] { "Supersampling - 150% (2880 x 1620)", "", "1.5" },
                new[] { "Supersampling - 170% (3264 x 1836)", "", "1.7" },
                new[] { "Supersampling - 200% (3840 x 2160)", "", "2" },
                new[] { "Supersampling - 300% (5760 x 3240)", "", "3" },
                new[] { "Auto (window size)", "", "0" } };
            var quality = combo();
            quality.Name = "UpscalingQuality";
            row(upscaling, "Quality", quality);
            var qualityGuide = Hint("");
            qualityGuide.Name = "ImageQualityGuide";
            upscaling.Controls.Add(qualityGuide);
            // As on GT7's page: an accent line while NVIDIA DLSS is selected.
            var dlssAdvice = Hint("");
            dlssAdvice.Name = "DlssAdvice";
            dlssAdvice.ForeColor = Dark.AccentText;
            dlssAdvice.Visible = false;
            upscaling.Controls.Add(dlssAdvice);
            bool fillingQuality = false;
            Action updateQualityGuide = delegate {
                dlssAdvice.Text = upscaler.SelectedIndex == 4 ? DlssAdvice : "";
                dlssAdvice.Visible = upscaler.SelectedIndex == 4;
                if (quality.SelectedIndex < 0) return;
                qualityGuide.Text = upscaler.SelectedIndex == 4
                    ? UseDlssGuide + " " + DlssModeGuide(quality.SelectedIndex)
                    : upscaler.SelectedIndex == 0 ? Native4kGuide +
                      (sceneBox.Checked ? " Set Quality to Native (100%) to turn off experimental scaling." : "")
                    : "Below 100%, the percentage sets the render size relative to the display. Above 100%, it sets the upscaler's output relative to the game's image.";
            };
            Action fillQuality = delegate {
                fillingQuality = true;
                quality.Items.Clear();
                if (upscaler.SelectedIndex == 4) {
                    quality.Items.AddRange(RenderResolution.DlssLabels);
                    var gpu = ReadGpuSettings(root, GameInfo.GTSport);
                    string saved = RenderResolution.SavedDlssMode(true, key => gpu.ContainsKey(key) ? gpu[key] : null);
                    quality.SelectedIndex = Array.IndexOf(RenderResolution.DlssModes, saved);
                } else {
                    foreach (var item in qualities) quality.Items.Add(item[0]);
                    string ratio = ReadStringSetting("GPU", "upscale_ratio", "0");
                    quality.SelectedIndex = sceneBox.Checked ? Math.Min(4, Math.Max(0, sceneCombo.SelectedIndex))
                        : Math.Max(5, Array.FindIndex(qualities, item => item[2] == ratio));
                }
                fillingQuality = false;
                updateQualityGuide();
            };
            quality.SelectedIndexChanged += delegate {
                if (fillingQuality || quality.SelectedIndex < 0) return;
                try {
                    if (upscaler.SelectedIndex == 4) {
                        WriteSetting("GPU", "dlss_preset", RenderResolution.DlssModes[quality.SelectedIndex]);
                    } else {
                        var item = qualities[quality.SelectedIndex];
                        sceneBox.Checked = item[1] != "";
                        if (sceneBox.Checked) sceneCombo.SelectedIndex = int.Parse(item[1]);
                        WriteSetting("GPU", "upscale_ratio", item[2] != "" ? item[2] : "0");
                    }
                } catch (Exception e) { MessageBox.Show(this, "Could not save the setting: " + e.Message); }
                updateQualityGuide();
                refreshSceneChoice();
            };
            upscaler.SelectedIndexChanged += delegate { fillQuality(); refreshSceneChoice(); };
            fillQuality();
            // PERF-210: AMD Anti-Lag (VK_AMD_anti_lag; AMD GPUs whose driver has it). Frame
            // generation paces its own frames, so Anti-Lag is off while it is on.
            var antiLag = AddOption(upscaling, "", "GPU", "anti_lag", false);

            var frames = combo();
            frames.Items.AddRange(new object[] { "Off", "AMD FSR - about 2x frames", "NVIDIA DLSS - about 2x frames (RTX 40+)" });
            frames.SelectedIndex = dlssg.Checked ? 2 : framegen.Checked ? 1 : 0;
            frames.SelectedIndexChanged += delegate
            {
                if (frames.SelectedIndex == 2 && !dlssg.Enabled)
                {
                    MessageBox.Show(this, "Download the NVIDIA files first, under Libraries.");
                    frames.SelectedIndex = 0;
                    return;
                }
                framegen.Checked = frames.SelectedIndex == 1;
                dlssg.Checked = frames.SelectedIndex == 2;
            };
            row(frameGen, "Method", frames);
            Action updateAntiLag = delegate
            {
                bool generated = frames.SelectedIndex != 0;
                antiLag.Enabled = !generated;
                antiLag.Text = generated ? "AMD Anti-Lag (off while frame generation is on)"
                                         : "AMD Anti-Lag: lower input lag (AMD GPUs)";
            };
            frames.SelectedIndexChanged += delegate { updateAntiLag(); };
            updateAntiLag();

            var sharpenCard = Section(graphicsPage, "Sharpening");
            // FSR's attenuation is 0 (sharpest) to 2 in thousandths; 0% turns the pass off.
            int attenuation = ReadIntSetting("GPU", "rcas_attenuation", 250);
            var fsrSharpen = new Slider { Minimum = 0, Maximum = 100, Width = 330, Suffix = "%", AccessibleName = "FSR sharpening" };
            fsrSharpen.Value = rcas.Checked ? Math.Max(0, Math.Min(100, 100 - attenuation / 20)) : 0;
            fsrSharpen.ValueChanged += delegate
            {
                rcas.Checked = fsrSharpen.Value != 0;
                try { WriteSetting("GPU", "rcas_attenuation", (100 - fsrSharpen.Value) * 20); }
                catch (Exception e) { MessageBox.Show(this, "Could not save the setting: " + e.Message); }
            };
            sharpenCard.Controls.Add(new SettingRow("FSR sharpening (0% off)", fsrSharpen));
            sharpenCard.Controls.Add(dlssSharpen);

            var librariesCard = Section(graphicsPage, "Libraries");
            librariesCard.Controls.Add(new SettingRow("Intel XeSS (any GPU)", xessDownload));
            librariesCard.Controls.Add(new SettingRow("AMD FSR 4 (Radeon RX 6000 and newer)", fsr4Download));
            librariesCard.Controls.Add(new SettingRow("NVIDIA DLSS and Streamline (RTX cards)", nvidiaDownload));
            librariesCard.Controls.Add(new SettingRow("Your own NVIDIA DLSS files", addDlss));
            librariesCard.Controls.Add(Hint("RyGT does not include these libraries; the buttons fetch each vendor's own files from its GitHub page."));

            var performanceCard = Section(graphicsPage, "Performance");
            foreach (var box in new[] { wet, mirror })
            {
                box.Margin = new Padding(0);
                performanceCard.Controls.Add(box);
            }

            // The direct renderer (shaders read game memory in place; Tokyo rain 42.7 fps vs 32.1)
            // is the only renderer since 2026-10-16; the old renderer's stored shaders stand in
            // while its own are built (D10), so it has no option any more.

            var extras = Section(graphicsPage, "Enhancements");
            AddOption(extras, "Disable motion blur", "GPU", "disable_motion_blur", false);
            Action refreshProWarning;
            checkerboard = AddProSettings(pro, () => dlss.Checked, methodProWarning, out refreshProWarning);
            dlss.CheckedChanged += delegate {
                if (dlss.Checked && checkerboard.Checked) {
                    dlss.Checked = false;
                    MessageBox.Show(this, "Choose base PS4 or PS4 Pro, 1080p TV before enabling NVIDIA DLSS.");
                }
                refreshProWarning();
            };
            uiHiresBox = AddOption(extras, "Draw the UI at screen resolution", "GPU", "ui_hires", false);
            uiHiresBox.CheckedChanged += delegate { refreshSceneChoice(); };
            // ---- Controls: which controller is which player (splitscreen) with a live preview, the
            // controller and keyboard bindings, and racing wheels and pedals (ryty-input.ini /
            // ryty-wheel.ini, read by libScePad).
            var controlsPage = NewPage("Controls", "Controls", "2 players for Arcade's Split Screen: pick each player's name and controller, and what each " +
                                                               "button does. In game, F3 or holding Share + Options (View + Menu) changes controllers.");
            var controls = Section(controlsPage, "Controllers and wheels");
            controls.Controls.Add(OpenRow("Controller ports: which controller plays as each player, with a live preview", OpenPorts));
            controls.Controls.Add(OpenRow("Button bindings: what each PS4 button is on your controller and keyboard", OpenBindings));
            controls.Controls.Add(OpenRow("Steering wheel: map your wheel and pedals; force feedback strength and feel", OpenWheel));
            // FEAT-GTS-280: HD rumble and the adaptive triggers (the trigger strength slider moved there).
            controls.Controls.Add(OpenRow("HD rumble & adaptive triggers: engine, road, wheel slip, ABS and pedal feel, with tests", OpenFeel));

        }


        void OpenBindings()
        {
            using (var form = new ControlsForm(root, Math.Max(1, ReadIntSetting("Input", "keyboard_player", 1)),
                                               player => WriteSetting("Input", "keyboard_player", player)))
            {
                form.ShowDialog(this);
            }
        }


        void OpenWheel()
        {
            using (var form = new WheelForm(root, GameInfo.GTSport)) form.ShowDialog(this);
        }


        void OpenFeel()
        {
            using (var form = new FeelForm(root, (key, fallback) => ReadIntSetting("Input", key, fallback),
                                           (key, value) => WriteSetting("Input", key, value)))
            {
                form.ShowDialog(this);
            }
        }


        // Screen: windowed, borderless fullscreen (the desktop's resolution and refresh rate), or
        // exclusive fullscreen at a chosen resolution and refresh rate (GPU.full_screen,
        // full_screen_mode, window_width/height, fullscreen_refresh_rate). The game draws at
        // 1920x1080 and runs at up to 60 fps; FSR or DLSS scale it to the screen, and a higher
        // refresh rate shows frame generation's extra frames.
        void SaveVulkanDevice(VulkanDevice device)
        {
            var config = LoadConfig();
            object found;
            var section = config.TryGetValue("Vulkan", out found) ? found as Dictionary<string, object> : null;
            if (section == null) { section = new Dictionary<string, object>(); config["Vulkan"] = section; }
            section["gpu_id"] = device.Index;
            // Extra identity fields are launcher-owned; the renderer consumes gpu_id.
            section["device_uuid"] = device.Index < 0 ? "" : device.Uuid;
            section["device_name"] = device.Index < 0 ? "" : device.Name;
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
            File.WriteAllText(ConfigPath, new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(config),
                              new UTF8Encoding(false));
        }


        void ResolveVulkanDevice()
        {
            string uuid = ReadStringSetting("Vulkan", "device_uuid", "");
            int index = ReadIntSetting("Vulkan", "gpu_id", -1);
            if (uuid.Length == 0 && index < 0) return;
            var devices = VulkanDevices.Enumerate();
            var device = uuid.Length != 0
                ? devices.FirstOrDefault(d => string.Equals(d.Uuid, uuid, StringComparison.OrdinalIgnoreCase))
                : devices.FirstOrDefault(d => d.Index == index);
            if (device == null)
                throw new Exception("The selected Vulkan device is unavailable. Choose another device or Automatic in Settings > Display.");
            if (device.ApiVersion < ((1u << 22) | (3u << 12)))
                throw new Exception("The selected device does not support Vulkan 1.3. Choose another Vulkan device in Settings > Display.");
            if (device.Index != index || uuid.Length == 0) SaveVulkanDevice(device);
        }


        void AddVulkanDeviceSetting(TableLayoutPanel panel)
        {
            var row = new FlowLayoutPanel { AutoSize = true, BackColor = Color.Transparent,
                Margin = new Padding(0, 2, 0, 2), WrapContents = false };
            row.Controls.Add(new Label { Text = "Vulkan device", Width = 104, Height = 20,
                BackColor = Color.Transparent, Margin = new Padding(0, 6, 8, 0) });
            var combo = new DarkComboBox { DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 252, DropDownWidth = 500, FlatStyle = FlatStyle.Flat };
            var automatic = new VulkanDevice { Index = -1, Name = "Automatic", Uuid = "" };
            combo.Items.Add(automatic);
            string uuid = ReadStringSetting("Vulkan", "device_uuid", "");
            int index = ReadIntSetting("Vulkan", "gpu_id", -1);
            VulkanDevice selected = uuid.Length == 0 && index < 0 ? automatic : null;
            string error = null;
            try
            {
                foreach (var device in VulkanDevices.Enumerate())
                {
                    if (device.ApiVersion < ((1u << 22) | (3u << 12))) device.Name += " (below Vulkan 1.3)";
                    combo.Items.Add(device);
                    if (uuid.Length != 0 ? string.Equals(device.Uuid, uuid, StringComparison.OrdinalIgnoreCase)
                                         : device.Index == index) selected = device;
                }
            }
            catch (Exception e) { error = "Could not list Vulkan devices: " + e.Message; }
            if (selected == null)
            {
                selected = new VulkanDevice { Index = -2, Uuid = uuid,
                    Name = ReadStringSetting("Vulkan", "device_name", "GPU " + index) + " (unavailable)" };
                combo.Items.Add(selected);
            }
            combo.SelectedItem = selected;
            combo.SelectedIndexChanged += delegate
            {
                var device = combo.SelectedItem as VulkanDevice;
                if (device == null || device.Index == -2) return;
                try { SaveVulkanDevice(device); }
                catch (Exception e) { MessageBox.Show(this, "Could not save Vulkan device: " + e.Message); }
            };
            row.Controls.Add(combo);
            panel.Controls.Add(row);
            if (error != null) panel.Controls.Add(new Label { Text = error, AutoSize = true,
                MaximumSize = new Size(365, 0), BackColor = Color.Transparent, ForeColor = Color.Silver });
        }


        CheckBox uiHiresBox;

        ComboBox rendererCombo;


        // Renderer (GPU.native_renderer): the native renderer (the default; GT Sport's command
        // buffers recorded straight into Vulkan, faster in races) or the old renderer (shadGT's,
        // with the direct memory renderer). SHADGT_NATIVE=1 or =0 overrides the choice.
        void AddRendererSetting(TableLayoutPanel panel)
        {
            var row = new FlowLayoutPanel { AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(0, 2, 0, 4), WrapContents = false };
            var label = new Label { Text = "Renderer", AutoSize = false, Width = 110, Height = 20, BackColor = Color.Transparent, Margin = new Padding(0, 6, 8, 0) };
            var renderer = new DarkComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230, FlatStyle = FlatStyle.Flat };
            renderer.Items.AddRange(new object[] { "Old renderer", "Native renderer (default)" });
            renderer.SelectedIndex = ReadSetting("GPU", "native_renderer", true) ? 1 : 0;
            rendererCombo = renderer;
            renderer.SelectedIndexChanged += delegate
            {
                try { WriteSetting("GPU", "native_renderer", renderer.SelectedIndex == 1); refreshSceneChoice(); }
                catch (Exception e) { MessageBox.Show(this, "Could not save the setting: " + e.Message); }
            };
            row.Controls.Add(label);
            row.Controls.Add(renderer);
            panel.Controls.Add(row);
        }


        // FEAT-GTS-469: like RPCS3's "Remove obsolete settings?", offer to drop config entries nothing
        // reads any more (ObsoleteSettings.cs). "Don't show again" is kept in CUSA03220.RyGT.ini (prompt_obsolete).
        // FEAT-GTS-275: and to rename the Advanced page's switches saved under a name the installed build no
        // longer reads (RYGT_PARALLEL_WORKERS, now SHADGT_PARALLEL_WORKERS). Reading the modules takes
        // a moment, so it runs off the window's thread.
        void PromptObsoleteSettings()
        {
            if (target != GameKind.GTSport || !RootReady(target)) return; // FEAT-GTS-290: GT Sport's settings only
            obsoleteAsked = true;
            if (!settings.PromptObsolete) return;
            Dictionary<string, object> config;
            try { config = LoadConfig(); } catch { return; } // an unreadable config is reported where it is used
            var found = ObsoleteSettings.Find(config);
            var saved = CustomEnvironment.Copy(settings.Env);
            if (!SwitchWalker.HasSwitchNames(saved)) { AskObsoleteSettings(config, found, null); return; }
            ThreadPool.QueueUserWorkItem(delegate
            {
                var renames = SwitchWalker.FindRenames(saved, SwitchWalker.KnownNames(root));
                try { BeginInvoke((Action)delegate { AskObsoleteSettings(config, found, renames); }); }
                catch (InvalidOperationException) { } // closed meanwhile
            });
        }


        void AskObsoleteSettings(Dictionary<string, object> config, List<string> found, List<KeyValuePair<string, string>> renames)
        {
            var lines = new List<string>(found);
            if (renames != null) lines.AddRange(renames.Select(r => r.Key + " - renamed to " + r.Value));
            if (lines.Count == 0) return;
            using (var dialog = new ObsoleteSettingsDialog(lines, renames != null && renames.Count > 0))
            {
                var answer = dialog.ShowDialog(this);
                if (dialog.DontShowAgain)
                {
                    settings.PromptObsolete = false;
                    try { settings.Save(); } catch { }
                }
                if (answer != DialogResult.Yes) return;
            }
            try
            {
                if (found.Count > 0)
                {
                    ObsoleteSettings.Remove(config);
                    File.WriteAllText(ConfigPath, new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(config),
                                      new UTF8Encoding(false));
                }
                if (renames != null && SwitchWalker.Rename(settings.Env, renames) > 0) settings.Save();
            }
            catch (Exception e) { MessageBox.Show(this, "Could not update the obsolete settings: " + e.Message); }
        }


        // The native renderer is the default, but the renderer itself still falls back to the old one
        // when GPU.native_renderer is not set, so write the launcher's default before the game starts.
        // A choice already saved (either renderer) is kept.
        void ApplyNativeRendererDefault()
        {
            bool unset = ReadSetting("GPU", "native_renderer", true) && !ReadSetting("GPU", "native_renderer", false);
            if (unset) WriteSetting("GPU", "native_renderer", true);
        }


        void AddExperimentalRenderScale(TableLayoutPanel panel)
        {
            var enabled = AddOption(panel, "Scene render scale (EXPERIMENTAL)",
                "GPU", "experimental_render_scale", false);
            var preset = new DarkComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
            var values = new[] { "ultra_performance", "performance", "balanced", "quality", "ultra_quality", "dlaa" };
            preset.Items.AddRange(new object[] { "33% - Ultra Performance (1080p to 640 x 360)", "50% - Performance (1080p to 960 x 540)", "59% - Balanced (1080p to 1136 x 639)", "67% - Quality (1080p to 1280 x 720)", "77% - Ultra Quality (1080p to 1472 x 828)", "100% - Native (1920 x 1080)" });
            preset.SelectedIndex = Math.Max(0, Array.IndexOf(values,
                ReadStringSetting("GPU", "experimental_render_preset", "performance")));
            preset.Enabled = enabled.Checked;
            preset.SelectedIndexChanged += delegate {
                try { WriteSetting("GPU", "experimental_render_preset", values[preset.SelectedIndex]); }
                catch (Exception e) { MessageBox.Show(this, "Could not save the setting: " + e.Message); }
            };
            enabled.CheckedChanged += delegate { preset.Enabled = enabled.Checked; };
            panel.Controls.Add(preset);
            var preview = new Label { AutoSize = true, ForeColor = Dark.DimText,
                Margin = new Padding(22, 2, 0, 4) };
            Action refreshPreview = delegate {
                try {
                    var output = RenderScaleOutputSize();
                    var scene = RenderResolution.Calculate(output.Width, output.Height, values[preset.SelectedIndex]);
                    preview.Text = "Scene: " + scene.Width + "x" + scene.Height + " -> output " +
                        output.Width + "x" + output.Height + ". Restart required.\n" +
                        "Works with or without an upscaler. Native renderer; restart required.\nExperimental; post-processing may differ. Off by default.";
                } catch (Exception e) { preview.Text = e.Message; }
            };
            preset.SelectedIndexChanged += delegate { refreshPreview(); };
            enabled.CheckedChanged += delegate { refreshPreview(); };
            refreshPreview();
            panel.Controls.Add(preview);
        }


        Size RenderScaleOutputSize()
        {
            int width = ReadIntSetting("GPU", "window_width", 1280);
            int height = ReadIntSetting("GPU", "window_height", 720);
            if (ReadSetting("GPU", "full_screen", false) &&
                ReadStringSetting("GPU", "full_screen_mode", "Borderless") != "Fullscreen")
            {
                var screen = Screen.FromControl(this).Bounds;
                width = screen.Width; height = screen.Height;
            }
            return new Size(width, height);
        }


        Size? experimentalSceneSize;


        void ApplyExperimentalRenderScale()
        {
            experimentalSceneSize = null;
            if (!ReadSetting("GPU", "experimental_render_scale", false)) return;
            if (ReadSetting("GPU", "dlss_enabled", false) && !ReadSetting("GPU", "fsr_enabled", false) &&
                SavedProMode() == "off") {
                var gpu = ReadGpuSettings(root, GameInfo.GTSport);
                if (SceneDlssAvailable(root, key => gpu.ContainsKey(key) ? gpu[key] : null,
                                       Environment.GetEnvironmentVariable("RYGT_DLSS_GUEST_OUTPUT"))) return;
            }
            if (!ReadSetting("GPU", "native_renderer", true))
                throw new InvalidOperationException("Experimental scene resolution requires the Native renderer.");
            if (ReadSetting("GPU", "ps4_pro", false))
                throw new InvalidOperationException("Turn off PS4 Pro mode before using experimental scene resolution.");
            bool dlss = ReadSetting("GPU", "dlss_enabled", false);
            var outputSize = RenderScaleOutputSize();
            string preset = ReadStringSetting("GPU", "experimental_render_preset", "performance");
            var size = RenderResolution.Calculate(outputSize.Width, outputSize.Height, preset);
            experimentalSceneSize = size;
            WriteSetting("GPU", "experimental_render_width", size.Width);
            WriteSetting("GPU", "experimental_render_height", size.Height);
            if (dlss) WriteSetting("GPU", "dlss_quality", preset == "ultra_quality" ? "quality" : preset);
            else if (ReadSetting("GPU", "fsr_enabled", false)) WriteSetting("GPU", "upscale_ratio", "0");
        }


        /// DLSS sharpening (GPU.dlss_sharpening, 0 off): a slider row, enabled while DLSS is on.
        SettingRow AddDlssSharpening(TableLayoutPanel panel, CheckBox dlss)
        {
            var slider = new Slider
            {
                Minimum = 0, Maximum = 100, SmallChange = 1, LargeChange = 10, Width = 330, Suffix = "%",
                AccessibleName = "DLSS sharpening"
            };
            slider.Value = Math.Max(0, Math.Min(100, ReadIntSetting("GPU", "dlss_sharpening", 0)));
            Action refresh = delegate { slider.Enabled = dlss.Checked && dlss.Enabled; };
            slider.ValueChanged += delegate
            {
                try { WriteSetting("GPU", "dlss_sharpening", slider.Value); }
                catch (Exception e) { MessageBox.Show(this, "Could not save the setting: " + e.Message); }
            };
            dlss.CheckedChanged += delegate { refresh(); };
            dlss.EnabledChanged += delegate { refresh(); };
            refresh();
            var row = new SettingRow("DLSS sharpening (0% off; needs DLSS)", slider);
            panel.Controls.Add(row);
            return row;
        }


        void RefreshStateGts()
        {
            bool ready = File.Exists(Exe) && Directory.Exists(Path.Combine(App0Layout.Top(root, info.TitleId), "sce_module"));
            play.Enabled = ready && !busy;
            if (setup != null) setup.Enabled = !busy;
            if (addDlss != null) addDlss.Enabled = !busy;
            if (!busy) status.Text = ready ? "Ready. Press Play." : "Not set up yet: open the settings (the gear), pick your game's folders and press Set up.";
        }


        void ShowSetupDumpHelp(Exception error)
        {
            if (error is NotDecryptedException) ShowNotDecrypted((NotDecryptedException)error); // FIX-RT-029
            else if (error is SetupDumpException)
                MessageBox.Show(this, error.Message, "Check your game dump and system modules",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            else if (error is DumpFileException) // a file that is no SELF or ELF, or still encrypted (Self.ToElf)
                MessageBox.Show(this, error.Message + "\n\n" +
                    "Re-dump your game/update and the PS4 sys_modules folder. Select the new dump folders, then run Set up again.",
                    "Check your game dump and system modules", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }


        void RunSetupGts()
        {
            GameBuildIntegrity.RequireStopped(Exe);
            string baseDir = settings.Game, updateDir = settings.Update;
            if (!Directory.Exists(baseDir)) throw new Exception("Pick the game folder (the one holding gt00.vol and eboot.bin).");
            if (!File.Exists(Path.Combine(baseDir, "gt00.vol")))
                throw new Exception("The game folder has no gt00.vol. Pick the base game folder of CUSA03220.");
            if (updateDir.Length > 0 && !Directory.Exists(updateDir)) throw new Exception("The update folder does not exist.");
            if (updateDir.Length == 0) updateDir = null;
            string gameDir = updateDir ?? baseDir; // the newest executable, modules and data index

            string version = ReadAppVersion(Path.Combine(gameDir, "sce_sys", "param.sfo"));
            Log("Game version: " + (version ?? "unknown"));
            if (version != null && version != "01.69")
                throw new Exception("This build is for update 1.69, but the game is " + version + ". Pick the 1.69 update folder too.");

            string fontDir, font2Dir;
            fontDir = font2Dir = null;
            if (!String.IsNullOrWhiteSpace(settings.Fonts))
                FindFonts(settings.Fonts, out fontDir, out font2Dir);
            else
                Log("Note: PS4 fonts are optional; without them, some in-game UI fonts may not draw or appear correctly.");
            var firmware = new Dictionary<string, string>();
            foreach (var name in FirmwareModules)
            {
                var found = FindModule(settings.Modules, name);
                if (found == null) throw new SetupDumpException("System module " + name + ".sprx was not found under " + settings.Modules + ".\n\n" +
                    "Re-dump the complete sys_modules folder from your PS4, including this module. Select the new folder, then run Set up again.");
                firmware[name] = found;
            }
            foreach (var name in OptionalFirmwareModules)
            {
                var found = FindModule(settings.Modules, name);
                if (found != null) firmware[name] = found;
                else Log("Note: " + name + " is not in the system modules folder; some related game features may be unavailable.");
            }

            // 1. The executable and modules to convert, unwrapped to plain ELF.
            SetStatus("Preparing the game files...");
            string work = info.OwnFolder(root, "convert"); // FEAT-GTS-291: convert\CUSA03220
            string workModules = Path.Combine(work, "sce_module");
            if (Directory.Exists(workModules)) Directory.Delete(workModules, true);
            Directory.CreateDirectory(workModules);
            Unwrap(Path.Combine(gameDir, "eboot.bin"), Path.Combine(work, "eboot.elf"));
            foreach (var name in new[] { "libc.prx", "libSceFios2.prx" })
            {
                string source = FirstExisting(Path.Combine(gameDir, "sce_module", name), Path.Combine(baseDir, "sce_module", name));
                if (source == null) throw new Exception("The game's sce_module\\" + name + " is missing.");
                Unwrap(source, Path.Combine(workModules, name));
            }
            foreach (var name in new[] { "libSceSecure.prx", "logiWheel.prx" })
            {
                string source = FirstExisting(Path.Combine(gameDir, name), Path.Combine(baseDir, name));
                if (source != null) Unwrap(source, Path.Combine(workModules, name));
            }
            foreach (var pair in firmware) Unwrap(pair.Value, Path.Combine(workModules, pair.Key + ".prx"));

            // 2. Convert.
            SetStatus("Converting the game for Windows (this takes a while)...");
            string converter = Path.Combine(root, "converter", "ryty.exe");
            if (!File.Exists(converter)) throw new Exception("converter\\ryty.exe is missing from the RyGT folder.");
            string staging = Path.Combine(work, "game-rebuild");
            string stagedExe = Path.Combine(staging, "GTSport.exe");
            // FEAT-GTS-291: the converted modules go in app0\CUSA03220 (App0Layout), and GTSport.exe loads them from there.
            bool perGame = App0Layout.Supported(root);
            string app0Option;
            string app0Target = App0Layout.SetupFolder(root, info.TitleId, perGame, out app0Option);
            string stagedModules = Path.Combine(perGame ? App0Layout.Folder(staging, info.TitleId) : Path.Combine(staging, "app0"), "sce_module");
            if (!perGame) Log("Note: this folder's converter or runtime libraries predate app0\\" + info.TitleId + "; the converted modules go in app0 as before.");
            Directory.CreateDirectory(stagedModules);
            int code = Run(converter, "--windows --ps4 --skip-syscall-check --registry" + app0Option + " --report \"" +
                Path.Combine(work, "port-report.json") + "\" \"" + Path.Combine(work, "eboot.elf") + "\" \"" + stagedExe + "\"", work);
            if (code != 0) // FIX-RT-029: an input the converter couldn't read as an ELF is not a decrypted dump
            {
                var notDecrypted = NotDecryptedException.FromConverterLog(Path.Combine(work, "convert.log"), Path.Combine(gameDir, "eboot.bin"));
                if (notDecrypted != null) throw notDecrypted;
            }
            if (code != 0 || !File.Exists(stagedExe)) throw new Exception("The converter failed (exit code " + code + ").");
            GameExe.Prepare(stagedExe);
            GameExe.ApplyVersionInfo(stagedExe);
            // Keep the previous executable until conversion and preparation have both succeeded.
            // The new GTSport.exe loads app0\CUSA03220\sce_module: the folder's older app0 moves there first.
            if (perGame) foreach (string note in App0Layout.Migrate(root, info, true)) Log(note);
            Directory.CreateDirectory(Path.Combine(app0Target, "sce_module"));
            foreach (string module in Directory.GetFiles(stagedModules))
                File.Copy(module, Path.Combine(app0Target, "sce_module", Path.GetFileName(module)), true);
            string registry = Path.Combine(staging, "GTSport.registry.json");
            if (File.Exists(registry)) File.Copy(registry, Path.Combine(root, "GTSport.registry.json"), true);
            if (File.Exists(Exe)) File.Replace(stagedExe, Exe, null);
            else File.Move(stagedExe, Exe);
            Log(Wine.Detected ? "Made GTSport.exe a Windows program." : "Gave GTSport.exe the Gran Turismo Sport icon and made it a Windows program.");

            // 3. The game's files stay where they are: /app0 is read from the update, then the base game.
            SetStatus("Checking the game files...");
            UseGameFoldersDirectly(baseDir, updateDir);

            // 4. Fonts.
            if (fontDir != null && font2Dir != null)
            {
                SetStatus("Copying the fonts...");
                CopyDirectory(fontDir, Path.Combine(root, "sys", "common", "font"));
                CopyDirectory(font2Dir, Path.Combine(root, "sys", "common", "font2"));
            }

            // 5. The shader folder every RyGT folder shares.
            UsePortableShaderCache();

            GameBuildIntegrity.Record(Exe);
            Log("Recorded GTSport.exe SHA-256: " + GameBuildIntegrity.Hash(Exe));
            Log("Set up finished.");
        }


        void RequireGameClosedForShaderPreparation()
        {
            if (Process.GetProcessesByName("GTSport").Length != 0)
                throw new IOException("Close the game before checking or updating its shader cache.");
        }


        static string ReadAppVersion(string sfo)
        {
            if (!File.Exists(sfo)) return null;
            var d = File.ReadAllBytes(sfo);
            if (d.Length < 20 || BitConverter.ToUInt32(d, 0) != 0x46535000) return null;
            int keys = (int)BitConverter.ToUInt32(d, 8), data = (int)BitConverter.ToUInt32(d, 12), count = (int)BitConverter.ToUInt32(d, 16);
            for (int i = 0; i < count; i++)
            {
                int e = 20 + i * 16;
                int keyOffset = BitConverter.ToUInt16(d, e);
                int length = (int)BitConverter.ToUInt32(d, e + 4);
                int dataOffset = (int)BitConverter.ToUInt32(d, e + 12);
                int end = keys + keyOffset;
                while (end < d.Length && d[end] != 0) end++;
                string key = Encoding.ASCII.GetString(d, keys + keyOffset, end - keys - keyOffset);
                if (key == "APP_VER") return Encoding.ASCII.GetString(d, data + dataOffset, length).TrimEnd('\0');
            }
            return null;
        }


        // FEAT-GTS-259: the game reads /app0 straight from the update and base game folders (named in
        // CUSA03220.RyGT.ini; see core/libs/prx/libc/include/AppOverlay.hpp), so nothing is linked into app0 any
        // more. Set up from earlier versions linked every game file there (hard links, which Explorer
        // counts as a second copy of the game); those links are removed. Only links are: a file is
        // removed only when it is the game's own file (the same file on disk, or a link to it), a
        // folder only when it is a junction or link. Copies and anything else are left alone.
        void UseGameFoldersDirectly(string baseDir, string updateDir)
        {
            RemoveGameLinks(App0Layout.Top(root, info.TitleId), baseDir, updateDir, Log);
            Log("The game reads its files from " + (updateDir != null ? updateDir + " and " : "") + baseDir + ".");
        }


        internal static void RemoveGameLinks(string app0, string baseDir, string updateDir, Action<string> Log)
        {
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var dirs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in new[] { baseDir, updateDir })
            {
                if (source == null) continue;
                foreach (var file in Directory.GetFiles(source))
                {
                    string name = Path.GetFileName(file);
                    if (!name.Equals("eboot.bin", StringComparison.OrdinalIgnoreCase)) files[name] = file;
                }
                foreach (var dir in Directory.GetDirectories(source))
                {
                    string name = Path.GetFileName(dir);
                    if (!name.Equals("sce_module", StringComparison.OrdinalIgnoreCase)) dirs[name] = dir;
                }
            }
            // GT Sport only uses its data archives when gt.idx is there; without it the game closes
            // right after its splash screen.
            if (!files.ContainsKey("gt.idx"))
                throw new Exception("Neither game folder has gt.idx. Pick the GT Sport folder (CUSA03220) and its update, " +
                                    "the folders with gt.idx and the gt*.vol files.");
            int removed = 0, kept = 0;
            foreach (var pair in files)
            {
                string old = Path.Combine(app0, pair.Key);
                if (!File.Exists(old)) continue;
                try
                {
                    if ((File.GetAttributes(old) & FileAttributes.ReparsePoint) != 0 || SameFile(old, pair.Value))
                    {
                        File.Delete(old);
                        removed++;
                    }
                    else kept++;
                }
                catch (Exception e) { kept++; Log("Note: could not remove the old link app0\\" + pair.Key + " (" + e.Message.TrimEnd('.') + ")."); }
            }
            foreach (var pair in dirs)
            {
                string old = Path.Combine(app0, pair.Key);
                if (!Directory.Exists(old)) continue;
                try
                {
                    // Deleting a junction or folder link removes the link, never what it points to.
                    if ((File.GetAttributes(old) & FileAttributes.ReparsePoint) != 0) { Directory.Delete(old, false); removed++; }
                    else kept++;
                }
                catch (Exception e) { kept++; Log("Note: could not remove the old link app0\\" + pair.Key + " (" + e.Message.TrimEnd('.') + ")."); }
            }
            if (removed > 0) Log("Removed " + removed + " links to the game's files from app0; the game reads them from their own folders now.");
            if (kept > 0) Log("Note: app0 still has " + kept + " of the game's files or folders that are not links to them; they are used before the game folders.");
        }


        void PlayGts()
        {
            if (!File.Exists(Exe)) { MessageBox.Show(this, "Press Set up first."); return; }
            try { ApplyNativeRendererDefault(); ApplyExperimentalRenderScale(); ResolveVulkanDevice(); }
            catch (Exception e) { MessageBox.Show(this, e.Message); return; }
            // Without its data index GT Sport closes right after the splash screen.
            string dataIndex = GameFile("gt.idx");
            string dataProblem = dataIndex == null ? "is missing" : ReadProblem(dataIndex, -1);
            if (dataProblem != null)
            {
                MessageBox.Show(this, "GT Sport's data index gt.idx " + dataProblem + ", so the game can't start.\n\n" +
                                "Check that the game and update folders on the Game page still exist, then press Set up again.");
                return;
            }
            // The one-time steps below can take minutes (copying shaders, preparing them), so they run
            // beside the window, which keeps showing their progress.
            string gameDir = settings.Game;
            string updateDir = settings.Update.Length > 0 && Directory.Exists(settings.Update) ? settings.Update : null;
            busy = true;
            RefreshState();
            bool verified = false;
            var thread = new Thread(delegate ()
            {
                Exception failure = null;
                try
                {
                    SetStatus("Checking the game executable...");
                    GameBuildIntegrity.Ensure(Exe, delegate {
                        Log("GTSport.exe has changed or has no SHA-256 build record. Rebuilding from your game files...");
                        SetStatus("Rebuilding the game executable...");
                        RunSetupGts();
                    });
                    if (OldShaderCacheExists()) SetStatus("Moving your shaders to the shaders folder (once)...");
                    UsePortableShaderCache();
                    // Repair the old 16 GB profile even when Settings was never opened.
                    if (ReadIntSetting("GPU", "system_ram_gb", 0) == 16 &&
                        !ReadSetting("Vulkan", "pipeline_cache_enabled", true))
                        WriteSetting("Vulkan", "pipeline_cache_enabled", true);
                    RequireGameClosedForShaderPreparation();
                    verified = true;
                }
                catch (Exception e) { failure = e; Log("ERROR: " + e.Message); }
                BeginInvoke(new Action(delegate
                {
                    busy = false;
                    RefreshState();
                    if (!verified)
                    {
                        status.Text = "Game verification/rebuild failed; see the log. The game was not started.";
                        ShowSetupDumpHelp(failure);
                        return;
                    }
                    // PERF-210: Anti-Lag only without frame generation (FSR or DLSS).
                    bool antiLag = ReadSetting("GPU", "anti_lag", false) &&
                                   !ReadSetting("GPU", "framegen_enabled", false) &&
                                   !ReadSetting("GPU", "dlssg_enabled", false);
                    StartGameGts(root, settings.HddStreaming, experimentalSceneSize, antiLag,
                              ReadSetting("GPU", "shader_pretranslate", true), CustomEnvironment.Copy(settings.Env));
                    status.Text = "GT Sport is starting. Logs: logs\\stdout.log and logs\\stderr.log.";
                }));
            });
            thread.IsBackground = true;
            thread.Start();
        }


        /// Starts GTSport.exe once, with session log rotation; after a GPU device loss it is not started
        /// again (FIX-GTS-491), the setup log says so.
        static void StartGameGts(string root, bool hddStreaming, Size? sceneSize, bool antiLag, bool shaderPretranslate, List<EnvVar> customEnv)
        {
            string exe = Path.Combine(root, "GTSport.exe");
            // Preserve GT Sport metadata while migrating the launcher publisher field from older installs.
            try { GameExe.ApplyVersionInfo(exe); } catch (Exception e) { AppendSetupLog(root, GameInfo.GTSport.TitleId, "Could not update game publisher metadata: " + e.Message); }
            // Folders set up by an earlier RyGT launcher get the icon and Windows subsystem now.
            try { if (!GameExe.IsPrepared(exe)) GameExe.Prepare(exe); } catch { }
            ThreadPool.QueueUserWorkItem(delegate { AmdSoftware.EnsureGameEntry(root, exe); });
            ThreadPool.QueueUserWorkItem(delegate
            {
                string logDirectory;
                try
                {
                    logDirectory = LogFiles.PrepareRuntimeLogs(root, GameInfo.Of(GameKind.GTSport).TitleId);
                }
                catch (Exception e)
                {
                    AppendSetupLog(root, GameInfo.GTSport.TitleId, "Could not prepare game logs: " + e.Message);
                    return;
                }

                Process started;
                try
                {
                    var start = new ProcessStartInfo(exe)
                    { WorkingDirectory = root, UseShellExecute = false };
                    RenderResolution.ConfigureStart(start, root, sceneSize);
                    start.EnvironmentVariables["RYGT_LOG_DIRECTORY"] = logDirectory;
                    // GAME-004: the renderer and the runtime take the game from the launcher.
                    start.EnvironmentVariables["SHADGT_GAME"] = "gts";
                    start.EnvironmentVariables["RYGT_HDD_STREAMING"] = hddStreaming ? "1" : "0";
                    start.EnvironmentVariables["SHADGT_ANTI_LAG"] = antiLag ? "1" : "0"; // PERF-210
                    // FIX-RT-028: shader objects are always off (the launcher's option is gone); the Advanced
                    // page, applied below, can still turn them on for a test.
                    start.EnvironmentVariables["SHADGT_SHADER_OBJECT"] = "0";
                    // FEAT-GTS-445 from the Shaders card. Pre-translation: each shader translation leaves
                    // a record (shaders\<serial>.manifest) and its code (shaders\<serial>.capture, this
                    // PC's only); at start, the ones the shader store lacks (after a renderer update)
                    // are translated again in the background.
                    // FEAT-GTS-285: on by default (the renderer's default too), so unchecked turns
                    // it off.
                    if (shaderPretranslate)
                    {
                        start.EnvironmentVariables["SHADGT_MANIFEST_RECORD"] = "1";
                        start.EnvironmentVariables["SHADGT_MANIFEST_PRETRANSLATE"] = "1";
                    }
                    else
                    {
                        start.EnvironmentVariables["SHADGT_MANIFEST_PRETRANSLATE"] = "0";
                    }
                    // FEAT-GTS-433: the user's list last, so it wins; only the count is logged.
                    int custom = ConfigureDisplayLaunch(start, root, GameInfo.GTSport, customEnv);
                    if (custom > 0) AppendSetupLog(root, GameInfo.GTSport.TitleId, custom + " custom environment variable(s) applied.");
                    started = Process.Start(start);
                }
                catch (Exception e)
                {
                    AppendSetupLog(root, GameInfo.GTSport.TitleId, "Could not start GT Sport: " + e.Message);
                    return;
                }
                if (started == null) return;
                using (Process game = started) game.WaitForExit();

                // FIX-GTS-491: after a GPU device loss the game is not started again. The interrupted
                // session could not be resumed anyway, and the restart hid the crash from the player
                // and ran a second session under the first one's launch (tests mixed the two).
                if (RendererReportedDeviceLoss(logDirectory))
                    AppendSetupLog(root, GameInfo.GTSport.TitleId, "Vulkan lost the GPU device (" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "); GT Sport was closed and is not started again. Its log: " + Path.Combine(logDirectory, "stdout.log") + ", renamed stdout-<UTC time>.log at the next start (the first one after this time).");
            });
        }
    }
}
