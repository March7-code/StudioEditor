using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using static BodyEditor.ReferenceModels.ReferenceModelOverlayUtilities;

namespace BodyEditor.ReferenceModels
{
    public enum BodySectionRecoveryStrategy
    {
        Tubular,
        TorsoContour,
        FeaturePreserving,
    }

    public enum BodySectionSampleProvenance
    {
        Observed,
        GapInserted,
        OutlierRepaired,
        BilateralMirrored,
    }

    public readonly struct BodySectionFieldSample
    {
        public BodySectionFieldSample(
            Vector3 position,
            float radius,
            float confidence,
            BodySectionSampleProvenance provenance,
            ReferenceSectionVertex sourceVertex = null)
        {
            Position = position;
            Radius = Mathf.Max(0f, radius);
            Confidence = Mathf.Clamp01(confidence);
            Provenance = provenance;
            SourceVertex = sourceVertex;
        }

        public Vector3 Position { get; }
        public float Radius { get; }
        public float Confidence { get; }
        public BodySectionSampleProvenance Provenance { get; }
        public ReferenceSectionVertex SourceVertex { get; }
        public bool IsObserved => Provenance == BodySectionSampleProvenance.Observed;
    }

    public sealed class BodySectionFieldRing
    {
        internal BodySectionFieldRing(
            ReferenceBodySegment segment,
            BodySectionRecoveryStrategy strategy,
            float normalizedPosition,
            Vector3 center,
            Vector3 axis,
            bool isClosed,
            IReadOnlyList<BodySectionFieldSample> samples)
        {
            Segment = segment;
            Strategy = strategy;
            NormalizedPosition = Mathf.Clamp01(normalizedPosition);
            Center = center;
            Axis = axis.normalized;
            IsClosed = isClosed;

            var values = new BodySectionFieldSample[samples.Count];
            for (var index = 0; index < samples.Count; index++)
            {
                values[index] = samples[index];
            }

            Samples = Array.AsReadOnly(values);
        }

        public ReferenceBodySegment Segment { get; }
        public BodySectionRecoveryStrategy Strategy { get; }
        public float NormalizedPosition { get; }
        public Vector3 Center { get; }
        public Vector3 Axis { get; }
        public bool IsClosed { get; }
        public IReadOnlyList<BodySectionFieldSample> Samples { get; }
    }

    public sealed class BodySectionField
    {
        private static readonly BodySectionField empty =
            new BodySectionField(Array.Empty<BodySectionFieldRing>());

        internal BodySectionField(IReadOnlyList<BodySectionFieldRing> rings)
        {
            var values = new BodySectionFieldRing[rings.Count];
            for (var index = 0; index < rings.Count; index++)
            {
                values[index] = rings[index];
            }

            Rings = Array.AsReadOnly(values);
        }

        public static BodySectionField Empty => empty;
        public IReadOnlyList<BodySectionFieldRing> Rings { get; }
        public bool HasData => Rings.Count > 0;
    }

    public static class BodySectionFieldBuilder
    {
        private const float TubularGapFactor = 1.75f;
        private const float TorsoGapFactor = 2.5f;
        private const int MaximumInsertionsPerGap = 8;
        private const float MaximumSymmetryPositionDelta = 0.04f;
        private const float ClothingRadiusRatio = 1.12f;

        public static BodySectionField Build(
            IReadOnlyList<ReferenceSectionRing> sourceRings,
            IReadOnlyList<ReferenceJointPatch> jointPatches = null)
        {
            if (sourceRings == null)
            {
                throw new ArgumentNullException(nameof(sourceRings));
            }

            var eligibleRings = new List<ReferenceSectionRing>(sourceRings.Count);
            for (var index = 0; index < sourceRings.Count; index++)
            {
                if (!IsCoveredByJointPatch(sourceRings[index], jointPatches))
                {
                    eligibleRings.Add(sourceRings[index]);
                }
            }

            var result = new List<BodySectionFieldRing>(eligibleRings.Count);
            var recoveredBySource =
                new Dictionary<ReferenceSectionRing, BodySectionFieldRing>();
            for (var index = 0; index < eligibleRings.Count; index++)
            {
                var recovered = RecoverRing(eligibleRings[index]);
                if (recovered != null)
                {
                    result.Add(recovered);
                    recoveredBySource.Add(eligibleRings[index], recovered);
                }
            }

            ApplyBilateralRecovery(
                eligibleRings,
                recoveredBySource,
                result);

            result.Sort((first, second) =>
            {
                var segmentComparison = first.Segment.CompareTo(second.Segment);
                return segmentComparison != 0
                    ? segmentComparison
                    : first.NormalizedPosition.CompareTo(second.NormalizedPosition);
            });
            return new BodySectionField(result);
        }

