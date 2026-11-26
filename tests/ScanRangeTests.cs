using System;
using System.Collections.Generic;
using System.Threading;

namespace EasyIpSwitch
{
    internal static class ScanRangeTests
    {
        private static int _assertions;

        public static int Run()
        {
            CidrSkipsNetworkAndBroadcast();
            Slash31AndSlash32AreUsable();
            ExplicitRangeAndSingleAddress();
            CommaListDeduplicates();
            InvalidInputsAreRejected();
            LargeRangesAreRejected();
            CidrFormatting();
            LocalSubnetsAreDeduplicated();
            ArpAddressUsesNetworkByteOrder();
            LoopbackScanCompletes();
            return _assertions;
        }

        private static void CidrSkipsNetworkAndBroadcast()
        {
            List<ScanRange> ranges;
            ScanRangeParseError error;
            string token;
            Assert(ScanRangeParser.TryParse("192.168.1.0/24", out ranges, out error, out token),
                "A /24 range must parse.");
            Assert(ranges.Count == 1 && ranges[0].Start == 0xC0A80101u && ranges[0].End == 0xC0A801FEu,
                "A /24 range must cover .1 to .254.");
            Assert(ranges[0].Count == 254, "A /24 range must contain 254 usable addresses.");

            List<uint> addresses = ScanRangeParser.Expand(ranges);
            Assert(addresses.Count == 254, "Expanding a /24 range must produce 254 addresses.");
            Assert(addresses[0] == 0xC0A80101u && addresses[addresses.Count - 1] == 0xC0A801FEu,
                "Expanded addresses must keep the usable bounds.");
        }

        private static void Slash31AndSlash32AreUsable()
        {
            List<ScanRange> ranges;
            ScanRangeParseError error;
            string token;
            Assert(ScanRangeParser.TryParse("192.168.1.10/31", out ranges, out error, out token)
                && ranges[0].Count == 2, "A /31 range must keep both addresses.");
            Assert(ScanRangeParser.TryParse("192.168.1.10/32", out ranges, out error, out token)
                && ranges[0].Count == 1, "A /32 range must keep its single address.");
        }

        private static void ExplicitRangeAndSingleAddress()
        {
            List<ScanRange> ranges;
            ScanRangeParseError error;
            string token;
            Assert(ScanRangeParser.TryParse("192.168.1.10-50", out ranges, out error, out token),
                "An explicit range must parse.");
            Assert(ranges[0].Start == 0xC0A8010Au && ranges[0].End == 0xC0A80132u && ranges[0].Count == 41,
                "An explicit range must cover both bounds.");
            Assert(ScanRangeParser.TryParse("192.168.1.42", out ranges, out error, out token)
                && ranges[0].Count == 1, "A single address must parse.");
        }

        private static void CommaListDeduplicates()
        {
            List<ScanRange> ranges;
            ScanRangeParseError error;
            string token;
            Assert(ScanRangeParser.TryParse("192.168.1.1, 192.168.1.1-3", out ranges, out error, out token),
                "A comma list must parse.");
            List<uint> addresses = ScanRangeParser.Expand(ranges);
            Assert(addresses.Count == 3, "Overlapping entries must be deduplicated.");
        }

        private static void InvalidInputsAreRejected()
        {
            List<ScanRange> ranges;
            ScanRangeParseError error;
            string token;

            Assert(!ScanRangeParser.TryParse("", out ranges, out error, out token)
                && error == ScanRangeParseError.Empty, "An empty range must be rejected.");
            Assert(!ScanRangeParser.TryParse("192.168.1.300", out ranges, out error, out token)
                && error == ScanRangeParseError.InvalidToken, "An invalid address must be rejected.");
            Assert(!ScanRangeParser.TryParse("192.168.1.0/33", out ranges, out error, out token)
                && error == ScanRangeParseError.InvalidPrefix, "A prefix above 32 must be rejected.");
            Assert(!ScanRangeParser.TryParse("192.168.1.50-10", out ranges, out error, out token)
                && error == ScanRangeParseError.StartAfterEnd, "A reversed range must be rejected.");
            Assert(!ScanRangeParser.TryParse("0.0.0.0", out ranges, out error, out token)
                && error == ScanRangeParseError.InvalidToken, "0.0.0.0 must be rejected.");
        }

