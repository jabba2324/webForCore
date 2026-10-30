using Xunit;

namespace IISGate
{
    // System.Web.Mail.SmtpMail (CDO on .NET Framework) through System.Net.Mail, and <system.net><mailSettings> read
    // from Web.config.
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
            Assert.Contains("configured=noreply@example.com/mail.example.com:2525;", body);

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
    }
}