        private static bool IsCoveredByJointPatch(
            ReferenceSectionRing ring,
            IReadOnlyList<ReferenceJointPatch> jointPatches)
        {
            if (jointPatches == null)
            {
                return false;
            }

            for (var patchIndex = 0;
                 patchIndex < jointPatches.Count;
                 patchIndex++)
            {
                var patch = jointPatches[patchIndex];
                if (!IsAdjacentSegment(ring.Segment, patch.Joint))
                {
                    continue;
                }

                var extent = CalculateJointPatchAxialExtent(patch);
                var segmentLength = Vector3.Distance(
                    ring.SegmentStart,
                    ring.SegmentEnd);
                var margin = Mathf.Max(0.001f, segmentLength * 0.02f);
                var distance = Mathf.Abs(Vector3.Dot(
                    ring.Center - patch.Center,
                    patch.Axis));
                if (distance <= extent + margin)
                {
                    return true;
                }
            }

            return false;
        }

        private static float CalculateJointPatchAxialExtent(
            ReferenceJointPatch patch)
        {
            var result = 0f;
            for (var index = 0; index < patch.Triangles.Count; index++)
            {
                var triangle = patch.Triangles[index];
                result = Mathf.Max(
                    result,
                    Mathf.Abs(Vector3.Dot(
                        triangle.First.Position - patch.Center,
                        patch.Axis)));
                result = Mathf.Max(
                    result,
                    Mathf.Abs(Vector3.Dot(
                        triangle.Second.Position - patch.Center,
                        patch.Axis)));
                result = Mathf.Max(
                    result,
                    Mathf.Abs(Vector3.Dot(
                        triangle.Third.Position - patch.Center,
                        patch.Axis)));
            }

            return result;
        }

        private static bool IsAdjacentSegment(
            ReferenceBodySegment segment,
            ReferenceJointType joint)
        {
            switch (joint)
            {
                case ReferenceJointType.LeftKnee:
                    return segment == ReferenceBodySegment.LeftThigh ||
                           segment == ReferenceBodySegment.LeftCalf;
                case ReferenceJointType.RightKnee:
                    return segment == ReferenceBodySegment.RightThigh ||
                           segment == ReferenceBodySegment.RightCalf;
                case ReferenceJointType.LeftElbow:
                    return segment == ReferenceBodySegment.LeftUpperArm ||
                           segment == ReferenceBodySegment.LeftForearm;
                case ReferenceJointType.RightElbow:
                    return segment == ReferenceBodySegment.RightUpperArm ||
                           segment == ReferenceBodySegment.RightForearm;
                default:
                    return false;
            }
        }

        private static BodySectionFieldRing RecoverRing(
            ReferenceSectionRing source)
        {
            if (source == null ||
                source.SourceSamples.Count < 4 ||
                source.Axis.sqrMagnitude < 0.00000001f)
            {
                return null;
            }

            var axis = source.Axis.normalized;
            BuildPlaneBasis(axis, out var axisX, out var axisY);
            var observations = new List<PolarObservation>(
                source.SourceSamples.Count);
            for (var index = 0; index < source.SourceSamples.Count; index++)
            {
                var sample = source.SourceSamples[index];
                var offset = sample.Position - source.Center;
                var x = Vector3.Dot(offset, axisX);
                var y = Vector3.Dot(offset, axisY);
                var radius = Mathf.Sqrt(x * x + y * y);
                if (radius > 0.000001f)
                {
                    observations.Add(new PolarObservation(
                        NormalizeAngle(Mathf.Atan2(y, x)),
                        radius,
                        sample));
                }
            }

            if (observations.Count < 4)
            {
                return null;
            }

            observations.Sort((first, second) =>
                first.Angle.CompareTo(second.Angle));
            var strategy = ResolveStrategy(source.Segment);
            var baseSamples = BuildObservedSamples(
                source,
                strategy,
                observations,
                axis,
                axisX,
                axisY);
            var recovered = InsertClearGapSamples(
                source,
                strategy,
                observations,
                baseSamples);
            return new BodySectionFieldRing(
                source.Segment,
                strategy,
                source.NormalizedPosition,
                source.Center,
                axis,
                source.IsClosed || recovered.Count > baseSamples.Length,
                recovered);
        }

        private static void ApplyBilateralRecovery(
            IReadOnlyList<ReferenceSectionRing> sourceRings,
            IDictionary<ReferenceSectionRing, BodySectionFieldRing>
                recoveredBySource,
            IList<BodySectionFieldRing> result)
        {
            if (!TryBuildSymmetryPlane(
                    sourceRings,
                    out var planePoint,
                    out var planeNormal))
            {
                return;
            }

            ApplySegmentPair(
                ReferenceBodySegment.LeftUpperArm,
                ReferenceBodySegment.RightUpperArm,
                sourceRings,
                recoveredBySource,
                result,
                planePoint,
                planeNormal);
            ApplySegmentPair(
                ReferenceBodySegment.LeftForearm,
                ReferenceBodySegment.RightForearm,
                sourceRings,
                recoveredBySource,
                result,
                planePoint,
                planeNormal);
            ApplySegmentPair(
                ReferenceBodySegment.LeftThigh,
                ReferenceBodySegment.RightThigh,
                sourceRings,
                recoveredBySource,
                result,
                planePoint,
                planeNormal);
            ApplySegmentPair(
                ReferenceBodySegment.LeftCalf,
                ReferenceBodySegment.RightCalf,
                sourceRings,
                recoveredBySource,
                result,
                planePoint,
                planeNormal);
        }

