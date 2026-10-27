using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace StudioEditor.ReferenceModels
{
    public readonly struct ReferenceModelLoadProgress
    {
        public ReferenceModelLoadProgress(float fraction, string stage, string detail,
            double elapsedSeconds, int cacheHits, int cacheMisses)
        {
            Fraction = fraction;
            Stage = stage ?? string.Empty;
            Detail = detail ?? string.Empty;
            ElapsedSeconds = elapsedSeconds;
            CacheHits = cacheHits;
            CacheMisses = cacheMisses;
        }
        public float Fraction { get; }
        public string Stage { get; }
        public string Detail { get; }
        public double ElapsedSeconds { get; }
        public int CacheHits { get; }
        public int CacheMisses { get; }
    }

    public sealed class ReferenceModelLoadContext
    {
        private static readonly AsyncLocal<ReferenceModelLoadContext> ambient =
            new AsyncLocal<ReferenceModelLoadContext>();
        private readonly IProgress<ReferenceModelLoadProgress> progress;
        private readonly Stopwatch timer = Stopwatch.StartNew();
        private readonly ConcurrentDictionary<string, byte[]> sourceBytes =
            new ConcurrentDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        private readonly object progressLock = new object();
        private float start;
        private float end = 1f;
        private float fraction;
        private int cacheHits;
        private int cacheMisses;
        private readonly Dictionary<string, double> stageTimes = new Dictionary<string, double>();
        private string currentStage;
        private double stageStarted;

        internal ReferenceModelLoadContext(CancellationToken cancellationToken,
            IProgress<ReferenceModelLoadProgress> progress)
        {
            CancellationToken = cancellationToken;
            this.progress = progress;
        }

        public static ReferenceModelLoadContext Current => ambient.Value;
        public CancellationToken CancellationToken { get; }
        public double ElapsedSeconds => timer.Elapsed.TotalSeconds;
        public int CacheHits => cacheHits;
        public int CacheMisses => cacheMisses;

        internal string TimingSummary
        {
            get
            {
                var times = new Dictionary<string, double>(stageTimes);
                if (currentStage != null)
                    times[currentStage] = (times.TryGetValue(currentStage, out var value) ? value : 0d) +
                        ElapsedSeconds - stageStarted;
                return string.Join(", ", times.OrderByDescending(pair => pair.Value)
                    .Select(pair => $"{pair.Key}={pair.Value:F2}s"));
            }
        }

        internal static void SetCurrent(ReferenceModelLoadContext context) => ambient.Value = context;

        public IDisposable Range(float from, float to)
        {
            var previousStart = start;
            var previousEnd = end;
            var width = end - start;
            end = previousStart + width * to;
            start = previousStart + width * from;
            return new Scope(() => { start = previousStart; end = previousEnd; });
        }

        public void Report(float value, string stage, string detail = null)
        {
            CancellationToken.ThrowIfCancellationRequested();
            lock (progressLock)
            {
                if (stage != currentStage)
                {
                    var elapsed = ElapsedSeconds;
                    if (currentStage != null)
                        stageTimes[currentStage] = (stageTimes.TryGetValue(currentStage, out var time) ? time : 0d) +
                            elapsed - stageStarted;
                    currentStage = stage;
                    stageStarted = elapsed;
                }
                fraction = Math.Max(fraction, start + (end - start) * Math.Max(0f, Math.Min(1f, value)));
                progress?.Report(new ReferenceModelLoadProgress(fraction, stage, detail,
                    ElapsedSeconds, cacheHits, cacheMisses));
            }
        }

        public void RecordCache(bool hit)
        {
            if (hit) Interlocked.Increment(ref cacheHits);
            else Interlocked.Increment(ref cacheMisses);
        }

        public byte[] ReadSourceBytes(string path)
        {
            CancellationToken.ThrowIfCancellationRequested();
            return sourceBytes.GetOrAdd(Path.GetFullPath(path), File.ReadAllBytes);
        }

        public static async Task YieldAsync(CancellationToken token, bool asynchronous = true)
        {
            token.ThrowIfCancellationRequested();
            if (asynchronous) await Task.Yield();
            token.ThrowIfCancellationRequested();
        }

        private sealed class Scope : IDisposable
        {
            private Action restore;
            public Scope(Action restore) { this.restore = restore; }
            public void Dispose() { restore?.Invoke(); restore = null; }
        }
    }

    public static class ReferenceModelLoadPipeline
    {
        // Serialize Unity loading, including cancellation cleanup, across controllers.
        private static readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);

        public static async Task<T> RunAsync<T>(string sourcePath,
            Func<CancellationToken, Task<T>> load,
            CancellationToken token,
            IProgress<ReferenceModelLoadProgress> progress = null)
        {
            StudioEditorDiskCache.Initialize();
            await gate.WaitAsync(token);
            var previous = ReferenceModelLoadContext.Current;
            var context = new ReferenceModelLoadContext(token, progress);
            ReferenceModelLoadContext.SetCurrent(context);
            try
            {
                context.Report(0f, "Preparing", Path.GetFileName(sourcePath));
                await ReferenceModelLoadContext.YieldAsync(token);
                var result = await load(token);
                // The caller owns the result and disposes it if cancellation arrived
                // at the final boundary; do not throw here and lose that ownership.
                if (!token.IsCancellationRequested)
                    context.Report(1f, "Finishing", Path.GetFileName(sourcePath));
                return result;
            }
            finally
            {
                Debug.Log($"Studio Editor import '{Path.GetFileName(sourcePath)}': " +
                    $"{context.ElapsedSeconds:F2}s, cache hits={context.CacheHits}, " +
                    $"misses={context.CacheMisses}, cancelled={token.IsCancellationRequested}; " + context.TimingSummary);
                ReferenceModelLoadContext.SetCurrent(previous);
                gate.Release();
            }
        }
    }
}
