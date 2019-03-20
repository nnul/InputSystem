#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditorInternal;
using UnityEngine.Experimental.Input.Utilities;

////TODO: better method for creating display names than InputControlPath.TryGetDeviceLayout

namespace UnityEngine.Experimental.Input.Editor
{
    /// <summary>
    /// Toolbar in input action asset editor.
    /// </summary>
    /// <remarks>
    /// Allows editing and selecting from the set of control schemes as well as selecting from the
    /// set of device requirements within the currently selected control scheme.
    ///
    /// Also controls saving and has the global search text field.
    /// </remarks>
    /// <seealso cref="InputActionEditorWindow"/>
    [Serializable]
    internal class InputActionEditorToolbar
    {
        public void OnGUI()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            DrawSchemeSelection();
            DrawDeviceFilterSelection();
            if (!InputEditorUserSettings.autoSaveInputActionAssets)
                DrawSaveButton();
            GUILayout.FlexibleSpace();
            DrawAutoSaveToggle();
            GUILayout.Space(5);
            DrawSearchField();
            GUILayout.Space(5);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSchemeSelection()
        {
            var buttonGUI = m_ControlSchemes.LengthSafe() > 0
                ? new GUIContent(selectedControlScheme?.name ?? "All Control Schemes")
                : new GUIContent("No Control Schemes");

            var buttonRect = GUILayoutUtility.GetRect(buttonGUI, EditorStyles.toolbarPopup, GUILayout.MinWidth(k_MinimumButtonWidth));

            if (GUI.Button(buttonRect, buttonGUI, EditorStyles.toolbarPopup))
            {
                buttonRect = new Rect(EditorGUIUtility.GUIToScreenPoint(new Vector2(buttonRect.x, buttonRect.y)), Vector2.zero);
                var menu = new GenericMenu();

                // Add entries to select control scheme, if we have some.
                if (m_ControlSchemes.LengthSafe() > 0)
                {
                    menu.AddItem(s_AllControlSchemes, m_SelectedControlSchemeIndex == -1, OnControlSchemeSelected, null);
                    var selectedControlSchemeName = m_SelectedControlSchemeIndex == -1
                        ? null : m_ControlSchemes[m_SelectedControlSchemeIndex].name;
                    foreach (var controlScheme in m_ControlSchemes.OrderBy(x => x.name))
                        menu.AddItem(new GUIContent(controlScheme.name),
                            controlScheme.name == selectedControlSchemeName, OnControlSchemeSelected,
                            controlScheme.name);

                    menu.AddSeparator(string.Empty);
                }

                // Add entries to add/edit/duplicate/delete control schemes.
                menu.AddItem(s_AddControlSchemeLabel, false, OnAddControlScheme, buttonRect);
                if (m_SelectedControlSchemeIndex >= 0)
                {
                    menu.AddItem(s_EditControlSchemeLabel, false, OnEditSelectedControlScheme, buttonRect);
                    menu.AddItem(s_DuplicateControlSchemeLabel, false, OnDuplicateControlScheme, buttonRect);
                    menu.AddItem(s_DeleteControlSchemeLabel, false, OnDeleteControlScheme);
                }
                else
                {
                    menu.AddDisabledItem(s_EditControlSchemeLabel, false);
                    menu.AddDisabledItem(s_DuplicateControlSchemeLabel, false);
                    menu.AddDisabledItem(s_DeleteControlSchemeLabel, false);
                }

                menu.ShowAsContext();
            }
        }

        private void DrawDeviceFilterSelection()
        {
            // Lazy-initialize list of GUIContents that represent each individual device requirement.
            if (m_SelectedSchemeDeviceRequirementNames == null && m_ControlSchemes.LengthSafe() > 0 && m_SelectedControlSchemeIndex >= 0)
            {
                m_SelectedSchemeDeviceRequirementNames = m_ControlSchemes[m_SelectedControlSchemeIndex]
                    .deviceRequirements.Select(x => new GUIContent(DeviceRequirementToDisplayString(x)))
                    .ToArray();
            }

            EditorGUI.BeginDisabledGroup(m_SelectedControlSchemeIndex < 0);
            if (m_SelectedSchemeDeviceRequirementNames.LengthSafe() == 0)
            {
                GUILayout.Button(s_AllDevicesLabel, EditorStyles.toolbarPopup, GUILayout.MinWidth(k_MinimumButtonWidth));
            }
            else if (GUILayout.Button(m_SelectedDeviceRequirementIndex < 0 ? s_AllDevicesLabel : m_SelectedSchemeDeviceRequirementNames[m_SelectedDeviceRequirementIndex],
                EditorStyles.toolbarPopup, GUILayout.MinWidth(k_MinimumButtonWidth)))
            {
                var menu = new GenericMenu();
                menu.AddItem(s_AllDevicesLabel, m_SelectedControlSchemeIndex == -1, OnSelectedDeviceChanged, -1);
                for (var i = 0; i < m_SelectedSchemeDeviceRequirementNames.Length; i++)
                    menu.AddItem(m_SelectedSchemeDeviceRequirementNames[i], m_SelectedDeviceRequirementIndex == i, OnSelectedDeviceChanged, i);
                menu.ShowAsContext();
            }
            EditorGUI.EndDisabledGroup();
        }

