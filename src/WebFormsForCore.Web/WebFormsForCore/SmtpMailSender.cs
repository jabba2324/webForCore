// webForCore: System.Web.Mail.SmtpMail on .NET (MIT license). .NET Framework sent through Windows' CDO COM component;
// here the message goes through System.Net.Mail.SmtpClient. CDO's configuration fields that applications set on
// MailMessage.Fields (server, port, authentication, SSL, timeout, pickup directory) are honoured; without a server
// (SmtpMail.SmtpServer or the smtpserver field), <system.net><mailSettings><smtp> applies, then localhost, as CDO used
// the local SMTP service.
#if !NETFRAMEWORK
using System.Collections;
using System.IO;
using System.Net;
using WebFormsForCore.Net.Configuration;
using NetMail = System.Net.Mail;

namespace System.Web.Mail
{
	internal static class SmtpMailSender
	{
		private const string Cdo = "http://schemas.microsoft.com/cdo/configuration/";

		internal static void Send(string from, string to, string subject, string messageText)
		{
			Send(new MailMessage { From = from, To = to, Subject = subject, Body = messageText });
		}

		internal static void Send(MailMessage message)
		{
			using var mail = new NetMail.MailMessage();
			if (!string.IsNullOrEmpty(message.From))
				mail.From = new NetMail.MailAddress(message.From);
			AddAddresses(mail.To, message.To);
			AddAddresses(mail.CC, message.Cc);
			AddAddresses(mail.Bcc, message.Bcc);
			mail.Subject = message.Subject ?? string.Empty;
			mail.Body = message.Body ?? string.Empty;
			mail.IsBodyHtml = message.BodyFormat == MailFormat.Html;
			if (message.BodyEncoding != null)
				mail.BodyEncoding = message.BodyEncoding;
			mail.Priority = message.Priority switch
			{
				MailPriority.Low => NetMail.MailPriority.Low,
				MailPriority.High => NetMail.MailPriority.High,
				_ => NetMail.MailPriority.Normal,
			};
			if (message.UrlContentBase != null)
				mail.Headers["Content-Base"] = message.UrlContentBase;
			if (message.UrlContentLocation != null)
				mail.Headers["Content-Location"] = message.UrlContentLocation;
			foreach (DictionaryEntry header in message.Headers)
				mail.Headers[(string)header.Key] = (string)header.Value;
			foreach (MailAttachment attachment in message.Attachments)
				mail.Attachments.Add(new NetMail.Attachment(attachment.Filename));

			using var client = CreateClient(message.Fields);
			mail.From ??= NetConfiguration.DefaultFrom;
			client.Send(mail);
		}

		private static NetMail.SmtpClient CreateClient(IDictionary fields)
		{
			var client = new NetMail.SmtpClient();

			string server = Field(fields, "smtpserver") ?? SmtpMail.SmtpServer;
			if (string.IsNullOrEmpty(server))
			{
				NetConfiguration.ApplyMailSettings(client);
				server = client.Host;
			}
			client.Host = string.IsNullOrEmpty(server) ? "localhost" : server;

			// CDO's fields, as MailMessage.Fields sets them
			if (int.TryParse(Field(fields, "smtpserverport"), out int port))
				client.Port = port;
			if (Field(fields, "smtpusessl") is string ssl)
				client.EnableSsl = ssl == "1" || string.Equals(ssl, "true", StringComparison.OrdinalIgnoreCase);
			if (int.TryParse(Field(fields, "smtpconnectiontimeout"), out int seconds))
				client.Timeout = seconds * 1000;
			if (Field(fields, "smtpauthenticate") == "1") // cdoBasic
				client.Credentials = new NetworkCredential(Field(fields, "sendusername"), Field(fields, "sendpassword"));
			if (Field(fields, "sendusing") == "1" || Field(fields, "smtpserverpickupdirectory") != null) // cdoSendUsingPickup
			{
				client.DeliveryMethod = NetMail.SmtpDeliveryMethod.SpecifiedPickupDirectory;
				client.PickupDirectoryLocation = Field(fields, "smtpserverpickupdirectory") ?? client.PickupDirectoryLocation;
			}
			return client;
		}

		private static string Field(IDictionary fields, string name)
		{
			object value = fields[Cdo + name];
			return value == null ? null : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
		}

		// CDO accepted addresses separated by ';' or ','.
		private static void AddAddresses(NetMail.MailAddressCollection collection, string addresses)
		{
			if (string.IsNullOrWhiteSpace(addresses))
				return;
			foreach (string address in addresses.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
				collection.Add(address);
		}
	}
}
#endif
