using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace StudioEditor.ReferenceModels
{
    // Character cards do not contain Studio's global shader settings. Give
    // their materials the game's defaults; a scene's property blocks override
    // these without introducing a second light rig for standalone imports.
    internal static class KoikatsuCharacterShading
    {
        internal static async Task ConfigureAsync(KoikatsuListCatalog catalog, string abdataRoot,
            GameObject root, ICollection<KoikatsuAssetBundleLease> leases,
            CancellationToken token, bool asynchronous)
        {
            if (catalog == null || !catalog.TryGet(432, 1, out var entry)) return;
            foreach (var source in catalog.ResolveBundleCandidates(abdataRoot, entry.Get("MainTexAB"), entry.Archive))
            {
                token.ThrowIfCancellationRequested();
                var lease = asynchronous ? await KoikatsuAssetBundleCache.AcquireAsync(source, token)
                    : KoikatsuAssetBundleCache.Acquire(source);
                leases.Add(lease);
                Texture2D ramp;
                if (asynchronous)
                {
                    var request = lease.Bundle.LoadAssetAsync<Texture2D>(entry.Get("MainTex"));
                    while (!request.isDone) await Task.Yield();
                    token.ThrowIfCancellationRequested();
                    ramp = request.asset as Texture2D;
                }
                else ramp = lease.Bundle.LoadAsset<Texture2D>(entry.Get("MainTex"));
                if (ramp == null) continue;
                // MaterialEditor may have instantiated converted materials.
                // Use the renderer's current materials, not the earlier owners list.
                ApplyDefaults(root.GetComponentsInChildren<Renderer>(true)
                    .SelectMany(renderer => renderer.sharedMaterials).Distinct(), ramp);
                return;
            }
        }

        internal static void ApplyDefaults(IEnumerable<Material> materials, Texture ramp)
        {
            if (ramp == null) return;
            foreach (var material in materials)
            {
                if (material == null || !material.HasProperty("_SourceCharacterModel") ||
                    material.GetFloat("_SourceCharacterModel") < .5f) continue;
                material.SetFloat("_SourceCharacterShading", 1f);
                material.SetTexture("_SourceRamp", ramp);
                // Config.EtceteraSystem.Init / Studio.SceneInfo.Init.
                material.SetVector("_SourceAmbientShadow", new Vector4(128f / 255, 128f / 255, 128f / 255, .74f));
                material.SetVector("_SourceLineColor", new Vector4(.5f, .5f, .5f, 0f));
                material.SetFloat("_SourceLineWidth", .307f);
            }
        }
    }
}
