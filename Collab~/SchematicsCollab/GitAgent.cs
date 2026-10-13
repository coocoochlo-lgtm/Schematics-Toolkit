using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Remedy.Schematics.Collaboration;

namespace SchematicsCollab;

/// <summary>
/// Keeps the project's git working copy in step with everyone else's, using the git command line.
///
/// Safety rules (see Collab~/README.md): it only ever stages the paths it was given, never force-pushes, only pushes
/// when every unpushed commit is one it made itself (marked with the <see cref="CollabProtocol.CommitTrailer"/>
/// trailer), never pulls while a file it would change has local edits, and never rewrites a scene that is live in a
/// session: those keep their bytes on disk (they show as modified in git status until the session's next snapshot).
/// </summary>
sealed class GitAgent
{
    private readonly string _projectPath;
    private readonly EditorLink _editor;
    private readonly SemaphoreSlim _busy = new(1, 1);
    private readonly SemaphoreSlim _wake = new(0, int.MaxValue);
    private readonly Dictionary<string, string> _pending = new();       // guid -> project path, announced by a peer
    private readonly Dictionary<string, string> _preserved = new();     // repo path -> hash of the live bytes we kept
    private string _lastPostponeNotice;

    public CollabGitInfo Info { get; private set; } = new();
    public bool HasRepo => Info.hasRepo;
    public bool HasUpstream => Info.hasRepo && !Kv.Empty(Info.branch) && !Kv.Empty(Info.upstream);

    // Settings from the editor.
    public int PollSeconds = 15;
    public bool AutoPull = true;
    public bool AutoPush = true;
    public HashSet<string> LiveScenes = new();          // project-relative scene paths open in the editor
    public Func<bool> SessionIsShared = () => false;    // live-scene protection applies with others in the session
    public Action InfoChanged;

    public GitAgent(string projectPath, EditorLink editor)
    {
        _projectPath = Path.GetFullPath(projectPath);
        _editor = editor;
    }

    #region Running git

    private sealed record Result(int Code, string Out, string Err)
    {
        public bool Ok => Code == 0;
        public string Message => (Err.Trim().Length > 0 ? Err : Out).Trim();
    }

