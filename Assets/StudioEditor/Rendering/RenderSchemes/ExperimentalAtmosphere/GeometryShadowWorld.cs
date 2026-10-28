using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.UnifiedRayTracing;

namespace StudioEditor.Rendering.Experimental
{
    /// <summary>Shared geometry for fog and local lighting, including sampled skinning.</summary>
    internal sealed class GeometryShadowWorld : IDisposable
    {
        // Instance masks. Fog sun rays trace FogRayMask, local lighting traces LocalRayMask.
        internal const uint FogMask = 1u, LocalCutoutMask = 2u, LocalOnlyMask = 4u, CharacterFogMask = 8u;
        internal const uint FogRayMask = FogMask | CharacterFogMask;
        internal const uint LocalRayMask = FogMask | LocalCutoutMask | LocalOnlyMask;

        internal sealed class Geometry
        {
            public Renderer Renderer;
            public Mesh Mesh;
            public bool OwnsMesh;
            public bool Character;
            public readonly List<int> Handles = new List<int>();
            public readonly List<uint> Masks = new List<uint>();
        }

        private readonly List<Geometry> geometry = new List<Geometry>();
        private RayTracingContext context;
        private IRayTracingAccelStruct acceleration;
        private IRayTracingShader shader;
        private GraphicsBuffer scratch;
        private RenderTexture visibility;
        private RTHandle visibilityHandle;
        private Vector3Int grid;
        private float nextGeometryUpdate;
        private int previousLayerMask;
        private bool failed;
        private bool includeLocalGeometry, includeAlphaClip;

        internal IReadOnlyList<Geometry> GeometryItems => geometry;
        internal RayTracingContext Context => context;
        internal IRayTracingAccelStruct Acceleration => acceleration;
        internal int Revision { get; private set; }

        public int InstanceCount { get; private set; }
        public int OmittedCutoutSlots { get; private set; }
        public RTHandle Visibility => visibilityHandle;
        public void InvalidateGeometry() => nextGeometryUpdate = 0f;

        public bool PrepareLighting(Camera camera, out string error)
        {
            if (!RayTracingContext.IsBackendSupported(RayTracingBackend.Hardware))
            {
                error = "DXR unavailable; local indirect lighting and contact are disabled.";
                return false;
            }
            return Prepare(camera, visibility == null ? Vector3Int.one : grid,
                ExperimentalRenderConfiguration.Settings ?? new AtmosphereSettings(), out error);
        }

        public bool Prepare(Camera camera, Vector3Int size, AtmosphereSettings settings, out string error)
        {
            error = string.Empty;
            if (failed) return false;
            try
            {
                if (context == null)
                {
                    if (!RayTracingContext.IsBackendSupported(RayTracingBackend.Hardware))
                        throw new NotSupportedException("Hardware ray tracing is unavailable; using shadow maps.");
                    var resources = new RayTracingResources();
                    if (!resources.LoadFromRenderPipelineResources())
                        throw new InvalidOperationException("UnifiedRayTracing resources are missing.");
                    context = new RayTracingContext(RayTracingBackend.Hardware, resources);
                    RayTracingShader asset = null;
                    foreach (var candidate in Resources.LoadAll<UnityEngine.Object>("StudioEditorAtmosphere/GeometryShadows"))
                        if (candidate is RayTracingShader rayShader) { asset = rayShader; break; }
                    if (asset == null) throw new InvalidOperationException("GeometryShadows ray tracing shader is missing.");
                    shader = context.CreateRayTracingShader(asset);
                    acceleration = context.CreateAccelerationStructure(new AccelerationStructureOptions());
                }
                if (visibility == null || grid != size)
                {
                    visibilityHandle?.Release();
                    if (visibility != null) { visibility.Release(); CoreUtils.Destroy(visibility); }
                    grid = size;
                    visibility = new RenderTexture(size.x, size.y, 0)
                    {
                        name = "Atmosphere geometry visibility",
                        dimension = TextureDimension.Tex3D,
                        volumeDepth = size.z,
                        graphicsFormat = GraphicsFormat.R32_SFloat,
                        enableRandomWrite = true,
                        filterMode = FilterMode.Point,
                        wrapMode = TextureWrapMode.Clamp,
                        hideFlags = HideFlags.HideAndDontSave,
                    };
                    if (!visibility.Create()) throw new InvalidOperationException("Cannot allocate the volume visibility texture.");
                    visibilityHandle = RTHandles.Alloc(visibility);
                }
                bool local = ExperimentalRenderConfiguration.LocalLighting?.Enabled == true &&
                    ExperimentalRenderConfiguration.Surfaces?.Enabled == true;
                bool cutout = local && ExperimentalRenderConfiguration.LocalLighting.IncludeAlphaClip;
                if (Time.unscaledTime >= nextGeometryUpdate || previousLayerMask != camera.cullingMask ||
                    includeLocalGeometry != local || includeAlphaClip != cutout)
                {
                    includeLocalGeometry = local; includeAlphaClip = cutout;
                    RebuildGeometry(camera.cullingMask);
                    previousLayerMask = camera.cullingMask;
                    nextGeometryUpdate = Time.unscaledTime +
                        1f / Mathf.Clamp(settings.GeometryUpdatesPerSecond, 0.5f, 30f);
                }
                foreach (var item in geometry)
                    if (item.Renderer != null)
                        for (int index = 0; index < item.Handles.Count; index++)
                        {
                            int handle = item.Handles[index];
                            acceleration.UpdateInstanceTransform(handle, item.Renderer.localToWorldMatrix);
                            acceleration.UpdateInstanceMask(handle, item.Renderer.enabled &&
                                !item.Renderer.forceRenderingOff && item.Renderer.gameObject.activeInHierarchy ? item.Masks[index] : 0u);
                        }
                    else
                        foreach (int handle in item.Handles) acceleration.UpdateInstanceMask(handle, 0);
                ulong bytes = Math.Max(acceleration.GetBuildScratchBufferRequiredSizeInBytes(),
                    shader.GetTraceScratchBufferRequiredSizeInBytes((uint)size.x, (uint)size.y, (uint)size.z));
                if (bytes > 0 && (scratch == null || (ulong)scratch.count * (ulong)scratch.stride < bytes))
                {
                    scratch?.Dispose();
                    scratch = new GraphicsBuffer(RayTracingHelper.ScratchBufferTarget, (int)((bytes + 3) / 4), 4);
                }
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                Dispose();
                failed = true;
                return false;
            }
        }

