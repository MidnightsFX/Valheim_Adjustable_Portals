using AdjustablePortals.modules;
using Jotunn.Managers;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Destination = AdjustablePortals.modules.PortalTargeting.Destination;

namespace AdjustablePortals.ui {

    /// <summary>
    /// SelectInSpace: every destination plotted where it lies relative to the others, against a
    /// starfield instead of the map. Nothing of the world's terrain is shown - only where the
    /// portals are - so it works in worlds where the map is turned off.
    /// </summary>
    /// <remarks>
    /// Zooming and panning move the markers rather than scaling a container, so icons and names
    /// stay the same size and crisp at any zoom.
    /// </remarks>
    internal class PortalSpacePicker : MonoBehaviour {

        private const float PanelWidth = 880f;
        private const float PanelHeight = 680f;
        private const float Margin = 20f;
        private const float ViewTop = 56f;
        private const float FooterHeight = 92f;
        private const float ButtonWidth = 120f;
        private const float ButtonHeight = 34f;
        private const float MarkerSize = 30f;
        private const float SourceMarkerSize = 26f;
        private const float FocusedScale = 1.35f;
        private const float LabelWidth = 160f;
        private const float LabelHeight = 20f;

        // Two portals a stone's throw apart should not fill the whole view as if they were
        // continents apart.
        private const float MinimumSpanMetres = 200f;
        private const float FitPadding = 1.2f;
        // Zooming out past the fit only loses detail, and zooming in further than this spreads a
        // single base across the whole view. Close enough to separate portals built a few metres
        // apart in the same base.
        private const float MinZoomOfFit = 0.5f;
        private const float MaxPixelsPerMetre = 3.75f;
        private const float ZoomStep = 1.25f;
        // Every name is shown while there are few enough of them not to pile up, and otherwise
        // once zoomed in far enough that they have room.
        private const int AlwaysLabelCount = 20;
        private const float LabelAllPixelsPerMetre = 0.4f;

        private const int StarfieldSeed = 7351;
        private const float StarsPerPixel = 0.0012f;

        private static readonly Color SourceTint = new Color(0.45f, 0.85f, 1f, 1f);
        private static readonly Color32 SkyColour = new Color32(6, 8, 20, 255);
        private const string DefaultFooter = "Pick a portal to travel to.";
        private const string Hint = "Scroll to zoom, drag to look around.";

        private class Marker {
            // Both null for the portal the player is standing in.
            public Destination Destination;
            public RectTransform Glow;
            public Vector2 WorldXZ;
            public RectTransform Rect;
            public Text Label;
        }

        private static PortalSpacePicker current = null;

        private PickerWindow window;
        private RectTransform glowRoot;
        private RectTransform markerRoot;
        private Text footer;
        private Texture2D starfield;
        private float viewWidth;
        private float viewHeight;
        private readonly List<Marker> markers = new List<Marker>();
        private int destinationCount;
        private Marker focused;
        // The world x/z at the centre of the view, and how many view pixels a metre takes up.
        private Vector2 viewCentre;
        private float pixelsPerMetre;
        private float minPixelsPerMetre;
        private float maxPixelsPerMetre;
        private Vector2 boundsMin;
        private Vector2 boundsMax;

        internal static bool Open(TeleportWorld source, List<Destination> destinations) {
            Close();
            GameObject overlay = PickerUI.CreateOverlay("AJP_PortalSpacePicker", out PickerWindow window);
            if (overlay == null) {
                return false;
            }
            window.Dismissed = PortalTargeting.Cancel;
            current = overlay.AddComponent<PortalSpacePicker>();
            current.window = window;
            current.Build(source, destinations);
            return true;
        }

        internal static void Close() {
            if (current == null) {
                return;
            }
            PortalSpacePicker closing = current;
            current = null;
            closing.window.Dismissed = null;
            Destroy(closing.gameObject);
        }

