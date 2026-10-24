using System.IO;
using HarmonyLib;
using UnityEngine;

namespace IronbarkCoop
{
    // The tower photos shown at the campfire are loaded as 1920x1080 pictures whatever size they were taken at. Taken in
    // a smaller window (two copies side by side), the load threw in CampfireManager.Start, which then never switched off
    // the campfire's way out ("next scene" and "early exit" triggers) (playtest log). The picture is loaded at its size.
    [HarmonyPatch(typeof(ScreenshotManager), nameof(ScreenshotManager.LoadSprite))]
    internal static class PhotoSizePatch
    {
        static bool Prefix(string path, ref Sprite __result)
        {
            __result = null;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            var texture = new Texture2D(2, 2, TextureFormat.RGB24, false) { filterMode = FilterMode.Bilinear };
            if (!texture.LoadImage(File.ReadAllBytes(path))) return false;
            __result = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f * texture.width / 1920f);
            return false;
        }
    }
}
