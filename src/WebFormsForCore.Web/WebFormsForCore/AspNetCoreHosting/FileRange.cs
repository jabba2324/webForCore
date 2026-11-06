#if NETCOREAPP

using System;
using System.Buffers;
using System.IO;

namespace System.Web.Hosting
{
	// webForCore: the copy of a file's range to the response, for AspNetCoreWorkerRequest.SendResponseFromFile
	// (Response.TransmitFile and WriteFile). Self-contained, so that IIS.Gate.Tests compiles it too and tests it with
	// streams a file system can't easily produce (#187).
	internal static class FileRange
	{
		internal const int MaxChunkLength = 64 * 1024;

		// Copies length bytes from source's position to destination, in chunks of at most MaxChunkLength, and returns the
		// number of bytes copied. Stream.Read may return fewer bytes than asked for, so it reads until the range is copied
		// or Read returns 0 (the file became shorter after its length was checked), as .NET Framework's
		// HttpFileResponseElement.GetBytes reads a file (System.Web/HttpWriter.cs). flush is called between the chunks
		// of a range longer than one chunk, so that the response doesn't hold the whole file.
		internal static long Copy(Stream source, Stream destination, long length, Action flush)
		{
			var chunk = ArrayPool<byte>.Shared.Rent((int)Math.Min(length, MaxChunkLength));
			try
			{
				long copied = 0;
				while (copied < length)
				{
					int filled = 0;
					int chunkLength = (int)Math.Min(length - copied, MaxChunkLength);
					while (filled < chunkLength)
					{
						int read = source.Read(chunk, filled, chunkLength - filled);
						if (read == 0)
						{
							break;
						}
						filled += read;
					}
					if (filled > 0)
					{
						destination.Write(chunk, 0, filled);
						copied += filled;
					}
					if (filled < chunkLength)
					{
						break;
					}
					if (copied < length && length > MaxChunkLength)
					{
						flush?.Invoke();
					}
				}
				return copied;
			}
			finally
			{
				ArrayPool<byte>.Shared.Return(chunk);
			}
		}
	}
}

#endif