        private void Build(TeleportWorld source, List<Destination> destinations) {
            GameObject panel = PickerUI.CreatePanel(transform, PanelWidth, PanelHeight, "Choose a destination");
            viewWidth = PanelWidth - Margin * 2f;
            viewHeight = PanelHeight - ViewTop - FooterHeight;

            RectTransform view = PickerUI.NewRect("Space", panel.transform, Margin, ViewTop, viewWidth, viewHeight);
            starfield = CreateStarfield(Mathf.RoundToInt(viewWidth), Mathf.RoundToInt(viewHeight));
            view.gameObject.AddComponent<RawImage>().texture = starfield;
            // Clips markers panned out of view, and their clicks along with them.
            view.gameObject.AddComponent<RectMask2D>();
            view.gameObject.AddComponent<SpaceViewInput>().Picker = this;

            // Glows first, so they draw behind every portal icon.
            glowRoot = NewCentredRoot("Glows", view);
            markerRoot = NewCentredRoot("Portals", view);

            float footerTop = ViewTop + viewHeight;
            footer = PickerUI.AddText(panel.transform, Margin, footerTop + 8f, viewWidth, 28f, DefaultFooter, 18, TextAnchor.MiddleCenter);
            PickerUI.AddText(panel.transform, Margin, footerTop + 46f, viewWidth - ButtonWidth - 10f, ButtonHeight, Hint, 14, TextAnchor.MiddleLeft);
            PickerUI.AddButton(panel.transform, PanelWidth - Margin - ButtonWidth, footerTop + 46f, ButtonWidth, ButtonHeight, "Cancel", PortalTargeting.Cancel);

            Vector3 origin = source.transform.position;
            Vector2 sourceXZ = new Vector2(origin.x, origin.z);
            Marker nearest = null;
            foreach (Destination destination in destinations) {
                Marker marker = AddMarker(destination, new Vector2(destination.Position.x, destination.Position.z),
                    PickerUI.PortalIcon(destination.PrefabHash), destination.Name, Color.white, MarkerSize);
                if (nearest == null || destination.Distance < nearest.Destination.Distance) {
                    nearest = marker;
                }
            }
            destinationCount = destinations.Count;
            // Last, so it draws over any destination sitting right next to it.
            AddMarker(null, sourceXZ, PickerUI.PortalIcon(source.m_nview.GetZDO().GetPrefab()), "You are here", SourceTint, SourceMarkerSize);

            FitView();

            if (ZInput.IsGamepadActive() && nearest != null && EventSystem.current != null) {
                EventSystem.current.SetSelectedGameObject(nearest.Rect.gameObject);
            }
        }

        private static RectTransform NewCentredRoot(string name, Transform parent) {
            RectTransform root = (RectTransform)PickerUI.NewUI(name, parent).transform;
            root.anchorMin = new Vector2(0.5f, 0.5f);
            root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = Vector2.zero;
            root.anchoredPosition = Vector2.zero;
            return root;
        }

        private Marker AddMarker(Destination destination, Vector2 worldXZ, Sprite icon, string label, Color tint, float size) {
            GameObject go = PickerUI.NewUI(destination != null ? "Portal" : "YouAreHere", markerRoot, typeof(Image));
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);

            Image image = go.GetComponent<Image>();
            image.sprite = icon;
            image.color = tint;
            image.preserveAspect = true;

            Marker marker = new Marker { Destination = destination, WorldXZ = worldXZ, Rect = rt };
            marker.Label = PickerUI.AddText(go.transform, (size - LabelWidth) / 2f, size, LabelWidth, LabelHeight, label, 13, TextAnchor.UpperCenter,
                destination != null ? GUIManager.Instance.ValheimBeige : SourceTint);

            if (destination != null) {
                marker.Glow = PickerUI.AddPortalGlow(glowRoot, size);
                Button button = go.AddComponent<Button>();
                button.targetGraphic = image;
                button.onClick.AddListener(() => Pick(destination));
                MarkerFocus focus = go.AddComponent<MarkerFocus>();
                focus.Picker = this;
                focus.Marker = marker;
            } else {
                image.raycastTarget = false;
            }
            markers.Add(marker);
            return marker;
        }

