using System;
using System.Collections.Generic;
using BodyEditor.Characters;
using BodyEditor.Characters.Controls;
using UnityEngine;
using UnityEngine.Rendering;

namespace BodyEditor.Editing
{
    [DefaultExecutionOrder(31000)]
    [DisallowMultipleComponent]
    public sealed class CharacterControlPointController : MonoBehaviour
    {
        private readonly List<RigEntry> entries = new List<RigEntry>();
        private ICharacterModelSource characterSource;
        private RigEntry selectedEntry;
        private CharacterControlPoint? selectedPoint;
        private ControlDragKind dragKind;
        private Plane dragPlane;
        private Vector3 dragOffset;
        private Vector3 dragAxis;
        private Vector3 dragStartPosition;
        private Quaternion dragStartRotation;
        private Vector3 dragStartDirection;
        private float dragStartAxisParameter;

        public event Action SelectionChanged;

        public ICharacterModel SelectedCharacter => selectedEntry?.Rig.Model;

        public CharacterControlPoint? SelectedControlPoint => selectedPoint;

        public bool IsDragging => dragKind != ControlDragKind.None;

        private void OnEnable()
        {
            TryBindCharacterSource();
            SynchronizeCharacters();
        }

        private void LateUpdate()
        {
            if (characterSource == null && TryBindCharacterSource())
            {
                SynchronizeCharacters();
            }

            RefreshOverlays();
        }

        public bool BeginPointerDrag(Ray ray, Vector3 viewNormal)
        {
            RefreshOverlays();
            if (selectedEntry != null && selectedPoint.HasValue &&
                selectedEntry.Overlay.TryPickManipulator(
                    ray,
                    out var manipulator) &&
                BeginManipulatorDrag(ray, manipulator))
            {
                return true;
            }

            if (!TryPick(ray, out var entry, out var point))
            {
                Select(null, null);
                return false;
            }

            Select(entry, point);
            if (!entry.Rig.TryGetControlPosition(point, out var position))
            {
                return false;
            }

            if (viewNormal.sqrMagnitude < 0.0001f)
            {
                viewNormal = Vector3.forward;
            }

            dragPlane = new Plane(viewNormal.normalized, position);
            if (!dragPlane.Raycast(ray, out var distance))
            {
                return false;
            }

            dragOffset = position - ray.GetPoint(distance);
            dragKind = entry.Rig.SetTarget(point, position)
                ? ControlDragKind.FreeMove
                : ControlDragKind.None;
            RefreshOverlays();
            return dragKind != ControlDragKind.None;
        }

        public bool UpdatePointerDrag(Ray ray)
        {
            if (dragKind == ControlDragKind.None || selectedEntry == null ||
                !selectedPoint.HasValue)
            {
                return false;
            }

            var changed = false;
            switch (dragKind)
            {
                case ControlDragKind.FreeMove:
                    if (dragPlane.Raycast(ray, out var freeDistance))
                    {
                        changed = selectedEntry.Rig.SetTarget(
                            selectedPoint.Value,
                            ray.GetPoint(freeDistance) + dragOffset);
                    }
                    break;
                case ControlDragKind.AxisMove:
                    if (TryGetAxisParameter(
                            ray,
                            dragStartPosition,
                            dragAxis,
                            out var axisParameter))
                    {
                        changed = selectedEntry.Rig.SetTarget(
                            selectedPoint.Value,
                            dragStartPosition + dragAxis *
                            (axisParameter - dragStartAxisParameter));
                    }
                    break;
                case ControlDragKind.Rotate:
                    if (dragPlane.Raycast(ray, out var rotationDistance))
                    {
                        var direction = ray.GetPoint(rotationDistance) -
                                        dragStartPosition;
                        if (direction.sqrMagnitude > 0.00000001f)
                        {
                            direction.Normalize();
                            var angle = Vector3.SignedAngle(
                                dragStartDirection,
                                direction,
                                dragAxis);
                            changed = selectedEntry.Rig.SetTargetRotation(
                                selectedPoint.Value,
                                Quaternion.AngleAxis(angle, dragAxis) *
                                dragStartRotation);
                        }
                    }
                    break;
            }

            if (!changed)
            {
                return false;
            }

            RefreshOverlays();
            return true;
        }

