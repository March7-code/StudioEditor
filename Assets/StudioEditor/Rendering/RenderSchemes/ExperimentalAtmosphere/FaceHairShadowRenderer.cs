using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace StudioEditor.Rendering.Experimental
{
    // A small atlas supplies hair / accessory occlusion, including directional
    // lights URP cannot shadow, without adding geometric face self-shadow.
    internal sealed class FaceHairShadowRenderer : IDisposable
    {
        internal const int Resolution = 512, MaxLights = 4;
        private static readonly int CountId = Shader.PropertyToID("_FaceHairShadowCount");
        private static readonly int MapId = Shader.PropertyToID("_FaceHairDepth");
        private static readonly int MatricesId = Shader.PropertyToID("_FaceHairWorldToShadow");
        private static readonly int LightsId = Shader.PropertyToID("_FaceHairLightPositions");
        private static readonly int ClipId = Shader.PropertyToID("_HairShadowClipMatrix");
        private static readonly int DepthId = Shader.PropertyToID("_HairShadowTextureMatrix");
        private readonly ExperimentalRenderSession session;
        private readonly Dictionary<FaceShadowBinding, Face> faces = new();
        private readonly Dictionary<Material, Material> depthMaterials = new();
        private readonly Dictionary<Renderer, FrontHairShadowGeometry> frontGeometry = new();
        private readonly MaterialPropertyBlock block = new();
        private readonly CommandBuffer commands = new() { name = "Face hair shadows" };
        private readonly Shader depthShader;
        private readonly FaceShadowCasterScope faceCasters = new();
        private readonly FaceEnvironmentShadowRenderer environmentShadows;
        private float nextScan;

        private sealed class Face
        {
            public FaceShadowBinding Binding;
            public Renderer[] Hair;
            public RenderTexture Depth;
            public readonly Matrix4x4[] Matrices = new Matrix4x4[MaxLights];
            public readonly Vector4[] Lights = new Vector4[MaxLights];
            public Vector4 Weights;
        }

        public FaceHairShadowRenderer(ExperimentalRenderSession session)
        {
            this.session = session;
            depthShader = Resources.Load<Shader>("StudioEditorAtmosphere/FaceHairDepth");
            environmentShadows = new FaceEnvironmentShadowRenderer(depthShader);
            RenderPipelineManager.beginCameraRendering += BeginCamera;
            RenderPipelineManager.endCameraRendering += EndCamera;
        }

        private void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera != session.RenderCamera) return;
            faceCasters.Restore();
            if (!session.IsActive || ExperimentalRenderConfiguration.Surfaces?.Enabled != true || depthShader == null)
            {
                foreach (var face in faces.Values) SetCount(face, 0);
                return;
            }
            if (Time.unscaledTime >= nextScan)
            {
                ScanFaces();
                nextScan = Time.unscaledTime + .5f;
            }
            var planes = GeometryUtility.CalculateFrustumPlanes(camera);
            var lights = UnityEngine.Object.FindObjectsByType<Light>();
            commands.Clear();
            foreach (var face in faces.Values)
            {
                var binding = face.Binding;
                var renderer = binding != null ? binding.Target : null;
                if (renderer == null || !binding.isActiveAndEnabled ||
                    !renderer.enabled || (camera.cullingMask & (1 << renderer.gameObject.layer)) == 0)
                { SetCount(face, 0); continue; }
                // The curve supplies facial self shading. Letting the nose
                // also cast through URP's geometric map reintroduces its
                // uneven silhouette, especially for a standalone main light.
                faceCasters.Exclude(binding);
                var bounds = WorldBounds(binding);
                if (!GeometryUtility.TestPlanesAABB(planes, bounds)) { SetCount(face, 0); continue; }
                var selected = lights.Where(light => (light.cullingMask & (1 << renderer.gameObject.layer)) != 0 &&
                        AcceptsLight(light, renderer.renderingLayerMask, bounds))
                    .OrderByDescending(light => light.GetComponent<ImportedSceneKeyLight>() != null)
                    .ThenByDescending(light => LightScore(light, bounds.center)).Take(MaxLights).ToArray();
                if (selected.Length == 0) { SetCount(face, 0); continue; }
                if (face.Depth == null)
                {
                    face.Depth = new RenderTexture(new RenderTextureDescriptor(Resolution, Resolution)
                    {
                        graphicsFormat = GraphicsFormat.R16_UNorm, depthBufferBits = 0,
                        dimension = TextureDimension.Tex2DArray, volumeDepth = MaxLights, msaaSamples = 1,
                    })
                    {
                        name = "Face-local hair depth", filterMode = FilterMode.Point,
                        wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave,
                    };
                    if (!face.Depth.Create()) { CoreUtils.Destroy(face.Depth); face.Depth = null; SetCount(face, 0); continue; }
                }
                environmentShadows.Render(commands, binding, bounds, selected);
                for (int index = 0; index < selected.Length; index++)
                {
                    var light = selected[index];
                    CreateProjection(bounds, light, binding.HeadFrame.up, out var clip, out var texture,
                        binding.HeadFrame.forward, ExperimentalRenderConfiguration.Surfaces.HairShadowMaxAngle);
                    face.Matrices[index] = texture;
                    Vector3 position = light.type == LightType.Directional ? -light.transform.forward : light.transform.position;
                    face.Lights[index] = new Vector4(position.x, position.y, position.z, light.type == LightType.Directional ? 0 : 1);
                    face.Weights[index] = light.shadowStrength;
                    commands.SetRenderTarget(face.Depth, 0, CubemapFace.Unknown, index);
                    commands.ClearRenderTarget(false, true, Color.white);
                    commands.SetGlobalMatrix(ClipId, clip);
                    commands.SetGlobalMatrix(DepthId, texture);
                    foreach (var hair in face.Hair)
                    {
                        if (hair == null || !hair.enabled || !hair.gameObject.activeInHierarchy ||
                            hair.forceRenderingOff || hair.shadowCastingMode == ShadowCastingMode.Off ||
                            !CastsForLight(hair, light)) continue;
                        if (!frontGeometry.TryGetValue(hair, out var geometry) || geometry.AcceptedTriangles == 0) continue;
                        var materials = hair.sharedMaterials;
                        int submeshes = hair is SkinnedMeshRenderer skin ? skin.sharedMesh?.subMeshCount ?? 0 :
                            hair.GetComponent<MeshFilter>()?.sharedMesh?.subMeshCount ?? 0;
                        for (int slot = 0; slot < materials.Length && slot < submeshes; slot++)
                            if (IsHeadOccluder(materials[slot])) commands.DrawRenderer(hair, DepthMaterial(materials[slot]), slot, 0);
                    }
                }
                renderer.GetPropertyBlock(block);
                block.SetFloat(CountId, selected.Length);
                block.SetTexture(MapId, face.Depth);
                block.SetMatrixArray(MatricesId, face.Matrices);
                block.SetVectorArray(LightsId, face.Lights);
                block.SetVector("_FaceHairShadowWeights", face.Weights);
                renderer.SetPropertyBlock(block);
            }
            context.ExecuteCommandBuffer(commands);
            commands.Clear();
        }

        private void EndCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera == session.RenderCamera) faceCasters.Restore();
        }

        private void ScanFaces()
        {
            var current = UnityEngine.Object.FindObjectsByType<FaceShadowBinding>();
            foreach (var binding in faces.Keys.Where(binding => binding == null || !current.Contains(binding)).ToArray())
            { Release(faces[binding]); faces.Remove(binding); }
            foreach (var binding in current)
            {
                if (binding.HeadRoot == null || binding.Target == null) continue;
                if (!faces.TryGetValue(binding, out var face)) faces.Add(binding, face = new Face { Binding = binding });
                // Modded hairstyles can be accessories parented to the body
                // head bone, outside the head prefab, with a different shader.
                // The import contract's roles survive that arrangement.
                face.Hair = FindHeadOccluders(binding);
                foreach (var hair in face.Hair)
                {
                    if (frontGeometry.TryGetValue(hair, out var geometry))
                    {
                        if (geometry.IsCurrent) continue;
                        geometry.Dispose();
                    }
                    frontGeometry[hair] = new FrontHairShadowGeometry(hair, binding);
                }
            }
            var activeHair = new HashSet<Renderer>(faces.Values.SelectMany(face => face.Hair));
            foreach (var hair in frontGeometry.Keys.Where(hair => hair == null || !activeHair.Contains(hair)).ToArray())
            { frontGeometry[hair].Dispose(); frontGeometry.Remove(hair); }
            foreach (var source in depthMaterials.Keys.Where(source => source == null).ToArray())
            { CoreUtils.Destroy(depthMaterials[source]); depthMaterials.Remove(source); }
        }

        internal static bool IsHeadOccluder(Material material)
        {
            if (material == null) return false;
            if (material.HasProperty("_SourceCharacterModel") && material.GetFloat("_SourceCharacterModel") == 2) return true;
            if (!material.HasProperty("_StudioSurfaceRole")) return false;
            var role = (CharacterRenderSurfaceRole)material.GetFloat("_StudioSurfaceRole");
            return role == CharacterRenderSurfaceRole.Hair || role == CharacterRenderSurfaceRole.Accessory;
        }

        internal static Renderer[] FindHeadOccluders(FaceShadowBinding binding)
        {
            var character = binding.GetComponentInParent<CharacterShadowBinding>();
            var root = character != null ? character.transform : binding.HeadRoot;
            return root != null ? root.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => !IsSideOrBackHair(renderer) && renderer.sharedMaterials.Any(IsHeadOccluder) &&
                    IsHeadOwned(renderer, binding)).ToArray() : Array.Empty<Renderer>();
        }

        private static bool IsSideOrBackHair(Renderer renderer)
        {
            // These are the importer's explicit back / side / option slots.
            // Combined accessory hairstyles have no such slot and use the
            // complete-card mask instead.
            for (var frame = renderer.transform; frame != null; frame = frame.parent)
                if (frame.name.StartsWith("ct_hairB", StringComparison.Ordinal) ||
                    frame.name.StartsWith("ct_hairS", StringComparison.Ordinal) ||
                    frame.name.StartsWith("ct_hairO", StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool IsHeadOwned(Renderer renderer, FaceShadowBinding binding)
        {
            // A Hair role is already an explicit importer contract. Accessory
            // alone also includes swords, belts and leg decorations, so those
            // need an actual head attachment or head-only skinning influence.
            if (renderer.sharedMaterials.Any(material => material != null &&
                (material.HasProperty("_SourceCharacterModel") && material.GetFloat("_SourceCharacterModel") == 2 ||
                 material.HasProperty("_StudioSurfaceRole") && material.GetFloat("_StudioSurfaceRole") == (float)CharacterRenderSurfaceRole.Hair))) return true;
            var head = binding.HeadRoot;
            if (head == null) return false;
            if (renderer.transform.IsChildOf(head)) return true;
            if (renderer is SkinnedMeshRenderer skin && skin.bones.Length > 0)
                return skin.bones.All(bone => bone != null && bone.IsChildOf(head));
            return false;
        }

        internal static bool CastsForLight(Renderer renderer, Light light)
        {
            if ((light.cullingMask & (1 << renderer.gameObject.layer)) == 0) return false;
            var data = light.GetUniversalAdditionalLightData();
            uint layers = data.customShadowLayers ? data.shadowRenderingLayers.value : data.renderingLayers.value;
            return (layers & renderer.renderingLayerMask) != 0;
        }

        internal static bool AcceptsLight(Light light, uint layers, Bounds bounds)
        {
            if (light == null || !light.isActiveAndEnabled || light.intensity <= 0 || light.shadows == LightShadows.None ||
                light.shadowStrength <= 0 || (light.GetUniversalAdditionalLightData().renderingLayers.value & layers) == 0) return false;
            if (light.type == LightType.Directional) return true;
            if (light.type != LightType.Point && light.type != LightType.Spot) return false;
            var delta = bounds.center - light.transform.position;
            if (delta.magnitude > light.range + bounds.extents.magnitude || delta.magnitude < bounds.extents.magnitude) return false;
            return light.type != LightType.Spot || Vector3.Angle(light.transform.forward, delta) < light.spotAngle * .5f + 15;
        }

        private static float LightScore(Light light, Vector3 center) => light.intensity * light.color.maxColorComponent /
            (light.type == LightType.Directional ? 1 : Mathf.Max(.05f, (light.transform.position - center).sqrMagnitude));

        // A light's own direction stretches bang silhouettes across the curved face
        // as it moves to the side or overhead. With headForward, the projection
        // direction stays within maxAngle of the face front, keeping the frontal
        // shape and still moving toward the light. The receiver still matches the
        // real light by its encoded position, and its toon band uses the real light.
        internal static void CreateProjection(Bounds bounds, Light light, Vector3 headUp,
            out Matrix4x4 clip, out Matrix4x4 texture, Vector3? headForward = null, float maxAngle = 180)
        {
            float radius = Mathf.Max(.06f, bounds.extents.magnitude + .015f);
            bool directional = light.type == LightType.Directional;
            Vector3 direction = directional ? -light.transform.forward : (light.transform.position - bounds.center).normalized;
            float lightDistance = directional ? 0 : Vector3.Distance(light.transform.position, bounds.center);
            if (headForward.HasValue && maxAngle < 180)
            {
                var forward = headForward.Value.normalized;
                float angle = Vector3.Angle(forward, direction);
                if (angle > maxAngle)
                    direction = Vector3.RotateTowards(forward, direction, maxAngle * Mathf.Deg2Rad, 0).normalized;
            }
            Vector3 eye = directional ? bounds.center + direction * (radius + .5f) : bounds.center + direction * lightDistance;
            Vector3 up = Mathf.Abs(Vector3.Dot(direction, headUp)) < .95f ? headUp :
                Mathf.Abs(direction.y) < .95f ? Vector3.up : Vector3.right;
            var view = Matrix4x4.Scale(new Vector3(1, 1, -1)) *
                Matrix4x4.TRS(eye, Quaternion.LookRotation(-direction, up), Vector3.one).inverse;
            float distance = Vector3.Distance(eye, bounds.center);
            float near = directional ? .001f : Mathf.Max(.001f, distance - radius - .5f);
            float far = distance + radius + .02f;
            var projection = directional ? Matrix4x4.Ortho(-radius, radius, -radius, radius, near, far) :
                Matrix4x4.Perspective(Mathf.Asin(Mathf.Min(.99f, radius / distance)) * 2 * Mathf.Rad2Deg, 1, near, far);
            clip = GL.GetGPUProjectionMatrix(projection, true) * view;
            var bias = Matrix4x4.identity;
            bias.m00 = .5f; bias.m03 = .5f;
            bias.m11 = SystemInfo.graphicsUVStartsAtTop ? -.5f : .5f; bias.m13 = .5f;
            bool openGL = SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLCore ||
                SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3;
            bias.m22 = SystemInfo.usesReversedZBuffer ? -1 : openGL ? .5f : 1;
            bias.m23 = SystemInfo.usesReversedZBuffer ? 1 : openGL ? .5f : 0;
            texture = bias * clip;
        }

        private static Bounds WorldBounds(FaceShadowBinding binding)
        {
            var local = binding.HeadLocalBounds;
            var result = new Bounds(binding.HeadFrame.TransformPoint(local.center), Vector3.zero);
            for (int corner = 0; corner < 8; corner++)
                result.Encapsulate(binding.HeadFrame.TransformPoint(local.center + Vector3.Scale(local.extents,
                    new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1))));
            return result;
        }

        private Material DepthMaterial(Material source)
        {
            if (!depthMaterials.TryGetValue(source, out var material))
                depthMaterials.Add(source, material = CoreUtils.CreateEngineMaterial(depthShader));
            material.SetTexture("_BaseMap", source.GetTexture("_BaseMap"));
            var scale = source.GetTextureScale("_BaseMap");
            var offset = source.GetTextureOffset("_BaseMap");
            material.SetVector("_BaseMap_ST", new Vector4(scale.x, scale.y, offset.x, offset.y));
            material.SetColor("_BaseColor", source.GetColor("_BaseColor"));
            bool transparent = source.HasProperty("_Surface") && source.GetFloat("_Surface") > .5f;
            material.SetFloat("_Cutoff", transparent ? Mathf.Max(.01f, source.GetFloat("_Cutoff")) : source.GetFloat("_Cutoff"));
            material.SetFloat("_HairAlphaClip", source.IsKeywordEnabled("_ALPHATEST_ON") || transparent ? 1 : 0);
            material.SetFloat("_HairFrontOnly", 1);
            return material;
        }

        private void SetCount(Face face, int count)
        {
            if (face.Binding == null || face.Binding.Target == null) return;
            environmentShadows.Clear(face.Binding);
            face.Binding.Target.GetPropertyBlock(block); block.SetFloat(CountId, count);
            face.Binding.Target.SetPropertyBlock(block);
        }
        private void Release(Face face) { SetCount(face, 0); environmentShadows.Release(face.Binding); if (face.Depth != null) { face.Depth.Release(); CoreUtils.Destroy(face.Depth); } }
        public void Dispose()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            RenderPipelineManager.endCameraRendering -= EndCamera;
            faceCasters.Restore();
            environmentShadows.Dispose();
            foreach (var face in faces.Values) Release(face);
            foreach (var material in depthMaterials.Values) CoreUtils.Destroy(material);
            foreach (var geometry in frontGeometry.Values) geometry.Dispose();
            frontGeometry.Clear();
            faces.Clear(); depthMaterials.Clear(); commands.Release();
        }
    }

    internal sealed class FaceShadowCasterScope
    {
        private readonly Dictionary<Renderer, ShadowCastingMode> original = new();
        private readonly MaterialPropertyBlock block = new();

        internal void Exclude(FaceShadowBinding binding)
        {
            var renderer = binding != null && binding.isActiveAndEnabled ? binding.Target : null;
            if (renderer == null || original.ContainsKey(renderer)) return;
            renderer.GetPropertyBlock(block);
            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null || !material.HasProperty("_SourceCharacterModel") ||
                    material.GetFloat("_SourceCharacterModel") != 4) continue;
                float Value(string name) => block.HasFloat(name) ? block.GetFloat(name) : material.GetFloat(name);
                if (Value("_SourceCharacterShading") < .5f || Value("_FaceShadowReady") < .5f ||
                    Value("_FaceShadowStrength") <= 0) continue;
                original.Add(renderer, renderer.shadowCastingMode);
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                return;
            }
        }

        internal void Restore()
        {
            foreach (var pair in original)
                if (pair.Key != null && pair.Key.shadowCastingMode == ShadowCastingMode.Off)
                    pair.Key.shadowCastingMode = pair.Value;
            original.Clear();
        }
    }
}