        private void FitView() {
            boundsMin = markers[0].WorldXZ;
            boundsMax = markers[0].WorldXZ;
            foreach (Marker marker in markers) {
                boundsMin = Vector2.Min(boundsMin, marker.WorldXZ);
                boundsMax = Vector2.Max(boundsMax, marker.WorldXZ);
            }
            float spanX = Mathf.Max(boundsMax.x - boundsMin.x, MinimumSpanMetres) * FitPadding;
            float spanZ = Mathf.Max(boundsMax.y - boundsMin.y, MinimumSpanMetres) * FitPadding;
            pixelsPerMetre = Mathf.Min(viewWidth / spanX, viewHeight / spanZ);
            minPixelsPerMetre = pixelsPerMetre * MinZoomOfFit;
            maxPixelsPerMetre = Mathf.Max(MaxPixelsPerMetre, pixelsPerMetre);
            viewCentre = (boundsMin + boundsMax) * 0.5f;
            Layout();
        }

        private void Layout() {
            bool labelAll = destinationCount <= AlwaysLabelCount || pixelsPerMetre >= LabelAllPixelsPerMetre;
            foreach (Marker marker in markers) {
                // Screen up is north, which is +z.
                marker.Rect.anchoredPosition = (marker.WorldXZ - viewCentre) * pixelsPerMetre;
                if (marker.Glow != null) {
                    marker.Glow.anchoredPosition = marker.Rect.anchoredPosition;
                }
                marker.Label.enabled = labelAll || marker == focused || marker.Destination == null;
            }
        }

