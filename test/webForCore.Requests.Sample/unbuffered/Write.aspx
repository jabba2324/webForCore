<%@ Page Language="C#" %>
<%-- #184: Response.Write(string) with BufferOutput = false (it threw in HttpResponseBufferElement.Recycle and the response never ended). --%>
<% Response.ContentType = "text/plain"; Response.BufferOutput = false; Response.Write("hello"); Response.Write(" world\n"); %>
