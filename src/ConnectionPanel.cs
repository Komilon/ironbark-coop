using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using UnityEngine;
using UnityEngine.Localization.Settings;

namespace IronbarkCoop
{
    // Helpers for the F8 panel: the player's own addresses (LAN / virtual LAN) and readable connection errors.
    internal static class ConnectionPanel
    {
        static List<string> addresses = new List<string>();
        static float nextRefresh;

        public static bool Russian
        {
            get
            {
                try { var locale = LocalizationSettings.SelectedLocale; return locale != null && locale.Identifier.Code.StartsWith("ru"); }
                catch { return false; }
            }
        }

        public static string L(string russian, string english) => Russian ? russian : english;

        // IPv4 addresses a friend could reach, labelled by adapter so Radmin/ZeroTier/Tailscale are easy to spot.
        public static List<string> Addresses()
        {
            if (Time.unscaledTime < nextRefresh) return addresses;
            nextRefresh = Time.unscaledTime + 10f;
            var result = new List<string>();
            try
            {
                foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var unicast in adapter.GetIPProperties().UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        string ip = unicast.Address.ToString();
                        if (ip.StartsWith("169.254.")) continue;
                        result.Add(ip + "  (" + Label(adapter.Description, ip) + ")");
                    }
                }
            }
            catch (Exception ex) { result.Add("? " + ex.Message); }
            addresses = result;
            return addresses;
        }

        // Address worth sharing: a virtual LAN (Radmin/ZeroTier/Tailscale/Hamachi) first, then the local network.
        public static string BestShareAddress()
        {
            string lan = null;
            foreach (var line in Addresses())
            {
                int space = line.IndexOf(' ');
                if (space <= 0) continue;
                string ip = line.Substring(0, space);
                if (line.Contains("Radmin") || line.Contains("ZeroTier") || line.Contains("Tailscale") || line.Contains("Hamachi")) return ip;
                if (lan == null && (ip.StartsWith("192.168.") || ip.StartsWith("10."))) lan = ip;
            }
            return lan;
        }

        static string Label(string description, string ip)
        {
            string d = description.ToLowerInvariant();
            if (d.Contains("radmin") || ip.StartsWith("26.")) return "Radmin VPN";
            if (d.Contains("zerotier")) return "ZeroTier";
            if (d.Contains("tailscale") || ip.StartsWith("100.")) return "Tailscale";
            if (d.Contains("hamachi") || ip.StartsWith("25.")) return "Hamachi";
            return L("локальная сеть", "LAN");
        }

        // Turns socket and handshake messages into something a player can act on.
        public static string Explain(string status)
        {
            if (string.IsNullOrEmpty(status)) return "";
            string s = status.ToLowerInvariant();
            if (s.Contains("actively refused") || s.Contains("connection refused"))
                return L("Хост не принимает подключение: он ещё не нажал «Host (IP)», неверный IP или порт 25061 закрыт брандмауэром.",
                         "The host refused: not hosting yet, wrong IP, or port 25061 blocked by a firewall.");
            if (s.Contains("timeout") || s.Contains("timed out"))
                return L("Хост не отвечает: проверьте IP и что вы оба в одной сети (Radmin/ZeroTier/Tailscale).",
                         "No answer from the host: check the IP and that you share a network (Radmin/ZeroTier/Tailscale).");
            if (s.Contains("mismatch"))
                return L("Разные версии игры или мода (или код сессии). Нужны одинаковые файлы у обоих.",
                         "Different game or mod version (or session code). Both need identical files.");
            if (s.Contains("only one usage") || s.Contains("address already in use"))
                return L("Порт 25061 уже занят: возможно, игра запущена дважды.", "Port 25061 is busy: the game may be running twice.");
            if (s.StartsWith("disconnected")) return L("Связь с партнёром потеряна. ", "Lost the partner. ") + status;
            if (s == "partner connected") return L("Партнёр подключён", "Partner connected");
            if (s == "waiting for partner") return L("Ждём партнёра…", "Waiting for partner…");
            if (s == "connecting") return L("Подключение…", "Connecting…");
            if (s == "offline") return L("Не подключено", "Offline");
            return status;
        }
    }
}
