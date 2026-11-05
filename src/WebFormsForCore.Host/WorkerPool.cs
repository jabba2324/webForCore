using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http.Features;
using Yarp.ReverseProxy.Forwarder;

namespace WebFormsForCore.Host;

// The workers, as IIS's Windows Process Activation Service kept an application pool's: one current worker serves the
// requests. When its application restarts (a Web.config, bin or App_Code change, HttpRuntime.UnloadAppDomain, a
// recycling limit), the worker tells the host and stops taking requests; the host starts the next worker, and requests
// wait for it while the old worker finishes the ones it has and exits. A worker that exits otherwise is replaced, up to
// rapid-fail protection's limit.
sealed class WorkerPool : IHostedLifecycleService, IDisposable
{
	readonly WorkerCommand command;
	readonly HostSettings settings;
	readonly IHttpForwarder forwarder;
	readonly ILogger logger;
	readonly ForwarderRequestConfig requestConfig;
	readonly string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
	readonly object sync = new();
	readonly List<Worker> retiring = new();
	readonly Queue<DateTime> failures = new();
	DirectoryInfo? runDirectory;
	ControlChannel? control;
	Worker? current;
	// Completes with the current worker once it is ready, or null when the worker it was for won't serve.
	TaskCompletionSource<Worker?> currentReady = NewReady();
	int lastId;
	bool shuttingDown;
	bool stopped; // rapid-fail protection stopped the pool

	public WorkerPool(WorkerCommand command, HostSettings settings, IHttpForwarder forwarder, ILogger<WorkerPool> logger)
	{
		this.command = command;
		this.settings = settings;
		this.forwarder = forwarder;
		this.logger = logger;
		requestConfig = new ForwarderRequestConfig
		{
			ActivityTimeout = settings.RequestIdleTimeout,
			Version = System.Net.HttpVersion.Version11,
			VersionPolicy = HttpVersionPolicy.RequestVersionExact,
		};
	}

	static TaskCompletionSource<Worker?> NewReady() => new(TaskCreationOptions.RunContinuationsAsynchronously);

