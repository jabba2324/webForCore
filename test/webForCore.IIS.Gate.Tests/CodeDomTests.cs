using System.Net;
using Xunit;

namespace IISGate
{
    // System.Web's C# and VB CodeDom providers, which generate and compile every page and App_Code. They take the
    // generator, the converters and member generation from System.CodeDom's public providers (#57); the strings are
    // what the providers gave when they created System.CodeDom's internal types by reflection.
    [Collection(IISSampleCollection.Name)]
    public class CodeDomTests
    {
        private readonly SampleServer _server;

        public CodeDomTests(SampleServer server)
        {
            _server = server;
        }

        [Fact]
        public async Task ProvidersGenerateAndConvertAsBefore()
        {
            var response = await _server.Client.GetAsync("/CodeDom.aspx");
            string body = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            Assert.Contains("cs member: public static int count;\n", body);
            Assert.Contains("cs member attributes: Internal\n", body);
            Assert.Contains("cs type attributes: Public\n", body);
            Assert.Contains("cs generator: Microsoft.CSharp.CSharpCodeGenerator, valid identifier: True\n", body);
            Assert.Contains("cs compiler: True\n", body);

            Assert.Contains("vb member: Public Shared count As Integer\n", body);
            Assert.Contains("vb member attributes: Friend\n", body);
            Assert.Contains("vb type attributes: Public\n", body);
            Assert.Contains("vb generator: Microsoft.VisualBasic.VBCodeGenerator, valid identifier: True\n", body);
            Assert.Contains("vb compiler: True\n", body);
        }
    }
}
