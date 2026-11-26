using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace EasyIpSwitch
{
    /// <summary>
    /// Résolution de nom NetBIOS (NBSTAT, UDP 137). Best effort : un pare-feu
    /// ou une machine non Windows peut ne pas répondre.
    /// </summary>
    internal static class NetBios
    {
        public static string Query(string ip, int timeoutMs)
        {
            try
            {
                IPAddress address;
                if (!IPAddress.TryParse(ip, out address))
                {
                    return "";
                }

                byte[] request = BuildRequest();
                using (UdpClient client = new UdpClient())
                {
                    client.Client.ReceiveTimeout = timeoutMs;
                    client.Connect(address, 137);
                    client.Send(request, request.Length);
                    IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                    byte[] response = client.Receive(ref remote);
                    return ParseResponse(response);
                }
            }
            catch
            {
                return "";
            }
        }

        private static byte[] BuildRequest()
        {
            List<byte> packet = new List<byte>(50);
            // Transaction, drapeaux, une question, aucune réponse.
            packet.AddRange(new byte[] { 0x82, 0x28, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });

            // Nom NetBIOS générique « * » (16 octets, complété par des zéros),
            // encodé en 32 caractères A-P.
            packet.Add(0x20);
            byte[] name = new byte[16];
            name[0] = (byte)'*';
            for (int i = 0; i < name.Length; i++)
            {
                packet.Add((byte)('A' + ((name[i] >> 4) & 0x0F)));
                packet.Add((byte)('A' + (name[i] & 0x0F)));
            }
            packet.Add(0x00);

            // QTYPE = NBSTAT (0x0021), QCLASS = IN (0x0001).
            packet.AddRange(new byte[] { 0x00, 0x21, 0x00, 0x01 });
            return packet.ToArray();
        }

        private static string ParseResponse(byte[] response)
        {
            if (response == null || response.Length < 12)
            {
                return "";
            }

            int offset = 12;
            if (offset >= response.Length || response[offset] != 0x20)
            {
                return "";
            }
            offset += 1 + 32 + 1;
            if (offset + 10 > response.Length)
            {
                return "";
            }

            offset += 2; // type
            offset += 2; // classe
            offset += 4; // TTL
            int length = (response[offset] << 8) | response[offset + 1];
            offset += 2;
            if (length <= 0 || offset >= response.Length || offset + length > response.Length)
            {
                return "";
            }

            int count = response[offset];
            offset++;
            string fallback = "";
            for (int i = 0; i < count && offset + 18 <= response.Length; i++)
            {
                string name = Encoding.ASCII.GetString(response, offset, 15).Trim();
                int suffix = response[offset + 15];
                int flags = (response[offset + 16] << 8) | response[offset + 17];
                offset += 18;

                if (name.Length == 0)
                {
                    continue;
                }
                bool group = (flags & 0x8000) != 0;
                if (!group && (suffix == 0x00 || suffix == 0x20))
                {
                    return name;
                }
                if (!group && fallback.Length == 0)
                {
                    fallback = name;
                }
            }
            return fallback;
        }
    }
}