	// A request a shutting-down worker didn't take is sent again after this delay, up to MaxRetries times (then the
	// worker's 503 goes to the client).
	static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);
	const int MaxRetries = 25;

	// A private directory for the sockets (on Unix, only this user can use it).
	public Task StartAsync(CancellationToken cancellationToken)
	{
		runDirectory = Directory.CreateTempSubdirectory("webforcore-");
		control = new ControlChannel(Path.Combine(runDirectory.FullName, "host.sock"), token, OnControlMessage, logger);
		control.Start();
		lock (sync) StartWorker("the host started");
		return Task.CompletedTask;
	}

	public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	// The host is stopping: Kestrel drains the forwarded requests first (HostOptions.ShutdownTimeout), then
	// StoppedAsync stops the workers.
	public Task StoppingAsync(CancellationToken cancellationToken)
	{
		lock (sync) shuttingDown = true;
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	public async Task StoppedAsync(CancellationToken cancellationToken)
	{
		List<Worker> workers;
		lock (sync)
		{
			workers = retiring.ToList();
			if (current != null) workers.Add(current);
			current = null;
			currentReady.TrySetResult(null);
		}
		foreach (var worker in workers) worker.RequestStop();
		await Task.WhenAll(workers.Select(worker => WaitForExitAsync(worker, settings.ShutdownTimeLimit)));
	}

	// Starts the next worker; the caller holds the lock.
	void StartWorker(string reason)
	{
		if (shuttingDown || stopped) return;
		var id = ++lastId;
		var worker = new Worker(id, Path.Combine(runDirectory!.FullName, $"worker-{id}.sock"));
		current = worker;
		if (currentReady.Task.IsCompleted) currentReady = NewReady();
		try
		{
			worker.Start(command, Path.Combine(runDirectory.FullName, "host.sock"), token);
		}
		catch (Exception e) when (e is InvalidOperationException || e is System.ComponentModel.Win32Exception)
		{
			logger.LogError(e, "Worker {Id} for {Application} didn't start.", id, command.Application);
			current = null;
			worker.Dispose();
			if (!RecordFailure()) currentReady.TrySetResult(null);
			return;
		}
		logger.LogInformation("Started worker {Id} (process {ProcessId}) for {Application}: {Reason}.", id, worker.ProcessId, command.Application, reason);
		_ = MonitorAsync(worker);
		_ = StartupLimitAsync(worker);
	}

	async Task MonitorAsync(Worker worker)
	{
		await worker.Exited;
		lock (sync) OnExited(worker);
	}

	async Task StartupLimitAsync(Worker worker)
	{
		await Task.Delay(settings.StartupTimeLimit);
		lock (sync)
		{
			if (worker != current || worker.State != WorkerState.Starting) return;
			logger.LogError("Worker {Id} (process {ProcessId}) didn't start serving within {Limit}: stopping it.", worker.Id, worker.ProcessId, settings.StartupTimeLimit);
		}
		worker.Kill(); // OnExited counts it as a failure and replaces it
	}

	// The caller holds the lock.
	void OnExited(Worker worker)
	{
		int exitCode = worker.ExitCode;
		if (retiring.Remove(worker) || worker != current)
		{
			logger.LogInformation("Worker {Id} (process {ProcessId}) exited ({ExitCode}).", worker.Id, worker.ProcessId, exitCode);
			worker.Dispose();
			return;
		}
		current = null;
		worker.Dispose();
		if (shuttingDown)
		{
			currentReady.TrySetResult(null);
			return;
		}
		logger.LogError("Worker {Id} (process {ProcessId}) exited unexpectedly ({ExitCode}){State}.", worker.Id, worker.ProcessId, exitCode,
			worker.State == WorkerState.Starting ? " while starting" : "");
		if (RecordFailure()) return;
		currentReady.TrySetResult(null);
		StartWorker($"worker {worker.Id} exited");
	}

	// Rapid-fail protection: whether this failure stops the pool. The caller holds the lock.
	bool RecordFailure()
	{
		if (!settings.RapidFailProtection) return false;
		var now = DateTime.UtcNow;
		failures.Enqueue(now);
		while (failures.Count > 0 && now - failures.Peek() > settings.RapidFailProtectionInterval) failures.Dequeue();
		if (failures.Count < settings.RapidFailProtectionMaxCrashes) return false;
		stopped = true;
		currentReady.TrySetResult(null);
		logger.LogCritical("Rapid-fail protection: {Count} worker failures within {Interval}. No more workers are started; requests get 503 until the host is restarted.",
			failures.Count, settings.RapidFailProtectionInterval);
		return true;
	}

	// A worker's control message.
	bool OnControlMessage(string kind, int id, string reason)
	{
		lock (sync)
		{
			var worker = current?.Id == id ? current : null;
			switch (kind)
			{
				case WorkerProtocol.Ready:
					if (worker == null || worker.State != WorkerState.Starting) return true; // retired meanwhile
					worker.State = WorkerState.Ready;
					logger.LogInformation("Worker {Id} (process {ProcessId}) is serving ({Seconds:0.0} s after it started).", id, worker.ProcessId,
						(DateTime.UtcNow - worker.Started).TotalSeconds);
					currentReady.TrySetResult(worker);
					return true;
				case WorkerProtocol.Recycle:
					if (worker == null) return true; // already retired
					// "<drain seconds> <reason>": how long the worker's requests may take to finish (#181).
					var drain = TimeSpan.Zero;
					int space = reason.IndexOf(' ');
					if (int.TryParse(space < 0 ? reason : reason[..space], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
					{
						drain = TimeSpan.FromSeconds(seconds);
						reason = space < 0 ? "" : reason[(space + 1)..];
					}
					worker.State = WorkerState.Retiring;
					retiring.Add(worker);
					current = null;
					logger.LogInformation("Recycling worker {Id} (process {ProcessId}): {Reason}.", id, worker.ProcessId, reason.Length != 0 ? reason : "the application restarted");
					currentReady.TrySetResult(null);
					StartWorker($"worker {id} recycled");
					// The worker finishes its requests, for up to the drain time (<httpRuntime shutdownTimeout> and a sixth of
					// it, as .NET Framework waited for an old domain's requests), and exits by itself; one that doesn't is
					// killed the shutdown time limit after that.
					_ = KillAfterAsync(worker, drain + settings.ShutdownTimeLimit);
					return true;
				default:
					return false;
			}
		}
	}

	async Task KillAfterAsync(Worker worker, TimeSpan limit)
	{
		if (await Task.WhenAny(worker.Exited, Task.Delay(limit)) == worker.Exited) return;
		logger.LogWarning("Worker {Id} (process {ProcessId}) was still running {Limit} after it was recycled: killing it.", worker.Id, worker.ProcessId, limit);
		worker.Kill();
	}

	async Task WaitForExitAsync(Worker worker, TimeSpan limit)
	{
		if (await Task.WhenAny(worker.Exited, Task.Delay(limit)) == worker.Exited) return;
		logger.LogWarning("Worker {Id} (process {ProcessId}) was still running {Limit} after it was asked to stop: killing it.", worker.Id, worker.ProcessId, limit);
		worker.Kill();
		await worker.Exited;
	}

	// The current worker once it is ready; null when none will be within the queue timeout (or the request was aborted).
	async Task<Worker?> GetReadyWorkerAsync(CancellationToken aborted)
	{
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(aborted);
		timeout.CancelAfter(settings.QueueTimeout);
		while (true)
		{
			Task<Worker?> ready;
			lock (sync)
			{
				if (stopped || shuttingDown) return null;
				if (current is { State: WorkerState.Ready } worker) return worker;
				if (current == null) StartWorker("a request arrived");
				ready = currentReady.Task;
			}
			try
			{
				await ready.WaitAsync(timeout.Token);
			}
			catch (OperationCanceledException)
			{
				return null;
			}
			// Ready, or that worker won't serve (StartWorker then has a new one starting): look again.
		}
	}

	public async Task ForwardAsync(HttpContext context)
	{
		// Only the host says where a request came from.
		foreach (var header in WorkerProtocol.RequestHeaders) context.Request.Headers.Remove(header);
		// A request without a body can be sent again to the next worker if one that is shutting down didn't take it.
		bool canRetry = context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == false;
		for (int attempt = 0; ; attempt++)
		{
			var worker = await GetReadyWorkerAsync(context.RequestAborted);
			if (worker == null)
			{
				if (!context.RequestAborted.IsCancellationRequested) await ServiceUnavailableAsync(context);
				return;
			}
			var transformer = new WorkerTransformer(canRetry && attempt < MaxRetries);
			ForwarderError error;
			worker.BeginRequest();
			try
			{
				error = await forwarder.SendAsync(context, "http://localhost/", worker.Invoker, requestConfig, transformer);
			}
			finally
			{
				worker.EndRequest();
			}
			if (transformer.WorkerRestarting && !context.Response.HasStarted)
			{
				// The worker is shutting down: its recycle message is on its way, after which the next worker serves. The
				// forwarder has already set the worker's 503 on the response (and won't forward into a response that isn't a
				// fresh 200): clear it first.
				context.Response.Clear();
				try { await Task.Delay(RetryDelay, context.RequestAborted); }
				catch (OperationCanceledException) { return; }
				continue;
			}
			if (error != ForwarderError.None && !context.Response.HasStarted && !context.RequestAborted.IsCancellationRequested)
			{
				var exception = context.Features.Get<IForwarderErrorFeature>()?.Exception;
				if (canRetry && attempt < MaxRetries && await HasEndedAsync(worker))
				{
					// The worker's process ended (a crash, Environment.Exit) without answering: the request goes to the next
					// worker, as one queued for a w3wp that died does on IIS.
					logger.LogInformation("Worker {Id} ended without answering {Method} {Path}: sending it to the next worker.", worker.Id, context.Request.Method, context.Request.Path);
					context.Response.Clear();
					try { await Task.Delay(RetryDelay, context.RequestAborted); }
					catch (OperationCanceledException) { return; }
					continue;
				}
				if (exception is HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError })
				{
					// The worker couldn't be reached at all: the gateway's error.
					logger.LogWarning(exception, "Forwarding {Method} {Path} to worker {Id} failed: {Error}.", context.Request.Method, context.Request.Path, worker.Id, error);
					context.Response.StatusCode = StatusCodes.Status502BadGateway;
				}
				else
				{
					// The worker took the request and then dropped its connection (Request.Abort(), or the process ended):
					// the client's connection is reset, as IIS resets it.
					logger.LogInformation("Worker {Id} ended {Method} {Path} without a response ({Error}): aborting the client's connection.", worker.Id, context.Request.Method, context.Request.Path, error);
					context.Abort();
				}
			}
			return;
		}
	}

	// Whether a worker that dropped a request is going away: its process exits within a second, or it is already retired.
	// A worker that is still serving afterwards dropped the request itself (Request.Abort()).
	static async Task<bool> HasEndedAsync(Worker worker)
	{
		if (worker.State == WorkerState.Retiring) return true;
		return await Task.WhenAny(worker.Exited, Task.Delay(TimeSpan.FromSeconds(1))) == worker.Exited;
	}

	// IIS's answer when the application pool can't serve: 503.
	static Task ServiceUnavailableAsync(HttpContext context)
	{
		context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
		context.Response.ContentType = "text/html; charset=utf-8";
		return context.Response.WriteAsync("<!DOCTYPE html><html><head><title>Service Unavailable</title></head><body><h1>Service Unavailable</h1><p>HTTP Error 503. The service is unavailable.</p></body></html>");
	}

	public void Dispose()
	{
		control?.Dispose();
		lock (sync)
		{
			current?.Dispose();
			foreach (var worker in retiring) worker.Dispose();
		}
		try { runDirectory?.Delete(recursive: true); }
		catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
	}
}
