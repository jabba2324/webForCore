// webForCore (#212): .NET Framework 4.8's StringComparer.InvariantCultureIgnoreCase.GetHashCode, which System.Web uses
// where a string's hash leaves the process: __VIEWSTATEGENERATOR (Page.GetClientStateIdentifier, and MobilePage's view
// state MAC key) and the <machineKey> IsolateApps and IsolateByAppId modifiers (MachineKeySection). Written for
// webForCore (MIT license); not code from .NET.
//
// On .NET Framework the hash is CompareInfo.GetHashCodeOfString: Windows' sort key for the invariant locale
// (LCMapStringEx with LCMAP_SORTKEY | NORM_IGNORECASE | NORM_LINGUISTIC_CASING), hashed byte by byte up to its first zero
// byte, alternating between two djb2-style accumulators (seed 5381, h = (h * 33) ^ b), combined as h1 + h2 * 1566083941;
// the empty string's hash is 0. .NET's string hashes are randomized and Linux has no NLS sort keys, so the key is
// built here from a table of each UTF-16 code unit's weights (FrameworkData/InvariantIgnoreCaseWeights.txt), read
// from .NET Framework by tools/stringhash/FrameworkSortKeys and checked against it on Windows by CI. A key is:
//   the primary weights of each character
//   01 the diacritic weight of each element (a nonspacing mark's added to the element before it, modulo 256), trailing
//      weights of 2 or less left out
//   01 the case weight of each element (upper case already folded), trailing 2s left out
//   01 01 the special weights (hyphen, apostrophe): FFFF minus the number of positions before it (2 bytes, big endian)
//      and the weight
//   00
// A code unit the table doesn't list (kana, iteration and length marks, conjoining Hangul jamo, private use characters,
// surrogates and a few others, whose weights depend on context in ways the table doesn't model) makes TryGetHashCode
// return false: the caller uses its own hash, and the value differs from .NET Framework's (docs/IIS-COMPATIBILITY.md).
// Compiled into System.Web, System.Web.Mobile, IIS.Gate.Tests and tools/stringhash/FrameworkSortKeys (on .NET
// Framework 4.8).
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace System.Web.Util
{
	internal sealed class InvariantIgnoreCaseHash
	{
		private const string ResourceName = "System.Web.Util.InvariantIgnoreCaseWeights.txt";

		private static readonly Lazy<InvariantIgnoreCaseHash> s_default = new Lazy<InvariantIgnoreCaseHash>(LoadDefault);

		// Per code unit: the offset of its entry in _entries, or -1. An entry is: kind (i, c, s, m), the number of primary
		// weight bytes, the number of elements, the number of positions, then the primary weights, the diacritic and the
		// case weights (one per element) of a c; the 2 bytes of an s; the diacritic weight of an m.
		private readonly int[] _offsets = new int[0x10000];
		private readonly List<byte> _entries = new List<byte>();

		internal InvariantIgnoreCaseHash(TextReader table)
		{
			for (int i = 0; i < _offsets.Length; i++) _offsets[i] = -1;
			string line;
			while ((line = table.ReadLine()) != null)
			{
				if (line.Length == 0 || line[0] == '#') continue;
				string[] fields = line.Split(' ');
				int first = int.Parse(fields[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
				int count = int.Parse(fields[1], CultureInfo.InvariantCulture);
				char kind = fields[2][0];
				byte[] primary = kind == 'c' || kind == 's' ? Bytes(fields[3]) : new byte[0];
				byte[] diacritic = kind == 'c' ? Bytes(fields[4]) : kind == 'm' ? Bytes(fields[3]) : new byte[0];
				byte[] @case = kind == 'c' ? Bytes(fields[5]) : new byte[0];
				int positions = kind == 'c' ? int.Parse(fields[6], CultureInfo.InvariantCulture) : 0;
				if (first + count > 0x10000 || (kind == 'c' && (diacritic.Length != @case.Length || primary.Length == 0)) || (kind == 's' && primary.Length != 2)
					|| (kind == 'm' && diacritic.Length != 1) || "icsm".IndexOf(kind) < 0)
				{
					throw new InvalidDataException("Invalid line in " + ResourceName + ": " + line);
				}
				for (int n = 0; n < count; n++)
				{
					_offsets[first + n] = _entries.Count;
					_entries.Add((byte)kind);
					_entries.Add((byte)primary.Length);
					_entries.Add((byte)diacritic.Length);
					_entries.Add((byte)positions);
					// The primary weights of a run of c lines go up by one per code unit.
					_entries.AddRange(kind == 'c' ? Add(primary, n) : primary);
					_entries.AddRange(diacritic);
					_entries.AddRange(@case);
				}
			}
		}

		// The table in this assembly.
		internal static InvariantIgnoreCaseHash Default => s_default.Value;

		// .NET Framework's StringComparer.InvariantCultureIgnoreCase.GetHashCode(s), when the table has every code unit of s.
		internal static bool TryGetFrameworkHashCode(string s, out int hashCode) => Default.TryGetHashCode(s, out hashCode);

		internal bool Reproduces(char c) => _offsets[c] >= 0;

		// .NET Framework's hash when the table reproduces s; otherwise a stable hash of s in lower case (the same in every
		// process, not .NET Framework's). For System.Web.Mobile, which has no StringUtil.
		internal static int GetFrameworkOrStableHashCode(string s)
		{
			if (TryGetFrameworkHashCode(s, out int hashCode)) return hashCode;
			uint hash1 = 5381, hash2 = 5381;
			string lower = s.ToLowerInvariant();
			for (int i = 0; i < lower.Length; i += 2)
			{
				hash1 = unchecked(((hash1 << 5) + hash1) ^ lower[i]);
				if (i + 1 < lower.Length) hash2 = unchecked(((hash2 << 5) + hash2) ^ lower[i + 1]);
			}
			return unchecked((int)(hash1 + hash2 * 1566083941u));
		}

		internal bool TryGetHashCode(string s, out int hashCode)
		{
			hashCode = 0;
			if (s == null) throw new ArgumentNullException(nameof(s));
			if (s.Length == 0) return true;

			var primary = new List<byte>(s.Length * 2);
			var diacritic = new List<byte>(s.Length);
			var @case = new List<byte>(s.Length);
			var special = new List<byte>();
			int positions = 0;
			foreach (char c in s)
			{
				int offset = _offsets[c];
				if (offset < 0) return false;
				char kind = (char)_entries[offset];
				int primaryLength = _entries[offset + 1], elements = _entries[offset + 2];
				int weights = offset + 4;
				switch (kind)
				{
					case 'i':
						break;
					case 'c':
						for (int i = 0; i < primaryLength; i++) primary.Add(_entries[weights + i]);
						for (int i = 0; i < elements; i++) diacritic.Add(_entries[weights + primaryLength + i]);
						for (int i = 0; i < elements; i++) @case.Add(_entries[weights + primaryLength + elements + i]);
						positions += _entries[offset + 3];
						break;
					case 's':
						// Positions past FFFF aren't reproduced (the corpus has no string near that long).
						if (positions > 0xFFFF) return false;
						int position = 0xFFFF - positions;
						special.Add((byte)(position >> 8));
						special.Add((byte)position);
						special.Add(_entries[weights]);
						special.Add(_entries[weights + 1]);
						break;
					default: // 'm'
						byte mark = _entries[weights];
						if (diacritic.Count > 0) diacritic[diacritic.Count - 1] = (byte)(diacritic[diacritic.Count - 1] + mark);
						else diacritic.Add(mark);
						break;
				}
			}
			while (diacritic.Count > 0 && diacritic[diacritic.Count - 1] <= 2) diacritic.RemoveAt(diacritic.Count - 1);
			while (@case.Count > 0 && @case[@case.Count - 1] == 2) @case.RemoveAt(@case.Count - 1);

			var key = new List<byte>(primary.Count + diacritic.Count + @case.Count + special.Count + 5);
			key.AddRange(primary);
			key.Add(1);
			key.AddRange(diacritic);
			key.Add(1);
			key.AddRange(@case);
			key.Add(1);
			key.Add(1);
			key.AddRange(special);
			key.Add(0);
			hashCode = HashSortKey(key);
			return true;
		}

		// The sort key's bytes up to the first zero, alternately into two accumulators.
		internal static int HashSortKey(IReadOnlyList<byte> key)
		{
			uint hash1 = 5381, hash2 = 5381;
			for (int i = 0; ; i += 2)
			{
				if (i >= key.Count || key[i] == 0) break;
				hash1 = unchecked(((hash1 << 5) + hash1) ^ key[i]);
				if (i + 1 >= key.Count || key[i + 1] == 0) break;
				hash2 = unchecked(((hash2 << 5) + hash2) ^ key[i + 1]);
			}
			return unchecked((int)(hash1 + hash2 * 1566083941u));
		}

		private static InvariantIgnoreCaseHash LoadDefault()
		{
			Stream stream = typeof(InvariantIgnoreCaseHash).Assembly.GetManifestResourceStream(ResourceName)
				?? throw new InvalidOperationException(ResourceName + " is missing from " + typeof(InvariantIgnoreCaseHash).Assembly.FullName);
			using (var reader = new StreamReader(stream))
			{
				return new InvariantIgnoreCaseHash(reader);
			}
		}

		private static byte[] Bytes(string hex)
		{
			if (hex == "-") return new byte[0];
			var bytes = new byte[hex.Length / 2];
			for (int i = 0; i < bytes.Length; i++) bytes[i] = byte.Parse(hex.Substring(2 * i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
			return bytes;
		}

		private static byte[] Add(byte[] weights, int n)
		{
			byte[] result = (byte[])weights.Clone();
			int carry = n;
			for (int i = result.Length - 1; i >= 0 && carry != 0; i--)
			{
				int v = result[i] + carry;
				result[i] = (byte)v;
				carry = v >> 8;
			}
			return result;
		}
	}
}
