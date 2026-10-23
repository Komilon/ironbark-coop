extern alias YarnUnity;
using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
// The game's own dialogue views (View, OptionsListView, OptionView) live in Assembly-CSharp under Yarn.Unity.
using Yarn.Unity;
using DialogueRunner = YarnUnity::Yarn.Unity.DialogueRunner;
using DialogueOption = YarnUnity::Yarn.Unity.DialogueOption;
using LocalizedLine = YarnUnity::Yarn.Unity.LocalizedLine;

namespace IronbarkCoop
{
    // Radio talks in the tower are the team's conversation. The player at the radio runs the game's dialogue; a partner
    // who is inside the tower sees the same lines and answers in a copy of the game's own dialogue panel and hears the
    // voice on their radio. An answer is given when both picked the same one; each pick is marked with a dot in the
    // colour of that player's character (no names). A partner who is outside, steps away (Space), or disconnects is
    // simply not asked; the story moves on for both either way.
    internal sealed class RadioTalk
    {
        // Character colours of the pick markers, the same on both screens.
        internal static readonly string MarcusColor = "#D8A24A", TessaColor = "#6FB7B0";
        const float RadioReach = 7f;

        readonly Plugin plugin;
        // At the radio.
        int serial;
        int[] optionIds;
        int myVote = -1, partnerVote = -1;
        bool partnerJoined, partnerAnswered, talking;
        float optionsAt;
        internal static bool Bypass;
        readonly Dictionary<OptionView, string> plainTexts = new Dictionary<OptionView, string>();
        // Listening in.
        ListenPanel panel;
        int listenSerial = -1, listenMine = -1, listenTheirs = -1;
        bool listening, choosing, awayThisTalk;
        // The talk's current line and open question, kept while away (out of the tower, the phone open), so whoever
        // comes back sees where the talk is at once instead of waiting for its next line.
        bool talkLive;
        string liveSpeaker, liveLine;
        Packet liveOptions;
        public int LinesHeard { get; private set; }
        public string Debug => (talking ? "speaker" : listening ? "listener" : "idle") + "/s" + serial + "/my" + myVote + "/their" + partnerVote + "/joined" + partnerJoined + "/lines" + LinesHeard + "/choosing" + choosing + "/l" + listenMine + "," + listenTheirs + "/line" + (panel != null ? panel.LineInfo : "-") + "/answers" + (panel != null ? panel.Answers : "-");

        public RadioTalk(Plugin plugin) { this.plugin = plugin; }

        static WatchTowerManager Tower => GameManager.instance ? GameManager.instance.watchTowerManager : null;

        internal static bool AtRadio
        {
            get
            {
                var tower = Tower;
                if (!tower) return false;
                // The night 2 radio in bed runs after the night's intro has already cleared "conversing": the running
                // dialogue counts as well.
                var runner = GameManager.instance.uIManager ? GameManager.instance.uIManager.dialogueRunner : null;
                bool talking = GameManager.instance.isConversing || (runner && runner.IsDialogueRunning);
                return tower && talking && (tower.state == WatchTowerManager.State.OnRadio || tower.state == WatchTowerManager.State.OnRadioStanding || tower.state == WatchTowerManager.State.OnRadioBed);
            }
        }

        string Scene => SceneManager.GetActiveScene().name;
        string MyColor => plugin.MyCharacter == "tessa" ? TessaColor : MarcusColor;
        string PartnerColor => (plugin.PartnerCharacter ?? (plugin.MyCharacter == "tessa" ? "marcus" : "tessa")) == "tessa" ? TessaColor : MarcusColor;

        public void SceneLoaded() { ResetSpeaker(); CloseListener(false); panel = null; ForgetLiveTalk(); }
        public void PartnerLeft() { partnerJoined = false; partnerAnswered = true; CloseListener(true); ForgetLiveTalk(); }
        void ForgetLiveTalk() { talkLive = false; liveSpeaker = liveLine = null; liveOptions = null; }

        void ResetSpeaker() { optionIds = null; myVote = partnerVote = -1; partnerJoined = partnerAnswered = false; plainTexts.Clear(); }

        // ---- at the radio ----

        bool PartnerNearRadio()
        {
            var tower = Tower;
            if (!plugin.Ready || !plugin.PartnerPresent || !tower || !tower.radioCollider) return false;
            var offset = plugin.PartnerPosition - tower.radioCollider.transform.position;
            return Mathf.Abs(offset.y) < 3f && new Vector2(offset.x, offset.z).magnitude < RadioReach;
        }

