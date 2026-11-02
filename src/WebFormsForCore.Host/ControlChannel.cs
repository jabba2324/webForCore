using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace WebFormsForCore.Host;

// The host's control socket: workers say when they are ready and when their application restarts. A Unix socket in the
// host's private directory, and each message carries the token the host gave its workers; one line each way.
sealed class ControlChannel : IDisposable
{
	readonly string path;
	readonly byte[] token;
	readonly Func<string, int, string, bool> handle;
	readonly ILogger logger;
	readonly CancellationTokenSource stopping = new();
	Socket? listener;

	public ControlChannel(string path, string token, Func<string, int, string, bool> handle, ILogger logger)
	{
		this.path = path;
		this.token = Encoding.ASCII.GetBytes(token);
		this.handle = handle;
		this.logger = logger;
	}

	public void Start()
	{
		listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
		listener.Bind(new UnixDomainSocketEndPoint(path));
		listener.Listen(16);
		_ = AcceptAsync(listener);
	}

	async Task AcceptAsync(Socket listener)
	{
		while (!stopping.IsCancellationRequested)
		{
			Socket connection;
			try { connection = await listener.AcceptAsync(stopping.Token); }
			catch (Exception) when (stopping.IsCancellationRequested) { return; }
			catch (SocketException e)
			{
				logger.LogWarning(e, "The control socket failed to accept a connection.");
				continue;
			}
			_ = ServeAsync(connection);
		}
	}

	async Task ServeAsync(Socket connection)
	{
		using var stream = new NetworkStream(connection, ownsSocket: true);
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
		timeout.CancelAfter(TimeSpan.FromSeconds(10));
		try
		{
			using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
			var line = await reader.ReadLineAsync(timeout.Token);
			var answer = line != null && Handle(line) ? "ok\n" : "error\n";
			await stream.WriteAsync(Encoding.ASCII.GetBytes(answer), timeout.Token);
		}
		catch (Exception e) when (e is IOException || e is OperationCanceledException || e is SocketException)
		{
			// The worker went away, or took too long to send its message.
		}
	}

	// "<token> <kind> <id> [reason]"
	bool Handle(string line)
	{
		var parts = line.Split(' ', 4);
		if (parts.Length < 3 || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(parts[0]), token)
			|| !int.TryParse(parts[2], out var id))
		{
			logger.LogWarning("The control socket received a message without the workers' token, or not in the expected form.");
			return false;
		}
		return handle(parts[1], id, parts.Length > 3 ? parts[3] : "");
	}

	// Safe to call more than once (the pool is disposed as a service and as a hosted service). The token source isn't
	// disposed: connections still being served link to it, and it has no timer to release.
	public void Dispose()
	{
		stopping.Cancel();
		listener?.Dispose();
	}
}
