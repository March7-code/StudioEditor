using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace StudioEditor.Rendering
{
    // Moving a bundle map into the editor scene does not transfer the source
    // scene's global lightmap table. Keep texture references and remap indices
    // after that scene is unloaded, while retaining the host's existing slots.
    [ExecuteAlways]
    public sealed class ImportedLightmapBinding : MonoBehaviour
    {
        private struct Entry
        {
            public Renderer Renderer;
            public LightmapData Map;
            public Vector4 ScaleOffset;
        }

        private static readonly HashSet<ImportedLightmapBinding> bindings = new();
        private static readonly Dictionary<int, LightmapData> ownedSlots = new();
        private static readonly HashSet<int> vacantSlots = new();
        private static LightmapsMode originalMode;
        private Entry[] entries = System.Array.Empty<Entry>();
        private readonly List<RenderTexture> decoded = new();
        private bool bound;
        public int RendererCount => entries.Length;

        public static ImportedLightmapBinding Capture(GameObject root)
        {
            var maps = LightmapSettings.lightmaps;
            var saved = root.GetComponentsInChildren<Renderer>(true)
                .Where(r => r.lightmapIndex >= 0 && r.lightmapIndex < maps.Length &&
                    maps[r.lightmapIndex]?.lightmapColor != null)
                .Select(r => new Entry { Renderer = r, Map = maps[r.lightmapIndex], ScaleOffset = r.lightmapScaleOffset })
                .ToArray();
            if (saved.Length == 0) return null;
            var binding = root.AddComponent<ImportedLightmapBinding>();
            binding.entries = saved;
            return binding;
        }

        public void DecodeLegacyRgbm(Material decoder)
        {
            var converted = new Dictionary<LightmapData, LightmapData>();
            var active = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    var map = entries[i].Map;
                    if (!converted.TryGetValue(map, out var hdr))
                    {
                        // Modern / HDR mod maps already match URP's encoding.
                        if (!(map.lightmapColor is Texture2D texture) || texture.format != TextureFormat.DXT5)
                            hdr = map;
                        else
                        {
                            var target = new RenderTexture(texture.width, texture.height, 0,
                                RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
                            { name = texture.name + " (decoded source lightmap)", hideFlags = HideFlags.DontSave };
                            target.Create();decoded.Add(target);GL.sRGBWrite = false;
                            Graphics.Blit(texture, target, decoder);
                            // LightmapData requires Texture2D, not RenderTexture.
                            var result = new Texture2D(texture.width, texture.height, TextureFormat.RGBAHalf, false, true)
                            { name = target.name, hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
                            RenderTexture.active = target;
                            result.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);result.Apply(false, true);
                            hdr = new LightmapData { lightmapColor = result, lightmapDir = map.lightmapDir, shadowMask = map.shadowMask };
                            decodedTextures.Add(result);
                        }
                        converted.Add(map, hdr);
                    }
                    entries[i].Map = hdr;
                }
            }
            finally
            {
                GL.sRGBWrite = srgb;RenderTexture.active = active;
                foreach (var temporary in decoded) { temporary.Release(); Destroy(temporary); }
                decoded.Clear();
            }
        }

        private readonly List<Texture2D> decodedTextures = new();

        // Call only after conversion and unloading the source scene, so its
        // temporary table does not get mistaken for a persistent host table.
        public void Bind()
        {
            if (bindings.Count == 0) originalMode = LightmapSettings.lightmapsMode;
            bindings.Add(this);bound = true;
            RebindAll();
        }

        private static bool Same(LightmapData a, LightmapData b) => a != null && b != null &&
            a.lightmapColor == b.lightmapColor && a.lightmapDir == b.lightmapDir && a.shadowMask == b.shadowMask;

        private static void RebindAll()
        {
            var maps = LightmapSettings.lightmaps.ToList();
            var used = new HashSet<int>();
            bool directional = false;
            foreach (var binding in bindings)
            foreach (var entry in binding.entries)
            {
                if (entry.Renderer == null) continue;
                int slot = maps.FindIndex(map => Same(map, entry.Map));
                if (slot < 0)
                {
                    slot = vacantSlots.Where(index => index < maps.Count && maps[index]?.lightmapColor == null)
                        .DefaultIfEmpty(-1).First();
                    if (slot < 0) { slot = maps.Count;maps.Add(entry.Map); }
                    else { maps[slot] = entry.Map;vacantSlots.Remove(slot); }
                    ownedSlots[slot] = entry.Map;
                }
                used.Add(slot);
                directional |= entry.Map.lightmapDir != null;
                entry.Renderer.lightmapIndex = slot;
                entry.Renderer.lightmapScaleOffset = entry.ScaleOffset;
            }
            foreach (var slot in ownedSlots.Keys.ToArray())
            {
                if (used.Contains(slot)) continue;
                if (slot < maps.Count && Same(maps[slot], ownedSlots[slot]))
                { maps[slot] = new LightmapData();vacantSlots.Add(slot); }
                ownedSlots.Remove(slot);
            }
            // Keep holes: compacting the array would invalidate host renderer
            // indices. Empty slots also release references to unloaded maps.
            LightmapSettings.lightmaps = maps.ToArray();
            LightmapSettings.lightmapsMode = directional ? LightmapsMode.CombinedDirectional : originalMode;
        }

        private void OnDestroy()
        {
            if (bound) { bindings.Remove(this);RebindAll(); }
            foreach (var texture in decodedTextures)
                if (Application.isPlaying) Destroy(texture); else DestroyImmediate(texture);
        }
    }
}
