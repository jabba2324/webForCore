namespace WebFormsForCore.Host;

// The host's settings, from the "WebForCore:Host" configuration section (the application's appsettings.json, environment
// variables such as WebForCore__Host__StartupTimeLimit, or the command line). The defaults are IIS's for an application
// pool (applicationHost.config <applicationPools><add><processModel> and <failure>).
public sealed class HostSettings
{
	public const string Section = "WebForCore:Host";

	// <processModel startupTimeLimit="00:01:30">: a worker that isn't serving by then is stopped, and counts as a failure.
	public TimeSpan StartupTimeLimit { get; set; } = TimeSpan.FromSeconds(90);

	// <processModel shutdownTimeLimit="00:01:30">: a worker that is still running this long after it was asked to stop (a
	// recycle, or the host stopping) is killed.
	public TimeSpan ShutdownTimeLimit { get; set; } = TimeSpan.FromSeconds(90);

	// <failure rapidFailProtection="true" rapidFailProtectionMaxCrashes="5" rapidFailProtectionInterval="00:05:00">: after
	// this many workers exit unexpectedly (or fail to start) within the interval, the host starts no more workers and
	// answers 503 until it is restarted, as IIS stops the application pool.
	public bool RapidFailProtection { get; set; } = true;
	public int RapidFailProtectionMaxCrashes { get; set; } = 5;
	public TimeSpan RapidFailProtectionInterval { get; set; } = TimeSpan.FromMinutes(5);

	// How long a request waits for a worker to be ready (the first start, a recycle, a replacement) before it gets 503.
	// IIS queued it in HTTP.sys, for as long as the worker took to start.
	public TimeSpan QueueTimeout { get; set; } = TimeSpan.FromSeconds(90);

	// A forwarded request with no data either way for this long is abandoned (502). Long enough for long polling
	// (SignalR waits up to 110 s) and for slow pages (<httpRuntime executionTimeout> is 110 s).
	public TimeSpan RequestIdleTimeout { get; set; } = TimeSpan.FromMinutes(10);

	// Client certificates on an HTTPS binding with "ClientCertificateMode": "AllowCertificate" (IIS's SSL setting
	// "Accept") are validated by Kestrel, which refuses one whose chain isn't trusted. With this set, any certificate
	// is passed to the application, which sees whether it is valid (Request.ClientCertificate.IsValid, CERT_FLAGS).
	// For tests and private deployments; off by default.
	public bool AcceptUntrustedClientCertificates { get; set; }
}

// The application the workers run: dotnet <Application> <Arguments>.
public sealed record WorkerCommand(string Application, string[] Arguments);
