// webForCore (#212): .NET Framework's StringComparer.InvariantCultureIgnoreCase.GetHashCode is a hash of the string's
// sort key: CompareInfo.GetHashCodeOfString asks Windows (LCMapStringEx, LCMAP_SORTKEY with NORM_IGNORECASE) for the
// invariant locale's sort key and hashes its bytes. System.Web uses that hash for __VIEWSTATEGENERATOR and for the
// <machineKey> IsolateApps and IsolateByAppId modifiers. .NET has neither the hash (its string hashes are randomized)
// nor Windows' sort keys on Linux, so webForCore builds the sort key from a table of each character's weights
// (src/WebFormsForCore.Web/WebFormsForCore/InvariantIgnoreCaseHash.cs). This program makes that table from .NET
// Framework itself, and checks it.
//
// For each UTF-16 code unit c it reads the sort keys of c alone and of c in four contexts (c + e-acute, c + fullwidth
// A, c + "-", "a" + c) and keeps c only if one of the four kinds the table has explains all five keys: ignored (adds
// nothing), a character (primary weights, a diacritic and a case weight for each of its sort elements, and the number
// of positions it takes), a special weight (hyphen, apostrophe: a weight placed by position at the end of the key), or
// a nonspacing mark (adds its diacritic weight to the previous element's). Characters it can't explain (kana, which have
// East Asian extra weights; iteration and length marks; private use characters, among others), and the ones whose
// weights depend on the characters around them although the five keys don't show it (surrogates, which form
// supplementary characters, and conjoining Hangul jamo, which form syllables), are left out: webForCore doesn't
// reproduce a string that has one, and says so (the caller falls back to its own stable hash).
//
// Windows' weights change between Windows versions (the invariant sort version: 6.4.3 on Windows Server 2022, 6.5.2 on
// Windows Server 2025), so the committed table has only the code units whose weights are the same on every version
// it was made from (--other: another version's full table).
//
// It hashes a fixed set of strings (paths and class names, ASCII, Latin, other scripts, and strings with characters left
// out) with .NET Framework, and with webForCore's code (InvariantIgnoreCaseHash.cs, compiled in here) from the table,
// and random strings of the table's characters: every string webForCore says it reproduces must have .NET Framework's
// hash.
//
// Usage (on Windows, .NET Framework 4.8):
//   FrameworkSortKeys.exe <output directory> [--other <full table of another Windows version>]
//       [--check <committed table> <committed hashes>]
// writes full-<sort version>.txt (every code unit this Windows explains), InvariantIgnoreCaseWeights.txt (that, or with
// --other the code units both have the same) and invariant-hash-framework.tsv (.NET Framework's hashes). With --check,
// every code unit of the committed table must have the same weights here, and every committed hash of a string the
// committed table reproduces must be this .NET Framework's (all of them if this is the version they were written on).
// Exits with 1 on a difference.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Util;

internal static class Program
{
    private static readonly CompareInfo Invariant = CultureInfo.InvariantCulture.CompareInfo;
    private static int s_failures;

    private static int Main(string[] args)
    {
        string output = null, other = null, committedTable = null, committedHashes = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--other" && i + 1 < args.Length) other = args[++i];
            else if (args[i] == "--check" && i + 2 < args.Length) { committedTable = args[++i]; committedHashes = args[++i]; }
            else if (output == null) output = args[i];
            else output = null;
        }
        if (output == null)
        {
            Console.Error.WriteLine("usage: FrameworkSortKeys <output directory> [--other <full table>] [--check <committed table> <committed hashes>]");
            return 2;
        }
        Directory.CreateDirectory(output);
        string version = Invariant.Version.FullVersion.ToString("x", CultureInfo.InvariantCulture);
        Console.WriteLine(".NET Framework " + Environment.Version + ", " + typeof(object).Assembly.FullName + ", " + Environment.OSVersion);
        Console.WriteLine("Invariant sort version " + version + ", " + Invariant.Version.SortId);
        if (StringComparer.InvariantCultureIgnoreCase.GetHashCode("The quick brown fox jumps over the lazy dog.") != 0x703e662e)
        {
            Console.WriteLine("FAILED  string hashes are randomized here (System.Web's test vector differs)");
            return 1;
        }

