using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Yarp.ReverseProxy.Forwarder;

namespace WebFormsForCore.Host;

// How a request goes to a worker: as the client sent it (its request target and Host header unchanged, no headers added
// by the proxy), plus the connection details the worker can't see itself (WorkerProtocol). A worker that is shutting
// down answers 503 with WorkerProtocol.RestartingHeader without processing the request; when the request can be sent
// again (it has no body), that response isn't passed on, and the pool sends the request to the next worker.
sealed class WorkerTransformer : HttpTransformer
{
	readonly bool canRetry;

	public WorkerTransformer(bool canRetry) => this.canRetry = canRetry;

	// The worker was shutting down and didn't take the request; nothing was sent to the client.
	public bool WorkerRestarting { get; private set; }

	public override async ValueTask TransformRequestAsync(HttpContext context, HttpRequestMessage proxyRequest, string destinationPrefix,
		CancellationToken cancellationToken)
	{
		await base.TransformRequestAsync(context, proxyRequest, destinationPrefix, cancellationToken);

		// The request target as the client sent it: System.Web reads the raw URL (Request.RawUrl), and the path Kestrel
		// decoded would come back re-encoded.
		var rawTarget = context.Features.Get<IHttpRequestFeature>()?.RawTarget;
		if (!string.IsNullOrEmpty(rawTarget) && rawTarget[0] == '/')
			proxyRequest.RequestUri = new Uri(destinationPrefix.TrimEnd('/') + rawTarget, UriKind.Absolute);
		// The client's Host header, not the worker's address.
		proxyRequest.Headers.Host = context.Request.Host.Value;

		var connection = context.Connection;
		Add(proxyRequest, WorkerProtocol.RemoteHeader, Endpoint(connection.RemoteIpAddress, connection.RemotePort));
		Add(proxyRequest, WorkerProtocol.LocalHeader, Endpoint(connection.LocalIpAddress, connection.LocalPort));
		Add(proxyRequest, WorkerProtocol.SchemeHeader, context.Request.Scheme);
		if (connection.ClientCertificate is { } certificate)
			Add(proxyRequest, WorkerProtocol.ClientCertificateHeader, Convert.ToBase64String(certificate.RawData));
		if (context.Features.Get<ITlsHandshakeFeature>() is { } tls)
		{
#pragma warning disable SYSLIB0058 // CipherStrength: what System.Web reports as HTTPS_KEYSIZE
			Add(proxyRequest, WorkerProtocol.TlsHeader, ((int)tls.Protocol).ToString(CultureInfo.InvariantCulture) + ";"
				+ tls.CipherStrength.ToString(CultureInfo.InvariantCulture));
#pragma warning restore SYSLIB0058
		}
	}

	public override ValueTask<bool> TransformResponseAsync(HttpContext context, HttpResponseMessage? proxyResponse, CancellationToken cancellationToken)
	{
		if (canRetry && proxyResponse is { StatusCode: HttpStatusCode.ServiceUnavailable }
			&& proxyResponse.Headers.Contains(WorkerProtocol.RestartingHeader))
		{
			WorkerRestarting = true;
			return new ValueTask<bool>(false); // nothing copied: the pool sends the request again
		}
		return base.TransformResponseAsync(context, proxyResponse, cancellationToken);
	}

	static string Endpoint(IPAddress? address, int port) =>
		address == null ? "" : "[" + address + "]:" + port.ToString(CultureInfo.InvariantCulture);

	static void Add(HttpRequestMessage request, string name, string value)
	{
		if (value.Length != 0) request.Headers.TryAddWithoutValidation(name, value);
	}
}