        internal bool VoteNeeded =>
            plugin.Ready && optionIds != null && realChoice && (partnerJoined || (!partnerAnswered && Time.unscaledTime - optionsAt < 2f && PartnerNearRadio()));
        // A single answer is just "go on" (the radio voice's own lines come as one coloured answer): no vote needed.
        bool realChoice;

        // The radio voice's own words, as the game's line view shows them above the answers.
        internal void LocalLine(LocalizedLine line)
        {
            if (!plugin.Ready || !AtRadio || line == null) return;
            talking = true;
            plugin.Send(new Packet { kind = "dlg", code = "line", scene = Scene, name = line.TextWithoutCharacterName.Text, build = line.CharacterName });
            Plugin.Log("TALK_LINE_SENT " + line.TextWithoutCharacterName.Text);
        }

        internal void LocalOptions(DialogueOption[] options)
        {
            if (!plugin.Ready || !AtRadio) return;
            talking = true;
            ResetSpeaker();
            serial++;
            optionIds = new int[options.Length];
            var texts = new string[options.Length];
            int available = 0;
            for (int i = 0; i < options.Length; i++)
            {
                optionIds[i] = options[i].DialogueOptionID;
                texts[i] = options[i].Line.TextWithoutCharacterName.Text;
                if (options[i].IsAvailable) available |= 1 << i;
            }
            optionsAt = Time.unscaledTime;
            realChoice = Bits(available) >= 2;
            plugin.Send(new Packet { kind = "dlg", code = "opts", scene = Scene, arg = serial, changes = texts, bits = available });
            Plugin.Log("TALK_OPTIONS " + serial + " count=" + options.Length + " partnerNear=" + PartnerNearRadio());
        }

        // A click on one of the game's answers: with the partner listening it is a vote, not yet the answer.
        internal bool BeforePick(OptionView view)
        {
            if (Bypass || optionIds == null || view.Option == null) return true;
            int index = Array.IndexOf(optionIds, view.Option.DialogueOptionID);
            if (index < 0 || !VoteNeeded) { Chosen(index); return true; }
            myVote = index;
            plugin.Send(new Packet { kind = "dlg", code = "vote", scene = Scene, arg = serial, bits = index });
            MarkSpeakerOptions();
            if (partnerVote == myVote) { Chosen(index); return true; }
            Plugin.Log("TALK_VOTE_WAIT " + index);
            return false;
        }

        void Chosen(int index)
        {
            if (index >= 0 && plugin.Ready) plugin.Send(new Packet { kind = "dlg", code = "chosen", scene = Scene, arg = serial, bits = index });
            RestoreSpeakerTexts();
            ResetSpeaker();
            Plugin.Log("TALK_CHOSEN " + index);
        }

        // Pick the agreed (or, once the partner is no longer asked, our own) answer on the game's option button.
        void Commit(int index)
        {
            if (optionIds == null || index < 0 || index >= optionIds.Length) return;
            int id = optionIds[index];
            foreach (var view in UnityEngine.Object.FindObjectsOfType<OptionView>())
            {
                if (view.Option == null || view.Option.DialogueOptionID != id || !view.gameObject.activeInHierarchy) continue;
                Chosen(index);
                Bypass = true;
                try { view.InvokeOptionSelected(); } finally { Bypass = false; }
                return;
            }
        }

        void MarkSpeakerOptions()
        {
            if (optionIds == null) return;
            foreach (var view in UnityEngine.Object.FindObjectsOfType<OptionView>())
            {
                if (view.Option == null) continue;
                int index = Array.IndexOf(optionIds, view.Option.DialogueOptionID);
                var text = AccessTools.Field(typeof(OptionView), "text").GetValue(view) as TextMeshProUGUI;
                if (index < 0 || !text) continue;
                if (!plainTexts.TryGetValue(view, out var plain)) plainTexts[view] = plain = text.text;
                text.text = Marked(plain, index == myVote, index == partnerVote, MyColor, PartnerColor);
            }
        }

