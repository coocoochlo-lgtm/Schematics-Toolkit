using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

namespace Remedy.StickyNotes
{
    /// <summary>
    /// Performance snapshots for sticky notes. A tester who writes "it stutters here" is much more useful when the note
    /// also says what the frame rate, frame times, memory and draw calls were at that moment, on which device.
    ///
    /// <para><b>When a note gets one.</b> Conditionally (<see cref="ShouldAttach"/>): when the author ticks "Attach
    /// performance snapshot", or automatically for Warning and Error notes while <see cref="AutoForWarningsAndErrors"/>
    /// is on (a project setting; builds get it from the scene data the build step embeds). It's taken when the note is
    /// started, so typing the note doesn't skew it.</para>
    ///
    /// <para><b>What's in it.</b> Average and minimum FPS over the last <see cref="SampleFrames"/> frames, CPU and GPU frame
    /// time from <see cref="FrameTimingManager"/> (0 when the platform or settings don't provide them), memory (Unity's
    /// allocated and reserved memory, the managed heap and the GC), render counters through <see cref="ProfilerRecorder"/>
    /// (draw calls, batches, SetPass calls, triangles; these only exist in the editor and development builds), the camera
    /// position, and device info. It's stored compactly in <see cref="StickyNote.Perf"/> as "key=value;key=value" text,
    /// which reads fine in a diff and keeps notes without a snapshot as small as before.</para>
    ///
    /// <para><b>Profiler captures.</b> In the editor's Play Mode the editor can also ask for a short binary Profiler capture
    /// (<see cref="ProfilerCapturePath"/>): about <see cref="ProfilerCaptureFrames"/> frames written to a .raw file with
    /// Profiler.logFile/enableBinaryLog. The note links it (<see cref="StickyNote.PerfCapture"/>), and the editor opens it
    /// in the Profiler window.</para>
    ///
    /// <para>Frame samples are collected by the in-game view's host object every frame (it exists in Play Mode and in
    /// builds); in Edit Mode there's no game loop to measure, so editor notes made outside Play Mode carry memory and
    /// device info only.</para>
    /// </summary>
    public static class StickyNotePerf
    {
        /// <summary>Snapshots are attached automatically to Warning and Error notes.</summary>
        public static bool AutoForWarningsAndErrors = true;

        /// <summary>How many recent frames the FPS figures cover.</summary>
        public static int SampleFrames = 120;

        /// <summary>How many frames a Profiler capture records.</summary>
        public static int ProfilerCaptureFrames = 60;

        /// <summary>
        /// Set by the editor: returns where to write a Profiler capture for a new note (a path without extension; ".raw"
        /// is added), or null for none. Null (the default, and always in builds) means no capture.
        /// </summary>
        public static Func<StickyNote, string> ProfilerCapturePath;

        /// <summary>Whether a new note of this type gets a snapshot (ticked by the author, or automatic for warnings/errors).</summary>
        public static bool ShouldAttach(StickyNoteType type, bool requested) =>
            requested || (AutoForWarningsAndErrors && type != StickyNoteType.Default);

        #region Sampling

        private const int RingSize = 600;
        private static readonly float[] _frameTimes = new float[RingSize];
        private static int _frameCount, _frameIndex;
        private static readonly FrameTiming[] _timing = new FrameTiming[1];
        private static double _lastCpuMs, _lastGpuMs;

        private static bool _recordersStarted;
        private static ProfilerRecorder _drawCalls, _batches, _setPass, _triangles, _vertices;

        private static int _captureFramesLeft;
        private static bool _captureOldEnabled, _captureOldBinary;
        private static string _captureOldFile;

        /// <summary>
        /// Records the current frame. Called every frame by the in-game view's host (Play Mode and builds); call it
        /// yourself if you replace that host.
        /// </summary>
        public static void Tick()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt > 0f && dt < 5f)
            {
                _frameTimes[_frameIndex] = dt;
                _frameIndex = (_frameIndex + 1) % RingSize;
                if (_frameCount < RingSize) _frameCount++;
            }

