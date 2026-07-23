using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using static StudioEditor.ReferenceModels.ReferenceModelOverlayUtilities;

namespace StudioEditor.ReferenceModels
{
    public enum ReferenceBodySegment
    {
        Waist,
        Abdomen,
        Chest,
        UpperChest,
        LeftUpperArm,
        LeftForearm,
        RightUpperArm,
        RightForearm,
        LeftThigh,
        LeftCalf,
        RightThigh,
        RightCalf,
    }

    public readonly struct ReferenceSectionBoneInfluence
    {
        public ReferenceSectionBoneInfluence(
            string boneName,
            HumanBodyBones? humanoidBone,
            float weight)
        {
            BoneName = boneName ?? string.Empty;
            HumanoidBone = humanoidBone;
            Weight = Mathf.Clamp01(weight);
        }

        public string BoneName { get; }
        public HumanBodyBones? HumanoidBone { get; }
        public float Weight { get; }
    }

    public sealed class ReferenceSectionVertex
    {
        internal ReferenceSectionVertex(
            Vector3 position,
            int sourceRendererIndex,
            int sourceVertexIndex,
            IReadOnlyList<ReferenceSectionBoneInfluence> influences)
        {
            Position = position;
            SourceRendererIndex = sourceRendererIndex;
            SourceVertexIndex = sourceVertexIndex;

            var values = new ReferenceSectionBoneInfluence[influences.Count];
            for (var index = 0; index < influences.Count; index++)
            {
                values[index] = influences[index];
            }

            Influences = Array.AsReadOnly(values);
        }

        public Vector3 Position { get; }
        public int SourceRendererIndex { get; }
        public int SourceVertexIndex { get; }
        public IReadOnlyList<ReferenceSectionBoneInfluence> Influences { get; }

        public HumanBodyBones? DominantHumanoidBone
        {
            get
            {
                var selectedWeight = 0f;
                HumanBodyBones? selected = null;
                for (var index = 0; index < Influences.Count; index++)
                {
                    var influence = Influences[index];
                    if (influence.HumanoidBone.HasValue &&
                        influence.Weight > selectedWeight)
                    {
                        selectedWeight = influence.Weight;
                        selected = influence.HumanoidBone;
                    }
                }

                return selected;
            }
        }

        public float GetHumanoidInfluence(HumanBodyBones bone)
        {
            var result = 0f;
            for (var index = 0; index < Influences.Count; index++)
            {
                if (Influences[index].HumanoidBone == bone)
                {
                    result += Influences[index].Weight;
                }
            }

            return Mathf.Clamp01(result);
        }
    }

    public sealed class ReferenceSectionRing
    {
        internal ReferenceSectionRing(
            ReferenceBodySegment segment,
            float normalizedPosition,
            Vector3 segmentStart,
            Vector3 segmentEnd,
            Vector3 center,
            Vector3 axis,
            bool isClosed,
            IReadOnlyList<ReferenceSectionVertex> sourceSamples)
        {
            Segment = segment;
            NormalizedPosition = Mathf.Clamp01(normalizedPosition);
            SegmentStart = segmentStart;
            SegmentEnd = segmentEnd;
            Center = center;
            Axis = axis.normalized;
            IsClosed = isClosed;

            var samples = new ReferenceSectionVertex[sourceSamples.Count];
            var vertices = new Vector3[sourceSamples.Count];
            for (var index = 0; index < sourceSamples.Count; index++)
            {
                samples[index] = sourceSamples[index];
                vertices[index] = sourceSamples[index].Position;
            }

            SourceSamples = Array.AsReadOnly(samples);
            SourceVertices = Array.AsReadOnly(vertices);
        }

        public ReferenceBodySegment Segment { get; }
        public float NormalizedPosition { get; }
        public Vector3 SegmentStart { get; }
        public Vector3 SegmentEnd { get; }
        public Vector3 Center { get; }
        public Vector3 Axis { get; }
        public bool IsClosed { get; }
        public IReadOnlyList<ReferenceSectionVertex> SourceSamples { get; }
        public IReadOnlyList<Vector3> SourceVertices { get; }
    }

    public enum ReferenceJointType
    {
        LeftKnee,
        RightKnee,
        LeftElbow,
        RightElbow,
    }

    public readonly struct ReferenceJointConnection
    {
        internal ReferenceJointConnection(
            ReferenceSectionVertex jointVertex,
            ReferenceSectionVertex adjacentVertex,
            bool towardChild)
        {
            JointVertex = jointVertex;
            AdjacentVertex = adjacentVertex;
            TowardChild = towardChild;
        }

        public ReferenceSectionVertex JointVertex { get; }
        public ReferenceSectionVertex AdjacentVertex { get; }
        public bool TowardChild { get; }
    }

    public readonly struct ReferenceJointTriangle
    {
        internal ReferenceJointTriangle(
            ReferenceSectionVertex first,
            ReferenceSectionVertex second,
            ReferenceSectionVertex third)
        {
            First = first;
            Second = second;
            Third = third;
        }

        public ReferenceSectionVertex First { get; }
        public ReferenceSectionVertex Second { get; }
        public ReferenceSectionVertex Third { get; }
    }

    public sealed class ReferenceJointPatch
    {
        internal ReferenceJointPatch(
            ReferenceJointType joint,
            Vector3 center,
            Vector3 axis,
            bool isClosed,
            IReadOnlyList<ReferenceSectionVertex> centerRing,
            IReadOnlyList<ReferenceJointConnection> connections,
            IReadOnlyList<ReferenceJointTriangle> triangles)
        {
            Joint = joint;
            Center = center;
            Axis = axis.normalized;
            IsClosed = isClosed;

            var ringValues = new ReferenceSectionVertex[centerRing.Count];
            for (var index = 0; index < centerRing.Count; index++)
            {
                ringValues[index] = centerRing[index];
            }

            var connectionValues = new ReferenceJointConnection[
                connections.Count];
            for (var index = 0; index < connections.Count; index++)
            {
                connectionValues[index] = connections[index];
            }

            var triangleValues = new ReferenceJointTriangle[triangles.Count];
            for (var index = 0; index < triangles.Count; index++)
            {
                triangleValues[index] = triangles[index];
            }

            CenterRing = Array.AsReadOnly(ringValues);
            Connections = Array.AsReadOnly(connectionValues);
            Triangles = Array.AsReadOnly(triangleValues);
        }

        public ReferenceJointType Joint { get; }
        public Vector3 Center { get; }
        public Vector3 Axis { get; }
        public bool IsClosed { get; }
        public IReadOnlyList<ReferenceSectionVertex> CenterRing { get; }
        public IReadOnlyList<ReferenceJointConnection> Connections { get; }
        public IReadOnlyList<ReferenceJointTriangle> Triangles { get; }
    }

    internal sealed class ReferenceSectionRingOverlay : IDisposable
    {
        public const int DefaultRingCount = 20;
        public const int MaximumRingCount = 100;

