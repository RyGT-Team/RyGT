// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
namespace RyGT
{
    internal static class RenderResolution
    {
        public static Size Calculate(int outputWidth, int outputHeight, string preset)
        {
            if (outputWidth <= 0 || outputHeight <= 0) throw new ArgumentOutOfRangeException("outputWidth");
            double ratio;
            switch (preset) {
                case "ultra_performance": ratio = 1.0 / 3.0; break;
                case "performance": ratio = 0.5; break;
                case "balanced": ratio = 1.0 / 1.7; break;
                case "quality": ratio = 1.0 / 1.5; break;
                case "ultra_quality": ratio = 1.0 / 1.3; break;
                case "dlaa": ratio = 1.0; break;
                default: throw new ArgumentException("Unknown render-scale preset.", "preset");
            }
            // The scene is 16:9; output/camera aspect remains independently configured.
            double height = Math.Min(outputHeight, outputWidth * 9.0 / 16.0) * ratio;
            int units = Math.Max(20, Math.Min(240, (int)Math.Round(height / 9.0)));
            return new Size(units * 16, units * 9);
        }

        // Only read the section table and the instruction bytes needed by this profile.
        static void RequireSignature(BinaryReader pe, int rva, string hex)
        {
            var file = pe.BaseStream;
            file.Position = 0x3c;
            long header = pe.ReadUInt32();
            file.Position = header + 6;
            int sections = pe.ReadUInt16();
            file.Position = header + 20;
            long table = header + 24 + pe.ReadUInt16();
            for (int i = 0; i < sections; ++i) {
                file.Position = table + i * 40 + 12;
                long va = pe.ReadUInt32();
                long size = pe.ReadUInt32();
                long raw = pe.ReadUInt32();
                if (rva < va || rva + hex.Length / 2 > va + size) continue;
                file.Position = raw + rva - va;
                for (int j = 0; j < hex.Length; j += 2) {
                    if (pe.ReadByte() != Convert.ToByte(hex.Substring(j, 2), 16))
                        throw new InvalidOperationException("This executable does not match the resolution profile at RVA " + rva.ToString("x") + ".");
                }
                return;
            }
            throw new InvalidOperationException("Resolution patch site is missing.");
        }

        static string Word(int value)
        {
            return BitConverter.ToString(BitConverter.GetBytes(value)).Replace("-", "").ToLowerInvariant();
        }

        internal static readonly string[] NativeModes = {
            "game", Auto, "1280x720", "1600x900", "1920x1080", "2560x1080", "3840x1080",
            "2560x1440", "3440x1440", "5120x1440", "3840x1600", "3200x1800", "3840x2160"
        };

        public const string Auto = "auto";
        public const string UltrawideWindow = "ultrawide_window";

        public static bool SupportsWideScene(Size size)
        {
            // Use the same width alignment and UHD pixel budget as GT Sport's True wide.
            return size.Width >= 640 && size.Width <= 7680 && size.Width % 16 == 0 &&
                size.Height >= 360 && size.Height <= 2160 &&
                (double)size.Width / size.Height > 16.0 / 9 + 0.01 && size.Width < size.Height * 6 &&
                (long)size.Width * size.Height <= 3840L * 2160;
        }

        public static Size? NativeSize(string mode, Size output)
        {
            if (mode == UltrawideWindow) return SupportsWideScene(output) ? (Size?)output : null;
            if (mode == Auto) {
                if (SupportsWideScene(output)) return output;
                if (output.Height >= 360 && IsWideOutput(output) && output.Width < (long)output.Height * 6) {
                    Size fit = TrueWideSize(output, Math.Min(2160, output.Height));
                    // Width alignment can round the last row over the allocation budget.
                    if ((long)fit.Width * fit.Height > 3840L * 2160)
                        fit = TrueWideSize(output, fit.Height - 1);
                    if (SupportsWideScene(fit)) return fit;
                }
                // Narrow windows and unsupported aspect ratios keep the game's 16:9 frame.
                int units = Math.Min(240, Math.Min(output.Width / 16, output.Height / 9));
                if (units < 1) throw new ArgumentOutOfRangeException("output");
                return new Size(units * 16, units * 9);
            }
            string[] parts = (mode ?? "").Split('x');
            int width, height;
            if (parts.Length != 2 || !int.TryParse(parts[0], out width) || !int.TryParse(parts[1], out height))
                return null;
            var size = new Size(width, height);
            bool standard = width >= 640 && width <= 3840 && height >= 360 && height <= 2160 &&
                            width * 9 == height * 16;
            return standard || SupportsWideScene(size) ? (Size?)size : null;
        }

