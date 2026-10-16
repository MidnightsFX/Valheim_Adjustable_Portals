using AdjustablePortals.modules;
using Jotunn.Managers;
using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace AdjustablePortals.ui {

    /// <summary>
    /// The small set of widgets the destination pickers are built from, on top of Jotunn's
    /// GUIManager.
    /// </summary>
    /// <remarks>
    /// Coordinates are top-left based throughout: every rect anchors and pivots on its parent's
    /// top-left corner and is placed at (x, -y), so y grows downward. Mixing that with Unity's
    /// centred default is how panels end up looking right only at one resolution.
    /// </remarks>
    internal static class PickerUI {

        internal const float TitleHeight = 34f;

        internal static readonly Color OverlayColor = new Color(0f, 0f, 0f, 0.35f);

        internal static GameObject NewUI(string name, Transform parent, params Type[] components) {
            GameObject go = new GameObject(name, components) { layer = GUIManager.UILayer };
            if (go.GetComponent<RectTransform>() == null) {
                go.AddComponent<RectTransform>();
            }
            go.transform.SetParent(parent, false);
            return go;
        }

        internal static RectTransform NewRect(string name, Transform parent, float x, float y, float w, float h) {
            GameObject go = NewUI(name, parent);
            RectTransform rt = (RectTransform)go.transform;
            PlaceTopLeft(rt, x, y, w, h);
            return rt;
        }

        internal static void PlaceTopLeft(RectTransform rt, float x, float y, float w, float h) {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, -y);
        }

        /// <summary>
        /// A full-screen dimmed overlay on Jotunn's front GUI layer, holding the input block for as
        /// long as it exists. Clicking the dimmed area outside the panel cancels the picker.
        /// </summary>
        internal static GameObject CreateOverlay(string name, out PickerWindow window) {
            window = null;
            if (GUIManager.Instance == null || GUIManager.CustomGUIFront == null) {
                return null;
            }
            GameObject overlay = NewUI(name, GUIManager.CustomGUIFront.transform, typeof(Image));
            RectTransform rt = (RectTransform)overlay.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            overlay.GetComponent<Image>().color = OverlayColor;
            // Also what keeps clicks from falling through to whatever is underneath.
            Button catcher = overlay.AddComponent<Button>();
            catcher.transition = Selectable.Transition.None;
            catcher.navigation = new Navigation { mode = Navigation.Mode.None };
            catcher.onClick.AddListener(PortalTargeting.Cancel);
            window = overlay.AddComponent<PickerWindow>();
            return overlay;
        }

        internal static GameObject CreatePanel(Transform parent, float w, float h, string title) {
            GameObject panel = GUIManager.Instance.CreateWoodpanel(
                parent: parent,
                anchorMin: new Vector2(0.5f, 0.5f), anchorMax: new Vector2(0.5f, 0.5f),
                position: Vector2.zero, width: w, height: h, draggable: false);
            AddText(panel.transform, 0f, 14f, w, TitleHeight, title, 22, TextAnchor.MiddleCenter, GUIManager.Instance.ValheimYellow);
            return panel;
        }

        /// <summary>
        /// A line of text. Rich text is off, as most of what is shown here is portal names that
        /// players typed.
        /// </summary>
        internal static Text AddText(Transform parent, float x, float y, float w, float h, string text, int fontSize, TextAnchor anchor, Color? color = null) {
            GameObject go = GUIManager.Instance.CreateText(
                text: text, parent: parent,
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(0f, 1f),
                position: new Vector2(x, -y),
                font: GUIManager.Instance.AveriaSerifBold, fontSize: fontSize,
                color: color ?? GUIManager.Instance.ValheimBeige,
                outline: true, outlineColor: Color.black,
                width: w, height: h, addContentSizeFitter: false);
            PlaceTopLeft((RectTransform)go.transform, x, y, w, h);
            Text component = go.GetComponent<Text>();
            component.alignment = anchor;
            component.supportRichText = false;
            component.raycastTarget = false;
            // A name too long for its box is cut off at the box rather than run under the next one.
            component.horizontalOverflow = HorizontalWrapMode.Wrap;
            component.verticalOverflow = VerticalWrapMode.Truncate;
            return component;
        }

        /// <summary>
        /// A row for the inside of a Jotunn scroll view. Its content carries a VerticalLayoutGroup
        /// that overwrites anchoredPosition, so rows size themselves through a LayoutElement
        /// instead of being placed.
        /// </summary>
        internal static GameObject NewLayoutRow(Transform content, float width, float height) {
            GameObject row = NewUI("Row", content, typeof(RectTransform), typeof(LayoutElement));
            PlaceTopLeft((RectTransform)row.transform, 0f, 0f, width, height);
            LayoutElement layout = row.GetComponent<LayoutElement>();
            layout.minHeight = height;
            layout.preferredHeight = height;
            layout.minWidth = width;
            layout.preferredWidth = width;
            return row;
        }

        /// <summary>
        /// Scrolls <paramref name="scroll"/> just far enough that <paramref name="item"/> is inside
        /// its viewport. A gamepad moving the selection down a list does not do this on its own.
        /// </summary>
        internal static void KeepInView(ScrollRect scroll, RectTransform item) {
            if (scroll == null || scroll.content == null || item == null) {
                return;
            }
            RectTransform viewport = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
            item.GetWorldCorners(itemCorners);
            viewport.GetWorldCorners(viewportCorners);
            // Corners run bottom-left, top-left, top-right, bottom-right.
            float above = itemCorners[1].y - viewportCorners[1].y;
            float below = viewportCorners[0].y - itemCorners[0].y;
            if (above > 0f) {
                scroll.content.position -= new Vector3(0f, above, 0f);
            } else if (below > 0f) {
                scroll.content.position += new Vector3(0f, below, 0f);
            }
        }

        private static readonly Vector3[] itemCorners = new Vector3[4];
        private static readonly Vector3[] viewportCorners = new Vector3[4];

        internal static Button AddButton(Transform parent, float x, float y, float w, float h, string text, UnityAction onClick) {
            GameObject go = GUIManager.Instance.CreateButton(text, parent,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -y), w, h);
            PlaceTopLeft((RectTransform)go.transform, x, y, w, h);
            Button button = go.GetComponent<Button>();
            if (onClick != null) {
                button.onClick.AddListener(onClick);
            }
            return button;
        }

        internal static InputField AddTextField(Transform parent, float x, float y, float w, float h, string placeholder) {
            GameObject go = GUIManager.Instance.CreateInputField(
                parent: parent,
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(0f, 1f),
                position: new Vector2(x, -y),
                contentType: InputField.ContentType.Standard, placeholderText: placeholder, fontSize: 16,
                width: w, height: h);
            PlaceTopLeft((RectTransform)go.transform, x, y, w, h);
            return go.GetComponent<InputField>();
        }

        /// <summary>
        /// The build-menu icon of the portal piece with this prefab hash, or the wooden portal's
        /// when the piece has none of its own.
        /// </summary>
        internal static Sprite PortalIcon(int prefabHash) {
            ZNetScene scene = ZNetScene.instance;
            if (scene == null) {
                return null;
            }
            Sprite icon = PieceIcon(scene.GetPrefab(prefabHash));
            if (icon != null) {
                return icon;
            }
            return PieceIcon(scene.GetPrefab("portal_wood"));
        }

        private static Sprite PieceIcon(GameObject prefab) {
            if (prefab == null) {
                return null;
            }
            Piece piece = prefab.GetComponent<Piece>();
            return piece != null ? piece.m_icon : null;
        }
    }

    /// <summary>
    /// Sits on the root of a picker panel. Holds Jotunn's input block for exactly as long as the
    /// panel exists, and backs out of the picker on Escape or the gamepad's cancel button.
    /// </summary>
    /// <remarks>
    /// The block is only ever released from OnDestroy, never from a close handler. Unity destroys
    /// objects after every Update in the frame has run, so the pause menu - which opens on the
    /// same Escape press - still finds input blocked whichever of the two updates first. Releasing
    /// it here also means an exception or scene change partway through can never leave the player
    /// frozen in place.
    /// </remarks>
    internal class PickerWindow : MonoBehaviour {

        // A jump or interact press that is still being released when the picker opens should not
        // count as the player's first choice in it.
        internal const float InputDelaySeconds = 0.3f;

        /// <summary>
        /// Called if the panel is destroyed by anything other than its picker closing it, such as
        /// leaving the world. The picker clears this before destroying the panel itself.
        /// </summary>
        internal Action Dismissed;

        private float openedAt;
        private bool holdingInput;

        internal bool AcceptingInput {
            get { return Time.unscaledTime - openedAt >= InputDelaySeconds; }
        }

        private void Awake() {
            openedAt = Time.unscaledTime;
            holdingInput = true;
            GUIManager.BlockInput(true);
        }

        private void Update() {
            if (AcceptingInput == false || Console.IsVisible()) {
                return;
            }
            if (ZInput.GetKeyDown(KeyCode.Escape) || ZInput.GetButtonDown("JoyButtonB")) {
                // B is read again by everything that runs after this frame's picker closes.
                ZInput.ResetButtonStatus("JoyButtonB");
                PortalTargeting.Cancel();
            }
        }

        private void OnDestroy() {
            if (holdingInput) {
                holdingInput = false;
                GUIManager.BlockInput(false);
            }
            Action dismissed = Dismissed;
            Dismissed = null;
            dismissed?.Invoke();
        }
    }
}