        private void DrawSaveButton()
        {
            EditorGUI.BeginDisabledGroup(!m_IsDirty);
            EditorGUILayout.Space();
            if (GUILayout.Button(s_SaveAssetLabel, EditorStyles.toolbarButton))
                onSave();
            EditorGUI.EndDisabledGroup();
        }

        private void DrawAutoSaveToggle()
        {
            ////FIXME: Using a normal Toggle style with a miniFont, I can't get the "Auto-Save" label to align properly on the vertical.
            ////       The workaround here splits it into a toggle with an empty label plus an extra label.
            ////       Not using EditorStyles.toolbarButton here as it makes it hard to tell that it's a toggle.
            if (s_MiniToggleStyle == null)
            {
                s_MiniToggleStyle = new GUIStyle("Toggle")
                {
                    font = EditorStyles.miniFont,
                    margin = new RectOffset(0, 0, 1, 0),
                    padding = new RectOffset(0, 16, 0, 0)
                };
                s_MiniLabelStyle = new GUIStyle("Label")
                {
                    font = EditorStyles.miniFont,
                    margin = new RectOffset(0, 0, 3, 0)
                };
            }

            var autoSaveNew = GUILayout.Toggle(InputEditorUserSettings.autoSaveInputActionAssets, "",
                s_MiniToggleStyle);
            GUILayout.Label(s_AutoSaveLabel, s_MiniLabelStyle);
            if (autoSaveNew != InputEditorUserSettings.autoSaveInputActionAssets && autoSaveNew && m_IsDirty)
            {
                // If it changed from disabled to enabled, perform an initial save.
                onSave();
            }

            InputEditorUserSettings.autoSaveInputActionAssets = autoSaveNew;

            GUILayout.Space(5);
        }

        private void DrawSearchField()
        {
            if (m_SearchField == null)
                m_SearchField = new SearchField();

            EditorGUI.BeginChangeCheck();
            m_SearchText = m_SearchField.OnToolbarGUI(m_SearchText, GUILayout.MaxWidth(250));
            if (EditorGUI.EndChangeCheck())
                onSearchChanged?.Invoke();
        }

        private void OnControlSchemeSelected(object nameObj)
        {
            var index = -1;
            var name = (string)nameObj;
            if (name != null)
            {
                index = ArrayHelpers.IndexOf(m_ControlSchemes,
                    x => x.name.Equals(name, StringComparison.InvariantCultureIgnoreCase));
                Debug.Assert(index != -1, $"Cannot find control scheme {name}");
            }

            m_SelectedControlSchemeIndex = index;
            m_SelectedDeviceRequirementIndex = -1;
            m_SelectedSchemeDeviceRequirementNames = null;

            onSelectedSchemeChanged?.Invoke();
        }

        private void OnSelectedDeviceChanged(object indexObj)
        {
            Debug.Assert(m_SelectedControlSchemeIndex >= 0, "Control scheme must be selected");

            m_SelectedDeviceRequirementIndex = (int)indexObj;
            onSelectedDeviceChanged?.Invoke();
        }

        private void OnAddControlScheme(object position)
        {
            var uniqueName = MakeUniqueControlSchemeName("New control scheme");
            ControlSchemePropertiesPopup.Show((Rect)position,
                new InputControlScheme(uniqueName),
                (s, _) => AddAndSelectControlScheme(s),
                MakeUniqueControlSchemeName);
        }