        public void EndPointerDrag()
        {
            dragKind = ControlDragKind.None;
        }

        private bool BeginManipulatorDrag(
            Ray ray,
            CharacterManipulatorHit manipulator)
        {
            if (selectedEntry == null || !selectedPoint.HasValue ||
                !selectedEntry.Rig.TryGetControlPosition(
                    selectedPoint.Value,
                    out dragStartPosition))
            {
                return false;
            }

            dragAxis = manipulator.Axis.normalized;
            if (manipulator.Kind == CharacterManipulatorKind.TranslateAxis)
            {
                if (!TryGetAxisParameter(
                        ray,
                        dragStartPosition,
                        dragAxis,
                        out dragStartAxisParameter) ||
                    !selectedEntry.Rig.SetTarget(
                        selectedPoint.Value,
                        dragStartPosition))
                {
                    return false;
                }

                dragKind = ControlDragKind.AxisMove;
                return true;
            }

            if (manipulator.Kind != CharacterManipulatorKind.RotateAxis ||
                !selectedEntry.Rig.TryGetControlRotation(
                    selectedPoint.Value,
                    out dragStartRotation))
            {
                return false;
            }

            dragPlane = new Plane(dragAxis, dragStartPosition);
            if (!dragPlane.Raycast(ray, out var distance))
            {
                return false;
            }

            dragStartDirection = ray.GetPoint(distance) - dragStartPosition;
            if (dragStartDirection.sqrMagnitude < 0.00000001f ||
                !selectedEntry.Rig.SetTargetRotation(
                    selectedPoint.Value,
                    dragStartRotation))
            {
                return false;
            }

            dragStartDirection.Normalize();
            dragKind = ControlDragKind.Rotate;
            return true;
        }

        private static bool TryGetAxisParameter(
            Ray ray,
            Vector3 axisOrigin,
            Vector3 axis,
            out float parameter)
        {
            var rayDirection = ray.direction.normalized;
            var originDelta = axisOrigin - ray.origin;
            var axisRayDot = Vector3.Dot(axis, rayDirection);
            var denominator = 1f - axisRayDot * axisRayDot;
            if (denominator < 0.000001f)
            {
                parameter = 0f;
                return false;
            }

            var axisOriginDot = Vector3.Dot(axis, originDelta);
            var rayOriginDot = Vector3.Dot(rayDirection, originDelta);
            parameter = (axisRayDot * rayOriginDot - axisOriginDot) /
                        denominator;
            return float.IsFinite(parameter);
        }

        public bool ClearSelectedControlPoint()
        {
            if (selectedEntry == null || !selectedPoint.HasValue)
            {
                return false;
            }

            var changed = selectedEntry.Rig.ClearTarget(selectedPoint.Value);
            RefreshOverlays();
            return changed;
        }

        public void ClearAllControlPoints()
        {
            for (var index = 0; index < entries.Count; index++)
            {
                entries[index].Rig.ClearTargets();
            }

            RefreshOverlays();
        }

        private bool TryBindCharacterSource()
        {
            var components = GetComponents<MonoBehaviour>();
            for (var index = 0; index < components.Length; index++)
            {
                if (!(components[index] is ICharacterModelSource source))
                {
                    continue;
                }

                BindCharacterSource(source);
                return true;
            }

            return false;
        }

        private void BindCharacterSource(ICharacterModelSource source)
        {
            if (ReferenceEquals(characterSource, source))
            {
                return;
            }

            if (characterSource != null)
            {
                characterSource.CharactersChanged -= SynchronizeCharacters;
            }

            characterSource = source;
            if (characterSource != null)
            {
                characterSource.CharactersChanged += SynchronizeCharacters;
            }
        }

