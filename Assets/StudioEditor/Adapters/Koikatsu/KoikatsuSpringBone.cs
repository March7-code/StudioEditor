using System;
using System.Collections.Generic;
using UnityEngine;

namespace StudioEditor.ReferenceModels
{
    // DynamicBone's particle update, including its authored parameter curves.
    // Keep simulation on the same transforms used by the skinned renderer.
    [DefaultExecutionOrder(32000)]
    internal sealed class KoikatsuSpringBone : MonoBehaviour
    {
        private Transform springRoot;
        private Particle[] particles = Array.Empty<Particle>();
        private IReadOnlyList<KoikatsuDynamicBoneCollider> colliders = Array.Empty<KoikatsuDynamicBoneCollider>();
        private float updateRate;
        private int freezeAxis;
        private Vector3 gravity, localGravity, force, previousOwnerPosition;
        private float accumulator;
        private bool configured, authoredAllowed, fkAllowed = true, simulationRequested;

        public bool Allowed => authoredAllowed && fkAllowed;
        public int FkGroupIndex { get; set; } = -1;

        public bool ContainsBoneName(ISet<string> names)
        {
            if (springRoot == null) return false;
            foreach (var bone in springRoot.GetComponentsInChildren<Transform>(true))
                if (names.Contains(bone.name)) return true;
            return false;
        }

        public void Configure(
            Transform root, float requestedUpdateRate, float damping,
            float elasticity, float stiffness, float inert, float radius,
            float endLength, Vector3 endOffset, Vector3 requestedGravity,
            Vector3 requestedForce, int requestedFreezeAxis,
            IReadOnlyCollection<Transform> exclusions,
            IReadOnlyList<KoikatsuDynamicBoneCollider> requestedColliders,
            bool allowed, AnimationCurve[] distributions = null,
            IReadOnlyCollection<Transform> notRolls = null)
        {
            springRoot = root ?? throw new ArgumentNullException(nameof(root));
            updateRate = Mathf.Max(0, requestedUpdateRate);
            freezeAxis = Mathf.Clamp(requestedFreezeAxis, 0, 3);
            gravity = requestedGravity;
            localGravity = root.InverseTransformDirection(gravity);
            force = requestedForce;
            authoredAllowed = allowed;
            colliders = requestedColliders ?? Array.Empty<KoikatsuDynamicBoneCollider>();
            var values = new List<Particle>();
            AppendParticles(root, -1, 0, values,
                new HashSet<Transform>(exclusions ?? Array.Empty<Transform>()),
                new HashSet<Transform>(notRolls ?? Array.Empty<Transform>()),
                endLength, endOffset);
            particles = values.ToArray();
            float totalLength = 0;
            foreach (var p in particles) totalLength = Mathf.Max(totalLength, p.BoneLength);
            foreach (var p in particles)
            {
                var rate = totalLength > 0 ? p.BoneLength / totalLength : 0;
                p.Damping = Mathf.Clamp01(damping * Distribution(distributions, 0, rate));
                p.Elasticity = Mathf.Clamp01(elasticity * Distribution(distributions, 1, rate));
                p.Stiffness = Mathf.Clamp01(stiffness * Distribution(distributions, 2, rate));
                p.Inert = Mathf.Clamp01(inert * Distribution(distributions, 3, rate));
                p.Radius = Mathf.Max(0, radius * Distribution(distributions, 4, rate));
            }
            configured = particles.Length > 1;
            ResetSimulation();
        }

        private static float Distribution(AnimationCurve[] curves, int index, float rate)
            => curves != null && index < curves.Length && curves[index] != null && curves[index].length > 0
                ? curves[index].Evaluate(rate) : 1;

        public void SetFkAllowed(bool value)
        {
            if (fkAllowed == value) return;
            fkAllowed = value;
            enabled = simulationRequested && Allowed && configured;
        }

        public void SetSimulationEnabled(bool value)
        {
            simulationRequested = value;
            enabled = value && Allowed && configured;
        }

        private void OnEnable() { if (configured) ResetSimulation(); }
        private void OnDisable() { if (configured) { RestoreTransforms(); ResetSimulation(); } }
        private void Update() { if (configured) RestoreTransforms(); }
        private void LateUpdate() => Simulate(Time.deltaTime);

