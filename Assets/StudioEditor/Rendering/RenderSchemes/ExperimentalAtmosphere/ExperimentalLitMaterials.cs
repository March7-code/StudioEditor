using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace StudioEditor.Rendering.Experimental
{
    /// <summary>Camera-scoped local lighting needs a Lit variant. Keep all source materials for restoration.</summary>
    internal sealed class ExperimentalLitMaterials
    {
        private readonly Dictionary<Material, Material> copies = new Dictionary<Material, Material>();
        private readonly List<(Renderer renderer, int slot, Material original, Material copy)> bindings =
            new List<(Renderer, int, Material, Material)>();
        private float nextScan;
        public void Tick()
        {
            if (Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + 0.5f;
            if (!SystemInfo.supportsRayTracing) return;
            bindings.RemoveAll(binding => binding.renderer == null);
            var retained = new HashSet<Material>();
            foreach (var binding in bindings) retained.Add(binding.copy);
            var unused = new List<Material>();
            foreach (var entry in copies)
                if (!retained.Contains(entry.Value)) unused.Add(entry.Key);
            foreach (var key in unused) { CoreUtils.Destroy(copies[key]); copies.Remove(key); }
            var shader = Resources.Load<Shader>("StudioEditorAtmosphere/LocalLit");
            if (shader == null) return;
            foreach (var renderer in Object.FindObjectsByType<Renderer>())
            {
                var materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    var original = materials[i];
                    if (original == null || original.shader.name != "Universal Render Pipeline/Lit") continue;
                    if (!copies.TryGetValue(original, out var copy))
                    {
                        var keywords = original.shaderKeywords;
                        copy = new Material(original) { shader = shader, name = original.name + " (local lighting)", hideFlags = HideFlags.DontSave };
                        copy.shaderKeywords = keywords;
                        copies.Add(original, copy);
                    }
                    materials[i] = copy; changed = true;
                    bindings.Add((renderer, i, original, copy));
                }
                if (changed) renderer.sharedMaterials = materials;
            }
        }
        public void Restore()
        {
            foreach (var binding in bindings)
            {
                if (binding.renderer == null) continue;
                var materials = binding.renderer.sharedMaterials;
                if (binding.slot < materials.Length && materials[binding.slot] == binding.copy)
                {
                    materials[binding.slot] = binding.original;
                    binding.renderer.sharedMaterials = materials;
                }
            }
            foreach (var copy in copies.Values) CoreUtils.Destroy(copy);
            bindings.Clear(); copies.Clear(); nextScan = 0;
        }
    }
}
