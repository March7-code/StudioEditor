using System;
using System.Collections.Generic;
using StudioEditor.Characters;
using StudioEditor.ReferenceModels;
using UnityEngine;
using UnityEngine.UIElements;

namespace StudioEditor.UI
{
    internal sealed class CharacterAnimationPanel : VisualElement
    {
        private static readonly CharacterKinematicGroups[] ikGroups =
        {
            CharacterKinematicGroups.Body,
            CharacterKinematicGroups.RightLeg,
            CharacterKinematicGroups.LeftLeg,
            CharacterKinematicGroups.RightHand,
            CharacterKinematicGroups.LeftHand,
        };

        private static readonly CharacterKinematicGroups[] fkGroups =
        {
            CharacterKinematicGroups.Hair,
            CharacterKinematicGroups.Neck,
            CharacterKinematicGroups.Breast,
            CharacterKinematicGroups.Body,
            CharacterKinematicGroups.RightHand,
            CharacterKinematicGroups.LeftHand,
            CharacterKinematicGroups.Skirt,
        };

        private readonly SceneContentController importController;
        private readonly List<ICharacterModel> characters =
            new List<ICharacterModel>();
        private readonly Button launcher;
        private readonly VisualElement menu;
        private readonly VisualElement surface;
        private readonly Label surfaceTitle;
        private readonly VisualElement facialView;
        private readonly VisualElement handView;
        private readonly VisualElement animationView;
        private readonly Button replaceButton;
        private readonly FacialSection eyebrowSection;
        private readonly FacialSection eyeSection;
        private readonly FacialSection mouthSection;
        private readonly HandSection leftHandSection;
        private readonly HandSection rightHandSection;
        private readonly ModeSection ikSection;
        private readonly ModeSection fkSection;
        private readonly Label emptyLabel;

        private int selectedCharacterIndex;
        private bool menuOpen;
        private bool controlsOpen;
        private bool refreshingFacialSettings;
        private bool refreshingHandSettings;
        private ChildView activeChildView;