        // This Windows' table, and the one to commit.
        Entry[] full = Table.Probe();
        File.WriteAllText(Path.Combine(output, "full-" + version + ".txt"), Table.Write(full, new[] { version }), new UTF8Encoding(false));
        Console.WriteLine("This Windows: " + Table.Summary(full));
        Entry[] merged = full;
        string[] versions = { version };
        if (other != null)
        {
            Entry[] otherTable = Table.Read(File.ReadAllText(other), out string[] otherVersions);
            merged = new Entry[0x10000];
            for (int c = 0; c < 0x10000; c++)
            {
                if (full[c] != null && otherTable[c] != null && full[c].Same(otherTable[c])) merged[c] = full[c];
            }
            versions = otherVersions.Concat(versions).Distinct().OrderBy(v => v, StringComparer.Ordinal).ToArray();
            Console.WriteLine("Same on " + string.Join(", ", versions) + ": " + Table.Summary(merged));
        }
        string tablePath = Path.Combine(output, "InvariantIgnoreCaseWeights.txt");
        File.WriteAllText(tablePath, Table.Write(merged, versions), new UTF8Encoding(false));

        // The table under test: the committed one, or the one just written.
        Entry[] tested = merged;
        string testedText = Table.Write(merged, versions);
        if (committedTable != null)
        {
            testedText = File.ReadAllText(committedTable);
            tested = Table.Read(testedText, out _);
            int differ = 0;
            for (int c = 0; c < 0x10000; c++)
            {
                if (tested[c] != null && (full[c] == null || !full[c].Same(tested[c])))
                {
                    if (differ++ < 20) Fail("code unit " + c.ToString("x4") + " is in the committed table, but its weights here are " + (full[c] == null ? "not explained" : "different"));
                }
            }
            Console.WriteLine((differ == 0 ? "ok      " : "FAILED  ") + "the committed table's code units have the same weights here (" + differ + " differ)");
            if (differ > 0) s_failures++;
        }
        var hash = new InvariantIgnoreCaseHash(new StringReader(testedText));

        // .NET Framework's hashes of the corpus.
        List<KeyValuePair<string, string>> corpus = Corpus.Create();
        var lines = new StringBuilder();
        lines.Append("# .NET Framework 4.8's StringComparer.InvariantCultureIgnoreCase.GetHashCode of each string (#212), written by\n");
        lines.Append("# tools/stringhash/FrameworkSortKeys on Windows' invariant sort version " + version + ". Group, the string's UTF-16 code\n");
        lines.Append("# units (hex), the hash (hex).\n");
        var counts = new Dictionary<string, int[]>();
        foreach (KeyValuePair<string, string> entry in corpus)
        {
            int framework = StringComparer.InvariantCultureIgnoreCase.GetHashCode(entry.Value);
            lines.Append(entry.Key).Append('\t').Append(Hex(entry.Value)).Append('\t').Append(framework.ToString("x8", CultureInfo.InvariantCulture)).Append('\n');
            if (!counts.TryGetValue(entry.Key, out int[] count)) counts[entry.Key] = count = new int[2];
            count[1]++;
            if (hash.TryGetHashCode(entry.Value, out int ours))
            {
                if (ours == framework) count[0]++;
                else Fail(entry.Key + " " + Hex(entry.Value) + ": .NET Framework " + framework.ToString("x8") + ", webForCore " + ours.ToString("x8"));
            }
        }
        foreach (KeyValuePair<string, int[]> group in counts)
        {
            Console.WriteLine("Reproduced " + group.Value[0] + " of " + group.Value[1] + " strings: " + group.Key);
        }
        string hashesPath = Path.Combine(output, "invariant-hash-framework.tsv");
        File.WriteAllText(hashesPath, lines.ToString(), new UTF8Encoding(false));