        internal void Simulate(float deltaTime)
        {
            if (!configured || springRoot == null) return;
            var movement = transform.position - previousOwnerPosition;
            previousOwnerPosition = transform.position;
            var steps = 1;
            if (updateRate > 0)
            {
                var interval = 1 / updateRate;
                accumulator += Mathf.Max(0, deltaTime);
                steps = 0;
                while (accumulator >= interval)
                {
                    accumulator -= interval;
                    if (++steps >= 3) { accumulator = 0; break; }
                }
            }
            if (steps == 0) SkipUpdateParticles(movement);
            for (int step = 0; step < steps; step++)
            {
                SimulateParticles(step == 0 ? movement : Vector3.zero);
                ConstrainParticles();
            }
            ApplyParticles();
        }

        private void SimulateParticles(Vector3 movement)
        {
            var direction = gravity.normalized;
            var acceleration = (gravity - direction * Mathf.Max(
                Vector3.Dot(springRoot.TransformDirection(localGravity), direction), 0) + force)
                * Mathf.Abs(transform.lossyScale.x);
            AnchorRoot();
            for (int i = 1; i < particles.Length; i++)
            {
                var p = particles[i];
                var velocity = p.Position - p.PreviousPosition;
                var inherited = movement * p.Inert;
                p.PreviousPosition = p.Position + inherited;
                p.Position += velocity * (1 - p.Damping) + acceleration + inherited;
            }
        }

        private void ConstrainParticles()
        {
            for (int i = 1; i < particles.Length; i++)
            {
                var p = particles[i];
                var parent = particles[p.ParentIndex];
                var length = RestLength(p, parent);
                ConstrainRestPose(p, parent, length, p.Elasticity);
                var position = p.Position;
                var radius = p.Radius * Mathf.Abs(transform.lossyScale.x);
                foreach (var collider in colliders) collider.Collide(ref position, radius);
                p.Position = position;
                ApplyFreezeAxis(parent, p);
                EnforceLength(p, parent, length);
            }
        }

        private static float RestLength(Particle p, Particle parent) => p.Transform != null
            ? Vector3.Distance(parent.Transform.position, p.Transform.position)
            : parent.Transform.TransformVector(p.VirtualOffset).magnitude;

        private static Vector3 RestPosition(Particle p, Particle parent)
        {
            var matrix = parent.Transform.localToWorldMatrix;
            matrix.SetColumn(3, new Vector4(parent.Position.x, parent.Position.y, parent.Position.z, 1));
            return matrix.MultiplyPoint3x4(p.Transform != null ? p.Transform.localPosition : p.VirtualOffset);
        }

        private static void ConstrainRestPose(Particle p, Particle parent, float length, float elasticity)
        {
            if (p.Stiffness <= 0 && elasticity <= 0) return;
            var desired = RestPosition(p, parent);
            p.Position += (desired - p.Position) * elasticity;
            if (p.Stiffness <= 0) return;
            var displacement = desired - p.Position;
            var distance = displacement.magnitude;
            var limit = length * (1 - p.Stiffness) * 2;
            if (distance > limit) p.Position += displacement * ((distance - limit) / distance);
        }

        private void SkipUpdateParticles(Vector3 movement)
        {
            AnchorRoot();
            for (int i = 1; i < particles.Length; i++)
            {
                var p = particles[i];
                p.PreviousPosition += movement;
                p.Position += movement;
                var parent = particles[p.ParentIndex];
                var length = RestLength(p, parent);
                ConstrainRestPose(p, parent, length, 0);
                EnforceLength(p, parent, length);
            }
        }

        private static void EnforceLength(Particle p, Particle parent, float length)
        {
            var delta = parent.Position - p.Position;
            var distance = delta.magnitude;
            if (distance > 0) p.Position += delta * ((distance - length) / distance);
        }

        private void ApplyFreezeAxis(Particle parent, Particle p)
        {
            if (freezeAxis == 0) return;
            var normal = freezeAxis == 1 ? parent.Transform.right : freezeAxis == 2
                ? parent.Transform.up : parent.Transform.forward;
            p.Position -= normal * Vector3.Dot(p.Position - parent.Position, normal);
        }