        public CharacterAnimationPanel(
            SceneContentController importController,
            VisualElement launcherParent)
        {
            this.importController = importController ??
                throw new ArgumentNullException(nameof(importController));
            if (launcherParent == null)
            {
                throw new ArgumentNullException(nameof(launcherParent));
            }

            name = "character-animation-host";
            pickingMode = PickingMode.Ignore;
            AddToClassList("character-animation-host");

            launcher = new Button(ToggleMenu)
            {
                text = "P",
                tooltip = "Open character animation controls",
                pickingMode = PickingMode.Position,
            };
            launcher.AddToClassList("character-animation-launcher");
            launcher.AddToClassList("workspace-tools__button");
            launcherParent.Add(launcher);

            menu = new VisualElement
            {
                pickingMode = PickingMode.Position,
            };
            menu.AddToClassList("character-animation-menu");

            replaceButton = new Button(PickAndReplaceCharacter)
            {
                text = "Replace Character...",
                tooltip = "Replace the selected character",
            };
            replaceButton.AddToClassList("character-animation-menu__item");
            menu.Add(replaceButton);

            menu.Add(CreateMenuItem(
                "Animation Control  >",
                "Open IK and FK controls",
                ChildView.Animation));
            menu.Add(CreateMenuItem(
                "Facial Control  >",
                "Open mouth, eye, and eyebrow controls",
                ChildView.Facial));
            menu.Add(CreateMenuItem(
                "Hand Control  >",
                "Open left and right hand shape controls",
                ChildView.Hands));
            Add(menu);

            surface = new VisualElement
            {
                pickingMode = PickingMode.Position,
            };
            surface.AddToClassList("character-animation-panel");

            var header = new VisualElement();
            header.AddToClassList("character-animation-panel__header");
            var backButton = CreateHeaderButton(
                "<",
                "Back",
                () => SetControlsOpen(false));
            header.Add(backButton);
            surfaceTitle = new Label();
            surfaceTitle.AddToClassList("character-animation-panel__title");
            header.Add(surfaceTitle);
            var closeButton = CreateHeaderButton(
                "x",
                "Close",
                () => SetMenuOpen(false));
            header.Add(closeButton);
            surface.Add(header);

            eyebrowSection = CreateFacialSection(
                "Eyebrows",
                FacialControlKind.Eyebrows,
                false,
                "Amount");
            eyeSection = CreateFacialSection(
                "Eyes",
                FacialControlKind.Eyes,
                true,
                "Open");
            mouthSection = CreateFacialSection(
                "Mouth",
                FacialControlKind.Mouth,
                false,
                "Open");
            facialView = new ScrollView(ScrollViewMode.Vertical);
            facialView.AddToClassList(
                "character-animation-panel__child-view");
            facialView.AddToClassList(
                "character-animation-panel__facial-view");
            facialView.Add(mouthSection.Root);
            facialView.Add(eyeSection.Root);
            facialView.Add(eyebrowSection.Root);
            surface.Add(facialView);

            leftHandSection = CreateHandSection(
                "Left Hand",
                CharacterHand.Left);
            rightHandSection = CreateHandSection(
                "Right Hand",
                CharacterHand.Right);
            handView = new ScrollView(ScrollViewMode.Vertical);
            handView.AddToClassList(
                "character-animation-panel__child-view");
            handView.AddToClassList(
                "character-animation-panel__facial-view");
            handView.Add(leftHandSection.Root);
            handView.Add(rightHandSection.Root);
            surface.Add(handView);

            animationView = new VisualElement();
            animationView.AddToClassList(
                "character-animation-panel__child-view");
            emptyLabel = new Label("No animation controls");
            emptyLabel.AddToClassList("character-animation-panel__empty");
            animationView.Add(emptyLabel);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("character-animation-panel__scroll");
            ikSection = CreateModeSection(
                "IK",
                CharacterKinematicMode.InverseKinematics,
                ikGroups);
            fkSection = CreateModeSection(
                "FK",
                CharacterKinematicMode.ForwardKinematics,
                fkGroups);
            scroll.Add(ikSection.Root);
            scroll.Add(fkSection.Root);
            animationView.Add(scroll);
            surface.Add(animationView);
            Add(surface);

            importController.StateChanged += RefreshCharacters;
            RegisterCallback<DetachFromPanelEvent>(HandleDetach);
            schedule.Execute(UpdateFollowTarget).Every(100);
            SetMenuOpen(false);
            RefreshCharacters();
        }

        private static Button CreateHeaderButton(
            string text,
            string tooltip,
            Action action)
        {
            var button = new Button(action)
            {
                text = text,
                tooltip = tooltip,
            };
            button.AddToClassList("character-animation-panel__header-button");
            return button;
        }

        private Button CreateMenuItem(
            string text,
            string tooltip,
            ChildView view)
        {
            var button = new Button(() => OpenChild(view))
            {
                text = text,
                tooltip = tooltip,
            };
            button.AddToClassList("character-animation-menu__item");
            return button;
        }

        private ModeSection CreateModeSection(
            string title,
            CharacterKinematicMode mode,
            IReadOnlyList<CharacterKinematicGroups> groups)
        {
            var root = new VisualElement();
            root.AddToClassList("character-animation-panel__section");

            var heading = new VisualElement();
            heading.AddToClassList("character-animation-panel__section-heading");
            var label = new Label(title);
            label.AddToClassList("character-animation-panel__section-title");
            heading.Add(label);

            var master = new Toggle("Enabled");
            master.AddToClassList("character-animation-panel__master");
            master.RegisterValueChangedCallback(changeEvent =>
                HandleModeChanged(mode, changeEvent.newValue));
            heading.Add(master);
            root.Add(heading);

            var groupGrid = new VisualElement();
            groupGrid.AddToClassList("character-animation-panel__groups");
            var toggles = new List<GroupToggle>(groups.Count);
            for (var index = 0; index < groups.Count; index++)
            {
                var group = groups[index];
                var toggle = new Toggle(GetGroupName(group));
                toggle.AddToClassList("character-animation-panel__group");
                toggle.RegisterValueChangedCallback(changeEvent =>
                    HandleGroupChanged(mode, group, changeEvent.newValue));
                groupGrid.Add(toggle);
                toggles.Add(new GroupToggle(group, toggle));
            }

            root.Add(groupGrid);
            return new ModeSection(root, master, toggles);
        }