        private void OnDeleteControlScheme()
        {
            Debug.Assert(m_SelectedControlSchemeIndex >= 0, "Control scheme must be selected");

            // Ask for confirmation.
            var name = m_ControlSchemes[m_SelectedControlSchemeIndex].name;
            if (!EditorUtility.DisplayDialog("Delete scheme?", $"Do you want to delete control scheme '{name}'?",
                "Delete", "Cancel"))
                return;

            ArrayHelpers.EraseAt(ref m_ControlSchemes, m_SelectedControlSchemeIndex);
            m_SelectedControlSchemeIndex = -1;
            m_SelectedSchemeDeviceRequirementNames = null;

            if (m_SelectedDeviceRequirementIndex >= 0)
            {
                m_SelectedDeviceRequirementIndex = -1;
                onSelectedDeviceChanged?.Invoke();
            }

            onControlSchemesChanged?.Invoke();
            onSelectedSchemeChanged?.Invoke();
        }

        private void OnDuplicateControlScheme(object position)
        {
            Debug.Assert(m_SelectedControlSchemeIndex >= 0, "Control scheme must be selected");

            var scheme = m_ControlSchemes[m_SelectedControlSchemeIndex];
            scheme = new InputControlScheme(MakeUniqueControlSchemeName(scheme.name),
                devices: scheme.deviceRequirements);

            ControlSchemePropertiesPopup.Show((Rect)position, scheme,
                (s, _) => AddAndSelectControlScheme(s),
                MakeUniqueControlSchemeName);
        }

        private void OnEditSelectedControlScheme(object position)
        {
            Debug.Assert(m_SelectedControlSchemeIndex >= 0, "Control scheme must be selected");

            ControlSchemePropertiesPopup.Show((Rect)position,
                m_ControlSchemes[m_SelectedControlSchemeIndex],
                UpdateControlScheme,
                MakeUniqueControlSchemeName,
                m_SelectedControlSchemeIndex);
        }

        private void AddAndSelectControlScheme(InputControlScheme scheme)
        {
            Debug.Assert(!string.IsNullOrEmpty(scheme.name), "Control scheme has no name");
            Debug.Assert(
                ArrayHelpers.IndexOf(m_ControlSchemes,
                    x => x.name.Equals(scheme.name, StringComparison.InvariantCultureIgnoreCase)) == -1,
                "Duplicate control scheme name");

            var index = ArrayHelpers.Append(ref m_ControlSchemes, scheme);
            onControlSchemesChanged?.Invoke();

            SelectControlScheme(index);
        }

        private void UpdateControlScheme(InputControlScheme scheme, int index)
        {
            Debug.Assert(index >= 0 && index < m_ControlSchemes.LengthSafe(), "Control scheme index out of range");
            Debug.Assert(!string.IsNullOrEmpty(scheme.name), "Control scheme has no name");

            m_ControlSchemes[index] = scheme;
            onControlSchemesChanged?.Invoke();
        }

        private void SelectControlScheme(int index)
        {
            Debug.Assert(index >= 0 && index < m_ControlSchemes.LengthSafe(), "Control scheme index out of range");

            m_SelectedControlSchemeIndex = index;
            m_SelectedSchemeDeviceRequirementNames = null;
            onSelectedSchemeChanged?.Invoke();

            // Reset device selection.
            if (m_SelectedDeviceRequirementIndex != -1)
            {
                m_SelectedDeviceRequirementIndex = -1;
                onSelectedDeviceChanged?.Invoke();
            }
        }

        private string MakeUniqueControlSchemeName(string name)
        {
            return StringHelpers.MakeUniqueName(name, m_ControlSchemes, x => x.name);
        }

        private static string DeviceRequirementToDisplayString(InputControlScheme.DeviceRequirement requirement)
        {
            ////TODO: need something more flexible to produce correct results for more than the simple string we produce here
            var deviceLayout = InputControlPath.TryGetDeviceLayout(requirement.controlPath);
            var usage = InputControlPath.TryGetDeviceUsage(requirement.controlPath);

            if (!string.IsNullOrEmpty(usage))
                return $"{deviceLayout} {usage}";

            return deviceLayout;
        }

        // Notifications.
        public Action onSearchChanged;
        public Action onSelectedSchemeChanged;
        public Action onSelectedDeviceChanged;
        public Action onControlSchemesChanged;
        public Action onSave;

