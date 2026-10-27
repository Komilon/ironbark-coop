using HarmonyLib;

namespace IronbarkCoop
{
    // The tower (the first night on) runs the game's own micInput for its shout meter. It restarts the same microphone
    // with its own clip, which silently ended the voice chat's recording: the partner stopped being heard, or heard the
    // last second of speech on repeat (playtest). While the mod's voice uses the microphone, the game does not open it
    // and its meter reads the mod's level instead; with the voice off, the game keeps its own microphone.
    [HarmonyPatch(typeof(micInput), "InitMic")]
    internal static class MicInitPatch
    {
        internal static bool VoiceOwnsMic => Plugin.Instance && Plugin.Instance.MicMode != VoiceMode.Off;
        static bool Prefix()
        {
            if (!VoiceOwnsMic) return true;
            Plugin.Log("MIC_SHARED the game's shout meter reads the voice chat's microphone");
            return false;
        }
    }

    [HarmonyPatch(typeof(micInput), "StopMicrophone")]
    internal static class MicStopPatch
    {
        static bool Prefix() => !MicInitPatch.VoiceOwnsMic;
    }

    [HarmonyPatch(typeof(micInput), "LevelMax")]
    internal static class MicLevelPatch
    {
        static bool Prefix(ref float __result)
        {
            if (!MicInitPatch.VoiceOwnsMic) return true;
            __result = Plugin.Instance.Capture?.Peak ?? 0f;
            return false;
        }
    }
}
