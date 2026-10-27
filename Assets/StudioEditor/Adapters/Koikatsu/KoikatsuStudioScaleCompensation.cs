using System.Collections.Generic;
using UnityEngine;

namespace StudioEditor.ReferenceModels
{
    // Studio.GuideObject keeps scene object scale in world units, including
    // objects attached to scaled character bones. Run after the character pose
    // and before item IK, with parents registered before their children.
    [DefaultExecutionOrder(30500)]
    internal sealed class KoikatsuStudioScaleCompensation : MonoBehaviour
    {
        private readonly List<Binding> bindings = new();

        internal static KoikatsuStudioScaleCompensation Attach(
            GameObject host,
            IReadOnlyList<KoikatsuSceneObject> sources,
            IReadOnlyDictionary<int, GameObject> objects)
        {
            var component = host.AddComponent<KoikatsuStudioScaleCompensation>();
            component.RegisterObjects(sources, objects);
            component.EvaluateNow();
            return component;
        }

        internal void Register(Transform target, Vector3 worldScale)
        {
            if (target != null) bindings.Add(new Binding(target, worldScale));
        }

        internal void Replace(Transform previous, Transform replacement)
        {
            foreach (var binding in bindings)
            {
                if (binding.Target != previous) continue;
                binding.Target = replacement;
                binding.Applied = false;
            }
            EvaluateNow();
        }

        internal bool TrySetWorldScale(Transform target, Vector3 scale)
        {
            foreach (var binding in bindings)
            {
                if (binding.Target != target) continue;
                binding.SetWorldScale(scale);
                return true;
            }
            return false;
        }

        private void RegisterObjects(
            IReadOnlyList<KoikatsuSceneObject> sources,
            IReadOnlyDictionary<int, GameObject> objects)
        {
            foreach (var source in sources)
            {
                if (objects.TryGetValue(source.Base.DicKey, out var root))
                    Register(root.transform, source.Base.Scale);
                RegisterObjects(source.Children, objects);
            }
        }

        internal void EvaluateNow()
        {
            foreach (var binding in bindings) binding.Evaluate();
        }

        private void LateUpdate() => EvaluateNow();

        private sealed class Binding
        {
            internal Transform Target;
            internal bool Applied;
            private Vector3 worldScale;
            private Vector3 lastLocalScale;

            internal Binding(Transform target, Vector3 scale)
            {
                Target = target;
                worldScale = scale;
            }

            internal void SetWorldScale(Vector3 scale)
            {
                worldScale = scale;
                Applied = false;
                Evaluate();
            }

            internal void Evaluate()
            {
                if (Target == null) return;
                var local = Target.localScale;
                var world = Target.lossyScale;
                if (Applied && local != lastLocalScale)
                {
                    // Preserve subsequent scale edits rather than restoring the
                    // value from the card every frame.
                    worldScale = new Vector3(
                        EditedScale(worldScale.x, local.x, lastLocalScale.x, world.x),
                        EditedScale(worldScale.y, local.y, lastLocalScale.y, world.y),
                        EditedScale(worldScale.z, local.z, lastLocalScale.z, world.z));
                }
                Target.localScale = new Vector3(
                    Compensate(local.x, world.x, worldScale.x),
                    Compensate(local.y, world.y, worldScale.y),
                    Compensate(local.z, world.z, worldScale.z));
                lastLocalScale = Target.localScale;
                Applied = true;
            }

            private static float EditedScale(float saved, float local, float previous, float world)
                => Mathf.Abs(previous) > 0.000001f ? saved * local / previous : world;

            private static float Compensate(float local, float world, float saved)
                => Mathf.Abs(world) > 0.000001f ? local / world * saved : local;
        }
    }
}
