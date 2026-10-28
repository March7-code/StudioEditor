using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.UnifiedRayTracing;

namespace StudioEditor.Rendering.Experimental
{
    /// <summary>GPU copies of hit triangles and base colors. Source meshes need not be CPU readable.</summary>
    internal sealed class RaySurfaceGeometry : IDisposable
    {
        private struct Instance
        {
            public Matrix4x4 LocalToWorld;
            public Vector4 Geometry, LightmapST;
        }
        private struct Surface
        {
            public Vector4 Albedo, Emission, TextureST, Data;
        }
        private readonly List<(Renderer renderer, int offset, int count, int material)> slots =
            new List<(Renderer, int, int, int)>();
        private readonly List<Material> materials = new List<Material>();
        private readonly Dictionary<Material, (int slice, int hash)> textureCopies =
            new Dictionary<Material, (int, int)>();
        private GraphicsBuffer triangles, instances, surfaces;
        private RenderTexture albedo, emissionTextures, bakedLightmaps, bakedDirections;
        private readonly Dictionary<Texture2D, int> lightmapSlices = new();
        private readonly ComputeShader copy;
        private int revision = -1;

        internal static void PrepareMeshBuffers(Mesh mesh)
        {
            // Changing targets may recreate GPU buffers. Do this before registering DXR instances.
            if ((mesh.vertexBufferTarget & GraphicsBuffer.Target.Raw) == 0)
                mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
            if ((mesh.indexBufferTarget & GraphicsBuffer.Target.Raw) == 0)
                mesh.indexBufferTarget |= GraphicsBuffer.Target.Raw;
        }

        public RaySurfaceGeometry()
        {
            var source = Resources.Load<ComputeShader>("StudioEditorAtmosphere/CopyRayGeometry");
            if (source == null) throw new InvalidOperationException("CopyRayGeometry resource is missing.");
            copy = UnityEngine.Object.Instantiate(source);
            copy.hideFlags = HideFlags.HideAndDontSave;
        }

        public void Prepare(GeometryShadowWorld world)
        {
            if (revision != world.Revision) Rebuild(world);
            var maps = LightmapSettings.lightmaps;
            PrepareLightmaps(maps);
            var data = new Instance[Math.Max(1, slots.Count)];
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                data[i] = new Instance
                {
                    LocalToWorld = slot.renderer != null ? slot.renderer.localToWorldMatrix : Matrix4x4.identity,
                    Geometry = new Vector4(slot.offset, slot.count, slot.material,
                        slot.renderer != null && slot.renderer.lightmapIndex >= 0 && slot.renderer.lightmapIndex < maps.Length &&
                        maps[slot.renderer.lightmapIndex]?.lightmapColor != null &&
                        lightmapSlices.TryGetValue(maps[slot.renderer.lightmapIndex].lightmapColor, out int slice) ? slice : -1),
                    LightmapST = slot.renderer != null ? slot.renderer.lightmapScaleOffset : Vector4.zero,
                };
            }
            instances.SetData(data);
        }

