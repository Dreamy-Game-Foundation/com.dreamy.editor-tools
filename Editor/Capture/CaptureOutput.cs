using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Dreamy.EditorTools.Capture
{
    public enum CaptureMode { Copy, Save, CopyAndSave }

    public sealed class CaptureImage
    {
        public readonly byte[] Png;
        public readonly byte[] Rgba;
        public readonly int Width;
        public readonly int Height;

        public CaptureImage(byte[] png, int width, int height, byte[] rgba = null)
        {
            Png = png ?? throw new ArgumentNullException(nameof(png));
            Width = width;
            Height = height;
            Rgba = rgba;
        }
    }

    public interface IImageClipboard
    {
        string UnavailableReason { get; }
        Task CopyAsync(CaptureImage image, CancellationToken cancellation);
    }

    public sealed class CaptureOutputResult
    {
        public bool Copied;
        public string SavedPath;
        public string CopyError;
        public string SaveError;
        public bool Cancelled;
        public bool HasOutput => Copied || !string.IsNullOrEmpty(SavedPath);
        public bool HasError => !string.IsNullOrEmpty(CopyError) || !string.IsNullOrEmpty(SaveError);

        public string Message
        {
            get
            {
                string result = Copied ? (SavedPath != null ? "Copied + saved." : "Copied image.") :
                    (SavedPath != null ? "Saved image." : "No output completed.");
                if (Cancelled) result += " Cancelled remaining work.";
                if (CopyError != null) result += " Copy failed: " + CopyError;
                if (SaveError != null) result += " Save failed: " + SaveError;
                return result;
            }
        }
    }

    public static class CaptureOutput
    {
        public static bool NeedsCopy(CaptureMode mode) => mode != CaptureMode.Save;
        public static bool NeedsSave(CaptureMode mode) => mode != CaptureMode.Copy;

        // The two destinations are independent: a failed copy must not discard a successful save.
        public static async Task<CaptureOutputResult> WriteAsync(CaptureImage image, CaptureMode mode,
            IImageClipboard clipboard, Func<byte[], string> save, CancellationToken cancellation)
        {
            var result = new CaptureOutputResult();
            if (NeedsCopy(mode))
            {
                try
                {
                    cancellation.ThrowIfCancellationRequested();
                    await clipboard.CopyAsync(image, cancellation);
                    result.Copied = true;
                }
                catch (OperationCanceledException) { result.Cancelled = true; }
                catch (Exception ex) { result.CopyError = ex.Message; }
            }
            if (cancellation.IsCancellationRequested) result.Cancelled = true;
            if (NeedsSave(mode) && !result.Cancelled)
            {
                try
                {
                    cancellation.ThrowIfCancellationRequested();
                    result.SavedPath = save(image.Png);
                }
                catch (OperationCanceledException) { result.Cancelled = true; }
                catch (Exception ex) { result.SaveError = ex.Message; }
            }
            return result;
        }
    }

    public static class CaptureStorage
    {
        public static string Save(byte[] png, string directory, string projectName)
        {
            if (png == null || png.Length == 0) throw new ArgumentException("Image is empty.");
            directory = Path.GetFullPath(directory);
            Directory.CreateDirectory(directory);
            foreach (char c in Path.GetInvalidFileNameChars()) projectName = projectName.Replace(c, '_');
            string name = projectName + "_GameView_" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") +
                          "_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".png";
            return SaveExact(png, Path.Combine(directory, name));
        }

        // Temp is in the same directory so publishing is a single rename; never overwrite.
        public static string SaveExact(byte[] png, string path)
        {
            path = Path.GetFullPath(path);
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(png, 0, png.Length);
                    stream.Flush();
                }
                File.Move(temp, path);
                return path;
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }
    }
}