        void RestoreSpeakerTexts()
        {
            foreach (var pair in plainTexts)
            {
                if (!pair.Key) continue;
                var text = AccessTools.Field(typeof(OptionView), "text").GetValue(pair.Key) as TextMeshProUGUI;
                if (text) text.text = pair.Value;
            }
            plainTexts.Clear();
        }

        static int Bits(int mask) { int n = 0; for (; mask != 0; mask &= mask - 1) n++; return n; }

        internal static string Marked(string plain, bool mine, bool theirs, string myColor, string theirColor)
        {
            string dots = (mine ? "<color=" + myColor + ">●</color>" : "") + (theirs ? "<color=" + theirColor + ">●</color>" : "");
            return dots.Length > 0 ? dots + " " + plain : plain;
        }

        internal void LocalVoice(string method, string index)
        {
            if (!plugin.Ready || !AtRadio) return;
            plugin.Send(new Packet { kind = "dlg", code = "vo", scene = Scene, name = method, code2 = index });
        }

        internal void LocalEnd()
        {
            if (!talking) return;
            talking = false;
            RestoreSpeakerTexts();
            ResetSpeaker();
            if (plugin.Ready) plugin.Send(new Packet { kind = "dlg", code = "end", scene = Scene });
        }

        public void Tick()
        {
            if (talking && !AtRadio) LocalEnd();
            // Waiting for the partner: their vote may have arrived, or they are no longer part of the talk.
            if (optionIds != null && myVote >= 0)
            {
                if (partnerVote == myVote) Commit(myVote);
                else if (!VoteNeeded) { Plugin.Log("TALK_PARTNER_NOT_ASKED"); Commit(myVote); }
            }
            ListenTick();
        }


        void AbandonOwnTalk()
        {
            var tower = Tower;
            var runner = GameManager.instance.uIManager ? GameManager.instance.uIManager.dialogueRunner : null;
            Plugin.Log("TALK_DUPLICATE_LEFT state=" + (tower ? tower.state.ToString() : "-"));
            RestoreSpeakerTexts();
            ResetSpeaker();
            talking = false;
            if (tower) tower.ExitRadio();
            if (runner && runner.IsDialogueRunning) runner.Stop();
        }
        // ---- listening in ----

        bool CanListen
        {
            get
            {
                var tower = Tower;
                var gm = GameManager.instance;
                // Asleep and dreaming, Marcus is still "inside" by the tower's flag, but nowhere near the radio.
                return plugin.Ready && tower && InTowerRoom(tower) && !gm.isDead && !(gm.isConversing && !choosing) && !awayThisTalk && !plugin.Dream.Dreaming;
            }
        }

        // The tower's isPlayerInside is set by the trigger in the doorway, so it goes stale when the game moves the
        // player without walking through it (the coffee on the balcony after a night in the tower): Connor's talks were
        // shown on the balcony until Marcus walked in and out once. That trigger is only a strip in the doorway, so the
        // room is the box around the game's own furniture in it (stove, bed, table, radio), closed by the doorway strip
        // on the door's side.
        static WatchTowerManager roomTower;
        static Bounds room;
        static bool hasRoom;
        internal static bool InTowerRoom(WatchTowerManager tower)
        {
            if (roomTower != tower) { roomTower = tower; hasRoom = MeasureRoom(tower, out room); }
            if (!hasRoom) return tower.isPlayerInside;
            var gm = GameManager.instance;
            // Lying in bed the walking body is switched off wherever it was left: the bed's camera is where the player is.
            Vector3 at = gm.playerFP && gm.playerFP.activeInHierarchy ? gm.playerFP.transform.position : Camera.main ? Camera.main.transform.position : gm.playerFP.transform.position;
            return room.Contains(at);
        }