        private static readonly SegmentDefinition[] SegmentDefinitions =
        {
            new SegmentDefinition(
                ReferenceBodySegment.Waist,
                HumanBodyBones.Hips,
                HumanBodyBones.Spine),
            new SegmentDefinition(
                ReferenceBodySegment.Abdomen,
                HumanBodyBones.Spine,
                HumanBodyBones.Chest),
            new SegmentDefinition(
                ReferenceBodySegment.Chest,
                HumanBodyBones.Chest,
                HumanBodyBones.Neck,
                HumanBodyBones.UpperChest),
            new SegmentDefinition(
                ReferenceBodySegment.Chest,
                HumanBodyBones.Chest,
                HumanBodyBones.UpperChest),
            new SegmentDefinition(
                ReferenceBodySegment.UpperChest,
                HumanBodyBones.UpperChest,
                HumanBodyBones.Neck),
            new SegmentDefinition(
                ReferenceBodySegment.LeftUpperArm,
                HumanBodyBones.LeftUpperArm,
                HumanBodyBones.LeftLowerArm),
            new SegmentDefinition(
                ReferenceBodySegment.LeftForearm,
                HumanBodyBones.LeftLowerArm,
                HumanBodyBones.LeftHand),
            new SegmentDefinition(
                ReferenceBodySegment.RightUpperArm,
                HumanBodyBones.RightUpperArm,
                HumanBodyBones.RightLowerArm),
            new SegmentDefinition(
                ReferenceBodySegment.RightForearm,
                HumanBodyBones.RightLowerArm,
                HumanBodyBones.RightHand),
            new SegmentDefinition(
                ReferenceBodySegment.LeftThigh,
                HumanBodyBones.LeftUpperLeg,
                HumanBodyBones.LeftLowerLeg),
            new SegmentDefinition(
                ReferenceBodySegment.LeftCalf,
                HumanBodyBones.LeftLowerLeg,
                HumanBodyBones.LeftFoot),
            new SegmentDefinition(
                ReferenceBodySegment.RightThigh,
                HumanBodyBones.RightUpperLeg,
                HumanBodyBones.RightLowerLeg),
            new SegmentDefinition(
                ReferenceBodySegment.RightCalf,
                HumanBodyBones.RightLowerLeg,
                HumanBodyBones.RightFoot),
        };

        private static readonly JointDefinition[] KneeDefinitions =
        {
            new JointDefinition(
                ReferenceJointType.LeftKnee,
                HumanBodyBones.LeftUpperLeg,
                HumanBodyBones.LeftLowerLeg,
                HumanBodyBones.LeftFoot,
                ReferenceBodySegment.LeftThigh),
            new JointDefinition(
                ReferenceJointType.RightKnee,
                HumanBodyBones.RightUpperLeg,
                HumanBodyBones.RightLowerLeg,
                HumanBodyBones.RightFoot,
                ReferenceBodySegment.RightThigh),
        };

        private static readonly JointDefinition[] ElbowDefinitions =
        {
            new JointDefinition(
                ReferenceJointType.LeftElbow,
                HumanBodyBones.LeftUpperArm,
                HumanBodyBones.LeftLowerArm,
                HumanBodyBones.LeftHand,
                ReferenceBodySegment.LeftUpperArm),
            new JointDefinition(
                ReferenceJointType.RightElbow,
                HumanBodyBones.RightUpperArm,
                HumanBodyBones.RightLowerArm,
                HumanBodyBones.RightHand,
                ReferenceBodySegment.RightUpperArm),
        };

        private readonly GameObject root;
        private readonly Mesh mesh;
        private readonly Material material;
        private readonly SliceSource[] sources;
        private readonly Dictionary<HumanBodyBones, Transform> bones =
            new Dictionary<HumanBodyBones, Transform>();
        private readonly Dictionary<HumanBodyBones, List<Transform>>
            skinningBones = new Dictionary<HumanBodyBones, List<Transform>>();
        private readonly Dictionary<Transform, ReferenceModelBone> boneMetadata =
            new Dictionary<Transform, ReferenceModelBone>();
        private readonly List<Vector3> lineVertices = new List<Vector3>();
        private readonly List<ActualRingCandidate> candidates =
            new List<ActualRingCandidate>();
        private readonly List<ReferenceSectionRing> rings =
            new List<ReferenceSectionRing>();
        private readonly List<ReferenceJointPatch> jointPatches =
            new List<ReferenceJointPatch>();
        private readonly HashSet<ulong> renderedSignatures =
            new HashSet<ulong>();
        private bool visible;

        public ReferenceSectionRingOverlay(
            Transform parent,
            IReadOnlyList<Renderer> renderers,
            IReferenceModelSkeletonProvider skeletonProvider,
            Shader shader,
            Color color)
        {
            root = new GameObject("Reference Source Vertex Rings");
            root.transform.SetParent(parent, false);
            mesh = new Mesh
            {
                name = "Reference Source Vertex Ring Mesh",
                hideFlags = HideFlags.DontSave,
                indexFormat = IndexFormat.UInt32,
            };
            root.AddComponent<MeshFilter>().sharedMesh = mesh;

            material = CreateOverlayMaterial(shader, color, 4010);
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;

            if (skeletonProvider?.Bones != null)
            {
                for (var index = 0; index < skeletonProvider.Bones.Count; index++)
                {
                    var sourceBone = skeletonProvider.Bones[index];
                    if (sourceBone.HumanoidBone.HasValue &&
                        sourceBone.Transform != null)
                    {
                        var semanticBone = sourceBone.HumanoidBone.Value;
                        if (!bones.ContainsKey(semanticBone))
                        {
                            bones.Add(semanticBone, sourceBone.Transform);
                        }

                        if (!skinningBones.TryGetValue(
                                semanticBone,
                                out var transforms))
                        {
                            transforms = new List<Transform>();
                            skinningBones.Add(semanticBone, transforms);
                        }

                        transforms.Add(sourceBone.Transform);
                    }

                    if (sourceBone.Transform != null &&
                        !boneMetadata.ContainsKey(sourceBone.Transform))
                    {
                        boneMetadata.Add(sourceBone.Transform, sourceBone);
                    }
                }
            }

            sources = new SliceSource[renderers.Count];
            for (var index = 0; index < renderers.Count; index++)
            {
                sources[index] = new SliceSource(
                    renderers[index],
                    boneMetadata);
            }

            SupportedSegmentCount = CountSupportedSegments();
            root.SetActive(false);
        }

        public int SupportedSegmentCount { get; }
        public bool IsSupported => SupportedSegmentCount > 0;
        public IReadOnlyList<ReferenceSectionRing> Rings => rings;
        public IReadOnlyList<ReferenceJointPatch> JointPatches => jointPatches;

        public void Rebuild(
            IReadOnlyList<ReferenceModelPartState> states,
            IReadOnlyList<bool> initiallyEnabled,
            int ringCount = DefaultRingCount)
        {
            lineVertices.Clear();
            rings.Clear();
            jointPatches.Clear();
            renderedSignatures.Clear();
            ringCount = Mathf.Clamp(ringCount, 1, MaximumRingCount);

            for (var segmentIndex = 0;
                 segmentIndex < SegmentDefinitions.Length;
                 segmentIndex++)
            {
                var definition = SegmentDefinitions[segmentIndex];
                if (!definition.IsAvailable(bones) ||
                    !bones.TryGetValue(definition.Start, out var start) ||
                    !bones.TryGetValue(definition.End, out var end))
                {
                    continue;
                }

                var axis = end.position - start.position;
                var length = axis.magnitude;
                if (length < 0.0001f)
                {
                    continue;
                }

                axis /= length;
                var planeEpsilon = Mathf.Max(0.000001f, length * 0.00001f);
                var markerSize = Mathf.Clamp(length * 0.006f, 0.0008f, 0.004f);
                for (var ringIndex = 0; ringIndex < ringCount; ringIndex++)
                {
                    var requestedT = (ringIndex + 1f) / (ringCount + 1f);
                    var requestedCenter = Vector3.Lerp(
                        start.position,
                        end.position,
                        requestedT);
                    var plane = new Plane(axis, requestedCenter);
                    candidates.Clear();

                    for (var sourceIndex = 0;
                         sourceIndex < sources.Length;
                         sourceIndex++)
                    {
                        if (sourceIndex >= states.Count ||
                            sourceIndex >= initiallyEnabled.Count ||
                            !states[sourceIndex].Visible ||
                            !initiallyEnabled[sourceIndex])
                        {
                            continue;
                        }

                        sources[sourceIndex].AppendCandidates(
                            sourceIndex,
                            plane,
                            definition,
                            skinningBones,
                            requestedCenter,
                            axis,
                            planeEpsilon,
                            candidates);
                    }

                    var selected = SelectCandidate(candidates);
                    if (selected == null ||
                        !renderedSignatures.Add(selected.Signature))
                    {
                        continue;
                    }

                    var actualT = CalculateNormalizedPosition(
                        selected.SourceVertices,
                        start.position,
                        axis,
                        length);
                    var actualCenter = start.position + axis * (actualT * length);
                    rings.Add(new ReferenceSectionRing(
                        definition.Segment,
                        actualT,
                        start.position,
                        end.position,
                        actualCenter,
                        axis,
                        selected.IsClosed,
                        selected.SourceSamples));
                    AppendCandidate(selected, markerSize);
                }
            }

            AppendKneeJointPatches(states, initiallyEnabled);
            AppendElbowJointPatches(states, initiallyEnabled);

            mesh.Clear(false);
            mesh.SetVertices(lineVertices);
            var indices = new int[lineVertices.Count];
            for (var index = 0; index < indices.Length; index++)
            {
                indices[index] = index;
            }

            mesh.SetIndices(indices, MeshTopology.Lines, 0);
            root.SetActive(visible && lineVertices.Count > 0);
        }