        private static void ApplySegmentPair(
            ReferenceBodySegment leftSegment,
            ReferenceBodySegment rightSegment,
            IReadOnlyList<ReferenceSectionRing> sourceRings,
            IDictionary<ReferenceSectionRing, BodySectionFieldRing>
                recoveredBySource,
            IList<BodySectionFieldRing> result,
            Vector3 planePoint,
            Vector3 planeNormal)
        {
            var left = CollectSegmentRings(sourceRings, leftSegment);
            var right = CollectSegmentRings(sourceRings, rightSegment);
            var matchedRight = new bool[right.Count];
            for (var leftIndex = 0; leftIndex < left.Count; leftIndex++)
            {
                var rightIndex = FindNearestUnmatchedRing(
                    left[leftIndex],
                    right,
                    matchedRight);
                if (rightIndex < 0)
                {
                    result.Add(MirrorWholeRing(
                        left[leftIndex],
                        rightSegment,
                        planePoint,
                        planeNormal));
                    continue;
                }

                matchedRight[rightIndex] = true;
                var leftSource = left[leftIndex];
                var rightSource = right[rightIndex];
                if (!recoveredBySource.TryGetValue(
                        leftSource,
                        out var leftField) ||
                    !recoveredBySource.TryGetValue(
                        rightSource,
                        out var rightField))
                {
                    continue;
                }

                SelectSymmetryDonor(
                    leftSource,
                    rightSource,
                    out var donorSide,
                    out var replaceRecipient);
                if (donorSide < 0)
                {
                    ReplaceRecoveredRing(
                        rightSource,
                        rightField,
                        AugmentWithMirror(
                            rightSource,
                            rightField,
                            leftSource,
                            planePoint,
                            planeNormal,
                            replaceRecipient),
                        recoveredBySource,
                        result);
                }
                else if (donorSide > 0)
                {
                    ReplaceRecoveredRing(
                        leftSource,
                        leftField,
                        AugmentWithMirror(
                            leftSource,
                            leftField,
                            rightSource,
                            planePoint,
                            planeNormal,
                            replaceRecipient),
                        recoveredBySource,
                        result);
                }
            }

            for (var rightIndex = 0; rightIndex < right.Count; rightIndex++)
            {
                if (!matchedRight[rightIndex])
                {
                    result.Add(MirrorWholeRing(
                        right[rightIndex],
                        leftSegment,
                        planePoint,
                        planeNormal));
                }
            }
        }

        private static List<ReferenceSectionRing> CollectSegmentRings(
            IReadOnlyList<ReferenceSectionRing> sourceRings,
            ReferenceBodySegment segment)
        {
            var result = new List<ReferenceSectionRing>();
            for (var index = 0; index < sourceRings.Count; index++)
            {
                if (sourceRings[index].Segment == segment)
                {
                    result.Add(sourceRings[index]);
                }
            }

            result.Sort((first, second) =>
                first.NormalizedPosition.CompareTo(second.NormalizedPosition));
            return result;
        }

        private static int FindNearestUnmatchedRing(
            ReferenceSectionRing source,
            IReadOnlyList<ReferenceSectionRing> candidates,
            IReadOnlyList<bool> matched)
        {
            var selected = -1;
            var selectedDistance = MaximumSymmetryPositionDelta;
            for (var index = 0; index < candidates.Count; index++)
            {
                if (matched[index])
                {
                    continue;
                }

                var distance = Mathf.Abs(
                    candidates[index].NormalizedPosition -
                    source.NormalizedPosition);
                if (distance <= selectedDistance)
                {
                    selected = index;
                    selectedDistance = distance;
                }
            }

            return selected;
        }

        private static void SelectSymmetryDonor(
            ReferenceSectionRing left,
            ReferenceSectionRing right,
            out int donorSide,
            out bool replaceRecipient)
        {
            donorSide = 0;
            replaceRecipient = false;
            var leftCoverage = CalculateAngularCoverage(left);
            var rightCoverage = CalculateAngularCoverage(right);
            var leftRadius = CalculateMedianRadius(left);
            var rightRadius = CalculateMedianRadius(right);
            var minimumRadius = Mathf.Max(
                0.000001f,
                Mathf.Min(leftRadius, rightRadius));
            var radiusRatio = Mathf.Max(leftRadius, rightRadius) /
                              minimumRadius;
            if (radiusRatio >= ClothingRadiusRatio)
            {
                var smallerIsLeft = leftRadius < rightRadius;
                var smallerCoverage = smallerIsLeft
                    ? leftCoverage
                    : rightCoverage;
                var largerCoverage = smallerIsLeft
                    ? rightCoverage
                    : leftCoverage;
                if (smallerCoverage >= largerCoverage * 0.55f)
                {
                    donorSide = smallerIsLeft ? -1 : 1;
                    replaceRecipient = true;
                    return;
                }
            }

            if (left.IsClosed != right.IsClosed)
            {
                donorSide = left.IsClosed ? -1 : 1;
                return;
            }

            if (Mathf.Abs(leftCoverage - rightCoverage) >= Mathf.PI / 12f)
            {
                donorSide = leftCoverage > rightCoverage ? -1 : 1;
            }
        }

