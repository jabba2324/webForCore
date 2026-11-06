using Microsoft.AspNetCore.Http.Features;

namespace WebFormsForCore.Host;

// #214: whether a request can be sent again to the next worker when the one it went to didn't take it (it was shutting
// down, or its process ended before reading the request). On IIS such a request waits in http.sys's queue for the next
// worker process, body and all. Here the host must still have the body:
// - a body of up to UploadReadAheadSize bytes is kept in memory as it goes to the worker, and sent again from the start;
// - a larger one, or one of unknown length, is sent with "Expect: 100-continue", so that the worker's Kestrel asks for
//   it only when the application reads it (and the host's handler waits for that, Worker.Invoker). A worker that turns
//   the request away hasn't read it, so the body is still the client's, unread, for the next worker. Once a worker has
//   started reading it, the request is that worker's.
// A request without a body can always be sent again.
sealed class RequestBody
{
	// IIS's <serverRuntime uploadReadAheadSize> (49,152 bytes): what IIS reads of a body before the application asks for
	// it. SocketsHttpHandler sends a body of up to 1,024 bytes even when the answer to "Expect: 100-continue" is an error,
	// so the limit must be above that.
	public const int UploadReadAheadSize = 49152;

	readonly bool none;
	readonly UnreadStream? unread;
	readonly HttpRequest? buffered;

	RequestBody(bool none, UnreadStream? unread, HttpRequest? buffered)
	{
		this.none = none;
		this.unread = unread;
		this.buffered = buffered;
	}

	public static RequestBody For(HttpContext context)
	{
		var request = context.Request;
		if (context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == false) return new RequestBody(true, null, null);
		if (request.ContentLength is long length && length <= UploadReadAheadSize)
		{
			request.EnableBuffering(UploadReadAheadSize);
			return new RequestBody(false, null, request);
		}
		var unread = new UnreadStream(request.Body);
		request.Body = unread;
		return new RequestBody(false, unread, null);
	}

	// Whether the worker is asked to say when it wants the body ("Expect: 100-continue").
	public bool ExpectContinue => unread != null;

	// Whether the request can be sent again, with all of its body.
	public bool CanSendAgain => unread == null || !unread.Started;

	// Whether no worker can have read any of the body: there is none, or it is still unread. (A body kept in memory goes
	// to the worker at once, whether or not its application reads it.)
	public bool Unread => none || unread is { Started: false };

	// Before the request is sent again.
	public void Rewind()
	{
		if (buffered != null) buffered.Body.Position = 0;
	}

	// The client's body, recording whether anything has read from it.
	sealed class UnreadStream(Stream inner) : Stream
	{
		volatile bool started;
		public bool Started => started;

		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();
		public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
		public override void Flush() { }
		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

		public override int Read(byte[] buffer, int offset, int count)
		{
			started = true;
			return inner.Read(buffer, offset, count);
		}

		public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
		{
			started = true;
			return inner.ReadAsync(buffer, offset, count, cancellationToken);
		}

		public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
		{
			started = true;
			return inner.ReadAsync(buffer, cancellationToken);
		}

		public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
		{
			started = true;
			return inner.CopyToAsync(destination, bufferSize, cancellationToken);
		}
	}
}
