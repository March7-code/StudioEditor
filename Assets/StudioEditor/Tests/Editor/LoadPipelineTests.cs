using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.Serialization;
using NUnit.Framework;
using StudioEditor.ReferenceModels;

namespace StudioEditor.Tests
{
    public sealed class LoadPipelineTests
    {
        private string directory;
        private DiskCacheStore cache;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "StudioEditorCacheTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            cache = new DiskCacheStore(Path.Combine(directory, "Cache"));
        }

        [TearDown]
        public void TearDown() => Directory.Delete(directory, true);

        [Test]
        public void CorruptPayloadIsRejectedAndReplaced()
        {
            var payload = new byte[] { 1, 2, 3 };
            Assert.That(cache.Write("Lists", "v1", payload), Is.True);
            var path = cache.GetPath("Lists", "v1");
            var bytes = File.ReadAllBytes(path);
            bytes[bytes.Length - 1] ^= 1;
            File.WriteAllBytes(path, bytes);
            Assert.That(cache.TryRead("Lists", "v1", out _), Is.False);
            Assert.That(cache.Write("Lists", "v1", payload), Is.True);
            Assert.That(cache.TryRead("Lists", "v1", out var restored), Is.True);
            Assert.That(restored, Is.EqualTo(payload));
            Assert.That(cache.TryRead("Lists", "v2", out _), Is.False);
        }

        [Test]
        public void TruncatedCacheIsRejected()
        {
            cache.Write("Lists", "key", new byte[] { 1 });
            File.WriteAllBytes(cache.GetPath("Lists", "key"), new byte[8]);
            Assert.That(cache.TryRead("Lists", "key", out _), Is.False);
        }

        [Test]
        public void InstallationMovePreservesKeyAndResourceEditInvalidatesIt()
        {
            var first = Path.Combine(directory, "first", "abdata", "asset.bin");
            var second = Path.Combine(directory, "second", "abdata", "asset.bin");
            Directory.CreateDirectory(Path.GetDirectoryName(first));
            Directory.CreateDirectory(Path.GetDirectoryName(second));
            File.WriteAllBytes(first, new byte[] { 1 });
            File.Copy(first, second);
            File.SetLastWriteTimeUtc(second, File.GetLastWriteTimeUtc(first));
            Assert.That(DiskCacheStore.SourceFingerprint(first), Is.EqualTo(DiskCacheStore.SourceFingerprint(second)));
            File.AppendAllText(second, "changed");
            Assert.That(DiskCacheStore.SourceFingerprint(first), Is.Not.EqualTo(DiskCacheStore.SourceFingerprint(second)));
        }