        private FacialSection CreateFacialSection(
            string title,
            FacialControlKind kind,
            bool followTarget,
            string openLabel)
        {
            var root = new VisualElement();
            root.AddToClassList("character-animation-menu__face-section");

            var heading = new Label(title);
            heading.AddToClassList(
                "character-animation-menu__face-section-title");
            root.Add(heading);

            var pattern = new DropdownField("Shape");
            pattern.AddToClassList(
                "character-animation-menu__face-field");
            root.Add(pattern);

            var open = new Slider(openLabel, 0f, 1f)
            {
                showInputField = true,
            };
            open.AddToClassList("character-animation-menu__face-slider");
            root.Add(open);

            Toggle follow = null;
            if (followTarget)
            {
                follow = new Toggle("Follow view");
                follow.AddToClassList(
                    "character-animation-menu__face-toggle");
                root.Add(follow);
            }

            var result = new FacialSection(root, kind, pattern, open, follow);
            pattern.RegisterValueChangedCallback(changeEvent =>
                HandleFacialPatternChanged(result, changeEvent));
            open.RegisterValueChangedCallback(changeEvent =>
                HandleFacialOpenChanged(result, changeEvent.newValue));
            if (follow != null)
            {
                follow.RegisterValueChangedCallback(changeEvent =>
                    HandleFacialFollowChanged(changeEvent.newValue));
            }

            return result;
        }

        private HandSection CreateHandSection(
            string title,
            CharacterHand hand)
        {
            var root = new VisualElement();
            root.AddToClassList("character-animation-menu__face-section");

            var heading = new Label(title);
            heading.AddToClassList(
                "character-animation-menu__face-section-title");
            root.Add(heading);

            var shape = new DropdownField("Shape");
            shape.AddToClassList(
                "character-animation-menu__face-field");
            root.Add(shape);

            var amount = new Slider("Amount", 0f, 1f)
            {
                showInputField = true,
            };
            amount.AddToClassList(
                "character-animation-menu__face-slider");
            root.Add(amount);

            var result = new HandSection(root, hand, shape, amount);
            shape.RegisterValueChangedCallback(changeEvent =>
                HandleHandShapeChanged(result, changeEvent));
            amount.RegisterValueChangedCallback(changeEvent =>
                HandleHandAmountChanged(result, changeEvent.newValue));
            return result;
        }

        private void ToggleMenu()
        {
            SetMenuOpen(!menuOpen);
        }

        private void SetMenuOpen(bool value)
        {
            menuOpen = value && GetSelectedCharacter() != null;
            menu.style.display = menuOpen
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            launcher.EnableInClassList(
                "character-animation-launcher--open",
                menuOpen);
            if (!menuOpen)
            {
                SetControlsOpen(false);
            }
        }

        private void OpenChild(ChildView view)
        {
            activeChildView = view;
            SetControlsOpen(true);
        }

        private void SetControlsOpen(bool value)
        {
            controlsOpen = value && menuOpen;
            surface.style.display = controlsOpen
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            if (controlsOpen)
            {
                RefreshChildView();
            }
        }

        private void RefreshChildView()
        {
            animationView.style.display = activeChildView == ChildView.Animation
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            facialView.style.display = activeChildView == ChildView.Facial
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            handView.style.display = activeChildView == ChildView.Hands
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            surfaceTitle.text = GetChildTitle(activeChildView);

            if (activeChildView == ChildView.Animation)
            {
                RefreshSettings();
            }
            else if (activeChildView == ChildView.Facial)
            {
                RefreshFacialSettings();
            }
            else if (activeChildView == ChildView.Hands)
            {
                RefreshHandSettings();
            }
        }

