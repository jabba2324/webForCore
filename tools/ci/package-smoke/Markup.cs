using System.Web;

// Compiled into the application's assembly, against the packages' System.Web. HttpUtility and IHtmlString are in the
// shared framework's System.Web.HttpUtility.dll too, which the packages' build targets must drop (issue #7: CS0433).
public static class Markup
{
    public static IHtmlString Bold(string text) => new HtmlString("<b>" + HttpUtility.HtmlEncode(text) + "</b>");
}