        [Test]
        public void CancelledWriteLeavesNoPartialCache()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                Assert.Throws<OperationCanceledException>(() => cache.Write("Lists", "key", new byte[] { 1 }, cancellation.Token));
            }
            Assert.That(cache.TryRead("Lists", "key", out _), Is.False);
            Assert.That(Directory.Exists(cache.DirectoryPath), Is.False);
        }

        [Test]
        public async Task ConcurrentWritesProduceAValidPayload()
        {
            await Task.WhenAll(Enumerable.Range(0, 8).Select(index => Task.Run(() =>
                cache.Write("Lists", "same", new byte[] { (byte)index }))));
            Assert.That(cache.TryRead("Lists", "same", out var payload), Is.True);
            Assert.That(payload.Length, Is.EqualTo(1));
            Assert.That(Directory.GetFiles(cache.DirectoryPath, "*.building-*", SearchOption.AllDirectories), Is.Empty);
        }

        [Test]
        public void CacheCategoryCannotEscapeDirectory()
        {
            Assert.Throws<ArgumentException>(() => cache.GetPath("../outside", "key"));
        }

        [Test]
        public void ParsedMetadataPreservesReadonlyCollectionsAndVectors()
        {
            StudioEditorDiskCache.Initialize();
            var path = Path.Combine(directory, "source.bin");
            File.WriteAllBytes(path, new byte[] { 1 });
            var source = new KoikatsuBundleSource(path);
            var identity = "test-" + Guid.NewGuid().ToString("N");
            var expected = new MetadataDescriptor(new[]
            {
                new KoikatsuDynamicBoneColliderDefinition("root", new UnityEngine.Vector3(1f, 2f, 3f),
                    0.25f, 0.5f, 1, 0),
            });
            var key = "metadata-v1|" + DiskCacheStore.SourceFingerprint(path) + "|0||" + identity;
            try
            {
                KoikatsuParsedMetadataCache.Read(source, identity, () => expected);
                var restored = KoikatsuParsedMetadataCache.Read<MetadataDescriptor>(source, identity,
                    () => throw new Exception("Expected a disk cache hit."));
                Assert.That(restored.Colliders.Count, Is.EqualTo(1));
                Assert.That(restored.Colliders[0].Center, Is.EqualTo(new UnityEngine.Vector3(1f, 2f, 3f)));
                Assert.That(restored.Colliders[0].Radius, Is.EqualTo(0.25f));
            }
            finally
            {
                var cachedPath = StudioEditorDiskCache.Store.GetPath("Metadata", key);
                if (File.Exists(cachedPath)) File.Delete(cachedPath);
            }
        }

        [DataContract]
        private sealed class MetadataDescriptor
        {
            public MetadataDescriptor(IEnumerable<KoikatsuDynamicBoneColliderDefinition> values)
            { Colliders = values.ToArray(); }

            [field: DataMember]
            public IReadOnlyList<KoikatsuDynamicBoneColliderDefinition> Colliders { get; }
        }

        [Test]
        public async Task ProgressIsMonotonicAndContextFlowsToWorkers()
        {
            var progress = new Recorder();
            var result = await ReferenceModelLoadPipeline.RunAsync("scene", async token =>
            {
                var context = ReferenceModelLoadContext.Current;
                using (context.Range(0.2f, 0.6f))
                {
                    context.Report(0.5f, "Loading");
                    using (context.Range(0f, 0.5f)) context.Report(0.1f, "Nested");
                    context.Report(1f, "Loaded");
                }
                Assert.That(await Task.Run(() => ReferenceModelLoadContext.Current == context, token), Is.True);
                return 7;
            }, default, progress);
            Assert.That(result, Is.EqualTo(7));
            Assert.That(ReferenceModelLoadContext.Current, Is.Null);
            Assert.That(progress.Values.Zip(progress.Values.Skip(1), (first, next) => next.Fraction >= first.Fraction).All(value => value), Is.True);
            Assert.That(progress.Values.Any(value => Math.Abs(value.Fraction - 0.4f) < 0.001f), Is.True);
        }

        [Test]
        public async Task CancelledQueuedImportDoesNotEnterAndQueueRemainsUsable()
        {
            var started = new TaskCompletionSource<bool>();
            var release = new TaskCompletionSource<bool>();
            var owner = ReferenceModelLoadPipeline.RunAsync("first", async token =>
            { started.SetResult(true); await release.Task; return 1; }, default);
            await started.Task;
            try
            {
                using (var cancellation = new CancellationTokenSource())
                {
                    var entered = false;
                    var waiting = ReferenceModelLoadPipeline.RunAsync("queued", token =>
                    { entered = true; return Task.FromResult(2); }, cancellation.Token);
                    cancellation.Cancel();
                    try { await waiting; Assert.Fail("Expected cancellation."); }
                    catch (OperationCanceledException) { }
                    Assert.That(entered, Is.False);
                }
            }
            finally { release.TrySetResult(true); await owner; }
            Assert.That(await ReferenceModelLoadPipeline.RunAsync("next", token => Task.FromResult(3), default), Is.EqualTo(3));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CancellationAfterAdapterReturnsDisposesResultAndRestoresState(bool keepExisting)
        {
            var owner = new UnityEngine.GameObject("Load cancellation test");
            try
            {
                var controller = owner.AddComponent<SceneContentController>();
                var adapter = new CancellationAdapter(controller);
                controller.RegisterAdapter(adapter);
                if (keepExisting) Assert.That(await controller.ImportAsync("first.loadprobe"), Is.True);
                var previous = controller.Current;
                adapter.CancelOnReturn = true;
                Assert.That(await controller.ImportAsync("cancelled.loadprobe"), Is.False);
                Assert.That(adapter.Last.Disposed, Is.True);
                Assert.That(controller.Current, Is.SameAs(previous));
                Assert.That(controller.ManagedImports.Count, Is.EqualTo(keepExisting ? 1 : 0));
                Assert.That(controller.Status, Is.EqualTo(keepExisting
                    ? ReferenceModelImportStatus.Ready : ReferenceModelImportStatus.Idle));
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }

        private sealed class CancellationAdapter : IReferenceModelFormatAdapter
        {
            private readonly SceneContentController controller;
            public CancellationAdapter(SceneContentController controller) { this.controller = controller; }
            public string FormatName => "Cancellation probe";
            public IReadOnlyList<string> FileExtensions => new[] { ".loadprobe" };
            public bool CancelOnReturn { get; set; }
            public CancellationInstance Last { get; private set; }
            public Task<IReferenceModelInstance> ImportAsync(string path, UnityEngine.Transform parent, CancellationToken token)
            {
                Last = new CancellationInstance(parent);
                if (CancelOnReturn) controller.CancelLoad();
                return Task.FromResult<IReferenceModelInstance>(Last);
            }
        }

        private sealed class CancellationInstance : IReferenceModelInstance
        {
            public CancellationInstance(UnityEngine.Transform parent)
            {
                Root = new UnityEngine.GameObject("Imported probe");
                Root.transform.SetParent(parent, false);
            }
            public string DisplayName => "Imported probe";
            public UnityEngine.GameObject Root { get; }
            public bool Disposed { get; private set; }
            public void Dispose() { Disposed = true; UnityEngine.Object.DestroyImmediate(Root); }
        }

        private sealed class Recorder : IProgress<ReferenceModelLoadProgress>
        {
            public readonly List<ReferenceModelLoadProgress> Values = new List<ReferenceModelLoadProgress>();
            public void Report(ReferenceModelLoadProgress value) => Values.Add(value);
        }
    }
}