        private static BodySectionFieldRing AugmentWithMirror(
            ReferenceSectionRing targetSource,
            BodySectionFieldRing target,
            ReferenceSectionRing donor,
            Vector3 planePoint,
            Vector3 planeNormal,
            bool replaceTarget)
        {
            var values = new List<AngularFieldSample>();
            if (!replaceTarget)
            {
                for (var index = 0; index < target.Samples.Count; index++)
                {
                    if (target.Samples[index].IsObserved)
                    {
                        values.Add(new AngularFieldSample(
                            CalculateAngle(
                                target.Samples[index].Position,
                                target.Center,
                                target.Axis),
                            target.Samples[index]));
                    }
                }
            }

            var mirroredCenter = ReflectPoint(
                donor.Center,
                planePoint,
                planeNormal);
            var centerCorrection = target.Center - mirroredCenter;
            var matchTolerance = Mathf.PI * 0.9f /
                                 Mathf.Max(4, donor.SourceSamples.Count);
            var added = 0;
            for (var index = 0; index < donor.SourceSamples.Count; index++)
            {
                var source = donor.SourceSamples[index];
                var position = ReflectPoint(
                                   source.Position,
                                   planePoint,
                                   planeNormal) +
                               centerCorrection;
                var angle = CalculateAngle(
                    position,
                    target.Center,
                    target.Axis);
                if (!replaceTarget &&
                    HasSampleNearAngle(values, angle, matchTolerance))
                {
                    continue;
                }

                values.Add(new AngularFieldSample(
                    angle,
                    CreateMirroredSample(
                        position,
                        target.Center,
                        target.Axis,
                        source)));
                added++;
            }

            if (added == 0)
            {
                return target;
            }

            values.Sort((first, second) =>
                first.Angle.CompareTo(second.Angle));
            var samples = new BodySectionFieldSample[values.Count];
            for (var index = 0; index < values.Count; index++)
            {
                samples[index] = values[index].Sample;
            }

            return new BodySectionFieldRing(
                target.Segment,
                target.Strategy,
                target.NormalizedPosition,
                target.Center,
                target.Axis,
                replaceTarget
                    ? donor.IsClosed
                    : targetSource.IsClosed || donor.IsClosed,
                samples);
        }

        private static BodySectionFieldRing MirrorWholeRing(
            ReferenceSectionRing donor,
            ReferenceBodySegment targetSegment,
            Vector3 planePoint,
            Vector3 planeNormal)
        {
            var center = ReflectPoint(
                donor.Center,
                planePoint,
                planeNormal);
            var axis = ReflectDirection(donor.Axis, planeNormal).normalized;
            var samples = new BodySectionFieldSample[donor.SourceSamples.Count];
            for (var index = 0; index < donor.SourceSamples.Count; index++)
            {
                var source = donor.SourceSamples[index];
                var position = ReflectPoint(
                    source.Position,
                    planePoint,
                    planeNormal);
                samples[index] = CreateMirroredSample(
                    position,
                    center,
                    axis,
                    source);
            }

            return new BodySectionFieldRing(
                targetSegment,
                BodySectionRecoveryStrategy.Tubular,
                donor.NormalizedPosition,
                center,
                axis,
                donor.IsClosed,
                samples);
        }

        private static BodySectionFieldSample CreateMirroredSample(
            Vector3 position,
            Vector3 center,
            Vector3 axis,
            ReferenceSectionVertex source)
        {
            var radial = position - center;
            radial -= axis * Vector3.Dot(radial, axis);
            return new BodySectionFieldSample(
                position,
                radial.magnitude,
                0.75f,
                BodySectionSampleProvenance.BilateralMirrored,
                source);
        }

        private static void ReplaceRecoveredRing(
            ReferenceSectionRing source,
            BodySectionFieldRing previous,
            BodySectionFieldRing replacement,
            IDictionary<ReferenceSectionRing, BodySectionFieldRing>
                recoveredBySource,
            IList<BodySectionFieldRing> result)
        {
            if (ReferenceEquals(previous, replacement))
            {
                return;
            }

            var index = result.IndexOf(previous);
            if (index >= 0)
            {
                result[index] = replacement;
            }

            recoveredBySource[source] = replacement;
        }

