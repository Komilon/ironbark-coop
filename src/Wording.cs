using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

namespace IronbarkCoop
{
    // The story told by two (playtest request): while a partner is connected, the narration, the thoughts, the radio
    // talks and what people say to the player are worded for both ("we didn't know what to make of it", "you two"),
    // Jack and his partner, in English and Russian. Lines one player does alone are worded without a person
    // ("hands were full"). The game's own lines come back when the partner leaves. Other languages keep the game's text.
    // The new wording (Wording.json, "table/key" -> en/ru) is written into the game's loaded string tables, which every
    // reader uses: subtitles, cards, the radio (Yarn) and the phone.
    internal static class Wording
    {
        static Dictionary<string, JObject> lines;
        // The game's text of every entry we changed, per locale, to put back.
        static readonly Dictionary<string, string> original = new Dictionary<string, string>();
        static bool plural;
        static string appliedLocale;
        internal static int Changed { get; private set; }

        static Dictionary<string, JObject> Lines
        {
            get
            {
                if (lines != null) return lines;
                lines = new Dictionary<string, JObject>();
                using (var stream = typeof(Wording).Assembly.GetManifestResourceStream("IronbarkCoop.Wording.json"))
                using (var reader = new StreamReader(stream))
                    foreach (var pair in JObject.Parse(reader.ReadToEnd())) lines[pair.Key] = (JObject)pair.Value;
                return lines;
            }
        }

        static string Language(Locale locale)
        {
            if (locale == null) return null;
            string code = locale.Identifier.Code;
            return code.StartsWith("ru") ? "ru" : code.StartsWith("en") ? "en" : null;
        }

        // A new scene may have loaded the tables afresh.
        internal static void SceneLoaded() => appliedLocale = null;

        // Called every frame: (re)applies after a connection change or a language change.
        internal static void Tick(bool connected)
        {
            if (!LocalizationSettings.InitializationOperation.IsDone) return;
            var locale = LocalizationSettings.SelectedLocale;
            string key = locale ? locale.Identifier.Code : "";
            if (connected == plural && key == appliedLocale) return;
            plural = connected;
            appliedLocale = key;
            Apply(locale);
        }

        static void Apply(Locale locale)
        {
            string language = Language(locale);
            Changed = 0;
            if (language == null) return;
            var database = LocalizationSettings.StringDatabase;
            foreach (var pair in Lines)
            {
                int slash = pair.Key.IndexOf('/');
                string table = pair.Key.Substring(0, slash), entryKey = pair.Key.Substring(slash + 1);
                var text = (string)pair.Value[language];
                if (text == null) continue;
                var loaded = database.GetTableAsync(table, locale).WaitForCompletion();
                var entry = loaded ? loaded.GetEntry(entryKey) : null;
                if (entry == null) continue;
                string id = locale.Identifier.Code + "/" + pair.Key;
                if (!original.ContainsKey(id)) original[id] = entry.Value;
                entry.Value = plural ? text : original[id];
                if (plural) Changed++;
            }
            Plugin.Log("WORDING " + (plural ? "for two" : "the game's own") + " " + locale.Identifier.Code + " changed=" + Changed);
        }

        // Lab: the text the game now gives for one entry ("table/key").
        internal static string LabText(string id)
        {
            int slash = id.IndexOf('/');
            return slash < 0 ? "?" : LocalizationSettings.StringDatabase.GetLocalizedString(id.Substring(0, slash), id.Substring(slash + 1));
        }

        // Lab: every string table of every locale, as the game has them loaded (kept in the lab, never shipped).
        internal static string LabDump(string path)
        {
            var root = new JObject();
            int count = 0;
            foreach (var locale in LocalizationSettings.AvailableLocales.Locales)
            {
                var tables = LocalizationSettings.StringDatabase.GetAllTables(locale).WaitForCompletion();
                var byTable = new JObject();
                foreach (var table in tables)
                {
                    var entries = new JObject();
                    foreach (var entry in table.Values) { entries[entry.Key ?? entry.KeyId.ToString()] = entry.Value; count++; }
                    byTable[table.TableCollectionName] = entries;
                }
                root[locale.Identifier.Code] = byTable;
            }
            File.WriteAllText(path, root.ToString());
            return count.ToString();
        }
    }
}