        private void AppendKneeJointPatches(
            IReadOnlyList<ReferenceModelPartState> states,
            IReadOnlyList<bool> initiallyEnabled)
        {
            for (var jointIndex = 0;
                 jointIndex < KneeDefinitions.Length;
                 jointIndex++)
            {
                var definition = KneeDefinitions[jointIndex];
                if (!definition.IsAvailable(bones))
                {
                    continue;
                }

                var parent = bones[definition.Parent];
                var joint = bones[definition.Joint];
                var child = bones[definition.Child];
                var incoming = joint.position - parent.position;
                var outgoing = child.position - joint.position;
                var length = (incoming.magnitude + outgoing.magnitude) * 0.5f;
                if (incoming.sqrMagnitude < 0.00000001f ||
                    outgoing.sqrMagnitude < 0.00000001f)
                {
                    continue;
                }

                var axis = incoming.normalized + outgoing.normalized;
                if (axis.sqrMagnitude < 0.00000001f)
                {
                    axis = incoming.normalized;
                }
                else
                {
                    axis.Normalize();
                }

                var center = joint.position;
                var plane = new Plane(axis, center);
                var epsilon = Mathf.Max(0.000001f, length * 0.00001f);
                candidates.Clear();
                for (var sourceIndex = 0;
                     sourceIndex < sources.Length;
                     sourceIndex++)
                {
                    if (sourceIndex >= states.Count ||
                        sourceIndex >= initiallyEnabled.Count ||
                        !states[sourceIndex].Visible ||
                        !initiallyEnabled[sourceIndex])
                    {
                        continue;
                    }

                    sources[sourceIndex].AppendCandidates(
                        sourceIndex,
                        plane,
                        definition.Segment,
                        skinningBones,
                        center,
                        axis,
                        epsilon,
                        candidates,
                        false);
                }

                var selected = SelectCandidate(candidates);
                if (selected == null || selected.SourceSamples.Count == 0)
                {
                    continue;
                }

                var sourceRendererIndex =
                    selected.SourceSamples[0].SourceRendererIndex;
                if (sourceRendererIndex < 0 ||
                    sourceRendererIndex >= sources.Length)
                {
                    continue;
                }

                var patch = sources[sourceRendererIndex].BuildJointPatch(
                    definition.JointType,
                    center,
                    axis,
                    selected);
                if (patch == null || patch.Triangles.Count == 0)
                {
                    continue;
                }

                jointPatches.Add(patch);
                AppendJointPatchTriangles(
                    patch,
                    Mathf.Clamp(length * 0.006f, 0.0008f, 0.004f));
            }
        }

        private void AppendElbowJointPatches(
            IReadOnlyList<ReferenceModelPartState> states,
            IReadOnlyList<bool> initiallyEnabled)
        {
            for (var jointIndex = 0;
                 jointIndex < ElbowDefinitions.Length;
                 jointIndex++)
            {
                var definition = ElbowDefinitions[jointIndex];
                if (!definition.IsAvailable(bones))
                {
                    continue;
                }

                var parent = bones[definition.Parent];
                var joint = bones[definition.Joint];
                var child = bones[definition.Child];
                var incoming = joint.position - parent.position;
                var outgoing = child.position - joint.position;
                var length = (incoming.magnitude + outgoing.magnitude) * 0.5f;
                if (incoming.sqrMagnitude < 0.00000001f ||
                    outgoing.sqrMagnitude < 0.00000001f)
                {
                    continue;
                }

                var axis = incoming.normalized + outgoing.normalized;
                if (axis.sqrMagnitude < 0.00000001f)
                {
                    axis = incoming.normalized;
                }
                else
                {
                    axis.Normalize();
                }

                var center = joint.position;
                var plane = new Plane(axis, center);
                var epsilon = Mathf.Max(0.000001f, length * 0.00001f);
                ReferenceJointPatch selected = null;
                var selectedRadius = float.PositiveInfinity;
                for (var sourceIndex = 0;
                     sourceIndex < sources.Length;
                     sourceIndex++)
                {
                    if (sourceIndex >= states.Count ||
                        sourceIndex >= initiallyEnabled.Count ||
                        !states[sourceIndex].Visible ||
                        !initiallyEnabled[sourceIndex])
                    {
                        continue;
                    }

                    var patch = sources[sourceIndex].BuildExpandedJointPatch(
                        sourceIndex,
                        definition.JointType,
                        center,
                        axis,
                        plane,
                        definition.Segment,
                        skinningBones,
                        epsilon,
                        2);
                    if (patch == null || patch.Triangles.Count == 0)
                    {
                        continue;
                    }

                    var averageRadius = CalculateJointSeedRadius(patch);
                    if (averageRadius < selectedRadius)
                    {
                        selected = patch;
                        selectedRadius = averageRadius;
                    }
                }

                if (selected == null)
                {
                    continue;
                }

                jointPatches.Add(selected);
                AppendJointPatchTriangles(
                    selected,
                    Mathf.Clamp(length * 0.006f, 0.0008f, 0.004f));
            }
        }

        private static float CalculateJointSeedRadius(
            ReferenceJointPatch patch)
        {
            if (patch.CenterRing.Count == 0)
            {
                return float.PositiveInfinity;
            }

            var result = 0f;
            for (var index = 0; index < patch.CenterRing.Count; index++)
            {
                var radial = patch.CenterRing[index].Position - patch.Center;
                radial -= patch.Axis * Vector3.Dot(radial, patch.Axis);
                result += radial.magnitude;
            }

            return result / patch.CenterRing.Count;
        }

        private void AppendJointPatchTriangles(
            ReferenceJointPatch patch,
            float markerSize)
        {
            var markedVertices = new HashSet<ulong>();
            for (var index = 0; index < patch.Triangles.Count; index++)
            {
                var triangle = patch.Triangles[index];
                AppendLine(triangle.First.Position, triangle.Second.Position);
                AppendLine(triangle.Second.Position, triangle.Third.Position);
                AppendLine(triangle.Third.Position, triangle.First.Position);
                AppendJointVertexMarker(
                    triangle.First,
                    markerSize,
                    markedVertices);
                AppendJointVertexMarker(
                    triangle.Second,
                    markerSize,
                    markedVertices);
                AppendJointVertexMarker(
                    triangle.Third,
                    markerSize,
                    markedVertices);
            }
        }