        private static bool TryBuildSymmetryPlane(
            IReadOnlyList<ReferenceSectionRing> sourceRings,
            out Vector3 planePoint,
            out Vector3 planeNormal)
        {
            var pointSum = Vector3.zero;
            var normalSum = Vector3.zero;
            var count = 0;
            AccumulateSymmetryPlaneEvidence(
                ReferenceBodySegment.LeftUpperArm,
                ReferenceBodySegment.RightUpperArm,
                sourceRings,
                ref pointSum,
                ref normalSum,
                ref count);
            AccumulateSymmetryPlaneEvidence(
                ReferenceBodySegment.LeftForearm,
                ReferenceBodySegment.RightForearm,
                sourceRings,
                ref pointSum,
                ref normalSum,
                ref count);
            AccumulateSymmetryPlaneEvidence(
                ReferenceBodySegment.LeftThigh,
                ReferenceBodySegment.RightThigh,
                sourceRings,
                ref pointSum,
                ref normalSum,
                ref count);
            AccumulateSymmetryPlaneEvidence(
                ReferenceBodySegment.LeftCalf,
                ReferenceBodySegment.RightCalf,
                sourceRings,
                ref pointSum,
                ref normalSum,
                ref count);

            if (count == 0 || normalSum.sqrMagnitude < 0.00000001f)
            {
                planePoint = Vector3.zero;
                planeNormal = Vector3.right;
                return false;
            }

            planePoint = pointSum / count;
            planeNormal = normalSum.normalized;
            return true;
        }

        private static void AccumulateSymmetryPlaneEvidence(
            ReferenceBodySegment leftSegment,
            ReferenceBodySegment rightSegment,
            IReadOnlyList<ReferenceSectionRing> sourceRings,
            ref Vector3 pointSum,
            ref Vector3 normalSum,
            ref int count)
        {
            var left = CollectSegmentRings(sourceRings, leftSegment);
            var right = CollectSegmentRings(sourceRings, rightSegment);
            var matched = new bool[right.Count];
            for (var leftIndex = 0; leftIndex < left.Count; leftIndex++)
            {
                var rightIndex = FindNearestUnmatchedRing(
                    left[leftIndex],
                    right,
                    matched);
                if (rightIndex < 0)
                {
                    continue;
                }

                matched[rightIndex] = true;
                var lateral = left[leftIndex].Center - right[rightIndex].Center;
                if (lateral.sqrMagnitude < 0.00000001f)
                {
                    continue;
                }

                lateral.Normalize();
                if (normalSum.sqrMagnitude > 0.00000001f &&
                    Vector3.Dot(lateral, normalSum) < 0f)
                {
                    lateral = -lateral;
                }

                pointSum += (left[leftIndex].Center + right[rightIndex].Center) *
                            0.5f;
                normalSum += lateral;
                count++;
            }
        }

        private static float CalculateAngularCoverage(
            ReferenceSectionRing ring)
        {
            if (ring.IsClosed)
            {
                return Mathf.PI * 2f;
            }

            var angles = new float[ring.SourceSamples.Count];
            for (var index = 0; index < angles.Length; index++)
            {
                angles[index] = CalculateAngle(
                    ring.SourceSamples[index].Position,
                    ring.Center,
                    ring.Axis);
            }

            Array.Sort(angles);
            var maximumGap = angles[0] + Mathf.PI * 2f -
                             angles[angles.Length - 1];
            for (var index = 1; index < angles.Length; index++)
            {
                maximumGap = Mathf.Max(
                    maximumGap,
                    angles[index] - angles[index - 1]);
            }

            return Mathf.PI * 2f - maximumGap;
        }

        private static float CalculateMedianRadius(ReferenceSectionRing ring)
        {
            var radii = new float[ring.SourceSamples.Count];
            for (var index = 0; index < radii.Length; index++)
            {
                var radial = ring.SourceSamples[index].Position - ring.Center;
                radial -= ring.Axis * Vector3.Dot(radial, ring.Axis);
                radii[index] = radial.magnitude;
            }

            return Median(radii);
        }

        private static float CalculateAngle(
            Vector3 position,
            Vector3 center,
            Vector3 axis)
        {
            BuildPlaneBasis(axis, out var axisX, out var axisY);
            var offset = position - center;
            return NormalizeAngle(Mathf.Atan2(
                Vector3.Dot(offset, axisY),
                Vector3.Dot(offset, axisX)));
        }

        private static bool HasSampleNearAngle(
            IReadOnlyList<AngularFieldSample> samples,
            float angle,
            float tolerance)
        {
            for (var index = 0; index < samples.Count; index++)
            {
                var delta = Mathf.Abs(Mathf.DeltaAngle(
                                angle * Mathf.Rad2Deg,
                                samples[index].Angle * Mathf.Rad2Deg)) *
                            Mathf.Deg2Rad;
                if (delta <= tolerance)
                {
                    return true;
                }
            }

            return false;
        }

        private static Vector3 ReflectPoint(
            Vector3 point,
            Vector3 planePoint,
            Vector3 planeNormal)
        {
            return point - planeNormal *
                   (2f * Vector3.Dot(point - planePoint, planeNormal));
        }

        private static Vector3 ReflectDirection(
            Vector3 direction,
            Vector3 planeNormal)
        {
            return direction - planeNormal *
                   (2f * Vector3.Dot(direction, planeNormal));
        }