        static double ConfiguredAspect(string value)
        {
            if (String.IsNullOrEmpty(value)) return 0;
            string[] parts = value.Split(':');
            double width, height = 1;
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            var style = System.Globalization.NumberStyles.Float;
            if (parts.Length > 2 || !double.TryParse(parts[0], style, culture, out width) ||
                (parts.Length == 2 && !double.TryParse(parts[1], style, culture, out height)) || height <= 0) return 0;
            return width / height;
        }

        // FEAT-RT-042: RYGT_ASPECT / SHADGT_ASPECT for GT7's saved screen shape. "auto" becomes the output's
        // own width:height (the window, or the desktop for borderless full screen), so the runtime and the
        // renderer get one number; an unknown or 16:9 shape is the game's 16:9.
        public static string ScreenShape(string saved, Size output)
        {
            string shape = saved == "auto" ? output.Width + ":" + output.Height : saved;
            return shape != "auto" && MatchOutputShape(shape) ? shape : "16:9";
        }

        public static bool MatchOutputShape(string value)
        {
            double ratio = ConfiguredAspect(value);
            return value == "auto" || (ratio > 0.5 && ratio < 6 && Math.Abs(ratio - 16.0 / 9) >= 0.01);
        }

        public static string ModeKey(Size size)
        {
            return size.Width + "x" + size.Height;
        }

        public static string WideOutputWarning(Size scene, Size output)
        {
            // Wide frames fill the window. Account for the compositor's 16-pixel alignment.
            if (Math.Abs(scene.Width - (double)output.Width * scene.Height / output.Height) <= 8)
                return null;
            return "Choose a window or display with the same aspect ratio as " +
                   scene.Width + " × " + scene.Height + " to keep the picture in proportion.";
        }

        // FIX-RT-048: the game's own 1920 x 1080 scene outside Pro mode needs no patch: it is the
        // game default (Auto in a 1920 x 1080 window), and nothing conflicts with it.
        public static bool IsGameDefault(Size? scene, string pro)
        {
            return !scene.HasValue || (scene.Value == new Size(1920, 1080) && pro != "1080p");
        }

        // FIX-RT-048: why Play refuses this render resolution, or null. One decision for the Display
        // page's preview and for the launch, so the page never shows as accepted a choice that Play
        // then refuses. pro is "off", "1080p" or "4k" (in 4k the game sets the scene itself).
        public static string Refusal(bool gts, Func<string, object> setting, Size? scene, Size output, string pro)
        {
            if (pro == "4k" || IsGameDefault(scene, pro)) return null;
            if (Equals(setting("fsr_enabled"), true) || Equals(setting("dlss_enabled"), true))
                return "Turn off upscaling (DLSS, FSR) on the Graphics page to use this render resolution, or set Render resolution to Game default.";
            if (Equals(setting("native_renderer"), false) || Equals(setting("experimental_render_scale"), true))
                return "Choose the Native renderer and turn off experimental viewport scaling for a fixed render resolution.";
            if (!SupportsWideScene(scene.Value)) return null;
            string shape = WideOutputWarning(scene.Value, output);
            if (shape != null) return shape;
            return !gts && pro != "off" ? "Choose base PS4 mode to use native ultrawide rendering in GT7." : null;
        }