        static bool MeasureRoom(WatchTowerManager tower, out Bounds box)
        {
            box = default;
            Transform doorway = null;
            foreach (var trigger in UnityEngine.Object.FindObjectsOfType<OnTrigger>(true))
                for (int i = 0; i < trigger.triggerEvent.GetPersistentEventCount(); i++)
                    if (trigger.triggerEvent.GetPersistentMethodName(i) == nameof(WatchTowerManager.PlayerInside)) doorway = trigger.transform;
            var things = new[] { tower.woodStovePosition ? tower.woodStovePosition.gameObject : null, tower.bed, tower.table, tower.radioCollider, tower.computer };
            if (!doorway || Array.Exists(things, t => !t)) { Plugin.Log("TOWER_ROOM_UNKNOWN"); return false; }
            var furniture = new Bounds(things[0].transform.position, Vector3.zero);
            foreach (var t in things) furniture.Encapsulate(t.transform.position);
            // Half a metre past the furniture to the walls, except on the doorway's side, where the strip is the wall.
            Vector3 door = doorway.position, min = furniture.min - new Vector3(0.6f, 0f, 0.6f), max = furniture.max + new Vector3(0.6f, 0f, 0.6f);
            if (door.x <= furniture.min.x) min.x = door.x; else if (door.x >= furniture.max.x) max.x = door.x;
            if (door.z <= furniture.min.z) min.z = door.z; else if (door.z >= furniture.max.z) max.z = door.z;
            min.y = door.y - 2f; max.y = door.y + 2.5f;
            box = new Bounds((min + max) / 2f, max - min);
            Plugin.Log("TOWER_ROOM " + box);
            return true;
        }

        void ListenTick()
        {
            if (!listening && talkLive && !awayThisTalk && CanListen && !GameManager.instance.isPaused) Rejoin();
            if (!listening) return;
            if (!CanListen && !choosing) { CloseListener(true); return; }
            var gm = GameManager.instance;
            // The phone (Esc) covers the talk while it is open; the talk goes on and shows again when it closes.
            bool paused = gm.isPaused;
            panel?.SetVisible(!paused);
            if (paused) { wasPaused = true; return; }
            if (wasPaused)
            {
                wasPaused = false;
                // Closing the phone locks and hides the cursor; the answers still need it.
                if (choosing) gm.ShowCursor();
                // The panel copied from the game's dialogue view empties its line when shown again: put the talk's
                // current line and question back.
                if (liveLine != null) panel?.ShowLine(liveSpeaker, liveLine);
                if (choosing && liveOptions != null && liveOptions.changes != null && liveOptions.arg == listenSerial && panel != null)
                {
                    panel.ShowOptions(liveOptions.changes, liveOptions.bits, Pick);
                    panel.Mark(listenMine, listenTheirs, MyColor, PartnerColor);
                }
                Plugin.Log("TALK_RESTORED_AFTER_PHONE line=" + (liveLine != null));
            }
            if (choosing)
            {
                // Space steps away from the talk; leaving the tower does too.
                bool away = Input.GetKeyDown(KeyCode.Space) || (gm.inputHandler && gm.inputHandler.spaceInput) || !Tower || !InTowerRoom(Tower);
                if (away) { awayThisTalk = true; plugin.Send(new Packet { kind = "dlg", code = "skip", scene = Scene, arg = listenSerial }); CloseListener(true); Plugin.Log("TALK_STEPPED_AWAY"); }
            }
            panel?.Tick();
        }

        bool wasPaused;

        void Listen(Packet packet)
        {
            switch (packet.code)
            {
                case "line":
                    talkLive = true; liveSpeaker = packet.build; liveLine = packet.name; liveOptions = null;
                    if (!CanListen) return;
                    EnsurePanel();
                    listening = true;
                    LinesHeard++;
                    panel.ShowLine(packet.build, packet.name);
                    Plugin.Log("TALK_LINE_SHOWN " + panel.LineInfo + " " + packet.name);
                    break;
                case "opts":
                    talkLive = true; liveOptions = packet;
                    if (!CanListen || packet.changes == null)
                    {
                        plugin.Send(new Packet { kind = "dlg", code = "skip", scene = Scene, arg = packet.arg });
                        return;
                    }
                    ShowQuestion(packet);
                    break;
                case "vote":
                    if (packet.arg != listenSerial || !choosing) return;
                    listenTheirs = packet.bits;
                    panel.Mark(listenMine, listenTheirs, MyColor, PartnerColor);
                    break;
                case "chosen":
                    if (liveOptions != null && liveOptions.arg == packet.arg) liveOptions = null;
                    if (packet.arg != listenSerial) return;
                    EndChoosing();
                    panel?.HideOptions();
                    break;
                case "end":
                    ForgetLiveTalk();
                    awayThisTalk = false;
                    CloseListener(true);
                    var tower = Tower;
                    if (tower && tower.radioVoiceOver && !AtRadio) tower.radioVoiceOver.radioVOAS.gameObject.SetActive(false);
                    break;
                case "vo":
                    if (!CanListen) return;
                    var voice = Tower ? Tower.radioVoiceOver : null;
                    var method = voice ? AccessTools.Method(typeof(RadioVoiceOver), packet.name) : null;
                    if (method == null) return;
                    voice.radioVOAS.gameObject.SetActive(true);
                    try { method.Invoke(voice, new object[] { packet.code2 }); } catch (Exception ex) { Plugin.Log("TALK_VO_FAILED " + ex.Message); }
                    break;
            }
        }