        if (committedHashes != null)
        {
            string[] committedLines = File.ReadAllLines(committedHashes);
            bool sameVersion = committedLines.Length > 1 && committedLines[1].Contains("sort version " + version + ".");
            int differ = 0;
            foreach (string line in committedLines)
            {
                if (line.StartsWith("#", StringComparison.Ordinal)) continue;
                string[] fields = line.Split('\t');
                string value = Unhex(fields[1]);
                int framework = StringComparer.InvariantCultureIgnoreCase.GetHashCode(value);
                if ((sameVersion || hash.TryGetHashCode(value, out _)) && framework.ToString("x8", CultureInfo.InvariantCulture) != fields[2])
                {
                    if (differ++ < 20) Fail("committed hash of " + fields[1] + " is " + fields[2] + ", .NET Framework's here " + framework.ToString("x8"));
                }
            }
            if (sameVersion && File.ReadAllText(committedHashes) != lines.ToString())
            {
                differ++;
                Fail(committedHashes + " differs from invariant-hash-framework.tsv in the output (the strings changed?)");
            }
            Console.WriteLine((differ == 0 ? "ok      " : "FAILED  ") + committedHashes + (sameVersion ? " (written on this version: every hash)" : " (the hashes of the strings the table reproduces)"));
        }

        // Random strings of the table's characters, beyond the committed ones: mostly from the same block, as names are.
        int random = 0;
        var rnd = new Random(212);
        char[] characters = Enumerable.Range(0, 0x10000).Select(c => (char)c).Where(c => hash.Reproduces(c)).ToArray();
        const int randomCount = 300000;
        for (int i = 0; i < randomCount; i++)
        {
            var s = new StringBuilder();
            int length = rnd.Next(1, 24);
            int block = characters[rnd.Next(characters.Length)] & 0xFF80;
            for (int j = 0; j < length; j++)
            {
                char c = characters[rnd.Next(characters.Length)];
                if (rnd.Next(3) > 0)
                {
                    char near = (char)(block + rnd.Next(0x80));
                    if (hash.Reproduces(near)) c = near;
                }
                s.Append(c);
            }
            string value = s.ToString();
            int framework = StringComparer.InvariantCultureIgnoreCase.GetHashCode(value);
            if (!hash.TryGetHashCode(value, out int ours) || ours != framework)
            {
                Fail("random " + Hex(value) + ": .NET Framework " + framework.ToString("x8") + ", webForCore " + ours.ToString("x8"));
            }
            else random++;
        }
        Console.WriteLine("Reproduced " + random + " of " + randomCount + " random strings of the table's " + characters.Length + " code units");

