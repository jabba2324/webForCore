using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace WebFormsForCore.Host;

enum WorkerState { Starting, Ready, Retiring }

// One worker process: "dotnet <application>" serving on its own Unix socket, with its stdin a pipe from the host (when
// that closes, the worker stops: it never outlives the host).
sealed class Worker : IDisposable
{
	public int Id { get; }
	public string SocketPath { get; }
	public WorkerState State { get; set; } = WorkerState.Starting;
	public DateTime Started { get; } = DateTime.UtcNow;
	public Process? Process { get; private set; }
	public HttpMessageInvoker Invoker { get; }
	int requests;

	public Worker(int id, string socketPath)
	{
		Id = id;
		SocketPath = socketPath;
		Invoker = new HttpMessageInvoker(new SocketsHttpHandler
		{
			// The request as the client sent it: no proxy, decompression, cookies, redirects or trace headers added.
			UseProxy = false,
			AllowAutoRedirect = false,
			AutomaticDecompression = DecompressionMethods.None,
			UseCookies = false,
			ActivityHeadersPropagator = DistributedContextPropagator.CreateNoOutputPropagator(),
			ConnectTimeout = TimeSpan.FromSeconds(15),
			ConnectCallback = async (_, cancellationToken) =>
			{
				var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
				try
				{
					await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), cancellationToken);
					return new NetworkStream(socket, ownsSocket: true);
				}
				catch
				{
					socket.Dispose();
					throw;
				}
			},
		}, disposeHandler: true);
	}

	public int ProcessId { get; private set; }
	// Completes when the process exits (taken when it starts: the Process object is disposed with the worker).
	public Task Exited { get; private set; } = Task.CompletedTask;
	public int ExitCode { get; private set; }

	public void Start(WorkerCommand command, string controlSocket, string token)
	{
		var start = new ProcessStartInfo(DotnetPath())
		{
			UseShellExecute = false,
			RedirectStandardInput = true,
		};
		start.ArgumentList.Add(command.Application);
		foreach (var argument in command.Arguments) start.ArgumentList.Add(argument);
		start.Environment[WorkerProtocol.SocketVariable] = SocketPath;
		start.Environment[WorkerProtocol.IdVariable] = Id.ToString(CultureInfo.InvariantCulture);
		start.Environment[WorkerProtocol.ControlVariable] = controlSocket;
		start.Environment[WorkerProtocol.TokenVariable] = token;
		// System.Web's hosting startup makes the application a worker (serving on the socket, with the host's connection
		// details) before its Program.cs runs, whatever that contains.
		var startups = Environment.GetEnvironmentVariable(WorkerProtocol.HostingStartupVariable);
		start.Environment[WorkerProtocol.HostingStartupVariable] = string.IsNullOrEmpty(startups) ? "System.Web" : startups + ";System.Web";
		var process = Process.Start(start) ?? throw new InvalidOperationException("The worker process didn't start.");
		Process = process;
		ProcessId = process.Id;
		Exited = WaitAsync(process);
	}

	async Task WaitAsync(Process process)
	{
		await process.WaitForExitAsync();
		ExitCode = process.ExitCode;
	}

	// The dotnet that runs the host runs the workers.
	static string DotnetPath()
	{
		var path = Environment.ProcessPath;
		if (path != null && Path.GetFileNameWithoutExtension(path).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) return path;
		var root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
		var dotnet = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
		return !string.IsNullOrEmpty(root) && File.Exists(Path.Combine(root, dotnet)) ? Path.Combine(root, dotnet) : dotnet;
	}

	public void BeginRequest() => Interlocked.Increment(ref requests);
	public void EndRequest() => Interlocked.Decrement(ref requests);
	public int Requests => Volatile.Read(ref requests);

	// Asks the worker to stop: its stdin closes, and it shuts down as on Ctrl+C.
	public void RequestStop()
	{
		try { Process?.StandardInput.Close(); }
		catch (Exception e) when (e is IOException || e is InvalidOperationException) { }
	}

	public void Kill()
	{
		try { Process?.Kill(entireProcessTree: true); }
		catch (Exception e) when (e is InvalidOperationException || e is System.ComponentModel.Win32Exception) { }
	}

	public void Dispose()
	{
		Invoker.Dispose();
		Process?.Dispose();
		try { File.Delete(SocketPath); }
		catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
	}
}