        internal static bool IsOpaqueCaster(Material material)
        {
            if (material == null || material.renderQueue >= (int)RenderQueue.Transparent) return false;
            return !material.IsKeywordEnabled("_ALPHATEST_ON") &&
                (!material.HasProperty("_AlphaClip") || material.GetFloat("_AlphaClip") < 0.5f) &&
                (!material.HasProperty("_AlphaClipEnable") || material.GetFloat("_AlphaClipEnable") < 0.5f);
        }

        internal bool AcceptsSurface(Material material)
        {
            if (IsOpaqueCaster(material)) return true;
            return includeAlphaClip && material != null && material.renderQueue < (int)RenderQueue.Transparent &&
                (!material.HasProperty("_Surface") || material.GetFloat("_Surface") < 0.5f) &&
                (material.IsKeywordEnabled("_ALPHATEST_ON") ||
                    material.HasProperty("_AlphaClip") && material.GetFloat("_AlphaClip") > 0.5f);
        }

        // Instance IDs index RaySurfaceGeometry slots; both must accept the same submeshes.
        internal bool AcceptsSlot(Geometry item, Material material) =>
            AcceptsSurface(material) && (!item.Character || IsOpaqueCaster(material));

        private static bool IsLocalSurface(Renderer renderer)
        {
            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null || material.shader == null) continue;
                string name = material.shader.name;
                if (name == "Universal Render Pipeline/Lit" || name == "StudioEditor/ExperimentalLocalLit" ||
                    name == "StudioEditor/KoikatsuStudioItem") return true;
            }
            return false;
        }

        internal static bool IsCharacter(Renderer renderer)
        {
            if (renderer.GetComponentInParent<CharacterShadowBinding>() != null) return true;
            foreach (var material in renderer.sharedMaterials)
                if (material != null && material.shader != null && material.shader.name == "StudioEditor/AnimeCharacter") return true;
            return false;
        }

        private void RebuildGeometry(int layerMask)
        {
            Revision++;
            acceleration.ClearInstances();
            var existing = new Dictionary<EntityId, Geometry>();
            foreach (var item in geometry)
                if (item.Renderer != null) existing[item.Renderer.GetEntityId()] = item;
                else if (item.OwnsMesh) CoreUtils.Destroy(item.Mesh);
            geometry.Clear();
            InstanceCount = 0;
            OmittedCutoutSlots = 0;
            try
            {
                foreach (var renderer in UnityEngine.Object.FindObjectsByType<Renderer>())
                {
                    if (!renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                        renderer.forceRenderingOff ||
                        (renderer.shadowCastingMode == ShadowCastingMode.Off && !(includeLocalGeometry && IsLocalSurface(renderer))) ||
                        (layerMask & (1 << renderer.gameObject.layer)) == 0 ||
                        !(renderer is MeshRenderer || renderer is SkinnedMeshRenderer)) continue;
                    Mesh source = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh :
                        renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    if (source == null || source.vertexCount == 0) continue;
                    bool character = IsCharacter(renderer);
                    if (character && renderer.shadowCastingMode == ShadowCastingMode.Off) continue;
                    var materials = renderer.sharedMaterials;
                    bool hasOpaqueSlot = false;
                    for (int sub = 0; sub < source.subMeshCount; sub++)
                    {
                        var material = sub < materials.Length ? materials[sub] : null;
                        if (source.GetTopology(sub) == MeshTopology.Triangles && AcceptsSurface(material) &&
                            (!character || IsOpaqueCaster(material))) hasOpaqueSlot = true;
                        else OmittedCutoutSlots++;
                    }
                    if (!hasOpaqueSlot) continue;
                    if (!existing.TryGetValue(renderer.GetEntityId(), out var item))
                    {
                        item = new Geometry { Renderer = renderer, Mesh = source };
                        if (renderer is SkinnedMeshRenderer)
                        {
                            item.OwnsMesh = true;
                            item.Mesh = new Mesh { name = "Atmosphere posed geometry", hideFlags = HideFlags.HideAndDontSave };
                        }
                    }
                    existing.Remove(renderer.GetEntityId());
                    item.Character = character;
                    geometry.Add(item);
                    item.Handles.Clear();
                    item.Masks.Clear();
                    if (renderer is SkinnedMeshRenderer skin) skin.BakeMesh(item.Mesh, false);
                    else item.Mesh = source;
                    if (includeLocalGeometry) RaySurfaceGeometry.PrepareMeshBuffers(item.Mesh);
                    for (int sub = 0; sub < item.Mesh.subMeshCount; sub++)
                    {
                        var material = sub < materials.Length ? materials[sub] : null;
                        if (item.Mesh.GetTopology(sub) != MeshTopology.Triangles || !AcceptsSlot(item, material)) continue;
                        bool opaque = IsOpaqueCaster(material);
                        bool casts = renderer.shadowCastingMode != ShadowCastingMode.Off;
                        // Characters keep their toon self shading and shadow maps. A shared
                        // world cache cannot tell a character from its own geometry, so they
                        // only occlude the fog's sun rays and never enter local lighting.
                        uint mask = character ? CharacterFogMask : !opaque ? LocalCutoutMask : !casts ? LocalOnlyMask : FogMask;
                        var desc = new MeshInstanceDesc(item.Mesh, sub)
                        {
                            instanceID = (uint)InstanceCount,
                            localToWorldMatrix = renderer.localToWorldMatrix,
                            enableTriangleCulling = false,
                            opaqueGeometry = opaque,
                            mask = mask,
                        };
                        item.Handles.Add(acceleration.AddInstance(desc));
                        item.Masks.Add(mask);
                        InstanceCount++;
                    }
                }
            }
            finally
            {
                foreach (var item in existing.Values) if (item.OwnsMesh) CoreUtils.Destroy(item.Mesh);
            }
        }

        public void Dispatch(CommandBuffer cmd, AtmosphereFrame frame)
        {
            acceleration.Build(cmd, scratch);
            shader.SetAccelerationStructure(cmd, "_FogScene", acceleration);
            shader.SetTextureParam(cmd, Shader.PropertyToID("_FogVisibilityOutput"), visibility);
            shader.SetVectorParam(cmd, Shader.PropertyToID("_FogGrid"), frame.Grid);
            shader.SetVectorParam(cmd, Shader.PropertyToID("_FogRange"), frame.Range);
            shader.SetVectorParam(cmd, Shader.PropertyToID("_FogSunDirection"), frame.SunDirection);
            shader.SetMatrixParam(cmd, Shader.PropertyToID("_FogInverseProjection"), frame.InverseProjection);
            shader.SetMatrixParam(cmd, Shader.PropertyToID("_FogCameraToWorld"), frame.CameraToWorld);
            shader.SetFloatParam(cmd, Shader.PropertyToID("_FogOrthographic"), frame.Orthographic);
            shader.SetFloatParam(cmd, Shader.PropertyToID("_FogFarClipValue"), frame.FarClipValue);
            shader.SetFloatParam(cmd, Shader.PropertyToID("_FogRayDistance"), frame.RayDistance);
            shader.Dispatch(cmd, scratch, (uint)grid.x, (uint)grid.y, (uint)grid.z);
        }

        public void Dispose()
        {
            acceleration?.Dispose(); acceleration = null;
            context?.Dispose(); context = null;
            scratch?.Dispose(); scratch = null;
            foreach (var item in geometry) if (item.OwnsMesh) CoreUtils.Destroy(item.Mesh);
            geometry.Clear();
            visibilityHandle?.Release(); visibilityHandle = null;
            if (visibility != null) { visibility.Release(); CoreUtils.Destroy(visibility); visibility = null; }
            InstanceCount = 0;
        }
    }
}