        private void ApplyParticles()
        {
            for (int i = 1; i < particles.Length; i++)
            {
                var p = particles[i];
                var parent = particles[p.ParentIndex];
                if (parent.Transform.childCount <= 1)
                {
                    var local = p.Transform != null ? p.Transform.localPosition : p.VirtualOffset;
                    var direction = p.Position - parent.Position;
                    direction = Mirror(direction, 0);
                    direction = Mirror(direction, 1);
                    direction = Mirror(direction, 2);
                    parent.Transform.rotation = Quaternion.FromToRotation(
                        parent.Transform.TransformDirection(local), direction) * parent.Transform.rotation;
                }
                if (p.Transform != null) p.Transform.position = p.Position;
            }
        }

        private Vector3 Mirror(Vector3 direction, int axis)
        {
            var owner = transform;
            if (owner.localScale[axis] >= 0)
            {
                owner = owner.parent;
                if (owner == null || owner.localScale[axis] >= 0) return direction;
            }
            var normal = axis == 0 ? owner.right : axis == 1 ? owner.up : owner.forward;
            return direction - normal * (Vector3.Dot(direction, normal) * 2);
        }

        private void AnchorRoot()
        {
            particles[0].PreviousPosition = particles[0].Position;
            particles[0].Position = springRoot.position;
        }

        internal void RestoreTransforms()
        {
            foreach (var p in particles)
            {
                if (p.Transform == null) continue;
                p.Transform.localPosition = p.InitialLocalPosition;
                p.Transform.localRotation = p.InitialLocalRotation;
            }
        }

        private void ResetSimulation()
        {
            accumulator = 0;
            previousOwnerPosition = transform.position;
            foreach (var p in particles)
            {
                p.Position = p.Transform != null ? p.Transform.position :
                    particles[p.ParentIndex].Transform.TransformPoint(p.VirtualOffset);
                p.PreviousPosition = p.Position;
            }
        }

        private void AppendParticles(Transform current, int parentIndex, float length,
            List<Particle> values, ISet<Transform> exclusions, ISet<Transform> notRolls,
            float endLength, Vector3 endOffset)
        {
            var p = new Particle { Transform = current, ParentIndex = parentIndex };
            if (current != null)
            {
                p.InitialLocalPosition = current.localPosition;
                p.InitialLocalRotation = current.localRotation;
                p.Position = current.position;
            }
            else
            {
                var parent = values[parentIndex].Transform;
                p.VirtualOffset = endLength > 0
                    ? parent.parent != null ? parent.InverseTransformPoint(parent.position * 2 - parent.parent.position) * endLength
                        : Vector3.right * endLength
                    : parent.InverseTransformPoint(transform.TransformDirection(endOffset) + parent.position);
                p.Position = parent.TransformPoint(p.VirtualOffset);
            }
            p.PreviousPosition = p.Position;
            if (parentIndex >= 0) length += Vector3.Distance(values[parentIndex].Transform.position, p.Position);
            p.BoneLength = length;
            var index = values.Count;
            values.Add(p);
            if (current == null) return;
            Transform skipped = null;
            for (int c = 0; c < current.childCount; c++)
            {
                var child = current.GetChild(c);
                if (exclusions.Contains(child)) continue;
                if (notRolls.Contains(child)) { skipped = child; continue; }
                AppendParticles(child, index, length, values, exclusions, notRolls, endLength, endOffset);
            }
            if (skipped != null)
                for (int c = 0; c < skipped.childCount; c++)
                {
                    var child = skipped.GetChild(c);
                    if (!exclusions.Contains(child) && !notRolls.Contains(child))
                        AppendParticles(child, index, length, values, exclusions, notRolls, endLength, endOffset);
                }
            if (current.childCount == 0 && (endLength > 0 || endOffset != Vector3.zero))
                AppendParticles(null, index, length, values, exclusions, notRolls, endLength, endOffset);
        }

        private sealed class Particle
        {
            public Transform Transform { get; set; }
            public int ParentIndex;
            public Vector3 InitialLocalPosition, VirtualOffset, Position, PreviousPosition;
            public Quaternion InitialLocalRotation;
            public float BoneLength, Damping, Elasticity, Stiffness, Inert, Radius;
        }
    }
}
