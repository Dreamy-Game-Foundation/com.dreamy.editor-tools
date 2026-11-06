using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Dreamy.EditorTools.Capture
{
    internal static class ImageClipboard
    {
        public static string GetSetupInstructions(out string installCommand)
        {
            installCommand = null;
            if (Application.platform == RuntimePlatform.WindowsEditor)
                return "Windows: image clipboard is built in. No additional installation is required.";
            if (Application.platform != RuntimePlatform.LinuxEditor)
                return "Image clipboard supports Windows and Ubuntu editors.";
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
            {
                installCommand = "sudo apt install wl-clipboard";
                return "Ubuntu Wayland requires wl-clipboard (wl-copy) on Unity's PATH. " +
                    "Run the command in Terminal, then reopen Unity. Save works without this tool.";
            }
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
            {
                installCommand = "sudo apt install xclip";
                return "Ubuntu X11 requires xclip on Unity's PATH. " +
                    "Run the command in Terminal, then reopen Unity. Save works without this tool.";
            }
            return "Ubuntu image clipboard requires a graphical Wayland or X11 desktop session.";
        }

        public static IImageClipboard Create()
        {
            if (Application.platform == RuntimePlatform.WindowsEditor) return new WindowsImageClipboard();
            if (Application.platform == RuntimePlatform.LinuxEditor) return new LinuxImageClipboard();
            return new UnavailableClipboard("Image clipboard supports Windows and Ubuntu editors.");
        }

        private sealed class UnavailableClipboard : IImageClipboard
        {
            public string UnavailableReason { get; }
            public UnavailableClipboard(string reason) { UnavailableReason = reason; }
            public Task CopyAsync(CaptureImage image, CancellationToken cancellation) =>
                Task.FromException(new NotSupportedException(UnavailableReason));
        }
    }

    internal sealed class LinuxImageClipboard : IImageClipboard
    {
        private readonly string executable;
        private readonly bool wayland;
        public string UnavailableReason { get; }

        public LinuxImageClipboard()
        {
            wayland = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
            string command = wayland ? "wl-copy" : "xclip";
            if (!wayland && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
            {
                UnavailableReason = "No desktop clipboard session is available.";
                return;
            }
            foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                string candidate = Path.Combine(dir, command);
                if (File.Exists(candidate)) { executable = candidate; break; }
            }
            if (executable == null)
                UnavailableReason = wayland ? "Copy Image requires wl-clipboard (wl-copy). Install it or select Save." :
                    "Copy Image requires xclip in an X11 session. Install it or select Save.";
        }

        public async Task CopyAsync(CaptureImage image, CancellationToken cancellation)
        {
            if (UnavailableReason != null) throw new NotSupportedException(UnavailableReason);
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                try
                {
                    // Both helpers own the clipboard in a background child until the next owner.
                    // Pass PNG through stdin: no screenshot or temporary file is created.
                    await Task.Run(() =>
                    {
                        timeout.Token.ThrowIfCancellationRequested();
                        using (var process = new Process())
                        {
                            process.StartInfo = new ProcessStartInfo(executable,
                                wayland ? "--type image/png" : "-selection clipboard -t image/png -i -silent")
                            {
                                UseShellExecute = false,
                                RedirectStandardInput = true,
                                CreateNoWindow = true
                            };
                            if (!process.Start()) throw new IOException("Could not start image clipboard helper.");
                            using (timeout.Token.Register(() =>
                            {
                                try { if (!process.HasExited) process.Kill(); }
                                catch (InvalidOperationException) { }
                                catch (System.ComponentModel.Win32Exception) { }
                            }))
                            {
                                try
                                {
                                    process.StandardInput.BaseStream.Write(image.Png, 0, image.Png.Length);
                                    process.StandardInput.Close();
                                    while (!process.WaitForExit(50)) timeout.Token.ThrowIfCancellationRequested();
                                    if (process.ExitCode != 0)
                                        throw new IOException("Clipboard helper exited with code " + process.ExitCode + ". Check the desktop session.");
                                    // Exit 0 is the helper's successful handoff; preserve it even if cancellation raced.
                                }
                                catch
                                {
                                    timeout.Token.ThrowIfCancellationRequested();
                                    throw;
                                }
                            }
                        }
                    }, timeout.Token);
                }
                catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
                {
                    throw new TimeoutException("Image clipboard did not respond within 5 seconds.");
                }
            }
        }
    }

    internal sealed class WindowsImageClipboard : IImageClipboard
    {
        public string UnavailableReason => null;

        public async Task CopyAsync(CaptureImage image, CancellationToken cancellation)
        {
            if (image.Rgba == null || image.Rgba.Length != checked(image.Width * image.Height * 4))
                throw new ArgumentException("RGBA pixels are required for Windows clipboard.");
            byte[] dib = BuildDib(image);
            int length = dib.Length;
            bool opened = false;
            for (int i = 0; i < 8; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                IntPtr owner = GetActiveWindow();
                if (owner != IntPtr.Zero && OpenClipboard(owner)) { opened = true; break; }
                await Task.Delay(40, cancellation);
            }
            if (!opened) throw new IOException("Clipboard is busy or Unity has no active window. Try Copy Image again.");
            IntPtr memory = IntPtr.Zero;
            try
            {
                memory = GlobalAlloc(0x0002, (UIntPtr)length);
                if (memory == IntPtr.Zero) throw new OutOfMemoryException("Could not allocate clipboard image.");
                IntPtr pointer = GlobalLock(memory);
                if (pointer == IntPtr.Zero) throw new IOException("Could not lock clipboard memory.");
                try { Marshal.Copy(dib, 0, pointer, length); }
                finally { GlobalUnlock(memory); }
                cancellation.ThrowIfCancellationRequested();
                if (!EmptyClipboard() || SetClipboardData(8, memory) == IntPtr.Zero)
                    throw new IOException("Windows could not accept the clipboard image.");
                memory = IntPtr.Zero; // Windows now owns this memory.
            }
            finally
            {
                if (memory != IntPtr.Zero) GlobalFree(memory);
                CloseClipboard();
            }
        }

        internal static byte[] BuildDib(CaptureImage image)
        {
            // CF_DIB, BITMAPINFOHEADER + bottom-up opaque BGRA. Unity RGBA rows are bottom-up.
            int length = checked(40 + image.Rgba.Length);
            byte[] dib = new byte[length];
            Buffer.BlockCopy(BitConverter.GetBytes(40), 0, dib, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(image.Width), 0, dib, 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(image.Height), 0, dib, 8, 4);
            dib[12] = 1;
            dib[14] = 32;
            Buffer.BlockCopy(BitConverter.GetBytes(image.Rgba.Length), 0, dib, 20, 4);
            for (int i = 0; i < image.Rgba.Length; i += 4)
            {
                dib[40 + i] = image.Rgba[i + 2];
                dib[41 + i] = image.Rgba[i + 1];
                dib[42 + i] = image.Rgba[i];
                dib[43 + i] = 255;
            }
            return dib;
        }

        [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
        [DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr owner);
        [DllImport("user32.dll")] private static extern bool CloseClipboard();
        [DllImport("user32.dll")] private static extern bool EmptyClipboard();
        [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint format, IntPtr memory);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr memory);
        [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr memory);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr memory);
    }
}
