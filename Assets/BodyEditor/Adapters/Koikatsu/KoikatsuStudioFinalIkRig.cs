using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BodyEditor.ReferenceModels
{
    internal sealed class KoikatsuStudioFinalIkRig
    {
        private readonly KoikatsuFinalIkComponent fullBodyIk;
        private readonly bool[] targetAvailable;
        private bool active;

        private KoikatsuStudioFinalIkRig(
            KoikatsuFinalIkComponent fullBodyIk,
            bool[] targetAvailable)
        {
            this.fullBodyIk = fullBodyIk;
            this.targetAvailable = targetAvailable;
        }

        public static bool TryCreate(
            GameObject host,
            IReadOnlyDictionary<string, Transform> skeleton,
            IReadOnlyList<Transform> targets,
            out KoikatsuStudioFinalIkRig rig,
            out string error)
        {
            rig = null;
            if (host == null)
            {
                error = "The character root is missing.";
                return false;
            }

            if (!TryBuildReferences(skeleton, out var references, out error))
            {
                return false;
            }

            KoikatsuFinalIkComponent component = null;
            var componentCreated = false;
            try
            {
                if (!KoikatsuFinalIkRuntime.TryGetOrAdd(
                        host,
                        out component,
                        out componentCreated,
                        out error))
                {
                    return false;
                }

                component.FixTransforms = true;
                component.SetReferences(
                    references,
                    KoikatsuFinalIkRuntime.GetMember<Transform>(
                        references,
                        "pelvis"));
                component.Enabled = false;

                if (component.ReferencesError(ref error))
                {
                    DestroyIfCreated(component, componentCreated);
                    return false;
                }

                var available = new bool[13];
                var solver = component.Solver;
                BindEffector(
                    KoikatsuFinalIkRuntime.GetMember(solver, "bodyEffector"),
                    targets,
                    0,
                    false,
                    available);
                BindEffector(
                    KoikatsuFinalIkRuntime.GetMember(
                        solver,
                        "leftShoulderEffector"),
                    targets,
                    1,
                    false,
                    available);
                BindBendGoal(
                    KoikatsuFinalIkRuntime.GetMember(solver, "leftArmChain"),
                    targets,
                    2,
                    available);
                BindEffector(
                    KoikatsuFinalIkRuntime.GetMember(
                        solver,
                        "leftHandEffector"),
                    targets,
                    3,
                    true,
                    available);
                BindEffector(
                    KoikatsuFinalIkRuntime.GetMember(
                        solver,
                        "rightShoulderEffector"),
                    targets,
                    4,
                    false,
                    available);
                BindBendGoal(
                    KoikatsuFinalIkRuntime.GetMember(solver, "rightArmChain"),
                    targets,
                    5,
                    available);
                BindEffector(
                    KoikatsuFinalIkRuntime.GetMember(
                        solver,
                        "rightHandEffector"),
                    targets,
                    6,
                    true,
                    available);
                BindEffector(
                    KoikatsuFinalIkRuntime.GetMember(
                        solver,
                        "leftThighEffector"),
                    targets,
                    7,
                    false,
                    available);
                BindBendGoal(
                    KoikatsuFinalIkRuntime.GetMember(solver, "leftLegChain"),
                    targets,
                    8,
                    available);
                BindEffector(
                    KoikatsuFinalIkRuntime.GetMember(
                        solver,
                        "leftFootEffector"),
                    targets,
                    9,
                    true,
                    available);
                BindEffector(
                    KoikatsuFinalIkRuntime.GetMember(
                        solver,
                        "rightThighEffector"),
                    targets,
                    10,
                    false,
                    available);
                BindBendGoal(
                    KoikatsuFinalIkRuntime.GetMember(solver, "rightLegChain"),
                    targets,
                    11,
                    available);
                BindEffector(
                    KoikatsuFinalIkRuntime.GetMember(
                        solver,
                        "rightFootEffector"),
                    targets,
                    12,
                    true,
                    available);

                rig = new KoikatsuStudioFinalIkRig(component, available);
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                DestroyIfCreated(component, componentCreated);
                error = exception.Message;
                return false;
            }
        }

        public void SetState(bool enabled, IReadOnlyList<bool> activeGroups)
        {
            if (fullBodyIk == null || !fullBodyIk.IsAlive)
            {
                return;
            }

            var solver = fullBodyIk.Solver;
            active = enabled;
            var body = enabled && IsActive(activeGroups, 0);
            var rightLeg = enabled && IsActive(activeGroups, 1);
            var leftLeg = enabled && IsActive(activeGroups, 2);
            var rightArm = enabled && IsActive(activeGroups, 3);
            var leftArm = enabled && IsActive(activeGroups, 4);

            SetNestedFloat(
                solver,
                "spineMapping",
                "twistWeight",
                body ? 1f : 0f);
            SetEffectorWeights(
                solver,
                "Body",
                body,
                0);

            SetNestedWeight(solver, "leftArmMapping", leftArm);
            SetEffectorWeights(
                solver,
                "LeftShoulder",
                leftArm,
                1);
            SetEffectorWeights(
                solver,
                "LeftHand",
                leftArm,
                3);

            SetNestedWeight(solver, "rightArmMapping", rightArm);
            SetEffectorWeights(
                solver,
                "RightShoulder",
                rightArm,
                4);
            SetEffectorWeights(
                solver,
                "RightHand",
                rightArm,
                6);

            SetNestedWeight(solver, "leftLegMapping", leftLeg);
            SetEffectorWeights(
                solver,
                "LeftThigh",
                leftLeg,
                7);
            SetEffectorWeights(
                solver,
                "LeftFoot",
                leftLeg,
                9);

            SetNestedWeight(solver, "rightLegMapping", rightLeg);
            SetEffectorWeights(
                solver,
                "RightThigh",
                rightLeg,
                10);
            SetEffectorWeights(
                solver,
                "RightFoot",
                rightLeg,
                12);

            // Final IK is driven by KoikatsuStudioCharacterPose at an explicit
            // point in the pose pipeline. Leaving the component disabled keeps
            // RootMotion's unspecified LateUpdate out of the evaluation order.
            fullBodyIk.Enabled = false;
        }

        public void Solve()
        {
            if (!active || fullBodyIk == null || !fullBodyIk.IsAlive)
            {
                return;
            }

            if (!fullBodyIk.SolverInitiated)
            {
                fullBodyIk.Initiate();
            }

            if (fullBodyIk.SolverInitiated)
            {
                // The component is intentionally disabled so Final IK cannot
                // run from its own LateUpdate. That also bypasses
                // SolverManager's automatic FixTransforms call, which is
                // required to restore animated bones before every solve.
                if (fullBodyIk.FixTransforms)
                {
                    fullBodyIk.FixSolverTransforms();
                }

                fullBodyIk.UpdateSolver();
            }
        }

        public void Disable()
        {
            active = false;
            if (fullBodyIk != null && fullBodyIk.IsAlive)
            {
                fullBodyIk.Enabled = false;
            }
        }

        private static bool TryBuildReferences(
            IReadOnlyDictionary<string, Transform> skeleton,
            out object references,
            out string error)
        {
            references = null;
            if (skeleton == null)
            {
                error = "The character skeleton is missing.";
                return false;
            }

            var requiredNames = new[]
            {
                "cf_j_root",
                "cf_j_hips",
                "cf_j_spine01",
                "cf_j_spine02",
                "cf_j_spine03",
                "cf_j_thigh00_L",
                "cf_j_leg01_L",
                "cf_j_foot_L",
                "cf_j_thigh00_R",
                "cf_j_leg01_R",
                "cf_j_foot_R",
                "cf_j_arm00_L",
                "cf_j_forearm01_L",
                "cf_j_hand_L",
                "cf_j_arm00_R",
                "cf_j_forearm01_R",
                "cf_j_hand_R",
            };
            for (var index = 0; index < requiredNames.Length; index++)
            {
                if (!skeleton.ContainsKey(requiredNames[index]))
                {
                    error = $"Required Final IK bone '{requiredNames[index]}' " +
                            "was not found.";
                    return false;
                }
            }

            skeleton.TryGetValue("cf_j_head", out var head);
            if (!KoikatsuFinalIkRuntime.IsAvailable)
            {
                KoikatsuFinalIkRuntime.TryGetStatus(out error);
                return false;
            }

            references = KoikatsuFinalIkRuntime.CreateReferences();
            SetReference(references, "root", skeleton["cf_j_root"]);
            SetReference(references, "pelvis", skeleton["cf_j_hips"]);
            SetReference(
                references,
                "leftThigh",
                skeleton["cf_j_thigh00_L"]);
            SetReference(
                references,
                "leftCalf",
                skeleton["cf_j_leg01_L"]);
            SetReference(references, "leftFoot", skeleton["cf_j_foot_L"]);
            SetReference(
                references,
                "rightThigh",
                skeleton["cf_j_thigh00_R"]);
            SetReference(
                references,
                "rightCalf",
                skeleton["cf_j_leg01_R"]);
            SetReference(references, "rightFoot", skeleton["cf_j_foot_R"]);
            SetReference(
                references,
                "leftUpperArm",
                skeleton["cf_j_arm00_L"]);
            SetReference(
                references,
                "leftForearm",
                skeleton["cf_j_forearm01_L"]);
            SetReference(references, "leftHand", skeleton["cf_j_hand_L"]);
            SetReference(
                references,
                "rightUpperArm",
                skeleton["cf_j_arm00_R"]);
            SetReference(
                references,
                "rightForearm",
                skeleton["cf_j_forearm01_R"]);
            SetReference(references, "rightHand", skeleton["cf_j_hand_R"]);
            SetReference(references, "head", head);
            SetReference(
                references,
                "spine",
                new[]
                {
                    skeleton["cf_j_spine01"],
                    skeleton["cf_j_spine02"],
                    skeleton["cf_j_spine03"],
                });
            SetReference(references, "eyes", Array.Empty<Transform>());

            error = string.Empty;
            return true;
        }

        private static void BindEffector(
            object effector,
            IReadOnlyList<Transform> targets,
            int targetIndex,
            bool useRotation,
            bool[] available)
        {
            var target = GetTarget(targets, targetIndex);
            available[targetIndex] = target != null;
            KoikatsuFinalIkRuntime.SetMember(effector, "target", target);
            KoikatsuFinalIkRuntime.SetMember(
                effector,
                "positionWeight",
                target != null ? 1f : 0f);
            KoikatsuFinalIkRuntime.SetMember(
                effector,
                "rotationWeight",
                target != null && useRotation ? 1f : 0f);
        }

        private static void BindBendGoal(
            object chain,
            IReadOnlyList<Transform> targets,
            int targetIndex,
            bool[] available)
        {
            var target = GetTarget(targets, targetIndex);
            available[targetIndex] = target != null;
            var constraint = KoikatsuFinalIkRuntime.GetMember(
                chain,
                "bendConstraint");
            KoikatsuFinalIkRuntime.SetMember(
                constraint,
                "bendGoal",
                target);
            KoikatsuFinalIkRuntime.SetMember(
                constraint,
                "weight",
                target != null ? 1f : 0f);
        }

        private void SetEffectorWeights(
            object solver,
            string effectorName,
            bool groupActive,
            int targetIndex)
        {
            var weight = groupActive && targetAvailable[targetIndex]
                ? 1f
                : 0f;
            KoikatsuFinalIkRuntime.Invoke(
                solver,
                "SetEffectorWeights",
                KoikatsuFinalIkRuntime.GetEffectorValue(effectorName),
                weight,
                weight);
        }

        private static void SetNestedWeight(
            object solver,
            string memberName,
            bool active)
        {
            SetNestedFloat(
                solver,
                memberName,
                "weight",
                active ? 1f : 0f);
        }

        private static void SetNestedFloat(
            object solver,
            string memberName,
            string valueName,
            float value)
        {
            var mapping = KoikatsuFinalIkRuntime.GetMember(solver, memberName);
            KoikatsuFinalIkRuntime.SetMember(mapping, valueName, value);
        }

        private static void SetReference(
            object references,
            string memberName,
            object value)
        {
            KoikatsuFinalIkRuntime.SetMember(references, memberName, value);
        }

        private static Transform GetTarget(
            IReadOnlyList<Transform> targets,
            int index)
        {
            return targets != null && index >= 0 && index < targets.Count
                ? targets[index]
                : null;
        }

        private static bool IsActive(IReadOnlyList<bool> values, int index)
        {
            return values != null && index >= 0 && index < values.Count &&
                   values[index];
        }

        private static void DestroyIfCreated(
            KoikatsuFinalIkComponent component,
            bool componentCreated)
        {
            if (!componentCreated || component == null || !component.IsAlive)
            {
                return;
            }

            component.Enabled = false;
            if (Application.isPlaying)
            {
                Object.Destroy(component.Value);
            }
            else
            {
                Object.DestroyImmediate(component.Value);
            }
        }
    }
}