        private void SynchronizeCharacters()
        {
            EndPointerDrag();
            var models = characterSource?.CharacterModels ??
                         Array.Empty<ICharacterModel>();
            for (var index = entries.Count - 1; index >= 0; index--)
            {
                if (ContainsModel(models, entries[index].Rig.Model))
                {
                    continue;
                }

                if (ReferenceEquals(selectedEntry, entries[index]))
                {
                    selectedEntry = null;
                    selectedPoint = null;
                    SelectionChanged?.Invoke();
                }

                entries[index].Dispose();
                entries.RemoveAt(index);
            }

            for (var index = 0; index < models.Count; index++)
            {
                var model = models[index];
                if (model == null || model.Root == null ||
                    model.PoseCoordinator == null ||
                    model.Skeleton == null ||
                    ContainsEntry(model))
                {
                    continue;
                }

                try
                {
                    var rig = new CharacterControlRig(model);
                    if (rig.ControlPoints.Count == 0)
                    {
                        rig.Dispose();
                        continue;
                    }

                    entries.Add(new RigEntry(
                        rig,
                        new CharacterControlPointOverlay(transform, rig)));
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                }
            }

            RefreshOverlays();
        }

        private bool TryPick(
            Ray ray,
            out RigEntry selected,
            out CharacterControlPoint point)
        {
            selected = null;
            point = default;
            var closestDistance = float.PositiveInfinity;
            for (var index = 0; index < entries.Count; index++)
            {
                if (!entries[index].Overlay.TryPick(
                        ray,
                        out var candidate,
                        out var distance) ||
                    distance >= closestDistance)
                {
                    continue;
                }

                closestDistance = distance;
                selected = entries[index];
                point = candidate;
            }

            return selected != null;
        }

        private void Select(RigEntry entry, CharacterControlPoint? point)
        {
            if (ReferenceEquals(selectedEntry, entry) && selectedPoint == point)
            {
                return;
            }

            selectedEntry = entry;
            selectedPoint = point;
            RefreshOverlays();
            SelectionChanged?.Invoke();
        }

        private void RefreshOverlays()
        {
            for (var index = 0; index < entries.Count; index++)
            {
                var selected = ReferenceEquals(selectedEntry, entries[index])
                    ? selectedPoint
                    : null;
                entries[index].Overlay.Refresh(selected);
            }
        }

