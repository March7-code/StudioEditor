using System.Collections.Generic;
using BodyEditor.Editing;
using UnityEngine;
using UnityEngine.UIElements;

namespace BodyEditor.UI
{
    internal sealed class BodySkeletonSidebar : VisualElement
    {
        private readonly EditableSkeletonController controller;
        private readonly Label selectedBoneLabel;
        private readonly Toggle symmetryToggle;
        private readonly FloatField positionX;
        private readonly FloatField positionY;
        private readonly FloatField positionZ;
        private readonly VisualElement positionFields;
        private readonly Button undoButton;
        private readonly Button redoButton;
        private readonly Button resetButton;
        private readonly Dictionary<HumanBodyBones, Button> boneButtons =
            new Dictionary<HumanBodyBones, Button>();

        public BodySkeletonSidebar(EditableSkeletonController controller)
        {
            this.controller = controller;
            name = "body-skeleton-sidebar";
            AddToClassList("body-skeleton-sidebar");

            var heading = new Label("Editable Body");
            heading.AddToClassList("body-sidebar__heading");
            Add(heading);

            var subtitle = new Label("Default humanoid skeleton");
            subtitle.AddToClassList("body-sidebar__subtitle");
            Add(subtitle);

            symmetryToggle = new Toggle("Symmetry")
            {
                tooltip = "Mirror paired joint positions across the skeleton X axis",
            };
            symmetryToggle.AddToClassList("body-sidebar__symmetry");
            symmetryToggle.RegisterValueChangedCallback(
                value => controller.SetSymmetryEnabled(value.newValue));
            Add(symmetryToggle);

            var editHeading = new Label("Joint position");
            editHeading.AddToClassList("body-sidebar__section-heading");
            Add(editHeading);

            selectedBoneLabel = new Label("No joint selected");
            selectedBoneLabel.AddToClassList("body-sidebar__selection");
            Add(selectedBoneLabel);

            positionFields = new VisualElement();
            positionFields.AddToClassList("body-sidebar__position-fields");
            positionX = CreatePositionField("X", HandlePositionXChanged);
            positionY = CreatePositionField("Y", HandlePositionYChanged);
            positionZ = CreatePositionField("Z", HandlePositionZChanged);
            positionFields.Add(positionX);
            positionFields.Add(positionY);
            positionFields.Add(positionZ);
            Add(positionFields);

            var commands = new VisualElement();
            commands.AddToClassList("body-sidebar__commands");
            undoButton = CreateCommandButton("Undo", controller.Undo);
            redoButton = CreateCommandButton("Redo", controller.Redo);
            resetButton = CreateCommandButton("Reset", controller.ResetPose);
            commands.Add(undoButton);
            commands.Add(redoButton);
            commands.Add(resetButton);
            Add(commands);

            var bonesHeading = new Label("Joints");
            bonesHeading.AddToClassList("body-sidebar__section-heading");
            Add(bonesHeading);

            var boneList = new ScrollView(ScrollViewMode.Vertical);
            boneList.AddToClassList("body-sidebar__bone-list");
            for (var index = 0;
                 index < HumanoidSkeletonSchema.DefaultDefinitions.Count;
                 index++)
            {
                var bone = HumanoidSkeletonSchema.DefaultDefinitions[index].Bone;
                var button = new Button(() => controller.SelectBone(bone))
                {
                    text = FormatBoneName(bone.ToString()),
                    tooltip = bone.ToString(),
                };
                button.AddToClassList("body-sidebar__bone");
                boneButtons.Add(bone, button);
                boneList.Add(button);
            }

            Add(boneList);

            controller.StateChanged += Refresh;
            RegisterCallback<DetachFromPanelEvent>(HandleDetach);
            Refresh();
        }

        private void Refresh()
        {
            symmetryToggle.SetValueWithoutNotify(controller.SymmetryEnabled);
            undoButton.SetEnabled(controller.CanUndo);
            redoButton.SetEnabled(controller.CanRedo);
            resetButton.SetEnabled(controller.CanReset);
            undoButton.tooltip = controller.CanUndo
                ? $"Undo {controller.UndoDescription} (Ctrl+Z)"
                : "Nothing to undo";
            redoButton.tooltip = controller.CanRedo
                ? $"Redo {controller.RedoDescription} (Ctrl+Y or Ctrl+Shift+Z)"
                : "Nothing to redo";

            var selected = controller.SelectedBone;
            selectedBoneLabel.text = selected.HasValue
                ? FormatBoneName(selected.Value.ToString())
                : "No joint selected";
            positionFields.SetEnabled(selected.HasValue);

            if (selected.HasValue &&
                controller.TryGetJointRootPosition(selected.Value, out var position))
            {
                positionX.SetValueWithoutNotify(position.x);
                positionY.SetValueWithoutNotify(position.y);
                positionZ.SetValueWithoutNotify(position.z);
            }
            else
            {
                positionX.SetValueWithoutNotify(0f);
                positionY.SetValueWithoutNotify(0f);
                positionZ.SetValueWithoutNotify(0f);
            }

            foreach (var pair in boneButtons)
            {
                pair.Value.EnableInClassList(
                    "body-sidebar__bone--selected",
                    selected == pair.Key);
            }
        }

        private FloatField CreatePositionField(
            string label,
            EventCallback<ChangeEvent<float>> callback)
        {
            var field = new FloatField(label)
            {
                isDelayed = true,
            };
            field.AddToClassList("body-sidebar__position-field");
            field.RegisterValueChangedCallback(callback);
            return field;
        }

        private static Button CreateCommandButton(string text, System.Action action)
        {
            var button = new Button(action)
            {
                text = text,
            };
            button.AddToClassList("body-sidebar__command");
            return button;
        }

        private void HandlePositionXChanged(ChangeEvent<float> changeEvent)
        {
            SetSelectedPosition(0, changeEvent.newValue);
        }

        private void HandlePositionYChanged(ChangeEvent<float> changeEvent)
        {
            SetSelectedPosition(1, changeEvent.newValue);
        }

        private void HandlePositionZChanged(ChangeEvent<float> changeEvent)
        {
            SetSelectedPosition(2, changeEvent.newValue);
        }

        private void SetSelectedPosition(int axis, float value)
        {
            if (!controller.SelectedBone.HasValue ||
                !controller.TryGetJointRootPosition(
                    controller.SelectedBone.Value,
                    out var position))
            {
                return;
            }

            position[axis] = value;
            controller.SetJointRootPosition(controller.SelectedBone.Value, position);
        }

        private void HandleDetach(DetachFromPanelEvent detachEvent)
        {
            controller.StateChanged -= Refresh;
        }

        private static string FormatBoneName(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var result = new System.Text.StringBuilder(value.Length + 4);
            result.Append(value[0]);
            for (var index = 1; index < value.Length; index++)
            {
                if (char.IsUpper(value[index]) && !char.IsUpper(value[index - 1]))
                {
                    result.Append(' ');
                }

                result.Append(value[index]);
            }

            return result.ToString();
        }
    }
}