        Console.WriteLine(s_failures == 0 ? "All ok." : s_failures + " failed.");
        return s_failures == 0 ? 0 : 1;
    }

    private static void Fail(string message)
    {
        if (s_failures++ < 100) Console.WriteLine("FAILED  " + message);
    }

    internal static byte[] SortKey(string s) => Invariant.GetSortKey(s, CompareOptions.IgnoreCase).KeyData;

    internal static string S(params int[] codeUnits) => new string(codeUnits.Select(c => (char)c).ToArray());

    internal static string Hex(string s)
    {
        var b = new StringBuilder(s.Length * 4);
        foreach (char c in s) b.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
        return b.ToString();
    }

    internal static string Unhex(string hex)
    {
        var b = new StringBuilder(hex.Length / 4);
        for (int i = 0; i < hex.Length; i += 4) b.Append((char)int.Parse(hex.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        return b.ToString();
    }

    internal static string Hex(IEnumerable<byte> bytes)
    {
        var b = new StringBuilder();
        foreach (byte x in bytes) b.Append(x.ToString("x2", CultureInfo.InvariantCulture));
        return b.ToString();
    }

    internal static byte[] Bytes(string hex)
    {
        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++) bytes[i] = byte.Parse(hex.Substring(2 * i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return bytes;
    }
}

// One code unit's weights, as InvariantIgnoreCaseWeights.txt has them.
internal sealed class Entry
{
    public char Kind;                      // i, c, s or m
    public byte[] Primary = new byte[0];   // c: the primary weights; s: the special weight
    public byte[] Diacritic = new byte[0]; // c: one per element; m: the mark's
    public byte[] Case = new byte[0];      // c: one per element
    public int Positions;                  // c: the positions it takes (where a later special weight goes)

    // The line's fields after the count, with these primary weights.
    public string Fields(byte[] primary)
    {
        switch (Kind)
        {
            case 'i': return "i";
            case 'c': return "c " + Program.Hex(primary) + " " + Program.Hex(Diacritic) + " " + Program.Hex(Case) + " " + Positions.ToString(CultureInfo.InvariantCulture);
            case 's': return "s " + Program.Hex(primary);
            default: return "m " + Program.Hex(Diacritic);
        }
    }

    public bool Same(Entry other) => Fields(Primary) == other.Fields(other.Primary);
}

internal static class Table
{
    // Left out although the five keys fit a kind: their weights depend on the characters around them.
    private static bool LeftOut(int c) =>
        (c >= 0xD800 && c <= 0xDFFF)      // surrogates: a pair is one supplementary character
        || (c >= 0x1100 && c <= 0x11FF)   // Hangul Jamo: conjoining jamo sequences sort as syllables
        || (c >= 0xA960 && c <= 0xA97F)   // Hangul Jamo Extended-A
        || (c >= 0xD7B0 && c <= 0xD7FF);  // Hangul Jamo Extended-B

    public static Entry[] Probe()
    {
        var entries = new Entry[0x10000];
        string eAcute = Program.S(0xe9), fullwidthA = Program.S(0xff21);
        byte[] eAcuteKey = Program.SortKey(eAcute), fullwidthAKey = Program.SortKey(fullwidthA), aKey = Program.SortKey("a"), hyphenKey = Program.SortKey("-");
        for (int c = 0; c < 0x10000; c++)
        {
            if (LeftOut(c)) continue;
            string s = ((char)c).ToString();
            entries[c] = Classify(Program.SortKey(s), Program.SortKey(s + eAcute), Program.SortKey(s + fullwidthA), Program.SortKey(s + "-"),
                Program.SortKey("a" + s), eAcuteKey, fullwidthAKey, aKey, hyphenKey);
        }
        return entries;
    }

    public static string Summary(Entry[] entries)
    {
        int Count(char kind) => entries.Count(e => e != null && e.Kind == kind);
        return Count('c') + " characters, " + Count('i') + " ignored, " + Count('s') + " special weights, " + Count('m') + " nonspacing marks; "
            + entries.Count(e => e == null) + " code units not reproduced";
    }

    public static string Write(Entry[] entries, string[] versions)
    {
        var text = new StringBuilder();
        text.Append("# The invariant locale's sort-key weights behind .NET Framework 4.8's StringComparer.InvariantCultureIgnoreCase\n");
        text.Append("# hash (#212), read from .NET Framework by tools/stringhash/FrameworkSortKeys. Windows' invariant sort versions:\n");
        text.Append("# " + string.Join(" ", versions) + "\n");
        text.Append("# Used by System.Web.Util.InvariantIgnoreCaseHash. <first UTF-16 code unit> <count> <kind> <weights>, one line for a\n");
        text.Append("# run of code units with the same weights (for c, the primary weights of each are the previous one's plus one):\n");
        text.Append("#   i                                   ignored\n");
        text.Append("#   c <primary> <diacritic> <case> <n>  a character: primary weights, a diacritic and a case weight per element,\n");
        text.Append("#                                       n positions\n");
        text.Append("#   s <weight>                          a special weight (hyphen, apostrophe), put at the end with its position\n");
        text.Append("#   m <diacritic>                       a nonspacing mark: added to the previous element's diacritic weight\n");
        text.Append("# A code unit that isn't listed isn't reproduced.\n");
        for (int c = 0; c < 0x10000;)
        {
            Entry entry = entries[c];
            if (entry == null) { c++; continue; }
            int count = 1;
            while (c + count < 0x10000 && entries[c + count] != null
                && entry.Fields(entry.Kind == 'c' ? Add(entry.Primary, count) : entry.Primary) == entries[c + count].Fields(entries[c + count].Primary))
            {
                count++;
            }
            text.Append(c.ToString("x4", CultureInfo.InvariantCulture)).Append(' ').Append(count.ToString(CultureInfo.InvariantCulture)).Append(' ')
                .Append(entry.Fields(entry.Primary)).Append('\n');
            c += count;
        }
        return text.ToString();
    }

    public static Entry[] Read(string text, out string[] versions)
    {
        var entries = new Entry[0x10000];
        string[] lines = text.Split('\n');
        versions = lines[2].TrimStart('#', ' ').Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string line in lines)
        {
            if (line.Length == 0 || line[0] == '#') continue;
            string[] f = line.Split(' ');
            int first = int.Parse(f[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture), count = int.Parse(f[1], CultureInfo.InvariantCulture);
            for (int n = 0; n < count; n++)
            {
                var entry = new Entry { Kind = f[2][0] };
                switch (entry.Kind)
                {
                    case 'c':
                        entry.Primary = Add(Program.Bytes(f[3]), n);
                        entry.Diacritic = Program.Bytes(f[4]);
                        entry.Case = Program.Bytes(f[5]);
                        entry.Positions = int.Parse(f[6], CultureInfo.InvariantCulture);
                        break;
                    case 's':
                        entry.Primary = Program.Bytes(f[3]);
                        break;
                    case 'm':
                        entry.Diacritic = Program.Bytes(f[3]);
                        break;
                }
                entries[first + n] = entry;
            }
        }
        return entries;
    }

    // The primary weights plus n, as a big-endian number of the same length (empty if it overflows).
    private static byte[] Add(byte[] primary, int n)
    {
        byte[] result = (byte[])primary.Clone();
        int carry = n;
        for (int i = result.Length - 1; i >= 0 && carry != 0; i--)
        {
            int v = result[i] + carry;
            result[i] = (byte)v;
            carry = v >> 8;
        }
        return carry == 0 ? result : new byte[0];
    }

    private static Entry Classify(byte[] alone, byte[] withEAcute, byte[] withFullwidthA, byte[] withHyphen, byte[] afterA,
        byte[] eAcute, byte[] fullwidthA, byte[] a, byte[] hyphen)
    {
        string single = Program.Hex(alone);
        // Ignored: adds nothing to any key.
        if (single == "0101010100" && Program.Hex(withEAcute) == Program.Hex(eAcute) && Program.Hex(withFullwidthA) == Program.Hex(fullwidthA)
            && Program.Hex(afterA) == Program.Hex(a) && Program.Hex(withHyphen) == Program.Hex(hyphen))
        {
            return new Entry { Kind = 'i' };
        }

        // A special weight: position 0 alone (ffff), 1 after "a" (fffe).
        if (alone.Length == 9 && single.StartsWith("01010101ffff", StringComparison.Ordinal))
        {
            byte[] weight = { alone[6], alone[7] };
            if (Program.Hex(afterA) == "0e0201010101fffe" + Program.Hex(weight) + "00"
                && Program.Hex(withEAcute) == "0e21010e010101ffff" + Program.Hex(weight) + "00"
                && Program.Hex(withFullwidthA) == "0e020101030101ffff" + Program.Hex(weight) + "00")
            {
                return new Entry { Kind = 's', Primary = weight };
            }
        }

        // A nonspacing mark: no primary weight; its diacritic weight is added to the previous element's, or is an
        // element of its own (with no case weight) at the start.
        if (alone.Length == 6 && alone[0] == 1 && alone[1] > 2 && alone[2] == 1 && alone[3] == 1 && alone[4] == 1)
        {
            byte mark = alone[1];
            if (Program.Hex(afterA) == Key(new byte[] { 0x0e, 0x02 }, new[] { (byte)(2 + mark) }, new byte[0], new byte[0])
                && Program.Hex(withEAcute) == Key(new byte[] { 0x0e, 0x21 }, new[] { mark, (byte)0x0e }, new byte[0], new byte[0])
                && Program.Hex(withFullwidthA) == Key(new byte[] { 0x0e, 0x02 }, new[] { mark }, new byte[] { 3 }, new byte[0]))
            {
                return new Entry { Kind = 'm', Diacritic = new[] { mark } };
            }
        }

        // A character: its primary weights are where the keys alone and before e-acute part.
        int n = 0;
        while (n < alone.Length && n < withEAcute.Length && alone[n] == withEAcute[n]) n++;
        if (n == 0) return null;
        byte[] primary = alone.Take(n).ToArray();
        // The rest alone: 01 <diacritic> 01 <case> 01 01 00.
        var parts = new List<List<byte>> { new List<byte>() };
        if (alone[n] != 1) return null;
        for (int i = n + 1; i < alone.Length - 1; i++)
        {
            if (alone[i] == 1) parts.Add(new List<byte>());
            else parts[parts.Count - 1].Add(alone[i]);
        }
        if (parts.Count != 4 || parts[2].Count != 0 || parts[3].Count != 0) return null;
        if (withHyphen.Length < 5) return null;
        int positions = 0xFFFF - ((withHyphen[withHyphen.Length - 5] << 8) | withHyphen[withHyphen.Length - 4]);
        byte[] hyphenWeight = { withHyphen[withHyphen.Length - 3], withHyphen[withHyphen.Length - 2] };
        for (int elements = 1; elements <= 4; elements++)
        {
            if (parts[0].Count > elements || parts[1].Count > elements) continue;
            byte[] diacritic = Pad(parts[0], elements), @case = Pad(parts[1], elements);
            if (Program.Hex(alone) == Key(primary, diacritic, @case, new byte[0])
                && Program.Hex(afterA) == Key(Concat(new byte[] { 0x0e, 0x02 }, primary), Concat(new byte[] { 2 }, diacritic), Concat(new byte[] { 2 }, @case), new byte[0])
                && Program.Hex(withEAcute) == Key(Concat(primary, new byte[] { 0x0e, 0x21 }), Concat(diacritic, new byte[] { 0x0e }), @case, new byte[0])
                && Program.Hex(withFullwidthA) == Key(Concat(primary, new byte[] { 0x0e, 0x02 }), diacritic, Concat(@case, new byte[] { 3 }), new byte[0])
                && Program.Hex(withHyphen) == Key(primary, diacritic, @case, new byte[] { (byte)((0xFFFF - positions) >> 8), (byte)(0xFFFF - positions), hyphenWeight[0], hyphenWeight[1] }))
            {
                return new Entry { Kind = 'c', Primary = primary, Diacritic = diacritic, Case = @case, Positions = positions };
            }
        }
        return null;
    }

    private static byte[] Pad(List<byte> weights, int elements)
    {
        var padded = new List<byte>(weights);
        while (padded.Count < elements) padded.Add(2);
        return padded.ToArray();
    }

    private static byte[] Concat(byte[] x, byte[] y) => x.Concat(y).ToArray();

    // A sort key as Windows writes it: the trailing minimal diacritic and case weights left out.
    private static string Key(byte[] primary, byte[] diacritic, byte[] @case, byte[] special)
    {
        var d = new List<byte>(diacritic);
        while (d.Count > 0 && d[d.Count - 1] <= 2) d.RemoveAt(d.Count - 1);
        var c = new List<byte>(@case);
        while (c.Count > 0 && c[c.Count - 1] == 2) c.RemoveAt(c.Count - 1);
        var key = new List<byte>(primary) { 1 };
        key.AddRange(d);
        key.Add(1);
        key.AddRange(c);
        key.Add(1);
        key.Add(1);
        key.AddRange(special);
        key.Add(0);
        return Program.Hex(key);
    }
}

// The strings whose .NET Framework hashes are committed (group, string): what System.Web hashes (virtual paths,
// application IDs, page directories and class names), random ASCII paths and class names, Latin, other scripts, and
// strings with characters webForCore doesn't reproduce. The same strings each run.
internal static class Corpus
{
    public static List<KeyValuePair<string, string>> Create()
    {
        var list = new List<KeyValuePair<string, string>>();
        void Add(string group, string value) => list.Add(new KeyValuePair<string, string>(group, value));
        string S(params int[] codeUnits) => Program.S(codeUnits);

        foreach (string s in new[]
        {
            "The quick brown fox jumps over the lazy dog.", "/", "/app", "/App", "/APP", "/interop", "/Interop", "/app/", "/LM/W3SVC/1/ROOT",
            "/LM/W3SVC/1/ROOT/app", "/LM/W3SVC/2/ROOT/interop", "/linq", "linq_products_aspx", "/regressions", "regressions_viewstategenerator_aspx",
            "default_aspx", "Default_aspx", "ASP.default_aspx", "_Default", "account_login_aspx", "/Account", "/WebSite1", "site_master",
            "global_asax", "/Pages/Admin", "admin_users_aspx", "/co-op", "o'brien_aspx", "/my folder", "/a.b-c_d~e",
            S(0xfc) + "ber_aspx", "/" + S(0xdc) + "ber", "/caf" + S(0xe9), "caf" + S(0xe9) + "_aspx", "/na" + S(0xef) + "ve", "/" + S(0xc6, 0x72, 0xf8),
            "/stra" + S(0xdf) + "e", "/" + S(0x130) + "stanbul", "/" + S(0x131) + "i", "/e" + S(0x301), "/" + S(0x41f, 0x440, 0x438, 0x432, 0x435, 0x442),
            "/" + S(0x4e2d, 0x6587), "/" + S(0xd55c, 0xae00),
        })
        {
            Add("fixed", s);
        }

        var rnd = new Random(2121);
        string RandomOf(string alphabet, int min, int max)
        {
            var s = new StringBuilder();
            int length = rnd.Next(min, max + 1);
            for (int i = 0; i < length; i++) s.Append(alphabet[rnd.Next(alphabet.Length)]);
            return s.ToString();
        }
        const string asciiMix = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789/_.-'";
        string RandomIn(int first, int last, int min, int max, double ascii)
        {
            var s = new StringBuilder();
            int length = rnd.Next(min, max + 1);
            for (int i = 0; i < length; i++)
            {
                if (rnd.NextDouble() < ascii) s.Append(asciiMix[rnd.Next(asciiMix.Length)]);
                else s.Append((char)rnd.Next(first, last + 1));
            }
            return s.ToString();
        }

        const string letters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
        const string identifier = letters + "0123456789_";
        const string segment = identifier + ".-~ '!$&()+,;=@";
        for (int i = 0; i < 1500; i++)
        {
            var p = new StringBuilder();
            int segments = rnd.Next(1, 5);
            for (int j = 0; j < segments; j++) p.Append('/').Append(RandomOf(segment, 1, 12));
            if (rnd.Next(4) == 0) p.Append('/');
            Add("ascii-path", p.ToString());
        }
        for (int i = 0; i < 1000; i++) Add("ascii-class", RandomOf(letters + "_", 1, 1) + RandomOf(identifier, 0, 30) + (rnd.Next(2) == 0 ? "_aspx" : ""));
        string printable = new string(Enumerable.Range(0x20, 0x5f).Select(c => (char)c).ToArray());
        for (int i = 0; i < 1000; i++) Add("ascii-printable", RandomOf(printable, 1, 40));
        string ascii = new string(Enumerable.Range(0, 0x80).Select(c => (char)c).ToArray());
        for (int i = 0; i < 500; i++) Add("ascii-all", RandomOf(ascii, 1, 20));
        for (int i = 0; i < 1000; i++) Add("latin", RandomIn(0x00a0, 0x024f, 1, 30, 0.5));

        var scripts = new (string, int, int)[]
        {
            ("latin-extended-additional", 0x1e00, 0x1eff), ("ipa", 0x0250, 0x02ff), ("combining", 0x0300, 0x036f), ("greek", 0x0370, 0x03ff),
            ("cyrillic", 0x0400, 0x04ff), ("hebrew", 0x0590, 0x05ff), ("arabic", 0x0600, 0x06ff), ("devanagari", 0x0900, 0x097f),
            ("thai", 0x0e00, 0x0e7f), ("punctuation-symbols", 0x2000, 0x2bff), ("cjk", 0x4e00, 0x9fff), ("hangul", 0xac00, 0xd7a3),
            ("fullwidth", 0xff00, 0xffef), ("bmp", 0x0000, 0xffff),
        };
        foreach (var (name, first, last) in scripts)
        {
            for (int i = 0; i < 150; i++) Add(name, RandomIn(first, last, 1, 16, 0.3));
        }

        // Not reproduced: kana, an iteration mark, conjoining jamo, private use, a supplementary character and a lone
        // surrogate, halfwidth katakana.
        foreach (string s in new[]
        {
            "/" + S(0x3042), S(0x30a2, 0x30c3, 0x30d7), "/" + S(0x30da, 0x30fc, 0x30b8), S(0x4eba, 0x3005), S(0x1100, 0x1161), "/" + S(0xe000),
            "/" + S(0xd83d, 0xde00), S(0xd83d), S(0xff71, 0xff72),
        })
        {
            Add("not-reproduced", s);
        }
        return list;
    }
}
