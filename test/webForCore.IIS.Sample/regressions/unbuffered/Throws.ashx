<%@ WebHandler Language="C#" Class="UnbufferedThrowsHandler" %>
using System;
using System.IO;
using System.Text;
using System.Web;

// #184: something fails while an unbuffered response is being sent, once its first chunk has gone out (headers sent,
// Transfer-Encoding: chunked, which classic mode's HttpResponse writes itself).
//   ?mode=filter     a response filter throws; ASP.NET writes the error into the response and ends it.
//   ?mode=encoding   the response's encoding fails from then on, so the error page can't be written either (as the
//                    Recycle exception failed every flush): the response can't end normally, and the connection is
//                    aborted.
// Either way the client gets an end or an abort, not a response that never ends.
public class UnbufferedThrowsHandler : IHttpHandler
{
    public bool IsReusable { get { return false; } }

    public void ProcessRequest(HttpContext context)
    {
        var response = context.Response;
        response.ContentType = "text/plain";
        if (context.Request.QueryString["mode"] == "filter")
        {
            response.Filter = new ThrowingFilter(response.Filter);
        }
        else
        {
            response.ContentEncoding = new FailingEncoding();
        }
        response.BufferOutput = false;
        response.Write("first chunk");
        response.Write("second chunk");
    }

    sealed class ThrowingFilter : Stream
    {
        readonly Stream _inner;
        int _writes;
        public ThrowingFilter(Stream inner) { _inner = inner; }
        public override bool CanRead { get { return false; } }
        public override bool CanSeek { get { return false; } }
        public override bool CanWrite { get { return true; } }
        public override long Length { get { throw new NotSupportedException(); } }
        public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
        public override void Flush() { _inner.Flush(); }
        public override int Read(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
        public override void SetLength(long value) { throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (++_writes > 1) throw new IOException("the filter failed");
            _inner.Write(buffer, offset, count);
        }
    }

    // ASCII for the first string it encodes, then it throws.
    sealed class FailingEncoding : Encoding
    {
        int _encoded;
        public override string WebName { get { return "us-ascii"; } }
        public override string HeaderName { get { return "us-ascii"; } }
        public override string BodyName { get { return "us-ascii"; } }
        public override int GetByteCount(char[] chars, int index, int count) { return count; }
        public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex)
        {
            if (++_encoded > 1) throw new InvalidOperationException("the encoding failed");
            return Encoding.ASCII.GetBytes(chars, charIndex, charCount, bytes, byteIndex);
        }
        public override int GetCharCount(byte[] bytes, int index, int count) { return count; }
        public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex) { return Encoding.ASCII.GetChars(bytes, byteIndex, byteCount, chars, charIndex); }
        public override int GetMaxByteCount(int charCount) { return charCount + 1; }
        public override int GetMaxCharCount(int byteCount) { return byteCount; }
    }
}
