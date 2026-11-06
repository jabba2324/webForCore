// webforcore-host (#56): runs an application on webForCore as IIS did. See HostSettings for its settings and
// WorkerPool for how workers are started, recycled and replaced.
//
//   dotnet webforcore-host.dll [host arguments] [-- <application>.dll [application arguments]]
//
// Host arguments are ASP.NET Core's (--urls, --environment, configuration keys); the bindings and TLS come from them and
// from the application's appsettings.json "Kestrel" section, read from the working directory as the application reads
// it. Without "-- <application>.dll", the host runs the application named in webforcore-host.app next to it (written by
// the webForCore.Host package's build targets), in the folder above.

using Microsoft.AspNetCore.Server.Kestrel.Core;
using WebFormsForCore.Host;

const string Usage = "usage: dotnet webforcore-host.dll [--urls <urls>] [other ASP.NET Core arguments] [-- <application>.dll [application arguments]]";

int separator = Array.IndexOf(args, "--");
string[] hostArgs = separator < 0 ? args : args[..separator];
string[] appCommand = separator < 0 ? [] : args[(separator + 1)..];
string? application = appCommand.Length > 0 ? appCommand[0] : DefaultApplication();
if (application == null)
{
	Console.Error.WriteLine(Usage);
	return 2;
}
application = Path.GetFullPath(application);
if (!File.Exists(application))
{
	Console.Error.WriteLine($"webforcore-host: the application {application} doesn't exist.");
	return 2;
}

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = hostArgs });
var settings = builder.Configuration.GetSection(HostSettings.Section).Get<HostSettings>() ?? new HostSettings();
if (settings.SiteId < 1)
{
	// IIS's site IDs are positive.
	Console.Error.WriteLine($"webforcore-host: {HostSettings.Section}:SiteId is {settings.SiteId}; an IIS site's ID is 1 or more.");
	return 2;
}
// The worker's System.Web enforces <httpRuntime maxRequestLength> and its own Kestrel limit, as when it runs on its own;
// the host doesn't limit the body a second time unless the configuration says so.
if (builder.Configuration["Kestrel:Limits:MaxRequestBodySize"] == null)
	builder.Services.Configure<KestrelServerOptions>(options => options.Limits.MaxRequestBodySize = null);
if (settings.AcceptUntrustedClientCertificates)
	builder.Services.Configure<KestrelServerOptions>(options => options.ConfigureHttpsDefaults(https =>
		https.ClientCertificateValidation = (certificate, chain, errors) => true));
// Kestrel drains the forwarded requests on shutdown; the workers then get their own shutdown time.
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = settings.ShutdownTimeLimit);
builder.Services.AddHttpForwarder();
builder.Services.AddSingleton(settings);
builder.Services.AddSingleton(new WorkerCommand(application, appCommand.Length > 1 ? appCommand[1..] : []));
builder.Services.AddSingleton<WorkerPool>();
builder.Services.AddHostedService(services => services.GetRequiredService<WorkerPool>());

var app = builder.Build();
var pool = app.Services.GetRequiredService<WorkerPool>();
// The proxy's headers, before the request goes to the worker (#194): the worker's peer is the host, so only the host can
// tell whether a request came from a trusted proxy. Invalid settings stop the host here, before it serves.
if (settings.ForwardedHeaders.Enabled)
{
	app.UseForwardedHeaders(settings.ForwardedHeaders.ToOptions());
	app.Logger.LogInformation("Forwarded headers: {ForwardedHeaders}.", settings.ForwardedHeaders);
}
app.Run(pool.ForwardAsync);
await app.RunAsync();
return 0;

// The application named in webforcore-host.app, in the folder above the host's.
static string? DefaultApplication()
{
	var file = Path.Combine(AppContext.BaseDirectory, "webforcore-host.app");
	if (!File.Exists(file)) return null;
	var name = File.ReadAllText(file).Trim();
	return name.Length == 0 ? null : Path.Combine(AppContext.BaseDirectory, "..", name);
}
