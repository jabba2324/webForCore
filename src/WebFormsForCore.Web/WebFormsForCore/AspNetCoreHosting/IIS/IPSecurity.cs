#if NETCOREAPP
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using static WebFormsForCore.Hosting.IIS.WebServerConfiguration;

namespace WebFormsForCore.Hosting.IIS
{
	// webForCore: IIS's IP and Domain Restrictions (<security><ipSecurity>) and Dynamic IP Restrictions
	// (<security><dynamicIpSecurity>).
	// ipSecurity: the first <add> that matches the client (ipAddress with subnetMask, or domainName when enableReverseDns)
	// decides; with no match, allowUnlisted. enableProxyMode also checks the addresses in X-Forwarded-For.
	// dynamicIpSecurity: denyByConcurrentRequests (maxConcurrentRequests at once from one address) and denyByRequestRate
	// (maxRequests within requestIntervalInMilliseconds); enableLoggingOnlyMode only logs.
	// A denied request gets denyAction: Forbidden (403.6 by address, 403.8 by name, 403.501 too many concurrent requests,
	// 403.502 too many requests), Unauthorized (401), NotFound (404) or AbortRequest (the connection is closed).
	sealed class IPSecurity
	{
		sealed class Client
		{
			public int Active;
			public readonly Queue<long> Requests = new Queue<long>();
			public long LastSeen;
		}

		readonly ILogger logger;
		readonly ConcurrentDictionary<IPAddress, Client> clients = new ConcurrentDictionary<IPAddress, Client>();

		public IPSecurity(ILogger logger)
		{
			this.logger = logger;
		}

		public delegate Task WriteError(HttpContext context, int status, int subStatus, string title, string reason);

		// True when the request was denied (and answered).
		public async Task<bool> Deny(HttpContext context, XElement section, WriteError writeError)
		{
			var remote = context.Connection.RemoteIpAddress;
			if (remote == null) return false;
			var ipSecurity = Element(section, "security/ipSecurity");
			if (ipSecurity != null)
			{
				var addresses = Addresses(context, remote, Bool(ipSecurity, "enableProxyMode", false));
				foreach (var address in addresses)
				{
					var (allowed, byName) = await Allowed(ipSecurity, Items(section, "security/ipSecurity"), address);
					if (!allowed)
					{
						return await Answer(context, Attr(ipSecurity, "denyAction", "Forbidden"), byName ? 8 : 6,
							byName ? "Forbidden: Client host name rejected." : "Forbidden: Client IP address rejected.", writeError);
					}
				}
			}
			var dynamic = Element(section, "security/dynamicIpSecurity");
			var concurrent = Element(section, "security/dynamicIpSecurity/denyByConcurrentRequests");
			var rate = Element(section, "security/dynamicIpSecurity/denyByRequestRate");
			bool byConcurrent = Bool(concurrent, "enabled", false), byRate = Bool(rate, "enabled", false);
			if (!byConcurrent && !byRate) return false;
			var key = Addresses(context, remote, Bool(dynamic, "enableProxyMode", false)).First();
			var now = Environment.TickCount64;
			var client = clients.GetOrAdd(key, _ => new Client());
			int subStatus = 0;
			lock (client)
			{
				client.LastSeen = now;
				if (byRate)
				{
					long interval = Long(rate, "requestIntervalInMilliseconds", 200);
					while (client.Requests.Count > 0 && now - client.Requests.Peek() >= interval) client.Requests.Dequeue();
					client.Requests.Enqueue(now);
					if (client.Requests.Count > Long(rate, "maxRequests", 20)) subStatus = 502;
				}
				if (byConcurrent && subStatus == 0 && client.Active >= Long(concurrent, "maxConcurrentRequests", 20)) subStatus = 501;
				if (subStatus == 0 || Bool(dynamic, "enableLoggingOnlyMode", false)) client.Active++;
			}
			Prune(now);
			if (subStatus != 0)
			{
				logger.LogWarning("Dynamic IP restriction for {Address}: {Reason}.", key, subStatus == 501 ? "too many concurrent requests" : "too many requests");
				if (!Bool(dynamic, "enableLoggingOnlyMode", false))
				{
					return await Answer(context, Attr(dynamic, "denyAction", "Forbidden"), subStatus, subStatus == 501
						? "Forbidden: Too many requests from the same client IP; Dynamic IP Restriction Concurrent request rate limit reached."
						: "Forbidden: Too many requests from the same client IP; Dynamic IP Restriction Maximum request rate limit reached.", writeError);
				}
			}
			context.Response.RegisterForDispose(new Release(client));
			return false;
		}