        private void AppendJointVertexMarker(
            ReferenceSectionVertex vertex,
            float markerSize,
            ISet<ulong> markedVertices)
        {
            var signature = ((ulong)(uint)vertex.SourceRendererIndex << 32) |
                            (uint)vertex.SourceVertexIndex;
            if (!markedVertices.Add(signature))
            {
                return;
            }

            var point = vertex.Position;
            AppendLine(
                point - Vector3.right * markerSize,
                point + Vector3.right * markerSize);
            AppendLine(
                point - Vector3.up * markerSize,
                point + Vector3.up * markerSize);
            AppendLine(
                point - Vector3.forward * markerSize,
                point + Vector3.forward * markerSize);
        }

        public void SetVisible(bool enabled)
        {
            visible = enabled;
            if (root != null)
            {
                root.SetActive(enabled && lineVertices.Count > 0);
            }
        }

        public void Dispose()
        {
            Destroy(root);
            Destroy(mesh);
            Destroy(material);
        }

        private int CountSupportedSegments()
        {
            var count = 0;
            for (var index = 0; index < SegmentDefinitions.Length; index++)
            {
                if (SegmentDefinitions[index].IsAvailable(bones) &&
                    HasWeightedSource(SegmentDefinitions[index]))
                {
                    count++;
                }
            }

            return count;
        }

        private bool HasWeightedSource(SegmentDefinition segment)
        {
            for (var index = 0; index < sources.Length; index++)
            {
                if (sources[index].SupportsSegment(segment, skinningBones))
                {
                    return true;
                }
            }

            return false;
        }

        private void AppendCandidate(ActualRingCandidate candidate, float markerSize)
        {
            var edgeCount = candidate.IsClosed
                ? candidate.SourceVertices.Count
                : candidate.SourceVertices.Count - 1;
            for (var index = 0; index < edgeCount; index++)
            {
                var next = (index + 1) % candidate.SourceVertices.Count;
                AppendLine(
                    candidate.SourceVertices[index],
                    candidate.SourceVertices[next]);
            }

            for (var index = 0; index < candidate.SourceVertices.Count; index++)
            {
                var point = candidate.SourceVertices[index];
                AppendLine(
                    point - Vector3.right * markerSize,
                    point + Vector3.right * markerSize);
                AppendLine(
                    point - Vector3.up * markerSize,
                    point + Vector3.up * markerSize);
                AppendLine(
                    point - Vector3.forward * markerSize,
                    point + Vector3.forward * markerSize);
            }
        }

        private void AppendLine(Vector3 start, Vector3 end)
        {
            lineVertices.Add(root.transform.InverseTransformPoint(start));
            lineVertices.Add(root.transform.InverseTransformPoint(end));
        }

        private static ActualRingCandidate SelectCandidate(
            IReadOnlyList<ActualRingCandidate> values)
        {
            ActualRingCandidate selected = null;
            var selectedScore = float.PositiveInfinity;
            var hasTopologyVerifiedCandidate = false;
            for (var index = 0; index < values.Count; index++)
            {
                hasTopologyVerifiedCandidate |= values[index].TopologyVerified;
            }

            for (var index = 0; index < values.Count; index++)
            {
                var candidate = values[index];
                if (hasTopologyVerifiedCandidate && !candidate.TopologyVerified)
                {
                    continue;
                }

                var coverage = Mathf.Max(0.05f, candidate.AngularCoverage /
                    (Mathf.PI * 2f));
                var score = candidate.TopologyVerified
                    ? (candidate.AveragePlaneDistance +
                       candidate.AverageRadius * 0.02f) /
                      (coverage * coverage)
                    : (candidate.AverageRadius +
                       candidate.AveragePlaneDistance * 2f) /
                      (coverage * coverage);
                if (!candidate.EnclosesAxis)
                {
                    score *= 2f;
                }
                if (score < selectedScore)
                {
                    selected = candidate;
                    selectedScore = score;
                }
            }

            return selected;
        }

        private static float CalculateNormalizedPosition(
            IReadOnlyList<Vector3> points,
            Vector3 start,
            Vector3 axis,
            float length)
        {
            var distance = 0f;
            for (var index = 0; index < points.Count; index++)
            {
                distance += Vector3.Dot(points[index] - start, axis);
            }

            return points.Count == 0
                ? 0f
                : Mathf.Clamp01(distance / points.Count / length);
        }

        private static void BuildPlaneBasis(
            Vector3 normal,
            out Vector3 axisX,
            out Vector3 axisY)
        {
            var reference = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.9f
                ? Vector3.right
                : Vector3.up;
            axisX = Vector3.Cross(normal, reference).normalized;
            axisY = Vector3.Cross(normal, axisX).normalized;
        }

        private static bool ContainsOrigin(IReadOnlyList<Vector2> polygon)
        {
            var inside = false;
            var previous = polygon.Count - 1;
            for (var index = 0; index < polygon.Count; index++)
            {
                var currentPoint = polygon[index];
                var previousPoint = polygon[previous];
                if ((currentPoint.y > 0f) != (previousPoint.y > 0f) &&
                    0f < (previousPoint.x - currentPoint.x) *
                    (-currentPoint.y) /
                    (previousPoint.y - currentPoint.y) + currentPoint.x)
                {
                    inside = !inside;
                }

                previous = index;
            }

            return inside;
        }

        private readonly struct SegmentDefinition
        {
            public SegmentDefinition(
                ReferenceBodySegment segment,
                HumanBodyBones start,
                HumanBodyBones end,
                HumanBodyBones? excludedWhenPresent = null)
            {
                Segment = segment;
                Start = start;
                End = end;
                ExcludedWhenPresent = excludedWhenPresent;
            }

            public ReferenceBodySegment Segment { get; }
            public HumanBodyBones Start { get; }
            public HumanBodyBones End { get; }
            public HumanBodyBones? ExcludedWhenPresent { get; }
            public bool UsesTransverseTopology =>
                Segment == ReferenceBodySegment.LeftUpperArm ||
                Segment == ReferenceBodySegment.LeftForearm ||
                Segment == ReferenceBodySegment.RightUpperArm ||
                Segment == ReferenceBodySegment.RightForearm ||
                Segment == ReferenceBodySegment.LeftThigh ||
                Segment == ReferenceBodySegment.LeftCalf ||
                Segment == ReferenceBodySegment.RightThigh ||
                Segment == ReferenceBodySegment.RightCalf;

            public bool IsAvailable(
                IReadOnlyDictionary<HumanBodyBones, Transform> semanticBones)
            {
                return semanticBones.ContainsKey(Start) &&
                       semanticBones.ContainsKey(End) &&
                       (!ExcludedWhenPresent.HasValue ||
                        !semanticBones.ContainsKey(ExcludedWhenPresent.Value));
            }
        }

        private readonly struct JointDefinition
        {
            public JointDefinition(
                ReferenceJointType jointType,
                HumanBodyBones parent,
                HumanBodyBones joint,
                HumanBodyBones child,
                ReferenceBodySegment segment)
            {
                JointType = jointType;
                Parent = parent;
                Joint = joint;
                Child = child;
                Segment = new SegmentDefinition(segment, parent, joint);
            }

            public ReferenceJointType JointType { get; }
            public HumanBodyBones Parent { get; }
            public HumanBodyBones Joint { get; }
            public HumanBodyBones Child { get; }
            public SegmentDefinition Segment { get; }

            public bool IsAvailable(
                IReadOnlyDictionary<HumanBodyBones, Transform> semanticBones)
            {
                return semanticBones.ContainsKey(Parent) &&
                       semanticBones.ContainsKey(Joint) &&
                       semanticBones.ContainsKey(Child);
            }
        }

