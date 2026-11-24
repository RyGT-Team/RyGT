// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Drawing;
using System.Diagnostics;
using System.IO;
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
            "game", "display", "1280x720", "1600x900", "1920x1080", "2560x1440", "3200x1800", "3840x2160"
        };

        public static Size? NativeSize(string mode, Size output)
        {
            if (mode == "display") {
                // Keep the guest's 16:9 scene inside the output. Display modes and camera
                // aspect are independent; UHD is the largest validated guest allocation.
                int units = Math.Min(240, Math.Min(output.Width / 16, output.Height / 9));
                if (units < 1) throw new ArgumentOutOfRangeException("output");
                return new Size(units * 16, units * 9);
            }
            if (Array.IndexOf(NativeModes, mode) < 2) return null;
            string[] parts = mode.Split('x');
            return new Size(int.Parse(parts[0]), int.Parse(parts[1]));
        }

        static string Immediate(int rva, string opcode, int original, int replacement)
        {
            return rva.ToString("x") + ":" + opcode + Word(original) + ":" + opcode + Word(replacement);
        }

        static string[] NativePatches(bool gt7, Size size)
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
            return new[] {
                gt7 ? "17a7432:813dd8811b0439040000:813dd8811b04ffffff7f"
                    : "1c6dea0:8b0d8696f500:b93804000090",
                Immediate(gt7 ? 0x17a7416 : 0x1c6dee5, gt7 ? "bb" : "41bf", width, size.Width),  // flip
                Immediate(gt7 ? 0x17a741b : 0x1c6deeb, gt7 ? "41bf" : "41bc", height, size.Height),
                Immediate(gt7 ? 0x17a7426 : 0x1c6dec9, gt7 ? "ba" : "c7055596f500", width, size.Width), // scene
                Immediate(gt7 ? 0x17a7421 : 0x1c6ded3, gt7 ? "b9" : "c7054f96f500", height, size.Height),
                (gt7 ? "27cadc6" : "1988ad3") + ":48b9" + extent + ":48b9" + target,
                Immediate(gt7 ? 0x27caa03 : 0x19885dd, "b8", pool, pool * poolCount),
                Immediate(gt7 ? 0x27cabf9 : 0x198899f, "05", 2048, stride),
                Immediate(gt7 ? 0x27cac03 : 0x19889b1, gt7 ? "c74604" : "c745b4", 2 * height, 2 * Math.Max(height, size.Height))
            };
        }

        public static void ConfigureNative(ProcessStartInfo start, string root, bool gt7, string patchVariable, Size size)
        {
            if (size.Width < 16 || size.Width > 3840 || size.Height < 9 || size.Height > 2160 ||
                size.Width * 9 != size.Height * 16) throw new ArgumentOutOfRangeException("size");
            if (!String.IsNullOrEmpty(start.EnvironmentVariables["SHADGT_PATCH"]) ||
                !String.IsNullOrEmpty(start.EnvironmentVariables["RYGT_PATCH"]))
                throw new InvalidOperationException("Native resolution conflicts with another game patch or scene scale.");
            if (size != new Size(1920, 1080)) {
                var patches = NativePatches(gt7, size);
                using (var exe = new BinaryReader(File.OpenRead(Path.Combine(root, gt7 ? "GT7.exe" : "GTSport.exe")))) {
                    foreach (var patch in patches) {
                        var parts = patch.Split(':');
                        RequireSignature(exe, Convert.ToInt32(parts[0], 16), parts[1]);
                    }
                }
                start.EnvironmentVariables[patchVariable] = String.Join(",", patches);
            }
            // The larger pools are on the base-console path in both games. Keep the Pro
            // checkerboard and independent viewport experiments out of this process.
            start.EnvironmentVariables["RYGT_NEO"] = "0";
            start.EnvironmentVariables["SHADGT_NEO"] = "0";
            start.EnvironmentVariables["SHADGT_PS4_PRO"] = "0";
            start.EnvironmentVariables["SHADGT_WIDE_SCENE"] = "0";
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
