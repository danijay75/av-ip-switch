using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EasyIpSwitch
{
    internal enum HostSystem
    {
        Unknown,
        Windows,
        MacOrLinux,
        NetworkDevice
    }

    internal sealed class LocalSubnet
    {
        public string AdapterName = "";
        public string Mac = "";
        public uint Network;
        public uint Mask;
        public uint SourceIp;
        public int Prefix;
    }

    internal sealed class ScanHost
    {
        public uint Address;
        public string Ip = "";
        public string Name = "";
        public string Mac = "";
        public HostSystem System = HostSystem.Unknown;
        public DateTime LastSeenUtc;
        public long RttMs = -1;
        public int Ttl = -1;
        public bool Arp;
        public bool Icmp;
    }

    internal sealed class ScanRequest
    {
        public List<uint> Addresses = new List<uint>();
        public List<LocalSubnet> Subnets = new List<LocalSubnet>();
        public bool ResolveNames = true;
        public int TimeoutMs = 500;
        public int Concurrency = 64;
    }

    internal static class NetworkScanner
    {
        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        private static extern int SendARP(uint destIp, uint srcIp, byte[] macAddr, ref uint phyAddrLen);

        public static List<LocalSubnet> LocalSubnets(List<AdapterInfo> adapters)
        {
            List<LocalSubnet> result = new List<LocalSubnet>();
            if (adapters == null)
            {
                return result;
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < adapters.Count; i++)
            {
                AdapterInfo info = adapters[i];
                if (!info.EnabledKnown || !info.Enabled || info.Ip.Length == 0 || info.Mask.Length == 0)
                {
                    continue;
                }

                uint ip;
                uint mask;
                if (!IpUtil.TryParse(info.Ip, out ip) || !IpUtil.TryParse(info.Mask, out mask))
                {
                    continue;
                }
                if (ip == 0u || ip == 0xFFFFFFFFu || IpUtil.IsApipa(ip) || !IpUtil.IsValidMask(mask))
                {
                    continue;
                }

                uint network = ip & mask;
                int prefix = CountBits(mask);
                string key = IpUtil.Format(network) + "/" + prefix.ToString(CultureInfo.InvariantCulture);
                if (!seen.Add(key))
                {
                    continue;
                }

                LocalSubnet subnet = new LocalSubnet();
                subnet.AdapterName = info.Name;
                subnet.Mac = info.Mac;
                subnet.Network = network;
                subnet.Mask = mask;
                subnet.SourceIp = ip;
                subnet.Prefix = prefix;
                result.Add(subnet);
            }
            return result;
        }

        public static string ToCidr(string ipText, string maskText)
        {
            uint ip;
            uint mask;
            if (!IpUtil.TryParse(ipText, out ip) || !IpUtil.TryParse(maskText, out mask))
            {
                return "";
            }
            if (ip == 0u || ip == 0xFFFFFFFFu || IpUtil.IsApipa(ip) || !IpUtil.IsValidMask(mask))
            {
                return "";
            }
            return IpUtil.Format(ip & mask) + "/" + CountBits(mask).ToString(CultureInfo.InvariantCulture);
        }

        public static void Scan(ScanRequest request, CancellationToken token,
            Action<ScanHost> onHost, Action<int, int> onProgress)
        {
            if (request == null || request.Addresses == null || request.Addresses.Count == 0)
            {
                return;
            }

            int total = request.Addresses.Count;
            int done = 0;
            ParallelOptions options = new ParallelOptions();
            options.MaxDegreeOfParallelism = Math.Max(1, request.Concurrency);
            options.CancellationToken = token;

            try
            {
                Parallel.ForEach(request.Addresses, options, delegate(uint address)
                {
                    token.ThrowIfCancellationRequested();
                    ScanHost host = null;
                    try
                    {
                        host = Probe(address, request, token);
                    }
                    catch
                    {
                        host = null;
                    }

                    if (host != null && onHost != null)
                    {
                        try
                        {
                            onHost(host);
                        }
                        catch
                        {
                        }
                    }

                    int current = Interlocked.Increment(ref done);
                    if (onProgress != null)
                    {
                        try
                        {
                            onProgress(current, total);
                        }
                        catch
                        {
                        }
                    }
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch (AggregateException ex)
            {
                if (!(ex.InnerException is OperationCanceledException))
                {
                    throw;
                }
            }
        }

        private static ScanHost Probe(uint address, ScanRequest request, CancellationToken token)
        {
            string ip = IpUtil.Format(address);
            LocalSubnet local = FindSubnet(address, request.Subnets);

            string mac = "";
            bool arp = TryArp(address, local == null ? 0u : local.SourceIp, out mac);
            PingReply reply = TryPing(address, request.TimeoutMs);
            bool icmp = reply != null && reply.Status == IPStatus.Success;

            if (!arp && !icmp)
            {
                return null;
            }

            ScanHost host = new ScanHost();
            host.Address = address;
            host.Ip = ip;
            host.Mac = mac;
            host.Arp = arp;
            host.Icmp = icmp;
            host.LastSeenUtc = DateTime.UtcNow;

            if (icmp)
            {
                host.RttMs = reply.RoundtripTime;
                if (reply.Options != null)
                {
                    host.Ttl = reply.Options.Ttl;
                }
            }

            bool isLocal = local != null && local.SourceIp == address;
            if (isLocal)
            {
                host.Name = Environment.MachineName;
                host.System = HostSystem.Windows;
                if (host.Mac.Length == 0 && local.Mac.Length > 0)
                {
                    host.Mac = IpUtil.FormatMac(local.Mac);
                }
            }
            else
            {
                if (request.ResolveNames && !token.IsCancellationRequested)
                {
                    host.Name = ResolveName(ip);
                }
                host.System = SystemFromTtl(host.Ttl, icmp);
            }
            return host;
        }

        private static LocalSubnet FindSubnet(uint address, List<LocalSubnet> subnets)
        {
            if (subnets == null)
            {
                return null;
            }
            for (int i = 0; i < subnets.Count; i++)
            {
                LocalSubnet subnet = subnets[i];
                if ((address & subnet.Mask) == subnet.Network)
                {
                    return subnet;
                }
            }
            return null;
        }

        private static bool TryArp(uint address, uint source, out string mac)
        {
            mac = "";
            byte[] bytes = new byte[6];
            uint length = (uint)bytes.Length;
            try
            {
                int result = SendARP(ToNativeAddress(address), ToNativeAddress(source), bytes, ref length);
                if (result != 0 || length < 6)
                {
                    return false;
                }

                bool empty = true;
                StringBuilder builder = new StringBuilder(17);
                for (int i = 0; i < 6; i++)
                {
                    if (bytes[i] != 0)
                    {
                        empty = false;
                    }
                    if (i > 0)
                    {
                        builder.Append(':');
                    }
                    builder.Append(bytes[i].ToString("X2", CultureInfo.InvariantCulture));
                }
                if (empty)
                {
                    return false;
                }
                mac = builder.ToString();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static PingReply TryPing(uint address, int timeoutMs)
        {
            try
            {
                using (Ping ping = new Ping())
                {
                    return ping.Send(ToAddress(address), Math.Max(100, timeoutMs));
                }
            }
            catch
            {
                return null;
            }
        }

        private static string ResolveName(string ip)
        {
            string name = NetBios.Query(ip, 250);
            if (name.Length > 0)
            {
                return name;
            }
            return ReverseDns(ip);
        }

        private static string ReverseDns(string ip)
        {
            try
            {
                IPAddress address = IPAddress.Parse(ip);
                Task<IPHostEntry> task = Task.Factory.StartNew<IPHostEntry>(delegate
                {
                    return Dns.GetHostEntry(address);
                });
                if (!task.Wait(800))
                {
                    return "";
                }

                string host = task.Result == null ? "" : task.Result.HostName;
                if (string.IsNullOrEmpty(host) || string.Equals(host, ip, StringComparison.OrdinalIgnoreCase))
                {
                    return "";
                }
                int dot = host.IndexOf('.');
                return dot > 0 ? host.Substring(0, dot) : host;
            }
            catch
            {
                return "";
            }
        }

        private static HostSystem SystemFromTtl(int ttl, bool icmp)
        {
            if (!icmp || ttl <= 0)
            {
                return HostSystem.Unknown;
            }
            if (ttl >= 200)
            {
                return HostSystem.NetworkDevice;
            }
            if (ttl >= 100)
            {
                return HostSystem.Windows;
            }
            if (ttl >= 32)
            {
                return HostSystem.MacOrLinux;
            }
            return HostSystem.Unknown;
        }

        private static IPAddress ToAddress(uint value)
        {
            return new IPAddress(new byte[]
            {
                (byte)(value >> 24),
                (byte)(value >> 16),
                (byte)(value >> 8),
                (byte)value
            });
        }

        internal static uint ToNativeAddress(uint value)
        {
            byte[] bytes = new byte[]
            {
                (byte)(value >> 24),
                (byte)(value >> 16),
                (byte)(value >> 8),
                (byte)value
            };
            return BitConverter.ToUInt32(bytes, 0);
        }

        private static int CountBits(uint value)
        {
            int count = 0;
            while (value != 0u)
            {
                count += (int)(value & 1u);
                value >>= 1;
            }
            return count;
        }
    }
}
