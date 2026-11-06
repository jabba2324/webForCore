using System.Net;
using System.Text;
using System.Web.Hosting;
using Xunit;

namespace IISGate
{
    // #187: Response.TransmitFile and WriteFile send a file's range through the worker request
    // (AspNetCoreWorkerRequest.SendResponseFromFile, FileRange.Copy). As .NET Framework's HttpFileResponseElement reads a
    // file (System.Web/HttpWriter.cs, GetBytes), it reads until the range is sent or Read returns 0: Stream.Read may
    // return fewer bytes than asked for, and a file that became shorter while it is sent ends the copy instead of
    // looping. FileRange.cs is compiled into this assembly too (see the project file).
    public class FileRangeTests
    {
        private static byte[] Data(int length)
        {
            var data = new byte[length];
            for (int i = 0; i < length; i++) data[i] = (byte)('a' + i % 26);
            return data;
        }

        [Theory]
        [InlineData(1000, 7)]
        [InlineData(60000, 4096)]
        [InlineData(FileRange.MaxChunkLength, 1000)]
        [InlineData(200000, 1000)]
        [InlineData(200000, 50000)]
        public void ShortReadsAreReadUntilTheRangeIsSent(int length, int maxRead)
        {
            byte[] data = Data(length + 10);
            var source = new ShortReadStream(data, maxRead);
            source.Position = 5;
            var destination = new MemoryStream();
            long copied = FileRange.Copy(source, destination, length, null);
            Assert.Equal(length, copied);
            Assert.Equal(data.AsSpan(5, length).ToArray(), destination.ToArray());
        }

        // A file that is shorter than the range when it is read (truncated after HttpResponse checked the range): what is
        // there is sent, and the copy ends.
        [Theory]
        [InlineData(1000, 400)]
        [InlineData(200000, 70000)]
        [InlineData(200000, 0)]
        public void AFileShorterThanTheRangeEndsTheCopy(int length, int available)
        {
            byte[] data = Data(available);
            var destination = new MemoryStream();
            var copy = Task.Run(() => FileRange.Copy(new ShortReadStream(data, 30000), destination, length, null));
            Assert.True(copy.Wait(TimeSpan.FromSeconds(10)), "The copy didn't end.");
            Assert.Equal(available, copy.Result);
            Assert.Equal(data, destination.ToArray());
        }

        // The response is flushed between the chunks of a range longer than one chunk, not after the last.
        [Fact]
        public void TheResponseIsFlushedBetweenChunks()
        {
            int flushes = 0;
            var destination = new MemoryStream();
            FileRange.Copy(new MemoryStream(Data(200000)), destination, 200000, () => flushes++);
            Assert.Equal(200000, destination.Length);
            Assert.Equal(3, flushes);
            flushes = 0;
            FileRange.Copy(new MemoryStream(Data(1000)), new MemoryStream(), 1000, () => flushes++);
            Assert.Equal(0, flushes);
        }

        // Returns at most maxRead bytes from each Read, as a network share or a pipe may; its Length is the data's.
        private sealed class ShortReadStream : MemoryStream
        {
            private readonly int _maxRead;

            public ShortReadStream(byte[] data, int maxRead) : base(data, false)
            {
                _maxRead = maxRead;
            }

            public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(count, _maxRead));

            public override int Read(Span<byte> buffer) => base.Read(buffer.Slice(0, Math.Min(buffer.Length, _maxRead)));
        }
    }

    // #187 on a running application: test/webForCore.Requests.Sample's files/Send.ashx sends files/Large.dat (140,000
    // bytes, three of the worker request's chunks) with TransmitFile and WriteFile, whole and in ranges. Recorded for
    // CI's iis-compare job, which sends the same requests to IIS 10.
    [Collection(RequestsSampleCollection.Name)]
    public class FileSendingTests
    {
        private readonly RequestsSampleServer _server;

        public FileSendingTests(RequestsSampleServer server)
        {
            _server = server;
        }

        private static byte[] Large()
        {
            var data = new StringBuilder();
            for (int i = 0; i < 4375; i++) data.Append(i.ToString("D6")).Append(" abcdefghijklmnopqrstuvwx\n");
            return Encoding.ASCII.GetBytes(data.ToString());
        }

        [Theory]
        [InlineData("transmit", 0, -1)]
        [InlineData("transmit", 0, 140000)]
        [InlineData("transmit", 1000, 70000)]
        [InlineData("transmit", 65536, 65536)]
        [InlineData("transmit", 3, 139990)]
        [InlineData("transmit", 139990, -1)]
        [InlineData("transmit", 100, 10)]
        [InlineData("writefile", 0, 140000)]
        [InlineData("writefile", 1000, 70000)]
        [InlineData("writefile", 65535, 74465)]
        [InlineData("writefile", 100, 10)]
        [InlineData("writefile-all", 0, -1)]
        public async Task TheRangeIsSent(string method, long offset, long length)
        {
            byte[] large = Large();
            Assert.Equal(140000, large.Length);
            var (response, body) = await Get($"/files/Send.ashx?m={method}&offset={offset}&length={length}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            int count = length == -1 ? large.Length - (int)offset : (int)length;
            Assert.Equal(count, response.Content.Headers.ContentLength);
            Assert.Equal(Encoding.ASCII.GetString(large, (int)offset, count), body);
        }

        // A range the file doesn't have is HttpResponse's exception before anything is sent, as on .NET Framework.
        [Theory]
        [InlineData("transmit", 0, 140001, "System.ArgumentException (length)")]
        [InlineData("transmit", 140001, -1, "System.ArgumentException (offset)")]
        [InlineData("transmit", 139000, 1001, "System.ArgumentException (length)")]
        [InlineData("writefile", 0, 140001, "System.Web.HttpException")]
        [InlineData("writefile", 139000, 1001, "System.Web.HttpException")]
        public async Task ARangeTheFileDoesntHaveIsAnException(string method, long offset, long length, string exception)
        {
            var (response, body) = await Get($"/files/Send.ashx?m={method}&offset={offset}&length={length}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(exception + "\n", body);
        }

        private async Task<(HttpResponseMessage Response, string Body)> Get(string path)
        {
            var response = await _server.Client.GetAsync(path);
            var body = await response.Content.ReadAsStringAsync();
            RequestHandlingTests.Record("GET " + path, response, body);
            return (response, body);
        }
    }
}
