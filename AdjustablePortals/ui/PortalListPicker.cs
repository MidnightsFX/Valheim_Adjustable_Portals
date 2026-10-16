using AdjustablePortals.modules;
using Jotunn.Managers;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Destination = AdjustablePortals.modules.PortalTargeting.Destination;

namespace AdjustablePortals.ui {

    /// <summary>
    /// SelectFromList: every destination by name in a filterable, scrolling list. One click on a
    /// name travels there.
    /// </summary>
    /// <remarks>
    /// A list rather than a Unity Dropdown. A dropdown is two clicks per trip and turns unusable
    /// somewhere past a few dozen entries, where a filter box stays quick at hundreds.
    /// </remarks>
    internal class PortalListPicker : MonoBehaviour {

        private const float PanelWidth = 480f;
        private const float PanelHeight = 600f;
        private const float Margin = 20f;
        private const float FilterTop = 56f;
        private const float FilterHeight = 30f;
        private const float ListTop = 96f;
        private const float FooterHeight = 64f;
        private const float RowHeight = 34f;
        private const float ButtonWidth = 140f;
        private const float ButtonHeight = 36f;
        // Rows are real GameObjects, rebuilt on every keystroke in the filter. The filter is the
        // way through a list longer than this.
        private const int MaxRows = 300;

        private static PortalListPicker current = null;

        private PickerWindow window;
        private List<Destination> destinations;
        private InputField filter;
        private ScrollRect scroll;
        private Transform content;
        private float contentWidth;
        private readonly List<Button> rows = new List<Button>();
        private Destination firstShown;
        private GameObject lastSelected;

        internal static bool Open(TeleportWorld source, List<Destination> destinations) {
            Close();
            GameObject overlay = PickerUI.CreateOverlay("AJP_PortalListPicker", out PickerWindow window);
            if (overlay == null) {
                return false;
            }
            window.Dismissed = PortalTargeting.Cancel;
            current = overlay.AddComponent<PortalListPicker>();
            current.window = window;
            current.Build(destinations);
            return true;
        }

        internal static void Close() {
            if (current == null) {
                return;
            }
            PortalListPicker closing = current;
            current = null;
            closing.window.Dismissed = null;
            Destroy(closing.gameObject);
        }

        private void Build(List<Destination> list) {
            destinations = new List<Destination>(list);
            // By name, then nearest first among portals that share one - every vanilla pair does.
            destinations.Sort((a, b) => {
                int byName = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                return byName != 0 ? byName : a.Distance.CompareTo(b.Distance);
            });

            GameObject panel = PickerUI.CreatePanel(transform, PanelWidth, PanelHeight, "Choose a destination");
            float innerWidth = PanelWidth - Margin * 2f;

            filter = PickerUI.AddTextField(panel.transform, Margin, FilterTop, innerWidth, FilterHeight, "Filter...");
            filter.onValueChanged.AddListener(Rebuild);
            filter.onEndEdit.AddListener(SubmitFilter);

            float listHeight = PanelHeight - ListTop - FooterHeight;
            RectTransform holder = PickerUI.NewRect("ScrollHolder", panel.transform, Margin, ListTop, innerWidth, listHeight);
            GameObject scrollView = GUIManager.Instance.CreateScrollView(holder, false, true, 8f, 4f,
                GUIManager.Instance.ValheimScrollbarHandleColorBlock, new Color(0f, 0f, 0f, 0.5f), innerWidth, listHeight);
            content = scrollView.transform.Find("Scroll View/Viewport/Content");
            scroll = scrollView.GetComponentInChildren<ScrollRect>();
            if (scroll != null) {
                scroll.scrollSensitivity = 200f;
            }
            // The scrollbar and its border take this much of the width.
            contentWidth = innerWidth - 16f;

            PickerUI.AddButton(panel.transform, (PanelWidth - ButtonWidth) / 2f, PanelHeight - FooterHeight + 12f, ButtonWidth, ButtonHeight, "Cancel", PortalTargeting.Cancel);

            Rebuild("");

            // A gamepad starts on the first portal; a keyboard starts typing in the filter.
            if (ZInput.IsGamepadActive()) {
                SelectFirstRow();
            } else {
                filter.ActivateInputField();
            }
        }

        private void Rebuild(string needle) {
            if (content == null) {
                return;
            }
            foreach (Transform child in content) {
                // Hidden first, since destruction waits for the end of the frame and the layout
                // group would go on placing the old rows until then.
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
            rows.Clear();
            firstShown = null;

            int hidden = 0;
            foreach (Destination destination in destinations) {
                if (string.IsNullOrEmpty(needle) == false && destination.Name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) {
                    continue;
                }
                if (rows.Count >= MaxRows) {
                    hidden++;
                    continue;
                }
                if (firstShown == null) {
                    firstShown = destination;
                }
                rows.Add(AddRow(destination));
            }

            if (rows.Count == 0) {
                AddNote("No portals match.");
            } else if (hidden > 0) {
                AddNote($"... {hidden} more, type to narrow the list.");
            }

            if (ZInput.IsGamepadActive()) {
                SelectFirstRow();
            }
        }

        private Button AddRow(Destination destination) {
            GameObject row = PickerUI.NewLayoutRow(content, contentWidth, RowHeight);
            float height = RowHeight - 2f;
            Button button = PickerUI.AddButton(row.transform, 0f, 0f, contentWidth, height, "", () => Pick(destination));
            float nameWidth = contentWidth * 0.62f;
            PickerUI.AddText(button.transform, 10f, 0f, nameWidth - 10f, height, destination.Name, 16, TextAnchor.MiddleLeft);
            PickerUI.AddText(button.transform, nameWidth, 0f, contentWidth - nameWidth - 10f, height, destination.DistanceText, 14, TextAnchor.MiddleRight, GUIManager.Instance.ValheimOrange);
            return button;
        }

        private void AddNote(string text) {
            GameObject row = PickerUI.NewLayoutRow(content, contentWidth, RowHeight);
            PickerUI.AddText(row.transform, 10f, 0f, contentWidth - 20f, RowHeight, text, 14, TextAnchor.MiddleLeft);
        }

        private void Pick(Destination destination) {
            if (window == null || window.AcceptingInput == false) {
                return;
            }
            PortalTargeting.TravelTo(destination);
        }

        // Enter in the filter box takes the first portal it matches. onEndEdit also fires when the
        // box merely loses focus, which is not a choice.
        private void SubmitFilter(string text) {
            if (firstShown == null) {
                return;
            }
            if (ZInput.GetKeyDown(KeyCode.Return) || ZInput.GetKeyDown(KeyCode.KeypadEnter)) {
                Pick(firstShown);
            }
        }

        private void SelectFirstRow() {
            if (rows.Count == 0 || EventSystem.current == null) {
                return;
            }
            EventSystem.current.SetSelectedGameObject(rows[0].gameObject);
        }

        private void Update() {
            if (EventSystem.current == null) {
                return;
            }
            GameObject selected = EventSystem.current.currentSelectedGameObject;
            if (selected == lastSelected) {
                return;
            }
            lastSelected = selected;
            if (selected != null && content != null && selected.transform.IsChildOf(content)) {
                PickerUI.KeepInView(scroll, (RectTransform)selected.transform);
            }
        }
    }
}