    private async Task<Result> Git(string workDir, int timeoutSeconds, params string[] args)
    {
        var info = new ProcessStartInfo("git")
        {
            WorkingDirectory = workDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        // Never sit waiting for a password nobody can type, never open an editor, and keep paths readable.
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        info.Environment["GIT_EDITOR"] = "true";
        info.Environment["GIT_MERGE_AUTOEDIT"] = "no";
        info.ArgumentList.Add("-c"); info.ArgumentList.Add("core.quotepath=off");
        foreach (var arg in args) info.ArgumentList.Add(arg);
        try
        {
            using var process = Process.Start(info)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            try { await process.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException)
            {
                try { process.Kill(true); } catch { }
                return new Result(-1, "", $"git {args.FirstOrDefault()} took longer than {timeoutSeconds} s");
            }
            return new Result(process.ExitCode, await stdout, await stderr);
        }
        catch (Exception e)
        {
            return new Result(-1, "", $"couldn't run git ({e.Message}). Is git installed and on the PATH?");
        }
    }

    private Task<Result> Git(params string[] args) => Git(Info.root, 120, args);
    private Task<Result> GitSlow(params string[] args) => Git(Info.root, 600, args);

    private static List<string> Zlist(string output) =>
        output.Split('\0', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim('\n', '\r')).Where(s => s.Length > 0).ToList();

    #endregion

    #region Detecting

    public async Task DetectAsync()
    {
        var info = new CollabGitInfo();
        var top = await Git(_projectPath, 30, "rev-parse", "--show-toplevel");
        if (top.Ok)
        {
            info.hasRepo = true;
            info.root = Path.GetFullPath(top.Out.Trim());
            info.prefix = (await Git(_projectPath, 30, "rev-parse", "--show-prefix")).Out.Trim();
            var branch = await Git(info.root, 30, "symbolic-ref", "--short", "-q", "HEAD");
            info.branch = branch.Ok ? branch.Out.Trim() : "";
            info.head = (await Git(info.root, 30, "rev-parse", "--short", "HEAD")).Out.Trim();
            info.userName = (await Git(info.root, 30, "config", "user.name")).Out.Trim();
            info.userEmail = (await Git(info.root, 30, "config", "user.email")).Out.Trim();
            if (info.branch.Length > 0)
            {
                string remote = (await Git(info.root, 30, "config", $"branch.{info.branch}.remote")).Out.Trim();
                var remotes = (await Git(info.root, 30, "remote")).Out.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(r => r.Trim()).ToList();
                if (remote.Length == 0 || remote == ".") remote = remotes.Contains("origin") ? "origin" : remotes.FirstOrDefault() ?? "";
                info.remote = remote;
                if (remote.Length > 0)
                {
                    info.remoteUrl = (await Git(info.root, 30, "remote", "get-url", remote)).Out.Trim();
                    // The branch's upstream if it has one on that remote, else the same name there (after a fetch it exists).
                    var upstream = await Git(info.root, 30, "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{u}");
                    info.upstream = upstream.Ok && upstream.Out.Trim().StartsWith(remote + "/") ? upstream.Out.Trim() : $"{remote}/{info.branch}";
                }
            }
        }
        Info = info;
        InfoChanged?.Invoke();
    }

    /// <summary>
    /// Peers only see each other when this matches: the normalized remote URL + branch + the project's folder in the
    /// repository. Without git: the product name + Unity version file + a project id file created once in Collab~.
    /// </summary>
    public string ProjectKey(string productName, string collabFolder)
    {
        string source;
        if (Info.hasRepo && !Kv.Empty(Info.remoteUrl))
            source = $"git|{NormalizeUrl(Info.remoteUrl)}|{Info.branch}|{Info.prefix}";
        else
        {
            string version = "";
            try { version = File.ReadAllText(Path.Combine(_projectPath, "ProjectSettings", "ProjectVersion.txt")).Trim(); } catch { }
            string id = "";
            if (!Kv.Empty(collabFolder))
            {
                string file = Path.Combine(collabFolder, "project-id");
                try
                {
                    if (!File.Exists(file)) File.WriteAllText(file, Guid.NewGuid().ToString("N") + "\n");
                    id = File.ReadAllText(file).Trim();
                }
                catch { }
            }
            source = $"local|{productName}|{version}|{id}|{(Info.hasRepo ? Info.branch : "")}";
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..16].ToLowerInvariant();
    }

    public static string NormalizeUrl(string url)
    {
        string u = url.Trim().ToLowerInvariant();
        int scheme = u.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) u = u[(scheme + 3)..];
        int at = u.IndexOf('@');
        if (at >= 0 && (u.IndexOf('/') < 0 || at < u.IndexOf('/'))) u = u[(at + 1)..];   // credentials / git@
        // scp-like host:path -> host/path (but keep host:port/path)
        int colon = u.IndexOf(':');
        if (colon > 0 && (u.IndexOf('/') < 0 || colon < u.IndexOf('/')))
        {
            string after = u[(colon + 1)..];
            int slash = after.IndexOf('/');
            string maybePort = slash >= 0 ? after[..slash] : after;
            u = maybePort.All(char.IsDigit) && maybePort.Length > 0 ? u[..colon] + after[maybePort.Length..] : u[..colon] + "/" + after;
        }
        u = u.TrimEnd('/');
        if (u.EndsWith(".git")) u = u[..^4];
        return u;
    }

    #endregion

    #region Paths

    private string ToRepo(string projectPath) => (Info.prefix ?? "") + projectPath.Replace('\\', '/');

    private string ToProject(string repoPath)
    {
        string prefix = Info.prefix ?? "";
        return repoPath.StartsWith(prefix, StringComparison.Ordinal) ? repoPath[prefix.Length..] : null;
    }

