<%@ Page Language="C#" %>
<%
    // Reads the body unbuffered, into the middle of a buffer (ReadEntityBody's offset).
    var stream = Request.GetBufferlessInputStream();
    var buffer = new byte[1 << 20];
    int total = 0, read;
    while ((read = stream.Read(buffer, total + 7 - total % 7, 4093)) > 0)
    {
        System.Buffer.BlockCopy(buffer, total + 7 - total % 7, buffer, total, read);
        total += read;
    }
    long sum = 0;
    for (int i = 0; i < total; i++) sum = (sum * 31 + buffer[i]) % 1000000007;
    Response.Write("length=" + total + ";sum=" + sum);
%>
