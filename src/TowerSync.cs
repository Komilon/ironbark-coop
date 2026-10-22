using System;
using System.Collections;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IronbarkCoop
{
    // The watchtower's shared machinery that the field mirror alone gets wrong:
    // - the night 7 power cut runs in whichever world ran the system update; the partner's world gets the same cut
    //   (lights, beeping generator that can be clicked) without being thrown off a chair they are not on, and the
    //   update counts as done for both, so the cut happens once;
    // - the computer is one machine: the password and the update are shared and its monitor shows what the partner
    //   at the desk sees (a small picture of their screen, about twice a second, while the partner is nearby);
    // - the chair at the desk seats one player.
    internal sealed class TowerSync
    {
        readonly Plugin plugin;
        bool revertChair;
        float mySitSince = -1f, partnerChairSince = -1f;
        bool sentPassword, sentUpdate;
        float nextShot;
        bool shotPending, finalShotPending;
        RenderTexture full, small;
        Texture2D shot, received;
        public int ShotsSent { get; private set; }
        public int ShotsReceived { get; private set; }
        public static int PowerCuts { get; private set; }

        public TowerSync(Plugin plugin) { this.plugin = plugin; }

        static WatchTowerManager Tower => GameManager.instance ? GameManager.instance.watchTowerManager : null;
        static ComputerManager Computer => GameManager.instance && GameManager.instance.uIManager ? GameManager.instance.uIManager.computerManager : null;

        public static bool LocalChair()
        {
            var tower = Tower;
            return tower && (tower.state == WatchTowerManager.State.OnChair || tower.state == WatchTowerManager.State.InComputer || tower.state == WatchTowerManager.State.OnRadio);
        }

        public void SceneLoaded()
        {
            revertChair = false; mySitSince = partnerChairSince = -1f; sentPassword = sentUpdate = sentBoot = false; shotPending = finalShotPending = false;
            PartnerBooted = false; sentWindows = null;
            PowerCuts = 0;
        }

        public void Tick()
        {
            var tower = Tower;
            if (!tower) return;
            Chair(tower);
            // The open stove takes wood from whoever holds some: the game enables its "put the wood in" spot only in the
            // world of the player who opened it, and only if that player held wood at that moment.
            var pickup = GameManager.instance.itemPickUp;
            if (tower.woodCheck && tower.isStoveOpen && !tower.woodInStove && pickup && tower.woodCheck.enabled != pickup.inHandWood) tower.woodCheck.enabled = pickup.inHandWood;
            if (!plugin.Ready) return;
            ShareComputer();
            Screen(tower);
        }

        // ---- the desk chair ----

        internal static bool ChairTaken => Plugin.Instance != null && Plugin.Instance.PartnerSeat == "tower-chair";

        // The game sets state = OnChair right after starting the (refused) fade; put it back.
        internal void RefuseChair() { revertChair = true; }

        void Chair(WatchTowerManager tower)
        {
            if (revertChair)
            {
                revertChair = false;
                if ((tower.state == WatchTowerManager.State.OnChair || tower.state == WatchTowerManager.State.InComputer) && GameManager.instance.playerFP.activeSelf)
                {
                    tower.state = WatchTowerManager.State.Inside;
                    if (tower.table) tower.table.tag = "Clickable";
                }
            }
            bool mine = LocalChair();
            if (!mine) mySitSince = -1f; else if (mySitSince < 0f) mySitSince = Time.unscaledTime;
            bool theirs = ChairTaken;
            if (!theirs) partnerChairSince = -1f; else if (partnerChairSince < 0f) partnerChairSince = Time.unscaledTime;
            // Only a race (both sat within moments) can end here; the fade into the chair finishes first.
            bool seatedHere = tower.state == WatchTowerManager.State.OnChair || tower.state == WatchTowerManager.State.InComputer;
            if (!mine || !theirs || !seatedHere || yielding || GameManager.instance.uIManager.inTransition || Time.unscaledTime - mySitSince > 4f) return;
            // Both sat down at once (on the chair or straight at the screen): whoever was there first keeps the chair;
            // a tie goes to the host.
            float lead = mySitSince - partnerChairSince;
            bool yield = lead > 0.3f || (Mathf.Abs(lead) <= 0.3f && !plugin.IsHost);
            if (!yield) return;
            Plugin.Log("TOWER_CHAIR_YIELDED lead=" + lead.ToString("F2") + " from=" + tower.state);
            plugin.StartCoroutine(YieldChair(tower));
        }

        bool yielding;

        IEnumerator YieldChair(WatchTowerManager tower)
        {
            yielding = true;
            var ui = GameManager.instance.uIManager;
            try
            {
                // At the screen: leave it the game's own way (Escape at the computer), then stand up.
                if (tower.state == WatchTowerManager.State.InComputer)
                {
                    ui.computerManager.CloseZombieGameWindow();
                    tower.state = WatchTowerManager.State.OnChair;
                    yield return ui.StartCoroutine(ui.FadeToComputer());
                    while (ui.inTransition) yield return null;
                }
                ui.StartCoroutine(ui.FadeToFPSTower());
                tower.state = WatchTowerManager.State.Inside;
                if (tower.table) tower.table.tag = "Clickable";
                ui.controlsText.text = "";
                GameManager.instance.subtitle.ShowSubAndClose(ConnectionPanel.L("Это место занято.", "That seat is taken."), 2f);
                mySitSince = -1f;
            }
            finally { yielding = false; }
        }

        // ---- the power cut ----

        internal void LocalPowerOut()
        {
            if (!plugin.Ready || RemoteCalls.Applying) return;
            plugin.Send(new Packet { kind = "tower", code = "powerout", scene = SceneManager.GetActiveScene().name });
        }

        // The radio runs on the tower's power: it goes quiet with the lights (in each world, without echoing).
        internal static IEnumerator RadioOffWithPower(WatchTowerManager tower)
        {
            yield return new WaitForSeconds(3f);
            PowerCuts++;
            if (!tower || !tower.radio || tower.radio.isStopped) yield break;
            RemoteCalls.Applying = true;
            try { tower.radio.Stop(); } finally { RemoteCalls.Applying = false; }
            Plugin.Log("TOWER_RADIO_POWER_OFF");
        }

        // The world half of WatchTowerManager.PowerOut: same lights, flicker, beep and clickable generator.
        IEnumerator WorldPowerOut(WatchTowerManager tower)
        {
            yield return new WaitForSeconds(3f);
            if (!tower) yield break;
            PowerCuts++;
            tower.seq7PowerOut = true;
            Lights(tower, false);
            tower.radioScreenMaterial.DisableKeyword("_EMISSION");
            var computer = Computer;
            if (computer) computer.PowerOut();
            // Only someone actually working at the computer is thrown off it.
            var gm = GameManager.instance;
            if (tower.state == WatchTowerManager.State.InComputer)
            {
                tower.state = WatchTowerManager.State.OnChair;
                gm.onComputer = false;
                gm.uIManager.computerUI.gameObject.SetActive(false);
                gm.uIManager.crosshairParent.SetActive(true);
                tower.movingCam.enabled = true; tower.fovZoom.enabled = true;
                gm.HideCursor();
            }
            tower.isGeneratorOn = false;
            tower.redLight.SetActive(true); tower.greenLight.SetActive(false);
            tower.generatorRunningAS.Stop();
            tower.poweroutBeep.Play();
            tower.generator.tag = "Clickable";
            tower.shedLight.SetActive(false);
            tower.shedLightMaterial.DisableKeyword("_EMISSION");
            foreach (var light in tower.outsideLights) light.SetActive(false);
            tower.floodLight.DisableKeyword("_EMISSION");
            if (tower.radio && !tower.radio.isStopped)
            {
                RemoteCalls.Applying = true;
                try { tower.radio.Stop(); } finally { RemoteCalls.Applying = false; }
            }
            foreach (float pause in new[] { 0.5f, 0.5f, 0.6f })
            {
                yield return new WaitForSeconds(pause);
                if (!tower || tower.isGeneratorOn) yield break;
                Lights(tower, !tower.middleLight.activeSelf);
            }
            if (tower && !tower.isGeneratorOn) Lights(tower, false);
            Plugin.Log("TOWER_POWER_OUT_MIRRORED");
        }

        static void Lights(WatchTowerManager tower, bool on)
        {
            tower.middleLight.SetActive(on); tower.tubeLight.SetActive(on);
            if (on) tower.tubeLightMaterial.EnableKeyword("_EMISSION"); else tower.tubeLightMaterial.DisableKeyword("_EMISSION");
            if (on) tower.lightSwitchBox.DisableKeyword("_EMISSION"); else tower.lightSwitchBox.EnableKeyword("_EMISSION");
            tower.lampMaterial2.SetFloat("_Brightness", 0f);
            tower.lamp.SetActive(on);
            if (on) tower.lampMaterial.EnableKeyword("_EMISSION"); else tower.lampMaterial.DisableKeyword("_EMISSION");
            if (on) tower.bedSideLamp.EnableKeyword("_EMISSION"); else tower.bedSideLamp.DisableKeyword("_EMISSION");
            tower.bedSideLampLight.SetActive(on);
        }

        // ---- the computer ----

        void ShareComputer()
        {
            var computer = Computer;
            if (!computer) return;
            bool updated = computer.consoleManager && computer.consoleManager.hasUpdated;
            bool booted = computer.bootUpDone;
            if (computer.passwordEntered != sentPassword || updated != sentUpdate || booted != sentBoot)
            {
                sentPassword = computer.passwordEntered; sentUpdate = updated; sentBoot = booted;
                plugin.Send(new Packet { kind = "tower", code = "pc", active = sentPassword, bits = (sentUpdate ? 1 : 0) | (sentBoot ? 2 : 0), scene = SceneManager.GetActiveScene().name });
            }
            // What is open on the screen, while we work at it: the partner's copy of the computer shows the same.
            if (GameManager.instance.onComputer && Time.unscaledTime >= nextWindows)
            {
                nextWindows = Time.unscaledTime + 0.5f;
                string state = WindowState(computer);
                if (state != sentWindows) { sentWindows = state; plugin.Send(new Packet { kind = "tower", code = "pcwin", name = state, scene = SceneManager.GetActiveScene().name }); }
            }
        }


        // ---- one computer for both: booted once, the same windows open ----

        bool sentBoot;
        internal static bool PartnerBooted;
        string sentWindows; float nextWindows;
        // The window flags (formOpen, sheetsOpen...). Terminal, minigame and video playback keep state the copy cannot
        // rebuild from a flag, so they stay each player's own.
        static readonly System.Reflection.FieldInfo[] WindowFlags = Array.FindAll(typeof(ComputerManager).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic),
            f => f.FieldType == typeof(bool) && f.Name.Contains("Open") && f.Name != "videoOpen" && f.Name != "terminalOpen" && f.Name != "zombiePopUpOpen");

        static bool Shared(ComputerManager pc, GameObject window) => window && window != pc.terminalWindow && window != pc.zombiePopUpWindow;

        static string WindowState(ComputerManager pc)
        {
            var s = new System.Text.StringBuilder();
            if (pc.allWindows != null) foreach (var window in pc.allWindows) s.Append(!Shared(pc, window) ? '-' : window.activeSelf ? '1' : '0');
            s.Append('|');
            foreach (var flag in WindowFlags) s.Append((bool)flag.GetValue(pc) ? '1' : '0');
            return s.ToString();
        }

        void ApplyWindows(ComputerManager pc, string state)
        {
            // Our own screen while we sit at it is ours.
            if (GameManager.instance.onComputer || string.IsNullOrEmpty(state)) return;
            int bar = state.IndexOf('|');
            if (bar < 0 || pc.allWindows == null || bar != pc.allWindows.Length || state.Length - bar - 1 != WindowFlags.Length) return;
            for (int i = 0; i < bar; i++)
            {
                var window = pc.allWindows[i];
                if (!Shared(pc, window) || state[i] == '-') continue;
                bool open = state[i] == '1';
                if (window.activeSelf != open) { window.SetActive(open); if (open) window.transform.localScale = Vector3.one; }
            }
            for (int i = 0; i < WindowFlags.Length; i++) WindowFlags[i].SetValue(pc, state[bar + 1 + i] == '1');
            sentWindows = state;
            Plugin.Log("TOWER_PC_WINDOWS " + state);
        }

        // The partner already started the computer: sitting down shows its desktop instead of booting it again.
        internal static IEnumerator FastBoot(ComputerManager pc)
        {
            pc.defaultScreen.SetActive(false);
            if (pc.bootUpParent) pc.bootUpParent.SetActive(false);
            if (pc.startUpParent) pc.startUpParent.SetActive(false);
            pc.passwordEntered = true; pc.inPassword = false; pc.atLogin = false;
            pc.cursor.gameObject.SetActive(true);
            pc.canGetup = true;
            pc.bootUpDone = true;
            pc.desktopParent.SetActive(true);
            pc.CursorSet();
            pc.isDesktopOn = true;
            if (pc.icons != null) foreach (var icon in pc.icons) if (icon) icon.gameObject.SetActive(true);
            Plugin.Log("TOWER_PC_FAST_BOOT");
            yield break;
        }
        bool PartnerNearDesk(WatchTowerManager tower)
        {
            if (!plugin.PartnerPresent || !tower.table) return false;
            return (plugin.PartnerPosition - tower.table.transform.position).sqrMagnitude < 25f * 25f;
        }

        void Screen(WatchTowerManager tower)
        {
            var gm = GameManager.instance;
            bool working = gm.onComputer && tower.state == WatchTowerManager.State.InComputer;
            if ((working && Time.unscaledTime >= nextShot) || finalShotPending)
            {
                if (!shotPending && (PartnerNearDesk(tower) || finalShotPending))
                {
                    shotPending = true;
                    finalShotPending = false;
                    plugin.StartCoroutine(Shoot());
                }
                nextShot = Time.unscaledTime + 0.5f;
            }
        }

        // Leaving the desk: the game freezes its last screen on the monitor; the partner's monitor freezes the same.
        internal void ExitedComputer() { if (plugin.Ready) finalShotPending = true; }

        // The computer's screen has its own camera and canvas: rendering that camera gives the screen alone, without
        // the game's hints or any overlay. The target matches the window so the canvas keeps its layout.
        IEnumerator Shoot()
        {
            yield return new WaitForEndOfFrame();
            try
            {
                var ui = GameManager.instance.uIManager;
                var camera = ui.computerUI ? ui.computerUI.GetComponentInChildren<Camera>() : null;
                if (!camera || !ui.computerUI.gameObject.activeInHierarchy) yield break;
                int w = UnityEngine.Screen.width, h = UnityEngine.Screen.height;
                if (!full || full.width != w || full.height != h)
                {
                    if (full) { full.Release(); UnityEngine.Object.Destroy(full); }
                    full = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
                }
                const int sw = 480, sh = 270;
                if (!small) small = new RenderTexture(sw, sh, 0, RenderTextureFormat.ARGB32);
                if (!shot) shot = new Texture2D(sw, sh, TextureFormat.RGB24, false);
                var target = camera.targetTexture;
                camera.targetTexture = full;
                try { camera.Render(); } finally { camera.targetTexture = target; }
                Graphics.Blit(full, small);
                var previous = RenderTexture.active;
                RenderTexture.active = small;
                shot.ReadPixels(new Rect(0, 0, sw, sh), 0, 0);
                shot.Apply();
                RenderTexture.active = previous;
                var jpg = ImageConversion.EncodeToJPG(shot, 55);
                plugin.Send(new Packet { kind = "screen", scene = SceneManager.GetActiveScene().name, audio = Convert.ToBase64String(jpg) });
                ShotsSent++;
            }
            catch (Exception ex) { Plugin.Log("SCREEN_SHOT_FAILED " + ex.Message); }
            finally { shotPending = false; }
        }

        void ShowScreen(string base64)
        {
            var computer = Computer;
            if (!computer || string.IsNullOrEmpty(base64)) return;
            if (!received) received = new Texture2D(2, 2, TextureFormat.RGB24, false);
            var bytes = Convert.FromBase64String(base64);
            if (!ImageConversion.LoadImage(received, bytes)) return;
            ShotsReceived++;
            plugin.LabWrite("screen-received.jpg", bytes);
            // While we sit at the computer ourselves our own screen is what counts.
            if (GameManager.instance.onComputer) return;
            computer.screenMaterial.mainTexture = received;
            computer.screenMaterial.SetTexture("_EmissionMap", received);
            computer.screenMaterial.EnableKeyword("_EMISSION");
            if (computer.screen && !computer.screen.activeSelf) computer.screen.SetActive(true);
            if (computer.defaultScreen && computer.defaultScreen.activeSelf) computer.defaultScreen.SetActive(false);
        }

        public void Receive(Packet packet)
        {
            var tower = Tower;
            if (!tower || packet.scene != SceneManager.GetActiveScene().name) return;
            if (packet.kind == "screen") { ShowScreen(packet.audio); return; }
            switch (packet.code)
            {
                case "powerout":
                    var console = Computer ? Computer.consoleManager : null;
                    if (console) console.hasUpdated = true;
                    sentUpdate = true;
                    tower.StartCoroutine(WorldPowerOut(tower));
                    Plugin.Log("TOWER_POWER_OUT_REMOTE");
                    break;
                case "pc":
                    var computer = Computer;
                    if (!computer) return;
                    if (packet.active && !computer.passwordEntered)
                    {
                        computer.passwordEntered = true; sentPassword = true;
                        if (computer.inPassword) computer.inPassword = false;
                        Plugin.Log("TOWER_PASSWORD_SHARED");
                    }
                    if ((packet.bits & 1) != 0 && computer.consoleManager && !computer.consoleManager.hasUpdated) { computer.consoleManager.hasUpdated = true; sentUpdate = true; }
                    if ((packet.bits & 2) != 0) PartnerBooted = true;
                    break;
                case "pcwin":
                    if (Computer) ApplyWindows(Computer, packet.name);
                    break;
            }
        }
    }

    [HarmonyPatch(typeof(WatchTowerManager), nameof(WatchTowerManager.PowerOut))]
    internal static class PowerOutPatch
    {
        static void Prefix(WatchTowerManager __instance)
        {
            Plugin.Instance?.Tower.LocalPowerOut();
            __instance.StartCoroutine(TowerSync.RadioOffWithPower(__instance));
        }
    }

    // The desk chair seats one: sitting down while the partner is on it is refused, whether the click was on the chair
    // (FadeToComputer) or straight on the monitor (FadeDirectlyToComputerUI).
    [HarmonyPatch]
    internal static class TowerChairPatch
    {
        // Lab: let a click through as if the partner's claim had not arrived yet (the network race).
        internal static bool LabRace;

        static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(UIManager), nameof(UIManager.FadeToComputer));
            yield return AccessTools.Method(typeof(UIManager), nameof(UIManager.FadeDirectlyToComputerUI));
        }

        static bool Prefix(ref IEnumerator __result)
        {
            var tower = GameManager.instance ? GameManager.instance.watchTowerManager : null;
            var plugin = Plugin.Instance;
            if (plugin == null || !tower || tower.state != WatchTowerManager.State.Inside || !TowerSync.ChairTaken || LabRace) return true;
            GameManager.instance.subtitle.ShowSubAndClose(ConnectionPanel.L("Это место занято.", "That seat is taken."), 2f);
            plugin.Tower.RefuseChair();
            Plugin.Log("TOWER_CHAIR_REFUSED");
            __result = Empty();
            return false;
        }
        static IEnumerator Empty() { yield break; }
    }

    [HarmonyPatch(typeof(ComputerManager), nameof(ComputerManager.ExitComputer))]
    internal static class ComputerExitPatch
    {
        static void Postfix() => Plugin.Instance?.Tower.ExitedComputer();
    }
}

namespace IronbarkCoop
{
    [HarmonyPatch(typeof(ComputerManager), nameof(ComputerManager.BootUp))]
    internal static class ComputerBootPatch
    {
        static bool Prefix(ComputerManager __instance, ref IEnumerator __result)
        {
            if (Plugin.Instance == null || !Plugin.Instance.Ready || !TowerSync.PartnerBooted || __instance.bootUpDone) return true;
            __result = TowerSync.FastBoot(__instance);
            return false;
        }
    }
}