		sealed class Release : IDisposable
		{
			Client client;
			public Release(Client client) { this.client = client; }
			public void Dispose()
			{
				var c = Interlocked.Exchange(ref client, null);
				if (c != null) lock (c) c.Active--;
			}
		}

		void Prune(long now)
		{
			if (clients.Count < 10000) return;
			foreach (var pair in clients)
				lock (pair.Value)
					if (pair.Value.Active == 0 && now - pair.Value.LastSeen > 60000) clients.TryRemove(pair.Key, out _);
		}

		static IEnumerable<IPAddress> Addresses(HttpContext context, IPAddress remote, bool proxyMode)
		{
			var result = new List<IPAddress>();
			if (proxyMode)
				foreach (var value in context.Request.Headers["X-Forwarded-For"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
					if (IPAddress.TryParse(value, out var forwarded)) result.Add(Normalize(forwarded));
			result.Add(Normalize(remote));
			return result;
		}

		static IPAddress Normalize(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

		async Task<(bool Allowed, bool ByName)> Allowed(XElement ipSecurity, IEnumerable<XElement> entries, IPAddress address)
		{
			string hostName = null;
			bool reverseDns = Bool(ipSecurity, "enableReverseDns", false);
			foreach (var entry in entries)
			{
				var ip = Attr(entry, "ipAddress");
				var domain = Attr(entry, "domainName");
				bool allowed = Bool(entry, "allowed", false);
				if (!string.IsNullOrEmpty(ip))
				{
					if (Matches(address, ip, Attr(entry, "subnetMask"))) return (allowed, false);
				}
				else if (!string.IsNullOrEmpty(domain) && reverseDns)
				{
					hostName ??= await HostName(address);
					if (hostName != null && WildcardMatch(hostName, domain)) return (allowed, true);
				}
			}
			return (Bool(ipSecurity, "allowUnlisted", true), false);
		}

		async Task<string> HostName(IPAddress address)
		{
			try
			{
				return (await Dns.GetHostEntryAsync(address)).HostName;
			}
			catch (SocketException)
			{
				return "";
			}
		}

		static bool WildcardMatch(string value, string pattern) =>
			System.Text.RegularExpressions.Regex.IsMatch(value, "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*") + "$",
				System.Text.RegularExpressions.RegexOptions.IgnoreCase);

		// An address against ipAddress and subnetMask (a dotted mask, or a prefix length; no mask: the address itself).
		static bool Matches(IPAddress address, string ipAddress, string subnetMask)
		{
			if (!IPAddress.TryParse(ipAddress, out var network)) return false;
			network = Normalize(network);
			if (network.AddressFamily != address.AddressFamily) return false;
			var a = address.GetAddressBytes();
			var n = network.GetAddressBytes();
			byte[] mask;
			if (string.IsNullOrEmpty(subnetMask)) mask = Enumerable.Repeat((byte)0xff, a.Length).ToArray();
			else if (int.TryParse(subnetMask, out int prefix)) mask = Enumerable.Range(0, a.Length).Select(i => (byte)(prefix >= (i + 1) * 8 ? 0xff : prefix <= i * 8 ? 0 : 0xff << (8 - (prefix - i * 8)))).ToArray();
			else if (IPAddress.TryParse(subnetMask, out var dotted) && dotted.GetAddressBytes().Length == a.Length) mask = dotted.GetAddressBytes();
			else return false;
			for (int i = 0; i < a.Length; i++)
				if ((a[i] & mask[i]) != (n[i] & mask[i])) return false;
			return true;
		}

		static async Task<bool> Answer(HttpContext context, string denyAction, int subStatus, string reason, WriteError writeError)
		{
			switch (denyAction)
			{
				case "AbortRequest":
					context.Abort();
					return true;
				case "Unauthorized":
					await writeError(context, 401, 0, "Unauthorized", reason);
					return true;
				case "NotFound":
					await writeError(context, 404, 0, "Not Found", reason);
					return true;
				default:
					await writeError(context, 403, subStatus, "Forbidden", reason);
					return true;
			}
		}
	}
}
#endif