        void ShowQuestion(Packet packet)
        {
            EnsurePanel();
            listening = true;
            listenSerial = packet.arg; listenMine = listenTheirs = -1;
            panel.ShowOptions(packet.changes, packet.bits, Pick);
            if (Bits(packet.bits) >= 2) BeginChoosing();
            plugin.Send(new Packet { kind = "dlg", code = "join", scene = Scene, arg = packet.arg });
        }

        // Back in the tower (or the phone closed) while the talk goes on: its current line, and its question if still
        // open (the vote is joined again if the speaker has not answered yet).
        void Rejoin()
        {
            EnsurePanel();
            listening = true;
            if (liveLine != null) panel.ShowLine(liveSpeaker, liveLine);
            if (liveOptions != null && liveOptions.changes != null) ShowQuestion(liveOptions);
            Plugin.Log("TALK_REJOINED line=" + (liveLine != null) + " question=" + (liveOptions != null));
        }

        void Pick(int index)
        {
            if (!choosing) return;
            listenMine = index;
            panel.Mark(listenMine, listenTheirs, MyColor, PartnerColor);
            plugin.Send(new Packet { kind = "dlg", code = "vote", scene = Scene, arg = listenSerial, bits = index });
            Plugin.Log("TALK_LISTENER_VOTE " + index);
        }

        internal void LabPick(int index)
        {
            if (choosing) { Pick(index); return; }
            if (optionIds != null && index >= 0 && index < optionIds.Length)
                foreach (var view in UnityEngine.Object.FindObjectsOfType<OptionView>())
                    if (view.Option != null && view.Option.DialogueOptionID == optionIds[index]) { view.InvokeOptionSelected(); return; }
        }

        bool cursorWasShown, controllerWasEnabled;
        void BeginChoosing()
        {
            if (choosing) return;
            choosing = true;
            var gm = GameManager.instance;
            controllerWasEnabled = gm.firstPersonController && gm.firstPersonController.enabled;
            if (gm.firstPersonController) gm.firstPersonController.enabled = false;
            cursorWasShown = Cursor.visible;
            gm.ShowCursor();
            panel.ShowHint(ConnectionPanel.L("[ПРОБЕЛ] Отойти", "[SPACE] Step away"));
        }

        void EndChoosing()
        {
            if (!choosing) return;
            choosing = false;
            var gm = GameManager.instance;
            if (gm && gm.firstPersonController && controllerWasEnabled && !gm.isConversing) gm.firstPersonController.enabled = true;
            if (gm && !cursorWasShown && !gm.isConversing) gm.HideCursor();
            panel?.ShowHint("");
        }

        void CloseListener(bool fade)
        {
            EndChoosing();
            listening = false;
            listenSerial = -1;
            panel?.Hide();
        }

        void EnsurePanel()
        {
            if (panel == null || !panel.Alive) panel = ListenPanel.Create();
        }

        public void Receive(Packet packet)
        {
            if (packet.scene != Scene) return;
            switch (packet.code)
            {
                case "join": if (packet.arg == serial) { partnerJoined = true; partnerAnswered = true; MarkSpeakerOptions(); } return;
                case "skip": if (packet.arg == serial || packet.arg == -1) { partnerJoined = false; partnerAnswered = true; partnerVote = -1; MarkSpeakerOptions(); } return;
                case "vote":
                    if (talking && packet.arg == serial && optionIds != null)
                    {
                        partnerVote = packet.bits;
                        MarkSpeakerOptions();
                        Plugin.Log("TALK_PARTNER_VOTE " + partnerVote);
                        return;
                    }
                    break;
            }
            // Both started the same radio talk at once (each world ran the story's trigger before the other's "done" flag
            // arrived): the guest leaves its own copy and listens to the host's.
            if (talking && !plugin.IsHost && (packet.code == "line" || packet.code == "opts") && AtRadio) AbandonOwnTalk();
            if (talking) return;
            Listen(packet);
        }
    }