        [SerializeField] private bool m_IsDirty;
        [SerializeField] private int m_SelectedControlSchemeIndex = -1;
        [SerializeField] private int m_SelectedDeviceRequirementIndex = -1;
        [SerializeField] private InputControlScheme[] m_ControlSchemes;
        [SerializeField] private string m_SearchText;

        private GUIContent[] m_SelectedSchemeDeviceRequirementNames;
        private SearchField m_SearchField;

        private static readonly GUIContent s_AllControlSchemes = EditorGUIUtility.TrTextContent("All Control Schemes");
        private static readonly GUIContent s_AddControlSchemeLabel = new GUIContent("Add Control Scheme...");
        private static readonly GUIContent s_EditControlSchemeLabel = EditorGUIUtility.TrTextContent("Edit Control Scheme...");
        private static readonly GUIContent s_DuplicateControlSchemeLabel = EditorGUIUtility.TrTextContent("Duplicate Control Scheme...");
        private static readonly GUIContent s_DeleteControlSchemeLabel = EditorGUIUtility.TrTextContent("Delete Control Scheme...");
        private static readonly GUIContent s_SaveAssetLabel = EditorGUIUtility.TrTextContent("Save Asset");
        private static readonly GUIContent s_AutoSaveLabel = EditorGUIUtility.TrTextContent("Auto-Save");
        private static readonly GUIContent s_AllDevicesLabel = EditorGUIUtility.TrTextContent("All Devices");

        private static GUIStyle s_MiniToggleStyle;
        private static GUIStyle s_MiniLabelStyle;

        private const float k_MinimumButtonWidth = 110f;

        public ReadOnlyArray<InputControlScheme> controlSchemes
        {
            get => m_ControlSchemes;
            set
            {
                m_ControlSchemes = controlSchemes.ToArray();
                m_SelectedSchemeDeviceRequirementNames = null;
            }
        }

        /// <summary>
        /// The control scheme currently selected in the toolbar or null if none is selected.
        /// </summary>
        public InputControlScheme? selectedControlScheme => m_SelectedControlSchemeIndex >= 0
        ? new InputControlScheme ? (m_ControlSchemes[m_SelectedControlSchemeIndex])
        : null;

        /// <summary>
        /// The device requirement of the currently selected control scheme which is currently selected
        /// in the toolbar or null if none is selected.
        /// </summary>
        public InputControlScheme.DeviceRequirement? selectedDeviceRequirement => m_SelectedDeviceRequirementIndex >= 0
        ? new InputControlScheme.DeviceRequirement ? (m_ControlSchemes[m_SelectedControlSchemeIndex]
            .deviceRequirements[m_SelectedDeviceRequirementIndex])
        : null;

        /// <summary>
        /// The search text currently entered in the toolbar or null.
        /// </summary>
        public string searchText => m_SearchText;

        public bool isDirty
        {
            get => m_IsDirty;
            set => m_IsDirty = value;
        }

        /// <summary>
        /// Popup window content for editing control schemes.
        /// </summary>
        private class ControlSchemePropertiesPopup : PopupWindowContent
        {
            public static void Show(Rect position, InputControlScheme controlScheme, Action<InputControlScheme, int> onApply,
                Func<string, string> onRename, int controlSchemeIndex = -1)
            {
                var popup = new ControlSchemePropertiesPopup
                {
                    m_ControlSchemeIndex = controlSchemeIndex,
                    m_ControlScheme = controlScheme,
                    m_OnApply = onApply,
                    m_OnRename = onRename,
                    m_SetFocus = true,
                };

                // We're calling here from a callback, so we need to manually handle ExitGUIException.
                try
                {
                    PopupWindow.Show(position, popup);
                }
                catch (ExitGUIException) {}
            }

            public override Vector2 GetWindowSize()
            {
                return m_ButtonsAndLabelsHeights > 0 ? new Vector2(300, m_ButtonsAndLabelsHeights) : s_DefaultSize;
            }

            public override void OnOpen()
            {
                m_DeviceList = m_ControlScheme.deviceRequirements.Select(a => new DeviceEntry(a)).ToList();
                m_DeviceView = new ReorderableList(m_DeviceList, typeof(InputControlScheme.DeviceRequirement));
                m_DeviceView.headerHeight = 2;
                m_DeviceView.onAddCallback += list =>
                {
                    var a = new AddDeviceDropdown(AddDeviceRequirement);
                    a.Show(new Rect(Event.current.mousePosition, Vector2.zero));
                };
                m_DeviceView.onRemoveCallback += list =>
                {
                    list.list.RemoveAt(list.index);
                    list.index = -1;
                };
            }

