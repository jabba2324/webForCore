using System.Net;
using System.Text.RegularExpressions;
using Xunit;

namespace IISGate
{
    // System.Web.Mail.SmtpMail (CDO on .NET Framework) through System.Net.Mail, and <system.net><mailSettings> read
    // from Web.config, which .NET's SmtpClient ignores and System.Web's mail senders apply.
    [Collection(IISSampleCollection.Name)]
    public class MailTests
    {
        private readonly SampleServer _server;

        public MailTests(SampleServer server)
        {
            _server = server;
        }

        [Fact]
        public async Task SmtpMailSendsWithCdoFields()
        {
            string body = await _server.Client.GetStringAsync("/mail/Send.aspx");
            Assert.Contains("configured=noreply@example.com/127.0.0.1:2525;", body);

            string id = body.Split("id=")[1].Split(';')[0];
            string[] files = Directory.GetFiles(Path.Combine(_server.SampleDirectory, "App_Data", "mail", id), "*.eml");
            string eml = File.ReadAllText(Assert.Single(files));
            Directory.Delete(Path.Combine(_server.SampleDirectory, "App_Data", "mail", id), recursive: true);

            Assert.Contains("From: \"Shop\" <shop@example.com>", eml);
            Assert.Contains("To: ada@example.com, bob@example.com", eml);
            Assert.Contains("Cc: cy@example.com", eml);
            Assert.Contains("Subject: Order 42", eml);
            Assert.Contains("X-Priority: 1", eml);
            Assert.Contains("X-Order: 42", eml);
            Assert.Contains("Content-Type: text/html", eml);
            Assert.Contains("attachment.txt", eml);
        }

        // PasswordRecovery mails the new password to the server and from the address <mailSettings> configures.
        [Fact]
        public async Task PasswordRecoveryMailsThroughMailSettings()
        {
            using var smtp = new FakeSmtpServer(2525);
            string page = await _server.Client.GetStringAsync("/mail/Recover.aspx");
            var fields = new Dictionary<string, string>
            {
                ["__EVENTTARGET"] = "",
                ["__EVENTARGUMENT"] = "",
                ["__VIEWSTATE"] = HiddenField(page, "__VIEWSTATE"),
                ["__VIEWSTATEGENERATOR"] = HiddenField(page, "__VIEWSTATEGENERATOR"),
                ["__EVENTVALIDATION"] = HiddenField(page, "__EVENTVALIDATION"),
                ["Recovery$UserNameContainerID$UserName"] = "ada",
                ["Recovery$UserNameContainerID$SubmitButton"] = "Submit",
            };
            HttpResponseMessage response = await _server.Client.PostAsync("/mail/Recover.aspx", new FormUrlEncodedContent(fields));
            string body = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, body);
            Assert.Contains("mail sent", body);

            string message = smtp.NextMessage(TimeSpan.FromSeconds(10));
            Assert.NotNull(message);
            Assert.Contains("MAIL FROM:<noreply@example.com>", message);
            Assert.Contains("RCPT TO:<ada@example.com>", message);
            Assert.Contains("Subject: Your new password", message);
            Assert.Contains("Reset-ada-42", message);
        }

        private static string HiddenField(string page, string name)
        {
            Match match = Regex.Match(page, "id=\"" + name + "\" value=\"([^\"]*)\"");
            Assert.True(match.Success, name + " not found");
            return WebUtility.HtmlDecode(match.Groups[1].Value);
        }
    }
}