    // The partner's side of a radio talk: a copy of the game's dialogue panel (its fonts, colours, answer buttons),
    // without the game's dialogue logic.
    internal sealed class ListenPanel
    {
        GameObject root;
        CanvasGroup lineGroup, optionsGroup;
        TextMeshProUGUI lineText, hintText;
        Transform optionsParent;
        OptionView optionTemplate;
        readonly List<OptionView> options = new List<OptionView>();
        readonly List<string> plain = new List<string>();
        float lineUntil;
        public bool Alive => root;
        // Lab: is the partner's line on screen, and how tall its slot is.
        // Lab: our answers on screen against every answer button in the list ("2of2"; a leftover button makes it "2of3").
        public string Answers { get { int ours = 0, shown = 0; foreach (var v in options) if (v && v.gameObject.activeInHierarchy) ours++; if (optionsParent && optionTemplate) foreach (Transform child in optionsParent) if (child.gameObject.activeInHierarchy && child.name.StartsWith(optionTemplate.name)) shown++; return ours + "of" + shown; } }
        public string LineInfo => lineText ? (lineText.gameObject.activeInHierarchy && lineGroup && lineGroup.alpha > 0.5f && lineText.text.Length > 0 ? "shown" : "hidden")
            + Mathf.RoundToInt(lineText.TryGetComponent<UnityEngine.UI.LayoutElement>(out var slot) ? slot.preferredHeight : lineText.rectTransform.rect.height) : "none";

        public static ListenPanel Create()
        {
            var ui = GameManager.instance.uIManager;
            var source = ui.dialogueCanvas ? ui.dialogueCanvas.gameObject : null;
            var panel = new ListenPanel();
            if (!source) return panel;
            var lineView = source.GetComponentInChildren<View>(true);
            var listView = source.GetComponentInChildren<OptionsListView>(true);
            panel.optionTemplate = listView ? AccessTools.Field(typeof(OptionsListView), "optionViewPrefab").GetValue(listView) as OptionView : null;
            // Copy the canvas with everything inside, then take the game's logic out of the copy.
            var holder = new GameObject("Coop talk holder");
            holder.SetActive(false);
            panel.root = UnityEngine.Object.Instantiate(source, holder.transform);
            panel.root.name = "Coop radio talk";
            string Path(Component c) => c ? ItemSync.PathOf(c.transform).Substring(ItemSync.PathOf(source.transform).Length) : null;
            Transform Find(string relative) => relative == null ? null : relative == "" ? panel.root.transform : panel.root.transform.Find(Strip(relative));
            // The game's panel: one "Content" group (fading, vertical layout) holds the line text and, below it, the answers.
            if (lineView)
            {
                panel.lineGroup = Find(Path(AccessTools.Field(typeof(View), "canvasGroup").GetValue(lineView) as Component))?.GetComponent<CanvasGroup>();
                panel.lineText = Find(Path(AccessTools.Field(typeof(View), "lineText").GetValue(lineView) as Component))?.GetComponent<TextMeshProUGUI>();
            }
            if (listView)
            {
                panel.optionsParent = Find(Path(listView));
                panel.optionsGroup = Find(Path(AccessTools.Field(typeof(OptionsListView), "canvasGroup").GetValue(listView) as Component))?.GetComponent<CanvasGroup>();
            }
            // Answer buttons the game's panel still holds from the player's own last talk are not ours (an old "Got it,
            // thanks" showed in every talk heard afterwards). They go before the game's components are stripped: the
            // button itself is the OptionView.
            if (panel.optionsParent)
                foreach (var old in panel.optionsParent.GetComponentsInChildren<OptionView>(true)) UnityEngine.Object.DestroyImmediate(old.gameObject);
            // The dialogue logic goes; the look stays (fonts, layout, the dialogue camera with its VHS filter).
            foreach (var behaviour in panel.root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                var type = behaviour.GetType();
                if (behaviour is YarnUnity::Yarn.Unity.DialogueViewBase || type.Assembly.GetName().Name.StartsWith("YarnSpinner") || type.Name == "OptionView" || type.Name == "Options" || type.Name == "dialogueSFX")
                    UnityEngine.Object.DestroyImmediate(behaviour);
            }
            panel.root.transform.SetParent(null, false);
            UnityEngine.Object.Destroy(holder);
            panel.root.SetActive(true);
            var canvas = panel.root.GetComponentInChildren<Canvas>(true);
            if (panel.lineText && canvas)
            {
                // A small hint line at the bottom of the screen, in the dialogue's own font.
                panel.hintText = UnityEngine.Object.Instantiate(panel.lineText.gameObject, canvas.transform).GetComponent<TextMeshProUGUI>();
                if (panel.hintText.TryGetComponent<UnityEngine.UI.LayoutElement>(out var layout)) UnityEngine.Object.Destroy(layout);
                var rt = panel.hintText.rectTransform;
                rt.localScale = Vector3.one; rt.localRotation = Quaternion.identity;
                rt.anchorMin = new Vector2(0.5f, 0f); rt.anchorMax = new Vector2(0.5f, 0f); rt.pivot = new Vector2(0.5f, 0f);
                rt.anchoredPosition = new Vector2(0f, 14f); rt.sizeDelta = new Vector2(600f, 20f);
                panel.hintText.fontSize = panel.lineText.fontSize * 0.75f;
                panel.hintText.alignment = TextAlignmentOptions.Bottom;
                panel.hintText.text = "";
            }
            // Clicks on the answers need an event system; the game keeps its own switched off outside its dialogues.
            if (!UnityEngine.EventSystems.EventSystem.current)
            {
                var events = new GameObject("Coop talk events");
                events.transform.SetParent(panel.root.transform, false);
                events.AddComponent<UnityEngine.EventSystems.EventSystem>();
                events.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }
            panel.Hide();
            Plugin.Log("TALK_PANEL lineText=" + (panel.lineText != null) + " options=" + (panel.optionsParent != null) + " template=" + (panel.optionTemplate != null));
            return panel;
        }