        private sealed class ActualRingCandidate
        {
            public ActualRingCandidate(
                ulong signature,
                IReadOnlyList<ReferenceSectionVertex> sourceSamples,
                float averageRadius,
                float averagePlaneDistance,
                float angularCoverage,
                bool enclosesAxis,
                bool topologyVerified,
                bool isClosed)
            {
                Signature = signature;
                SourceSamples = sourceSamples;
                var vertices = new Vector3[sourceSamples.Count];
                for (var index = 0; index < sourceSamples.Count; index++)
                {
                    vertices[index] = sourceSamples[index].Position;
                }

                SourceVertices = Array.AsReadOnly(vertices);
                AverageRadius = averageRadius;
                AveragePlaneDistance = averagePlaneDistance;
                AngularCoverage = angularCoverage;
                EnclosesAxis = enclosesAxis;
                TopologyVerified = topologyVerified;
                IsClosed = isClosed;
            }

            public ulong Signature { get; }
            public IReadOnlyList<ReferenceSectionVertex> SourceSamples { get; }
            public IReadOnlyList<Vector3> SourceVertices { get; }
            public float AverageRadius { get; }
            public float AveragePlaneDistance { get; }
            public float AngularCoverage { get; }
            public bool EnclosesAxis { get; }
            public bool TopologyVerified { get; }
            public bool IsClosed { get; }
        }

        private readonly struct AngularVertex
        {
            public AngularVertex(int index, Vector3 point, Vector2 projected)
            {
                Index = index;
                Point = point;
                Projected = projected;
                Angle = Mathf.Atan2(projected.y, projected.x);
            }

            public int Index { get; }
            public Vector3 Point { get; }
            public Vector2 Projected { get; }
            public float Angle { get; }
        }

        private sealed class SliceSource
        {
            private const float MinimumBoneInfluence = 0.05f;
            private const float PositionWeldScale = 100000f;
            private const float MaximumTransverseAxisRatio = 0.45f;

            private readonly Vector3[] vertices;
            private readonly int[] triangles;
            private readonly List<int>[] vertexNeighbors;
            private readonly Dictionary<Vector3Int, List<int>>
                vertexIndicesByPosition;
            private readonly BoneWeight[] boneWeights;
            private readonly string[] boneNames;
            private readonly HumanBodyBones?[] humanoidBones;
            private readonly Dictionary<Transform, int> boneIndices =
                new Dictionary<Transform, int>();
            private readonly HashSet<int> activeBoneIndices = new HashSet<int>();
            private readonly HashSet<int> selectedVertices = new HashSet<int>();
            private readonly HashSet<int> tracedVertices = new HashSet<int>();
            private readonly Queue<int> traversal = new Queue<int>();
            private readonly List<int> component = new List<int>();

            public SliceSource(
                Renderer renderer,
                IReadOnlyDictionary<Transform, ReferenceModelBone> boneMetadata)
            {
                var sourceMesh = renderer is SkinnedMeshRenderer skinned
                    ? skinned.sharedMesh
                    : renderer.GetComponent<MeshFilter>().sharedMesh;
                var sourceVertices = sourceMesh.vertices;
                vertices = new Vector3[sourceVertices.Length];
                var localToWorld = renderer.transform.localToWorldMatrix;
                for (var index = 0; index < sourceVertices.Length; index++)
                {
                    vertices[index] = localToWorld.MultiplyPoint3x4(
                        sourceVertices[index]);
                }

                var triangleList = new List<int>();
                for (var subMeshIndex = 0;
                     subMeshIndex < sourceMesh.subMeshCount;
                     subMeshIndex++)
                {
                    if (sourceMesh.GetTopology(subMeshIndex) ==
                        MeshTopology.Triangles)
                    {
                        triangleList.AddRange(
                            sourceMesh.GetIndices(subMeshIndex, true));
                    }
                }

                triangles = triangleList.ToArray();
                vertexNeighbors = BuildVertexNeighbors(vertices, triangles);
                vertexIndicesByPosition = BuildPositionIndex(vertices);
                boneWeights = sourceMesh.boneWeights;

                if (renderer is SkinnedMeshRenderer skinnedRenderer)
                {
                    var rendererBones = skinnedRenderer.bones;
                    boneNames = new string[rendererBones.Length];
                    humanoidBones = new HumanBodyBones?[rendererBones.Length];
                    for (var index = 0; index < rendererBones.Length; index++)
                    {
                        var rendererBone = rendererBones[index];
                        if (rendererBone != null &&
                            !boneIndices.ContainsKey(rendererBone))
                        {
                            boneIndices.Add(rendererBone, index);
                        }

                        if (rendererBone != null &&
                            boneMetadata.TryGetValue(rendererBone, out var metadata))
                        {
                            boneNames[index] = metadata.Name;
                            humanoidBones[index] = metadata.HumanoidBone;
                        }
                        else
                        {
                            boneNames[index] = rendererBone != null
                                ? rendererBone.name
                                : string.Empty;
                        }
                    }
                }
                else
                {
                    boneNames = Array.Empty<string>();
                    humanoidBones = Array.Empty<HumanBodyBones?>();
                }
            }

            public ReferenceJointPatch BuildExpandedJointPatch(
                int sourceRendererIndex,
                ReferenceJointType joint,
                Vector3 center,
                Vector3 axis,
                Plane plane,
                SegmentDefinition segment,
                IReadOnlyDictionary<HumanBodyBones, List<Transform>>
                    semanticBones,
                float epsilon,
                int expansionLayers)
            {
                activeBoneIndices.Clear();
                AddBoneIndices(semanticBones, segment.Start, activeBoneIndices);
                AddBoneIndices(semanticBones, segment.End, activeBoneIndices);
                if (boneWeights.Length != vertices.Length ||
                    activeBoneIndices.Count == 0)
                {
                    return null;
                }

                selectedVertices.Clear();
                for (var index = 0; index + 2 < triangles.Length; index += 3)
                {
                    var first = triangles[index];
                    var second = triangles[index + 1];
                    var third = triangles[index + 2];
                    if (!IsValidVertex(first) ||
                        !IsValidVertex(second) ||
                        !IsValidVertex(third))
                    {
                        continue;
                    }

                    var firstDistance = plane.GetDistanceToPoint(vertices[first]);
                    var secondDistance = plane.GetDistanceToPoint(vertices[second]);
                    var thirdDistance = plane.GetDistanceToPoint(vertices[third]);
                    var minimum = Mathf.Min(
                        firstDistance,
                        Mathf.Min(secondDistance, thirdDistance));
                    var maximum = Mathf.Max(
                        firstDistance,
                        Mathf.Max(secondDistance, thirdDistance));
                    if (minimum > epsilon || maximum < -epsilon)
                    {
                        continue;
                    }

                    var closest = SelectClosestInfluencedVertex(
                        first,
                        firstDistance,
                        second,
                        secondDistance,
                        third,
                        thirdDistance);
                    if (closest >= 0)
                    {
                        selectedVertices.Add(closest);
                    }
                }

                if (selectedVertices.Count < 3)
                {
                    return null;
                }

                var centerSamples = new List<ReferenceSectionVertex>();
                var uniquePositions = new HashSet<Vector3Int>();
                foreach (var vertexIndex in selectedVertices)
                {
                    if (uniquePositions.Add(ToPositionKey(vertices[vertexIndex])))
                    {
                        centerSamples.Add(BuildSourceSample(
                            sourceRendererIndex,
                            vertexIndex,
                            vertices[vertexIndex]));
                    }
                }

                if (centerSamples.Count < 3)
                {
                    return null;
                }

                var includedTriangles = new HashSet<int>();
                var patchVertices = new HashSet<int>(selectedVertices);
                var frontier = new HashSet<int>(selectedVertices);
                expansionLayers = Mathf.Clamp(expansionLayers, 0, 8);
                for (var layer = 0; layer <= expansionLayers; layer++)
                {
                    var nextFrontier = new HashSet<int>();
                    for (var index = 0;
                         index + 2 < triangles.Length;
                         index += 3)
                    {
                        if (includedTriangles.Contains(index))
                        {
                            continue;
                        }

                        var first = triangles[index];
                        var second = triangles[index + 1];
                        var third = triangles[index + 2];
                        if (!frontier.Contains(first) &&
                            !frontier.Contains(second) &&
                            !frontier.Contains(third))
                        {
                            continue;
                        }

                        includedTriangles.Add(index);
                        AddNewPatchVertex(first, patchVertices, nextFrontier);
                        AddNewPatchVertex(second, patchVertices, nextFrontier);
                        AddNewPatchVertex(third, patchVertices, nextFrontier);
                    }

                    frontier = nextFrontier;
                    if (frontier.Count == 0)
                    {
                        break;
                    }
                }

                var patchTriangles = new List<ReferenceJointTriangle>(
                    includedTriangles.Count);
                foreach (var triangleIndex in includedTriangles)
                {
                    var first = triangles[triangleIndex];
                    var second = triangles[triangleIndex + 1];
                    var third = triangles[triangleIndex + 2];
                    patchTriangles.Add(new ReferenceJointTriangle(
                        BuildSourceSample(
                            sourceRendererIndex,
                            first,
                            vertices[first]),
                        BuildSourceSample(
                            sourceRendererIndex,
                            second,
                            vertices[second]),
                        BuildSourceSample(
                            sourceRendererIndex,
                            third,
                            vertices[third])));
                }

                return patchTriangles.Count == 0
                    ? null
                    : new ReferenceJointPatch(
                        joint,
                        center,
                        axis,
                        false,
                        centerSamples,
                        Array.Empty<ReferenceJointConnection>(),
                        patchTriangles);
            }