        public static string SavedMode(bool gts, Func<string, object> setting, Size output)
        {
            string mode = setting("native_resolution") as string ?? (Equals(setting("native_4k"), true) ? "3840x2160" : "game");
            if (mode == UltrawideWindow)
                mode = SupportsWideScene(output) ? ModeKey(output) : setting("native_resolution_16_9") as string ?? "game";
            if (mode == Auto) return mode;
            Size? size = NativeSize(mode, output);
            if (size.HasValue && SupportsWideScene(size.Value)) return mode;
            // Older GTS profiles stored the height separately and derived the width at launch.
            if (gts && Equals(setting("wide_scene"), true) && !Equals(setting("wide_scene_alloc"), false) &&
                !Equals(setting("native_renderer"), false) && !Equals(setting("ui_hires"), false) &&
                MatchOutputShape(setting("aspect_ratio") as string) && IsWideOutput(output)) {
                int height;
                int.TryParse(Convert.ToString(setting("wide_scene_height")), out height);
                return ModeKey(TrueWideSize(output, height));
            }
            return mode == "display" ? Auto : mode;
        }

        public static bool IsWideOutput(Size output)
        {
            return (double)output.Width / output.Height > 16.0 / 9.0 + 0.01;
        }

        // shadGT WIDE-001 rounds to 16 pixels and caps the scene at UHD's pixel count.
        public static Size TrueWideSize(Size output, int height)
        {
            double aspect = (float)((double)output.Width / output.Height);
            if (height < 360 || height > 2160) height = 1080;
            if (height * (double)height * aspect > 3840.0 * 2160)
                height = (int)Math.Sqrt(3840.0 * 2160 / aspect) & ~7;
            int width = (int)Math.Round(height * aspect / 16.0, MidpointRounding.AwayFromZero) * 16;
            return new Size(Math.Min(7680, width), height);
        }

        static string Immediate(int rva, string opcode, int original, int replacement)
        {
            return rva.ToString("x") + ":" + opcode + Word(original) + ":" + opcode + Word(replacement);
        }

        static List<string> NativePatches(bool gt7, Size size, bool pro1080)
        {
            const int width = 1920, height = 1080;
            string extent = Word(width) + Word(height);
            string target = Word(size.Width) + Word(size.Height);
            int pool = gt7 ? 0xb000000 : 0xe800000;
            int poolCount = (size.Width * size.Height + width * height - 1) / (width * height);
            // The base UI scratch surface has a 2048-wide stride and two stacked heights.
            // Keep at least the original space for fixed-size UI passes, and round the scaled stride up.
            int stride = Math.Max(2048, ((size.Width * 2048 + width * 256 - 1) / (width * 256)) * 256);
            // Ghidra: both base paths allocate the scene, flip and screen targets independently.
            // Their TV/AA selectors and base pool sizes differ. Both UI allocators stack two surfaces.
            var patches = new List<string> {
                gt7 ? "17a7432:813dd8811b0439040000:813dd8811b04ffffff7f"
                    : "1c6dea0:8b0d8696f500:b93804000090",
                Immediate(gt7 ? 0x17a7416 : 0x1c6dee5, gt7 ? "bb" : "41bf", width, size.Width),  // flip
                Immediate(gt7 ? 0x17a741b : 0x1c6deeb, gt7 ? "41bf" : "41bc", height, size.Height),
                Immediate(gt7 ? 0x17a7426 : 0x1c6dec9, gt7 ? "ba" : "c7055596f500", width, size.Width), // scene
                Immediate(gt7 ? 0x17a7421 : 0x1c6ded3, gt7 ? "b9" : "c7054f96f500", height, size.Height),
                (gt7 ? "27cadc6" : "1988ad3") + ":48b9" + extent + ":48b9" + target,
                Immediate(gt7 ? 0x27caa03 : 0x19885dd, "b8", pool, pool * poolCount),
                // In Pro mode the game adds 2048 before this instruction. Keep its stock
                // 4096-wide scratch surface and avoid counting that offset twice.
                pro1080 ? (gt7 ? "27cabf9" : "198899f") + ":0500080000:b8" + Word(Math.Max(4096, stride))
                        : Immediate(gt7 ? 0x27cabf9 : 0x198899f, "05", 2048, stride),
                Immediate(gt7 ? 0x27cac03 : 0x19889b1, gt7 ? "c74604" : "c745b4", 2 * height, 2 * Math.Max(height, size.Height))
            };
            if (pro1080) {
                // TV height chooses the scene path; the console flag independently selects
                // the flip dimensions and pool. Keep the Pro flag and its 1080p effects.
                patches.Add(Immediate(gt7 ? 0x17a746d : 0x1c6dee0, "b8", 3840, size.Width));
                patches.Add(Immediate(gt7 ? 0x17a7481 : 0x1c6def5, "b8", 2160, size.Height));
                const int proPool = 0x1bb00000;
                patches.Add(Immediate(gt7 ? 0x27ca9f2 : 0x19885c8, "b9", proPool,
                                      proPool + pool * (poolCount - 1)));
            }
            return patches;
        }

