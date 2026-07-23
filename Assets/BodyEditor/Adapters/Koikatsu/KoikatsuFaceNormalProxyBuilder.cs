using System.Collections.Generic;
using BodyEditor.Rendering;
using UnityEngine;

namespace BodyEditor.ReferenceModels
{
    internal static class KoikatsuFaceNormalProxyBuilder
    {
        private const string BlendProperty = "_FaceSphereNormalBlend";

        public static void Attach(GameObject headModel, Transform faceRoot)
        {
            if (headModel == null || faceRoot == null)
            {
                return;
            }

            var renderers = CollectFaceRenderers(headModel);
            if (renderers.Count == 0 ||
                !TryEstimateSphere(
                    renderers,
                    faceRoot,
                    out var centerLocal))
            {
                return;
            }

            var proxy = faceRoot.GetComponent<FaceSphereNormalProxy>() ??
                        faceRoot.gameObject.AddComponent<FaceSphereNormalProxy>();
            proxy.Configure(renderers, centerLocal);
        }

        private static List<Renderer> CollectFaceRenderers(GameObject headModel)
        {
            var result = new List<Renderer>();
            var renderers = headModel.GetComponentsInChildren<
                SkinnedMeshRenderer>(true);
            for (var rendererIndex = 0;
                 rendererIndex < renderers.Length;
                 rendererIndex++)
            {
                var materials = renderers[rendererIndex].sharedMaterials;
                for (var materialIndex = 0;
                     materialIndex < materials.Length;
                     materialIndex++)
                {
                    var material = materials[materialIndex];
                    if (material != null &&
                        material.HasProperty(BlendProperty) &&
                        material.GetFloat(BlendProperty) > 0.001f)
                    {
                        result.Add(renderers[rendererIndex]);
                        break;
                    }
                }
            }

            return result;
        }

        private static bool TryEstimateSphere(
            IReadOnlyList<Renderer> renderers,
            Transform faceRoot,
            out Vector3 centerLocal)
        {
            var bounds = default(Bounds);
            var found = false;
            var normalSum = Vector3.zero;
            var normalCount = 0;

            for (var rendererIndex = 0;
                 rendererIndex < renderers.Count;
                 rendererIndex++)
            {
                var renderer = renderers[rendererIndex] as SkinnedMeshRenderer;
                if (renderer == null || renderer.sharedMesh == null)
                {
                    continue;
                }

                var bakedMesh = new Mesh
                {
                    name = "BodyEditor Face Normal Proxy Bake",
                };
                renderer.BakeMesh(bakedMesh);
                AccumulateFaceGeometry(
                    renderer,
                    bakedMesh,
                    faceRoot,
                    ref bounds,
                    ref found,
                    ref normalSum,
                    ref normalCount);
                KoikatsuCharacterAssembler.DestroyRuntimeObject(bakedMesh);
            }

            if (!found)
            {
                centerLocal = Vector3.zero;
                return false;
            }

            var horizontalRadius = Mathf.Max(
                bounds.extents.x,
                bounds.extents.y * 0.65f);
            centerLocal = bounds.center;

            var coherence = normalCount > 0
                ? normalSum.magnitude / normalCount
                : 0f;
            if (coherence > 0.2f)
            {
                var outward = normalSum.normalized;
                var depthOffset = Mathf.Max(
                    horizontalRadius * 0.72f,
                    bounds.extents.z * 0.25f);
                centerLocal -= outward * depthOffset;
            }

            return true;
        }

        private static void AccumulateFaceGeometry(
            SkinnedMeshRenderer renderer,
            Mesh mesh,
            Transform faceRoot,
            ref Bounds bounds,
            ref bool found,
            ref Vector3 normalSum,
            ref int normalCount)
        {
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var materials = renderer.sharedMaterials;
            var subMeshCount = Mathf.Min(mesh.subMeshCount, materials.Length);
            for (var subMesh = 0; subMesh < subMeshCount; subMesh++)
            {
                var material = materials[subMesh];
                if (material == null ||
                    !material.HasProperty(BlendProperty) ||
                    material.GetFloat(BlendProperty) <= 0.001f)
                {
                    continue;
                }

                var indices = mesh.GetIndices(subMesh);
                for (var index = 0; index < indices.Length; index++)
                {
                    var vertexIndex = indices[index];
                    if (vertexIndex < 0 || vertexIndex >= vertices.Length)
                    {
                        continue;
                    }

                    var worldPosition = renderer.transform.TransformPoint(
                        vertices[vertexIndex]);
                    var localPosition = faceRoot.InverseTransformPoint(
                        worldPosition);
                    if (found)
                    {
                        bounds.Encapsulate(localPosition);
                    }
                    else
                    {
                        bounds = new Bounds(localPosition, Vector3.zero);
                        found = true;
                    }

                    if (vertexIndex < normals.Length)
                    {
                        var worldNormal = renderer.transform.TransformDirection(
                            normals[vertexIndex]);
                        normalSum += faceRoot.InverseTransformDirection(
                            worldNormal).normalized;
                        normalCount++;
                    }
                }
            }
        }
    }
}
