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

	// A reverse proxy in front of the host (a load balancer or an ingress that ends TLS) and the headers the host takes
	// from it (#194). Off by default: the client's address and the scheme are the connection's, as on IIS.
	public ForwardedHeadersSettings ForwardedHeaders { get; set; } = new();
}

// "WebForCore:Host:ForwardedHeaders": the host applies X-Forwarded-For, -Proto and -Host from the proxies it trusts, with
// ASP.NET Core's forwarded headers middleware, before it forwards the request to the worker; the application then sees
// the client's address (Request.UserHostAddress, REMOTE_ADDR, <ipSecurity>), the scheme (IsSecureConnection, HTTPS,
// Request.Url, requireSSL) and the host name (SERVER_NAME, Request.Url) the proxy received. Only the host sees which
// proxy a request came from: the worker's own connection is the host's (docs/MIGRATING.md).
public sealed class ForwardedHeadersSettings
{
	// Which headers: XForwardedFor, XForwardedProto, XForwardedHost, comma-separated ("XForwardedFor, XForwardedProto").
	// None by default. XForwardedPrefix isn't taken: an application under a path has it as its virtual path.
	public Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders Headers { get; set; }

	// The proxies whose headers are applied, by address, and by network in CIDR notation ("10.0.0.0/8"). With neither, a
	// proxy on this machine (the loopback addresses, ASP.NET Core's default). Headers from any other address are ignored: a
	// client that reaches the host directly can't set its own address or scheme.
	public string[] KnownProxies { get; set; } = [];
	public string[] KnownNetworks { get; set; } = [];

	// How many proxies' entries are applied, from the right of each header: the number of trusted proxies in a chain.
	public int ForwardLimit { get; set; } = 1;

	// The host names X-Forwarded-Host may give ("example.com", "*.example.com"); any when empty, as in ASP.NET Core.
	public string[] AllowedHosts { get; set; } = [];

	public bool Enabled => Headers != Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.None;

	public ForwardedHeadersOptions ToOptions()
	{
		var supported = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
			| Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
			| Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedHost;
		if ((Headers & ~supported) != 0)
			throw new InvalidOperationException($"{HostSettings.Section}:ForwardedHeaders:Headers: only XForwardedFor, XForwardedProto and XForwardedHost " +
				"are taken. An application under a path sets it as its virtual path: UseWebForms(o => o.VirtualPath(\"/app\")).");
		var options = new ForwardedHeadersOptions { ForwardedHeaders = Headers, ForwardLimit = ForwardLimit };
		if (KnownProxies.Length + KnownNetworks.Length > 0)
		{
			options.KnownProxies.Clear();
			options.KnownIPNetworks.Clear();
		}
		foreach (var proxy in KnownProxies) options.KnownProxies.Add(System.Net.IPAddress.Parse(proxy));
		foreach (var network in KnownNetworks) options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
		foreach (var host in AllowedHosts) options.AllowedHosts.Add(host);
		return options;
	}

	public override string ToString() => $"{Headers} from {string.Join(", ", KnownProxies.Length + KnownNetworks.Length == 0 ? ["loopback"] : KnownProxies.Concat(KnownNetworks))}" +
		$" (ForwardLimit {ForwardLimit}{(AllowedHosts.Length > 0 ? ", hosts " + string.Join(", ", AllowedHosts) : "")})";
}

// The application the workers run: dotnet <Application> <Arguments>.
public sealed record WorkerCommand(string Application, string[] Arguments);
