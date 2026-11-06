using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Dreamy.EditorTools.Capture;
using NUnit.Framework;

namespace Dreamy.EditorTools.Tests
{
    public sealed class CaptureOutputTests
    {
        private sealed class Clipboard : IImageClipboard
        {
            public string UnavailableReason => null;
            public Func<Task> Run = () => Task.CompletedTask;
            public CaptureImage Received;
            public Task CopyAsync(CaptureImage image, CancellationToken cancellation)
            {
                Received = image;
                cancellation.ThrowIfCancellationRequested();
                return Run();
            }
        }

        private static CaptureImage Image() => new CaptureImage(new byte[] { 1, 2, 3 }, 1, 1);

        [Test]
        public async Task CopyOnlyNeverInvokesStorage()
        {
            var clipboard = new Clipboard();
            var image = Image();
            var result = await CaptureOutput.WriteAsync(image, CaptureMode.Copy, clipboard,
                _ => throw new AssertionException("Copy must not touch storage."), CancellationToken.None);
            Assert.That(result.Copied, Is.True);
            Assert.That(result.SavedPath, Is.Null);
            Assert.That(clipboard.Received, Is.SameAs(image));
        }

        [Test]
        public async Task SaveOnlyDoesNotTouchClipboard()
        {
            var image = Image();
            var result = await CaptureOutput.WriteAsync(image, CaptureMode.Save, null,
                bytes => { Assert.That(bytes, Is.SameAs(image.Png)); return "saved.png"; }, CancellationToken.None);
            Assert.That(result.Copied, Is.False);
            Assert.That(result.SavedPath, Is.EqualTo("saved.png"));
        }

        [Test]
        public async Task CopyFailureStillSavesSameFrame()
        {
            var clipboard = new Clipboard { Run = () => Task.FromException(new IOException("busy")) };
            var image = Image();
            var result = await CaptureOutput.WriteAsync(image, CaptureMode.CopyAndSave, clipboard,
                bytes => { Assert.That(bytes, Is.SameAs(clipboard.Received.Png)); return "saved.png"; }, CancellationToken.None);
            Assert.That(result.CopyError, Is.EqualTo("busy"));
            Assert.That(result.SavedPath, Is.EqualTo("saved.png"));
            Assert.That(result.HasOutput && result.HasError, Is.True);
        }

        [Test]
        public async Task SaveFailurePreservesSuccessfulCopy()
        {
            var result = await CaptureOutput.WriteAsync(Image(), CaptureMode.CopyAndSave, new Clipboard(),
                _ => throw new IOException("read only"), CancellationToken.None);
            Assert.That(result.Copied, Is.True);
            Assert.That(result.SaveError, Is.EqualTo("read only"));
        }

        [Test]
        public async Task CancellationAfterCopyPreservesCopyWithoutWritingFile()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                var clipboard = new Clipboard { Run = () => { cancellation.Cancel(); return Task.CompletedTask; } };
                var result = await CaptureOutput.WriteAsync(Image(), CaptureMode.CopyAndSave, clipboard,
                    _ => throw new AssertionException("Cancelled work must not write."), cancellation.Token);
                Assert.That(result.Copied, Is.True);
                Assert.That(result.Cancelled, Is.True);
                Assert.That(result.SavedPath, Is.Null);
            }
        }

        [Test]
        public async Task FailedCopyOnlyDoesNotFallBackToSave()
        {
            var clipboard = new Clipboard { Run = () => Task.FromException(new TimeoutException("timeout")) };
            var result = await CaptureOutput.WriteAsync(Image(), CaptureMode.Copy, clipboard,
                _ => throw new AssertionException("No fallback save."), CancellationToken.None);
            Assert.That(result.HasOutput, Is.False);
            Assert.That(result.CopyError, Is.EqualTo("timeout"));
        }

        [Test]
        public void AtomicSavePreservesExistingFileAndCleansTemporaryFile()
        {
            string directory = Path.Combine(Path.GetTempPath(), "dreamy-capture-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string path = Path.Combine(directory, "ảnh có dấu.png");
                File.WriteAllBytes(path, new byte[] { 42 });
                Assert.Throws<IOException>(() => CaptureStorage.SaveExact(new byte[] { 1, 2 }, path));
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(new byte[] { 42 }));
                Assert.That(Directory.GetFiles(directory), Has.Length.EqualTo(1));
                string unique = CaptureStorage.Save(new byte[] { 1, 2 }, directory, "Game");
                Assert.That(File.ReadAllBytes(unique), Is.EqualTo(new byte[] { 1, 2 }));
            }
            finally { Directory.Delete(directory, true); }
        }

        [Test]
        public void WindowsDibPreservesBottomUpRowsAndConvertsRgbaToOpaqueBgra()
        {
            var image = new CaptureImage(new byte[] { 1 }, 1, 2,
                new byte[] { 255, 0, 0, 10, 0, 0, 255, 20 });
            byte[] dib = WindowsImageClipboard.BuildDib(image);
            Assert.That(BitConverter.ToInt32(dib, 0), Is.EqualTo(40));
            Assert.That(BitConverter.ToInt32(dib, 4), Is.EqualTo(1));
            Assert.That(BitConverter.ToInt32(dib, 8), Is.EqualTo(2));
            Assert.That(BitConverter.ToInt16(dib, 14), Is.EqualTo(32));
            Assert.That(new ArraySegment<byte>(dib, 40, 8),
                Is.EqualTo(new byte[] { 0, 0, 255, 255, 255, 0, 0, 255 }));
        }

        [Test]
        public void BatchCaptureFailsFastWithoutStartingJob()
        {
            if (!UnityEngine.Application.isBatchMode) Assert.Ignore("Only applies to batch mode.");
            Assert.That(GameViewCapture.GetUnavailableReason(CaptureMode.Save), Does.Contain("batch"));
            GameViewCapture.Start(CaptureMode.Save, 0, "unused");
            Assert.That(GameViewCapture.IsBusy, Is.False);
        }
    }
}
