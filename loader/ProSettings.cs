// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;

namespace RyGT
{
    partial class LoaderForm
    {
        static readonly string[] ProModes = { "off", "4k", "1080p", "full", "custom" };
        const string Native4kGuide = "For native 4K, choose PS4 Pro, 1080p TV, set upscaling to Off and choose 3840 × 2160 under Display → Render resolution.";
        const string CheckerboardWarning = "PS4 Pro, 4K TV uses the game's checkerboard reconstruction. DLSS and custom render resolutions are unavailable with checkerboard on.";
        const string DlaaLabel = "DLAA (native resolution)";
        const string DlaaGuide = "DLAA applies NVIDIA anti-aliasing at the render resolution. Use base PS4 mode and matching 16:9 render and window sizes to test it.";
        const string UseDlssGuide = "For DLSS, select PS4 (no Pro mode) and set Display → Render resolution to Game default.";

        sealed class ProOption
        {
            public readonly string Key, Token, Label;
            public readonly bool Gt7Only;
            public ProOption(string token, string label, bool gt7Only = false)
            {
                Key = "ps4_pro_" + token;
                Token = token == "replay_blur" ? "replayblur" : token;
                Label = label;
                Gt7Only = gt7Only;
            }
        }

        // The GT7 preset copy supports five effects. GTS has three separate Pro/TV gates (NEO-GTS-506).
        static readonly ProOption[] ProOptions = {
            new ProOption("checkerboard", "3200 x 1800 checkerboard with the game's 4K interface"),
            new ProOption("blur", "Motion blur in races"),
            new ProOption("replays60", "Replays and photo mode at 60 FPS"),
            new ProOption("replay_blur", "Extra blur in replays and photo mode", true),
            new ProOption("garage60", "High-quality garage scenes at 60 FPS", true),
            new ProOption("lod", "Extend level-of-detail distance by 1.3×")
        };

        static IEnumerable<ProOption> ProOptionsFor(GameKind game)
        {
            return ProOptions.Where(option => !option.Gt7Only || game == GameKind.GT7);
        }

        string SavedProMode()
        {
            return ReadStringSetting("GPU", "ps4_pro_mode",
                target == GameKind.GTSport && ReadSetting("GPU", "ps4_pro", false) ? "1080p" : "off");
        }

        static bool UsesCheckerboard(string mode, bool box)
        {
            return mode == "4k" || mode == "full" || (mode == "custom" && box);
        }

        static void ConfigurePro(ProcessStartInfo start, GameKind game, Func<string, object> setting)
        {
            bool legacy = game == GameKind.GTSport && Equals(setting("ps4_pro"), true);
            string mode = setting("ps4_pro_mode") as string ?? (legacy ? "1080p" : "off");
            if (mode == "off" || !ProModes.Contains(mode)) return;
            bool tv1080 = mode == "1080p";
            bool pro = tv1080 || UsesCheckerboard(mode, Equals(setting("ps4_pro_checkerboard"), true));
            var env = start.EnvironmentVariables;
            env["RYGT_NEO"] = env["SHADGT_NEO"] = pro ? "1" : "0";
            if (game == GameKind.GTSport) env["SHADGT_PS4_PRO"] = tv1080 ? "1080" : pro ? "2160" : "0";
            string extras = null;
            if (mode == "custom" || mode == "full") {
                var enabled = ProOptionsFor(game).Skip(1).Where(o => mode == "full" || Equals(setting(o.Key), true));
                extras = string.Join(",", enabled.Select(o => o.Token));
                if (extras.Length == 0) extras = "none";
            }
            // Both switch spellings exist in released renderers. Stock TV modes use the game's own effects.
            foreach (string prefix in new[] { "SHADGT_", "RYGT_" }) {
                if (tv1080) env[prefix + "NEO_TV"] = "1080"; else env.Remove(prefix + "NEO_TV");
                if (extras != null) env[prefix + "NEO_EXTRAS"] = extras; else env.Remove(prefix + "NEO_EXTRAS");
            }
        }

