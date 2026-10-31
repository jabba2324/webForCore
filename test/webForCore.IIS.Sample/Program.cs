using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;
using WebFormsForCore.Hosting.IIS;

namespace IISSample
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            // IIS's compression and W3C logging (App_Data is a hidden segment, so the logs are not served).
            builder.Services.AddWebForms(o => o.LogDirectory = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "logs"));
            // GATE_HTTPS_PORT: also listen with TLS, a generated certificate, and client certificates accepted (as IIS's
            // SSL settings "Accept"), for Request.ClientCertificate and the HTTPS/CERT_ server variables.
            if (int.TryParse(Environment.GetEnvironmentVariable("GATE_HTTPS_PORT"), out int httpsPort))
            {
                var http = new Uri(builder.Configuration["urls"]);
                builder.WebHost.ConfigureKestrel(kestrel =>
                {
                    kestrel.Listen(IPAddress.Loopback, http.Port);
                    kestrel.Listen(IPAddress.Loopback, httpsPort, listen => listen.UseHttps(new HttpsConnectionAdapterOptions
                    {
                        ServerCertificate = ServerCertificate(),
                        ClientCertificateMode = ClientCertificateMode.AllowCertificate,
                        ClientCertificateValidation = (certificate, chain, errors) => true,
                    }));
                });
            }
            var app = builder.Build();
            app.UseWebForms(options =>
            {
                options.HandleAllRequestsWithWebForms();
                // GATE_RECYCLE_CHECK: seconds between unload checks, so a test sees an old domain collected sooner.
                if (int.TryParse(Environment.GetEnvironmentVariable("GATE_RECYCLE_CHECK"), out int seconds))
                    options.Recycling(r => r.CheckInterval = TimeSpan.FromSeconds(seconds));
            });
            app.Run();
        }

        static X509Certificate2 ServerCertificate()
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), null);
        }
    }
}
