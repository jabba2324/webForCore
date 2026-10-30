using System.Net;
using System.Text;
using Xunit;

namespace IISGate
{
    // ASMX web services (System.Web.Services): the sample's services/Catalog.asmx, compiled from App_Code at run time.
    [Collection(IISSampleCollection.Name)]
    public class WebServiceTests
    {
        private const string Url = "/services/Catalog.asmx";
        private readonly SampleServer _server;

        public WebServiceTests(SampleServer server)
        {
            _server = server;
        }

        private static string Soap11(string body) =>
            "<?xml version=\"1.0\" encoding=\"utf-8\"?><soap:Envelope xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\"><soap:Body>" + body + "</soap:Body></soap:Envelope>";

        private async Task<HttpResponseMessage> PostSoap11(string action, string body)
        {
            var content = new StringContent(Soap11(body), Encoding.UTF8, "text/xml");
            var request = new HttpRequestMessage(HttpMethod.Post, Url) { Content = content };
            request.Headers.Add("SOAPAction", "\"http://example.com/catalog/" + action + "\"");
            return await _server.Client.SendAsync(request);
        }

        [Fact]
        public async Task Wsdl()
        {
            string wsdl = await _server.Client.GetStringAsync(Url + "?wsdl");

            Assert.Contains("<wsdl:definitions", wsdl);
            Assert.Contains("targetNamespace=\"http://example.com/catalog\"", wsdl);
            foreach (string operation in new[] { "Add", "GetItem", "Fail", "Visit" })
            {
                Assert.Contains("<wsdl:operation name=\"" + operation + "\">", wsdl);
            }
            Assert.Contains("soap12:binding", wsdl); // SOAP 1.2 binding too
        }

        [Fact]
        public async Task Soap11Call()
        {
            HttpResponseMessage response = await PostSoap11("Add", "<Add xmlns=\"http://example.com/catalog\"><a>2</a><b>3</b></Add>");
            string body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/xml", response.Content.Headers.ContentType.MediaType);
            Assert.Contains("<AddResult>5</AddResult>", body);
        }

        [Fact]
        public async Task Soap12CallWithComplexType()
        {
            const string envelope = "<?xml version=\"1.0\" encoding=\"utf-8\"?><soap12:Envelope xmlns:soap12=\"http://www.w3.org/2003/05/soap-envelope\"><soap12:Body>"
                + "<GetItem xmlns=\"http://example.com/catalog\"><id>7</id></GetItem></soap12:Body></soap12:Envelope>";
            HttpResponseMessage response = await _server.Client.PostAsync(Url, new StringContent(envelope, Encoding.UTF8, "application/soap+xml"));
            string body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/soap+xml", response.Content.Headers.ContentType.MediaType);
            Assert.Contains("<Id>7</Id><Name>Item 7</Name><Price>9.5</Price><Tags><string>new</string><string>sale</string></Tags>", body);
        }

        [Fact]
        public async Task ExceptionsBecomeSoapFaults()
        {
            HttpResponseMessage response = await PostSoap11("Fail", "<Fail xmlns=\"http://example.com/catalog\" />");
            string body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Contains("<soap:Fault>", body);
            Assert.Contains("<faultcode>soap:Server</faultcode>", body);
            Assert.Contains("Out of stock", body);
        }

        [Fact]
        public async Task HttpPostFromLocalhost()
        {
            // HttpPostLocalhost is on by default (machine.config's <webServices><protocols>).
            HttpResponseMessage response = await _server.Client.PostAsync(Url + "/Add",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["a"] = "4", ["b"] = "5" }));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("<int xmlns=\"http://example.com/catalog\">9</int>", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task HelpPage()
        {
            // A browser opening the .asmx gets the help page (DefaultWsdlHelpGenerator.aspx, webForCore's own).
            string page = await _server.Client.GetStringAsync(Url);

            Assert.Contains("<title>Catalog Web Service</title>", page);
            Assert.Contains("Catalog.asmx?op=GetItem", page);
            Assert.Contains("Catalog.asmx?WSDL", page);

            // An operation's page has a form that invokes it with HTTP POST, for requests from the local machine.
            string op = await _server.Client.GetStringAsync(Url + "?op=Add");

            Assert.Contains("<h2>Add</h2>", op);
            Assert.Contains("action=\"/services/Catalog.asmx/Add\"", op);
            Assert.Contains("name=\"a\"", op);
            Assert.Contains("name=\"b\"", op);
        }

        [Fact]
        public async Task ScriptServiceAnswersJson()
        {
            // [ScriptService]: ASP.NET AJAX calls the method with JSON and gets {"d": ...} (System.Web.Extensions).
            HttpResponseMessage response = await _server.Client.PostAsync(Url + "/GetItem",
                new StringContent("{\"id\":3}", Encoding.UTF8, "application/json"));
            string body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType.MediaType);
            Assert.Equal("{\"d\":{\"__type\":\"IISSample.Item\",\"Id\":3,\"Name\":\"Item 3\",\"Price\":9.5,\"Tags\":[\"new\",\"sale\"]}}", body);
        }
    }
}