            private static void AddNewPatchVertex(
                int vertexIndex,
                ISet<int> patchVertices,
                ISet<int> frontier)
            {
                if (patchVertices.Add(vertexIndex))
                {
                    frontier.Add(vertexIndex);
                }
            }

            public ReferenceJointPatch BuildJointPatch(
                ReferenceJointType joint,
                Vector3 center,
                Vector3 axis,
                ActualRingCandidate centerRing)
            {
                if (centerRing == null || centerRing.SourceSamples.Count < 3)
                {
                    return null;
                }

                var ringIndices = new HashSet<int>();
                for (var index = 0;
                     index < centerRing.SourceSamples.Count;
                     index++)
                {
                    var source = centerRing.SourceSamples[index];
                    var key = ToPositionKey(source.Position);
                    if (!vertexIndicesByPosition.TryGetValue(
                            key,
                            out var coincident))
                    {
                        continue;
                    }

                    for (var coincidentIndex = 0;
                         coincidentIndex < coincident.Count;
                         coincidentIndex++)
                    {
                        ringIndices.Add(coincident[coincidentIndex]);
                    }
                }

                var connections = new List<ReferenceJointConnection>();
                var patchTriangles = new List<ReferenceJointTriangle>();
                var connectionEdges = new HashSet<ulong>();
                for (var index = 0; index + 2 < triangles.Length; index += 3)
                {
                    var first = triangles[index];
                    var second = triangles[index + 1];
                    var third = triangles[index + 2];
                    if (!IsValidVertex(first) ||
                        !IsValidVertex(second) ||
                        !IsValidVertex(third) ||
                        (!ringIndices.Contains(first) &&
                         !ringIndices.Contains(second) &&
                         !ringIndices.Contains(third)))
                    {
                        continue;
                    }

                    var sourceRendererIndex =
                        centerRing.SourceSamples[0].SourceRendererIndex;
                    var firstSample = BuildSourceSample(
                        sourceRendererIndex,
                        first,
                        vertices[first]);
                    var secondSample = BuildSourceSample(
                        sourceRendererIndex,
                        second,
                        vertices[second]);
                    var thirdSample = BuildSourceSample(
                        sourceRendererIndex,
                        third,
                        vertices[third]);
                    patchTriangles.Add(new ReferenceJointTriangle(
                        firstSample,
                        secondSample,
                        thirdSample));
                    AppendJointConnection(
                        first,
                        second,
                        ringIndices,
                        center,
                        axis,
                        sourceRendererIndex,
                        connectionEdges,
                        connections);
                    AppendJointConnection(
                        second,
                        third,
                        ringIndices,
                        center,
                        axis,
                        sourceRendererIndex,
                        connectionEdges,
                        connections);
                    AppendJointConnection(
                        third,
                        first,
                        ringIndices,
                        center,
                        axis,
                        sourceRendererIndex,
                        connectionEdges,
                        connections);
                }

                return patchTriangles.Count == 0
                    ? null
                    : new ReferenceJointPatch(
                        joint,
                        center,
                        axis,
                        centerRing.IsClosed,
                        centerRing.SourceSamples,
                        connections,
                        patchTriangles);
            }

            private void AppendJointConnection(
                int first,
                int second,
                ISet<int> ringIndices,
                Vector3 center,
                Vector3 axis,
                int sourceRendererIndex,
                ISet<ulong> renderedEdges,
                ICollection<ReferenceJointConnection> result)
            {
                var firstIsRing = ringIndices.Contains(first);
                var secondIsRing = ringIndices.Contains(second);
                if (firstIsRing == secondIsRing)
                {
                    return;
                }

                var jointIndex = firstIsRing ? first : second;
                var adjacentIndex = firstIsRing ? second : first;
                var edge = vertices[adjacentIndex] - vertices[jointIndex];
                var length = edge.magnitude;
                if (length <= 0.0000001f ||
                    Mathf.Abs(Vector3.Dot(edge / length, axis)) < 0.2f)
                {
                    return;
                }

                var minimum = (uint)Mathf.Min(jointIndex, adjacentIndex);
                var maximum = (uint)Mathf.Max(jointIndex, adjacentIndex);
                var signature = ((ulong)minimum << 32) | maximum;
                if (!renderedEdges.Add(signature))
                {
                    return;
                }

                var jointVertex = BuildSourceSample(
                    sourceRendererIndex,
                    jointIndex,
                    vertices[jointIndex]);
                var adjacentVertex = BuildSourceSample(
                    sourceRendererIndex,
                    adjacentIndex,
                    vertices[adjacentIndex]);
                result.Add(new ReferenceJointConnection(
                    jointVertex,
                    adjacentVertex,
                    Vector3.Dot(adjacentVertex.Position - center, axis) > 0f));
            }

            public bool SupportsSegment(
                SegmentDefinition segment,
                IReadOnlyDictionary<HumanBodyBones, List<Transform>>
                    semanticBones)
            {
                if (boneWeights.Length != vertices.Length)
                {
                    return false;
                }

                return HasBoneIndex(semanticBones, segment.Start) ||
                       HasBoneIndex(semanticBones, segment.End);
            }

