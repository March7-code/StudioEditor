using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using static StudioEditor.ReferenceModels.ReferenceModelOverlayUtilities;

namespace StudioEditor.ReferenceModels
{
    internal sealed class SkeletonOverlay : IDisposable
    {
        private readonly GameObject root;
        private readonly Transform[] bones;
        private readonly int[] parentIndices;
        private readonly int[] bodyParentIndices;
        private readonly IReadOnlyList<ReferenceModelPartState> states;
        private readonly SkeletonLineLayer normalLines;
        private readonly SkeletonLineLayer highlightedLines;
        private readonly List<Vector3> normalVertices = new List<Vector3>();
        private readonly List<Vector3> highlightedVertices = new List<Vector3>();
        private bool bodyBonesOnly;
        private bool visible = true;

        public SkeletonOverlay(
            Transform parent,
            Transform[] bones,
            int[] parentIndices,
            int[] bodyParentIndices,
            IReadOnlyList<ReferenceModelPartState> states,
            Shader shader,
            Color normalColor,
            Color highlightedColor)
        {
            this.bones = bones;
            this.parentIndices = parentIndices;
            this.bodyParentIndices = bodyParentIndices;
            this.states = states;
            root = new GameObject("Reference Skeleton Overlay");
            root.transform.SetParent(parent, false);
            normalLines = new SkeletonLineLayer(
                root.transform,
                "Reference Skeleton Lines",
                shader,
                normalColor,
                4000);
            highlightedLines = new SkeletonLineLayer(
                root.transform,
                "Reference Skeleton Highlight",
                shader,
                highlightedColor,
                4001);
            Refresh();
        }

        public void Refresh()
        {
            if (root == null)
            {
                return;
            }

            normalVertices.Clear();
            highlightedVertices.Clear();
            for (var index = 0; index < bones.Length; index++)
            {
                if (bodyBonesOnly && !states[index].IsBodyBone)
                {
                    continue;
                }

                var parentIndex = bodyBonesOnly
                    ? bodyParentIndices[index]
                    : parentIndices[index];
                if (parentIndex < 0 || !states[index].Visible ||
                    bones[index] == null || bones[parentIndex] == null)
                {
                    continue;
                }

                var vertices = states[index].Highlighted
                    ? highlightedVertices
                    : normalVertices;
                vertices.Add(root.transform.InverseTransformPoint(
                    bones[parentIndex].position));
                vertices.Add(root.transform.InverseTransformPoint(
                    bones[index].position));
            }

            var hasVisibleLines = normalVertices.Count > 0 ||
                                  highlightedVertices.Count > 0;
            root.SetActive(visible && hasVisibleLines);
            if (visible && hasVisibleLines)
            {
                normalLines.SetVertices(normalVertices);
                highlightedLines.SetVertices(highlightedVertices);
            }
        }

        public void SetBodyBonesOnly(bool enabled)
        {
            bodyBonesOnly = enabled;
            Refresh();
        }

        public void SetVisible(bool enabled)
        {
            visible = enabled;
            Refresh();
        }

        public void Dispose()
        {
            normalLines.Dispose();
            highlightedLines.Dispose();
            Destroy(root);
        }
    }

    internal sealed class SkeletonLineLayer : IDisposable
    {
        private readonly Mesh mesh;
        private readonly Material material;
        private int vertexCount = -1;

        public SkeletonLineLayer(
            Transform parent,
            string name,
            Shader shader,
            Color color,
            int renderQueue)
        {
            var layer = new GameObject(name);
            layer.transform.SetParent(parent, false);
            mesh = new Mesh
            {
                name = name + " Mesh",
                hideFlags = HideFlags.DontSave,
            };
            layer.AddComponent<MeshFilter>().sharedMesh = mesh;

            material = CreateOverlayMaterial(shader, color, renderQueue);
            var renderer = layer.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }

