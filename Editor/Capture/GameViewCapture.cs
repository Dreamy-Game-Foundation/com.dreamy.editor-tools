using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Dreamy.EditorTools.Capture
{
    [FilePath("UserSettings/DreamyCapture/Preferences.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class CapturePreferences : ScriptableSingleton<CapturePreferences>
    {
        public CaptureMode Mode = CaptureMode.Copy;
        public int DelaySeconds;
        public string OutputDirectory = "Screenshots";
        public void Persist() => Save(true);
    }

    [InitializeOnLoad]
    public static class GameViewCapture
    {
        public static bool IsBusy => current != null;
        public static string Status { get; private set; } = "Ready. Start Play Mode and keep Game View rendering.";
        public static Texture2D Preview { get; private set; }
        public static string SavedPath { get; private set; }
        public static CaptureOutputResult LastResult { get; private set; }
        public static event Action Changed;
        private static CancellationTokenSource current;
        private static object owner;
        private static double deadline;
        private static double countdownEnd;
        private static bool waitingForFrame;
        private const long MaxPixels = 32_000_000;
        private const int FrameTimeoutSeconds = 10;

        static GameViewCapture()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
            EditorApplication.quitting += Shutdown;
        }

        public static string GetUnavailableReason(CaptureMode mode, bool needsFrame = true)
        {
            if (!Enum.IsDefined(typeof(CaptureMode), mode)) return "Choose Copy, Save or Copy + Save.";
            if (Application.isBatchMode) return "Game View capture requires an interactive Editor (not batch mode).";
            if (Application.platform != RuntimePlatform.WindowsEditor && Application.platform != RuntimePlatform.LinuxEditor)
                return "Game View Screenshot supports Windows and Ubuntu editors.";
            if (needsFrame)
            {
                if (!EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode != EditorApplication.isPlaying)
                    return "Start Play Mode before capturing Game View.";
                if (EditorApplication.isPaused) return "Resume the Editor and keep Game View rendering before capturing.";
            }
            return CaptureOutput.NeedsCopy(mode) ? ImageClipboard.Create().UnavailableReason : null;
        }

        public static void Start(CaptureMode mode, int delaySeconds, string outputDirectory, object jobOwner = null)
        {
            if (IsBusy) { SetStatus("A capture is already running."); return; }
            string unavailable = GetUnavailableReason(mode);
            if (unavailable != null) { SetStatus(unavailable); return; }
            if (CaptureOutput.NeedsSave(mode) && string.IsNullOrWhiteSpace(outputDirectory))
            { SetStatus("Choose a save folder before capturing."); return; }
            Begin(jobOwner);
            countdownEnd = EditorApplication.timeSinceStartup + Mathf.Clamp(delaySeconds, 0, 5);
            deadline = countdownEnd + FrameTimeoutSeconds;
            RunCapture(current, mode, outputDirectory);
        }

        public static void OutputPreview(CaptureMode mode, string outputDirectory, object jobOwner = null)
        {
            if (IsBusy || Preview == null) return;
            string unavailable = GetUnavailableReason(mode, false);
            if (unavailable != null) { SetStatus(unavailable); return; }
            if (CaptureOutput.NeedsSave(mode) && string.IsNullOrWhiteSpace(outputDirectory))
            { SetStatus("Choose a save folder."); return; }
            Begin(jobOwner);
            RunOutput(current, mode, outputDirectory);
        }

        private static void Begin(object jobOwner)
        {
            current = new CancellationTokenSource();
            owner = jobOwner;
            waitingForFrame = false;
            deadline = double.PositiveInfinity;
            LastResult = null;
        }

        private static async void RunCapture(CancellationTokenSource job, CaptureMode mode, string directory)
        {
            try
            {
                SetStatus("Preparing Game View capture…");
                while (EditorApplication.timeSinceStartup < countdownEnd)
                {
                    await Task.Delay(30, job.Token);
                    SetStatus("Capturing in " + Math.Ceiling(countdownEnd - EditorApplication.timeSinceStartup) + "s…");
                }
                job.Token.ThrowIfCancellationRequested();
                waitingForFrame = true;
                SetStatus("Waiting for Game View frame. Keep Game View open.");
                await Awaitable.EndOfFrameAsync(job.Token);
                job.Token.ThrowIfCancellationRequested();
                waitingForFrame = false;
                deadline = double.PositiveInfinity;
                if (EditorApplication.isPaused || !EditorApplication.isPlaying)
                    throw new InvalidOperationException("Game View stopped rendering. Resume Play Mode and try again.");
                ValidateSize(Screen.width, Screen.height);
                Texture2D image = ScreenCapture.CaptureScreenshotAsTexture();
                if (image == null) throw new InvalidOperationException("Unity did not return a Game View image.");
                try
                {
                    ValidateSize(image.width, image.height);
                    image.hideFlags = HideFlags.HideAndDontSave;
                    ReleasePreview();
                    Preview = image;
                    SavedPath = null;
                    image = null; // Ownership transferred to Preview.
                }
                finally { if (image != null) UnityEngine.Object.DestroyImmediate(image); }
                await WritePreview(job, mode, directory);
            }
            catch (OperationCanceledException) { /* Cancel/timeout already sets the status. */ }
            catch (Exception ex) { if (current == job) SetStatus("Capture failed: " + ex.Message); }
            finally { Finish(job); }
        }

        private static async void RunOutput(CancellationTokenSource job, CaptureMode mode, string directory)
        {
            try { await WritePreview(job, mode, directory); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (current == job) SetStatus("Output failed: " + ex.Message); }
            finally { Finish(job); }
        }

        private static async Task WritePreview(CancellationTokenSource job, CaptureMode mode, string directory)
        {
            job.Token.ThrowIfCancellationRequested();
            SetStatus("Processing image…");
            byte[] rgba = null;
            if (CaptureOutput.NeedsCopy(mode) && Application.platform == RuntimePlatform.WindowsEditor)
            {
                Color32[] pixels = Preview.GetPixels32();
                rgba = new byte[checked(pixels.Length * 4)];
                for (int i = 0; i < pixels.Length; i++)
                {
                    rgba[i * 4] = pixels[i].r;
                    rgba[i * 4 + 1] = pixels[i].g;
                    rgba[i * 4 + 2] = pixels[i].b;
                    rgba[i * 4 + 3] = 255;
                }
            }
            var encoded = new CaptureImage(Preview.EncodeToPNG(), Preview.width, Preview.height, rgba);
            SetStatus(CaptureOutput.NeedsCopy(mode) ? "Copying image…" : "Saving PNG…");
            var result = await CaptureOutput.WriteAsync(encoded, mode, ImageClipboard.Create(),
                bytes => CaptureStorage.Save(bytes, ResolveDirectory(directory), new DirectoryInfo(ProjectRoot).Name), job.Token);
            if (current != job) return;
            LastResult = result;
            if (result.SavedPath != null) SavedPath = result.SavedPath;
            SetStatus(result.Message);
            if (result.HasError) Debug.LogWarning("[Dreamy Capture] " + result.Message);
        }

        public static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        public static string ResolveDirectory(string directory) =>
            Path.GetFullPath(Path.IsPathRooted(directory) ? directory : Path.Combine(ProjectRoot, directory));

        private static void ValidateSize(int width, int height)
        {
            if (width <= 0 || height <= 0 || (long)width * height > MaxPixels)
                throw new InvalidOperationException("Game View size is invalid or exceeds the 32 megapixel capture limit.");
            // Conservative working-set estimate includes texture/readback, PNG and Windows DIB copies.
            long bytesPerPixel = Application.platform == RuntimePlatform.WindowsEditor ? 32L : 16L;
            if ((long)width * height * bytesPerPixel > 512L * 1024 * 1024)
                throw new InvalidOperationException("Capture exceeds the estimated 512 MiB working-memory budget. Reduce Game View resolution.");
        }

        public static void Cancel(object jobOwner = null)
        {
            if (current == null || (jobOwner != null && !ReferenceEquals(owner, jobOwner))) return;
            // Keep the job busy until its output helper acknowledges cancellation.
            SetStatus("Cancelling capture…");
            current.Cancel();
        }

        internal static void ClearPreview(object jobOwner)
        {
            Cancel(jobOwner);
            ReleasePreview();
            SavedPath = null;
        }

        private static void Finish(CancellationTokenSource job)
        {
            if (current == job)
            {
                if (job.IsCancellationRequested && LastResult == null && !Status.StartsWith("Timed out"))
                    SetStatus("Capture cancelled.");
                current = null;
                owner = null;
                waitingForFrame = false;
                Changed?.Invoke();
            }
            job.Dispose();
        }

        private static void Tick()
        {
            if (current == null || current.IsCancellationRequested) return;
            if (!EditorApplication.isPlaying && waitingForFrame) { Cancel(); return; }
            if (EditorApplication.timeSinceStartup > deadline)
            {
                SetStatus("Timed out waiting for Game View. Open Game View and try again.");
                current.Cancel();
            }
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.ExitingEditMode) Cancel();
        }

        private static void Shutdown()
        {
            Cancel();
            ReleasePreview();
        }

        private static void ReleasePreview()
        {
            if (Preview != null) UnityEngine.Object.DestroyImmediate(Preview);
            Preview = null;
        }

        private static void SetStatus(string message) { Status = message; Changed?.Invoke(); }
    }
}