        private static BodySectionFieldSample[] BuildObservedSamples(
            ReferenceSectionRing source,
            BodySectionRecoveryStrategy strategy,
            IReadOnlyList<PolarObservation> observations,
            Vector3 axis,
            Vector3 axisX,
            Vector3 axisY)
        {
            var result = new BodySectionFieldSample[observations.Count];
            var radii = new float[observations.Count];
            var residuals = new float[observations.Count];
            for (var index = 0; index < observations.Count; index++)
            {
                radii[index] = observations[index].Radius;
                residuals[index] = CalculateLocalRadiusResidual(
                    observations,
                    index);
            }

            var medianRadius = Median(radii);
            var medianResidual = Median(residuals);
            var absoluteDeviations = new float[residuals.Length];
            for (var index = 0; index < residuals.Length; index++)
            {
                absoluteDeviations[index] = Mathf.Abs(
                    residuals[index] - medianResidual);
            }

            var residualMad = Median(absoluteDeviations);
            var outlierThreshold = Mathf.Max(
                medianRadius * 0.18f,
                medianResidual + residualMad * 6f);
            for (var index = 0; index < observations.Count; index++)
            {
                var observation = observations[index];
                var isOpenEndpoint = !source.IsClosed &&
                                     (ReferenceEquals(
                                          observation.Source,
                                          source.SourceSamples[0]) ||
                                      ReferenceEquals(
                                          observation.Source,
                                          source.SourceSamples[
                                              source.SourceSamples.Count - 1]));
                var shouldRepair = strategy ==
                                       BodySectionRecoveryStrategy.Tubular &&
                                   observations.Count >= 6 &&
                                   !isOpenEndpoint &&
                                   residuals[index] > outlierThreshold &&
                                   HasStrongSegmentInfluence(
                                       observation.Source,
                                       source.Segment);
                if (!shouldRepair)
                {
                    result[index] = new BodySectionFieldSample(
                        observation.Source.Position,
                        observation.Radius,
                        1f,
                        BodySectionSampleProvenance.Observed,
                        observation.Source);
                    continue;
                }

                var previousIndex = Wrap(index - 1, observations.Count);
                var nextIndex = Wrap(index + 1, observations.Count);
                GetUnwrappedBracket(
                    observations,
                    index,
                    previousIndex,
                    nextIndex,
                    out var currentAngle,
                    out var previousAngle,
                    out var nextAngle);
                var alpha = Mathf.InverseLerp(
                    previousAngle,
                    nextAngle,
                    currentAngle);
                var repairedRadius = Mathf.Lerp(
                    observations[previousIndex].Radius,
                    observations[nextIndex].Radius,
                    alpha);
                var previousOffset = observations[previousIndex].Source.Position -
                                     source.Center;
                var nextOffset = observations[nextIndex].Source.Position -
                                 source.Center;
                var axialOffset = Mathf.Lerp(
                    Vector3.Dot(previousOffset, axis),
                    Vector3.Dot(nextOffset, axis),
                    alpha);
                var direction = axisX * Mathf.Cos(observation.Angle) +
                                axisY * Mathf.Sin(observation.Angle);
                result[index] = new BodySectionFieldSample(
                    source.Center + direction * repairedRadius +
                    axis * axialOffset,
                    repairedRadius,
                    GetSegmentInfluence(observation.Source, source.Segment),
                    BodySectionSampleProvenance.OutlierRepaired,
                    observation.Source);
            }

            return result;
        }

        private static IReadOnlyList<BodySectionFieldSample> InsertClearGapSamples(
            ReferenceSectionRing source,
            BodySectionRecoveryStrategy strategy,
            IReadOnlyList<PolarObservation> observations,
            IReadOnlyList<BodySectionFieldSample> baseSamples)
        {
            if (strategy == BodySectionRecoveryStrategy.FeaturePreserving)
            {
                return baseSamples;
            }

            var gaps = new float[observations.Count];
            for (var index = 0; index < observations.Count; index++)
            {
                gaps[index] = ForwardAngleDistance(
                    observations[index].Angle,
                    observations[(index + 1) % observations.Count].Angle);
            }

            var expectedGap = Median(gaps);
            if (expectedGap <= 0.000001f)
            {
                return baseSamples;
            }

            var gapFactor = strategy == BodySectionRecoveryStrategy.Tubular
                ? TubularGapFactor
                : TorsoGapFactor;
            var result = new List<BodySectionFieldSample>(
                observations.Count + MaximumInsertionsPerGap);
            for (var index = 0; index < observations.Count; index++)
            {
                result.Add(baseSamples[index]);
                var gap = gaps[index];
                if (gap <= expectedGap * gapFactor)
                {
                    continue;
                }

                var insertionCount = Mathf.Clamp(
                    Mathf.RoundToInt(gap / expectedGap) - 1,
                    1,
                    MaximumInsertionsPerGap);
                var previous = baseSamples[Wrap(index - 1, baseSamples.Count)].Position;
                var start = baseSamples[index].Position;
                var end = baseSamples[(index + 1) % baseSamples.Count].Position;
                var next = baseSamples[(index + 2) % baseSamples.Count].Position;
                for (var insertion = 0;
                     insertion < insertionCount;
                     insertion++)
                {
                    var t = (insertion + 1f) / (insertionCount + 1f);
                    var position = CatmullRom(previous, start, end, next, t);
                    var radial = position - source.Center;
                    radial -= source.Axis * Vector3.Dot(radial, source.Axis);
                    var confidence = Mathf.Clamp01(expectedGap / gap) * 0.6f;
                    result.Add(new BodySectionFieldSample(
                        position,
                        radial.magnitude,
                        confidence,
                        BodySectionSampleProvenance.GapInserted));
                }
            }

            return result;
        }