        static string Strip(string relative) => System.Text.RegularExpressions.Regex.Replace(relative.TrimStart('/'), @"#\d+", "");

        public void ShowLine(string speaker, string text)
        {
            if (!root) return;
            if (lineGroup) { lineGroup.gameObject.SetActive(true); lineGroup.alpha = 1f; }
            if (lineText)
            {
                lineText.gameObject.SetActive(true);
                lineText.text = "<font=dogicapixelbold SDF><mark=#00000090>" + text + "</mark>";
                lineText.maxVisibleCharacters = int.MaxValue;
                // The game sizes the line's layout slot while typing it out (from its hidden dummy text); the copy was
                // taken with an empty slot, so size it for the whole line here or the answers cover it.
                if (lineText.TryGetComponent<UnityEngine.UI.LayoutElement>(out var slot))
                    slot.preferredHeight = lineText.GetPreferredValues(lineText.text, lineText.rectTransform.rect.width, 0f).y;
            }
            lineUntil = Time.unscaledTime + Mathf.Clamp(2f + (text ?? "").Length * 0.06f, 3f, 12f);
        }

        public void ShowOptions(string[] texts, int available, Action<int> pick)
        {
            HideOptions();
            if (!root || !optionTemplate || !optionsParent) return;
            for (int i = 0; i < texts.Length; i++)
            {
                if ((available & (1 << i)) == 0) { options.Add(null); plain.Add(texts[i]); continue; }
                var view = UnityEngine.Object.Instantiate(optionTemplate, optionsParent, false);
                int index = i;
                view.Option = new DialogueOption { DialogueOptionID = i, Line = new LocalizedLine { Text = new Yarn.Markup.MarkupParseResult { Text = texts[i], Attributes = new List<Yarn.Markup.MarkupAttribute>() } }, IsAvailable = true };
                view.OnOptionSelected = _ => pick(index);
                view.gameObject.SetActive(true);
                // The game rewrites the text it is given (colour words, brackets); mark what it shows.
                var shown = AccessTools.Field(typeof(OptionView), "text").GetValue(view) as TextMeshProUGUI;
                options.Add(view); plain.Add(shown ? shown.text : texts[i]);
            }
            if (optionsGroup) { optionsGroup.alpha = 1f; optionsGroup.interactable = true; optionsGroup.blocksRaycasts = true; }
            lineUntil = float.MaxValue;
        }

