using System;
using System.Collections.Generic;
using StudioEditor.Characters;
using UnityEngine;

namespace StudioEditor.ReferenceModels
{
    // ExpressionBone corrects skin deformation and the attached body colliders.
    // Evaluate the final pose, including user edits, before clothing proxies copy it.
    internal sealed class KoikatsuJointCorrection : ICharacterPoseModifier
    {
        private readonly CharacterSkeleton skeleton;
        private readonly Dictionary<string, int> bones = new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly string[] armTwists = { "cf_d_arm01", "cf_d_arm02", "cf_d_arm03" };
        private static readonly string[] thighTwists = { "cf_d_thigh01", "cf_d_thigh02", "cf_d_thigh03" };
        private static readonly string[] sides = { "L", "R" };
        private readonly Dictionary<string, int>[] sideBones = {
            new Dictionary<string, int>(StringComparer.Ordinal), new Dictionary<string, int>(StringComparer.Ordinal)
        };
        private int activeSide;
        private readonly bool[] categories = { true, true, true, true, true, true, true, true };
        private readonly int calculationRoot;
        private CharacterPoseBuffer pose;

        internal KoikatsuJointCorrection(CharacterSkeleton skeleton, Transform bodyRoot)
        {
            this.skeleton = skeleton ?? throw new ArgumentNullException(nameof(skeleton));
            if (bodyRoot == null) throw new ArgumentNullException(nameof(bodyRoot));
            // Body, clothes and accessories can contain identical bone names.
            // Only the canonical animation skeleton owns these corrections.
            foreach (var transform in bodyRoot.GetComponentsInChildren<Transform>(true))
                if (skeleton.TryGetBoneIndex(transform, out var index) && !bones.ContainsKey(transform.name))
                    bones.Add(transform.name, index);
            foreach (var pair in bones)
            {
                if (pair.Key.EndsWith("_L", StringComparison.Ordinal)) sideBones[0][pair.Key.Substring(0, pair.Key.Length - 2)] = pair.Value;
                else if (pair.Key.EndsWith("_R", StringComparison.Ordinal)) sideBones[1][pair.Key.Substring(0, pair.Key.Length - 2)] = pair.Value;
            }
            calculationRoot = skeleton.TryGetBoneIndex(bodyRoot, out var root) ? root : -1;
        }

        public int Order => CharacterPoseStages.JointCorrections;
        public bool Enabled { get; set; } = true;

        internal void Configure(IReadOnlyList<bool> enabled)
        {
            for (var i = 0; i < categories.Length; i++)
                categories[i] = enabled == null || i >= enabled.Count || enabled[i];
        }

        public void Evaluate(CharacterPoseBuffer value)
        {
            if (!ReferenceEquals(value.Skeleton, skeleton)) return;
            pose = value;
            Torso();
            for (var side = 0; side < 2; side++)
            {
                activeSide = side;
                var sign = side == 0 ? 1f : -1f;
                if (categories[side])
                {
                    Shoulder(sign);
                    Twist("cf_j_arm00", armTwists, 0, -.6f, -.5f, -.125f);
                    Elbow(sign);
                    if (Find("cf_j_hand") >= 0)
                        SetAngle("cf_d_forearm02", 0, Angle("cf_j_hand", 0) * .45f);
                }
                if (categories[4 + side]) Forearm(sign);
                if (categories[2 + side])
                {
                    Leg(sign);
                    Knee(sign);
                    if (Find("cf_j_leg03") >= 0)
                        SetAngle("cf_d_leg02", 1, Angle("cf_j_leg03", 1) * .5f);
                }
                if (categories[6 + side])
                    Twist("cf_j_thigh00", thighTwists, 1, -.75f, -.5f, -.25f);
            }
        }