        public static void ConfigureNative(ProcessStartInfo start, string root, bool gt7, string patchVariable, Size size, bool pro1080 = false)
        {
            bool wide = gt7 && SupportsWideScene(size);
            if (!wide && (size.Width < 16 || size.Width > 3840 || size.Height < 9 || size.Height > 2160 ||
                size.Width * 9 != size.Height * 16)) throw new ArgumentOutOfRangeException("size");
            if (!String.IsNullOrEmpty(start.EnvironmentVariables["SHADGT_PATCH"]) ||
                !String.IsNullOrEmpty(start.EnvironmentVariables["RYGT_PATCH"]))
                throw new InvalidOperationException("Native resolution conflicts with another game patch or scene scale.");
            if (pro1080 || size != new Size(1920, 1080)) {
                var patches = NativePatches(gt7, size, pro1080);
                using (var exe = new BinaryReader(File.OpenRead(Path.Combine(root, gt7 ? "GT7.exe" : "GTSport.exe")))) {
                    foreach (var patch in patches) {
                        var parts = patch.Split(':');
                        RequireSignature(exe, Convert.ToInt32(parts[0], 16), parts[1]);
                    }
                }
                start.EnvironmentVariables[patchVariable] = String.Join(",", patches);
            }
            if (wide) {
                string aspect = size.Width + ":" + size.Height;
                start.EnvironmentVariables["RYGT_ASPECT"] = aspect;
                start.EnvironmentVariables["SHADGT_ASPECT"] = aspect;
            }
            start.EnvironmentVariables["SHADGT_EXPERIMENTAL_SCENE_VIEWPORT"] = "0";
            start.EnvironmentVariables["SHADGT_LOW_RES_COMPOSITION"] = "0";
        }

        public static void ConfigureStart(ProcessStartInfo start, string root, Size? scene)
        {
            start.EnvironmentVariables.Remove("SHADGT_EXPERIMENTAL_SCENE_VIEWPORT");
            start.EnvironmentVariables.Remove("SHADGT_SCENE_WIDTH");
            start.EnvironmentVariables.Remove("SHADGT_SCENE_HEIGHT");
            // Composition downscaling is an independent quality experiment, not an upscaler.
            start.EnvironmentVariables.Remove("SHADGT_LOW_RES_COMPOSITION");
            if (!scene.HasValue) return;
            if (!String.IsNullOrEmpty(start.EnvironmentVariables["SHADGT_PATCH"]))
                throw new InvalidOperationException("Unset SHADGT_PATCH before using launcher render scaling.");
            Size size = scene.Value;
            using (var pe = new BinaryReader(File.OpenRead(Path.Combine(root, "GTSport.exe")))) {
                RequireSignature(pe, 0x1988ad3, "48b98007000038040000");
                RequireSignature(pe, 0x1988ab0, "8b0d72ea2301");
                RequireSignature(pe, 0x1988ab9, "8b0d6dea2301");
            }
            string width = Word(size.Width), height = Word(size.Height);
            start.EnvironmentVariables["SHADGT_PATCH"] = "1988ad3:48b98007000038040000:48b9" + width + height +
                ",1988ab0:8b0d72ea2301:b9" + width + "90,1988ab9:8b0d6dea2301:b9" + height + "90";
            start.EnvironmentVariables["SHADGT_EXPERIMENTAL_SCENE_VIEWPORT"] = "1";
            start.EnvironmentVariables["SHADGT_SCENE_WIDTH"] = size.Width.ToString(System.Globalization.CultureInfo.InvariantCulture);
            start.EnvironmentVariables["SHADGT_SCENE_HEIGHT"] = size.Height.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