        static string EffectiveProMode(ProcessStartInfo start, GameKind game, bool legacy)
        {
            var env = start.EnvironmentVariables;
            if (game == GameKind.GTSport) {
                string value = env["SHADGT_PS4_PRO"];
                if (value != null) return value == "1080" ? "1080p" : value == "2160" || value == "1" ? "4k" : "off";
                if (env["SHADGT_NEO"] != "1") return legacy ? "1080p" : "off";
            } else if (env["RYGT_NEO"] != "1") return "off";
            return env["SHADGT_NEO_TV"] == "1080" ? "1080p" : "4k";
        }

        CheckBox AddProSettings(TableLayoutPanel pro, Func<bool> dlssSelected, Label methodProWarning,
                                out Action refreshWarning)
        {
            var proKeys = ProModes;
            var proMode = new DarkComboBox { Name = "ProMode", DropDownStyle = ComboBoxStyle.DropDownList, Width = 320, FlatStyle = FlatStyle.Flat };
            proMode.Items.AddRange(new object[] { "PS4 (no Pro mode)", "PS4 Pro, 4K TV", "PS4 Pro, 1080p TV", "Full (everything on)", "Custom" });
            proMode.SelectedIndex = Math.Max(0, Array.IndexOf(proKeys, SavedProMode()));
            var proNote = Hint("");
            pro.Controls.Add(new SettingRow("Mode", proMode));
            pro.Controls.Add(proNote);
            // FEAT-GT7-180: NVIDIA DLSS and the 3200 x 1800 checkerboard picture are not offered together. The
            // game rebuilds that picture from several frames itself, and DLSS on top cost about 6 ms of GPU time
            // a frame on an RTX 4070 (the only GPU it was measured on). Picking one while the other is on is
            // refused with a message; a configuration saved before with both gets a warning in both sections.
            // The rule is this page's: a RYGT_NEO set from the Advanced page's own variables is not checked.
            var proDlssWarning = Hint("");
            proDlssWarning.Name = "ProReconstructionWarning";
            proDlssWarning.ForeColor = Dark.AccentText;
            proDlssWarning.Visible = false;
            pro.Controls.Add(proDlssWarning);
            int lastPro = proMode.SelectedIndex;
            bool proReverting = false;
            var options = ProOptionsFor(target);
            var proBoxes = options.Select(option => {
                string label = target == GameKind.GTSport && option.Token == "replays60"
                    ? "60 FPS in reduced-rate views" : option.Label;
                var box = AddOption(pro, label, "GPU", option.Key, false);
                box.Name = option.Key;
                return box;
            }).ToArray();
            pro.Controls.Add(Hint("Change individual effects to create a custom preset. Changes apply at the next game start."));
            bool proApplying = false;
            // FEAT-GT7-180: GPU.dlss_enabled can be saved on while the list shows another method (the NVIDIA files
            // are missing). Turning the 3200 x 1800 picture on then switches that setting off, or adding the
            // files later would bring both on without a word.
            Action proDropUnshownDlss = delegate
            {
                if (dlssSelected() || !ReadSetting("GPU", "dlss_enabled", false)) return;
                try { WriteSetting("GPU", "dlss_enabled", false); }
                catch (Exception e) { MessageBox.Show(this, "Could not save the setting: " + e.Message); }
            };
            Action proDlssRefresh = delegate
            {
                string warning = proBoxes[0].Checked && dlssSelected()
                    ? "Settings conflict. Turn DLSS off to keep the game's 4K TV checkerboard mode. " + UseDlssGuide
                    : CheckerboardWarning + " " + UseDlssGuide;
                proDlssWarning.Text = methodProWarning.Text = warning;
                proDlssWarning.Visible = methodProWarning.Visible = proBoxes[0].Checked;
            };
            Action proRefresh = delegate
            {
                proDlssRefresh();
                if (refreshSceneChoice != null) refreshSceneChoice();
                int p = proMode.SelectedIndex;
                proNote.Visible = !proBoxes[0].Checked || p == 3;
                proNote.Text = p == 0 ? "Base PS4. Choose the render resolution in Display."
                             : p == 1 ? "PS4 Pro on a 4K TV. The game sets the render resolution for checkerboard rendering."
                             : p == 2 ? "PS4 Pro on a 1080p TV. Choose the render resolution in Display. The effects below are enabled."
                             : p == 3 ? "Everything on: the 4K TV picture with the 1080p TV's settings. The heaviest."
                             : proBoxes[0].Checked ? "Your own choice of the boxes below, in the game's PS4 Pro mode (checkerboard)."
                                                   : "Your own choice of the boxes below, in the PS4 mode (no checkerboard).";
            };
            // The boxes a mode ticks: 4K TV the checkerboard alone, 1080p TV the other effects, Full all options.
            Action proApply = delegate
            {
                int p = proMode.SelectedIndex;
                if (p == 4) return;
                proApplying = true;
                for (int i = 0; i < proBoxes.Length; i++)
                    proBoxes[i].Checked = p == 3 || (p == 1 && i == 0) || (p == 2 && i != 0);
                proApplying = false;
            };
            // The mode the boxes make: none PS4, the checkerboard alone 4K TV, the other effects 1080p TV, all options
            // Full, anything else Custom (keeping the boxes as they are).
            Func<int> proModeOfBoxes = delegate
            {
                int others = 0;
                for (int i = 1; i < proBoxes.Length; i++) if (proBoxes[i].Checked) others++;
                bool checkerboard = proBoxes[0].Checked;
                if (!checkerboard && others == 0) return 0;
                if (checkerboard && others == 0) return 1;
                if (!checkerboard && others == proBoxes.Length - 1) return 2;
                if (checkerboard && others == proBoxes.Length - 1) return 3;
                return 4;
            };
            foreach (var box in proBoxes)
            {
                var changed = box;
                box.CheckedChanged += delegate
                {
                    if (proApplying) return;
                    if (changed == proBoxes[0] && changed.Checked && dlssSelected())
                    {
                        // FEAT-GT7-180: not with NVIDIA DLSS. The box is unticked before the message (its own
                        // handler has already saved it ticked), and the mode stays as it is: Custom stays Custom.
                        proApplying = true;
                        changed.Checked = false;
                        proApplying = false;
                        MessageBox.Show(this, CheckerboardWarning + " Turn DLSS off first to use checkerboard.", "PS4 Pro and DLSS");
                        return;
                    }
                    if (changed == proBoxes[0] && changed.Checked) proDropUnshownDlss();
                    int boxesMode = proModeOfBoxes();
                    if (boxesMode != proMode.SelectedIndex) proMode.SelectedIndex = boxesMode;
                    else proRefresh();
                };
            }
            proMode.SelectedIndexChanged += delegate
            {
                int picked = proMode.SelectedIndex;
                // FEAT-GT7-180: the modes with the checkerboard (4K TV, Full) are refused while NVIDIA DLSS is the
                // upscaling method. Custom keeps the boxes as they are, and the box has its own check.
                if (!proReverting && (picked == 1 || picked == 3) && dlssSelected() && !proBoxes[0].Checked)
                {
                    MessageBox.Show(this, CheckerboardWarning + " Turn DLSS off first to use this mode.", "PS4 Pro and DLSS");
                    proReverting = true;
                    proMode.SelectedIndex = lastPro;
                    proReverting = false;
                    return;
                }
                lastPro = picked;
                if (picked == 1 || picked == 3) proDropUnshownDlss();
                try {
                    WriteSetting("GPU", "ps4_pro_mode", proKeys[picked]);
                    if (target == GameKind.GTSport)
                        WriteSetting("GPU", "ps4_pro", picked == 2 || picked == 1 || picked == 3 ||
                                     (picked == 4 && proBoxes[0].Checked));
                }
                catch (Exception e) { MessageBox.Show(this, "Could not save the setting: " + e.Message); }
                proApply();
                proRefresh();
            };
            proApply();
            proRefresh();
            refreshWarning = proDlssRefresh;
            return proBoxes[0];
        }
    }
}