        private static BodySectionRecoveryStrategy ResolveStrategy(
            ReferenceBodySegment segment)
        {
            switch (segment)
            {
                case ReferenceBodySegment.Waist:
                case ReferenceBodySegment.Abdomen:
                    return BodySectionRecoveryStrategy.TorsoContour;
                case ReferenceBodySegment.Chest:
                case ReferenceBodySegment.UpperChest:
                    return BodySectionRecoveryStrategy.FeaturePreserving;
                default:
                    return BodySectionRecoveryStrategy.Tubular;
            }
        }

        private static bool HasStrongSegmentInfluence(
            ReferenceSectionVertex vertex,
            ReferenceBodySegment segment)
        {
            var dominant = vertex.DominantHumanoidBone;
            return dominant.HasValue &&
                   IsSegmentBone(dominant.Value, segment) &&
                   GetSegmentInfluence(vertex, segment) >= 0.5f;
        }

        private static float GetSegmentInfluence(
            ReferenceSectionVertex vertex,
            ReferenceBodySegment segment)
        {
            var result = 0f;
            for (var index = 0; index < vertex.Influences.Count; index++)
            {
                var influence = vertex.Influences[index];
                if (influence.HumanoidBone.HasValue &&
                    IsSegmentBone(influence.HumanoidBone.Value, segment))
                {
                    result += influence.Weight;
                }
            }

            return Mathf.Clamp01(result);
        }

        private static bool IsSegmentBone(
            HumanBodyBones bone,
            ReferenceBodySegment segment)
        {
            switch (segment)
            {
                case ReferenceBodySegment.LeftUpperArm:
                    return bone == HumanBodyBones.LeftUpperArm ||
                           bone == HumanBodyBones.LeftLowerArm;
                case ReferenceBodySegment.LeftForearm:
                    return bone == HumanBodyBones.LeftLowerArm ||
                           bone == HumanBodyBones.LeftHand;
                case ReferenceBodySegment.RightUpperArm:
                    return bone == HumanBodyBones.RightUpperArm ||
                           bone == HumanBodyBones.RightLowerArm;
                case ReferenceBodySegment.RightForearm:
                    return bone == HumanBodyBones.RightLowerArm ||
                           bone == HumanBodyBones.RightHand;
                case ReferenceBodySegment.LeftThigh:
                    return bone == HumanBodyBones.LeftUpperLeg ||
                           bone == HumanBodyBones.LeftLowerLeg;
                case ReferenceBodySegment.LeftCalf:
                    return bone == HumanBodyBones.LeftLowerLeg ||
                           bone == HumanBodyBones.LeftFoot;
                case ReferenceBodySegment.RightThigh:
                    return bone == HumanBodyBones.RightUpperLeg ||
                           bone == HumanBodyBones.RightLowerLeg;
                case ReferenceBodySegment.RightCalf:
                    return bone == HumanBodyBones.RightLowerLeg ||
                           bone == HumanBodyBones.RightFoot;
                case ReferenceBodySegment.Waist:
                    return bone == HumanBodyBones.Hips ||
                           bone == HumanBodyBones.Spine;
                case ReferenceBodySegment.Abdomen:
                    return bone == HumanBodyBones.Spine ||
                           bone == HumanBodyBones.Chest;
                case ReferenceBodySegment.Chest:
                    return bone == HumanBodyBones.Chest ||
                           bone == HumanBodyBones.UpperChest;
                case ReferenceBodySegment.UpperChest:
                    return bone == HumanBodyBones.UpperChest ||
                           bone == HumanBodyBones.Neck;
                default:
                    return false;
            }
        }

        private static float CalculateLocalRadiusResidual(
            IReadOnlyList<PolarObservation> values,
            int index)
        {
            var previousIndex = Wrap(index - 1, values.Count);
            var nextIndex = Wrap(index + 1, values.Count);
            GetUnwrappedBracket(
                values,
                index,
                previousIndex,
                nextIndex,
                out var currentAngle,
                out var previousAngle,
                out var nextAngle);
            var alpha = Mathf.InverseLerp(
                previousAngle,
                nextAngle,
                currentAngle);
            var expected = Mathf.Lerp(
                values[previousIndex].Radius,
                values[nextIndex].Radius,
                alpha);
            return Mathf.Abs(values[index].Radius - expected);
        }