            public override void OnGUI(Rect rect)
            {
                if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
                {
                    editorWindow.Close();
                    Event.current.Use();
                }

                if (Event.current.type == EventType.Repaint)
                    m_ButtonsAndLabelsHeights = 0;

                GUILayout.BeginArea(rect);
                DrawTopBar();
                EditorGUILayout.BeginVertical(EditorStyles.label);
                DrawSpace();
                DrawNameEditTextField();
                DrawSpace();
                DrawDeviceList();
                DrawConfirmationButton();
                EditorGUILayout.EndVertical();
                GUILayout.EndArea();
            }

            private void DrawConfirmationButton()
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Cancel", GUILayout.ExpandWidth(true)))
                {
                    editorWindow.Close();
                }
                if (GUILayout.Button("Save", GUILayout.ExpandWidth(true)))
                {
                    m_ControlScheme = new InputControlScheme(m_ControlScheme.name,
                        devices: m_DeviceList.Select(a => a.deviceRequirement));

                    editorWindow.Close();
                    m_OnApply(m_ControlScheme, m_ControlSchemeIndex);
                }
                if (Event.current.type == EventType.Repaint)
                    m_ButtonsAndLabelsHeights += GUILayoutUtility.GetLastRect().height;
                EditorGUILayout.EndHorizontal();
            }

            private void DrawDeviceList()
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.label);
                var requirementsLabelSize = EditorStyles.label.CalcSize(s_RequirementsLabel);
                var deviceListRect = GUILayoutUtility.GetRect(GetWindowSize().x - requirementsLabelSize.x - 20, m_DeviceView.GetHeight());
                m_DeviceView.DoList(deviceListRect);
                var requirementsHeight = DrawRequirementsCheckboxes();
                var listHeight = m_DeviceView.GetHeight() + EditorGUIUtility.singleLineHeight * 3;
                if (Event.current.type == EventType.Repaint)
                {
                    if (listHeight < requirementsHeight)
                    {
                        m_ButtonsAndLabelsHeights += requirementsHeight;
                    }
                    else
                    {
                        m_ButtonsAndLabelsHeights += listHeight;
                    }
                }