        private void Torso()
        {
            if (bones.TryGetValue("cf_j_spine03", out var spine) && bones.TryGetValue("cf_d_bust00", out var chest))
            {
                var angle = KoikatsuJointRotation.ToDegrees(pose.GetLocalRotation(spine));
                pose.SetLocalRotation(chest, KoikatsuJointRotation.FromDegrees(angle * -.35f));
                var position = pose.GetLocalPosition(chest);
                position.y = (-.081f + angle.x * .003f) * .1f;
                position.z = angle.x * .004f * .1f;
                pose.SetLocalPosition(chest, position);
            }
            foreach (var side in sides)
            {
                if (!bones.TryGetValue("cf_j_bust02_" + side, out var source) ||
                    !bones.TryGetValue("cf_d_hit_bust_" + side, out var target)) continue;
                var position = pose.GetLocalPosition(source);
                var scale = pose.GetLocalScale(source);
                pose.SetLocalPosition(target, new Vector3(-position.x * .5f, -position.y * .5f, -position.z * .25f));
                pose.SetLocalScale(target, new Vector3(1.5f - scale.x * .5f, 1.5f - scale.y * .5f, 1.5f - scale.z * .5f));
            }
        }

        private void Shoulder(float sign)
        {
            var arm = Find("cf_j_arm00");
            var shoulder = Find("cf_j_shoulder");
            var elbow = Find("cf_j_forearm01");
            var target = Find("cf_d_shoulder02");
            if (arm < 0 || shoulder < 0 || elbow < 0 || target < 0) return;
            // Original n_goal_forearm is a child of the shoulder at (0, .3, 0).
            var offset = pose.GetWorldMatrix(shoulder).inverse.MultiplyPoint3x4(pose.GetWorldPosition(elbow)) - new Vector3(0, .3f, 0);
            var lift = Mathf.Max(0, .3f - Mathf.Abs(offset.y));
            var position = pose.GetLocalPosition(target);
            position.x = pose.GetLocalPosition(arm).x - sign * (lift + Mathf.Abs(offset.z)) * .03f;
            position.y = lift * .2f;
            position.z = -offset.z * .06f;
            pose.SetLocalPosition(target, position);
            var angles = KoikatsuJointRotation.ToDegrees(pose.GetLocalRotation(target), KoikatsuJointRotation.Order.YZX);
            angles.y = sign * offset.z * 50;
            angles.z = -sign * lift * 250;
            pose.SetLocalRotation(target, KoikatsuJointRotation.FromDegrees(angles, KoikatsuJointRotation.Order.YZX));
        }

        private void Elbow(float sign)
        {
            var source = Find("cf_j_forearm01");
            if (source < 0) return;
            var bend = KoikatsuJointRotation.ToDegrees(pose.GetLocalRotation(source), KoikatsuJointRotation.Order.ZXY).y;
            var front = "cf_s_elbo";
            var back = "cf_s_elboback";
            SetAngle(front, 1, -bend * .5f, KoikatsuJointRotation.Order.ZXY);
            SetPosition(front, 0, Mathf.Sin(bend * .015f) * .022f);
            SetPosition(front, 2, (-.25f + sign * Mathf.Sin((bend - 1) * .006f) * .18f) * .1f);
            SetAngle(back, 1, -bend * .5f, KoikatsuJointRotation.Order.ZXY);
            SetPosition(back, 0, -Mathf.Tan(bend * .0039f) * 1.2f);
            SetPosition(back, 2, (.3f + sign * Mathf.Sin(bend * .009f) * 7.6f - sign * Mathf.Tan(bend * .0022f) * 16) * .1f);
        }

        private void Forearm(float sign)
        {
            var forearm = Find("cf_j_forearm01");
            var hand = Find("cf_j_hand");
            var finger = Find("cf_j_middle01");
            if (forearm < 0 || hand < 0 || finger < 0) return;
            var handPosition = pose.GetWorldPosition(hand);
            var offset = CalculationMatrix(handPosition, WorldRotation(forearm), Vector3.one).inverse.MultiplyPoint3x4(pose.GetWorldPosition(finger));
            var spread = Mathf.Sin(Mathf.Max(0, offset.y) * 100 * Mathf.Deg2Rad) * 1.5f;
            SetPosition("cf_d_hand", 0, -sign * (.025f - spread));
            SetPosition("cf_d_hand", 1, spread);
            // Transform.Translate in self space uses rotation, without scale.
            var point = handPosition + WorldRotation(hand) * new Vector3(-sign * .01f, .01f, 0);
            offset = pose.GetWorldMatrix(forearm).inverse.MultiplyPoint3x4(point);
            offset.x = 0;
            var twist = Vector3.Angle(Vector3.up, offset.normalized) * Mathf.Sign(offset.z);
            // SetLocalRotationX in the source uses Unity's default Euler order.
            var wrist = Find("cf_d_wrist");
            if (wrist < 0) return;
            var euler = pose.GetLocalRotation(wrist).eulerAngles;
            euler.x = twist;
            pose.SetLocalRotation(wrist, Quaternion.Euler(euler));
        }