        private static string GetChildTitle(ChildView view)
        {
            switch (view)
            {
                case ChildView.Animation: return "Animation Control";
                case ChildView.Facial: return "Facial Control";
                case ChildView.Hands: return "Hand Control";
                default: return "Person";
            }
        }

        private void RefreshCharacters()
        {
            var selected = GetSelectedCharacter();
            characters.Clear();
            var models = importController.CharacterModels;
            for (var index = 0; index < models.Count; index++)
            {
                if (models[index] != null)
                {
                    characters.Add(models[index]);
                }
            }

            selectedCharacterIndex = selected != null
                ? characters.IndexOf(selected)
                : -1;

            var hasSelection = selectedCharacterIndex >= 0;
            launcher.SetEnabled(hasSelection);
            replaceButton.SetEnabled(
                hasSelection &&
                importController.Status != ReferenceModelImportStatus.Loading);
            if (!hasSelection)
            {
                SetMenuOpen(false);
            }

            RefreshFacialSettings();
            RefreshHandSettings();
            RefreshSettings();
        }

        private async void PickAndReplaceCharacter()
        {
            var character = GetSelectedCharacter();
            if (character == null)
            {
                return;
            }

            var extensions = CollectCharacterExtensions();
            if (!WindowsModelFilePicker.TryPick(
                    extensions,
                    out var filePath,
                    out var pickerError,
                    "Replace Character",
                    "Character Models"))
            {
                if (!string.IsNullOrEmpty(pickerError))
                {
                    replaceButton.tooltip = pickerError;
                    Debug.LogError($"[Studio Editor] {pickerError}");
                }

                return;
            }

            replaceButton.tooltip = "Replacing character...";
            replaceButton.SetEnabled(false);
            var replaced = await importController.ReplaceCharacterAsync(
                character,
                filePath);

            replaceButton.tooltip = replaced
                ? "Replace the selected character"
                : string.IsNullOrWhiteSpace(importController.Error)
                    ? "Character replacement failed"
                    : importController.Error;
            RefreshCharacters();
        }

        private List<string> CollectCharacterExtensions()
        {
            var result = new List<string>();
            var adapters = importController.Adapters;
            for (var adapterIndex = 0;
                 adapterIndex < adapters.Count;
                 adapterIndex++)
            {
                if (adapters[adapterIndex] is IReferenceSceneFormatAdapter)
                {
                    continue;
                }

                var extensions = adapters[adapterIndex].FileExtensions;
                for (var extensionIndex = 0;
                     extensionIndex < extensions.Count;
                     extensionIndex++)
                {
                    if (!result.Contains(extensions[extensionIndex]))
                    {
                        result.Add(extensions[extensionIndex]);
                    }
                }
            }

            return result;
        }

        internal void SetSelectedSceneNode(IReferenceSceneNode node)
        {
            selectedCharacterIndex = -1;
            if (node != null &&
                node.Kind == ReferenceSceneObjectKind.Character &&
                node.Root != null)
            {
                for (var index = 0; index < characters.Count; index++)
                {
                    if (ReferenceEquals(characters[index].Root, node.Root))
                    {
                        selectedCharacterIndex = index;
                        break;
                    }
                }
            }

            var hasSelection = selectedCharacterIndex >= 0;
            launcher.SetEnabled(hasSelection);
            replaceButton.SetEnabled(
                hasSelection &&
                importController.Status != ReferenceModelImportStatus.Loading);
            if (!hasSelection)
            {
                SetMenuOpen(false);
                return;
            }

            RefreshFacialSettings();
            RefreshHandSettings();
            RefreshSettings();
        }

