// webForCore (MIT): the <system.net> settings that .NET Framework's System.Net read from configuration and .NET's
// doesn't: <defaultProxy> and <mailSettings><smtp>. System.Web applies the proxy when an application starts and
// configures its own mail senders (PasswordRecovery, CreateUserWizard, ChangePassword, MailWebEventProvider,
// SmtpMail, WebMail) with these; an application's own `new SmtpClient()` reads nothing on .NET, so it can use
// CreateSmtpClient instead.
using System;
using System.Configuration;
using System.Net;
using System.Net.Configuration;
using System.Net.Http;
using System.Net.Mail;
using System.Reflection;

namespace WebFormsForCore.Net.Configuration
{
	public static class NetConfiguration
	{
		// HttpClient.DefaultProxy before the first application changed it: process-wide, as the proxy is, so each
		// start of an application (a PseudoAppDomain, with its own copy of this class) begins from it.
		private const string OriginalProxyKey = "WebFormsForCore.OriginalDefaultProxy";

		// <system.net><mailSettings><smtp>, or null.
		public static SmtpSection MailSettings
		{
			get
			{
				try
				{
					return ConfigurationManager.GetSection("system.net/mailSettings/smtp") as SmtpSection;
				}
				catch (ConfigurationException)
				{
					return null;
				}
			}
		}

		// <smtp from>: the From address of a new MailMessage on .NET Framework.
		public static MailAddress DefaultFrom =>
			MailSettings?.From is string from && from.Length != 0 ? new MailAddress(from) : null;

		// A SmtpClient set up as .NET Framework's parameterless constructor set it up from configuration.
		public static SmtpClient CreateSmtpClient()
		{
			var client = new SmtpClient();
			ApplyMailSettings(client);
			return client;
		}

		// .NET Framework's SmtpClient.Initialize: the host only if the client has none, the rest always.
		public static void ApplyMailSettings(SmtpClient client)
		{
			if (client == null)
				throw new ArgumentNullException(nameof(client));
			SmtpSection settings = MailSettings;
			if (settings == null)
				return;
			SmtpNetworkElement network = settings.Network;
			if (network != null)
			{
				if (string.IsNullOrEmpty(client.Host) && !string.IsNullOrEmpty(network.Host))
				{
					client.Host = network.Host.Trim();
					client.Port = network.Port;
				}
				if (network.DefaultCredentials)
					client.UseDefaultCredentials = true;
				else if (!string.IsNullOrEmpty(network.UserName))
					client.Credentials = new NetworkCredential(network.UserName, network.Password);
				client.EnableSsl = network.EnableSsl;
				if (network.TargetName != null)
					client.TargetName = network.TargetName;
			}
			client.DeliveryFormat = settings.DeliveryFormat;
			client.DeliveryMethod = settings.DeliveryMethod;
			if (settings.SpecifiedPickupDirectory?.PickupDirectoryLocation is string pickup && pickup.Length != 0)
				client.PickupDirectoryLocation = pickup;
		}

		// <system.net><defaultProxy> into HttpClient.DefaultProxy, which HttpClient, HttpWebRequest, WebClient and
		// SOAP proxies use. Without the section, .NET's default stays (HTTP_PROXY, HTTPS_PROXY, NO_PROXY: the
		// system's settings on Linux).
		public static void ApplyDefaultProxy()
		{
			IWebProxy original = AppContext.GetData(OriginalProxyKey) as IWebProxy;
			if (original == null)
			{
				original = HttpClient.DefaultProxy;
				AppContext.SetData(OriginalProxyKey, original);
			}
			HttpClient.DefaultProxy = CreateDefaultProxy(ConfigurationManager.GetSection("system.net/defaultProxy") as DefaultProxySection)
				?? original;
		}

		// .NET Framework's DefaultProxySectionInternal. Proxy auto-detection and scripts (WPAD, PAC) are Windows
		// only and are ignored. Null: the system's proxy.
		private static IWebProxy CreateDefaultProxy(DefaultProxySection section)
		{
			if (section == null)
				return null;
			if (!section.Enabled)
				return new WebProxy(); // no proxy
			ProxyElement proxy = section.Proxy;
			IWebProxy result;
			if (string.IsNullOrEmpty(section.Module.Type)
				&& proxy.ProxyAddress == null
				&& proxy.BypassOnLocal == ProxyElement.BypassOnLocalValues.Unspecified
				&& section.BypassList.Count == 0)
			{
				if (proxy.UseSystemDefault != ProxyElement.UseSystemDefaultValues.False)
					return WithCredentials(null, section);
				result = new WebProxy(); // usesystemdefault="false" and nothing else: no proxy
			}
			else if (!string.IsNullOrEmpty(section.Module.Type))
			{
				Type type = Type.GetType(section.Module.Type, true, true);
				if ((type.Attributes & TypeAttributes.VisibilityMask) != TypeAttributes.Public)
					throw new ConfigurationErrorsException($"The <defaultProxy><module> type '{type.FullName}' must be public.");
				if (!typeof(IWebProxy).IsAssignableFrom(type))
					throw new InvalidCastException($"The <defaultProxy><module> type '{type.FullName}' doesn't implement IWebProxy.");
				result = (IWebProxy)Activator.CreateInstance(type, nonPublic: true);
			}
			else if (proxy.ProxyAddress == null && proxy.UseSystemDefault == ProxyElement.UseSystemDefaultValues.True)
				return WithCredentials(null, section); // the system's proxy; its bypass list is NO_PROXY
			else
				result = new WebProxy();

			if (result is WebProxy webProxy)
			{
				if (proxy.BypassOnLocal != ProxyElement.BypassOnLocalValues.Unspecified)
					webProxy.BypassProxyOnLocal = proxy.BypassOnLocal == ProxyElement.BypassOnLocalValues.True;
				if (proxy.ProxyAddress != null)
					webProxy.Address = proxy.ProxyAddress;
				if (section.BypassList.Count > 0)
				{
					var bypass = new string[section.BypassList.Count];
					for (int i = 0; i < bypass.Length; i++)
						bypass[i] = section.BypassList[i].Address;
					webProxy.BypassList = bypass;
				}
			}
			return WithCredentials(result, section);
		}

		private static IWebProxy WithCredentials(IWebProxy proxy, DefaultProxySection section)
		{
			if (section.UseDefaultCredentials)
			{
				proxy ??= AppContext.GetData(OriginalProxyKey) as IWebProxy;
				if (proxy != null)
					proxy.Credentials = CredentialCache.DefaultCredentials;
			}
			return proxy;
		}
	}
}