            try
            {
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, _timing) > 0)
                {
                    if (_timing[0].cpuFrameTime > 0) _lastCpuMs = _timing[0].cpuFrameTime;
                    if (_timing[0].gpuFrameTime > 0) _lastGpuMs = _timing[0].gpuFrameTime;
                }
            }
            catch { /* not supported here */ }

            if (!_recordersStarted) StartRecorders();
            if (_captureFramesLeft > 0 && --_captureFramesLeft == 0) EndProfilerCapture();
        }

        private static void StartRecorders()
        {
            _recordersStarted = true;
            try
            {
                _drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
                _batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
                _setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
                _triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
                _vertices = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Vertices Count");
            }
            catch (Exception e) { Debug.LogWarning($"[Sticky Notes] Render counters aren't available: {e.Message}"); }
            Application.quitting -= StopRecorders;
            Application.quitting += StopRecorders;
        }

        private static void StopRecorders()
        {
            if (!_recordersStarted) return;
            _recordersStarted = false;
            DisposeRecorder(ref _drawCalls);
            DisposeRecorder(ref _batches);
            DisposeRecorder(ref _setPass);
            DisposeRecorder(ref _triangles);
            DisposeRecorder(ref _vertices);
            if (_captureFramesLeft > 0) { _captureFramesLeft = 0; EndProfilerCapture(); }
            _frameCount = 0;
        }

        private static void DisposeRecorder(ref ProfilerRecorder r)
        {
            try { if (r.Valid) r.Dispose(); }
            catch { }
            r = default;
        }

        private static long Read(ProfilerRecorder r)
        {
            try { return r.Valid ? r.LastValue : -1; }
            catch { return -1; }
        }

        #endregion

        #region Capturing

        /// <summary>
        /// Takes a snapshot now and returns it in the compact form stored in <see cref="StickyNote.Perf"/>.
        /// <paramref name="scene"/> and <paramref name="position"/> describe where the note goes; the camera position is
        /// added when <paramref name="camera"/> (or Camera.main) is known.
        /// </summary>
        public static string Capture(string scene = null, Vector3? position = null, Camera camera = null)
        {
            var d = new Dictionary<string, string>();
            void Put(string k, string v) { if (!string.IsNullOrEmpty(v)) d[k] = Clean(v); }
            void PutF(string k, double v, string fmt = "0.0") { if (v > 0 && !double.IsNaN(v) && !double.IsInfinity(v)) d[k] = v.ToString(fmt, CultureInfo.InvariantCulture); }
            void PutL(string k, long v) { if (v >= 0) d[k] = v.ToString(CultureInfo.InvariantCulture); }

            Put("v", "1");
            Put("t", StickyNote.Now());
            Put("play", Application.isPlaying ? "1" : "0");

            // FPS over the last N frames.
            int n = Mathf.Min(_frameCount, Mathf.Clamp(SampleFrames, 1, RingSize));
            if (Application.isPlaying && n > 0)
            {
                double sum = 0, worst = 0;
                for (int i = 0; i < n; i++)
                {
                    float dt = _frameTimes[(_frameIndex - 1 - i + RingSize) % RingSize];
                    sum += dt;
                    if (dt > worst) worst = dt;
                }
                PutF("fps", n / sum);
                PutF("fpsMin", worst > 0 ? 1.0 / worst : 0);
                PutL("n", n);
            }
            if (Application.isPlaying)
            {
                PutF("cpu", _lastCpuMs, "0.00");
                PutF("gpu", _lastGpuMs, "0.00");
                PutL("dc", Read(_drawCalls));
                PutL("batch", Read(_batches));
                PutL("setpass", Read(_setPass));
                PutL("tris", Read(_triangles));
                PutL("verts", Read(_vertices));
            }

            // Memory (MB).
            try
            {
                PutF("alloc", Profiler.GetTotalAllocatedMemoryLong() / 1048576.0);
                PutF("reserved", Profiler.GetTotalReservedMemoryLong() / 1048576.0);
                PutF("mono", Profiler.GetMonoUsedSizeLong() / 1048576.0);
            }
            catch { }
            PutF("gc", GC.GetTotalMemory(false) / 1048576.0);
            PutL("gcCount", GC.CollectionCount(0));

            // Where.
            Put("scene", scene);
            if (position.HasValue) Put("pos", Vec(position.Value));
            var cam = camera != null ? camera : (Application.isPlaying ? Camera.main : null);
            if (cam != null) Put("cam", Vec(cam.transform.position));

            // Device.
            try
            {
                Put("device", SystemInfo.deviceModel);
                Put("os", SystemInfo.operatingSystem);
                Put("cpuName", SystemInfo.processorType);
                PutL("cores", SystemInfo.processorCount);
                PutL("ram", SystemInfo.systemMemorySize);
                Put("gpuName", SystemInfo.graphicsDeviceName);
                PutL("vram", SystemInfo.graphicsMemorySize);
                Put("api", SystemInfo.graphicsDeviceType.ToString());
                Put("res", $"{Screen.width}x{Screen.height}");
                int q = QualitySettings.GetQualityLevel();
                var names = QualitySettings.names;
                if (names != null && q >= 0 && q < names.Length) Put("quality", names[q]);
                Put("ver", Application.version);
                Put("unity", Application.unityVersion);
                Put("build", Application.isEditor ? "editor" : Debug.isDebugBuild ? "development" : "release");
            }
            catch { }

            return Format(d);
        }

        /// <summary>
        /// Starts a binary Profiler capture of <see cref="ProfilerCaptureFrames"/> frames into
        /// <paramref name="pathWithoutExtension"/> + ".raw" (Profiler.logFile + enableBinaryLog). Returns the .raw path,
        /// or null when a capture is already running or the game isn't playing.
        /// </summary>
        public static string BeginProfilerCapture(string pathWithoutExtension)
        {
            if (string.IsNullOrEmpty(pathWithoutExtension) || !Application.isPlaying || _captureFramesLeft > 0) return null;
            try
            {
                var dir = System.IO.Path.GetDirectoryName(pathWithoutExtension);
                if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
                _captureOldEnabled = Profiler.enabled;
                _captureOldBinary = Profiler.enableBinaryLog;
                _captureOldFile = Profiler.logFile;
                Profiler.logFile = pathWithoutExtension;
                Profiler.enableBinaryLog = true;
                Profiler.enabled = true;
                _captureFramesLeft = Mathf.Max(1, ProfilerCaptureFrames);
                return pathWithoutExtension + ".raw";
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Sticky Notes] Couldn't start a Profiler capture: {e.Message}");
                return null;
            }
        }

        private static void EndProfilerCapture()
        {
            try
            {
                Profiler.enableBinaryLog = _captureOldBinary;
                Profiler.logFile = _captureOldFile ?? "";
                Profiler.enabled = _captureOldEnabled;
            }
            catch (Exception e) { Debug.LogWarning($"[Sticky Notes] Couldn't stop the Profiler capture: {e.Message}"); }
        }

        /// <summary>
        /// Everything a UI needs when a new note is saved: the snapshot (when <see cref="ShouldAttach"/> says so, using
        /// <paramref name="takenEarlier"/> when the UI captured it when the note was started) and, in the editor's Play
        /// Mode, a Profiler capture path. Writes both into <paramref name="note"/>.
        /// </summary>
        public static void AttachIfWanted(StickyNote note, bool requested, string takenEarlier = null, Camera camera = null)
        {
            if (note == null) return;
            if (!ShouldAttach(note.NoteType, requested)) { note.Perf = ""; note.PerfCapture = ""; return; }
            note.Perf = !string.IsNullOrEmpty(takenEarlier) ? takenEarlier : Capture(note.Scene, note.Position, camera);
            if (Application.isPlaying && ProfilerCapturePath != null)
            {
                string path = null;
                try { path = ProfilerCapturePath(note); }
                catch (Exception e) { Debug.LogException(e); }
                if (!string.IsNullOrEmpty(path)) note.PerfCapture = BeginProfilerCapture(path) ?? "";
            }
        }

        #endregion

        #region Reading

        /// <summary>Parses the compact form into key/value pairs (empty for an empty snapshot).</summary>
        public static Dictionary<string, string> Parse(string perf)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(perf)) return d;
            foreach (var part in perf.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq > 0) d[part.Substring(0, eq)] = part.Substring(eq + 1);
            }
            return d;
        }

        /// <summary>One short line for cards and tooltips: "58 fps (min 41) · CPU 12.3 ms · GPU 8.1 ms · 512 MB · 1.2k draws".</summary>
        public static string Summary(string perf)
        {
            var d = Parse(perf);
            if (d.Count == 0) return "";
            var parts = new List<string>();
            if (d.TryGetValue("fps", out var fps))
                parts.Add(d.TryGetValue("fpsMin", out var min) ? $"{Round(fps)} fps (min {Round(min)})" : $"{Round(fps)} fps");
            if (d.TryGetValue("cpu", out var cpu)) parts.Add($"CPU {cpu} ms");
            if (d.TryGetValue("gpu", out var gpu)) parts.Add($"GPU {gpu} ms");
            if (d.TryGetValue("alloc", out var alloc)) parts.Add($"{Round(alloc)} MB");
            if (d.TryGetValue("dc", out var dc)) parts.Add($"{Short(dc)} draws");
            if (parts.Count == 0) parts.Add(d.TryGetValue("play", out var p) && p == "0" ? "edit mode snapshot" : "snapshot");
            return string.Join(" · ", parts);
        }

        /// <summary>Several readable lines with everything in the snapshot (for the note panel).</summary>
        public static string Details(string perf)
        {
            var d = Parse(perf);
            if (d.Count == 0) return "";
            var sb = new StringBuilder();
            string G(string k) => d.TryGetValue(k, out var v) ? v : null;
            void Line(string label, string value) { if (!string.IsNullOrEmpty(value)) sb.Append(label).Append(": ").Append(value).Append('\n'); }

            if (G("fps") != null) Line("FPS", $"{G("fps")} avg, {G("fpsMin") ?? "?"} min over {G("n") ?? "?"} frames");
            else if (G("play") == "0") Line("FPS", "not playing (edit mode)");
            if (G("cpu") != null || G("gpu") != null) Line("Frame", $"CPU {G("cpu") ?? "?"} ms, GPU {G("gpu") ?? "?"} ms");
            if (G("dc") != null || G("batch") != null || G("tris") != null)
                Line("Render", $"{Short(G("dc"))} draws, {Short(G("batch"))} batches, {Short(G("setpass"))} SetPass, {Short(G("tris"))} tris");
            if (G("alloc") != null) Line("Memory", $"{Round(G("alloc"))} MB allocated, {Round(G("reserved"))} MB reserved, managed {Round(G("mono") ?? G("gc"))} MB, {G("gcCount") ?? "?"} GCs");
            if (G("cam") != null) Line("Camera", G("cam"));
            if (G("device") != null) Line("Device", $"{G("device")}, {G("os")}");
            if (G("cpuName") != null) Line("Hardware", $"{G("cpuName")} ({G("cores")} cores, {G("ram")} MB), {G("gpuName")} ({G("vram")} MB, {G("api")})");
            if (G("res") != null) Line("Screen", $"{G("res")}, quality {G("quality") ?? "?"}");
            if (G("build") != null) Line("Build", $"{G("build")} {G("ver")}, Unity {G("unity")}");
            return sb.ToString().TrimEnd('\n');
        }

        private static string Format(Dictionary<string, string> d)
        {
            var sb = new StringBuilder();
            foreach (var pair in d)
            {
                if (sb.Length > 0) sb.Append(';');
                sb.Append(pair.Key).Append('=').Append(pair.Value);
            }
            return sb.ToString();
        }

        // Values can't contain the separators.
        private static string Clean(string v) => v.Replace(';', ',').Replace('=', ':').Replace('\n', ' ').Replace('\r', ' ').Trim();

        private static string Vec(Vector3 v) =>
            string.Format(CultureInfo.InvariantCulture, "{0:0.##} {1:0.##} {2:0.##}", v.x, v.y, v.z);

        private static string Round(string v) =>
            double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ? Math.Round(x).ToString(CultureInfo.InvariantCulture) : v ?? "?";

        private static string Short(string v)
        {
            if (!double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var x)) return v ?? "?";
            if (x >= 1e6) return (x / 1e6).ToString("0.#", CultureInfo.InvariantCulture) + "M";
            if (x >= 1e3) return (x / 1e3).ToString("0.#", CultureInfo.InvariantCulture) + "k";
            return x.ToString("0", CultureInfo.InvariantCulture);
        }

        #endregion
    }
}