        public void SetVertices(List<Vector3> vertices)
        {
            if (vertexCount != vertices.Count)
            {
                // SetVertices validates the existing index buffer immediately, so clear
                // stale line indices before shrinking the vertex buffer.
                mesh.Clear(false);
                mesh.SetVertices(vertices);
                vertexCount = vertices.Count;
                var indices = new int[vertexCount];
                for (var index = 0; index < vertexCount; index++)
                {
                    indices[index] = index;
                }

                mesh.SetIndices(indices, MeshTopology.Lines, 0);
            }
            else
            {
                mesh.SetVertices(vertices);
                mesh.RecalculateBounds();
            }
        }

        public void Dispose()
        {
            Destroy(mesh);
            Destroy(material);
        }
    }

    internal sealed class ReferenceMeshTopologyOverlay : IDisposable
    {
        private readonly Material material;
        private readonly ReferenceMeshTopologyEntry[] entries;

        public ReferenceMeshTopologyOverlay(
            IReadOnlyList<Renderer> renderers,
            Shader shader,
            Color color)
        {
            material = CreateOverlayMaterial(shader, color, 4005);
            entries = new ReferenceMeshTopologyEntry[renderers.Count];
            for (var index = 0; index < renderers.Count; index++)
            {
                entries[index] = new ReferenceMeshTopologyEntry(
                    renderers[index],
                    material);
                entries[index].SetVisible(false);
            }
        }

        public void SetVisible(int index, bool visible)
        {
            if (index >= 0 && index < entries.Length)
            {
                entries[index].SetVisible(visible);
            }
        }

        public void Dispose()
        {
            for (var index = 0; index < entries.Length; index++)
            {
                entries[index].Dispose();
            }

            Destroy(material);
        }
    }

    internal sealed class ReferenceMeshTopologyEntry : IDisposable
    {
        private readonly GameObject root;
        private readonly Mesh topologyMesh;

        public ReferenceMeshTopologyEntry(Renderer source, Material material)
        {
            root = new GameObject(source.name + " Topology");
            root.transform.SetParent(source.transform, false);
            root.layer = source.gameObject.layer;

            var sourceMesh = GetSourceMesh(source);
            topologyMesh = BuildTopologyMesh(sourceMesh);

            if (source is SkinnedMeshRenderer skinned)
            {
                var renderer = root.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = topologyMesh;
                renderer.bones = skinned.bones;
                renderer.rootBone = skinned.rootBone;
                renderer.localBounds = skinned.localBounds;
                renderer.updateWhenOffscreen = true;
                renderer.quality = skinned.quality;
                ConfigureRenderer(renderer, material);
                return;
            }

            root.AddComponent<MeshFilter>().sharedMesh = topologyMesh;
            ConfigureRenderer(root.AddComponent<MeshRenderer>(), material);
        }

        public void SetVisible(bool visible)
        {
            if (root != null)
            {
                root.SetActive(visible);
            }
        }

        public void Dispose()
        {
            Destroy(root);
            Destroy(topologyMesh);
        }

        private static Mesh GetSourceMesh(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned)
            {
                return skinned.sharedMesh;
            }

            return renderer.GetComponent<MeshFilter>().sharedMesh;
        }

        private static Mesh BuildTopologyMesh(Mesh source)
        {
            var mesh = new Mesh
            {
                name = source.name + " Topology",
                hideFlags = HideFlags.DontSave,
                indexFormat = source.indexFormat,
            };
            mesh.vertices = source.vertices;
            var boneWeights = source.boneWeights;
            if (boneWeights.Length == source.vertexCount)
            {
                mesh.boneWeights = boneWeights;
                mesh.bindposes = source.bindposes;
            }

            mesh.bounds = source.bounds;
            mesh.SetIndices(
                ReferenceMeshTopology.BuildUniqueTriangleEdges(source),
                MeshTopology.Lines,
                0,
                false);
            return mesh;
        }

