using System;
using System.Collections.Generic;
using System.Globalization;

namespace EasyIpSwitch
{
    internal enum ScanRangeParseError
    {
        None,
        Empty,
        InvalidToken,
        InvalidPrefix,
        StartAfterEnd,
        RangeTooLarge,
        TotalTooLarge
    }

    internal sealed class ScanRange
    {
        public uint Start;
        public uint End;

        public long Count
        {
            get { return (long)End - (long)Start + 1; }
        }
    }

    /// <summary>
    /// Analyse les plages de scan : CIDR (192.168.1.0/24), intervalle
    /// (192.168.1.10-50), adresse seule et listes séparées par des virgules.
    /// </summary>
    internal static class ScanRangeParser
    {
        public const int MaxAddressesPerRange = 65536;
        public const int MaxAddressesTotal = 65536;
        public const int WarningThreshold = 4096;

        public static bool TryParse(string text, out List<ScanRange> ranges,
            out ScanRangeParseError error, out string token)
        {
            ranges = new List<ScanRange>();
            error = ScanRangeParseError.None;
            token = "";

            if (text == null || text.Trim().Length == 0)
            {
                error = ScanRangeParseError.Empty;
                return false;
            }

            string[] parts = text.Split(new char[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                error = ScanRangeParseError.Empty;
                return false;
            }

            long total = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                if (part.Length == 0)
                {
                    continue;
                }

                ScanRange range;
                if (!TryParsePart(part, out range, out error))
                {
                    token = part;
                    ranges.Clear();
                    return false;
                }
                if (range.Count > MaxAddressesPerRange)
                {
                    error = ScanRangeParseError.RangeTooLarge;
                    token = part;
                    ranges.Clear();
                    return false;
                }

                total += range.Count;
                ranges.Add(range);
            }

            if (ranges.Count == 0)
            {
                error = ScanRangeParseError.Empty;
                return false;
            }
            if (total > MaxAddressesTotal)
            {
                error = ScanRangeParseError.TotalTooLarge;
                token = "";
                ranges.Clear();
                return false;
            }
            return true;
        }

        private static bool TryParsePart(string part, out ScanRange range, out ScanRangeParseError error)
        {
            range = new ScanRange();
            error = ScanRangeParseError.None;

            int slash = part.IndexOf('/');
            if (slash >= 0)
            {
                if (slash != part.LastIndexOf('/'))
                {
                    error = ScanRangeParseError.InvalidToken;
                    return false;
                }

                string addressText = part.Substring(0, slash).Trim();
                string prefixText = part.Substring(slash + 1).Trim();
                uint address;
                if (!IpUtil.TryParse(addressText, out address))
                {
                    error = ScanRangeParseError.InvalidToken;
                    return false;
                }

                int prefix;
                if (!int.TryParse(prefixText, NumberStyles.None, CultureInfo.InvariantCulture, out prefix)
                    || prefix < 0 || prefix > 32)
                {
                    error = ScanRangeParseError.InvalidPrefix;
                    return false;
                }

                uint mask = prefix == 0 ? 0u : 0xFFFFFFFFu << (32 - prefix);
                uint network = address & mask;
                uint broadcast = network | ~mask;
                if (prefix >= 31)
                {
                    range.Start = network;
                    range.End = broadcast;
                }
                else
                {
                    range.Start = network + 1u;
                    range.End = broadcast - 1u;
                }
                return range.Start <= range.End;
            }

            int dash = part.IndexOf('-');
            if (dash >= 0)
            {
                if (dash != part.LastIndexOf('-'))
                {
                    error = ScanRangeParseError.InvalidToken;
                    return false;
                }

                string startText = part.Substring(0, dash).Trim();
                string endText = part.Substring(dash + 1).Trim();
                uint start;
                uint end;
                if (!IpUtil.TryParse(startText, out start))
                {
                    error = ScanRangeParseError.InvalidToken;
                    return false;
                }
                if (!IpUtil.TryParse(endText, out end))
                {
                    int lastOctet;
                    if (endText.Length == 0 || endText.Length > 3
                        || !int.TryParse(endText, NumberStyles.None, CultureInfo.InvariantCulture, out lastOctet)
                        || lastOctet < 0 || lastOctet > 255)
                    {
                        error = ScanRangeParseError.InvalidToken;
                        return false;
                    }
                    end = (start & 0xFFFFFF00u) | (uint)lastOctet;
                }
                if (start > end)
                {
                    error = ScanRangeParseError.StartAfterEnd;
                    return false;
                }
                range.Start = start;
                range.End = end;
                return true;
            }

            uint single;
            if (!IpUtil.TryParse(part, out single))
            {
                error = ScanRangeParseError.InvalidToken;
                return false;
            }
            if (single == 0u || single == 0xFFFFFFFFu)
            {
                error = ScanRangeParseError.InvalidToken;
                return false;
            }
            range.Start = single;
            range.End = single;
            return true;
        }

        /// <summary>
        /// Développe les plages en adresses uniques, dans l'ordre, en ignorant
        /// 0.0.0.0 et 255.255.255.255.
        /// </summary>
        public static List<uint> Expand(List<ScanRange> ranges)
        {
            List<uint> addresses = new List<uint>();
            if (ranges == null || ranges.Count == 0)
            {
                return addresses;
            }

            HashSet<uint> seen = new HashSet<uint>();
            for (int i = 0; i < ranges.Count; i++)
            {
                ScanRange range = ranges[i];
                uint value = range.Start;
                while (true)
                {
                    if (value != 0u && value != 0xFFFFFFFFu && seen.Add(value))
                    {
                        addresses.Add(value);
                    }
                    if (value == range.End)
                    {
                        break;
                    }
                    value++;
                }
            }
            return addresses;
        }
    }
}
