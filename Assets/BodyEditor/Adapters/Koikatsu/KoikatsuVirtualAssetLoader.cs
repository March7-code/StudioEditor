using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BodyEditor.ReferenceModels
{
    internal static class KoikatsuVirtualAssetLoader
    {
        public static KoikatsuAssetBundleLease AcquireAsset<T>(
            IReadOnlyList<KoikatsuBundleSource> sources,
            string assetName,
            out T asset,
            out KoikatsuBundleSource loadedSource)
            where T : Object
        {
            if (sources == null)
            {
                throw new ArgumentNullException(nameof(sources));
            }

            for (var index = 0; index < sources.Count; index++)
            {
                var source = sources[index];
                if (source == null || !File.Exists(source.FilePath))
                {
                    continue;
                }

                var lease = KoikatsuAssetBundleCache.Acquire(source);
                if (!lease.Bundle.Contains(assetName))
                {
                    lease.Dispose();
                    continue;
                }

                asset = lease.Bundle.LoadAsset<T>(assetName);
                if (asset != null)
                {
                    loadedSource = source;
                    return lease;
                }

                lease.Dispose();
            }

            asset = null;
            loadedSource = null;
            return null;
        }

        public static KoikatsuAssetBundleLease AcquireFirst(
            IReadOnlyList<KoikatsuBundleSource> sources,
            out KoikatsuBundleSource loadedSource)
        {
            if (sources == null)
            {
                throw new ArgumentNullException(nameof(sources));
            }

            for (var index = 0; index < sources.Count; index++)
            {
                if (sources[index] == null ||
                    !File.Exists(sources[index].FilePath))
                {
                    continue;
                }

                loadedSource = sources[index];
                return KoikatsuAssetBundleCache.Acquire(loadedSource);
            }

            loadedSource = null;
            return null;
        }
    }
}