        private void Leg(float sign)
        {
            var waist = Find("cf_j_waist02");
            var thigh = Find("cf_j_thigh00");
            var knee = Find("cf_j_leg01");
            var height = Find("cf_n_height");
            if (waist < 0 || thigh < 0 || knee < 0 || height < 0) return;
            var offset = CalculationMatrix(pose.GetWorldPosition(thigh), WorldRotation(waist), Vector3.one).inverse.MultiplyPoint3x4(pose.GetWorldPosition(knee));
            var heightScale = pose.GetLocalScale(height).y;
            if (Mathf.Abs(heightScale) < 1e-6f) return;
            var lift = Mathf.Max(0, offset.y / heightScale * 10);
            // Original n_leg_goal is parented to waist02.
            offset = (pose.GetWorldMatrix(waist).inverse.MultiplyPoint3x4(pose.GetWorldPosition(knee)) - new Vector3(-sign * .083f, .4f, -.008f)) * -10;
            var target = Find("cf_s_leg");
            if (target >= 0)
            {
                pose.SetLocalPosition(target, new Vector3(
                    (-sign - offset.x * .2f - sign * (Mathf.Abs(offset.z * 1.1f) - (8.849f - offset.y)) * .06f) * .1f,
                    (1 + (8.849f - offset.y) * .4f + sign * offset.x * .2f + lift * .6f) * .1f,
                    (-offset.z * .2f + sign * offset.x * .05f - lift * .4f) * .1f));
                pose.SetLocalRotation(target, KoikatsuJointRotation.FromDegrees(new Vector3(offset.z * 6 - sign * offset.x * 2 - lift * 5, -offset.x * 12 - sign * offset.z * 3, -offset.x)));
                pose.SetLocalScale(target, new Vector3(1 + Mathf.Abs(offset.z) * .2f, 1 + lift * .4f, 1 + Mathf.Abs(offset.z) * .3f + lift * .3f));
            }
            target = Find("cf_d_siri");
            if (target < 0) return;
            pose.SetLocalPosition(target, new Vector3(
                (-sign * .7f + offset.x * .01f - sign * Mathf.Abs(offset.x * .02f) - sign * Mathf.Abs(offset.z * .005f)) * .1f,
                ((8.849f - offset.y) * .01f + Mathf.Abs(offset.z * .02f) + sign * offset.x * .04f) * .1f,
                (-(8.849f - offset.y) * .03f - (offset.z - lift * 3) * .03f - Mathf.Abs(offset.x) * .02f) * .1f));
            var scale = pose.GetLocalScale(target);
            scale.x = 1 + Mathf.Abs(offset.z) * .02f;
            scale.y = 1 + Mathf.Abs(lift) * .01f;
            pose.SetLocalScale(target, scale);
            // Source calculates, but does not assign, siri rotation.
        }

        private void Knee(float sign)
        {
            var source = Find("cf_j_leg01");
            if (source < 0) return;
            var bend = Angle("cf_j_leg01", 0);
            var back = "cf_s_kneeB";
            var front = "cf_d_kneeF";
            SetAngle(back, 0, -bend * .5f);
            SetAngle(back, 2, sign * bend * .1f);
            SetPosition(back, 1, (-bend * .048f + Mathf.Sin(bend * .01f) * 1.1f) * .1f);
            SetPosition(back, 2, (-.6f - Mathf.Sin(bend * .0115f) * 4 + Mathf.Tan(bend * .0032f) * 5) * .1f);
            SetAngle(front, 0, -bend * .5f);
            SetPosition(front, 1, (Mathf.Sin(bend * .012f) * 1.8f - Mathf.Tan(bend * .00335f) * 4.5f) * .1f);
            SetPosition(front, 2, (.4f + bend * .0125f - Mathf.Tan(bend * .0032f) * 4.5f) * .1f);
        }

        private void Twist(string source, string[] targets, int axis, float a, float b, float c)
        {
            if (Find(source) < 0) return;
            var angle = Angle(source, axis);
            SetAngle(targets[0], axis, angle * a);
            SetAngle(targets[1], axis, angle * b);
            SetAngle(targets[2], axis, angle * c);
        }