        /// <summary>
        /// Zooms one step in (<paramref name="direction"/> above zero) or out, keeping whatever is
        /// under the pointer where it is.
        /// </summary>
        private void Zoom(float direction, Vector2 screenPoint, Camera eventCamera) {
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(markerRoot, screenPoint, eventCamera, out Vector2 pointer) == false) {
                return;
            }
            Vector2 worldUnderPointer = viewCentre + pointer / pixelsPerMetre;
            float zoomed = Mathf.Clamp(pixelsPerMetre * (direction > 0f ? ZoomStep : 1f / ZoomStep), minPixelsPerMetre, maxPixelsPerMetre);
            if (Mathf.Approximately(zoomed, pixelsPerMetre)) {
                return;
            }
            pixelsPerMetre = zoomed;
            viewCentre = ClampToBounds(worldUnderPointer - pointer / pixelsPerMetre);
            Layout();
        }

        private void Pan(Vector2 screenPoint, Vector2 screenDelta, Camera eventCamera) {
            // Both ends taken through the canvas, so the canvas scaler and GUI scale are accounted for.
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(markerRoot, screenPoint, eventCamera, out Vector2 to) == false ||
                RectTransformUtility.ScreenPointToLocalPointInRectangle(markerRoot, screenPoint - screenDelta, eventCamera, out Vector2 from) == false) {
                return;
            }
            viewCentre = ClampToBounds(viewCentre - (to - from) / pixelsPerMetre);
            Layout();
        }

        // Keeps the view over the portals, so dragging cannot lose them all off one edge.
        private Vector2 ClampToBounds(Vector2 centre) {
            return Vector2.Max(boundsMin, Vector2.Min(boundsMax, centre));
        }

        private void SetFocus(Marker marker, bool fromSelection) {
            if (focused != null && focused != marker) {
                SetScale(focused, 1f);
            }
            focused = marker;
            if (marker == null) {
                footer.text = DefaultFooter;
                Layout();
                return;
            }
            SetScale(marker, FocusedScale);
            // Drawn over its neighbours, label and all.
            marker.Rect.SetAsLastSibling();
            marker.Glow.SetAsLastSibling();
            footer.text = $"{marker.Destination.Name}   {marker.Destination.DistanceText}";
            // A gamepad can move the selection onto a portal that is out of view.
            if (fromSelection && IsInView(marker) == false) {
                viewCentre = marker.WorldXZ;
            }
            Layout();
        }

        private static void SetScale(Marker marker, float scale) {
            Vector3 scaled = new Vector3(scale, scale, 1f);
            marker.Rect.localScale = scaled;
            marker.Glow.localScale = scaled;
        }

        private void ClearFocus(Marker marker) {
            if (focused == marker) {
                SetFocus(null, false);
            }
        }

        private bool IsInView(Marker marker) {
            Vector2 position = (marker.WorldXZ - viewCentre) * pixelsPerMetre;
            return Mathf.Abs(position.x) <= (viewWidth - MarkerSize) / 2f && Mathf.Abs(position.y) <= (viewHeight - MarkerSize) / 2f;
        }

        private void Pick(Destination destination) {
            if (window == null || window.AcceptingInput == false) {
                return;
            }
            PortalTargeting.TravelTo(destination);
        }

        private void OnDestroy() {
            if (starfield != null) {
                Destroy(starfield);
                starfield = null;
            }
        }

        /// <summary>
        /// A field of single-pixel stars, a few of them larger and brighter, sized to the view so
        /// every star lands on a whole pixel.
        /// </summary>
        private static Texture2D CreateStarfield(int width, int height) {
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;

            Color32[] pixels = new Color32[width * height];
            for (int i = 0; i < pixels.Length; i++) {
                pixels[i] = SkyColour;
            }
            // Seeded, so the sky is the same every time the picker opens.
            System.Random random = new System.Random(StarfieldSeed);
            int stars = Mathf.RoundToInt(width * height * StarsPerPixel);
            for (int i = 0; i < stars; i++) {
                int x = random.Next(width);
                int y = random.Next(height);
                byte brightness = (byte)random.Next(70, 256);
                // A touch of blue or warmth, so it does not read as noise.
                bool warm = random.Next(4) == 0;
                Color32 star = warm
                    ? new Color32(brightness, (byte)(brightness * 0.9f), (byte)(brightness * 0.75f), 255)
                    : new Color32((byte)(brightness * 0.85f), (byte)(brightness * 0.9f), brightness, 255);
                pixels[y * width + x] = star;
                if (brightness > 230 && random.Next(3) == 0) {
                    Color32 glow = new Color32((byte)(star.r / 2), (byte)(star.g / 2), (byte)(star.b / 2), 255);
                    if (x > 0) { pixels[y * width + x - 1] = glow; }
                    if (x < width - 1) { pixels[y * width + x + 1] = glow; }
                    if (y > 0) { pixels[(y - 1) * width + x] = glow; }
                    if (y < height - 1) { pixels[(y + 1) * width + x] = glow; }
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        /// <summary>
        /// Wheel and drag over the view. Both bubble up from the markers too, which only handle
        /// pointing and clicking - so a drag that starts on a portal pans rather than travels.
        /// </summary>
        private class SpaceViewInput : MonoBehaviour, IScrollHandler, IDragHandler {
            internal PortalSpacePicker Picker;

            public void OnScroll(PointerEventData eventData) {
                if (eventData.scrollDelta.y != 0f) {
                    Picker.Zoom(eventData.scrollDelta.y, eventData.position, eventData.enterEventCamera);
                }
            }

            public void OnDrag(PointerEventData eventData) {
                Picker.Pan(eventData.position, eventData.delta, eventData.pressEventCamera);
            }
        }

        private class MarkerFocus : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler {
            internal PortalSpacePicker Picker;
            internal Marker Marker;

            public void OnPointerEnter(PointerEventData eventData) {
                Picker.SetFocus(Marker, false);
            }

            public void OnPointerExit(PointerEventData eventData) {
                Picker.ClearFocus(Marker);
            }

            public void OnSelect(BaseEventData eventData) {
                Picker.SetFocus(Marker, true);
            }

            public void OnDeselect(BaseEventData eventData) {
                Picker.ClearFocus(Marker);
            }
        }
    }
}
