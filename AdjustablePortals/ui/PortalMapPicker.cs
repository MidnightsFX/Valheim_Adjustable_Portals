using AdjustablePortals.modules;
using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;
using Destination = AdjustablePortals.modules.PortalTargeting.Destination;

namespace AdjustablePortals.ui {

    /// <summary>
    /// SelectOnMap: opens the large map with a temporary pin on every destination. Clicking a pin,
    /// or pressing the gamepad's confirm button with one under the crosshair, travels there.
    /// Closing the map backs out.
    /// </summary>
    /// <remarks>
    /// The pins are unsaved, so they are never written to the character or shared through a
    /// cartography table - and vanilla's own pin handling never sees them either, since
    /// Minimap.GetClosestPin skips unsaved pins. Clicks are therefore matched against them here.
    /// </remarks>
    internal static class PortalMapPicker {

        // OnMapLeftUp can follow the click that picked a portal with a double click in the very
        // same call, after this picker has closed - and a double click opens the name box for a
        // new, saved pin on a map that is no longer showing. Long enough to outlast that.
        private const float DoubleClickGraceSeconds = 0.35f;

        // Vanilla's closest zoom still leaves portals built a few metres apart in the same base
        // sitting on top of each other. Smaller is closer: the zoom is the share of the map shown.
        private const float ExtraZoomIn = 1.25f;

        private static readonly Dictionary<Minimap.PinData, Destination> pins = new Dictionary<Minimap.PinData, Destination>();
        private static readonly Dictionary<Minimap.PinData, RectTransform> glows = new Dictionary<Minimap.PinData, RectTransform>();
        private static bool selecting = false;
        private static float closedAt = -10f;
        private static bool inUpdateNoMap = false;
        // The map's own closest zoom, put back when the picker closes. Negative while nothing is
        // borrowed from it.
        private static float vanillaMinZoom = -1f;

        internal static bool Open(TeleportWorld source, List<Destination> destinations) {
            Close();
            Minimap map = Minimap.instance;
            if (map == null) {
                return false;
            }
            map.ShowPointOnMap(source.transform.position);
            // SetMapMode quietly refuses while no-map is on, and it may have come on since the
            // picker was chosen.
            if (map.m_mode != Minimap.MapMode.Large) {
                return false;
            }

            selecting = true;
            vanillaMinZoom = map.m_minZoom;
            map.m_minZoom = vanillaMinZoom / ExtraZoomIn;
            foreach (Destination destination in destinations) {
                Minimap.PinData pin = map.AddPin(destination.Position, Minimap.PinType.None, destination.Name, save: false, isChecked: false);
                // PinType.None has no sprite of its own. Set before the next UpdatePins, which is
                // the only time the pin's icon is read.
                pin.m_icon = PickerUI.PortalIcon(destination.PrefabHash);
                pins[pin] = destination;

                // First in the pin root, so it draws behind every pin on the map; PinGlows
                // moves it onto its pin.
                RectTransform glow = PickerUI.AddPortalGlow(map.m_pinRootLarge, map.m_pinSizeLarge);
                glow.SetAsFirstSibling();
                glow.gameObject.SetActive(false);
                glows[pin] = glow;
            }
            return true;
        }

        internal static void Close() {
            if (selecting == false) {
                return;
            }
            // Cleared first, so the SetMapMode below does not come back around as a cancel.
            selecting = false;
            closedAt = Time.unscaledTime;
            Minimap map = Minimap.instance;
            if (map != null) {
                foreach (Minimap.PinData pin in pins.Keys) {
                    map.RemovePin(pin);
                }
                foreach (RectTransform glow in glows.Values) {
                    if (glow != null) {
                        UnityEngine.Object.Destroy(glow.gameObject);
                    }
                }
                if (vanillaMinZoom > 0f) {
                    map.m_minZoom = vanillaMinZoom;
                    // Re-clamped, so the ordinary map does not reopen zoomed in further than it
                    // allows.
                    map.LargeZoom = map.LargeZoom;
                }
                if (map.m_mode == Minimap.MapMode.Large) {
                    map.SetMapMode(Minimap.MapMode.Small);
                }
            }
            pins.Clear();
            glows.Clear();
            vanillaMinZoom = -1f;
        }

        private static bool InDoubleClickGrace() {
            return selecting || Time.unscaledTime - closedAt < DoubleClickGraceSeconds;
        }

        private static void PickAt(Minimap map, Vector3 worldPoint) {
            float radius = map.PinInteractRadius;
            Destination closest = null;
            float closestDistance = float.MaxValue;
            foreach (KeyValuePair<Minimap.PinData, Destination> entry in pins) {
                float distance = Utils.DistanceXZ(worldPoint, entry.Key.m_pos);
                if (distance < radius && distance < closestDistance) {
                    closest = entry.Value;
                    closestDistance = distance;
                }
            }
            if (closest != null) {
                PortalTargeting.TravelTo(closest);
            }
        }

