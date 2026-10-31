using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace IISGate
{
    // An SMTP server on 127.0.0.1 for the sample's <system.net><mailSettings>: accepts every message and keeps it.
    public sealed class FakeSmtpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly BlockingCollection<string> _messages = new BlockingCollection<string>();

        public FakeSmtpServer(int port)
        {
            _listener = new TcpListener(IPAddress.Loopback, port);
            _listener.Start();
            _ = AcceptAsync();
        }

        // The next message received (envelope lines, then the DATA), or null after the timeout.
        public string NextMessage(TimeSpan timeout) => _messages.TryTake(out string message, timeout) ? message : null;

        private async Task AcceptAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(_stop.Token); }
                catch (OperationCanceledException) { return; }
                catch (ObjectDisposedException) { return; }
                _ = Task.Run(() => Serve(client));
            }
        }

        private void Serve(TcpClient client)
        {
            using (client)
            using (var stream = client.GetStream())
            using (var reader = new StreamReader(stream, Encoding.ASCII))
            using (var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true })
            {
                var message = new StringBuilder();
                writer.WriteLine("220 gate ESMTP");
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string command = line.Length >= 4 ? line.Substring(0, 4).ToUpperInvariant() : line.ToUpperInvariant();
                    switch (command)
                    {
                        case "EHLO":
                            writer.WriteLine("250-gate");
                            writer.WriteLine("250 OK");
                            break;
                        case "MAIL":
                        case "RCPT":
                            message.Append(line).Append("\r\n");
                            writer.WriteLine("250 OK");
                            break;
                        case "DATA":
                            writer.WriteLine("354 End data with <CR><LF>.<CR><LF>");
                            while ((line = reader.ReadLine()) != null && line != ".")
                                message.Append(line).Append("\r\n");
                            _messages.Add(message.ToString());
                            message.Clear();
                            writer.WriteLine("250 OK");
                            break;
                        case "QUIT":
                            writer.WriteLine("221 Bye");
                            return;
                        default:
                            writer.WriteLine("250 OK");
                            break;
                    }
                }
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
        }
    }

    // An HTTP proxy on 127.0.0.1 for the sample's <system.net><defaultProxy>: answers every request itself with
    // "proxied:<path>" and keeps the request lines.
    public sealed class FakeHttpProxy : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();

        public ConcurrentQueue<string> RequestLines { get; } = new ConcurrentQueue<string>();

        public FakeHttpProxy(int port)
        {
            _listener = new TcpListener(IPAddress.Loopback, port);
            _listener.Start();
            _ = AcceptAsync();
        }

        private async Task AcceptAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(_stop.Token); }
                catch (OperationCanceledException) { return; }
                catch (ObjectDisposedException) { return; }
                _ = Task.Run(() => Serve(client));
            }
        }

        private void Serve(TcpClient client)
        {
            using (client)
            using (var stream = client.GetStream())
            using (var reader = new StreamReader(stream, Encoding.ASCII))
            {
                string requestLine = reader.ReadLine();
                if (requestLine == null)
                    return;
                RequestLines.Enqueue(requestLine);
                while (!string.IsNullOrEmpty(reader.ReadLine()))
                {
                }
                // GET http://host/path HTTP/1.1: a proxy request has the absolute URI.
                string target = requestLine.Split(' ')[1];
                string body = "proxied:" + (Uri.TryCreate(target, UriKind.Absolute, out Uri uri) ? uri.AbsolutePath : target);
                byte[] response = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n" + body);
                stream.Write(response, 0, response.Length);
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
        }
    }
}
