using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace Remedy.Schematics.Collaboration
{
    /// <summary>Which room this project is in, and which commit it's at: what a playtest build carries.</summary>
    public sealed class CollabProjectIdentity
    {
        /// <summary>The project key: the room editors and builds of this repository + branch + project folder meet in.</summary>
        public string Key;
        public bool HasRepository;
        public string RemoteUrl;
        public string Branch;
        /// <summary>Short hash of HEAD.</summary>
        public string Commit;
        /// <summary>The Unity project's folder inside the repository ("" at its root).</summary>
        public string ProjectFolder;

        /// <summary>
        /// The current project's identity. The key comes from the running collaboration helper when there is one (it's
        /// what every editor uses); otherwise it is computed the same way (<see cref="CollabProtocol.ProjectKey"/>) from
        /// git, so builds made with collaboration switched off still find the editors.
        /// </summary>
        public static CollabProjectIdentity Current()
        {
            string project = Path.GetDirectoryName(Application.dataPath);
            var identity = new CollabProjectIdentity();
            var info = CollabGit.Info;
            if (info != null && info.hasRepo)
            {
                identity.HasRepository = true;
                identity.RemoteUrl = info.remoteUrl;
                identity.Branch = info.branch;
                identity.ProjectFolder = info.prefix;
            }
            else
            {
                string top = Git(project, "rev-parse", "--show-toplevel");
                if (!string.IsNullOrEmpty(top))
                {
                    identity.HasRepository = true;
                    identity.ProjectFolder = Git(project, "rev-parse", "--show-prefix") ?? "";
                    identity.Branch = Git(project, "symbolic-ref", "--short", "-q", "HEAD") ?? "";
                    string remote = identity.Branch.Length > 0 ? Git(project, "config", "branch." + identity.Branch + ".remote") : null;
                    if (string.IsNullOrEmpty(remote) || remote == ".")
                    {
                        var remotes = (Git(project, "remote") ?? "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        remote = Array.IndexOf(remotes, "origin") >= 0 ? "origin" : remotes.Length > 0 ? remotes[0].Trim() : "";
                    }
                    if (!string.IsNullOrEmpty(remote)) identity.RemoteUrl = Git(project, "remote", "get-url", remote) ?? "";
                }
            }
            identity.Commit = identity.HasRepository ? Git(project, "rev-parse", "--short", "HEAD") ?? "" : "";

            identity.Key = CollabBridge.ProjectKey;
            if (string.IsNullOrEmpty(identity.Key))
            {
                string version = "", id = "";
                try { version = File.ReadAllText(Path.Combine(project, "ProjectSettings", "ProjectVersion.txt")).Trim(); } catch { }
                try
                {
                    string folder = CollabBridge.CollabFolder;
                    string file = folder != null ? Path.Combine(folder, "project-id") : null;
                    if (file != null && File.Exists(file)) id = File.ReadAllText(file).Trim();
                }
                catch { }
                identity.Key = CollabProtocol.ProjectKey(identity.HasRepository, identity.RemoteUrl, identity.Branch, identity.ProjectFolder,
                    Application.productName, version, id);
            }
            return identity;
        }

        /// <summary>The relay this editor uses (Preferences), copied into builds so they meet the same session.</summary>
        public static string RelayAddress => CollabSettings.RelayAddress;

        private static string Git(string dir, params string[] args)
        {
            try
            {
                var info = new ProcessStartInfo("git")
                {
                    WorkingDirectory = dir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                info.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
                info.Arguments = string.Join(" ", Array.ConvertAll(args, a => a.Contains(" ") ? "\"" + a + "\"" : a));
                using (var process = Process.Start(info))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    if (!process.WaitForExit(10000)) { try { process.Kill(); } catch { } return null; }
                    return process.ExitCode == 0 ? output.Trim() : null;
                }
            }
            catch (Exception e)
            {
                CollabLog.Warning("Couldn't run git to identify the project for the build (" + e.Message + ").");
                return null;
            }
        }
    }
}