        private void HandleHandShapeChanged(
            HandSection section,
            ChangeEvent<string> changeEvent)
        {
            if (refreshingHandSettings)
            {
                return;
            }

            var controller = GetSelectedCharacter()?.Controls?.Hands;
            if (controller == null)
            {
                return;
            }

            var pose = section.Shape.index - 1;
            try
            {
                if (pose < 0)
                {
                    controller.ClearPose(section.Hand);
                }
                else
                {
                    controller.SetPose(
                        section.Hand,
                        pose,
                        controller.GetWeight(section.Hand));
                }

                RefreshHandSettings();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                RefreshHandSettings();
            }
        }

        private void HandleHandAmountChanged(
            HandSection section,
            float value)
        {
            if (refreshingHandSettings)
            {
                return;
            }

            var controller = GetSelectedCharacter()?.Controls?.Hands;
            if (controller == null)
            {
                return;
            }

            var pose = controller.GetPose(section.Hand);
            if (pose < 0)
            {
                return;
            }

            try
            {
                controller.SetPose(section.Hand, pose, value);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                RefreshHandSettings();
            }
        }

        private void HandleFacialPatternChanged(
            FacialSection section,
            ChangeEvent<string> changeEvent)
        {
            if (refreshingFacialSettings)
            {
                return;
            }

            var controller = GetFacialController(section.Kind);
            var pattern = section.Pattern.choices.IndexOf(
                changeEvent.newValue);
            if (controller == null || pattern < 0)
            {
                return;
            }

            try
            {
                controller.SetPattern(pattern);
                section.Open.SetValueWithoutNotify(controller.OpenRate);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                RefreshFacialSettings();
            }
        }

