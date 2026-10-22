using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace IronbarkCoop
{
    // Short join codes ("IB-XXXX-XXXX") carrying the host's IPv4 address and port, so friends paste a code instead of an IP.
    internal static class JoinCode
    {
        // Crockford-style base32 without look-alike letters.
        const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

        public static string Encode(IPAddress address, int port)
        {
            var bytes = address.GetAddressBytes();
            if (bytes.Length != 4) throw new ArgumentException("IPv4 only");
            ulong value = ((ulong)bytes[0] << 40) | ((ulong)bytes[1] << 32) | ((ulong)bytes[2] << 24) | ((ulong)bytes[3] << 16) | (ushort)port;
            var chars = new char[10];
            for (int i = 9; i >= 0; i--) { chars[i] = Alphabet[(int)(value & 31)]; value >>= 5; }
            var text = new string(chars);
            return "IB-" + text.Substring(0, 5) + "-" + text.Substring(5);
        }

        public static bool TryDecode(string code, out string address, out int port)
        {
            address = null; port = 0;
            if (string.IsNullOrWhiteSpace(code)) return false;
            string clean = code.Trim().ToUpperInvariant().Replace("IB-", "").Replace("-", "").Replace(" ", "").Replace('O', '0').Replace('I', '1').Replace('L', '1');
            if (clean.Length != 10) return false;
            ulong value = 0;
            foreach (char c in clean)
            {
                int digit = Alphabet.IndexOf(c);
                if (digit < 0) return false;
                value = (value << 5) | (uint)digit;
            }
            port = (int)(value & 0xFFFF);
            address = ((value >> 40) & 255) + "." + ((value >> 32) & 255) + "." + ((value >> 24) & 255) + "." + ((value >> 16) & 255);
            return port > 0;
        }

        public static bool LooksLikeCode(string text) => !string.IsNullOrEmpty(text) && Regex.IsMatch(text.Trim(), "^(?i)(IB-?)?[0-9A-Z]{5}-?[0-9A-Z]{5}$") && !IPAddress.TryParse(text.Trim(), out _);
    }

    // Experimental UPnP IGD port mapping (TCP + UDP) so a host can be reached without a VPN when the router allows it.
    internal sealed class Upnp
    {
        public string State { get; private set; } = "off";
        public string ExternalAddress { get; private set; }
        string controlUrl, serviceType, localAddress;
        int mappedPort;

        public void Open(int port, bool changeRouter)
        {
            new Thread(() =>
            {
                try
                {
                    State = "searching";
                    if (!Discover()) { State = "no UPnP router"; return; }
                    ExternalAddress = Soap("GetExternalIPAddress", "", "NewExternalIPAddress");
                    if (!changeRouter) { State = "found (read-only)"; return; }
                    foreach (var protocol in new[] { "TCP", "UDP" })
                        Soap("AddPortMapping",
                            "<NewRemoteHost></NewRemoteHost><NewExternalPort>" + port + "</NewExternalPort><NewProtocol>" + protocol + "</NewProtocol>" +
                            "<NewInternalPort>" + port + "</NewInternalPort><NewInternalClient>" + localAddress + "</NewInternalClient><NewEnabled>1</NewEnabled>" +
                            "<NewPortMappingDescription>Ironbark Coop</NewPortMappingDescription><NewLeaseDuration>0</NewLeaseDuration>", null);
                    mappedPort = port;
                    State = "open";
                }
                catch (Exception ex) { State = "failed: " + ex.Message; }
                Plugin.LogFromThread("UPNP " + State + " external=" + ExternalAddress);
            }) { IsBackground = true, Name = "IronbarkCoop UPnP" }.Start();
        }

        public void Close()
        {
            if (mappedPort == 0 || controlUrl == null) return;
            int port = mappedPort; mappedPort = 0;
            new Thread(() =>
            {
                foreach (var protocol in new[] { "TCP", "UDP" })
                    try { Soap("DeletePortMapping", "<NewRemoteHost></NewRemoteHost><NewExternalPort>" + port + "</NewExternalPort><NewProtocol>" + protocol + "</NewProtocol>", null); } catch { }
                State = "closed";
                Plugin.LogFromThread("UPNP closed " + port);
            }) { IsBackground = true }.Start();
        }

        bool Discover()
        {
            const string search = "M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nST: urn:schemas-upnp-org:device:InternetGatewayDevice:1\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\n\r\n";
            using (var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0)))
            {
                udp.Client.ReceiveTimeout = 3000;
                var request = Encoding.ASCII.GetBytes(search);
                udp.Send(request, request.Length, new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900));
                var deadline = DateTime.UtcNow.AddSeconds(3);
                while (DateTime.UtcNow < deadline)
                {
                    var from = new IPEndPoint(IPAddress.Any, 0);
                    byte[] reply;
                    try { reply = udp.Receive(ref from); } catch (SocketException) { return false; }
                    var match = Regex.Match(Encoding.ASCII.GetString(reply), @"(?im)^location:\s*(\S+)");
                    if (match.Success && Describe(match.Groups[1].Value)) return true;
                }
            }
            return false;
        }

        bool Describe(string location)
        {
            string xml;
            using (var web = new WebClient()) xml = web.DownloadString(location);
            foreach (var type in new[] { "urn:schemas-upnp-org:service:WANIPConnection:1", "urn:schemas-upnp-org:service:WANIPConnection:2", "urn:schemas-upnp-org:service:WANPPPConnection:1" })
            {
                int at = xml.IndexOf("<serviceType>" + type + "</serviceType>", StringComparison.OrdinalIgnoreCase);
                if (at < 0) continue;
                var control = Regex.Match(xml.Substring(at), @"<controlURL>\s*([^<]+)\s*</controlURL>", RegexOptions.IgnoreCase);
                if (!control.Success) continue;
                serviceType = type;
                controlUrl = new Uri(new Uri(location), control.Groups[1].Value.Trim()).ToString();
                // The address the router sees us on: the local end of a socket toward it.
                using (var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    var uri = new Uri(location);
                    probe.Connect(uri.Host, uri.Port > 0 ? uri.Port : 80);
                    localAddress = ((IPEndPoint)probe.LocalEndPoint).Address.ToString();
                }
                return true;
            }
            return false;
        }

        string Soap(string action, string arguments, string resultTag)
        {
            string body = "<?xml version=\"1.0\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\"><s:Body>" +
                "<u:" + action + " xmlns:u=\"" + serviceType + "\">" + arguments + "</u:" + action + "></s:Body></s:Envelope>";
            var request = (HttpWebRequest)WebRequest.Create(controlUrl);
            request.Method = "POST";
            request.ContentType = "text/xml; charset=\"utf-8\"";
            request.Headers.Add("SOAPACTION", "\"" + serviceType + "#" + action + "\"");
            request.Timeout = 5000;
            var data = Encoding.UTF8.GetBytes(body);
            using (var stream = request.GetRequestStream()) stream.Write(data, 0, data.Length);
            string reply;
            using (var response = request.GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream())) reply = reader.ReadToEnd();
            if (resultTag == null) return null;
            var match = Regex.Match(reply, "<" + resultTag + ">([^<]*)</" + resultTag + ">");
            return match.Success ? match.Groups[1].Value : null;
        }
    }
}