        private void Rebuild(GeometryShadowWorld world)
        {
            slots.Clear(); materials.Clear();
            var materialIds = new Dictionary<Material, int>();
            int count = 0;
            foreach (var item in world.GeometryItems)
            {
                var mesh = item.Mesh;
                var source = item.Renderer.sharedMaterials;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    var material = sub < source.Length ? source[sub] : null;
                    if (mesh.GetTopology(sub) != MeshTopology.Triangles || !world.AcceptsSlot(item, material)) continue;
                    if (!materialIds.TryGetValue(material, out int id))
                    {
                        id = materials.Count; materialIds.Add(material, id); materials.Add(material);
                    }
                    int triangleCount = (int)mesh.GetIndexCount(sub) / 3;
                    slots.Add((item.Renderer, count, triangleCount, id));
                    count += triangleCount;
                }
            }
            Resize(ref triangles, Math.Max(1, count), 112);
            Resize(ref instances, Math.Max(1, slots.Count), 96);
            Resize(ref surfaces, Math.Max(1, materials.Count), 64);
            var unused = new List<Material>();
            foreach (var key in textureCopies.Keys)
                if (!materialIds.ContainsKey(key)) unused.Add(key);
            foreach (var key in unused) textureCopies.Remove(key);
            if (materials.Count > SystemInfo.maxTextureArraySlices)
                throw new InvalidOperationException("Too many opaque materials for the local lighting texture array.");
            if (albedo == null || albedo.volumeDepth != Math.Max(1, materials.Count))
            {
                textureCopies.Clear();
                if (albedo != null) { albedo.Release(); CoreUtils.Destroy(albedo); }
                if (emissionTextures != null) { emissionTextures.Release(); CoreUtils.Destroy(emissionTextures); }
                RenderTexture Array(string name) => new RenderTexture(new RenderTextureDescriptor(64, 64)
                {
                    graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat,
                    dimension = TextureDimension.Tex2DArray, volumeDepth = Math.Max(1, materials.Count),
                    depthBufferBits = 0, msaaSamples = 1,
                }) { name = name, hideFlags = HideFlags.HideAndDontSave };
                albedo = Array("Ray hit albedo array");
                emissionTextures = Array("Ray hit emission array");
                if (!albedo.Create() || !emissionTextures.Create())
                    throw new InvalidOperationException("Cannot create ray hit material arrays.");
            }
            var surfaceData = new Surface[Math.Max(1, materials.Count)];
            var temporary = RenderTexture.GetTemporary(64, 64, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            bool srgbWrite = GL.sRGBWrite;
            var activeTarget = RenderTexture.active;
            try
            {
                GL.sRGBWrite = false;
                for (int i = 0; i < materials.Count; i++)
                {
                    var material = materials[i];
                    string map = material.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
                    var texture = material.HasProperty(map) ? material.GetTexture(map) : null;
                    var scale = material.HasProperty(map) ? material.GetTextureScale(map) : Vector2.one;
                    var offset = material.HasProperty(map) ? material.GetTextureOffset(map) : Vector2.zero;
                    var color = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") :
                        material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
                    var emission = material.IsKeywordEnabled("_EMISSION") && material.HasProperty("_EmissionColor")
                        ? material.GetColor("_EmissionColor") : Color.black;
                    var emissionMap = material.HasProperty("_EmissionMap") ? material.GetTexture("_EmissionMap") : null;
                    // HDR Color properties already hold linear radiance; ordinary
                    // Color properties are converted by Unity before shading.
                    int emissionProperty = material.shader.FindPropertyIndex("_EmissionColor");
                    if (emissionProperty >= 0 &&
                        (material.shader.GetPropertyFlags(emissionProperty) & ShaderPropertyFlags.HDR) == 0)
                        emission = emission.linear;
                    float metallic = material.HasProperty("_Metallic") ? Mathf.Clamp01(material.GetFloat("_Metallic")) : 0;
                    surfaceData[i] = new Surface
                    {
                        Albedo = color.linear, Emission = emission,
                        TextureST = new Vector4(scale.x, scale.y, offset.x, offset.y),
                        Data = new Vector4(i, 1 - metallic,
                            GeometryShadowWorld.IsOpaqueCaster(material) ? 0 : material.HasProperty("_Cutoff") ? material.GetFloat("_Cutoff") : 0.5f,
                            color.a),
                    };
                    int hash = HashCode.Combine(texture != null ? texture.GetEntityId().GetHashCode() : 0,
                        texture != null ? texture.updateCount : 0,
                        emissionMap != null ? emissionMap.GetEntityId().GetHashCode() : 0,
                        emissionMap != null ? emissionMap.updateCount : 0);
                    if (!textureCopies.TryGetValue(material, out var previous) || previous.slice != i || previous.hash != hash)
                    {
                        Graphics.Blit(texture != null ? texture : Texture2D.whiteTexture, temporary);
                        Graphics.CopyTexture(temporary, 0, 0, albedo, i, 0);
                        // URP Lit samples emission with the base-map UV transform.
                        Graphics.Blit(emissionMap != null ? emissionMap : Texture2D.whiteTexture, temporary);
                        Graphics.CopyTexture(temporary, 0, 0, emissionTextures, i, 0);
                        textureCopies[material] = (i, hash);
                    }
                }
            }
            finally
            {
                GL.sRGBWrite = srgbWrite; RenderTexture.active = activeTarget;
                RenderTexture.ReleaseTemporary(temporary);
            }
            surfaces.SetData(surfaceData);

            int slotIndex = 0;
            var cmd = CommandBufferPool.Get("Copy ray hit triangles");
            var wrappers = new List<GraphicsBuffer>();
            try
            {
                foreach (var item in world.GeometryItems)
                {
                    var mesh = item.Mesh;
                    if (mesh.GetVertexAttributeFormat(VertexAttribute.Position) != VertexAttributeFormat.Float32)
                        throw new NotSupportedException("Local lighting requires float32 mesh positions.");
                    int positionStream = mesh.GetVertexAttributeStream(VertexAttribute.Position);
                    var positions = mesh.GetVertexBuffer(positionStream); wrappers.Add(positions);
                    bool hasUv = mesh.HasVertexAttribute(VertexAttribute.TexCoord0);
                    int uvStream = hasUv ? mesh.GetVertexAttributeStream(VertexAttribute.TexCoord0) : positionStream;
                    var uvs = uvStream == positionStream ? positions : mesh.GetVertexBuffer(uvStream);
                    if (uvs != positions) wrappers.Add(uvs);
                    bool hasLightmapUv = mesh.HasVertexAttribute(VertexAttribute.TexCoord1);
                    int lmStream = hasLightmapUv ? mesh.GetVertexAttributeStream(VertexAttribute.TexCoord1) : positionStream;
                    var lmUvs = lmStream == positionStream ? positions : lmStream == uvStream ? uvs : mesh.GetVertexBuffer(lmStream);
                    if (lmUvs != positions && lmUvs != uvs) wrappers.Add(lmUvs);
                    var indices = mesh.GetIndexBuffer(); wrappers.Add(indices);
                    cmd.SetComputeBufferParam(copy, 0, "_Positions", positions);
                    cmd.SetComputeBufferParam(copy, 0, "_Uvs", uvs);
                    cmd.SetComputeBufferParam(copy, 0, "_LightmapUvs", lmUvs);
                    cmd.SetComputeVectorParam(copy, "_LightmapUvLayout", new Vector4(mesh.GetVertexBufferStride(lmStream),
                        hasLightmapUv ? mesh.GetVertexAttributeOffset(VertexAttribute.TexCoord1) : -1,
                        hasLightmapUv && mesh.GetVertexAttributeFormat(VertexAttribute.TexCoord1) == VertexAttributeFormat.Float16 ? 1 : 0, 0));
                    cmd.SetComputeBufferParam(copy, 0, "_Indices", indices);
                    cmd.SetComputeBufferParam(copy, 0, "_Triangles", triangles);
                    cmd.SetComputeVectorParam(copy, "_PositionLayout", new Vector4(mesh.GetVertexBufferStride(positionStream),
                        mesh.GetVertexAttributeOffset(VertexAttribute.Position), 0, 0));
                    cmd.SetComputeVectorParam(copy, "_UvLayout", new Vector4(mesh.GetVertexBufferStride(uvStream),
                        hasUv ? mesh.GetVertexAttributeOffset(VertexAttribute.TexCoord0) : -1,
                        hasUv && mesh.GetVertexAttributeFormat(VertexAttribute.TexCoord0) == VertexAttributeFormat.Float16 ? 1 : 0, 0));
                    cmd.SetComputeIntParam(copy, "_Index16", mesh.indexFormat == IndexFormat.UInt16 ? 1 : 0);
                    var source = item.Renderer.sharedMaterials;
                    for (int sub = 0; sub < mesh.subMeshCount; sub++)
                    {
                        if (mesh.GetTopology(sub) != MeshTopology.Triangles || sub >= source.Length || !world.AcceptsSlot(item, source[sub])) continue;
                        var slot = slots[slotIndex++];
                        cmd.SetComputeVectorParam(copy, "_TriangleRange", new Vector4(slot.offset, slot.count,
                            mesh.GetIndexStart(sub), mesh.GetBaseVertex(sub)));
                        if (slot.count > 0) cmd.DispatchCompute(copy, 0, Mathf.CeilToInt(slot.count / 64f), 1, 1);
                    }
                }
                Graphics.ExecuteCommandBuffer(cmd);
            }
            finally
            {
                CommandBufferPool.Release(cmd);
                foreach (var wrapper in wrappers) wrapper.Dispose();
            }
            revision = world.Revision;
        }