        private int Find(string name) => sideBones[activeSide].TryGetValue(name, out var index) ? index
            : bones.TryGetValue(name, out index) ? index : -1;

        private float Angle(string name, int axis)
        {
            var index = Find(name);
            return index < 0 ? 0 : KoikatsuJointRotation.ToDegrees(pose.GetLocalRotation(index))[axis];
        }

        private void SetPosition(string name, int axis, float value)
        {
            var index = Find(name);
            if (index < 0) return;
            var position = pose.GetLocalPosition(index);
            position[axis] = value;
            pose.SetLocalPosition(index, position);
        }

        private void SetAngle(string name, int axis, float value, KoikatsuJointRotation.Order order = KoikatsuJointRotation.Order.ZYX)
        {
            var index = Find(name);
            if (index < 0) return;
            var angles = KoikatsuJointRotation.ToDegrees(pose.GetLocalRotation(index), order);
            angles[axis] = value;
            pose.SetLocalRotation(index, KoikatsuJointRotation.FromDegrees(angles, order));
        }

        // Transform.rotation composes quaternions directly; extracting rotation
        // from a matrix with nonuniform parent scale can give a different answer.
        private Quaternion WorldRotation(int index)
        {
            var parent = skeleton.Bones[index].ParentIndex;
            return parent >= 0 ? WorldRotation(parent) * pose.GetLocalRotation(index)
                : (skeleton.Bones[index].Transform.parent != null ? skeleton.Bones[index].Transform.parent.rotation : Quaternion.identity) * pose.GetLocalRotation(index);
        }

        private Matrix4x4 CalculationMatrix(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            if (calculationRoot < 0) return Matrix4x4.TRS(position, rotation, scale);
            var parent = pose.GetWorldMatrix(calculationRoot);
            return parent * Matrix4x4.TRS(parent.inverse.MultiplyPoint3x4(position), Quaternion.Inverse(WorldRotation(calculationRoot)) * rotation, scale);
        }
    }

    // The original uses three different rotation orders for limbs. Unity's
    // eulerAngles (ZXY) cannot replace them, especially across the 0/360 seam.
    internal static class KoikatsuJointRotation
    {
        internal enum Order { ZYX, ZXY, YZX }

        internal static Quaternion FromDegrees(Vector3 value, Order order = Order.ZYX)
        {
            var x = Quaternion.AngleAxis(value.x, Vector3.right);
            var y = Quaternion.AngleAxis(value.y, Vector3.up);
            var z = Quaternion.AngleAxis(value.z, Vector3.forward);
            return order == Order.ZYX ? z * y * x : order == Order.ZXY ? z * x * y : y * z * x;
        }

        internal static Vector3 ToDegrees(Quaternion value, Order order = Order.ZYX)
        {
            var m = Matrix4x4.Rotate(value);
            Vector3 result;
            switch (order)
            {
                case Order.ZXY:
                    result = new Vector3(Mathf.Asin(Mathf.Clamp(m.m21, -1, 1)), m.m21 > .9999f ? 0 : Mathf.Atan2(-m.m20, m.m22), m.m21 > .9999f ? Mathf.Atan2(m.m10, m.m00) : Mathf.Atan2(-m.m01, m.m11));
                    break;
                case Order.YZX:
                    result = new Vector3(m.m10 > .9999f ? 0 : Mathf.Atan2(-m.m12, m.m11), m.m10 > .9999f ? Mathf.Atan2(m.m02, m.m22) : Mathf.Atan2(-m.m20, m.m00), Mathf.Asin(Mathf.Clamp(m.m10, -1, 1)));
                    break;
                default:
                    result = new Vector3(-m.m20 > .9999f ? 0 : Mathf.Atan2(m.m21, m.m22), Mathf.Asin(Mathf.Clamp(-m.m20, -1, 1)), -m.m20 > .9999f ? Mathf.Atan2(-m.m01, m.m11) : Mathf.Atan2(m.m10, m.m00));
                    break;
            }
            for (var i = 0; i < 3; i++) if (float.IsNaN(result[i])) result[i] = 0;
            return result * Mathf.Rad2Deg;
        }
    }
}
