using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LinqScenarios;
using Xunit;

namespace IISGate
{
    // LinqDataSource (System.Web.Extensions) over LINQ to SQL (#142), on test/webForCore.Requests.Sample's linq/ pages:
    // a GridView with paging, sorting, edit, update and delete, a DetailsView that inserts, Where with a parameter,
    // OrderBy, AutoGenerateWhereClause, a Select projection, and Selecting, Updating, Updated, Inserted and Deleted
    // handlers; and Dynamic Data's LINQ to SQL model provider (MetaModel.RegisterContext with the DataContext); against
    // SQL Server (127.0.0.1,14333). test/webForCore.Linq.Scenarios sends the requests as a browser
    // would, postbacks included; the responses are written to linq-responses.json, which CI's iis-compare job compares
    // with the same scenarios on IIS 10 and .NET Framework 4.8.
    [Collection(RequestsSampleCollection.Name)]
    public class LinqDataSourceTests
    {
        private readonly RequestsSampleServer _server;

        public LinqDataSourceTests(RequestsSampleServer server)
        {
            _server = server;
        }

        [Fact]
        public async Task LinqDataSourceSelectsSortsPagesUpdatesInsertsAndDeletes()
        {
            var results = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
            try
            {
                await Scenarios.RunAsync(_server.Client, results);
            }
            finally
            {
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "linq-responses.json"), Scenarios.ToJson(results));
            }
            string Log(string key) => Regex.Match(Body(results, key), "<span id=\"Log\">(.*?)</span>").Groups[1].Value;

            Assert.Equal("reset 7", Body(results, "01 reset").Trim());
            // Where="UnitPrice >= @MinPrice" OrderBy="ProductName", paged by the data source (AutoPage): 3 of 7 rows.
            Assert.Equal(new[] { "3|Aniseed Syrup|2|10.00", "1|Chai|1|18.00", "2|Chang|1|19.00" }, Rows(results, "02 products"));
            Assert.Equal("selecting sort= start=0 max=3 count=True min=0;selected total=7;", Log("02 products"));
            Assert.Equal(new[] { "4|Ikura|3|31.00", "5|Konbu|3|6.00", "7|Pavlova|2|17.45" }, Rows(results, "03 page 2"));
            Assert.Equal("selecting sort= start=3 max=3 count=True min=0;selected total=7;", Log("03 page 2"));
            // Sorting by the data source (AutoSort), then the other way.
            Assert.Equal(new[] { "5|Konbu|3|6.00", "3|Aniseed Syrup|2|10.00", "7|Pavlova|2|17.45" }, Rows(results, "04 sort by price"));
            Assert.Equal(new[] { "4|Ikura|3|31.00", "6|Tofu|2|23.25", "2|Chang|1|19.00" }, Rows(results, "05 sort by price descending"));
            Assert.StartsWith("selecting sort=UnitPrice DESC start=0", Log("05 sort by price descending"));
            // The Where parameter from the query string.
            Assert.Equal(new[] { "4|Ikura|3|31.00", "6|Tofu|2|23.25" }, Rows(results, "06 where min=20"));
            Assert.Equal("selecting sort= start=0 max=3 count=True min=20;selected total=2;", Log("06 where min=20"));

            // Edit and update: the original object (with its rowversion, a Binary, from view state) and the new one.
            Assert.Contains("<input name=\"Grid$ctl03$ctl04\" type=\"text\" value=\"18.0000\" title=\"Price\" />", Body(results, "08 edit chai"));
            Assert.Equal("updating Chai -&gt; Chai Tea 18.50 version=True;updated Chai Tea;selecting sort= start=0 max=3 count=True min=0;selected total=7;", Log("09 update chai"));
            Assert.Contains("1|Chai Tea|1|18.50", Rows(results, "09 update chai"));
            // The row changed after it was read: LINQ to SQL's rowversion check fails, the Updated handler sees why.
            Assert.Equal("touched Chang 19.25", Body(results, "11 touch chang").Trim());
            Assert.StartsWith("updating Chang -&gt; Chang 20.00 version=True;update failed: System.Data.Linq.ChangeConflictException: Row not found or changed.;", Log("12 update chang"));
            Assert.Contains("2|Chang|1|19.25", Rows(results, "12 update chang"));

            // Delete, insert (the GridView isn't bound again after the DetailsView's insert: LinqDataSource raises no
            // DataSourceViewChanged for it), and the new row on the last page.
            Assert.StartsWith("deleted Aniseed Syrup;", Log("14 delete aniseed syrup"));
            Assert.Equal(new[] { "1|Chai Tea|1|18.50", "2|Chang|1|19.25", "4|Ikura|3|31.00" }, Rows(results, "14 delete aniseed syrup"));
            Assert.Equal("inserted Uni id=8 version=True;", Log("15 insert"));
            Assert.Equal(new[] { "8|Uni|3|12.50" }, Rows(results, "17 page 3"));

            // AutoGenerateWhereClause (CategoryID = @CategoryID, left out without a value), Select="new (...)" and
            // OrderBy="UnitPrice desc"; a Selecting handler's e.Result.
            Assert.Equal(new[] { "Ikura|31.0000", "Uni|12.5000", "Konbu|6.0000" }, Rows(results, "18 category 3"));
            Assert.Equal(7, Rows(results, "19 no category").Length);
            Assert.Equal(new[] { "Pavlova|17.4500", "Konbu|6.0000" }, Rows(results, "20 selecting result"));

            // Dynamic Data's LINQ to SQL model provider.
            string dynamicData = Body(results, "21 dynamic data");
            Assert.Contains("Categories (Categories, LinqCategory, Categories): CategoryID Int32 key generated required hidden; CategoryName String required max=50;", dynamicData);
            Assert.Contains("Products (Products, LinqProduct, Products): ProductID Int32 key generated required hidden; ProductName String required max=50; CategoryID Int32 required; UnitPrice Decimal required; Discontinued Boolean required; Version Binary generated required hidden;", dynamicData);
            Assert.Contains("query: Chai Tea,Chang", dynamicData);
        }

        private static string Body(SortedDictionary<string, JsonObject> results, string key) => (string)results["linq " + key]["body"];

        // The GridView's data rows: each cell's text or text box value, without the command and check box columns.
        private static string[] Rows(SortedDictionary<string, JsonObject> results, string key)
        {
            string grid = Regex.Match(Body(results, key), "id=\"Grid\">([\\s\\S]*?)</table>\\s*</div>").Groups[1].Value;
            return Regex.Matches(grid, "<tr>([\\s\\S]*?)</tr>")
                .Select(row => Regex.Matches(row.Groups[1].Value, "<td[^>]*>([\\s\\S]*?)</td>")
                    .Select(cell => Regex.Replace(Regex.Replace(cell.Groups[1].Value, "<input[^>]*type=\"text\"[^>]*value=\"([^\"]*)\"[^>]*>", "$1"), "<[^>]+>", "").Trim())
                    .Where(text => text != "" && !text.StartsWith("Edit&nbsp;", StringComparison.Ordinal) && !text.StartsWith("Update&nbsp;", StringComparison.Ordinal))
                    .ToArray())
                .Where(cells => cells.Length > 1 && !cells.All(c => Regex.IsMatch(c, "^\\d+$") && c.Length == 1))
                .Select(cells => string.Join("|", cells))
                .ToArray();
        }
    }
}
