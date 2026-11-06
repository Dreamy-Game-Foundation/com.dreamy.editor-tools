using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Dreamy.EditorTools.Package
{
    internal sealed class PackageRelease
    {
        public string Version;
        public string Tag;
        public string Revision;
        public string Unity;
        public string Description;
        public Dictionary<string, string> Dependencies;
        public bool Verified;
        public bool EditorCompatible = true;
        public string InvalidReason;
    }

    internal sealed class PackageReleaseIndex
    {
        public string Id;
        public string SourceKey;
        public string CheckedAt;
        public List<PackageRelease> Releases = new();
        public string Error;
        [JsonIgnore] public bool FromCache;
        internal PackageRelease Latest(bool previews) => Releases.FirstOrDefault(r => r.Verified &&
            PackageVersion.TryParse(r.Version, out var v) && (previews || !v.IsPreview));
    }

    /// <summary>Window-owned metadata lookups. Package mutations belong to PackageOperations.</summary>
    internal sealed class PackageReleases : IDisposable
    {
        private readonly HttpClient http;
        private readonly SemaphoreSlim network = new(3);
        private readonly CancellationTokenSource lifetime = new();
        private readonly Dictionary<string, Task<PackageReleaseIndex>> inFlight = new();
        private readonly Dictionary<string, PackageReleaseIndex> indices = new();
        internal IReadOnlyDictionary<string, PackageReleaseIndex> Indices => indices;
        internal static string Key(CatalogEntry entry) => entry.Source + "|" + entry.Repository + "|" + entry.PackagePath;
        internal static string CacheFile(string id) => Path.Combine(PackageCatalog.CachePath, id + ".json");

        internal PackageReleases(HttpMessageHandler handler = null)
        {
            http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(25) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Dreamy-Package-Manager/1.0");
        }

        internal void LoadCache(IEnumerable<CatalogEntry> entries)
        {
            var list = entries.ToList();
            foreach (var id in indices.Keys.ToArray())
                if (!list.Any(e => e.Id == id && Key(e) == indices[id].SourceKey)) indices.Remove(id);
            foreach (var entry in list)
            {
                try
                {
                    if (!File.Exists(CacheFile(entry.Id))) continue;
                    var cached = JsonConvert.DeserializeObject<PackageReleaseIndex>(File.ReadAllText(CacheFile(entry.Id)));
                    if (cached == null || cached.Id != entry.Id || cached.SourceKey != Key(entry)) continue;
                    cached.FromCache = true;
                    indices[entry.Id] = cached;
                }
                catch (Exception e) when (e is IOException || e is JsonException) { /* Invalid cache is fetched again. */ }
            }
        }

        internal Task<PackageReleaseIndex> Check(CatalogEntry entry, bool force = false)
        {
            if (inFlight.TryGetValue(entry.Id, out var pending)) return pending;
            if (!force && indices.TryGetValue(entry.Id, out var existing) && existing.SourceKey == Key(entry) && string.IsNullOrEmpty(existing.Error))
                return Task.FromResult(existing);
            var task = Fetch(entry);
            inFlight[entry.Id] = task;
            return task;
        }

        private async Task<PackageReleaseIndex> Fetch(CatalogEntry entry)
        {
            // Always yield so Check registers the in-flight operation before its finally runs.
            await Task.Yield();
            var result = new PackageReleaseIndex { Id = entry.Id, SourceKey = Key(entry) };
            try
            {
                if (entry.Source == "registry")
                {
                    var found = await SearchRegistry(entry.Id, lifetime.Token);
                    var package = found.FirstOrDefault(p => p.name == entry.Id);
                    if (package == null) throw new InvalidDataException("Package was not found in configured registries.");
                    var compatible = new HashSet<string>(package.versions.compatible ?? Array.Empty<string>());
                    result.Releases = (package.versions.all ?? new[] { package.version }).Distinct()
                        .Where(v => PackageVersion.TryParse(v, out _)).Select(v => new PackageRelease
                        {
                            Version = v, Tag = v, Revision = v, EditorCompatible = compatible.Contains(v),
                            Verified = v == package.version, Description = package.description,
                            Dependencies = v == package.version ? package.dependencies.ToDictionary(d => d.name, d => d.version) : null
                        }).ToList();
                    foreach (bool preview in new[] { false, true })
                    {
                        var latest = result.Releases.Where(r => { PackageVersion.TryParse(r.Version, out var v); return v.IsPreview == preview; })
                            .OrderByDescending(r => { PackageVersion.TryParse(r.Version, out var v); return v; }).FirstOrDefault();
                        if (latest != null) await Verify(entry, latest);
                    }
                }
                else
                {
                    var repo = new Uri(entry.Repository).AbsolutePath.Trim('/');
                    bool complete = false;
                    for (int page = 1; page <= 20; page++)
                    {
                        var tags = JArray.Parse(await Get($"https://api.github.com/repos/{repo}/tags?per_page=100&page={page}", lifetime.Token));
                        foreach (var tag in tags)
                            if (PackageVersion.TryParse((string)tag["name"], out _))
                                result.Releases.Add(new PackageRelease { Version = ((string)tag["name"]).TrimStart('v'), Tag = (string)tag["name"], Revision = (string)tag["commit"]?["sha"] });
                        if (tags.Count < 100) { complete = true; break; }
                    }
                    if (!complete) throw new InvalidDataException("Tag list exceeds 2,000 entries; latest cannot be established.");
                }
                result.Releases = result.Releases.OrderByDescending(r => { PackageVersion.TryParse(r.Version, out var v); return v; }).ToList();
                if (entry.Source == "git")
                {
                    // Verify the highest valid stable and preview independently; retain diagnostics for bad tags.
                    foreach (bool preview in new[] { false, true })
                    {
                        foreach (var release in result.Releases.Where(r => { PackageVersion.TryParse(r.Version, out var v); return v.IsPreview == preview; }))
                        {
                            await Verify(entry, release);
                            if (release.Verified) break;
                        }
                    }
                }
                result.CheckedAt = DateTime.UtcNow.ToString("O");
                indices[entry.Id] = result;
                Save(result);
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { throw; }
            catch (Exception e)
            {
                if (indices.TryGetValue(entry.Id, out var cached) && cached.SourceKey == Key(entry))
                {
                    result = cached;
                    result.FromCache = true;
                }
                result.Error = SafeError(e);
                indices[entry.Id] = result;
            }
            finally { inFlight.Remove(entry.Id); }
            return result;
        }

        internal async Task<PackageRelease> Verify(CatalogEntry entry, PackageRelease release)
        {
            if (release.Verified || release.InvalidReason != null) return release;
            if (entry.Source == "registry")
            {
                var found = await SearchRegistry(entry.Id + "@" + release.Version, lifetime.Token);
                var package = found.FirstOrDefault(p => p.name == entry.Id && p.version == release.Version);
                if (package == null) { release.InvalidReason = "Registry did not return the requested exact version."; return release; }
                release.Description = package.description;
                release.Dependencies = package.dependencies.ToDictionary(d => d.name, d => d.version);
                release.Verified = true;
                return release;
            }
            var repo = new Uri(entry.Repository).AbsolutePath.Trim('/');
            var path = string.IsNullOrEmpty(entry.PackagePath) ? "package.json" : entry.PackagePath.Trim('/') + "/package.json";
            // Contents API resolves annotated tags to the package at the immutable commit returned by tags API.
            var response = JObject.Parse(await Get($"https://api.github.com/repos/{repo}/contents/{path}?ref={Uri.EscapeDataString(release.Revision)}", lifetime.Token));
            var data = JObject.Parse(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String((string)response["content"] ?? "")));
            if ((string)data["name"] != entry.Id || (string)data["version"] != release.Version)
            {
                release.InvalidReason = $"Tag {release.Tag} does not match package name/version.";
                return release;
            }
            release.Unity = (string)data["unity"];
            if (!string.IsNullOrEmpty((string)data["unityRelease"])) release.Unity += "." + (string)data["unityRelease"];
            release.Description = (string)data["description"];
            release.Dependencies = (data["dependencies"] as JObject)?.Properties().ToDictionary(p => p.Name, p => (string)p.Value) ?? new Dictionary<string, string>();
            release.Verified = true;
            return release;
        }

        internal async Task<PackageRelease> Find(CatalogEntry entry, string minimum, bool previews)
        {
            var index = await Check(entry);
            if (!string.IsNullOrEmpty(index.Error)) throw new InvalidOperationException($"{entry.Id}: {index.Error}");
            foreach (var release in index.Releases.Where(r => PackageVersion.Meets(r.Version, minimum) &&
                         PackageVersion.TryParse(r.Version, out var v) && (previews || !v.IsPreview)).Reverse())
            {
                await Verify(entry, release);
                if (release.Verified && release.EditorCompatible && PackageVersion.UnityCompatible(release.Unity, Application.unityVersion))
                { Save(index); return release; }
            }
            throw new InvalidOperationException($"{entry.Id}: no verified compatible release >= {minimum}.");
        }

        internal async Task<string> Get(string url, CancellationToken cancellation)
        {
            await network.WaitAsync(cancellation);
            try
            {
                for (int attempt = 0; ; attempt++)
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    var uri = new Uri(url);
                    // Credentials only leave the process for GitHub's API, never arbitrary catalog endpoints.
                    var token = Environment.GetEnvironmentVariable("DREAMY_GITHUB_TOKEN") ?? Environment.GetEnvironmentVariable("GH_TOKEN") ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN");
                    if (uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo)) throw new InvalidDataException("Use an HTTPS URL without credentials.");
                    if (uri.Host == "api.github.com" && !string.IsNullOrWhiteSpace(token))
                        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellation);
                    if ((int)response.StatusCode >= 500 && attempt == 0) { await Task.Delay(500, cancellation); continue; }
                    if (!response.IsSuccessStatusCode)
                    {
                        string hint = response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.NotFound
                            ? " Check repository access, GitHub rate limit and DREAMY_GITHUB_TOKEN environment variable."
                            : (int)response.StatusCode == 429 ? " Rate limited; retry later." : "";
                        throw new InvalidOperationException($"HTTP {(int)response.StatusCode}.{hint}");
                    }
                    if (response.Content.Headers.ContentLength > 4 * 1024 * 1024) throw new InvalidDataException("Metadata exceeds 4 MiB.");
                    var body = await response.Content.ReadAsStringAsync();
                    if (body.Length > 4 * 1024 * 1024) throw new InvalidDataException("Metadata exceeds 4 MiB.");
                    cancellation.ThrowIfCancellationRequested();
                    return body;
                }
            }
            finally { network.Release(); }
        }

        private static string SafeError(Exception e) => e is TaskCanceledException ? "Request timed out. Retry when connected." :
            e is HttpRequestException ? "Network request failed. Check connection and retry." : e.Message;
        private static void Save(PackageReleaseIndex index)
        {
            Directory.CreateDirectory(PackageCatalog.CachePath);
            File.WriteAllText(CacheFile(index.Id), JsonConvert.SerializeObject(index, Formatting.Indented));
        }

        private static Task<PackageInfo[]> SearchRegistry(string id, CancellationToken cancellation)
        {
            var source = new TaskCompletionSource<PackageInfo[]>();
            var request = Client.Search(id, false);
            void Poll()
            {
                if (!cancellation.IsCancellationRequested && !request.IsCompleted) return;
                EditorApplication.update -= Poll;
                if (cancellation.IsCancellationRequested) source.TrySetCanceled();
                else if (request.Status != StatusCode.Success) source.TrySetException(new InvalidOperationException(request.Error?.message ?? "Registry search failed."));
                else source.TrySetResult(request.Result.Where(p => p.name == id.Split('@')[0]).ToArray());
            }
            EditorApplication.update += Poll;
            return source.Task;
        }

        public void Dispose()
        {
            lifetime.Cancel();
            // HttpClient requests are cancelled above. Do not dispose semaphore while pending finally blocks release it.
            http.Dispose();
        }
    }
}