            public void AppendCandidates(
                int sourceIndex,
                Plane plane,
                SegmentDefinition segment,
                IReadOnlyDictionary<HumanBodyBones, List<Transform>>
                    semanticBones,
                Vector3 center,
                Vector3 axis,
                float epsilon,
                ICollection<ActualRingCandidate> result,
                bool requireTransverseTopology = true)
            {
                activeBoneIndices.Clear();
                AddBoneIndices(semanticBones, segment.Start, activeBoneIndices);
                AddBoneIndices(semanticBones, segment.End, activeBoneIndices);
                if (boneWeights.Length != vertices.Length ||
                    activeBoneIndices.Count == 0)
                {
                    return;
                }

                selectedVertices.Clear();
                for (var index = 0; index + 2 < triangles.Length; index += 3)
                {
                    var first = triangles[index];
                    var second = triangles[index + 1];
                    var third = triangles[index + 2];
                    if (!IsValidVertex(first) ||
                        !IsValidVertex(second) ||
                        !IsValidVertex(third))
                    {
                        continue;
                    }

                    var firstDistance = plane.GetDistanceToPoint(vertices[first]);
                    var secondDistance = plane.GetDistanceToPoint(vertices[second]);
                    var thirdDistance = plane.GetDistanceToPoint(vertices[third]);
                    var minimum = Mathf.Min(
                        firstDistance,
                        Mathf.Min(secondDistance, thirdDistance));
                    var maximum = Mathf.Max(
                        firstDistance,
                        Mathf.Max(secondDistance, thirdDistance));
                    if (minimum > epsilon || maximum < -epsilon)
                    {
                        continue;
                    }

                    var closest = SelectClosestInfluencedVertex(
                        first,
                        firstDistance,
                        second,
                        secondDistance,
                        third,
                        thirdDistance);
                    if (closest >= 0)
                    {
                        selectedVertices.Add(closest);
                    }
                }

                if (segment.UsesTransverseTopology &&
                    requireTransverseTopology)
                {
                    AppendLimbLoopCandidates(
                        sourceIndex,
                        center,
                        axis,
                        plane,
                        result);
                    return;
                }

                if (selectedVertices.Count < 4)
                {
                    return;
                }

                component.Clear();
                foreach (var selected in selectedVertices)
                {
                    component.Add(selected);
                }

                var candidate = BuildCandidate(
                    sourceIndex,
                    component,
                    center,
                    axis,
                    plane,
                    false,
                    true);
                if (candidate != null)
                {
                    result.Add(candidate);
                }
            }

            private void AppendLimbLoopCandidates(
                int sourceIndex,
                Vector3 center,
                Vector3 axis,
                Plane plane,
                ICollection<ActualRingCandidate> result)
            {
                tracedVertices.Clear();
                foreach (var seed in selectedVertices)
                {
                    if (!tracedVertices.Add(seed))
                    {
                        continue;
                    }

                    component.Clear();
                    traversal.Clear();
                    traversal.Enqueue(seed);
                    while (traversal.Count > 0)
                    {
                        var current = traversal.Dequeue();
                        component.Add(current);
                        var neighbors = vertexNeighbors[current];
                        for (var index = 0; index < neighbors.Count; index++)
                        {
                            var neighbor = neighbors[index];
                            if (!IsTransverseEdge(current, neighbor, axis) ||
                                !tracedVertices.Add(neighbor))
                            {
                                continue;
                            }

                            traversal.Enqueue(neighbor);
                        }
                    }

                    if (!IsCoherentTransverseChain(
                            component,
                            axis,
                            out var isClosed))
                    {
                        continue;
                    }

                    var candidate = BuildCandidate(
                        sourceIndex,
                        component,
                        center,
                        axis,
                        plane,
                        true,
                        isClosed);
                    if (candidate != null)
                    {
                        result.Add(candidate);
                    }
                }
            }

            private ActualRingCandidate BuildCandidate(
                int sourceIndex,
                IReadOnlyList<int> indices,
                Vector3 center,
                Vector3 axis,
                Plane plane,
                bool topologyVerified,
                bool isClosed)
            {
                if (indices.Count < 4)
                {
                    return null;
                }

                BuildPlaneBasis(axis, out var axisX, out var axisY);
                var angularVertices = new List<AngularVertex>(indices.Count);
                var uniquePositions = new HashSet<Vector3Int>();
                for (var index = 0; index < indices.Count; index++)
                {
                    var vertexIndex = indices[index];
                    var point = vertices[vertexIndex];
                    if (!uniquePositions.Add(ToPositionKey(point)))
                    {
                        continue;
                    }

                    var offset = point - center;
                    var projected = new Vector2(
                        Vector3.Dot(offset, axisX),
                        Vector3.Dot(offset, axisY));
                    if (projected.sqrMagnitude > 0.0000000001f)
                    {
                        angularVertices.Add(new AngularVertex(
                            vertexIndex,
                            point,
                            projected));
                    }
                }

                if (angularVertices.Count < 4)
                {
                    return null;
                }

                angularVertices.Sort((first, second) =>
                    first.Angle.CompareTo(second.Angle));
                var maximumGap = angularVertices[0].Angle + Mathf.PI * 2f -
                                 angularVertices[angularVertices.Count - 1].Angle;
                var maximumGapStart = angularVertices.Count - 1;
                for (var index = 1; index < angularVertices.Count; index++)
                {
                    var gap = angularVertices[index].Angle -
                              angularVertices[index - 1].Angle;
                    if (gap > maximumGap)
                    {
                        maximumGap = gap;
                        maximumGapStart = index - 1;
                    }
                }

                var angularCoverage = Mathf.PI * 2f - maximumGap;
                if ((isClosed && maximumGap > Mathf.PI * 0.8f) ||
                    (!isClosed && angularCoverage < Mathf.PI * 0.5f))
                {
                    return null;
                }

                var projectedPolygon = new Vector2[angularVertices.Count];
                for (var index = 0; index < angularVertices.Count; index++)
                {
                    projectedPolygon[index] = angularVertices[index].Projected;
                }

                var enclosesAxis = isClosed && ContainsOrigin(projectedPolygon);
                if (isClosed && !enclosesAxis)
                {
                    return null;
                }

                var sourceSamples = new ReferenceSectionVertex[
                    angularVertices.Count];
                var signatureIndices = new int[angularVertices.Count];
                var averageRadius = 0f;
                var averagePlaneDistance = 0f;
                var firstAngularIndex = isClosed
                    ? 0
                    : (maximumGapStart + 1) % angularVertices.Count;
                for (var index = 0; index < angularVertices.Count; index++)
                {
                    var angular = angularVertices[
                        (firstAngularIndex + index) % angularVertices.Count];
                    var point = angular.Point;
                    sourceSamples[index] = BuildSourceSample(
                        sourceIndex,
                        angular.Index,
                        point);
                    signatureIndices[index] = angular.Index;
                    averageRadius += angular.Projected.magnitude;
                    averagePlaneDistance += Mathf.Abs(
                        plane.GetDistanceToPoint(point));
                }

                averageRadius /= angularVertices.Count;
                averagePlaneDistance /= angularVertices.Count;

                Array.Sort(signatureIndices);
                var signature = 1469598103934665603UL;
                signature = (signature ^ (uint)(sourceIndex + 1)) *
                            1099511628211UL;
                for (var index = 0; index < signatureIndices.Length; index++)
                {
                    signature = (signature ^ (uint)signatureIndices[index]) *
                                1099511628211UL;
                }

                return new ActualRingCandidate(
                    signature,
                    Array.AsReadOnly(sourceSamples),
                    averageRadius,
                    averagePlaneDistance,
                    angularCoverage,
                    enclosesAxis,
                    topologyVerified,
                    isClosed);
            }

