using System.Net;
using System.Text.RegularExpressions;
using Xunit;

namespace MvcGate
{
    // LINQ to SQL's Binary in MVC 5 (#142): a rowversion round trip, as an application on LINQ to SQL edits a row with
    // optimistic concurrency (test/webForCore.Mvc.Sample's NotesController, against SQL Server at 127.0.0.1,14333).
    // The edit form writes the row's Version as base64 (the default editor template's hidden input for [HiddenInput],
    // and Html.Hidden), the post binds it back to a Binary (LinqBinaryModelBinder, in ModelBinders.Binders by
    // default), and LINQ to SQL updates with it: the first post saves, a second post with the same, now stale, version
    // is a ChangeConflictException.
    [Collection(MvcSampleCollection.Name)]
    public class LinqBinaryTests
    {
        private readonly SampleServer _server;

        public LinqBinaryTests(SampleServer server)
        {
            _server = server;
        }

        [Fact]
        public async Task ARowversionRoundTripsThroughTheEditForm()
        {
            Assert.Equal("reset", await _server.Client.GetStringAsync("/Notes/Reset"));
            string form = await _server.Client.GetStringAsync("/Notes/Edit/1");

            string version = Regex.Match(form, "<input id=\"Version\" name=\"Version\" type=\"hidden\" value=\"([^\"]*)\" />").Groups[1].Value;
            string check = Regex.Match(form, "<input id=\"check\" name=\"check\" type=\"hidden\" value=\"([^\"]*)\" />").Groups[1].Value;
            // A rowversion is 8 bytes: 12 characters of base64.
            Assert.Equal(8, Convert.FromBase64String(version).Length);
            Assert.Equal(version, check);

            var post = new Dictionary<string, string> { ["Id"] = "1", ["Text"] = "second", ["Version"] = version, ["check"] = check };
            HttpResponseMessage saved = await _server.Client.PostAsync("/Notes/Edit", new FormUrlEncodedContent(post));
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            Assert.Equal("System.Data.Linq.Binary[8] check=True saved second version changed=True", await saved.Content.ReadAsStringAsync());

            post["Text"] = "third";
            HttpResponseMessage stale = await _server.Client.PostAsync("/Notes/Edit", new FormUrlEncodedContent(post));
            Assert.Equal("System.Data.Linq.Binary[8] check=True conflict: Row not found or changed.", await stale.Content.ReadAsStringAsync());

            Assert.Contains("value=\"second\"", await _server.Client.GetStringAsync("/Notes/Edit/1"));
        }
    }
}
