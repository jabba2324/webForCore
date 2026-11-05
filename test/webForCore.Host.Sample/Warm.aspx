<%@ Page Language="C#" %><%
    // An initialization page (#193): takes ?sleep= milliseconds, and records that it ran, with its Host header, user agent
    // and client address, for State.aspx.
    int sleep;
    if (int.TryParse(Request.QueryString["sleep"], out sleep)) System.Threading.Thread.Sleep(sleep);
    Application.Lock();
    Application["warm"] = (string)Application["warm"] + Request.QueryString["n"] + "|" + Request.Headers["Host"] + "|" + Request.UserAgent + "|" + Request.UserHostAddress + ",";
    Application.UnLock();
%>warmed