                EditorGUILayout.EndHorizontal();
            }

            private void DrawSpace()
            {
                GUILayout.Space(6f);
                if (Event.current.type == EventType.Repaint)
                    m_ButtonsAndLabelsHeights += 6f;
            }

            private void DrawTopBar()
            {
                EditorGUILayout.LabelField(s_AddControlSchemeLabel, Styles.headerLabel);

                if (Event.current.type == EventType.Repaint)
                    m_ButtonsAndLabelsHeights += GUILayoutUtility.GetLastRect().height;
            }

            private void DrawNameEditTextField()
            {
                EditorGUILayout.BeginHorizontal();
                var labelSize = EditorStyles.label.CalcSize(s_RequirementsLabel);
                EditorGUILayout.LabelField(s_ControlSchemeNameLabel, GUILayout.Width(labelSize.x));

                GUI.SetNextControlName("ControlSchemeName");
                var name = EditorGUILayout.DelayedTextField(m_ControlScheme.name);
                if (name != m_ControlScheme.name)
                {
                    m_ControlScheme.m_Name = m_OnRename(name);
                    editorWindow.Repaint();
                }

                if (m_SetFocus)
                {
                    EditorGUI.FocusTextInControl("ControlSchemeName");
                    m_SetFocus = false;
                }

                EditorGUILayout.EndHorizontal();
            }

            private float DrawRequirementsCheckboxes()
            {
                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField(s_RequirementsLabel, GUILayout.Width(200));
                var requirementHeights = GUILayoutUtility.GetLastRect().y;
                EditorGUI.BeginDisabledGroup(m_DeviceView.index == -1);
                var requirementsOption = -1;
                if (m_DeviceView.index >= 0)
                {
                    var deviceEntryForList = (DeviceEntry)m_DeviceView.list[m_DeviceView.index];
                    requirementsOption = deviceEntryForList.deviceRequirement.isOptional ? 0 : 1;
                }
                EditorGUI.BeginChangeCheck();
                requirementsOption = GUILayout.SelectionGrid(requirementsOption, s_RequiredOptionalChoices, 1, EditorStyles.radioButton);
                requirementHeights += GUILayoutUtility.GetLastRect().y;
                if (EditorGUI.EndChangeCheck())
                {
                    m_DeviceList[m_DeviceView.index].deviceRequirement.isOptional = requirementsOption == 0;
                }
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.EndVertical();
                return requirementHeights;
            }

            private void AddDeviceRequirement(InputControlScheme.DeviceRequirement requirement)
            {
                ArrayHelpers.Append(ref m_ControlScheme.m_DeviceRequirements, requirement);
                m_DeviceList.Add(new DeviceEntry(requirement));
                m_DeviceView.index = m_DeviceView.list.Count - 1;

                editorWindow.Repaint();
            }

            /// <summary>
            /// The control scheme edited by the popup.
            /// </summary>
            public InputControlScheme controlScheme => m_ControlScheme;

            private int m_ControlSchemeIndex;
            private InputControlScheme m_ControlScheme;
            private Action<InputControlScheme, int> m_OnApply;
            private Func<string, string> m_OnRename;

            private ReorderableList m_DeviceView;
            private List<DeviceEntry> m_DeviceList = new List<DeviceEntry>();
            private int m_RequirementsOptionsChoice;

            private bool m_SetFocus;
            private float m_ButtonsAndLabelsHeights;

            private static Vector2 s_DefaultSize => new Vector2(300, 200);
            private static readonly GUIContent s_RequirementsLabel = EditorGUIUtility.TrTextContent("Requirements:");
            private static readonly GUIContent s_AddControlSchemeLabel = EditorGUIUtility.TrTextContent("Add control scheme");
            private static readonly GUIContent s_ControlSchemeNameLabel = EditorGUIUtility.TrTextContent("Scheme Name");
            private static readonly string[] s_RequiredOptionalChoices = { "Optional", "Required" };

            private static class Styles
            {
                public static readonly GUIStyle headerLabel = new GUIStyle(EditorStyles.toolbar);
                static Styles()
                {
                    headerLabel.alignment = TextAnchor.MiddleCenter;
                    headerLabel.fontStyle = FontStyle.Bold;
                    headerLabel.padding.left = 10;
                }
            }

            private class DeviceEntry
            {
                public string displayText;
                public InputControlScheme.DeviceRequirement deviceRequirement;

                public DeviceEntry(InputControlScheme.DeviceRequirement requirement)
                {
                    displayText = DeviceRequirementToDisplayString(requirement);
                    deviceRequirement = requirement;
                }

                public override string ToString()
                {
                    return displayText;
                }
            }

            private class AddDeviceDropdown : AdvancedDropdown
            {
                private readonly Action<InputControlScheme.DeviceRequirement> m_OnAddRequirement;

                public AddDeviceDropdown(Action<InputControlScheme.DeviceRequirement> onAddRequirement)
                    : base(new AdvancedDropdownState())
                {
                    m_OnAddRequirement = onAddRequirement;
                }

                protected override AdvancedDropdownItem BuildRoot()
                {
                    var root = new AdvancedDropdownItem(string.Empty);
                    foreach (var layout in EditorInputControlLayoutCache.allLayouts.Where(x => x.isDeviceLayout).OrderBy(x => x.name))
                    {
                        root.AddChild(new DeviceItem(layout.name));
                        foreach (var usage in layout.commonUsages.OrderBy(x => x))
                            root.AddChild(new DeviceItem(layout.name, usage));
                    }
                    return root;
                }

                protected override void ItemSelected(AdvancedDropdownItem item)
                {
                    var deviceItem = (DeviceItem)item;
                    var requirement = new InputControlScheme.DeviceRequirement
                    {
                        controlPath = deviceItem.ToString(),
                        isOptional = false
                    };

                    m_OnAddRequirement(requirement);
                }

                private class DeviceItem : AdvancedDropdownItem
                {
                    public string layoutName { get; }
                    public string usage { get; }

                    public DeviceItem(string layoutName, string usage = null)
                        : base(string.IsNullOrEmpty(usage) ? layoutName : $"{layoutName} {usage}")
                    {
                        this.layoutName = layoutName;
                        this.usage = usage;
                    }

                    public override string ToString()
                    {
                        return !string.IsNullOrEmpty(usage) ? $"<{layoutName}>{{{usage}}}" : $"<{layoutName}>";
                    }
                }
            }
        }
    }
}
#endif // UNITY_EDITOR
