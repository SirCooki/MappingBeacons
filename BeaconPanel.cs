using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ScoutingBeacons
{
    /// Status and preview panel to the right of the vanilla container window while a beacon is open.
    /// Buttons in two columns around the title (Take all / Place stacks left, Add all fuel / Feed beacon right); the
    /// window grows by one button row while a beacon is open and is restored afterwards.
    public class BeaconPanel : MonoBehaviour
    {
        private const float RefreshSeconds = 0.25f;
        private const float MarginX = 20f, ButtonGap = 8f, TitleGap = 16f, SlotsGap = 10f, TitleScale = 1.5f;
        private static BeaconPanel s_instance;

        private InventoryGui m_gui;
        private RectTransform m_root;
        private TMP_Text m_text;
        private float m_timer;
        private Button m_addFuelButton, m_feedButton;
        private Beacon m_beacon;

        // The vanilla window and buttons as they were before a beacon opened
        private bool m_saved;
        private Vector2 m_windowSize, m_gridPosition, m_titlePosition, m_titleSize;
        private float m_titleFont, m_titleMin, m_titleMax;
        private TextAlignmentOptions m_titleAlign;
        private bool m_titleAuto, m_titleWrap;
        private ButtonState m_takeAll, m_stackAll;

        private class ButtonState
        {
            public RectTransform Rect;
            public Vector2 Pivot, Position, Size;
            public void Save() { Pivot = Rect.pivot; Position = Rect.anchoredPosition; Size = Rect.sizeDelta; }
            public void Restore() { Rect.pivot = Pivot; Rect.anchoredPosition = Position; Rect.sizeDelta = Size; }
        }

        public static void Ensure(InventoryGui gui)
        {
            if (s_instance || !gui.m_container) return;
            var go = new GameObject("ScoutingBeacons_Panel", typeof(RectTransform));
            go.transform.SetParent(gui.m_container, false);
            s_instance = go.AddComponent<BeaconPanel>();
            s_instance.Build(gui);
        }

        private void Build(InventoryGui gui)
        {
            m_gui = gui;
            m_root = (RectTransform)transform;
            m_root.anchorMin = m_root.anchorMax = new Vector2(1f, 1f);
            m_root.pivot = new Vector2(0f, 1f);
            m_root.anchoredPosition = new Vector2(12f, 0f);
            m_root.sizeDelta = new Vector2(430f, 340f);

            var bkg = gui.m_container.GetComponent<Image>();
            var image = gameObject.AddComponent<Image>();
            if (bkg)
            {
                image.sprite = bkg.sprite;
                image.type = bkg.type;
                image.material = bkg.material;
                image.color = bkg.color;
            }
            else image.color = new Color(0f, 0f, 0f, 0.75f);

            var textGo = Instantiate(gui.m_containerName.gameObject, transform, false);
            textGo.name = "text";
            m_text = textGo.GetComponent<TMP_Text>();
            var rt = (RectTransform)textGo.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(18f, 14f);
            rt.offsetMax = new Vector2(-18f, -14f);
            m_text.alignment = TextAlignmentOptions.TopLeft;
            m_text.enableWordWrapping = true;
            m_text.richText = true;
            m_text.fontSize = 16f;
            m_text.enableAutoSizing = false;
            m_text.color = Color.white;

            m_addFuelButton = CopyButton(gui, "Add all fuel", OnAddAllFuel);
            m_feedButton = CopyButton(gui, "Feed beacon", OnFeed);
            gameObject.SetActive(false);
        }

        /// Copy of the vanilla "Take all" button, stripped of its click and gamepad shortcut. Hidden until a beacon opens.
        private static Button CopyButton(InventoryGui gui, string label, UnityEngine.Events.UnityAction onClick)
        {
            if (!gui.m_takeAllButton) return null;
            var source = gui.m_takeAllButton.gameObject;
            var go = Instantiate(source, source.transform.parent, false);
            go.name = "ScoutingBeacons_" + label.Replace(" ", "");
            foreach (var pad in go.GetComponentsInChildren<UIGamePad>(true))
            {
                if (pad.m_hint && pad.m_hint.transform.IsChildOf(go.transform)) Destroy(pad.m_hint);
                Destroy(pad);
            }
            var text = go.GetComponentInChildren<TMP_Text>(true);
            if (text) text.text = label;
            var button = go.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(onClick);
            go.SetActive(false);
            return button;
        }

        private void OnAddAllFuel()
        {
            var player = Player.m_localPlayer;
            var storage = m_beacon && m_beacon.Container ? m_beacon.Container.GetInventory() : null;
            if (!player || storage == null) return;
            var inventory = player.GetInventory();
            int moved = 0;
            foreach (var item in new System.Collections.Generic.List<ItemDrop.ItemData>(inventory.GetAllItems()))
            {
                if (item.m_equipped || Settings.FuelValue(item) <= 0f) continue;
                int before = item.m_stack;
                storage.MoveItemToThis(inventory, item);
                moved += before - (inventory.ContainsItem(item) ? item.m_stack : 0);
            }
            if (moved == 0) player.Message(MessageHud.MessageType.Center, "No fuel in your inventory");
            m_timer = 0f;
        }

        private void OnFeed()
        {
            if (!m_beacon) return;
            float added = m_beacon.Feed(dropOthers: false);
            if (added > 0f && Player.m_localPlayer)
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, $"+{added:0.#} fuel");
            m_timer = 0f;
        }

        /// Like a two-row table:   [Take all]      (title)     [Add all fuel]
        ///                          [Place stacks]              [Feed beacon]
        /// with the title centred on the line between the rows, and the slots right under the second row.
        private void Layout()
        {
            var window = m_gui.m_container;
            var take = m_gui.m_takeAllButton ? (RectTransform)m_gui.m_takeAllButton.transform : null;
            var stack = m_gui.m_stackAllButton ? (RectTransform)m_gui.m_stackAllButton.transform : null;
            var grid = m_gui.m_containerGrid ? (RectTransform)m_gui.m_containerGrid.transform : null;
            var title = m_gui.m_containerName ? m_gui.m_containerName.rectTransform : null;
            if (!window || !take || !stack || !grid || !title || !m_addFuelButton || !m_feedButton) return;
            if (!m_saved)
            {
                m_windowSize = window.sizeDelta;
                m_gridPosition = grid.anchoredPosition;
                m_titlePosition = title.anchoredPosition;
                var t = m_gui.m_containerName;
                m_titleSize = title.sizeDelta;
                m_titleFont = t.fontSize;
                m_titleAlign = t.alignment;
                m_titleAuto = t.enableAutoSizing;
                m_titleMin = t.fontSizeMin;
                m_titleMax = t.fontSizeMax;
                m_titleWrap = t.enableWordWrapping;
                m_takeAll = new ButtonState { Rect = take };
                m_stackAll = new ButtonState { Rect = stack };
                m_takeAll.Save();
                m_stackAll.Save();
                m_saved = true;
            }

            // Measure from the vanilla places: first row = Take all's centre, top of the first slot row
            m_takeAll.Restore();
            window.sizeDelta = m_windowSize;
            grid.anchoredPosition = m_gridPosition;
            Vector3 centre = window.InverseTransformPoint(take.TransformPoint(take.rect.center));
            var slots = m_gui.m_containerGrid.m_gridRoot ? m_gui.m_containerGrid.m_gridRoot : grid;
            float slotsTop = window.InverseTransformPoint(slots.TransformPoint(new Vector3(0f, slots.rect.yMax, 0f))).y;

            float bw = m_takeAll.Size.x, bh = m_takeAll.Size.y;
            float step = bh + ButtonGap;
            // Title = the merged middle cell of a 2 x 3 grid: as tall as both rows, text centred both ways, larger than vanilla
            var text = m_gui.m_containerName;
            text.enableAutoSizing = false;
            text.enableWordWrapping = false;
            text.fontSize = m_titleFont * TitleScale;
            text.alignment = TextAlignmentOptions.Center;
            float titleWidth = text.GetPreferredValues(text.text).x;
            float need = 2f * MarginX + 2f * bw + 2f * TitleGap + titleWidth;
            float wanted = centre.y - step - bh / 2f - SlotsGap;   // slots just under the second row
            float shift = slotsTop - wanted;                        // > 0: move the slots down
            window.sizeDelta = new Vector2(Mathf.Max(m_windowSize.x, need), m_windowSize.y + shift);
            grid.anchoredPosition = m_gridPosition - new Vector2(0f, shift);

            var r = window.rect;
            float left = r.xMin + MarginX, right = r.xMax - MarginX - bw;
            Place(take, bw, left, centre.y);
            Place(stack, bw, left, centre.y - step);
            Place((RectTransform)m_addFuelButton.transform, bw, right, centre.y);
            Place((RectTransform)m_feedButton.transform, bw, right, centre.y - step);

            title.sizeDelta = new Vector2(right - (left + bw) - 2f * TitleGap, bh * 2f + ButtonGap);
            Vector3 from = title.TransformPoint(title.rect.center);
            Vector3 to = window.TransformPoint(new Vector3(r.center.x, centre.y - step / 2f, 0f));
            title.position += to - from;

            m_addFuelButton.gameObject.SetActive(true);
            m_feedButton.gameObject.SetActive(true);
        }

        /// Puts a button's left edge at x and its middle at y (window space).
        private void Place(RectTransform button, float width, float x, float y)
        {
            button.sizeDelta = new Vector2(width, m_takeAll.Size.y);
            button.pivot = new Vector2(0f, 0.5f);
            button.position = m_gui.m_container.TransformPoint(new Vector3(x, y, 0f));
        }

        private void RestoreLayout()
        {
            if (!m_saved) return;
            m_gui.m_container.sizeDelta = m_windowSize;
            if (m_gui.m_containerGrid) ((RectTransform)m_gui.m_containerGrid.transform).anchoredPosition = m_gridPosition;
            var t = m_gui.m_containerName;
            if (t)
            {
                t.rectTransform.sizeDelta = m_titleSize;
                t.rectTransform.anchoredPosition = m_titlePosition;
                t.alignment = m_titleAlign;
                t.enableAutoSizing = m_titleAuto;
                t.fontSizeMin = m_titleMin;
                t.fontSizeMax = m_titleMax;
                t.fontSize = m_titleFont;
                t.enableWordWrapping = m_titleWrap;
            }
            m_takeAll.Restore();
            m_stackAll.Restore();
            if (m_addFuelButton) m_addFuelButton.gameObject.SetActive(false);
            if (m_feedButton) m_feedButton.gameObject.SetActive(false);
            m_saved = false;
        }

        public static Beacon OpenBeacon(InventoryGui gui)
        {
            var c = gui ? gui.m_currentContainer : null;
            return c ? c.GetComponent<Beacon>() : null;
        }

        /// Called every frame from InventoryGui.Update.
        public static void Refresh(InventoryGui gui)
        {
            if (!s_instance) return;
            var beacon = gui.IsContainerOpen() ? OpenBeacon(gui) : null;
            s_instance.Show(beacon);
        }

        private void Show(Beacon beacon)
        {
            bool show = beacon && beacon.Tier != null;
            m_beacon = show ? beacon : null;
            if (gameObject.activeSelf != show) gameObject.SetActive(show);
            if (!show) { RestoreLayout(); return; }
            Layout();
            m_timer -= Time.unscaledDeltaTime;
            if (m_timer > 0f) return;
            m_timer = RefreshSeconds;
            m_text.text = Describe(beacon);
            if (m_feedButton) m_feedButton.interactable = beacon.PendingFuel(out _) > 0f;
        }

        private const string Gold = "#FFD15A", Grey = "#BBBBBB", Red = "#FF6B5A", Amber = "#FFB347", Blue = "#7FC8FF";

        /// Real time as mm:ss (minutes keep counting past an hour). World time runs at real speed while the game isn't paused.
        private static string Clock(double seconds)
        {
            long t = (long)System.Math.Ceiling(seconds);
            return $"{t / 60:00}:{t % 60:00}";
        }

        private static string Describe(Beacon b)
        {
            var t = b.Tier;
            float w = b.Land.Width;
            int limit = t.LimitRing;
            float pending = b.PendingFuel(out var nonFuel);
            var now = b.Predict(b.Fuel);
            var after = b.Predict(b.Fuel + pending);
            int tier = BeaconTiers.All.IndexOf(t) + 1;
            float fullNeed = b.FuelToLimit(); // rings the builder already knew are free
            string state = b.CoastReached ? $"<color={Blue}>coast reached</color>"
                : b.Active ? "<color=#7CFC7C>exploring</color>" : $"<color={Red}>waiting for fuel</color>";

            var s = new StringBuilder();
            s.AppendLine($"<color={Gold}><b>{t.DisplayName}</b></color>  ·  {state}");
            s.AppendLine($"<color={Grey}>Map beacon tier {tier}  ·  optimal range {t.LimitKm2.Value:0.##} km²  ·  " +
                         $"fuel ratio {t.Efficiency.Value:0.##}</color>");
            s.AppendLine();
            s.AppendLine(pending > 0f ? $"Fuel:  <b>{b.Fuel:0.#}</b>  (+{pending:0.#})" : $"Fuel:  <b>{b.Fuel:0.#}</b>");
            s.AppendLine($"Theoretical fuel need:  <b>{fullNeed:0}</b>  (for {t.LimitKm2.Value:0.##} km²)");
            s.AppendLine($"Exploring range:  <b>{b.Ring * w:0} m</b>");
            int extra = after.Ring - now.Ring;
            s.AppendLine(pending > 0f && extra > 0
                ? $"Fuel range:  <b>{now.Ring * w:0} m</b>  (+{extra * w:0} m)"
                : $"Fuel range:  <b>{now.Ring * w:0} m</b>");
            if (now.Seconds > 0.0)
                s.AppendLine(now.Coast
                    ? $"Coast reached in:  <b>{Clock(now.Seconds)}</b>"
                    : $"Fuel depletion countdown:  <b>{Clock(now.Seconds)}</b>");

            float limitRange = limit * w;
            if (b.Ring > limit)
                s.AppendLine($"<color={Red}>Limit overreach: {b.Ring * w - limitRange:0} m past {limitRange:0} m, " +
                             $"each ring costs {t.RingCost(b.Ring + 1) / t.RingCost(limit):0.#}× more</color>");
            else if (after.Ring > limit)
                s.AppendLine($"<color={Amber}>Limit overreach: fuel carries past {limitRange:0} m, costs rise steeply</color>");
            // The whole island is (or will be) explored: the ocean rings it in, so more fuel does nothing here
            if (b.CoastReached)
                s.AppendLine($"<color={Red}>Island fully explored: ocean on every side, no further exploration " +
                             $"possible. More fuel won't help; {b.Fuel:0.#} fuel stays stored.</color>");
            else if (after.Coast)
                s.AppendLine($"<color={Blue}>This fuel explores the whole island (ocean on every side at " +
                             $"{after.Ring * w:0} m); {after.Leftover:0.#} fuel will be left over.</color>");
            if (nonFuel.Count > 0)
                s.AppendLine(Settings.ConvertOnClose.Value
                    ? $"<color={Red}>{string.Join(", ", nonFuel)} will be DROPPED (not fuel)</color>"
                    : $"<color={Amber}>{string.Join(", ", nonFuel)}: not fuel, stays in the beacon</color>");
            return s.ToString();
        }
    }
}
