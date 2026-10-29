using System;
using System.Collections;
using DG.Tweening;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace IronbarkCoop
{
    // Multiplayer entry in the game's own main menu: a "Multiplayer" item cloned from the menu's buttons, opening a
    // panel cloned from the save-selection panel, laid out like Scratch Creek's lobby (two survivor slots, friend play,
    // disconnect). Everything reuses the menu's own fonts, hover markers and slide animation.
    internal static class MultiplayerMenu
    {
        internal static Font MenuFont;
        static RectTransform panel;
        static Vector2 hiddenPosition;
        static readonly AccessTools.FieldRef<UIManagerMainMenu, Vector2> ButtonsHome = AccessTools.FieldRefAccess<UIManagerMainMenu, Vector2>("mmButtonsPaneliPos");
        static readonly AccessTools.FieldRef<UIManagerMainMenu, bool> CameraAside = AccessTools.FieldRefAccess<UIManagerMainMenu, bool>("isCamPos2");
        internal static string L(string ru, string en) => ConnectionPanel.L(ru, en);

        public static IEnumerator InstallWhenReady()
        {
            // UIManagerMainMenu.Start records the panels' home positions first.
            yield return null;
            yield return null;
            try { Install(); }
            catch (Exception ex) { Plugin.Log("MENU_INSTALL_FAILED " + ex); }
        }

        static void Install()
        {
            var menu = UnityEngine.Object.FindObjectOfType<UIManagerMainMenu>();
            if (!menu || panel) return;
            var buttons = menu.mmButtonsPanel;
            var saved = menu.savedPanel;
            var itemTemplate = buttons.Find("Credits");
            if (!itemTemplate) { Plugin.Log("MENU_INSTALL_FAILED no Credits item"); return; }
            // The menu's own typeface, kept for the mod's notices (joined / left / connection) everywhere.
            var itemText = itemTemplate.GetComponentInChildren<Text>(true);
            if (itemText && itemText.font) MenuFont = itemText.font;

            // Main menu: "Multiplayer" right under "Episodes"; the items below move down one row.
            float row = 30.75f;
            foreach (var name in new[] { "Discord", "Options", "Credits", "Exit" })
            {
                var item = buttons.Find(name) as RectTransform;
                if (item) item.anchoredPosition -= new Vector2(0f, row);
            }
            var episodes = buttons.Find("episodes") as RectTransform;
            var entry = Clone(itemTemplate, buttons, "Multiplayer", "Мультиплеер", "Multiplayer", Show);
            entry.anchoredPosition = new Vector2(episodes.anchoredPosition.x, episodes.anchoredPosition.y - row);

            // Panel: a copy of the save panel keeps its frame, title style and Back button (BackToMainMenu).
            panel = UnityEngine.Object.Instantiate(saved.gameObject, saved.parent).GetComponent<RectTransform>();
            panel.name = "multiplayer panel";
            hiddenPosition = saved.anchoredPosition;
            panel.anchoredPosition = hiddenPosition;
            Transform title = null, back = null, bigTemplate = null;
            foreach (Transform child in panel)
            {
                if (child.name == "Text (5)") title = child;
                else if (child.name == "Back") back = child;
                else if (child.name == "New Game") bigTemplate = child;
            }
            foreach (Transform child in panel)
                if (child != title && child != back && child != bigTemplate) UnityEngine.Object.Destroy(child.gameObject);
            bigTemplate.gameObject.SetActive(false);
            RemoveLocalization(title);
            panel.gameObject.AddComponent<MultiplayerPanel>().Build(title.GetComponent<Text>(), bigTemplate, menu);
            Plugin.Log("MENU_INSTALLED");
        }

        internal static void Show()
        {
            var menu = UnityEngine.Object.FindObjectOfType<UIManagerMainMenu>();
            if (!menu || !panel) return;
            panel.DOAnchorPos(ButtonsHome(menu), 1.2f).SetEase(Ease.OutCubic);
            menu.mmButtonsPanel.DOAnchorPos(menu.mmButtonsPanelToPos.anchoredPosition, 1.2f).SetEase(Ease.OutCubic);
            CameraAside(menu) = true;
            menu.blackPanel.CrossFadeAlpha(menu.blackPanelAlpha, menu.blackPanelSpeed, true);
        }

        internal static void Hide()
        {
            if (panel) panel.DOAnchorPos(hiddenPosition, 1.2f).SetEase(Ease.OutCubic);
        }

        // From the lobby the host picks the chapter in the game's own save panel; the partner follows the scene.
        internal static void OpenChapters()
        {
            var menu = UnityEngine.Object.FindObjectOfType<UIManagerMainMenu>();
            if (!menu) return;
            Hide();
            menu.SavedPanelShow();
        }

        // A fixed label in both languages: it follows the game's language when the player changes it in Options.
        internal static RectTransform Clone(Transform template, Transform parent, string name, string russian, string english, Action onClick)
        {
            var copy = Clone(template, parent, name, L(russian, english), onClick);
            var live = copy.gameObject.AddComponent<TwoLanguages>();
            live.Russian = russian; live.English = english;
            return copy;
        }

        internal static RectTransform Clone(Transform template, Transform parent, string name, string text, Action onClick)
        {
            var copy = UnityEngine.Object.Instantiate(template.gameObject, parent);
            copy.name = name;
            copy.SetActive(true);
            RemoveLocalization(copy.transform);
            var label = copy.GetComponent<Text>();
            if (label) { label.text = text; label.horizontalOverflow = HorizontalWrapMode.Overflow; }
            var trigger = copy.GetComponent<EventTrigger>();
            if (trigger)
            {
                // Menu items act on PointerDown (action + click sound). Keep the sound and hover marker, swap the action.
                var sounds = new System.Collections.Generic.List<AudioSource>();
                foreach (var e in trigger.triggers)
                {
                    if (e.eventID != EventTriggerType.PointerDown && e.eventID != EventTriggerType.PointerClick) continue;
                    for (int i = 0; i < e.callback.GetPersistentEventCount(); i++)
                        if (e.callback.GetPersistentTarget(i) is AudioSource sound) sounds.Add(sound);
                }
                trigger.triggers.RemoveAll(e => e.eventID == EventTriggerType.PointerDown || e.eventID == EventTriggerType.PointerClick);
                if (onClick != null)
                {
                    var press = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
                    press.callback.AddListener(_ => { foreach (var sound in sounds) if (sound) sound.Play(); onClick(); });
                    trigger.triggers.Add(press);
                }
            }
            // The hover marker starts hidden, as on the original items.
            foreach (Transform child in copy.transform)
                if (child.TryGetComponent<Image>(out var marker)) marker.enabled = false;
            return copy.GetComponent<RectTransform>();
        }

        static void RemoveLocalization(Transform target)
        {
            foreach (var component in target.GetComponents<MonoBehaviour>())
                if (component && component.GetType().Name == "LocalizeStringEvent") UnityEngine.Object.DestroyImmediate(component);
        }
    }

    internal sealed class TwoLanguages : MonoBehaviour
    {
        internal string Russian, English;
        Text text;
        int shown = -1;
        void Update()
        {
            int now = ConnectionPanel.Russian ? 1 : 0;
            if (now == shown) return;
            shown = now;
            if (!text) text = GetComponent<Text>();
            if (text) text.text = now == 1 ? Russian : English;
        }
    }

    // Lobby contents, refreshed every frame from the plugin's connection state.
    internal sealed class MultiplayerPanel : MonoBehaviour
    {
        Text title, hostSlot, partnerSlot, status, addresses;
        RectTransform hostButton, joinButton, steamButton, inviteButton, playButton, leaveButton, ipRow, copyButton, upnpButton;
        Text upnpLabel, characterLabel, lookLabel, poseLabel, lookName, micLabel;
        RectTransform characterButton, lookButton, micButton, prevButton, nextButton, poseButton, chooseButton, closeButton;
        CharacterPreview preview;
        bool previewOpen;
        int lookIndex;
        Transform back;
        InputField ip, nick;
        RectTransform nickRow;
        float talkPulse;
        static string L(string ru, string en) => ConnectionPanel.L(ru, en);

        public void Build(Text titleText, Transform big, UIManagerMainMenu menu)
        {
            title = titleText;
            var rect = title.rectTransform;
            float x = rect.anchoredPosition.x, top = rect.anchoredPosition.y;
            // Two survivor slots, as in Scratch Creek's lobby.
            hostSlot = Label("host slot", x, top - 26f, 15, FontStyle.Normal, 300f);
            partnerSlot = Label("partner slot", x, top - 47f, 15, FontStyle.Normal, 300f);
            hostSlot.horizontalOverflow = partnerSlot.horizontalOverflow = HorizontalWrapMode.Overflow;
            status = Label("status", x, top - 76f, 11, FontStyle.Normal, 270f, 34f);
            // The player's name, typed on the host line while not connected (the Steam name is the default).
            nickRow = Field("name field", x, top - 26f, 82f, 150f, "", "", 24, out nick);
            nick.text = Plugin.Instance ? Plugin.Instance.NameSetting : "";
            nick.onEndEdit.AddListener(value => { if (Plugin.Instance) Plugin.Instance.NameSetting = value; });
            float bx = big.GetComponent<RectTransform>().anchoredPosition.x, y = top - 122f;
            hostButton = MultiplayerMenu.Clone(big, transform, "host", "Создать игру", "Host game", () => Plugin.Instance?.HostDirect());
            hostButton.anchoredPosition = new Vector2(bx, y); y -= 31f;
            joinButton = MultiplayerMenu.Clone(big, transform, "join", "Подключиться", "Join", () => Plugin.Instance?.JoinDirect());
            joinButton.anchoredPosition = new Vector2(bx, y); y -= 28f;
            ipRow = IpField(x, y); y -= 59f;
            steamButton = MultiplayerMenu.Clone(big, transform, "steam", "Играть с другом в Steam", "Play with a Steam friend", () => Plugin.Instance?.HostSteam());
            steamButton.anchoredPosition = new Vector2(bx, top - 228f);
            inviteButton = MultiplayerMenu.Clone(big, transform, "invite", "Пригласить друга", "Invite friend", () => Plugin.Instance?.InviteSteam());
            inviteButton.anchoredPosition = new Vector2(bx, top - 228f);
            playButton = MultiplayerMenu.Clone(big, transform, "play", "Выбрать главу", "Choose chapter", MultiplayerMenu.OpenChapters);
            // Connected rows reuse the top lines, which the idle buttons leave free.
            playButton.anchoredPosition = new Vector2(bx, top - 122f);
            leaveButton = MultiplayerMenu.Clone(big, transform, "leave", "Отключиться", "Disconnect", () => Plugin.Instance?.Leave());
            leaveButton.anchoredPosition = new Vector2(bx, top - 153f);
            // Host: share a short code instead of an address.
            copyButton = MultiplayerMenu.Clone(big, transform, "copy code", "Скопировать код", "Copy code", () =>
            {
                var code = Plugin.Instance ? Plugin.Instance.HostCode : null;
                if (code != null) GUIUtility.systemCopyBuffer = code;
            });
            copyButton.anchoredPosition = new Vector2(bx, top - 122f);
            upnpButton = MultiplayerMenu.Clone(big, transform, "upnp", "", () => { if (Plugin.Instance) Plugin.Instance.UpnpEnabled = !Plugin.Instance.UpnpEnabled; });
            upnpButton.anchoredPosition = new Vector2(bx, top - 204f);
            upnpLabel = upnpButton.GetComponent<Text>();
            upnpLabel.fontSize = 13;
            // Survivor choice, as on Scratch Creek's survivor select (Marcus / Tessa).
            characterButton = MultiplayerMenu.Clone(big, transform, "character", "", () =>
            {
                var p = Plugin.Instance;
                if (p) p.SetCharacter(p.CharacterSetting == "auto" ? "marcus" : p.CharacterSetting == "marcus" ? "tessa" : "auto");
            });
            characterButton.anchoredPosition = new Vector2(bx, top - 272f);
            characterLabel = characterButton.GetComponent<Text>();
            characterLabel.fontSize = 13;
            // Look: who the partner sees you as, picked in a 3D view (Scratch Creek's survivors and Ironbark's people).
            lookButton = MultiplayerMenu.Clone(big, transform, "look", "", OpenPreview);
            lookButton.anchoredPosition = new Vector2(bx, top - 289f);
            lookLabel = lookButton.GetComponent<Text>();
            lookLabel.fontSize = 13;
            // The microphone: on speech (as in Scratch Creek), push-to-talk or off.
            micButton = MultiplayerMenu.Clone(big, transform, "microphone", "", () => Plugin.Instance?.NextMicrophone());
            micButton.anchoredPosition = new Vector2(bx, top - 255f);
            micLabel = micButton.GetComponent<Text>();
            micLabel.fontSize = 13;
            float py = top - 122f;
            prevButton = MultiplayerMenu.Clone(big, transform, "look prev", "‹ Пред.", "‹ Prev", () => Step(-1));
            prevButton.anchoredPosition = new Vector2(bx, py); py -= 31f;
            nextButton = MultiplayerMenu.Clone(big, transform, "look next", "След. ›", "Next ›", () => Step(1));
            nextButton.anchoredPosition = new Vector2(bx, py); py -= 31f;
            poseButton = MultiplayerMenu.Clone(big, transform, "look pose", "", () => { if (preview) preview.Current = (CharacterPreview.Pose)(((int)preview.Current + 1) % 6); });
            poseButton.anchoredPosition = new Vector2(bx, py); py -= 40f;
            poseLabel = poseButton.GetComponent<Text>();
            poseLabel.fontSize = 15;
            chooseButton = MultiplayerMenu.Clone(big, transform, "look choose", "Выбрать", "Choose", () =>
            {
                var looks = ScRuntime.Available();
                if (looks.Count > 0) Plugin.Instance?.SetLook(looks[lookIndex % looks.Count].Id);
                previewOpen = false;
            });
            chooseButton.anchoredPosition = new Vector2(bx, py); py -= 31f;
            closeButton = MultiplayerMenu.Clone(big, transform, "look close", "Отмена", "Cancel", () => previewOpen = false);
            closeButton.anchoredPosition = new Vector2(bx, py);
            lookName = Label("look name", x, top - 40f, 16, FontStyle.Bold, 150f, 44f);
            lookName.alignment = TextAnchor.UpperLeft;
            preview = CharacterPreview.Create(transform, new Vector2(x + 222f + (260f - title.rectTransform.sizeDelta.x) / 2f, top - 148f), new Vector2(151f, 252f));
            foreach (Transform child in transform) if (child.name == "Back") back = child;
            // Fixed slot above Back (which sits at the panel bottom) so the list never overlaps it.
            addresses = Label("addresses", x, top - 206f, 10, FontStyle.Normal, 270f, 44f);
        }

        Text Label(string name, float x, float y, int size, FontStyle style, float width, float height = 20f)
        {
            var go = Instantiate(title.gameObject, transform);
            go.name = name;
            var text = go.GetComponent<Text>();
            text.fontSize = size;
            text.fontStyle = style;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            var r = text.rectTransform;
            r.sizeDelta = new Vector2(width, height);
            r.anchoredPosition = new Vector2(x + (width - title.rectTransform.sizeDelta.x) / 2f, y);
            return text;
        }

        RectTransform IpField(float x, float y)
        {
            var row = Field("ip field", x, y, 10f, 200f, "Код друга или его IP", "Friend's code or IP", 64, out ip);
            ip.text = Plugin.Instance ? Plugin.Instance.HostAddress : "";
            ip.onValueChanged.AddListener(value => { if (Plugin.Instance) Plugin.Instance.HostAddress = value.Trim(); });
            return row;
        }

        // A text field in the menu's font: a faint line, the typed text, a hint while empty.
        RectTransform Field(string name, float x, float y, float indent, float width, string hintRussian, string hintEnglish, int limit, out InputField field)
        {
            var row = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            row.SetParent(transform, false);
            row.anchorMin = row.anchorMax = title.rectTransform.anchorMin;
            row.pivot = new Vector2(0.5f, 0.5f);
            row.sizeDelta = new Vector2(width, 22f);
            row.anchoredPosition = new Vector2(x + (width - title.rectTransform.sizeDelta.x) / 2f + indent, y);
            var line = row.gameObject.AddComponent<Image>();
            line.color = new Color(1f, 1f, 1f, 0.08f);
            var text = Instantiate(title.gameObject, row).GetComponent<Text>();
            text.name = "Text";
            text.fontSize = 14;
            text.supportRichText = false;
            text.alignment = TextAnchor.MiddleLeft;
            var tr = text.rectTransform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = new Vector2(6f, 0f); tr.offsetMax = new Vector2(-6f, 0f);
            var hint = Instantiate(text.gameObject, row).GetComponent<Text>();
            hint.name = "Placeholder";
            hint.text = L(hintRussian, hintEnglish);
            if (hintRussian != "")
            {
                var hintLive = hint.gameObject.AddComponent<TwoLanguages>();
                hintLive.Russian = hintRussian; hintLive.English = hintEnglish;
            }
            hint.color = new Color(1f, 1f, 1f, 0.35f);
            field = row.gameObject.AddComponent<InputField>();
            field.textComponent = text;
            field.placeholder = hint;
            field.characterLimit = limit;
            return row;
        }

        void OpenPreview()
        {
            var looks = ScRuntime.Available();
            if (looks.Count == 0) return;
            var current = Plugin.Instance ? Plugin.Instance.MyLook : null;
            lookIndex = Math.Max(0, looks.FindIndex(l => l.Id == current));
            previewOpen = true;
        }

        void Step(int by)
        {
            var looks = ScRuntime.Available();
            if (looks.Count > 0) lookIndex = (lookIndex + by + looks.Count) % looks.Count;
        }

        static string LookName(string id)
        {
            var info = ScRuntime.Info(id);
            return info == null ? "?" : L(info.Russian, info.English);
        }

        // While the look picker is open only its controls show (the lobby's own come back when it closes).
        bool UpdatePreview()
        {
            foreach (var control in new[] { prevButton, nextButton, poseButton, chooseButton, closeButton }) control.gameObject.SetActive(previewOpen);
            lookName.gameObject.SetActive(previewOpen);
            preview.gameObject.SetActive(previewOpen);
            if (back) back.gameObject.SetActive(!previewOpen);
            if (!previewOpen) return false;
            foreach (var control in new Component[] { hostSlot, partnerSlot, status, addresses, nickRow, hostButton, joinButton, ipRow, steamButton, inviteButton, playButton, leaveButton, copyButton, upnpButton, characterButton, lookButton, micButton })
                control.gameObject.SetActive(false);
            var looks = ScRuntime.Available();
            var look = looks.Count > 0 ? looks[lookIndex % looks.Count] : null;
            preview.Look = look?.Id;
            title.text = L("Внешность", "Look");
            lookName.text = look == null ? "" : L(look.Russian, look.English) + "\n<size=11>" + (look.ScratchCreek ? "Scratch Creek" : "Ironbark Lookout") + "</size>";
            poseLabel.text = L("Поза: ", "Pose: ") + CharacterPreview.PoseName(preview.Current);
            return true;
        }

        void Update()
        {
            var plugin = Plugin.Instance;
            if (!plugin) return;
            if (UpdatePreview()) return;
            // The lobby rows hidden by the picker come back (the rows below set their own visibility).
            hostSlot.gameObject.SetActive(true); partnerSlot.gameObject.SetActive(true); status.gameObject.SetActive(true);
            talkPulse += Time.unscaledDeltaTime * 6f;
            string me = plugin.MyName, partner = plugin.PartnerName;
            bool connected = partner != null, host = plugin.IsHost;
            string Talk(bool talking) => talking && Mathf.Sin(talkPulse) > -0.3f ? "  ((•))" : "";
            title.text = L("Мультиплеер", "Multiplayer") + (plugin.NetInfo != "" ? "  ·  " + plugin.NetInfo : "");
            string hostName = !plugin.Online || host ? me : partner;
            string guestName = !plugin.Online || host ? partner : me;
            hostSlot.text = L("Хост: ", "Host: ") + (plugin.Online || connected ? hostName ?? "…" : me) + Talk(host ? plugin.LocalTalking : plugin.PartnerTalking);
            partnerSlot.text = L("Второй выживший: ", "Second survivor: ")
                + (connected ? guestName + Talk(host ? plugin.PartnerTalking : plugin.LocalTalking) : L("ищем выжившего…", "looking for survivor…"));
            status.text = plugin.Online ? ConnectionPanel.Explain(plugin.Status)
                : L("Создайте игру или подключитесь к другу.", "Host a game or join a friend.");
            bool idle = !plugin.Online;
            // Not connected: the host line is where the name is typed (empty: the Steam name, shown as the hint).
            nickRow.gameObject.SetActive(idle);
            if (idle)
            {
                hostSlot.text = L("Ваш ник:", "Your name:");
                if (nick.placeholder is Text auto) auto.text = plugin.AutoName;
            }
            hostButton.gameObject.SetActive(idle);
            joinButton.gameObject.SetActive(idle);
            ipRow.gameObject.SetActive(idle);
            steamButton.gameObject.SetActive(idle && SteamTransport.Available);
            inviteButton.gameObject.SetActive(plugin.SteamHostWithLobby && !connected);
            playButton.gameObject.SetActive(connected && host);
            leaveButton.gameObject.SetActive(!idle);
            bool waitingHost = host && plugin.Online && !connected && !plugin.UsingSteam;
            copyButton.gameObject.SetActive(waitingHost && plugin.HostCode != null);
            upnpButton.gameObject.SetActive(idle);
            lookButton.gameObject.SetActive(ScRuntime.Available().Count > 0);
            lookLabel.text = L("Внешность: ", "Look: ") + LookName(plugin.MyLook) + (plugin.LookSetting == "auto" ? L(" (авто)", " (auto)") : "");
            // The story role: Marcus leads the story (the diner, the ranger), whatever either player looks like.
            characterButton.gameObject.SetActive(!connected);
            string Name(string id) => id == "tessa" ? L("Тесса", "Tessa") : L("Маркус", "Marcus");
            characterLabel.text = L("Роль: ", "Role: ") + (plugin.CharacterSetting == "auto" ? L("авто (хост — Маркус)", "auto (host is Marcus)") : Name(plugin.CharacterSetting));
            if (connected && plugin.PartnerCharacter != null)
                partnerSlot.text += "  (" + Name(plugin.PartnerCharacter) + ")";
            micButton.gameObject.SetActive(true);
            micLabel.text = L("Микрофон: ", "Microphone: ") + (plugin.MicMode == VoiceMode.VoiceActivation ? L("по голосу", "voice activated")
                : plugin.MicMode == VoiceMode.PushToTalk ? L("удерживать ", "hold ") + plugin.TalkKey : L("выкл", "off"));
            upnpLabel.text = L("Авто-порт UPnP (эксперимент): ", "Auto port UPnP (experimental): ") + (plugin.UpnpEnabled ? L("вкл", "on") : L("выкл", "off"));
            if (waitingHost && plugin.HostCode != null)
                status.text = L("Код для друга: ", "Code for your friend: ") + plugin.HostCode + (plugin.UpnpEnabled ? "   (UPnP: " + plugin.UpnpState + ")" : "");
            if (connected && !host) status.text = L("Хост выбирает главу. Игра начнётся у вас автоматически.", "The host is choosing a chapter; your game will follow.");
            var mine = ConnectionPanel.Addresses();
            addresses.gameObject.SetActive(waitingHost);
            addresses.text = L("Ваши адреса для друга:\n", "Your addresses for a friend:\n") + string.Join("\n", mine);
        }
    }

    // The menu's Back resets every panel; ours slides away with them.
    [HarmonyPatch(typeof(UIManagerMainMenu), nameof(UIManagerMainMenu.BackToMainMenu))]
    internal static class MenuBackPatch
    {
        static void Postfix() => MultiplayerMenu.Hide();
    }
}
