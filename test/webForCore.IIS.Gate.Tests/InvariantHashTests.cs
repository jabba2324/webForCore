using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Util;
using Xunit;
using Xunit.Abstractions;

namespace IISGate
{
    // #212: .NET Framework 4.8's StringComparer.InvariantCultureIgnoreCase hash, which System.Web uses for
    // __VIEWSTATEGENERATOR and the <machineKey> IsolateApps and IsolateByAppId modifiers. webForCore builds the string's
    // NLS sort key from a table of weights read from .NET Framework (src/WebFormsForCore.Web/FrameworkData/
    // InvariantIgnoreCaseWeights.txt) and hashes it as .NET Framework does (InvariantIgnoreCaseHash.cs, compiled in here
    // with the table). FrameworkData/invariant-hash-framework.tsv has .NET Framework's hashes of a set of strings, written
    // by tools/stringhash/FrameworkSortKeys on Windows (CI's invariant-hash-framework-data job also checks 300,000 random
    // strings of the table's characters there).
    [Collection(IISSampleCollection.Name)]
    public class InvariantHashTests
    {
        private readonly SampleServer _server;
        private readonly ITestOutputHelper _output;

        public InvariantHashTests(SampleServer server, ITestOutputHelper output)
        {
            _server = server;
            _output = output;
        }

        private static List<(string Group, string Value, int Hash)> FrameworkHashes()
        {
            var list = new List<(string, string, int)>();
            foreach (string line in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "FrameworkData", "invariant-hash-framework.tsv")))
            {
                if (line.StartsWith('#')) continue;
                string[] fields = line.Split('\t');
                var value = new StringBuilder();
                for (int i = 0; i < fields[1].Length; i += 4) value.Append((char)int.Parse(fields[1].AsSpan(i, 4), NumberStyles.HexNumber));
                list.Add((fields[0], value.ToString(), int.Parse(fields[2], NumberStyles.HexNumber)));
            }
            return list;
        }

        private static int FrameworkHash(string value) => FrameworkHashes().Single(e => e.Group == "fixed" && e.Value == value).Hash;

        // System.Web's own test vector (ApplicationManager's check that string hashes aren't randomized).
        [Fact]
        public void TheTestVectorHasFrameworksHash()
        {
            Assert.True(InvariantIgnoreCaseHash.TryGetFrameworkHashCode("The quick brown fox jumps over the lazy dog.", out int hash));
            Assert.Equal(0x703e662e, hash);
            Assert.True(InvariantIgnoreCaseHash.TryGetFrameworkHashCode("", out hash));
            Assert.Equal(0, hash);
        }

        // The issue's page: linq/Products.aspx of test/webForCore.Requests.Sample has __VIEWSTATEGENERATOR D1602CB0 on IIS
        // 10 (PR #206's comparison), the hash of its directory plus that of its class name.
        [Fact]
        public void TheLinqProductsPageHasIISsViewStateGenerator()
        {
            Assert.True(InvariantIgnoreCaseHash.TryGetFrameworkHashCode("/linq", out int directory));
            Assert.True(InvariantIgnoreCaseHash.TryGetFrameworkHashCode("linq_products_aspx", out int className));
            Assert.Equal("D1602CB0", ((uint)(directory + className)).ToString("X8", CultureInfo.InvariantCulture));
        }

        // Every string of .NET Framework's set whose characters the table has gets .NET Framework's hash; all the ASCII and
        // Latin ones (paths, class names, application IDs) are among them, and the ones with characters the table leaves
        // out (kana, conjoining jamo, private use, surrogates) aren't hashed.
        [Fact]
        public void StringsGetFrameworksHash()
        {
            var problems = new List<string>();
            foreach (var group in FrameworkHashes().GroupBy(e => e.Group))
            {
                int reproduced = 0;
                foreach (var (_, value, framework) in group)
                {
                    bool ours = InvariantIgnoreCaseHash.TryGetFrameworkHashCode(value, out int hash);
                    if (ours && hash != framework) problems.Add($"{group.Key} {Hex(value)}: .NET Framework {framework:x8}, webForCore {hash:x8}");
                    if (ours) reproduced++;
                    bool expected = group.Key != "not-reproduced";
                    if (!ours && (group.Key == "fixed" || group.Key.StartsWith("ascii-") || group.Key == "latin")) problems.Add($"{group.Key} {Hex(value)} isn't reproduced");
                    if (ours && !expected) problems.Add($"{group.Key} {Hex(value)} is reproduced");
                }
                _output.WriteLine($"{group.Key}: {reproduced} of {group.Count()} reproduced");
            }
            Assert.True(problems.Count == 0, string.Join("\n", problems.Take(50)));
        }

        // A page's __VIEWSTATEGENERATOR is .NET Framework's for the same directory and class name.
        [Fact]
        public async Task APagesViewStateGeneratorIsFrameworks()
        {
            var response = await _server.Client.GetAsync("/regressions/ViewStateGenerator.aspx");
            string body = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Match generator = Regex.Match(body, "id=\"__VIEWSTATEGENERATOR\" value=\"([^\"]*)\"");
            Assert.True(generator.Success, body);
            uint expected = (uint)(FrameworkHash("/regressions") + FrameworkHash("regressions_viewstategenerator_aspx"));
            Assert.Equal(expected.ToString("X8", CultureInfo.InvariantCulture), generator.Groups[1].Value);
        }

        private static string Hex(string value) => string.Concat(value.Select(c => ((int)c).ToString("x4")));
    }
}