        private void HandleFacialOpenChanged(
            FacialSection section,
            float value)
        {
            if (refreshingFacialSettings)
            {
                return;
            }

            var controller = GetFacialController(section.Kind);
            if (controller == null)
            {
                return;
            }

            try
            {
                if (section.Kind == FacialControlKind.Mouth &&
                    controller is ICharacterMouthController mouth)
                {
                    mouth.SetFixedOpenRate(value);
                }
                else
                {
                    controller.SetOpenRate(value);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                RefreshFacialSettings();
            }
        }

        private void HandleFacialFollowChanged(bool enabled)
        {
            if (refreshingFacialSettings)
            {
                return;
            }

            var look = GetSelectedCharacter()?.Controls?.Eyes?.Look;
            if (look == null)
            {
                return;
            }

            if (enabled)
            {
                var camera = Camera.main;
                look.SetTarget(camera != null ? camera.transform : null);
            }

            look.SetFollowTarget(enabled);
            RefreshFacialSettings();
        }

        private void UpdateFollowTarget()
        {
            var look = GetSelectedCharacter()?.Controls?.Eyes?.Look;
            if (look == null || !look.IsFollowingTarget)
            {
                return;
            }

            var camera = Camera.main;
            if (camera != null &&
                !ReferenceEquals(look.Target, camera.transform))
            {
                look.SetTarget(camera.transform);
            }
        }

        private ICharacterPatternController GetFacialController(
            FacialControlKind kind)
        {
            var controls = GetSelectedCharacter()?.Controls;
            if (controls == null)
            {
                return null;
            }

            switch (kind)
            {
                case FacialControlKind.Eyebrows:
                    return controls.Eyebrows;
                case FacialControlKind.Eyes:
                    return controls.Eyes?.Open;
                case FacialControlKind.Mouth:
                    return controls.Mouth;
                default:
                    return null;
            }
        }

        private void RefreshFacialSettings()
        {
            refreshingFacialSettings = true;
            try
            {
                RefreshFacialSection(eyebrowSection);
                RefreshFacialSection(eyeSection);
                RefreshFacialSection(mouthSection);

                var look = GetSelectedCharacter()?.Controls?.Eyes?.Look;
                eyeSection.Follow?.SetValueWithoutNotify(
                    look != null && look.IsFollowingTarget);
                eyeSection.Follow?.SetEnabled(look != null);
            }
            finally
            {
                refreshingFacialSettings = false;
            }
        }

        private void RefreshFacialSection(FacialSection section)
        {
            var controller = GetFacialController(section.Kind);
            section.Root.SetEnabled(controller != null);
            if (controller == null || controller.PatternCount == 0)
            {
                section.Pattern.choices = new List<string>();
                section.Pattern.SetValueWithoutNotify(string.Empty);
                section.Open.SetValueWithoutNotify(0f);
                return;
            }

            var choices = new List<string>(controller.PatternCount);
            for (var index = 0; index < controller.PatternCount; index++)
            {
                choices.Add(controller.GetPatternName(index));
            }

            section.Pattern.choices = choices;
            section.Pattern.SetValueWithoutNotify(
                choices[Mathf.Clamp(
                    controller.Pattern,
                    0,
                    choices.Count - 1)]);
            section.Open.SetValueWithoutNotify(
                Mathf.Clamp01(controller.OpenRate));
        }

        private void RefreshHandSettings()
        {
            refreshingHandSettings = true;
            try
            {
                var controller = GetSelectedCharacter()?.Controls?.Hands;
                RefreshHandSection(controller, leftHandSection);
                RefreshHandSection(controller, rightHandSection);
            }
            finally
            {
                refreshingHandSettings = false;
            }
        }

        private static void RefreshHandSection(
            ICharacterHandPoseController controller,
            HandSection section)
        {
            section.Root.SetEnabled(controller != null);
            if (controller == null)
            {
                section.Shape.choices = new List<string>();
                section.Shape.SetValueWithoutNotify(string.Empty);
                section.Amount.SetValueWithoutNotify(0f);
                section.Amount.SetEnabled(false);
                return;
            }

            var poseCount = controller.GetPoseCount(section.Hand);
            var choices = new List<string>(poseCount + 1)
            {
                "None",
            };
            for (var index = 0; index < poseCount; index++)
            {
                var name = controller.GetPoseName(section.Hand, index);
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = $"Shape {index + 1}";
                }

                var uniqueName = name;
                var suffix = 2;
                while (choices.Contains(uniqueName))
                {
                    uniqueName = $"{name} ({suffix++})";
                }

                choices.Add(uniqueName);
            }

            var pose = controller.GetPose(section.Hand);
            section.Shape.choices = choices;
            section.Shape.SetValueWithoutNotify(
                pose >= 0 && pose < poseCount
                    ? choices[pose + 1]
                    : choices[0]);
            section.Amount.SetValueWithoutNotify(
                Mathf.Clamp01(controller.GetWeight(section.Hand)));
            section.Amount.SetEnabled(pose >= 0 && pose < poseCount);
        }

        private void HandleModeChanged(
            CharacterKinematicMode mode,
            bool enabled)
        {
            var controller = GetSelectedController();
            if (controller == null)
            {
                return;
            }

            try
            {
                controller.SetKinematicModeActive(mode, enabled);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }

            RefreshSettings();
        }

