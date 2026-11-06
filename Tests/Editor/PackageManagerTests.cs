using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Dreamy.EditorTools.Package;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Dreamy.EditorTools.Tests
{
    public sealed class PackageManagerTests
    {
        private const string TestId = "com.dreamy.manager-tests";
        private static CatalogEntry Entry(string id = TestId) => new() { Id = id, Repository = "https://github.com/Dreamy-Game-Foundation/" + id };
        private static PackageRelease Release(string version = "1.0.0", params (string, string)[] dependencies) => new()
        {
            Version = version, Tag = "v" + version, Revision = new string('a', 40), Unity = "6000.0", Verified = true,
            Dependencies = dependencies.ToDictionary(d => d.Item1, d => d.Item2)
        };
        private static InstalledPackage Installed(string id, string version = "1.0.0", string source = "Git") => new()
        {
            Id = id, Version = version, Source = source, Direct = true,
            ManifestValue = source == "Local" ? "file:../LocalPackages/" + id : Entry(id).GitUrl("v" + version)
        };

        [TearDown]
        public void Cleanup()
        {
            var cache = PackageReleases.CacheFile(TestId);
            if (File.Exists(cache)) File.Delete(cache);
        }

        [TestCase("1.9.0", "1.10.0", -1)]
        [TestCase("1.0.0-preview.2", "1.0.0-preview.10", -1)]
        [TestCase("1.0.0-preview.1", "1.0.0", -1)]
        [TestCase("v2.0.0", "1.99.99", 1)]
        [TestCase("1.0.0+build1", "1.0.0+build2", 0)]
        [TestCase("1.0.0-alpha.9999999999999999999", "1.0.0-alpha.99999999999999999999", -1)]
        public void SemVerOrder(string a, string b, int expected)
        {
            Assert.That(PackageVersion.TryParse(a, out var av), Is.True);
            Assert.That(PackageVersion.TryParse(b, out var bv), Is.True);
            Assert.That(Math.Sign(av.CompareTo(bv)), Is.EqualTo(expected));
        }
        [TestCase("01.0.0")]
        [TestCase("1.0.0-preview.01")]
        [TestCase("main")]
        [TestCase("file:../LocalPackages/test")]
        public void InvalidVersionsAreNotComparable(string version) => Assert.That(PackageVersion.TryParse(version, out _), Is.False);

        [TestCase("6000.0", "6000.4.12f1", true)]
        [TestCase("6000.4.13f1", "6000.4.12f1", false)]
        [TestCase("6000.5", "6000.4.12f1", false)]
        public void UnityMinimum(string required, string actual, bool compatible) => Assert.That(PackageVersion.UnityCompatible(required, actual), Is.EqualTo(compatible));

        [Test]
        public void CatalogExtendsWithoutCodeAndProjectOverridesRemote()
        {
            var original = new CatalogDocument { Packages = new List<CatalogEntry> { Entry("com.dreamy.core") } };
            var remote = PackageCatalog.Parse("{\"schemaVersion\":1,\"packages\":[{\"id\":\"com.dreamy.future\",\"repository\":\"https://github.com/Dreamy-Game-Foundation/future\"}]}");
            var projectEntry = Entry("com.dreamy.future"); projectEntry.Category = "Custom";
            var merged = PackageCatalog.Merge(original, remote, new CatalogDocument { Packages = new List<CatalogEntry> { projectEntry } });
            Assert.That(merged.Count, Is.EqualTo(2));
            Assert.That(merged.Single(p => p.Id == projectEntry.Id).Category, Is.EqualTo("Custom"));
        }
        [TestCase("{}")]
        [TestCase("{\"schemaVersion\":2,\"packages\":[]}")]
        [TestCase("{\"schemaVersion\":1,\"packages\":[{\"id\":\"../../escape\"}]}")]
        [TestCase("{\"schemaVersion\":1,\"packages\":[{\"id\":\"com.test.a\",\"repository\":\"https://user:token@github.com/owner/repo\"}]}")]
        public void CatalogRejectsInvalidDocuments(string json) => Assert.Throws<InvalidDataException>(() => PackageCatalog.Parse(json));
        [Test]
        public void CatalogRejectsDuplicateEntries()
        {
            var json = JsonConvert.SerializeObject(new CatalogDocument { Packages = new List<CatalogEntry> { Entry(), Entry() } });
            Assert.Throws<InvalidDataException>(() => PackageCatalog.Parse(json));
        }
        [Test]
        public void BundledCatalogContainsEveryLocalDreamyPackageAndIsPortable()
        {
            var catalog = PackageCatalog.Load();
            foreach (var p in UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages().Where(p => p.name.StartsWith("com.dreamy.")))
                Assert.That(catalog.Any(e => e.Id == p.name), Is.True, p.name);
            Assert.That(catalog.All(e => e.Source == "registry" || e.Repository.StartsWith("https://github.com/")), Is.True);
        }

        private static Task<PackageChangePlan> Plan(PackageRelease root, Dictionary<string, InstalledPackage> installed = null,
            Func<CatalogEntry, string, Task<PackageRelease>> find = null, bool switchSource = false, params CatalogEntry[] extra) =>
            PackageChangePlanner.Build(Entry(), root, new[] { Entry() }.Concat(extra).ToList(), installed ?? new Dictionary<string, InstalledPackage>(),
                find ?? ((e, v) => Task.FromResult(Release(v))), "6000.4.12f1", switchSource);

        [Test]
        public async Task DependencyClosurePinsCommitAndDoesNotMutateManifest()
        {
            string manifest = File.ReadAllText("Packages/manifest.json");
            var plan = await Plan(Release("1.0.0", ("com.dreamy.dependency", "1.0.0")), extra: new[] { Entry("com.dreamy.dependency") });
            Assert.That(plan.CanApply, Is.True, plan.Summary);
            Assert.That(plan.Changes.Select(c => c.Id), Is.EqualTo(new[] { "com.dreamy.dependency", TestId }));
            Assert.That(plan.Changes.All(c => c.Value.EndsWith("#" + new string('a', 40))), Is.True);
            Assert.That(File.ReadAllText("Packages/manifest.json"), Is.EqualTo(manifest));
        }
        [Test]
        public async Task LocalRootRequiresExplicitSourceSwitch()
        {
            var local = new Dictionary<string, InstalledPackage> { [TestId] = Installed(TestId, source: "Local") };
            Assert.That((await Plan(Release(), local)).CanApply, Is.False);
            Assert.That((await Plan(Release(), local, switchSource: true)).CanApply, Is.True);
        }
        [Test]
        public async Task LocalDependencyIsNeverSilentlySwitched()
        {
            var dep = "com.dreamy.dependency";
            var current = new Dictionary<string, InstalledPackage> { [dep] = Installed(dep, "0.1.0", "Local") };
            var plan = await Plan(Release("1.0.0", (dep, "1.0.0")), current, switchSource: true, extra: new[] { Entry(dep) });
            Assert.That(plan.CanApply, Is.False);
            Assert.That(plan.Summary, Does.Contain("local/embedded"));
        }
        [Test]
        public async Task SufficientInstalledLocalDependencyIsPreserved()
        {
            var dep = "com.dreamy.dependency";
            var current = new Dictionary<string, InstalledPackage> { [dep] = Installed(dep, "1.0.0", "Local") };
            var plan = await Plan(Release("1.0.0", (dep, "1.0.0")), current);
            Assert.That(plan.CanApply, Is.True);
            Assert.That(plan.Changes.Count, Is.EqualTo(1));
        }
        [Test]
        public async Task UnmappedDependencyBlocksApply()
        {
            var plan = await Plan(Release("1.0.0", ("com.vendor.unknown", "1.0.0")));
            Assert.That(plan.CanApply, Is.False);
            Assert.That(plan.Summary, Does.Contain("No source mapping"));
        }
        [Test]
        public async Task DowngradeCannotBreakInstalledDependent()
        {
            var owner = Installed("com.dreamy.owner"); owner.Dependencies[TestId] = "2.0.0";
            var plan = await Plan(Release(), new Dictionary<string, InstalledPackage> { [owner.Id] = owner });
            Assert.That(plan.CanApply, Is.False);
            Assert.That(plan.Summary, Does.Contain("requires"));
        }
        [Test]
        public async Task DependencyCycleBlocksApply()
        {
            var plan = await Plan(Release("1.0.0", ("com.dreamy.dep", "1.0.0")), find: (e, v) => Task.FromResult(Release(v, (TestId, "1.0.0"))), extra: new[] { Entry("com.dreamy.dep") });
            Assert.That(plan.CanApply, Is.False);
            Assert.That(plan.Summary, Does.Contain("cycle"));
        }
        [Test]
        public async Task UnityIncompatibleReleaseBlocksApply()
        {
            var release = Release(); release.Unity = "6000.5";
            Assert.That((await Plan(release)).CanApply, Is.False);
        }
        [Test]
        public async Task DifferentRepositoryRequiresSourceSwitch()
        {
            var current = Installed(TestId); current.ManifestValue = "https://github.com/other/fork.git#v1.0.0";
            Assert.That((await Plan(Release(), new Dictionary<string, InstalledPackage> { [TestId] = current })).CanApply, Is.False);
        }
        [Test]
        public void RecoveryRejectsUnrelatedManifestChanges()
        {
            const string before = "{\"dependencies\":{\"com.test.a\":\"1.0.0\"}}";
            var changes = new[] { new PackageChange { Id = "com.test.a", Value = "2.0.0" } };
            Assert.That(PackageChangePlanner.ManifestMatchesIntent(before, "{\"dependencies\":{\"com.test.a\":\"2.0.0\"}}", changes, null), Is.True);
            Assert.That(PackageChangePlanner.ManifestMatchesIntent(before, "{\"dependencies\":{\"com.test.a\":\"2.0.0\",\"com.test.b\":\"1.0.0\"}}", changes, null), Is.False);
        }

        private sealed class Transport : HttpMessageHandler
        {
            public int Calls;
            public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Send;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
            { Calls++; return Send(request, cancellation); }
        }
        private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(json) };
        private static string Metadata(string version) => JsonConvert.SerializeObject(new
        {
            content = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new
            { name = TestId, version, unity = "6000.0", dependencies = new Dictionary<string, string>() })))
        });
        [Test]
        public async Task HighestTagIsValidatedAndDuplicateChecksAreDeduplicated()
        {
            var handler = new Transport { Send = async (request, cancel) =>
            {
                await Task.Yield();
                if (request.RequestUri.AbsolutePath.EndsWith("/tags")) return Json("[{\"name\":\"v1.9.0\",\"commit\":{\"sha\":\"a\"}},{\"name\":\"v1.10.0\",\"commit\":{\"sha\":\"b\"}}]");
                return Json(Metadata("1.10.0"));
            } };
            using var provider = new PackageReleases(handler);
            var first = provider.Check(Entry(), true);
            var second = provider.Check(Entry(), true);
            Assert.That(second, Is.SameAs(first));
            var result = await first;
            Assert.That(result.Error, Is.Null);
            Assert.That(result.Latest(false).Version, Is.EqualTo("1.10.0"));
            Assert.That(handler.Calls, Is.EqualTo(2));
        }
        [Test]
        public async Task TagVersionMismatchDoesNotBecomeLatest()
        {
            var handler = new Transport { Send = (request, cancel) => Task.FromResult(request.RequestUri.AbsolutePath.EndsWith("/tags")
                ? Json("[{\"name\":\"v1.0.0\",\"commit\":{\"sha\":\"a\"}}]") : Json(Metadata("0.1.0"))) };
            using var provider = new PackageReleases(handler);
            var result = await provider.Check(Entry(), true);
            Assert.That(result.Latest(false), Is.Null);
            Assert.That(result.Releases.Single().InvalidReason, Does.Contain("does not match"));
        }
        [TestCase(401)]
        [TestCase(403)]
        [TestCase(429)]
        public async Task AuthenticationAndRateLimitAreErrorsNotUpToDate(int status)
        {
            var handler = new Transport { Send = (request, cancel) => Task.FromResult(Json("{}", (HttpStatusCode)status)) };
            using var provider = new PackageReleases(handler);
            var result = await provider.Check(Entry(), true);
            Assert.That(result.Error, Does.Contain(status.ToString()));
            Assert.That(result.Latest(false), Is.Null);
        }
        [Test]
        public async Task OfflineRefreshRetainsCachedVersionWithExplicitError()
        {
            Directory.CreateDirectory(PackageCatalog.CachePath);
            File.WriteAllText(PackageReleases.CacheFile(TestId), JsonConvert.SerializeObject(new PackageReleaseIndex
            { Id = TestId, SourceKey = PackageReleases.Key(Entry()), CheckedAt = "2026-11-01T00:00:00Z", Releases = new List<PackageRelease> { Release() } }));
            var handler = new Transport { Send = (r, c) => throw new HttpRequestException("offline") };
            using var provider = new PackageReleases(handler);
            provider.LoadCache(new[] { Entry() });
            var result = await provider.Check(Entry(), true);
            Assert.That(result.Latest(false).Version, Is.EqualTo("1.0.0"));
            Assert.That(result.FromCache, Is.True);
            Assert.That(result.Error, Does.Contain("Network request failed"));
        }
        [Test]
        public async Task ClosingOwnerCancelsLookup()
        {
            var handler = new Transport { Send = async (r, c) => { await Task.Delay(10000, c); return Json("[]"); } };
            var provider = new PackageReleases(handler);
            var task = provider.Check(Entry(), true);
            provider.Dispose();
            try { await task; Assert.Fail("Expected lookup cancellation."); }
            catch (OperationCanceledException) { /* Expected owner cancellation. */ }
        }
    }
}
