using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace LinqScenarios
{
    // A user of the linq/ pages, step by step: each GET or postback is sent, and its response recorded under a key in
    // tools/ci/iis_compare.cs's format (request, status, Content-Type, body). A postback sends the form back as a browser
    // does (every input with its value, checked check boxes, __EVENTTARGET and __EVENTARGUMENT from the link's
    // __doPostBack call). The recorded body is the page's form without what differs between servers by design: the
    // values of __VIEWSTATE and __EVENTVALIDATION (protected with each server's machine key) and WebResource.axd's
    // d and t parameters; __VIEWSTATEGENERATOR's value is recorded on its own, as "(__VIEWSTATEGENERATOR)" beside the
    // headers.
    public static class Scenarios
    {
        public static async Task<SortedDictionary<string, JsonObject>> RunAsync(HttpClient client)
        {
            var results = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
            await RunAsync(client, results);
            return results;
        }

        // Fills results as it goes, so that a failing step leaves the responses before it.
        public static async Task RunAsync(HttpClient client, SortedDictionary<string, JsonObject> results)
        {
            var run = new Run(client, results);

            await run.Get("01 reset", "/linq/Reset.aspx");
            Page page = await run.Get("02 products", "/linq/Products.aspx");
            page = await run.PostBack("03 page 2", page, "Grid", "Page$2");
            page = await run.PostBack("04 sort by price", page, "Grid", "Sort$UnitPrice");
            await run.PostBack("05 sort by price descending", page, "Grid", "Sort$UnitPrice");
            await run.Get("06 where min=20", "/linq/Products.aspx?min=20");

            // Edit and update the second row (Chai): its name and price.
            page = await run.Get("07 products", "/linq/Products.aspx");
            page = await run.PostBack("08 edit chai", page, "Grid", "Edit$1");
            page = await run.Click("09 update chai", page, "Update", fields =>
            {
                Replace(fields, "Grid$", "Chai", "Chai Tea");
                Replace(fields, "Grid$", "18.0000", "18.50");
            });

            // A concurrency conflict: Chang changes (and with it its rowversion) between the edit and the update.
            page = await run.PostBack("10 edit chang", page, "Grid", "Edit$2");
            await run.Get("11 touch chang", "/linq/Touch.aspx?id=2&price=19.25");
            await run.Click("12 update chang", page, "Update", fields => Replace(fields, "Grid$", "19.0000", "20.00"));

            // Delete the first row (Aniseed Syrup), and insert a product through the DetailsView.
            page = await run.Get("13 products", "/linq/Products.aspx");
            page = await run.PostBack("14 delete aniseed syrup", page, "Grid", "Delete$0");
            page = await run.Click("15 insert", page, "Insert", fields =>
            {
                string[] text = fields.Keys.Where(k => k.StartsWith("NewProduct$", StringComparison.Ordinal) && fields[k] == "").ToArray();
                fields[text[0]] = "Uni";
                fields[text[1]] = "3";
                fields[text[2]] = "12.5";
            });
            // The GridView doesn't rebind after the DetailsView's insert: LinqDataSource's insert doesn't raise
            // DataSourceViewChanged. The next request shows the new row, on the last page.
            page = await run.Get("16 products", "/linq/Products.aspx");
            await run.PostBack("17 page 3", page, "Grid", "Page$3");

            // AutoGenerateWhereClause, a projection and OrderBy ... desc; a Selecting handler's own query.
            await run.Get("18 category 3", "/linq/Categories.aspx?category=3");
            await run.Get("19 no category", "/linq/Categories.aspx");
            await run.Get("20 selecting result", "/linq/Categories.aspx?custom=1");

            // Dynamic Data's LINQ to SQL model provider (MetaModel.RegisterContext with the DataContext).
            await run.Get("21 dynamic data", "/linq/DynamicData.aspx");
        }

        public static string ToJson(SortedDictionary<string, JsonObject> results)
        {
            var all = new JsonObject();
            foreach (var (key, value) in results) all[key] = value.DeepClone();
            return all.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }

        // The form's fields whose name starts with the prefix and whose value is the old one get the new value.
        private static void Replace(Dictionary<string, string> fields, string prefix, string oldValue, string newValue)
        {
            string[] names = fields.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal) && fields[k] == oldValue).ToArray();
            if (names.Length != 1) throw new InvalidOperationException($"{names.Length} fields with the value '{oldValue}'");
            fields[names[0]] = newValue;
        }

        private sealed class Run
        {
            private readonly HttpClient _client;
            private readonly SortedDictionary<string, JsonObject> _results;

            public Run(HttpClient client, SortedDictionary<string, JsonObject> results)
            {
                _client = client;
                _results = results;
            }

            public Task<Page> Get(string key, string path) => Send(key, new HttpRequestMessage(HttpMethod.Get, path), null);

            // Clicks the page's only link with the text (a LinkButton: __doPostBack with its own name and no argument).
            public Task<Page> Click(string key, Page page, string text, Action<Dictionary<string, string>> edit = null)
            {
                var (target, argument) = page.Link(text);
                return PostBack(key, page, target, argument, edit);
            }

            // Posts back as the page's __doPostBack(target, argument) link does (GridView's paging, sorting and commands).
            public Task<Page> PostBack(string key, Page page, string target, string argument, Action<Dictionary<string, string>> edit = null)
            {
                string actualArgument = page.PostBackArgument(target, argument);
                var fields = page.Fields();
                fields["__EVENTTARGET"] = target;
                fields["__EVENTARGUMENT"] = actualArgument;
                edit?.Invoke(fields);
                var request = new HttpRequestMessage(HttpMethod.Post, page.Path)
                {
                    Content = new FormUrlEncodedContent(fields),
                };
                return Send(key, request, fields);
            }

            private async Task<Page> Send(string key, HttpRequestMessage request, Dictionary<string, string> form)
            {
                using HttpResponseMessage response = await _client.SendAsync(request);
                string body = await response.Content.ReadAsStringAsync();
                var headers = new JsonObject();
                if (response.Content.Headers.ContentType != null) headers["Content-Type"] = response.Content.Headers.ContentType.ToString();
                // The page's __VIEWSTATEGENERATOR, out of the body, under a name of its own: it differs from IIS's
                // (tools/ci/iis-known-differences-linq.json), and everything else in the form must not.
                Match generator = Regex.Match(body, "id=\"__VIEWSTATEGENERATOR\" value=\"([^\"]*)\"");
                if (generator.Success) headers["(__VIEWSTATEGENERATOR)"] = generator.Groups[1].Value;
                _results["linq " + key] = new JsonObject
                {
                    ["request"] = new JsonObject
                    {
                        ["method"] = request.Method.Method,
                        ["url"] = new Uri(_client.BaseAddress, request.RequestUri).AbsoluteUri,
                        ["form"] = form == null ? null : JsonSerializer.SerializeToNode(Visible(form)),
                    },
                    ["status"] = (int)response.StatusCode,
                    ["headers"] = headers,
                    ["body"] = Normalize(body),
                };
                if (response.StatusCode != HttpStatusCode.OK)
                    throw new InvalidOperationException($"{key}: {(int)response.StatusCode}\n{body}");
                return new Page(request.RequestUri.OriginalString, body);
            }

            private static SortedDictionary<string, string> Visible(Dictionary<string, string> form) =>
                new(form.Where(f => !f.Key.StartsWith("__VIEWSTATE", StringComparison.Ordinal) && f.Key != "__EVENTVALIDATION")
                    .ToDictionary(f => f.Key, f => f.Value), StringComparer.Ordinal);
        }

        // The form, or the whole body if it has none (Reset.aspx, Touch.aspx), without the server-specific values.
        public static string Normalize(string body)
        {
            Match form = Regex.Match(body, @"<form[\s\S]*</form>");
            string text = form.Success ? form.Value : body;
            text = Regex.Replace(text, @"(id=""__(VIEWSTATE|VIEWSTATEGENERATOR|EVENTVALIDATION)"" value="")[^""]*""", "$1(...)\"");
            text = Regex.Replace(text, @"(WebResource\.axd\?d=)[^""&]*(&amp;t=)\d+", "$1(...)$2(...)");
            return text;
        }
    }

    public sealed class Page
    {
        public Page(string path, string html)
        {
            Path = path;
            Html = html;
        }

        public string Path { get; }

        public string Html { get; }

        // The form's fields as a browser posts them: text and hidden inputs, checked check boxes and radio buttons.
        public Dictionary<string, string> Fields()
        {
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match input in Regex.Matches(Html, @"<input\b[^>]*>"))
            {
                string name = Attribute(input.Value, "name");
                if (name == null) continue;
                string type = (Attribute(input.Value, "type") ?? "text").ToLowerInvariant();
                if (type is "submit" or "button" or "image" or "reset" or "file") continue;
                if (type is "checkbox" or "radio" && Attribute(input.Value, "checked") == null) continue;
                fields[name] = WebUtility.HtmlDecode(Attribute(input.Value, "value") ?? (type is "checkbox" or "radio" ? "on" : ""));
            }
            return fields;
        }

        public string PostBackArgument(string target, string argument)
        {
            foreach (Match call in Regex.Matches(Html, @"__doPostBack\(&#39;([^&]*)&#39;,&#39;([^&]*)&#39;\)"))
            {
                if (call.Groups[1].Value == target && call.Groups[2].Value == argument)
                    return argument;
            }
            throw new InvalidOperationException($"No __doPostBack('{target}', '{argument}') on the page:\n{Html}");
        }

        // The __doPostBack call of the page's only link with the text.
        public (string Target, string Argument) Link(string text)
        {
            var links = Regex.Matches(Html, @"<a href=""javascript:__doPostBack\(&#39;([^&]*)&#39;,&#39;([^&]*)&#39;\)"">" + Regex.Escape(text) + "</a>");
            if (links.Count != 1) throw new InvalidOperationException($"{links.Count} links '{text}' on the page:\n{Html}");
            return (links[0].Groups[1].Value, links[0].Groups[2].Value);
        }

        private static string Attribute(string tag, string name)
        {
            Match m = Regex.Match(tag, @"\s" + name + @"(?:=""([^""]*)"")?(?=[\s/>])", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : null;
        }
    }
}
