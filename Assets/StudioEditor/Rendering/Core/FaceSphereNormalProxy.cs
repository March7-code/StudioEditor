using System.Collections.Generic;
using UnityEngine;

namespace StudioEditor.Rendering
{
    [ExecuteAlways]
    [DefaultExecutionOrder(1000)]
    public sealed class FaceSphereNormalProxy : MonoBehaviour
    {
        private static readonly int CenterProperty =
            Shader.PropertyToID("_FaceSphereCenterWS");
        private static readonly int EnabledProperty =
            Shader.PropertyToID("_FaceSphereNormalEnabled");
        private static readonly int UpProperty =
            Shader.PropertyToID("_FaceSphereUpWS");

        [SerializeField]
        private Renderer[] targetRenderers = new Renderer[0];

        [SerializeField]
        private Vector3 centerLocal;

        private MaterialPropertyBlock propertyBlock;

        public void Configure(
            IReadOnlyList<Renderer> renderers,
            Vector3 sphereCenterLocal)
        {
            centerLocal = sphereCenterLocal;
            if (renderers == null || renderers.Count == 0)
            {
                targetRenderers = new Renderer[0];
            }
            else
            {
                var targets = new List<Renderer>(renderers.Count);
                for (var index = 0; index < renderers.Count; index++)
                {
                    if (renderers[index] != null &&
                        !targets.Contains(renderers[index]))
                    {
                        targets.Add(renderers[index]);
                    }
                }

                targetRenderers = targets.ToArray();
            }

            Apply(true);
        }

        private void OnEnable()
        {
            Apply(true);
        }

        private void LateUpdate()
        {
            Apply(true);
        }

        private void OnDisable()
        {
            Apply(false);
        }

        private void Apply(bool enabledState)
        {
            if (targetRenderers == null || targetRenderers.Length == 0)
            {
                return;
            }

            if (propertyBlock == null)
            {
                propertyBlock = new MaterialPropertyBlock();
            }

            var center = transform.TransformPoint(centerLocal);
            var centerValue = new Vector4(center.x, center.y, center.z, 1f);
            var up = transform.up.normalized;
            var upValue = new Vector4(up.x, up.y, up.z, 0f);
            for (var index = 0; index < targetRenderers.Length; index++)
            {
                var target = targetRenderers[index];
                if (target == null)
                {
                    continue;
                }

                target.GetPropertyBlock(propertyBlock);
                propertyBlock.SetVector(CenterProperty, centerValue);
                propertyBlock.SetVector(UpProperty, upValue);
                propertyBlock.SetFloat(EnabledProperty, enabledState ? 1f : 0f);
                target.SetPropertyBlock(propertyBlock);
                propertyBlock.Clear();
            }
        }
    }
}