        private static void LargeRangesAreRejected()
        {
            List<ScanRange> ranges;
            ScanRangeParseError error;
            string token;

            Assert(!ScanRangeParser.TryParse("10.0.0.0/8", out ranges, out error, out token)
                && error == ScanRangeParseError.RangeTooLarge, "A /8 range must be rejected.");
            Assert(!ScanRangeParser.TryParse("10.0.0.0/16, 10.1.0.0/16", out ranges, out error, out token)
                && error == ScanRangeParseError.TotalTooLarge, "A range above the total limit must be rejected.");
        }

        private static void CidrFormatting()
        {
            Assert(NetworkScanner.ToCidr("192.168.1.42", "255.255.255.0") == "192.168.1.0/24",
                "A /24 subnet must be formatted as CIDR.");
            Assert(NetworkScanner.ToCidr("10.1.2.3", "255.255.240.0") == "10.1.0.0/20",
                "A /20 subnet must be formatted as CIDR.");
            Assert(NetworkScanner.ToCidr("169.254.1.2", "255.255.0.0") == "",
                "An APIPA address must not produce a subnet.");
            Assert(NetworkScanner.ToCidr("192.168.1.1", "255.0.255.0") == "",
                "A non-contiguous mask must not produce a subnet.");
        }

        private static void LocalSubnetsAreDeduplicated()
        {
            List<AdapterInfo> adapters = new List<AdapterInfo>();

            AdapterInfo first = new AdapterInfo();
            first.Name = "Ethernet";
            first.Enabled = true;
            first.EnabledKnown = true;
            first.Ip = "192.168.1.42";
            first.Mask = "255.255.255.0";
            first.Mac = "AA-BB-CC-DD-EE-FF";
            adapters.Add(first);

            AdapterInfo duplicate = new AdapterInfo();
            duplicate.Name = "Wi-Fi";
            duplicate.Enabled = true;
            duplicate.EnabledKnown = true;
            duplicate.Ip = "192.168.1.43";
            duplicate.Mask = "255.255.255.0";
            adapters.Add(duplicate);

            AdapterInfo disabled = new AdapterInfo();
            disabled.Name = "Disabled";
            disabled.Enabled = false;
            disabled.EnabledKnown = true;
            disabled.Ip = "10.0.0.2";
            disabled.Mask = "255.255.255.0";
            adapters.Add(disabled);

            List<LocalSubnet> subnets = NetworkScanner.LocalSubnets(adapters);
            Assert(subnets.Count == 1, "Duplicate and disabled adapters must not add subnets.");
            Assert(subnets[0].Network == 0xC0A80100u && subnets[0].Prefix == 24
                && subnets[0].SourceIp == 0xC0A8012Au,
                "The local subnet must keep its network, prefix and source address.");
        }

        private static void ArpAddressUsesNetworkByteOrder()
        {
            Assert(NetworkScanner.ToNativeAddress(0xC0A8012Au) == 0x2A01A8C0u,
                "SendARP must receive IPv4 bytes in network order.");
        }

        private static void LoopbackScanCompletes()
        {
            ScanRequest request = new ScanRequest();
            request.Addresses.Add(0x7F000001u);
            request.ResolveNames = false;
            request.TimeoutMs = 300;
            request.Concurrency = 1;

            bool found = false;
            bool progressed = false;
            NetworkScanner.Scan(request, CancellationToken.None,
                delegate(ScanHost host) { found = host != null && host.Ip == "127.0.0.1"; },
                delegate(int done, int total) { progressed = done == 1 && total == 1; });

            Assert(found, "A loopback ping must be discovered by the scan.");
            Assert(progressed, "A one-address scan must report completion.");
        }

        private static void Assert(bool condition, string message)
        {
            _assertions++;
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
