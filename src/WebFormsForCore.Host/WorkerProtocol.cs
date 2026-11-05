namespace WebFormsForCore.Host;

// What the host and its workers agree on. The worker's side is System.Web's WebFormsForCore.Hosting.WorkerProcess
// (src/WebFormsForCore.Web/WebFormsForCore/AspNetCoreHosting/WorkerProcess.cs); keep the two in step.
static class WorkerProtocol
{
	// Environment variables the host starts a worker with.
	public const string SocketVariable = "WEBFORCORE_WORKER_SOCKET";   // the Unix socket the worker serves on
	public const string IdVariable = "WEBFORCORE_WORKER_ID";           // the worker's number
	public const string ControlVariable = "WEBFORCORE_HOST_CONTROL";   // the host's control socket
	public const string TokenVariable = "WEBFORCORE_HOST_TOKEN";       // proves a control message comes from a worker
	// ASP.NET Core's: System.Web's WorkerHostingStartup sets the worker up before the application's Program.cs runs.
	public const string HostingStartupVariable = "ASPNETCORE_HOSTINGSTARTUPASSEMBLIES";

	// Request headers with what the worker can't see itself; the host removes any a client sent.
	public const string RemoteHeader = "X-WebForCore-Remote";          // client address and port: "[address]:port"
	public const string LocalHeader = "X-WebForCore-Local";            // the binding's local address and port
	public const string SchemeHeader = "X-WebForCore-Scheme";          // http or https
	public const string ClientCertificateHeader = "X-WebForCore-Client-Certificate"; // DER, base64
	public const string TlsHeader = "X-WebForCore-Tls";                // "<SslProtocols>;<cipher strength>"
	public static readonly string[] RequestHeaders = [RemoteHeader, LocalHeader, SchemeHeader, ClientCertificateHeader, TlsHeader];

	// Response header of a worker that is shutting down and didn't process the request (with 503): the host can send
	// the request to the next worker if it has no body.
	public const string RestartingHeader = "X-WebForCore-Restarting";

	// Control messages, one line each, worker to host: "<token> ready <id>" once the worker serves, and
	// "<token> recycle <id> <drain seconds> <reason>" when the application restarts: the worker finishes its requests,
	// for up to the drain time (#181), and exits. The host answers "ok" or "error".
	public const string Ready = "ready";
	public const string Recycle = "recycle";
}