        private static void GetUnwrappedBracket(
            IReadOnlyList<PolarObservation> values,
            int currentIndex,
            int previousIndex,
            int nextIndex,
            out float current,
            out float previous,
            out float next)
        {
            current = values[currentIndex].Angle;
            previous = values[previousIndex].Angle;
            next = values[nextIndex].Angle;
            if (previousIndex > currentIndex)
            {
                previous -= Mathf.PI * 2f;
            }
            if (nextIndex < currentIndex)
            {
                next += Mathf.PI * 2f;
            }
        }

        private static float ForwardAngleDistance(float start, float end)
        {
            var result = end - start;
            return result > 0f ? result : result + Mathf.PI * 2f;
        }

        private static Vector3 CatmullRom(
            Vector3 previous,
            Vector3 start,
            Vector3 end,
            Vector3 next,
            float t)
        {
            var t2 = t * t;
            var t3 = t2 * t;
            return 0.5f * ((2f * start) +
                           (-previous + end) * t +
                           (2f * previous - 5f * start + 4f * end - next) * t2 +
                           (-previous + 3f * start - 3f * end + next) * t3);
        }

        private static float Median(float[] values)
        {
            if (values.Length == 0)
            {
                return 0f;
            }

            var sorted = (float[])values.Clone();
            Array.Sort(sorted);
            var middle = sorted.Length / 2;
            return sorted.Length % 2 == 0
                ? (sorted[middle - 1] + sorted[middle]) * 0.5f
                : sorted[middle];
        }

        private static int Wrap(int index, int count)
        {
            return (index % count + count) % count;
        }

        private static float NormalizeAngle(float angle)
        {
            return angle < 0f ? angle + Mathf.PI * 2f : angle;
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

        private readonly struct PolarObservation
        {
            public PolarObservation(
                float angle,
                float radius,
                ReferenceSectionVertex source)
            {
                Angle = angle;
                Radius = radius;
                Source = source;
            }

            public float Angle { get; }
            public float Radius { get; }
            public ReferenceSectionVertex Source { get; }
        }

        private readonly struct AngularFieldSample
        {
            public AngularFieldSample(
                float angle,
                BodySectionFieldSample sample)
            {
                Angle = angle;
                Sample = sample;
            }

            public float Angle { get; }
            public BodySectionFieldSample Sample { get; }
        }
    }

    internal sealed class BodySectionFieldOverlay : IDisposable
    {
        private readonly GameObject root;
        private readonly Mesh mesh;
        private readonly Material material;
        private readonly List<Vector3> lineVertices = new List<Vector3>();
        private bool visible;

        public BodySectionFieldOverlay(
            Transform parent,
            Shader shader,
            Color color)
        {
            root = new GameObject("Sparse Body Section Repairs");
            root.transform.SetParent(parent, false);
            mesh = new Mesh
            {
                name = "Sparse Body Section Repair Mesh",
                hideFlags = HideFlags.DontSave,
                indexFormat = IndexFormat.UInt32,
            };
            root.AddComponent<MeshFilter>().sharedMesh = mesh;

            material = CreateOverlayMaterial(shader, color, 4009);
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
            root.SetActive(false);
        }

        public void Rebuild(BodySectionField field)
        {
            lineVertices.Clear();
            for (var ringIndex = 0; ringIndex < field.Rings.Count; ringIndex++)
            {
                var ring = field.Rings[ringIndex];
                var edgeCount = ring.IsClosed
                    ? ring.Samples.Count
                    : Mathf.Max(0, ring.Samples.Count - 1);
                for (var sampleIndex = 0;
                     sampleIndex < edgeCount;
                     sampleIndex++)
                {
                    var sample = ring.Samples[sampleIndex];
                    var next = ring.Samples[
                        (sampleIndex + 1) % ring.Samples.Count];
                    if (!sample.IsObserved || !next.IsObserved)
                    {
                        AppendLine(sample.Position, next.Position);
                    }
                }

                for (var sampleIndex = 0;
                     sampleIndex < ring.Samples.Count;
                     sampleIndex++)
                {
                    var sample = ring.Samples[sampleIndex];
                    if (!sample.IsObserved)
                    {
                        AppendMarker(
                            sample.Position,
                            Mathf.Clamp(sample.Radius * 0.04f, 0.0005f, 0.004f));
                    }
                }
            }

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

        public void SetVisible(bool enabled)
        {
            visible = enabled;
            root.SetActive(enabled && lineVertices.Count > 0);
        }

        public void Dispose()
        {
            Destroy(root);
            Destroy(mesh);
            Destroy(material);
        }

        private void AppendMarker(Vector3 point, float size)
        {
            AppendLine(point - Vector3.right * size, point + Vector3.right * size);
            AppendLine(point - Vector3.up * size, point + Vector3.up * size);
            AppendLine(point - Vector3.forward * size, point + Vector3.forward * size);
        }

        private void AppendLine(Vector3 start, Vector3 end)
        {
            lineVertices.Add(root.transform.InverseTransformPoint(start));
            lineVertices.Add(root.transform.InverseTransformPoint(end));
        }
    }
}