        public void Bind(CommandBuffer cmd, IRayTracingShader shader)
        {
            shader.SetBufferParam(cmd, Shader.PropertyToID("_LocalTriangles"), triangles);
            shader.SetBufferParam(cmd, Shader.PropertyToID("_LocalInstances"), instances);
            shader.SetBufferParam(cmd, Shader.PropertyToID("_LocalMaterials"), surfaces);
            shader.SetTextureParam(cmd, Shader.PropertyToID("_LocalAlbedo"), albedo);
            shader.SetTextureParam(cmd, Shader.PropertyToID("_LocalEmission"), emissionTextures);
            shader.SetTextureParam(cmd, Shader.PropertyToID("_LocalBakedLightmaps"), bakedLightmaps);
            shader.SetTextureParam(cmd, Shader.PropertyToID("_LocalBakedDirections"), bakedDirections);
        }

        private void PrepareLightmaps(LightmapData[] maps)
        {
            var textures = new List<Texture2D>();
            foreach (var map in maps)
                if (map?.lightmapColor != null && !textures.Contains(map.lightmapColor)) textures.Add(map.lightmapColor);
            bool changed = textures.Count != lightmapSlices.Count;
            for (int i = 0; i < textures.Count; i++)
                changed |= !lightmapSlices.TryGetValue(textures[i], out int previous) || previous != i;
            if (!changed && bakedLightmaps != null) return;
            lightmapSlices.Clear();
            if (bakedLightmaps != null) { bakedLightmaps.Release(); CoreUtils.Destroy(bakedLightmaps); }
            if (bakedDirections != null) { bakedDirections.Release(); CoreUtils.Destroy(bakedDirections); }
            RenderTexture Array(string name)
            {
                var texture = new RenderTexture(new RenderTextureDescriptor(256,256)
                {
                    graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat,
                    dimension = TextureDimension.Tex2DArray, volumeDepth = Math.Max(1,textures.Count),
                    depthBufferBits = 0, msaaSamples = 1
                }) { name = name, hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
                texture.Create(); return texture;
            }
            bakedLightmaps = Array("Source map baked irradiance");
            bakedDirections = Array("Source map baked directions");
            var temporary = RenderTexture.GetTemporary(256,256,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);
            bool srgb = GL.sRGBWrite;
            var active = RenderTexture.active;
            try
            {
                GL.sRGBWrite = false;
                for (int i = 0; i < textures.Count; i++)
                {
                    lightmapSlices.Add(textures[i],i);
                    var map = System.Array.Find(maps,m=>m?.lightmapColor == textures[i]);
                    Graphics.Blit(textures[i],temporary);Graphics.CopyTexture(temporary,0,0,bakedLightmaps,i,0);
                    Graphics.Blit(map.lightmapDir != null ? map.lightmapDir : Texture2D.grayTexture,temporary);
                    Graphics.CopyTexture(temporary,0,0,bakedDirections,i,0);
                }
            }
            finally { GL.sRGBWrite = srgb;RenderTexture.active = active;RenderTexture.ReleaseTemporary(temporary); }
        }

        private static void Resize(ref GraphicsBuffer buffer, int count, int stride)
        {
            if (buffer != null && buffer.count == count) return;
            buffer?.Dispose();
            buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, stride);
        }

        public void Dispose()
        {
            triangles?.Dispose(); instances?.Dispose(); surfaces?.Dispose();
            if (albedo != null) { albedo.Release(); CoreUtils.Destroy(albedo); }
            if (emissionTextures != null) { emissionTextures.Release(); CoreUtils.Destroy(emissionTextures); }
            if (bakedLightmaps != null) { bakedLightmaps.Release(); CoreUtils.Destroy(bakedLightmaps); }
            if (bakedDirections != null) { bakedDirections.Release(); CoreUtils.Destroy(bakedDirections); }
            CoreUtils.Destroy(copy);
        }
    }
}
