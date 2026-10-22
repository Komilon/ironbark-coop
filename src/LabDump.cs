using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IronbarkCoop
{
    // Lab-only inspection written to the bridge folder; never runs without -coop-bridge.
    internal static class LabDump
    {
        public static void Write(string path)
        {
            var animators = new JArray();
            foreach (var animator in Resources.FindObjectsOfTypeAll<Animator>())
            {
                var controller = animator.runtimeAnimatorController;
                var clips = new JArray();
                if (controller) foreach (var clip in controller.animationClips) clips.Add(clip.name + ":" + clip.length.ToString("F2"));
                var parameters = new JArray();
                if (controller && animator.isActiveAndEnabled)
                    foreach (var parameter in animator.parameters) parameters.Add(parameter.name + ":" + parameter.type);
                animators.Add(new JObject {
                    ["path"] = PathOf(animator.transform),
                    ["scene"] = animator.gameObject.scene.name ?? "",
                    ["active"] = animator.gameObject.activeInHierarchy,
                    ["human"] = animator.avatar && animator.avatar.isHuman,
                    ["avatar"] = animator.avatar ? animator.avatar.name : "",
                    ["controller"] = controller ? controller.name : "",
                    ["clips"] = clips,
                    ["parameters"] = parameters,
                    ["skinned"] = animator.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length
                });
            }
            var roots = new JArray();
            for (int i = 0; i < SceneManager.sceneCount; i++)
                foreach (var root in SceneManager.GetSceneAt(i).GetRootGameObjects())
                    roots.Add(SceneManager.GetSceneAt(i).name + "/" + root.name + (root.activeSelf ? "" : " (inactive)"));
            var clipsAll = new JArray();
            foreach (var clip in Resources.FindObjectsOfTypeAll<AnimationClip>()) clipsAll.Add(clip.name + ":" + clip.length.ToString("F2") + (clip.humanMotion ? ":human" : ""));
            var audioClips = new JArray();
            foreach (var clip in Resources.FindObjectsOfTypeAll<AudioClip>()) audioClips.Add(clip.name + ":" + clip.length.ToString("F2"));
            File.WriteAllText(path, new JObject {
                ["scene"] = SceneManager.GetActiveScene().name,
                ["animators"] = animators,
                ["roots"] = roots,
                ["animationClips"] = clipsAll,
                ["audioClips"] = audioClips
            }.ToString());
        }

        static string PathOf(Transform transform)
        {
            string path = transform.name;
            while (transform.parent) { transform = transform.parent; path = transform.name + "/" + path; }
            return path;
        }
    }
}
