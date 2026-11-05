<%@ Page Language="C#" %>
<%-- #184: Bonobo Git Server's smart HTTP: Response.Write, then BufferOutput = false, then copies to Response.OutputStream, then Write again. --%>
<%
    Response.ContentType = "text/plain";
    Response.Write("001e# service=git-upload-pack\n");
    Response.BufferOutput = false;
    var data = new byte[10000];
    for (int i = 0; i < data.Length; i++) data[i] = (byte)(i % 64 == 63 ? '\n' : 'a' + i % 26);
    new System.IO.MemoryStream(data).CopyTo(Response.OutputStream, 4096);
    Response.Write("|end\n");
%>