        public void Mark(int mine, int theirs, string myColor, string theirColor)
        {
            for (int i = 0; i < options.Count; i++)
            {
                var view = options[i];
                if (!view) continue;
                var text = AccessTools.Field(typeof(OptionView), "text").GetValue(view) as TextMeshProUGUI;
                if (text) text.text = RadioTalk.Marked(plain[i], i == mine, i == theirs, myColor, theirColor);
                // Clicking again lets the player change their mind until both agree.
                AccessTools.Field(typeof(OptionView), "hasSubmittedOptionSelection").SetValue(view, false);
            }
        }

        public void SetVisible(bool visible) { if (root && root.activeSelf != visible) root.SetActive(visible); }
        public void Destroy() { if (root) UnityEngine.Object.Destroy(root); root = null; }
        public void ShowHint(string text) { if (hintText) hintText.text = text; }

        public void HideOptions()
        {
            foreach (var view in options) if (view) UnityEngine.Object.Destroy(view.gameObject);
            options.Clear(); plain.Clear();
            if (optionsGroup) { optionsGroup.interactable = false; optionsGroup.blocksRaycasts = false; if (optionsGroup != lineGroup) optionsGroup.alpha = 0f; }
            if (lineUntil == float.MaxValue) lineUntil = Time.unscaledTime + 3f;
        }

        public void Hide()
        {
            HideOptions();
            if (lineGroup) lineGroup.alpha = 0f;
            if (lineText) { lineText.text = ""; if (lineText.TryGetComponent<UnityEngine.UI.LayoutElement>(out var slot)) slot.preferredHeight = 0f; }
            ShowHint("");
            lineUntil = 0f;
        }

        public void Tick()
        {
            if (lineGroup && lineUntil > 0f && Time.unscaledTime > lineUntil && options.Count == 0) { lineGroup.alpha = 0f; lineUntil = 0f; }
        }
    }

    // The game's own line view (radio talks show the voice's words there, above the answers).
    [HarmonyPatch(typeof(View), nameof(View.RunLine))]
    internal static class TalkLinePatch
    {
        static void Postfix(LocalizedLine dialogueLine) => Plugin.Instance?.Talk.LocalLine(dialogueLine);
    }

    [HarmonyPatch(typeof(DialogueRunner), "HandleOptions")]
    internal static class TalkOptionsPatch
    {
        // The options as the views got them (the runner builds DialogueOption[] inside HandleOptions).
        static void Prefix(Yarn.OptionSet options, DialogueRunner __instance)
        {
            var plugin = Plugin.Instance;
            if (plugin == null || !RadioTalk.AtRadio) return;
            var provider = __instance.lineProvider;
            if (provider == null) return;
            var list = new DialogueOption[options.Options.Length];
            for (int i = 0; i < list.Length; i++)
            {
                var line = provider.GetLocalizedLine(options.Options[i].Line);
                string text = Yarn.Dialogue.ExpandSubstitutions(line.RawText, options.Options[i].Line.Substitutions);
                Yarn.Markup.MarkupParseResult parsed;
                try { parsed = __instance.Dialogue.ParseMarkup(text); }
                catch { parsed = new Yarn.Markup.MarkupParseResult { Text = text, Attributes = new List<Yarn.Markup.MarkupAttribute>() }; }
                line.Text = parsed;
                list[i] = new DialogueOption { DialogueOptionID = options.Options[i].ID, Line = line, IsAvailable = options.Options[i].IsAvailable };
            }
            plugin.Talk.LocalOptions(list);
        }
    }

    [HarmonyPatch(typeof(DialogueRunner), "HandleDialogueComplete")]
    internal static class TalkEndPatch
    {
        static void Postfix() => Plugin.Instance?.Talk.LocalEnd();
    }

    [HarmonyPatch(typeof(OptionView), nameof(OptionView.InvokeOptionSelected))]
    internal static class TalkPickPatch
    {
        static bool Prefix(OptionView __instance)
        {
            var plugin = Plugin.Instance;
            if (plugin == null || RadioTalk.Bypass || !RadioTalk.AtRadio) return true;
            return plugin.Talk.BeforePick(__instance);
        }
    }

    [HarmonyPatch]
    internal static class TalkVoicePatch
    {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            foreach (var method in typeof(RadioVoiceOver).GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly))
                if (method.Name.StartsWith("VO_")) yield return method;
        }

        static void Postfix(System.Reflection.MethodBase __originalMethod, string index) => Plugin.Instance?.Talk.LocalVoice(__originalMethod.Name, index);
    }
}
