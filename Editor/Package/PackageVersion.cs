using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Dreamy.EditorTools.Package
{
    internal sealed class PackageVersion : IComparable<PackageVersion>
    {
        private static readonly Regex Pattern = new(@"^v?(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$");
        public int Major, Minor, Patch;
        public string Pre;
        public bool IsPreview => !string.IsNullOrEmpty(Pre);
        public static bool TryParse(string text, out PackageVersion version)
        {
            version = null;
            var m = Pattern.Match(text ?? "");
            if (!m.Success || !int.TryParse(m.Groups[1].Value, out var major) ||
                !int.TryParse(m.Groups[2].Value, out var minor) || !int.TryParse(m.Groups[3].Value, out var patch)) return false;
            var pre = m.Groups[4].Value;
            if (pre.Split('.').Any(s => s.Length > 1 && s[0] == '0' && s.All(char.IsDigit))) return false;
            version = new PackageVersion { Major = major, Minor = minor, Patch = patch, Pre = pre };
            return true;
        }
        public int CompareTo(PackageVersion other)
        {
            if (other == null) return 1;
            int c = Major.CompareTo(other.Major);
            if (c != 0) return c;
            c = Minor.CompareTo(other.Minor);
            if (c != 0) return c;
            c = Patch.CompareTo(other.Patch);
            if (c != 0) return c;
            if (!IsPreview || !other.IsPreview) return IsPreview == other.IsPreview ? 0 : IsPreview ? -1 : 1;
            var a = Pre.Split('.'); var b = other.Pre.Split('.');
            for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
            {
                bool an = a[i].All(char.IsDigit), bn = b[i].All(char.IsDigit);
                c = an && bn ? (a[i].Length == b[i].Length ? string.CompareOrdinal(a[i], b[i]) : a[i].Length.CompareTo(b[i].Length))
                    : an != bn ? (an ? -1 : 1) : string.CompareOrdinal(a[i], b[i]);
                if (c != 0) return c;
            }
            return a.Length.CompareTo(b.Length);
        }
        internal static bool Meets(string actual, string minimum) => TryParse(actual, out var a) && TryParse(minimum, out var m) && a.CompareTo(m) >= 0;
        internal static bool UnityCompatible(string required, string current)
        {
            if (string.IsNullOrEmpty(required)) return true;
            var a = Regex.Match(required, @"^(\d+)\.(\d+)(?:\.(\d+))?");
            var b = Regex.Match(current ?? "", @"^(\d+)\.(\d+)\.(\d+)");
            if (!a.Success || !b.Success) return false;
            for (int i = 1; i <= 3; i++)
            {
                if (!int.TryParse(a.Groups[i].Success ? a.Groups[i].Value : "0", out var av) || !int.TryParse(b.Groups[i].Value, out var bv)) return false;
                if (av != bv) return bv > av;
            }
            return true;
        }
    }
}