        // The same gate vanilla's Minimap.Update puts in front of all of its own input.
        private static bool MapTakesInput() {
            return (Chat.instance == null || Chat.instance.HasFocus() == false) && Console.IsVisible() == false &&
                TextInput.IsVisible() == false && Menu.IsActive() == false && InventoryGui.IsVisible() == false;
        }

        [HarmonyPatch(typeof(Minimap))]
        private static class MapClicks {

            // Every left click belongs to the picker while it is open, hit or miss - vanilla would
            // otherwise be checking off the player's own pins underneath.
            [HarmonyPatch(nameof(Minimap.OnMapLeftClick))]
            [HarmonyPrefix]
            private static bool LeftClick(Minimap __instance) {
                if (selecting == false) {
                    return true;
                }
                PickAt(__instance, __instance.ScreenToWorldPoint(ZInput.pointerPosition));
                return false;
            }

            [HarmonyPatch(nameof(Minimap.OnMapDblClick))]
            [HarmonyPrefix]
            private static bool DoubleClick() {
                return InDoubleClickGrace() == false;
            }

            // Also reached from the gamepad's confirm button, which places a pin where the
            // crosshair is.
            [HarmonyPatch(nameof(Minimap.ShowPinNameInput))]
            [HarmonyPrefix]
            private static bool PinNameInput() {
                return InDoubleClickGrace() == false;
            }

            [HarmonyPatch(nameof(Minimap.RemovePinUnderPointer))]
            [HarmonyPrefix]
            private static bool RightClick() {
                return selecting == false;
            }

            [HarmonyPatch(nameof(Minimap.OnMapMiddleClick))]
            [HarmonyPrefix]
            private static bool MiddleClick() {
                return selecting == false;
            }

            // A gamepad never clicks the map; it picks whatever is under the crosshair in the
            // centre of the screen, behind the same checks vanilla uses for placing a pin there.
            [HarmonyPatch("Update")]
            [HarmonyPrefix]
            private static void GamepadPick(Minimap __instance) {
                if (selecting == false || __instance.m_mode != Minimap.MapMode.Large) {
                    return;
                }
                if (ZInput.GetButtonUp("JoyButtonA") && ZInput.IsMouseActive() == false && Minimap.InTextInput() == false &&
                    __instance.inputDelay <= 0f && MapTakesInput()) {
                    PickAt(__instance, __instance.ScreenToWorldPoint(new Vector3(Screen.width / 2, Screen.height / 2)));
                }
            }

            // Game.UpdateNoMap sends the map back to its small mode unconditionally, and it runs
            // on every global key change - a boss waking anywhere in the world is enough. Only let
            // that through when it is actually turning the map off.
            [HarmonyPatch(nameof(Minimap.SetMapMode))]
            [HarmonyPrefix]
            private static bool KeepOpenThroughKeyChanges(Minimap.MapMode mode) {
                return (selecting && inUpdateNoMap && Game.m_noMap == false && mode != Minimap.MapMode.Large) == false;
            }

            // Escape, the map key, death, no-map coming on: however the map closed, the picker is
            // over. SetMapMode rewrites the mode it is given, so the result is what is checked.
            [HarmonyPatch(nameof(Minimap.SetMapMode))]
            [HarmonyPostfix]
            private static void CancelWhenMapCloses(Minimap __instance) {
                if (selecting && __instance.m_mode != Minimap.MapMode.Large) {
                    PortalTargeting.Cancel();
                }
            }
        }

        // UpdatePins places every pin's marker after the map pans or zooms, and destroys the marker
        // of a pin that is off the part of the map on screen.
        [HarmonyPatch(typeof(Minimap), nameof(Minimap.UpdatePins))]
        private static class PinGlows {
            private static void Postfix() {
                if (selecting == false) {
                    return;
                }
                foreach (KeyValuePair<Minimap.PinData, RectTransform> entry in glows) {
                    RectTransform marker = entry.Key.m_uiElement;
                    RectTransform glow = entry.Value;
                    if (glow == null) {
                        continue;
                    }
                    bool shown = marker != null;
                    if (glow.gameObject.activeSelf != shown) {
                        glow.gameObject.SetActive(shown);
                    }
                    if (shown) {
                        glow.position = marker.TransformPoint(marker.rect.center);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Game), nameof(Game.UpdateNoMap))]
        private static class NoMapRefresh {
            private static void Prefix() {
                inUpdateNoMap = true;
            }

            private static Exception Finalizer(Exception __exception) {
                inUpdateNoMap = false;
                return __exception;
            }
        }
    }
}