        private static void ConfigureRenderer(Renderer renderer, Material material)
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }
    }

    internal sealed class MeshHighlightOverlay : IDisposable
    {
        private readonly Material material;
        private readonly MeshHighlightEntry[] entries;

        public MeshHighlightOverlay(
            IReadOnlyList<Renderer> renderers,
            Shader shader,
            Color color)
        {
            material = CreateOverlayMaterial(shader, color, 3990);
            entries = new MeshHighlightEntry[renderers.Count];
            for (var index = 0; index < renderers.Count; index++)
            {
                entries[index] = new MeshHighlightEntry(renderers[index], material);
                entries[index].SetVisible(false);
            }
        }

        public void SetVisible(int index, bool visible)
        {
            if (index >= 0 && index < entries.Length)
            {
                entries[index].SetVisible(visible);
            }
        }

        public void Refresh()
        {
            for (var index = 0; index < entries.Length; index++)
            {
                entries[index].Refresh();
            }
        }

        public void Dispose()
        {
            for (var index = 0; index < entries.Length; index++)
            {
                entries[index].Dispose();
            }

            Destroy(material);
        }
    }

    internal sealed class MeshHighlightEntry : IDisposable
    {
        private readonly GameObject root;
        private readonly SkinnedMeshRenderer sourceSkinned;
        private readonly SkinnedMeshRenderer overlaySkinned;

        public MeshHighlightEntry(Renderer source, Material material)
        {
            root = new GameObject(source.name + " Highlight");
            root.transform.SetParent(source.transform, false);
            root.layer = source.gameObject.layer;

            if (source is SkinnedMeshRenderer skinned)
            {
                sourceSkinned = skinned;
                overlaySkinned = root.AddComponent<SkinnedMeshRenderer>();
                overlaySkinned.sharedMesh = skinned.sharedMesh;
                overlaySkinned.bones = skinned.bones;
                overlaySkinned.rootBone = skinned.rootBone;
                overlaySkinned.localBounds = skinned.localBounds;
                overlaySkinned.updateWhenOffscreen = skinned.updateWhenOffscreen;
                overlaySkinned.quality = skinned.quality;
                overlaySkinned.sharedMaterials = RepeatMaterial(
                    material,
                    skinned.sharedMesh.subMeshCount);
                overlaySkinned.shadowCastingMode = ShadowCastingMode.Off;
                overlaySkinned.receiveShadows = false;
                return;
            }

            var sourceFilter = source.GetComponent<MeshFilter>();
            root.AddComponent<MeshFilter>().sharedMesh = sourceFilter.sharedMesh;
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = RepeatMaterial(
                material,
                sourceFilter.sharedMesh.subMeshCount);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        public void SetVisible(bool visible)
        {
            if (root != null)
            {
                root.SetActive(visible);
            }
        }

        public void Refresh()
        {
            if (root == null || !root.activeSelf ||
                sourceSkinned == null || overlaySkinned == null)
            {
                return;
            }

            var blendShapeCount = sourceSkinned.sharedMesh?.blendShapeCount ?? 0;
            for (var index = 0; index < blendShapeCount; index++)
            {
                overlaySkinned.SetBlendShapeWeight(
                    index,
                    sourceSkinned.GetBlendShapeWeight(index));
            }
        }

        public void Dispose()
        {
            Destroy(root);
        }

        private static Material[] RepeatMaterial(Material value, int count)
        {
            var result = new Material[Mathf.Max(1, count)];
            for (var index = 0; index < result.Length; index++)
            {
                result[index] = value;
            }

            return result;
        }
    }

    internal static class ReferenceModelOverlayUtilities
    {
        public static Material CreateOverlayMaterial(
            Shader shader,
            Color color,
            int renderQueue)
        {
            var material = new Material(shader)
            {
                name = "Studio Editor Overlay Material",
                hideFlags = HideFlags.DontSave,
                renderQueue = renderQueue,
            };
            material.SetColor("_BaseColor", color);
            return material;
        }

        public static void Destroy(UnityEngine.Object value)
        {
            if (value != null)
            {
                UnityEngine.Object.Destroy(value);
            }
        }
    }
}