    private string Full(string repoPath) => Path.Combine(Info.root, repoPath.Replace('/', Path.DirectorySeparatorChar));

    private HashSet<string> LiveRepoPaths()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        List<string> scenes;
        lock (Gate.Lock)
        {
            if (!SessionIsShared()) return set;
            scenes = LiveScenes.ToList();
        }
        foreach (var scene in scenes)
        {
            set.Add(ToRepo(scene));
            set.Add(ToRepo(scene) + ".meta");
        }
        return set;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    #endregion

    #region Polling and pulling

    public async Task PollLoopAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            // Quicker while announced assets are on their way.
            int seconds;
            lock (Gate.Lock) seconds = _pending.Count > 0 ? Math.Min(3, PollSeconds) : PollSeconds;
            await _wake.WaitAsync(TimeSpan.FromSeconds(Math.Max(2, seconds)), stop).ContinueWith(_ => { });
            if (stop.IsCancellationRequested) return;
            while (_wake.CurrentCount > 0) _wake.Wait(0);
            if (!HasUpstream || !AutoPull || !_editor.Attached) continue;
            try { await SyncAsync(); }
            catch (Exception e) { Log.Write($"Pull failed: {e}"); }
        }
    }

    /// <summary>Fetch and pull now (Pull Now in the editor, or an asset announcement arrived).</summary>
    public void PullSoon() => _wake.Release();

    private async Task SyncAsync()
    {
        await _busy.WaitAsync();
        try { await IntegrateAsync(); }
        finally { _busy.Release(); }
    }

    /// <summary>Fetches, and brings in what's new upstream if that's safe. Holds <see cref="_busy"/>.</summary>
    private async Task<bool> IntegrateAsync()
    {
        if (!HasUpstream) return false;
        if (InProgress(out string what))
        {
            Notice($"Not pulling: a {what} is in progress in {Info.root}. Finish it and Collab pulls again.");
            return false;
        }
        var fetch = await GitSlow("fetch", "--quiet", Info.remote, Info.branch);
        if (!fetch.Ok)
        {
            Notice($"git fetch failed: {fetch.Message}");
            return false;
        }
        string up = $"refs/remotes/{Info.upstream}";
        var behindResult = await Git("rev-list", "--count", $"HEAD..{up}");
        if (!behindResult.Ok || behindResult.Out.Trim() == "0") return behindResult.Ok;

        var changed = Zlist((await Git("diff", "--name-only", "-z", $"HEAD...{up}")).Out);
        var live = LiveRepoPaths();
        var status = await Git("status", "--porcelain=v1", "-z", "--untracked-files=all");
        var (dirty, untracked) = ParseStatus(status.Out);

        var toRevert = new List<string>();
        var conflicts = new List<string>();
        foreach (var path in changed)
        {
            if (live.Contains(path)) continue;
            if (dirty.Contains(path))
            {
                // A scene we kept for a session that's over and nobody saved since: let the newer version in.
                if (_preserved.TryGetValue(path, out var hash) && File.Exists(Full(path)) && Hash(File.ReadAllBytes(Full(path))) == hash)
                    toRevert.Add(path);
                else conflicts.Add(path);
            }
            else if (untracked.Contains(path)) conflicts.Add(path);
        }
        if (conflicts.Count > 0)
        {
            Notice($"Not pulling {Info.upstream} yet: these have local changes and were also changed upstream: "
                + string.Join(", ", conflicts.Take(5)) + (conflicts.Count > 5 ? $" and {conflicts.Count - 5} more" : "")
                + ". Commit, stash or revert them and Collab pulls automatically.");
            return false;
        }

        string id = Guid.NewGuid().ToString("N")[..8];
        if (!await _editor.BeginPullAsync(id, 20000))
        {
            Log.Write("Pull postponed: the editor didn't confirm (busy or reloading).");
            return false;
        }
        string error = null;
        string[] pulled = Array.Empty<string>();
        var kept = new Dictionary<string, byte[]>();
        try
        {
            var liveTouched = changed.Where(live.Contains).ToList();
            foreach (var path in liveTouched)
                if (File.Exists(Full(path))) kept[path] = File.ReadAllBytes(Full(path));
            // Set live (and stale preserved) files back to HEAD so git can move past them; live bytes are restored below.
            var resetPaths = liveTouched.Concat(toRevert).ToList();
            if (resetPaths.Count > 0)
            {
                var inHead = Zlist((await Git(new[] { "ls-files", "-z", "--" }.Concat(resetPaths).ToArray())).Out).ToHashSet();
                var tracked = resetPaths.Where(inHead.Contains).ToArray();
                if (tracked.Length > 0)
                {
                    var checkout = await Git(new[] { "checkout", "HEAD", "--" }.Concat(tracked).ToArray());
                    if (!checkout.Ok) throw new IOException($"couldn't set aside live scenes: {checkout.Message}");
                }
                foreach (var path in liveTouched.Where(p => !inHead.Contains(p)))
                    File.Delete(Full(path));
                foreach (var path in toRevert) _preserved.Remove(path);
            }

            var aheadResult = await Git("rev-list", "--count", $"{up}..HEAD");
            Result merge;
            if (aheadResult.Out.Trim() == "0") merge = await Git("merge", "--ff-only", "--quiet", up);
            else
            {
                merge = await GitSlow("rebase", "--autostash", up);
                if (!merge.Ok) await Git("rebase", "--abort");
            }
            if (!merge.Ok) error = $"git couldn't bring in {Info.upstream}: {merge.Message}";
            else
                pulled = changed.Where(p => !live.Contains(p)).Select(ToProject).Where(p => p != null).ToArray();
        }
        catch (Exception e)
        {
            error = e.Message;
        }
        finally
        {
            foreach (var pair in kept)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Full(pair.Key))!);
                    File.WriteAllBytes(Full(pair.Key), pair.Value);
                    _preserved[pair.Key] = Hash(pair.Value);
                }
                catch (Exception e) { error = (error == null ? "" : error + " ") + $"Couldn't restore live scene {pair.Key}: {e.Message}"; }
            }
            _editor.EndPull(id, pulled, error);
        }
        if (error != null) _editor.Error("[Collab] " + error);
        else
        {
            _lastPostponeNotice = null;
            Log.Write($"Pulled {pulled.Length} changed paths from {Info.upstream}" + (kept.Count > 0 ? $" (kept live scenes: {string.Join(", ", kept.Keys)})" : "") + ".");
        }
        await RefreshHeadAsync();
        ResolvePending();
        return error == null;
    }

    private bool InProgress(out string what)
    {
        string gitDir = Path.Combine(Info.root, ".git");
        what = null;
        if (!Directory.Exists(gitDir)) return false;   // a worktree or submodule: git itself will refuse if needed
        if (File.Exists(Path.Combine(gitDir, "MERGE_HEAD"))) what = "merge";
        else if (Directory.Exists(Path.Combine(gitDir, "rebase-merge")) || Directory.Exists(Path.Combine(gitDir, "rebase-apply"))) what = "rebase";
        else if (File.Exists(Path.Combine(gitDir, "CHERRY_PICK_HEAD"))) what = "cherry-pick";
        return what != null;
    }

    private static (HashSet<string> dirty, HashSet<string> untracked) ParseStatus(string output)
    {
        var dirty = new HashSet<string>(StringComparer.Ordinal);
        var untracked = new HashSet<string>(StringComparer.Ordinal);
        var parts = output.Split('\0');
        for (int i = 0; i < parts.Length; i++)
        {
            string entry = parts[i];
            if (entry.Length < 4) continue;
            string code = entry[..2], path = entry[3..];
            if (code == "??") untracked.Add(path);
            else if (code != "!!")
            {
                dirty.Add(path);
                if (code[0] == 'R' || code[0] == 'C') { i++; if (i < parts.Length) dirty.Add(parts[i]); }
            }
        }
        return (dirty, untracked);
    }

    // The same notice once until something changes, not every poll.
    private void Notice(string text)
    {
        if (text == _lastPostponeNotice) return;
        _lastPostponeNotice = text;
        _editor.Warn("[Collab] " + text);
    }

    private async Task RefreshHeadAsync()
    {
        string head = (await Git("rev-parse", "--short", "HEAD")).Out.Trim();
        if (head != Info.head)
        {
            Info.head = head;
            InfoChanged?.Invoke();
        }
    }

    #endregion

    #region Pending assets

    public void AddPending(string guid, string projectPath)
    {
        lock (Gate.Lock)
        {
            if (MetaHasGuid(projectPath, guid)) return;
            _pending[guid] = projectPath;
        }
        PullSoon();
    }

    public bool IsPending(string guid) { lock (Gate.Lock) return _pending.ContainsKey(guid); }

    public CollabKV[] PendingEntries() { lock (Gate.Lock) return Kv.From(_pending); }

    private void ResolvePending()
    {
        lock (Gate.Lock)
            foreach (var pair in _pending.ToList())
                if (MetaHasGuid(pair.Value, pair.Key))
                {
                    _pending.Remove(pair.Key);
                    _editor.Emit(new CollabFrame { type = CollabFrameType.Resolved, key = pair.Key, text = pair.Value });
                }
    }

    private bool MetaHasGuid(string projectPath, string guid)
    {
        try
        {
            string meta = Path.Combine(_projectPath, projectPath.Replace('/', Path.DirectorySeparatorChar) + ".meta");
            return File.Exists(meta) && File.ReadLines(meta).Take(5).Any(line => line.Trim() == $"guid: {guid}");
        }
        catch { return false; }
    }

    #endregion

    #region Committing and pushing

    private static string Trailers(IEnumerable<CollabCoAuthor> coAuthors)
    {
        var lines = new List<string> { CollabProtocol.CommitTrailer };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var author in coAuthors ?? Enumerable.Empty<CollabCoAuthor>())
        {
            if (author == null || Kv.Empty(author.email) || !seen.Add(author.email)) continue;
            string name = Kv.Empty(author.name) ? author.email : author.name.Replace("\n", " ").Replace("<", "").Replace(">", "");
            lines.Add($"Co-authored-by: {name} <{author.email.Trim()}>");
        }
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Commits a new asset (with its .meta and dependencies) and pushes it. Peers were already told (they show a
    /// placeholder until their next pull brings it).
    /// </summary>
    public async Task AnnounceAsync(string assetPath, string[] projectPaths)
    {
        if (!HasRepo) return;
        await _busy.WaitAsync();
        try
        {
            var me = new CollabCoAuthor { name = Info.userName, email = Info.userEmail };
            bool committed = await CommitAsync(projectPaths, $"Add {assetPath} (collab)\n\n{Trailers(new[] { me })}");
            if (!committed) return;
            if (!AutoPush)
            {
                _editor.Info($"[Collab] Committed {assetPath} locally; automatic pushing is off, so push it to share it.");
                return;
            }
            await PushAsync();
        }
        finally { _busy.Release(); }
    }

    /// <summary>The host's periodic scene snapshot: the saved scene, with everyone in the session as co-authors.</summary>
    public async Task CommitSceneAsync(string scenePath, string summary, CollabCoAuthor[] coAuthors)
    {
        if (!HasRepo) { _editor.Warn("[Collab] No git repository for this project, so the scene snapshot wasn't committed."); return; }
        await _busy.WaitAsync();
        try
        {
            // Bring in what others pushed first, so the snapshot lands on top and the push is a fast-forward.
            if (HasUpstream) await IntegrateAsync();
            string title = Kv.Empty(summary) ? $"Scene snapshot: {scenePath}" : summary.Trim();
            var others = (coAuthors ?? Array.Empty<CollabCoAuthor>())
                .Where(a => a != null && !string.Equals(a.email, Info.userEmail, StringComparison.OrdinalIgnoreCase));
            bool committed = await CommitAsync(new[] { scenePath, scenePath + ".meta" }, $"{title} (collab)\n\n{Trailers(others)}");
            if (committed) _preserved.Remove(ToRepo(scenePath));
            if (committed && HasUpstream) await PushAsync();
        }
        finally { _busy.Release(); }
    }

    // Stages exactly these paths (those that exist) and commits only them, leaving anything else staged as it was.
    private async Task<bool> CommitAsync(string[] projectPaths, string message)
    {
        var repoPaths = projectPaths.Where(p => !Kv.Empty(p)).Select(ToRepo).Distinct()
            .Where(p => File.Exists(Full(p)) || Directory.Exists(Full(p))).ToArray();
        if (repoPaths.Length == 0) { _editor.Warn("[Collab] Nothing to commit: the files don't exist."); return false; }
        if (Kv.Empty(Info.userName) || Kv.Empty(Info.userEmail))
        {
            _editor.Error("[Collab] git doesn't know who you are, so Collab can't commit. Run: git config --global user.name \"Your Name\" and git config --global user.email you@example.com");
            return false;
        }
        var add = await Git(new[] { "add", "--" }.Concat(repoPaths).ToArray());
        if (!add.Ok) { _editor.Error($"[Collab] git add failed: {add.Message}"); return false; }
        var diff = await Git(new[] { "diff", "--cached", "--quiet", "--" }.Concat(repoPaths).ToArray());
        if (diff.Code == 0) return false;   // nothing new in these paths
        var commit = await Git(new[] { "commit", "--quiet", "-m", message, "--only", "--" }.Concat(repoPaths).ToArray());
        if (!commit.Ok) { _editor.Error($"[Collab] git commit failed: {commit.Message}"); return false; }
        Log.Write($"Committed {string.Join(", ", repoPaths)}.");
        await RefreshHeadAsync();
        return true;
    }

    // Pushes the branch, but only if every unpushed commit is ours; pulls (rebasing our commits) and retries on reject.
    private async Task<bool> PushAsync()
    {
        if (!HasUpstream) { _editor.Warn("[Collab] This branch has no remote to push to; the commit stays local."); return false; }
        for (int attempt = 0; attempt < 4; attempt++)
        {
            string up = $"refs/remotes/{Info.upstream}";
            bool upstreamExists = (await Git("rev-parse", "--verify", "--quiet", up)).Ok;
            var unpushed = await Git("log", "--format=%B%x1e", upstreamExists ? $"{up}..HEAD" : "HEAD", "-n", "50");
            var bodies = unpushed.Out.Split('\x1e', StringSplitOptions.RemoveEmptyEntries).Select(b => b.Trim()).Where(b => b.Length > 0).ToList();
            if (bodies.Any(b => !b.Contains(CollabProtocol.CommitTrailer)))
            {
                _editor.Warn($"[Collab] Not pushing: {Info.branch} has commits of yours that aren't pushed yet, and Collab only pushes its own. Push them yourself (that sends Collab's too).");
                return false;
            }
            var push = await GitSlow("push", "--quiet", Info.remote, $"HEAD:refs/heads/{Info.branch}");
            if (push.Ok)
            {
                Log.Write($"Pushed to {Info.remote}/{Info.branch}.");
                await Git("fetch", "--quiet", Info.remote, Info.branch);
                return true;
            }
            bool rejected = push.Message.Contains("rejected") || push.Message.Contains("fetch first") || push.Message.Contains("non-fast-forward");
            if (!rejected)
            {
                _editor.Error($"[Collab] git push failed: {push.Message}");
                return false;
            }
            Log.Write("Push rejected (someone pushed first); pulling and trying again.");
            if (!await IntegrateAsync() && attempt > 0)
            {
                _editor.Error("[Collab] Couldn't push: the branch moved on and Collab couldn't bring the changes in (see the warnings above).");
                return false;
            }
        }
        _editor.Error("[Collab] Couldn't push after several tries; the commit is safe locally and goes out with the next push.");
        return false;
    }

    #endregion
}