        private void HandleGroupChanged(
            CharacterKinematicMode mode,
            CharacterKinematicGroups group,
            bool active)
        {
            var controller = GetSelectedController();
            if (controller == null)
            {
                return;
            }

            try
            {
                controller.SetGroupActive(mode, group, active);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private void RefreshSettings()
        {
            var controller = GetSelectedController();
            emptyLabel.style.display = controller == null
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            ikSection.Root.style.display = controller != null
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            fkSection.Root.style.display = controller != null
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            if (controller == null)
            {
                return;
            }

            RefreshModeSection(
                controller,
                CharacterKinematicMode.InverseKinematics,
                CharacterKinematicModes.InverseKinematics,
                ikSection);
            RefreshModeSection(
                controller,
                CharacterKinematicMode.ForwardKinematics,
                CharacterKinematicModes.ForwardKinematics,
                fkSection);
        }

        private static void RefreshModeSection(
            ICharacterKinematicGroupController controller,
            CharacterKinematicMode mode,
            CharacterKinematicModes modeFlag,
            ModeSection section)
        {
            var modeSupported =
                (controller.SupportedKinematicModes & modeFlag) != 0;
            var supported = controller.GetSupportedGroups(mode);
            var active = controller.GetActiveGroups(mode);
            section.Master.SetEnabled(modeSupported);
            section.Master.SetValueWithoutNotify(
                (controller.ActiveKinematicModes & modeFlag) != 0);
            for (var index = 0; index < section.Groups.Count; index++)
            {
                var group = section.Groups[index];
                group.Toggle.SetEnabled(
                    modeSupported && (supported & group.Group) != 0);
                group.Toggle.SetValueWithoutNotify(
                    (active & group.Group) != 0);
            }
        }

        private ICharacterModel GetSelectedCharacter()
        {
            return selectedCharacterIndex >= 0 &&
                   selectedCharacterIndex < characters.Count
                ? characters[selectedCharacterIndex]
                : null;
        }

        private ICharacterKinematicGroupController GetSelectedController()
        {
            var character = GetSelectedCharacter();
            return character?.Controls?.Pose?.Kinematics as
                       ICharacterKinematicGroupController ??
                   character as ICharacterKinematicGroupController;
        }

        private void HandleDetach(DetachFromPanelEvent detachEvent)
        {
            importController.StateChanged -= RefreshCharacters;
        }

        private static string GetGroupName(CharacterKinematicGroups group)
        {
            switch (group)
            {
                case CharacterKinematicGroups.Body: return "Body";
                case CharacterKinematicGroups.RightLeg: return "Right leg";
                case CharacterKinematicGroups.LeftLeg: return "Left leg";
                case CharacterKinematicGroups.RightHand: return "Right hand";
                case CharacterKinematicGroups.LeftHand: return "Left hand";
                case CharacterKinematicGroups.Hair: return "Hair";
                case CharacterKinematicGroups.Neck: return "Neck";
                case CharacterKinematicGroups.Breast: return "Breast";
                case CharacterKinematicGroups.Skirt: return "Skirt";
                default: return group.ToString();
            }
        }

        private enum ChildView
        {
            Animation,
            Facial,
            Hands,
        }

        private enum FacialControlKind
        {
            Eyebrows,
            Eyes,
            Mouth,
        }

        private sealed class FacialSection
        {
            public FacialSection(
                VisualElement root,
                FacialControlKind kind,
                DropdownField pattern,
                Slider open,
                Toggle follow)
            {
                Root = root;
                Kind = kind;
                Pattern = pattern;
                Open = open;
                Follow = follow;
            }

            public VisualElement Root { get; }

            public FacialControlKind Kind { get; }

            public DropdownField Pattern { get; }

            public Slider Open { get; }

            public Toggle Follow { get; }
        }

        private sealed class HandSection
        {
            public HandSection(
                VisualElement root,
                CharacterHand hand,
                DropdownField shape,
                Slider amount)
            {
                Root = root;
                Hand = hand;
                Shape = shape;
                Amount = amount;
            }

            public VisualElement Root { get; }

            public CharacterHand Hand { get; }

            public DropdownField Shape { get; }

            public Slider Amount { get; }
        }

        private sealed class ModeSection
        {
            public ModeSection(
                VisualElement root,
                Toggle master,
                IReadOnlyList<GroupToggle> groups)
            {
                Root = root;
                Master = master;
                Groups = groups;
            }

            public VisualElement Root { get; }

            public Toggle Master { get; }

            public IReadOnlyList<GroupToggle> Groups { get; }
        }

        private readonly struct GroupToggle
        {
            public GroupToggle(
                CharacterKinematicGroups group,
                Toggle toggle)
            {
                Group = group;
                Toggle = toggle;
            }

            public CharacterKinematicGroups Group { get; }

            public Toggle Toggle { get; }
        }
    }
}