        private bool ContainsEntry(ICharacterModel model)
        {
            for (var index = 0; index < entries.Count; index++)
            {
                if (ReferenceEquals(entries[index].Rig.Model, model))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsModel(
            IReadOnlyList<ICharacterModel> models,
            ICharacterModel model)
        {
            for (var index = 0; index < models.Count; index++)
            {
                if (ReferenceEquals(models[index], model))
                {
                    return true;
                }
            }

            return false;
        }

        private void OnDisable()
        {
            EndPointerDrag();
            BindCharacterSource(null);
            selectedEntry = null;
            selectedPoint = null;
            for (var index = entries.Count - 1; index >= 0; index--)
            {
                entries[index].Dispose();
            }

            entries.Clear();
        }

        private void OnDestroy()
        {
            SelectionChanged = null;
        }

        private enum ControlDragKind
        {
            None,
            FreeMove,
            AxisMove,
            Rotate,
        }

        private sealed class RigEntry : IDisposable
        {
            public RigEntry(
                CharacterControlRig rig,
                CharacterControlPointOverlay overlay)
            {
                Rig = rig;
                Overlay = overlay;
            }

            public CharacterControlRig Rig { get; }

            public CharacterControlPointOverlay Overlay { get; }

            public void Dispose()
            {
                Overlay.Dispose();
                Rig.Dispose();
            }
        }
    }

    internal enum CharacterManipulatorKind
    {
        TranslateAxis,
        RotateAxis,
    }

    internal readonly struct CharacterManipulatorHit
    {
        public CharacterManipulatorHit(
            CharacterManipulatorKind kind,
            Vector3 axis,
            float distance)
        {
            Kind = kind;
            Axis = axis;
            Distance = distance;
        }

        public CharacterManipulatorKind Kind { get; }

        public Vector3 Axis { get; }

        public float Distance { get; }
    }

    internal sealed class CharacterControlPointOverlay : IDisposable
    {
        private readonly CharacterControlRig rig;
        private readonly GameObject root;
        private readonly Mesh lineMesh;
        private readonly Material lineMaterial;
        private readonly Material inactiveMaterial;
        private readonly Material activeMaterial;
        private readonly Material selectedMaterial;
        private readonly CharacterTransformGizmo gizmo;
        private readonly Dictionary<CharacterControlPoint, HandleEntry> handles =
            new Dictionary<CharacterControlPoint, HandleEntry>();
        private readonly List<Vector3> lineVertices = new List<Vector3>();

        public CharacterControlPointOverlay(
            Transform parent,
            CharacterControlRig rig)
        {
            this.rig = rig ?? throw new ArgumentNullException(nameof(rig));
            root = new GameObject(rig.Model.DisplayName + " Control Points")
            {
                hideFlags = HideFlags.DontSave,
            };
            root.transform.SetParent(parent, false);

            var shader = Resources.Load<Shader>("BodyEditorOverlay") ??
                         Shader.Find("Universal Render Pipeline/Unlit") ??
                         Shader.Find("Unlit/Color");
            if (shader == null)
            {
                Debug.LogWarning("Character control point shader was not found.");
                return;
            }

            lineMaterial = CreateMaterial(
                shader,
                "Character Control Lines",
                new Color(0.25f, 0.72f, 0.92f, 0.9f),
                4110);
            inactiveMaterial = CreateMaterial(
                shader,
                "Inactive Character Controls",
                new Color(0.48f, 0.54f, 0.58f, 0.85f),
                4111);
            activeMaterial = CreateMaterial(
                shader,
                "Active Character Controls",
                new Color(0.12f, 0.78f, 0.7f, 1f),
                4112);
            selectedMaterial = CreateMaterial(
                shader,
                "Selected Character Control",
                new Color(1f, 0.63f, 0.12f, 1f),
                4113);

            var lineObject = new GameObject("Character Control Lines");
            lineObject.hideFlags = HideFlags.DontSave;
            lineObject.transform.SetParent(root.transform, false);
            lineMesh = new Mesh
            {
                name = "Character Control Line Mesh",
                hideFlags = HideFlags.DontSave,
            };
            lineObject.AddComponent<MeshFilter>().sharedMesh = lineMesh;
            ConfigureRenderer(
                lineObject.AddComponent<MeshRenderer>(),
                lineMaterial);

            for (var index = 0; index < rig.ControlPoints.Count; index++)
            {
                var point = rig.ControlPoints[index];
                var handle = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                handle.name = point + " Control";
                handle.hideFlags = HideFlags.DontSave;
                handle.transform.SetParent(root.transform, false);
                var collider = handle.GetComponent<Collider>();
                DestroyObject(collider);
                var renderer = handle.GetComponent<MeshRenderer>();
                ConfigureRenderer(renderer, inactiveMaterial);
                handles.Add(point, new HandleEntry(handle.transform, renderer));
            }

            gizmo = new CharacterTransformGizmo(root.transform, shader);
        }

        public void Refresh(CharacterControlPoint? selectedPoint)
        {
            if (root == null)
            {
                return;
            }

            lineVertices.Clear();
            var baseDiameter = Mathf.Clamp(
                rig.EstimatedHeight * 0.035f,
                0.025f,
                0.12f);
            foreach (var pair in handles)
            {
                if (!rig.TryGetControlPosition(pair.Key, out var position))
                {
                    pair.Value.Transform.gameObject.SetActive(false);
                    continue;
                }

                pair.Value.Transform.gameObject.SetActive(true);
                pair.Value.Transform.position = position;
                var selected = selectedPoint == pair.Key;
                var diameter = baseDiameter * (selected ? 1.35f : 1f);
                pair.Value.Transform.localScale = Vector3.one * diameter;
                pair.Value.Renderer.sharedMaterial = selected
                    ? selectedMaterial
                    : rig.IsActive(pair.Key)
                        ? activeMaterial
                        : inactiveMaterial;

                if (rig.IsActive(pair.Key) &&
                    rig.TryGetAnchorPosition(pair.Key, out var anchor))
                {
                    lineVertices.Add(root.transform.InverseTransformPoint(anchor));
                    lineVertices.Add(root.transform.InverseTransformPoint(position));
                }
            }

            if (selectedPoint.HasValue &&
                rig.TryGetControlPosition(
                    selectedPoint.Value,
                    out var selectedPosition))
            {
                gizmo?.Refresh(
                    selectedPosition,
                    baseDiameter,
                    rig.SupportsRotation(selectedPoint.Value));
            }
            else
            {
                gizmo?.Hide();
            }

            if (lineMesh == null)
            {
                return;
            }

            lineMesh.Clear(false);
            lineMesh.SetVertices(lineVertices);
            var indices = new int[lineVertices.Count];
            for (var index = 0; index < indices.Length; index++)
            {
                indices[index] = index;
            }

            lineMesh.SetIndices(indices, MeshTopology.Lines, 0);
        }

        public bool TryPick(
            Ray ray,
            out CharacterControlPoint point,
            out float distance)
        {
            point = default;
            distance = float.PositiveInfinity;
            var found = false;
            var direction = ray.direction.normalized;
            foreach (var pair in handles)
            {
                if (!pair.Value.Transform.gameObject.activeInHierarchy)
                {
                    continue;
                }

                var center = pair.Value.Transform.position;
                var radius = pair.Value.Transform.lossyScale.x * 0.55f;
                var originToCenter = center - ray.origin;
                var projection = Vector3.Dot(originToCenter, direction);
                if (projection < 0f)
                {
                    continue;
                }

                var closest = ray.origin + direction * projection;
                var squareOffset = (center - closest).sqrMagnitude;
                var squareRadius = radius * radius;
                if (squareOffset > squareRadius)
                {
                    continue;
                }

                var hitDistance = projection -
                                  Mathf.Sqrt(squareRadius - squareOffset);
                if (hitDistance >= distance)
                {
                    continue;
                }

                distance = Mathf.Max(0f, hitDistance);
                point = pair.Key;
                found = true;
            }

            return found;
        }

        public bool TryPickManipulator(
            Ray ray,
            out CharacterManipulatorHit hit)
        {
            if (gizmo != null)
            {
                return gizmo.TryPick(ray, out hit);
            }

            hit = default;
            return false;
        }

        public void Dispose()
        {
            DestroyObject(root);
            DestroyObject(lineMesh);
            DestroyObject(lineMaterial);
            DestroyObject(inactiveMaterial);
            DestroyObject(activeMaterial);
            DestroyObject(selectedMaterial);
            gizmo?.Dispose();
            handles.Clear();
            lineVertices.Clear();
        }

        private static Material CreateMaterial(
            Shader shader,
            string name,
            Color color,
            int renderQueue)
        {
            var material = new Material(shader)
            {
                name = name,
                hideFlags = HideFlags.DontSave,
                renderQueue = renderQueue,
            };
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            return material;
        }

        private static void ConfigureRenderer(Renderer renderer, Material material)
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
        }

        private static void DestroyObject(UnityEngine.Object value)
        {
            if (value == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(value);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(value);
            }
        }

        private sealed class CharacterTransformGizmo : IDisposable
        {
            private const int RingSegments = 64;
            private static readonly Vector3[] axes =
            {
                Vector3.right,
                Vector3.up,
                Vector3.forward,
            };

            private static readonly Color[] axisColors =
            {
                new Color(0.9f, 0.24f, 0.2f, 1f),
                new Color(0.28f, 0.78f, 0.3f, 1f),
                new Color(0.22f, 0.48f, 0.95f, 1f),
            };

            private readonly GameObject root;
            private readonly AxisVisual[] visuals = new AxisVisual[3];
            private Vector3 center;
            private float diameter;
            private float axisStart;
            private float axisLength;
            private float ringRadius;
            private bool rotationVisible;

            public CharacterTransformGizmo(Transform parent, Shader shader)
            {
                root = new GameObject("Selected Control Transform Gizmo")
                {
                    hideFlags = HideFlags.DontSave,
                };
                root.transform.SetParent(parent, false);

                for (var index = 0; index < axes.Length; index++)
                {
                    var material = CreateMaterial(
                        shader,
                        "Character Gizmo Axis " + index,
                        axisColors[index],
                        4120 + index);
                    var shaft = CreatePrimitive(
                        PrimitiveType.Cylinder,
                        "Translate Axis " + index,
                        root.transform,
                        material);
                    var cap = CreatePrimitive(
                        PrimitiveType.Cube,
                        "Translate Axis End " + index,
                        root.transform,
                        material);

                    var ringObject = new GameObject("Rotate Ring " + index)
                    {
                        hideFlags = HideFlags.DontSave,
                    };
                    ringObject.transform.SetParent(root.transform, false);
                    var ringMesh = BuildUnitRing(index);
                    ringObject.AddComponent<MeshFilter>().sharedMesh = ringMesh;
                    ConfigureRenderer(
                        ringObject.AddComponent<MeshRenderer>(),
                        material);
                    visuals[index] = new AxisVisual(
                        axes[index],
                        material,
                        shaft.transform,
                        cap.transform,
                        ringObject,
                        ringMesh);
                }

                Hide();
            }

            public void Refresh(
                Vector3 position,
                float controlDiameter,
                bool supportsRotation)
            {
                center = position;
                diameter = controlDiameter;
                axisStart = diameter * 0.9f;
                axisLength = diameter * 3.8f;
                ringRadius = diameter * 2.25f;
                rotationVisible = supportsRotation;
                root.SetActive(true);
                root.transform.SetPositionAndRotation(
                    center,
                    Quaternion.identity);

                var shaftLength = axisLength - axisStart;
                for (var index = 0; index < visuals.Length; index++)
                {
                    var visual = visuals[index];
                    visual.Shaft.localPosition = visual.Axis *
                                                 (axisStart +
                                                  shaftLength * 0.5f);
                    visual.Shaft.localRotation = Quaternion.FromToRotation(
                        Vector3.up,
                        visual.Axis);
                    visual.Shaft.localScale = new Vector3(
                        diameter * 0.075f,
                        shaftLength * 0.5f,
                        diameter * 0.075f);
                    visual.Cap.localPosition = visual.Axis * axisLength;
                    visual.Cap.localRotation = Quaternion.identity;
                    visual.Cap.localScale = Vector3.one * diameter * 0.28f;
                    visual.Ring.SetActive(rotationVisible);
                    visual.Ring.transform.localPosition = Vector3.zero;
                    visual.Ring.transform.localRotation = Quaternion.identity;
                    visual.Ring.transform.localScale = Vector3.one * ringRadius;
                }
            }

            public void Hide()
            {
                if (root != null)
                {
                    root.SetActive(false);
                }
            }

            public bool TryPick(Ray ray, out CharacterManipulatorHit hit)
            {
                hit = default;
                if (root == null || !root.activeInHierarchy)
                {
                    return false;
                }

                var found = false;
                var closestDistance = float.PositiveInfinity;
                var axisPickRadius = diameter * 0.28f;
                for (var index = 0; index < visuals.Length; index++)
                {
                    var axis = visuals[index].Axis;
                    if (TryDistanceToSegment(
                            ray,
                            center + axis * axisStart,
                            axis,
                            axisLength - axisStart,
                            out var axisDistance,
                            out var separation) &&
                        separation <= axisPickRadius &&
                        axisDistance < closestDistance)
                    {
                        closestDistance = axisDistance;
                        hit = new CharacterManipulatorHit(
                            CharacterManipulatorKind.TranslateAxis,
                            axis,
                            axisDistance);
                        found = true;
                    }

                    if (!rotationVisible)
                    {
                        continue;
                    }

                    var plane = new Plane(axis, center);
                    if (!plane.Raycast(ray, out var ringDistance))
                    {
                        continue;
                    }

                    var radius = Vector3.Distance(
                        ray.GetPoint(ringDistance),
                        center);
                    if (Mathf.Abs(radius - ringRadius) > diameter * 0.24f ||
                        ringDistance >= closestDistance)
                    {
                        continue;
                    }

                    closestDistance = ringDistance;
                    hit = new CharacterManipulatorHit(
                        CharacterManipulatorKind.RotateAxis,
                        axis,
                        ringDistance);
                    found = true;
                }

                return found;
            }

            public void Dispose()
            {
                for (var index = 0; index < visuals.Length; index++)
                {
                    DestroyObject(visuals[index].RingMesh);
                    DestroyObject(visuals[index].Material);
                }

                DestroyObject(root);
            }

            private static GameObject CreatePrimitive(
                PrimitiveType type,
                string name,
                Transform parent,
                Material material)
            {
                var value = GameObject.CreatePrimitive(type);
                value.name = name;
                value.hideFlags = HideFlags.DontSave;
                value.transform.SetParent(parent, false);
                DestroyObject(value.GetComponent<Collider>());
                ConfigureRenderer(value.GetComponent<MeshRenderer>(), material);
                return value;
            }

            private static Mesh BuildUnitRing(int axisIndex)
            {
                var vertices = new List<Vector3>(RingSegments * 2);
                for (var index = 0; index < RingSegments; index++)
                {
                    var firstAngle = index * Mathf.PI * 2f / RingSegments;
                    var secondAngle = (index + 1) * Mathf.PI * 2f /
                                      RingSegments;
                    vertices.Add(RingPoint(axisIndex, firstAngle));
                    vertices.Add(RingPoint(axisIndex, secondAngle));
                }

                var mesh = new Mesh
                {
                    name = "Character Gizmo Rotation Ring " + axisIndex,
                    hideFlags = HideFlags.DontSave,
                };
                mesh.SetVertices(vertices);
                var indices = new int[vertices.Count];
                for (var index = 0; index < indices.Length; index++)
                {
                    indices[index] = index;
                }

                mesh.SetIndices(indices, MeshTopology.Lines, 0);
                return mesh;
            }

            private static Vector3 RingPoint(int axisIndex, float angle)
            {
                var first = Mathf.Cos(angle);
                var second = Mathf.Sin(angle);
                switch (axisIndex)
                {
                    case 0:
                        return new Vector3(0f, first, second);
                    case 1:
                        return new Vector3(first, 0f, second);
                    default:
                        return new Vector3(first, second, 0f);
                }
            }

            private static bool TryDistanceToSegment(
                Ray ray,
                Vector3 segmentStart,
                Vector3 segmentDirection,
                float segmentLength,
                out float rayDistance,
                out float separation)
            {
                var rayDirection = ray.direction.normalized;
                var delta = ray.origin - segmentStart;
                var directionDot = Vector3.Dot(
                    rayDirection,
                    segmentDirection);
                var rayDeltaDot = Vector3.Dot(rayDirection, delta);
                var segmentDeltaDot = Vector3.Dot(segmentDirection, delta);
                var denominator = 1f - directionDot * directionDot;
                var segmentDistance = denominator > 0.000001f
                    ? (segmentDeltaDot - directionDot * rayDeltaDot) /
                      denominator
                    : 0f;
                segmentDistance = Mathf.Clamp(
                    segmentDistance,
                    0f,
                    segmentLength);
                var segmentPoint = segmentStart +
                                   segmentDirection * segmentDistance;
                rayDistance = Vector3.Dot(
                    segmentPoint - ray.origin,
                    rayDirection);
                if (rayDistance < 0f)
                {
                    separation = float.PositiveInfinity;
                    return false;
                }

                separation = Vector3.Distance(
                    ray.origin + rayDirection * rayDistance,
                    segmentPoint);
                return true;
            }

            private readonly struct AxisVisual
            {
                public AxisVisual(
                    Vector3 axis,
                    Material material,
                    Transform shaft,
                    Transform cap,
                    GameObject ring,
                    Mesh ringMesh)
                {
                    Axis = axis;
                    Material = material;
                    Shaft = shaft;
                    Cap = cap;
                    Ring = ring;
                    RingMesh = ringMesh;
                }

                public Vector3 Axis { get; }
                public Material Material { get; }
                public Transform Shaft { get; }
                public Transform Cap { get; }
                public GameObject Ring { get; }
                public Mesh RingMesh { get; }
            }
        }

        private readonly struct HandleEntry
        {
            public HandleEntry(Transform transform, MeshRenderer renderer)
            {
                Transform = transform;
                Renderer = renderer;
            }

            public Transform Transform { get; }

            public MeshRenderer Renderer { get; }
        }
    }
}