            private bool IsCoherentTransverseChain(
                IReadOnlyList<int> indices,
                Vector3 axis,
                out bool isClosed)
            {
                isClosed = false;
                if (indices.Count < 4)
                {
                    return false;
                }

                var members = new HashSet<int>(indices);
                var positionGraph =
                    new Dictionary<Vector3Int, HashSet<Vector3Int>>();
                for (var index = 0; index < indices.Count; index++)
                {
                    var vertexIndex = indices[index];
                    var key = ToPositionKey(vertices[vertexIndex]);
                    if (!positionGraph.ContainsKey(key))
                    {
                        positionGraph.Add(key, new HashSet<Vector3Int>());
                    }

                    var neighbors = vertexNeighbors[vertexIndex];
                    for (var neighborIndex = 0;
                         neighborIndex < neighbors.Count;
                         neighborIndex++)
                    {
                        var neighbor = neighbors[neighborIndex];
                        if (!members.Contains(neighbor) ||
                            !IsTransverseEdge(vertexIndex, neighbor, axis))
                        {
                            continue;
                        }

                        var neighborKey = ToPositionKey(vertices[neighbor]);
                        if (neighborKey != key)
                        {
                            positionGraph[key].Add(neighborKey);
                        }
                    }
                }

                if (positionGraph.Count < 4)
                {
                    return false;
                }

                var endpointCount = 0;
                foreach (var pair in positionGraph)
                {
                    if (pair.Value.Count == 0)
                    {
                        return false;
                    }

                    if (pair.Value.Count == 1)
                    {
                        endpointCount++;
                    }
                    else if (pair.Value.Count != 2)
                    {
                        return false;
                    }
                }

                if (endpointCount != 0 && endpointCount != 2)
                {
                    return false;
                }

                isClosed = endpointCount == 0;
                return true;
            }

            private bool IsTransverseEdge(int first, int second, Vector3 axis)
            {
                var edge = vertices[second] - vertices[first];
                var length = edge.magnitude;
                return length <= 0.0000001f ||
                       Mathf.Abs(Vector3.Dot(edge / length, axis)) <=
                       MaximumTransverseAxisRatio;
            }

            private static List<int>[] BuildVertexNeighbors(
                IReadOnlyList<Vector3> sourceVertices,
                IReadOnlyList<int> sourceTriangles)
            {
                var result = new List<int>[sourceVertices.Count];
                for (var index = 0; index < result.Length; index++)
                {
                    result[index] = new List<int>(6);
                }

                for (var index = 0;
                     index + 2 < sourceTriangles.Count;
                     index += 3)
                {
                    AddUndirectedEdge(
                        result,
                        sourceTriangles[index],
                        sourceTriangles[index + 1]);
                    AddUndirectedEdge(
                        result,
                        sourceTriangles[index + 1],
                        sourceTriangles[index + 2]);
                    AddUndirectedEdge(
                        result,
                        sourceTriangles[index + 2],
                        sourceTriangles[index]);
                }

                var firstByPosition = new Dictionary<Vector3Int, int>();
                for (var index = 0; index < sourceVertices.Count; index++)
                {
                    var key = ToPositionKey(sourceVertices[index]);
                    if (firstByPosition.TryGetValue(key, out var first))
                    {
                        AddUndirectedEdge(result, first, index);
                    }
                    else
                    {
                        firstByPosition.Add(key, index);
                    }
                }

                return result;
            }

            private static Dictionary<Vector3Int, List<int>> BuildPositionIndex(
                IReadOnlyList<Vector3> sourceVertices)
            {
                var result = new Dictionary<Vector3Int, List<int>>();
                for (var index = 0; index < sourceVertices.Count; index++)
                {
                    var key = ToPositionKey(sourceVertices[index]);
                    if (!result.TryGetValue(key, out var indices))
                    {
                        indices = new List<int>();
                        result.Add(key, indices);
                    }

                    indices.Add(index);
                }

                return result;
            }

            private static void AddUndirectedEdge(
                IReadOnlyList<List<int>> neighbors,
                int first,
                int second)
            {
                if (first < 0 ||
                    second < 0 ||
                    first >= neighbors.Count ||
                    second >= neighbors.Count ||
                    first == second)
                {
                    return;
                }

                if (!neighbors[first].Contains(second))
                {
                    neighbors[first].Add(second);
                }

                if (!neighbors[second].Contains(first))
                {
                    neighbors[second].Add(first);
                }
            }

            private ReferenceSectionVertex BuildSourceSample(
                int sourceRendererIndex,
                int sourceVertexIndex,
                Vector3 position)
            {
                var values = new List<ReferenceSectionBoneInfluence>(4);
                var weight = boneWeights[sourceVertexIndex];
                AddInfluence(values, weight.boneIndex0, weight.weight0);
                AddInfluence(values, weight.boneIndex1, weight.weight1);
                AddInfluence(values, weight.boneIndex2, weight.weight2);
                AddInfluence(values, weight.boneIndex3, weight.weight3);
                values.Sort((first, second) =>
                    second.Weight.CompareTo(first.Weight));
                return new ReferenceSectionVertex(
                    position,
                    sourceRendererIndex,
                    sourceVertexIndex,
                    values);
            }

            private void AddInfluence(
                ICollection<ReferenceSectionBoneInfluence> result,
                int boneIndex,
                float weight)
            {
                if (weight <= 0f ||
                    boneIndex < 0 ||
                    boneIndex >= boneNames.Length)
                {
                    return;
                }

                result.Add(new ReferenceSectionBoneInfluence(
                    boneNames[boneIndex],
                    humanoidBones[boneIndex],
                    weight));
            }

            private int SelectClosestInfluencedVertex(
                int first,
                float firstDistance,
                int second,
                float secondDistance,
                int third,
                float thirdDistance)
            {
                var selected = -1;
                var selectedDistance = float.PositiveInfinity;
                TrySelectVertex(
                    first,
                    firstDistance,
                    ref selected,
                    ref selectedDistance);
                TrySelectVertex(
                    second,
                    secondDistance,
                    ref selected,
                    ref selectedDistance);
                TrySelectVertex(
                    third,
                    thirdDistance,
                    ref selected,
                    ref selectedDistance);
                return selected;
            }

            private void TrySelectVertex(
                int vertexIndex,
                float planeDistance,
                ref int selected,
                ref float selectedDistance)
            {
                if (GetInfluence(
                        boneWeights[vertexIndex],
                        activeBoneIndices) <= MinimumBoneInfluence)
                {
                    return;
                }

                var distance = Mathf.Abs(planeDistance);
                if (distance < selectedDistance)
                {
                    selected = vertexIndex;
                    selectedDistance = distance;
                }
            }

            private bool HasBoneIndex(
                IReadOnlyDictionary<HumanBodyBones, List<Transform>>
                    semanticBones,
                HumanBodyBones bone)
            {
                if (!semanticBones.TryGetValue(bone, out var transforms))
                {
                    return false;
                }

                for (var index = 0; index < transforms.Count; index++)
                {
                    if (boneIndices.ContainsKey(transforms[index]))
                    {
                        return true;
                    }
                }

                return false;
            }

            private void AddBoneIndices(
                IReadOnlyDictionary<HumanBodyBones, List<Transform>>
                    semanticBones,
                HumanBodyBones bone,
                ISet<int> result)
            {
                if (!semanticBones.TryGetValue(bone, out var transforms))
                {
                    return;
                }

                for (var index = 0; index < transforms.Count; index++)
                {
                    if (boneIndices.TryGetValue(
                            transforms[index],
                            out var boneIndex))
                    {
                        result.Add(boneIndex);
                    }
                }
            }

            private bool IsValidVertex(int index)
            {
                return index >= 0 && index < vertices.Length;
            }

            private static float GetInfluence(
                BoneWeight weight,
                ISet<int> targetBones)
            {
                var result = 0f;
                if (targetBones.Contains(weight.boneIndex0))
                    result += weight.weight0;
                if (targetBones.Contains(weight.boneIndex1))
                    result += weight.weight1;
                if (targetBones.Contains(weight.boneIndex2))
                    result += weight.weight2;
                if (targetBones.Contains(weight.boneIndex3))
                    result += weight.weight3;
                return result;
            }

            private static Vector3Int ToPositionKey(Vector3 point)
            {
                return new Vector3Int(
                    Mathf.RoundToInt(point.x * PositionWeldScale),
                    Mathf.RoundToInt(point.y * PositionWeldScale),
                    Mathf.RoundToInt(point.z * PositionWeldScale));
            }
        }
    }
}
