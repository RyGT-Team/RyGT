// SPDX-License-Identifier: GPL-2.0-or-later
// FEAT-GTS-272: the launcher's updater. Three channels: Stable (the newest GitHub release), Prerelease
// (the newest GitHub release, pre-releases included) and Nightly (the newest successful CI build,
// downloaded through nightly.link because GitHub's own artifact download needs a signed-in
// account). It checks the package and installs its program files over this folder, keeping a
// backup to roll back to. User data is never written. FEAT-GTS-291: the one updater for both games: it
// updates the selected game's folder (GT Sport's RyGTS folder or GT7's ryGT7 folder) from the RyGTS
// releases, the same way for either (ryGT7's GitLab updater, Gt7Updater.cs, is gone).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace RyGT
{
    /// A build the launcher can update to.
    class ReleaseInfo
    {
        /// "2026-11-10" for a release, "ci-1a2b3c4d" for a nightly (FIX-RT-044: "ci-1a2b3c4d+5e6f7a8b", with
        /// the renderer's commit after the "+").
        public string Version, Tag, Name, Notes, ZipName, ZipUrl, Sha256;
        /// FIX-GTS-286: the commit the build is from (a nightly's run, a release's tag), or null.
        public string Commit;
        /// FIX-RT-044: a nightly's renderer (shadGT) commit, from its artifact's name; null for a release.
        public string RendererCommit;
        /// FIX-RT-044: when a nightly's run started (UTC, ISO 8601); null for a release.
        public string Built;
        /// A nightly: the download is a GitHub Actions artifact (a zip holding the package zip and
        /// its .sha256), which also gives the checksum.
        public bool Artifact;
        /// A GitHub pre-release.
        public bool Prerelease;

        /// The first lines of the notes' "What's new" (or of the notes), for the home view.
        /// FIX-GTS-286: the notes as a list of entries for the Changelog window: headings (level 0), paragraphs
        /// and bullets (1), and nested bullets (2), without the Markdown marks.
        public List<KeyValuePair<int, string>> Changes()
        {
            var entries = new List<KeyValuePair<int, string>>();
            foreach (var raw in (Notes ?? "").Replace("\r", "").Split('\n'))
            {
                if (raw.Trim().Length == 0) continue;
                string line = raw.TrimEnd();
                int indent = line.Length - line.TrimStart().Length;
                string text = line.Trim();
                int level;
                if (text.StartsWith("#")) { level = 0; text = text.TrimStart('#').Trim(); }
                else if (text.StartsWith("- ") || text.StartsWith("* ")) { level = indent >= 2 ? 2 : 1; text = text.Substring(2); }
                else if (entries.Count > 0 && indent >= 2) { var last = entries[entries.Count - 1]; entries[entries.Count - 1] = new KeyValuePair<int, string>(last.Key, last.Value + " " + Plain(text)); continue; }
                else level = 1;
                entries.Add(new KeyValuePair<int, string>(level, Plain(text)));
            }
            return entries;
        }

        static string Plain(string text)
        {
            text = Regex.Replace(text, @"\[([^\]]*)\]\([^)]*\)", "$1"); // [text](link)
            return Regex.Replace(text, @"\*\*|`", "");
        }

        public string Summary(int maxLength)
        {
            string text = Notes ?? "";
            int at = text.IndexOf("What's new", StringComparison.OrdinalIgnoreCase);
            if (at >= 0) text = text.Substring(at + 10);
            var lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("- ") || l.StartsWith("* ")).ToList();
            string summary = string.Join(" " + (char)183 + " ", lines.Select(l => Regex.Replace(l.Substring(2), @"\*\*|`", "")));
            if (summary.Length == 0) summary = Regex.Replace(text, @"\s+|\*\*|`|#", " ").Trim();
            return summary.Length <= maxLength ? summary : summary.Substring(0, maxLength - 1).TrimEnd() + (char)0x2026;
        }
    }

    static class Updater
    {
        // ---- Where the releases are.
        const string Repository = "RyGT-Team/RyGT", Workflow = "ci-package-release.yml", Branch = "development";
        // FIX-RT-044: renderer changes are packaged by shadGT's own runs (its ci-package.yml calls RyGT's
        // package workflow for each ryty-gpu push), which RyGT's run list doesn't show. The nightly is the
        // newer of the two.
        const string RendererRepository = "RyGT-Team/shadGT", RendererWorkflow = "ci-package.yml", RendererBranch = "ryty-gpu";
        const string NightlyPrefix = "RyGT-ci-"; // FIX-RT-015: RyGTS-ci- before
        // The package artifact both repositories' runs upload: ci-<RyGT commit>-renderer-<shadGT commit>.
        static readonly Regex NightlyArtifact = new Regex("^ci-([0-9a-f]{40})-renderer-([0-9a-f]{40})$");
        // FIX-GTS-286: the release tags ("gt-sport-2026-11-15") and package zips ("GTSport-Ryty-shadGT-2026-11-15.zip")
        // as they are published. FIX-RT-044: RyGT's releases are tagged rygt-<date> ("rygt-2026-11-23").
        static readonly string[] TagPrefixes = { "rygt-", "gt-sport-", "gts-" }, PackagePrefixes = { "GTSport-", "RyGTS-", "RyGT-" };
        const string Product = "RyGT"; // FIX-RT-015: packages are RyGT-<version> (RyGTS-<version> before)
        // FEAT-GTS-291: the launcher started after an update (RyGTS.exe and ryGT7's RyGT7.exe before it).
        public const string LauncherExe = "RyGTLauncher.exe";
        // Either game, in whichever folder is updated: its process, its settings and its converted game.
        static readonly string[] GameProcesses = { "GTSport", "GT7" };
        // FEAT-GTS-291: each game's settings are <serial>.RyGT.ini now; the old names stay protected for
        // folders not moved yet.
        static readonly string[] ProtectedFiles = { "CUSA03220.RyGT.ini", "CUSA24769.RyGT.ini", "RyGT.ini", "RyGT7.ini", "RyGT-Launcher.ini", "GTSport.exe", "GTSport.exe.converted",
                                                    "GT7.exe", "GT7.exe.converted", "GT7.registry.json", "setup-out.txt",
                                                    // FEAT-RT-006: RyGTS packages carry GT7's server; its shadNet settings hold
                                                    // the keys the Online page made for this player (EnsureShadNetKeys).
                                                    "server\\shadnet\\shadnet.cfg" };
        // ----

        public const string Stable = "stable", Prerelease = "prerelease", Nightly = "nightly";
        public static readonly string[] Channels = { Stable, Prerelease, Nightly };

        // Folders and files that hold the player's data or the converted game: an update never
        // writes, moves or deletes anything under them, whatever a package lists.
        static readonly string[] ProtectedFolders = { "user", "shaders", "app0", "sys", "_sd", "convert", "data", "logs", "temp",
                                                       "temp0", "download0", "update", "dlss", "Photos", "screenshots", "decals",
                                                       "launcher\\profiles" };
        // Written last, so a failed update still reports the old version.
        static readonly string[] VersionFiles = { "BUILD_INFO.txt", "BUILD_INFO.json", "manifest.json", "SHA256SUMS.txt" };

        public static string UpdateFolder(string root) { return Path.Combine(root, "update"); }

        /// The installed version: the version this updater installed (update\installed.json), while
        /// the installed build is still the one it recorded (a release can be a nightly repackaged,
        /// whose manifest still says "RyGT7-ci-..."); else a package's manifest.json name
        /// ("RyGT7-2026-11-10", "RyGT7-ci-1a2b3c4d"), or BUILD_INFO.txt's commit as a nightly. Null
        /// for a build that didn't come from a package.
        public static string InstalledVersion(string root)
        {
            try
            {
                string identity = BuildIdentity(root);
                if (identity == null) return null;
                string recordPath = Path.Combine(UpdateFolder(root), "installed.json");
                if (File.Exists(recordPath))
                {
                    var record = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(recordPath)) as Dictionary<string, object>;
                    object version, recorded;
                    if (record != null && record.TryGetValue("version", out version) && record.TryGetValue("build", out recorded) &&
                        string.Equals(recorded as string, identity, StringComparison.OrdinalIgnoreCase) && version is string)
                        return (string)version;
                }
                if (identity.StartsWith("commit:")) return WithRenderer("ci-" + identity.Substring(7, 8), root);
                var match = Regex.Match(identity, "^" + Regex.Escape(Product) + @"S?-(.+?)(-prerelease)?$"); // RyGT- or the older RyGTS-
                return match.Success ? WithRenderer(match.Groups[1].Value, root) : null;
            }
            catch { return null; }
        }

        /// FIX-RT-044: a nightly's version names its renderer too ("ci-1a2b3c4d+5e6f7a8b"), so builds of one
        /// RyGT commit with different renderers are told apart.
        static string WithRenderer(string version, string root)
        {
            if (!version.StartsWith("ci-") || version.Contains("+")) return version;
            string renderer = BuildRendererCommit(root);
            return renderer == null ? version : version + "+" + renderer.Substring(0, 8);
        }

        /// The folder's release as shown to the player: the installed version, else the version in another
        /// package's manifest.json name (a ryGT7 release from GitLab, "RyGT7-2026-10-30"). Null without one.
        public static string ReleaseVersion(string root)
        {
            string installed = InstalledVersion(root);
            if (installed != null) return installed;
            try
            {
                var match = Regex.Match(BuildIdentity(root) ?? "", @"^[A-Za-z0-9]+-(.+?)(-prerelease)?$");
                return match.Success ? match.Groups[1].Value : null;
            }
            catch { return null; }
        }

        /// What tells installed builds apart: manifest.json's name, else "commit:" + BUILD_INFO.txt's
        /// commit. Null when there is neither.
        static string BuildIdentity(string root)
        {
            try
            {
                string manifestPath = Path.Combine(root, "manifest.json");
                if (File.Exists(manifestPath))
                {
                    var manifest = new JavaScriptSerializer { MaxJsonLength = 16 << 20 }.DeserializeObject(File.ReadAllText(manifestPath)) as Dictionary<string, object>;
                    object name;
                    if (manifest != null && manifest.TryGetValue("name", out name) && !string.IsNullOrEmpty(name as string)) return (string)name;
                }
            }
            catch { }
            string commit = BuildCommit(root);
            return commit != null ? "commit:" + commit : null;
        }

        /// BUILD_INFO.txt's commit (CI and release packages built by the pipeline), or null.
        static string BuildCommit(string root)
        {
            try
            {
                string path = Path.Combine(root, "BUILD_INFO.txt");
                if (!File.Exists(path)) return null;
                var info = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(path)) as Dictionary<string, object>;
                object commit;
                // FIX-GTS-286: older packages name it source_commit.
                if (info == null || (!info.TryGetValue("commit", out commit) && !info.TryGetValue("source_commit", out commit))) return null;
                string text = commit as string ?? "";
                return Regex.IsMatch(text, "^[0-9a-f]{8,40}$") ? text : null;
            }
            catch { return null; }
        }

        /// FIX-RT-044: BUILD_INFO.txt's renderer commit: the second line of renderer_source
        /// ("https://github.com/RyGT-Team/shadGT.git\n<commit>"), or null.
        static string BuildRendererCommit(string root)
        {
            try
            {
                string path = Path.Combine(root, "BUILD_INFO.txt");
                if (!File.Exists(path)) return null;
                var info = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(path)) as Dictionary<string, object>;
                object source;
                if (info == null || !info.TryGetValue("renderer_source", out source)) return null;
                string[] lines = (source as string ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                string text = lines.Length == 2 ? lines[1].Trim() : "";
                return Regex.IsMatch(text, "^[0-9a-f]{40}$") ? text : null;
            }
            catch { return null; }
        }

        /// A version as shown to the player: a nightly ("ci-1a2b3c4d") as "nightly 1a2b3c4d", and
        /// FIX-RT-044 "ci-1a2b3c4d+5e6f7a8b" as "nightly 1a2b3c4d, renderer 5e6f7a8b".
        public static string Display(string version)
        {
            if (version == null) return "";
            if (!version.StartsWith("ci-")) return version;
            int plus = version.IndexOf('+');
            return plus < 0 ? "nightly " + version.Substring(3)
                            : "nightly " + version.Substring(3, plus - 3) + ", renderer " + version.Substring(plus + 1);
        }

        /// Releases are dates with an optional letter for a hotfix (2026-10-27b), so they sort as
        /// text. A nightly is offered when it is another commit than the installed build (the newest
        /// successful run is the one offered), and a release over a nightly and the other way round.
        /// FIX-GTS-286: a build from the same commit as the installed one is not newer, whatever its channel
        /// calls it (today's release can be the newest nightly).
        public static bool IsNewer(ReleaseInfo candidate, string installed, string root)
        {
            if (candidate == null) return false;
            string commit = BuildCommit(root);
            if (commit != null && !string.IsNullOrEmpty(candidate.Commit) && SameCommit(candidate.Commit, commit))
            {
                // FIX-RT-044: the same RyGT commit with another renderer is a newer build (a renderer change
                // packaged by shadGT's run); with the same renderer, or one not known, it is this build.
                string renderer = BuildRendererCommit(root);
                return renderer != null && !string.IsNullOrEmpty(candidate.RendererCommit) && !SameCommit(candidate.RendererCommit, renderer);
            }
            return IsNewer(candidate.Version, installed);
        }

        /// Two commits, either one possibly shortened.
        static bool SameCommit(string a, string b)
        {
            return a.StartsWith(b, StringComparison.OrdinalIgnoreCase) || b.StartsWith(a, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsNewer(string candidate, string installed)
        {
            if (installed == null || candidate == null) return false;
            if (candidate.StartsWith("ci-") || installed.StartsWith("ci-")) return !string.Equals(candidate, installed, StringComparison.Ordinal);
            return string.CompareOrdinal(candidate, installed) > 0;
        }

        static WebClient Client()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var client = new WebClient { Encoding = System.Text.Encoding.UTF8 }; // FIX-GTS-286: the notes are UTF-8 ("·", not "Â·")
            client.Headers[HttpRequestHeader.UserAgent] = Product + "-launcher";
            return client;
        }

        static object Json(string url)
        {
            string text;
            using (var client = Client())
            {
                client.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                text = client.DownloadString(url);
            }
            return new JavaScriptSerializer { MaxJsonLength = 16 << 20 }.DeserializeObject(text);
        }

        /// The newest build on the channel, or null. Public repository: GitHub lists releases, runs
        /// and artifacts without signing in (60 requests an hour per address).
        public static ReleaseInfo Latest(string channel)
        {
            return channel == Nightly ? LatestNightly() : LatestRelease(channel == Prerelease);
        }

        /// The newest GitHub release (not a draft; not a pre-release unless asked) with a package zip.
        static ReleaseInfo LatestRelease(bool prereleases)
        {
            var releases = Json("https://api.github.com/repos/" + Repository + "/releases?per_page=20") as object[];
            if (releases == null) return null;
            foreach (Dictionary<string, object> release in releases.OfType<Dictionary<string, object>>())
            {
                bool pre = true.Equals(release["prerelease"]);
                if (true.Equals(release["draft"]) || (pre && !prereleases)) continue;
                string tag = release["tag_name"] as string ?? "";
                var info = new ReleaseInfo
                {
                    Tag = tag, Version = TagVersion(tag), Prerelease = pre,
                    Name = release["name"] as string ?? tag, Notes = release["body"] as string ?? ""
                };
                string shaUrl = null;
                var assets = (release["assets"] as object[] ?? new object[0]).OfType<Dictionary<string, object>>().ToList();
                var names = new HashSet<string>(assets.Select(a => a["name"] as string ?? ""), StringComparer.OrdinalIgnoreCase);
                // FIX-GTS-286: the package: a zip named like one (PackagePrefixes), the one with a .sha256 beside it first.
                var package = assets.Where(a => (a["name"] as string ?? "").EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                                                PackagePrefixes.Any(p => (a["name"] as string ?? "").StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                                    .OrderBy(a => names.Contains((a["name"] as string) + ".sha256") ? 0 : 1).FirstOrDefault();
                if (package == null) continue;
                info.ZipName = package["name"] as string;
                info.ZipUrl = package["browser_download_url"] as string;
                // The commit the tag points at, so the same build on another channel is not offered.
                try
                {
                    var tagged = Json("https://api.github.com/repos/" + Repository + "/commits/" + Uri.EscapeDataString(tag)) as Dictionary<string, object>;
                    object sha;
                    if (tagged != null && tagged.TryGetValue("sha", out sha)) info.Commit = sha as string;
                }
                catch { }
                foreach (Dictionary<string, object> asset in assets.OfType<Dictionary<string, object>>())
                    if (string.Equals(asset["name"] as string, info.ZipName + ".sha256", StringComparison.OrdinalIgnoreCase))
                        shaUrl = asset["browser_download_url"] as string;
                // The checksum: the .sha256 asset, else a SHA-256 in the notes.
                if (shaUrl != null)
                {
                    try { using (var client = Client()) info.Sha256 = client.DownloadString(shaUrl).Split(' ')[0].Trim().ToLowerInvariant(); }
                    catch { }
                }
                if (info.Sha256 == null)
                {
                    var sha = Regex.Match(info.Notes, @"\b([0-9a-fA-F]{64})\b");
                    info.Sha256 = sha.Success ? sha.Groups[1].Value.ToLowerInvariant() : null;
                }
                return info;
            }
            return null;
        }

        /// The newest successful CI build with a package artifact that has not expired. FIX-RT-044: from
        /// RyGT's runs (a push to development) and shadGT's (a push to ryty-gpu), whichever was built last.
        static ReleaseInfo LatestNightly()
        {
            ReleaseInfo runtime = null, renderer = null;
            Exception failed = null;
            try { runtime = LatestNightly(Repository, Workflow, Branch, false); }
            catch (Exception e) { failed = e; }
            try { renderer = LatestNightly(RendererRepository, RendererWorkflow, RendererBranch, true); }
            catch (Exception e) { failed = failed ?? e; }
            if (runtime == null && renderer == null && failed != null) throw failed;
            if (runtime == null || renderer == null) return runtime ?? renderer;
            // Run times are UTC in ISO 8601, so they sort as text.
            return string.CompareOrdinal(renderer.Built, runtime.Built) > 0 ? renderer : runtime;
        }

        /// The newest of one repository's package runs. Its runs are for that repository's commit: RyGT's
        /// artifact names the run's commit first, shadGT's names it as the renderer.
        static ReleaseInfo LatestNightly(string repository, string workflow, string branch, bool rendererRuns)
        {
            var runs = Json("https://api.github.com/repos/" + repository + "/actions/workflows/" + workflow +
                            "/runs?branch=" + branch + "&status=success&per_page=5") as Dictionary<string, object>;
            object list;
            if (runs == null || !runs.TryGetValue("workflow_runs", out list)) return null;
            foreach (Dictionary<string, object> run in ((object[])list).OfType<Dictionary<string, object>>())
            {
                string id = Convert.ToString(run["id"]), head = run["head_sha"] as string ?? "";
                if (head.Length < 8) continue;
                var artifacts = Json("https://api.github.com/repos/" + repository + "/actions/runs/" + id + "/artifacts") as Dictionary<string, object>;
                object items;
                if (artifacts == null || !artifacts.TryGetValue("artifacts", out items)) continue;
                foreach (Dictionary<string, object> artifact in ((object[])items).OfType<Dictionary<string, object>>())
                {
                    string name = artifact["name"] as string ?? "";
                    var parts = NightlyArtifact.Match(name);
                    if (!parts.Success || true.Equals(artifact["expired"])) continue;
                    string sha = parts.Groups[1].Value, rendererSha = parts.Groups[2].Value;
                    if ((rendererRuns ? rendererSha : sha) != head) continue;
                    string created = run["created_at"] as string ?? "", when = created.Replace('T', ' ').TrimEnd('Z');
                    string version = "ci-" + sha.Substring(0, 8) + "+" + rendererSha.Substring(0, 8);
                    return new ReleaseInfo
                    {
                        Artifact = true, Version = version, Tag = sha, Built = created, Commit = sha, RendererCommit = rendererSha,
                        Name = "RyGT " + Display(version),
                        Notes = CommitNotes(run, "Built " + (when.Length >= 16 ? when.Substring(0, 16) + " UTC" : when) + " from " +
                                (rendererRuns ? "shadGT " + branch : branch) + " (RyGT " + sha.Substring(0, 8) + ", renderer " + rendererSha.Substring(0, 8) + ")."),
                        ZipName = NightlyPrefix + sha.Substring(0, 8) + ".zip",
                        ZipUrl = "https://nightly.link/" + repository + "/actions/runs/" + id + "/" + name + ".zip"
                    };
                }
            }
            return null;
        }

        /// FEAT-RT-045: a nightly's changelog is its commit's description as a bulleted list: each paragraph
        /// (or bullet) of the run's head commit message, without the "Merge pull request" line and the
        /// trailers, then the commit's author. The fallback when the run gives no message.
        public static string CommitNotes(Dictionary<string, object> run, string fallback)
        {
            var commit = run.ContainsKey("head_commit") ? run["head_commit"] as Dictionary<string, object> : null;
            string message = commit != null && commit.ContainsKey("message") ? commit["message"] as string ?? "" : "";
            var bullets = new List<string>();
            foreach (string paragraph in Regex.Split(message.Replace("\r", ""), @"\n\s*\n"))
            {
                var lines = paragraph.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 &&
                    !l.StartsWith("Merge pull request #", StringComparison.Ordinal) &&
                    !Regex.IsMatch(l, @"^(Co-Authored-By|Signed-off-by):", RegexOptions.IgnoreCase)).ToList();
                if (lines.Count == 0) continue;
                if (lines.Any(l => l.StartsWith("- ") || l.StartsWith("* ")))
                {
                    // Bullets stay bullets; a wrapped bullet's lines join it.
                    foreach (string line in lines)
                        if (line.StartsWith("- ") || line.StartsWith("* ") || bullets.Count == 0) bullets.Add(line.TrimStart('-', '*', ' '));
                        else bullets[bullets.Count - 1] += " " + line;
                }
                else bullets.Add(string.Join(" ", lines));
            }
            if (bullets.Count == 0) return fallback;
            // The commit's author (the name only, never the e-mail) as the last bullet.
            var author = commit.ContainsKey("author") ? commit["author"] as Dictionary<string, object> : null;
            string name = author != null && author.ContainsKey("name") ? (author["name"] as string ?? "").Trim() : "";
            if (name.Length > 0) bullets.Add("Author: " + name);
            return string.Join("\n", bullets.Select(b => "- " + b));
        }

        /// A release's version: its tag without the prefix ("rygt-2026-11-23" is "2026-11-23").
        public static string TagVersion(string tag) { return WithoutPrefix(tag, TagPrefixes); }

        static string WithoutPrefix(string tag, string[] prefixes)
        {
            foreach (var prefix in prefixes)
                if (tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return tag.Substring(prefix.Length);
            return tag;
        }

        static string Sha256Of(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        static bool IsProtected(string relative)
        {
            string path = relative.Replace('/', '\\').TrimStart('\\');
            if (path.Contains("..") || Path.IsPathRooted(path)) return true;
            foreach (var folder in ProtectedFolders)
                if (path.StartsWith(folder + "\\", StringComparison.OrdinalIgnoreCase)) return true;
            // GAME-005: the old save folders the runtime set aside (_sd.moved-<date>, download0.moved-<date>).
            int slash = path.IndexOf('\\');
            if (slash > 0 && path.Substring(0, slash).IndexOf(".moved-", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            // FEAT-GTS-291: app0 on its way into app0\<serial> (app0.moving-<serial>-<time>), and the app0 folders
            // Set up kept (app0.previous-<time>, app0.<serial>.previous-<time>).
            if (slash > 0 && path.StartsWith("app0.", StringComparison.OrdinalIgnoreCase)) return true;
            // FEAT-GTS-291: convert, temp0 and data on their way into <name>\<serial> (<name>.moving-<serial>-<time>),
            // and the old settings files kept beside the new one (RyGT7.ini.moved-<date>, a .RyGT.ini.partial).
            if (slash > 0 && path.Substring(0, slash).IndexOf(".moving-", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (slash < 0 && (path.IndexOf(".ini.moved-", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              path.EndsWith(".RyGT.ini.partial", StringComparison.OrdinalIgnoreCase))) return true;
            return ProtectedFiles.Any(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));
        }

        /// Leftovers of an update: the download and staging folders, and the previous launcher exe,
        /// which couldn't be deleted while it was running.
        public static void CleanUp(string root)
        {
            foreach (string name in new[] { "staging", "download", "trash" })
            {
                try { string dir = Path.Combine(UpdateFolder(root), name); if (Directory.Exists(dir)) Directory.Delete(dir, true); }
                catch { }
            }
        }

        static void CheckGameClosed(string root)
        {
            foreach (var process in GameProcesses.SelectMany(name => Process.GetProcessesByName(name)))
            {
                try
                {
                    if (string.Equals(Path.GetDirectoryName(process.MainModule.FileName), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Close the game before updating.");
                }
                catch (System.ComponentModel.Win32Exception) { }
            }
        }

        static void Download(string url, string path, string what, Action<string, double> progress)
        {
            using (var client = Client())
            {
                var done = new System.Threading.ManualResetEvent(false);
                Exception failure = null;
                client.DownloadProgressChanged += (s, e) =>
                    progress("Downloading " + what + ": " + (e.BytesReceived >> 20) +
                             (e.TotalBytesToReceive > 0 ? " of " + (e.TotalBytesToReceive >> 20) : "") + " MB",
                             e.TotalBytesToReceive > 0 ? e.BytesReceived / (double)e.TotalBytesToReceive : -1);
                client.DownloadFileCompleted += (s, e) => { failure = e.Error; done.Set(); };
                client.DownloadFileAsync(new Uri(url), path + ".part");
                done.WaitOne();
                if (failure != null) throw failure;
            }
            File.Move(path + ".part", path);
        }

        /// The package's files and their SHA-256, from manifest.json ("files": path, sha256) or
        /// SHA256SUMS.txt ("<sha256>  <path>").
        static List<KeyValuePair<string, string>> PackageFiles(string packageRoot)
        {
            var files = new List<KeyValuePair<string, string>>();
            string manifestPath = Path.Combine(packageRoot, "manifest.json");
            string sumsPath = Path.Combine(packageRoot, "SHA256SUMS.txt");
            if (File.Exists(manifestPath))
            {
                var manifest = (Dictionary<string, object>)new JavaScriptSerializer { MaxJsonLength = 16 << 20 }.DeserializeObject(File.ReadAllText(manifestPath));
                foreach (Dictionary<string, object> file in ((object[])manifest["files"]).OfType<Dictionary<string, object>>())
                    files.Add(new KeyValuePair<string, string>((string)file["path"], ((string)file["sha256"]).ToLowerInvariant()));
                files.Add(new KeyValuePair<string, string>("manifest.json", null));
            }
            else if (File.Exists(sumsPath))
            {
                foreach (var line in File.ReadAllLines(sumsPath))
                {
                    var match = Regex.Match(line, @"^([0-9a-fA-F]{64}) [ *](.+)$");
                    if (match.Success) files.Add(new KeyValuePair<string, string>(match.Groups[2].Value, match.Groups[1].Value.ToLowerInvariant()));
                }
                files.Add(new KeyValuePair<string, string>("SHA256SUMS.txt", null));
            }
            else throw new InvalidDataException("The package lists no files (no manifest.json or SHA256SUMS.txt).");
            return files;
        }

        /// Downloads the build, checks it and installs its program files over root, the folder of the
        /// game with the title ID titleId. progress gets a line of text and a fraction (or -1). The
        /// replaced files go to update\backup-<old version>.
        public static void Install(string root, string titleId, ReleaseInfo release, Action<string, double> progress)
        {
            if (!release.Artifact && string.IsNullOrEmpty(release.Sha256))
                throw new InvalidDataException("The release gives no SHA-256 for its package, so it can't be checked.");
            CheckGameClosed(root);
            CleanUp(root);
            string folder = UpdateFolder(root), download = Path.Combine(folder, "download"), staging = Path.Combine(folder, "staging");
            Directory.CreateDirectory(download);
            string zip = Path.Combine(download, release.ZipName);
            string expected = release.Sha256;

            if (release.Artifact)
            {
                // A nightly: the artifact holds the package zip and its .sha256.
                string artifact = Path.Combine(download, "artifact.zip");
                Download(release.ZipUrl, artifact, release.ZipName, progress);
                progress("Unpacking the download...", -1);
                string unpacked = Path.Combine(download, "artifact");
                ZipFile.ExtractToDirectory(artifact, unpacked);
                string inner = Directory.GetFiles(unpacked, NightlyPrefix + "*.zip", SearchOption.AllDirectories).FirstOrDefault();
                if (inner == null || !File.Exists(inner + ".sha256")) throw new InvalidDataException("The nightly has no package zip and checksum.");
                expected = File.ReadAllText(inner + ".sha256").Split(' ')[0].Trim().ToLowerInvariant();
                File.Move(inner, zip);
            }
            else
            {
                Download(release.ZipUrl, zip, release.ZipName, progress);
            }
            if (string.IsNullOrEmpty(expected) || !Regex.IsMatch(expected, "^[0-9a-f]{64}$"))
                throw new InvalidDataException("The package's SHA-256 is not given, so it can't be checked.");

            progress("Checking the download...", -1);
            string sha = Sha256Of(zip);
            if (sha != expected)
                throw new InvalidDataException("The download is damaged (SHA-256 " + sha + ", expected " + expected + ").");

            progress("Unpacking...", -1);
            ZipFile.ExtractToDirectory(zip, staging);
            // The package's top folder: the one holding manifest.json or SHA256SUMS.txt.
            string listing = Directory.GetFiles(staging, "manifest.json", SearchOption.AllDirectories)
                                      .Concat(Directory.GetFiles(staging, "SHA256SUMS.txt", SearchOption.AllDirectories))
                                      .OrderBy(p => p.Length).FirstOrDefault();
            if (listing == null) throw new InvalidDataException("The package lists no files.");
            string packageRoot = Path.GetDirectoryName(listing);
            var listed = PackageFiles(packageRoot);
            // FEAT-GTS-291: only a package made for the folder's game, which ships that game's files
            // (GT Sport's user\custom_configs\CUSA03220.json, GT7's patches\CUSA24769.xml): another
            // game's runtime over it would break it.
            if (!PackageIsFor(listed.Select(f => f.Key), titleId))
                throw new InvalidDataException("This package has no files for " + titleId + ", so it can't update this folder. " +
                                               "Nothing was changed.");
            var files = new List<string>();
            foreach (var file in listed)
            {
                string relative = file.Key.Replace('/', '\\');
                if (IsProtected(relative)) continue;
                string source = Path.Combine(packageRoot, relative);
                if (!File.Exists(source) || (file.Value != null && Sha256Of(source) != file.Value))
                    throw new InvalidDataException("The package's " + relative + " doesn't match its checksum list.");
                files.Add(relative);
            }
            files = files.Where(f => !VersionFiles.Contains(f, StringComparer.OrdinalIgnoreCase))
                         .Concat(files.Where(f => VersionFiles.Contains(f, StringComparer.OrdinalIgnoreCase))).ToList();

            string installed = InstalledVersion(root) ?? "unknown";
            string backup = Path.Combine(folder, "backup-" + installed);
            if (Directory.Exists(backup)) Directory.Delete(backup, true);
            Directory.CreateDirectory(backup);
            var replaced = new List<string>();
            var added = new List<string>();
            try
            {
                int n = 0;
                foreach (string relative in files)
                {
                    progress("Installing " + relative, ++n / (double)files.Count);
                    string target = Path.Combine(root, relative), source = Path.Combine(packageRoot, relative);
                    if (File.Exists(target))
                    {
                        string saved = Path.Combine(backup, relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(saved));
                        // A move, not a copy: the launcher exe is running and can only be renamed.
                        File.Move(target, saved);
                        replaced.Add(relative);
                    }
                    else
                    {
                        added.Add(relative);
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Copy(source, target);
                }
            }
            catch
            {
                Restore(root, backup, replaced, added);
                Directory.Delete(backup, true);
                throw;
            }
            WriteRecord(backup, installed, release.Version, replaced, added);
            RecordInstalled(root, release.Version);
            CleanUp(root);
            progress("Updated to " + Display(release.Version) + ".", 1);
        }

        /// Whether a package's file list has a file named after the title ID (CUSA03220.json, CUSA24769.xml).
        public static bool PackageIsFor(IEnumerable<string> files, string titleId)
        {
            return !string.IsNullOrEmpty(titleId) &&
                   files.Any(f => string.Equals(Path.GetFileNameWithoutExtension(f.Replace('/', '\\')), titleId, StringComparison.OrdinalIgnoreCase));
        }

        /// What was installed, for packages whose files don't name its version (a nightly repackaged as
        /// a release, or BUILD_INFO.txt + SHA256SUMS.txt): valid while the build identity is the same.
        static void RecordInstalled(string root, string version)
        {
            string identity = BuildIdentity(root), path = Path.Combine(UpdateFolder(root), "installed.json");
            try
            {
                if (identity == null || version == null || version == "unknown") { if (File.Exists(path)) File.Delete(path); return; }
                var record = new Dictionary<string, object> { { "version", version }, { "build", identity } };
                File.WriteAllText(path, new JavaScriptSerializer().Serialize(record));
            }
            catch { }
        }

        static void WriteRecord(string backup, string from, string to, List<string> replaced, List<string> added)
        {
            var record = new Dictionary<string, object> { { "from", from }, { "to", to }, { "replaced", replaced }, { "added", added } };
            File.WriteAllText(Path.Combine(backup, "rollback.json"), new JavaScriptSerializer().Serialize(record));
        }

        static void Restore(string root, string backup, List<string> replaced, List<string> added)
        {
            string trash = Path.Combine(UpdateFolder(root), "trash");
            Directory.CreateDirectory(trash);
            foreach (string relative in added.Concat(replaced))
            {
                string target = Path.Combine(root, relative);
                if (!File.Exists(target)) continue;
                // Moved aside rather than deleted: one of them may be the running launcher.
                string aside = Path.Combine(trash, Guid.NewGuid().ToString("N"));
                File.Move(target, aside);
            }
            foreach (string relative in replaced)
            {
                string saved = Path.Combine(backup, relative), target = Path.Combine(root, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Move(saved, target);
            }
        }

        /// The update that can be rolled back (the newest backup), as its folder, "from" and "to".
        public static Tuple<string, string, string> LastUpdate(string root)
        {
            string folder = UpdateFolder(root);
            if (!Directory.Exists(folder)) return null;
            var newest = Directory.GetDirectories(folder, "backup-*")
                                  .Where(d => File.Exists(Path.Combine(d, "rollback.json")))
                                  .OrderByDescending(d => File.GetLastWriteTimeUtc(Path.Combine(d, "rollback.json"))).FirstOrDefault();
            if (newest == null) return null;
            var record = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(newest, "rollback.json")));
            return Tuple.Create(newest, (string)record["from"], (string)record["to"]);
        }

        /// Puts the files of the last update back as they were before it.
        public static void RollBack(string root)
        {
            CheckGameClosed(root);
            var last = LastUpdate(root);
            if (last == null) throw new InvalidOperationException("There is no update to roll back.");
            var record = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(last.Item1, "rollback.json")));
            var replaced = ((object[])record["replaced"]).Cast<string>().ToList();
            var added = ((object[])record["added"]).Cast<string>().ToList();
            Restore(root, last.Item1, replaced, added);
            Directory.Delete(last.Item1, true);
            RecordInstalled(root, last.Item2);
        }

        /// Starts the (new) launcher and closes this one: RyGTLauncher.exe beside this launcher (the new
        /// one when this folder was the one updated), else this launcher again. After a roll back to a
        /// release from before RyGTLauncher.exe, the older launcher OldLaunchers renamed is put back and
        /// started instead.
        public static void Restart(string root)
        {
            string self = System.Windows.Forms.Application.ExecutablePath;
            string folder = Path.GetDirectoryName(self);
            string next = Path.Combine(folder, LauncherExe);
            if (!File.Exists(next)) next = File.Exists(self) ? self : OldLaunchers.PutBack(folder);
            if (next != null)
                Process.Start(new ProcessStartInfo(next) { WorkingDirectory = folder, UseShellExecute = false });
            System.Windows.Forms.Application.Exit();
        }
    }
}
