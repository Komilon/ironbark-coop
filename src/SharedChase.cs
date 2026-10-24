using System.Collections;
using DG.Tweening;
using HarmonyLib;
using UnityEngine;

namespace IronbarkCoop
{
    // What night 8's cultist and the one at the trail's end share, after Scratch Creek's chases:
    // - a death is the team's: the victim gets the game's own jumpscare, the partner sees it happen (the view turns to the
    //   killer, the screen breaks up as the victim's does, the controls are gone), and then both start the chase over
    //   from a checkpoint with the cultist there again (before: the victim came back alone and the cultist was gone);
    // - an invisible wall holds whoever is ready to run until the partner is ready too (a big zone in front of it), so
    //   nobody is left behind or chased alone;
    // - the partner hears the hunt's music too, quieter;
    // - a test mode (Ctrl+F9) in which the cultist does not see this player, for trying the chase alone with two copies.
    internal static class SharedChase
    {
        static GameManager Gm => GameManager.instance;

        // Ctrl+F9: the cultist ignores this player (for testing with two copies on one PC). Told to the partner.
        internal static bool Ghost, PartnerGhost;

        internal static void Tick(Plugin plugin)
        {
            if (Input.GetKeyDown(KeyCode.F9) && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)))
            {
                Ghost = !Ghost;
                if (Gm && Gm.subtitle) Gm.subtitle.ShowSubAndClose(Ghost ? ConnectionPanel.L("Режим теста: культист вас не замечает (Ctrl+F9 — выключить).", "Test mode: the cultist ignores you (Ctrl+F9 to turn off).") : ConnectionPanel.L("Режим теста выключен.", "Test mode off."), 4f);
                if (plugin.Ready) plugin.Send(new Packet { kind = "chase", code = "ghost", active = Ghost });
                Plugin.Log("CHASE_GHOST " + Ghost);
            }
        }

        internal static void Receive(Packet packet)
        {
            if (packet.code == "ghost") { PartnerGhost = packet.active; Plugin.Log("CHASE_PARTNER_GHOST " + PartnerGhost); }
        }

        // ---- the partner's death, from here ----
        static Coroutine witnessing;
        static Behaviour[] witnessFilters;
        static AudioSource witnessHeartbeat;
        internal static bool Witnessing { get; private set; }

        internal static void Witness(Behaviour[] filters, Transform killer, AudioSource heartbeat)
        {
            if (Witnessing || !Gm) return;
            Witnessing = true;
            witnessFilters = filters; witnessHeartbeat = heartbeat;
            foreach (var f in filters) if (f) f.enabled = true;
            if (heartbeat) heartbeat.Play();
            if (Gm.firstPersonController) Gm.firstPersonController.enabled = false;
            if (Gm.phone) { Gm.phone.ClosePhone(); Gm.phone.enabled = false; }
            witnessing = Gm.StartCoroutine(LookAt(killer));
            Plugin.Log("CHASE_WITNESS");
        }

        static IEnumerator LookAt(Transform killer)
        {
            var cam = Gm.fpCamera;
            while (Witnessing && cam && killer)
            {
                var forward = killer.position + Vector3.up * 1.5f - cam.transform.position;
                if (forward.sqrMagnitude > 0.01f) cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, Quaternion.LookRotation(forward), Time.deltaTime * 6f);
                yield return null;
            }
        }

        internal static void EndWitness()
        {
            if (!Witnessing) return;
            Witnessing = false;
            if (witnessing != null && Gm) Gm.StopCoroutine(witnessing);
            witnessing = null;
            if (witnessFilters != null) foreach (var f in witnessFilters) if (f) f.enabled = false;
            if (witnessHeartbeat) witnessHeartbeat.Stop();
            if (Gm && Gm.phone) Gm.phone.enabled = true;
        }

        // The hunt's music, heard from the partner's side (Scratch Creek keeps the hunt sound on for the other player).
        static AudioSource huntMusic; static float huntVolume;
        internal static void HearHunt(AudioSource haunting, AudioClip clip)
        {
            if (!haunting || haunting.isPlaying) return;
            huntMusic = haunting; huntVolume = haunting.volume;
            if (clip) haunting.clip = clip;
            haunting.loop = true;
            haunting.volume = huntVolume * 0.4f;
            haunting.Play();
        }

        internal static void StopHearingHunt()
        {
            if (!huntMusic) return;
            huntMusic.Stop();
            huntMusic.volume = huntVolume;
            huntMusic = null;
        }

        // ---- the wait at the way out ----
        internal static GameObject MakeWall(string name, Vector3 center, Vector3 size)
        {
            var wall = new GameObject(name);
            wall.transform.position = center;
            var box = wall.AddComponent<BoxCollider>();
            box.size = size;
            wall.SetActive(false);
            Plugin.Log("CHASE_WALL " + name + " at " + center.ToString("F2") + " size " + size.ToString("F2"));
            return wall;
        }

        // A plain fade to black and back around a reset (the game's transition image).
        internal static IEnumerator Fade(bool toBlack, float seconds = 0.8f)
        {
            var ui = Gm ? Gm.uIManager : null;
            if (!ui || !ui.transitionImage) yield break;
            ui.transitionImage.gameObject.SetActive(true);
            var c = ui.transitionImage.color;
            ui.transitionImage.color = new Color(c.r, c.g, c.b, toBlack ? 0f : 1f);
            ui.transitionImage.DOColor(new Color(c.r, c.g, c.b, toBlack ? 1f : 0f), seconds);
            yield return new WaitForSeconds(seconds + 0.1f);
            if (!toBlack) ui.transitionImage.gameObject.SetActive(false);
        }

        // Puts the player's body at a spot on the ground (the character controller would undo a plain move).
        internal static void PlacePlayer(Vector3 feet, float yaw)
        {
            var fp = Gm.playerFP;
            if (!fp) return;
            if (!fp.activeSelf) fp.SetActive(true);
            var controller = fp.GetComponent<CharacterController>();
            if (controller) controller.enabled = false;
            fp.transform.position = feet + Vector3.up * (controller ? controller.height * 0.5f - controller.center.y + 0.05f : 1f);
            fp.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            Physics.SyncTransforms();
            if (controller) controller.enabled = true;
        }

        internal static Vector3 Ground(Vector3 at)
        {
            return Physics.Raycast(at + Vector3.up * 2f, Vector3.down, out var hit, 6f, ~0, QueryTriggerInteraction.Ignore) ? hit.point : at;
        }

        // The game's own state after a death, undone: alive, moving, the phone back.
        internal static void Revive()
        {
            var gm = Gm;
            gm.isDead = false;
            if (gm.phone) gm.phone.enabled = true;
            if (gm.cameraShake) gm.cameraShake.enabled = false;
            if (gm.firstPersonController) gm.firstPersonController.enabled = true;
            if (gm.uIManager) gm.uIManager.controlsText.text = "";
        }
    }
}
