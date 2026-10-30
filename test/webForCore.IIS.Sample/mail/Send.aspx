<%@ Page Language="C#" %>
<%@ Import Namespace="System.IO" %>
<%@ Import Namespace="System.Web.Mail" %>
<%
    // System.Web.Mail.SmtpMail, as an old app uses it: CDO's configuration fields choose pickup-directory delivery.
    string id = Guid.NewGuid().ToString("N");
    string pickup = Path.Combine(Server.MapPath("~/App_Data"), "mail", id);
    Directory.CreateDirectory(pickup);
    var message = new MailMessage
    {
        From = "Shop <shop@example.com>",
        To = "ada@example.com; bob@example.com",
        Cc = "cy@example.com",
        Subject = "Order 42",
        Body = "<p>Thank you</p>",
        BodyFormat = MailFormat.Html,
        Priority = MailPriority.High,
    };
    message.Headers.Add("X-Order", "42");
    message.Attachments.Add(new MailAttachment(Server.MapPath("~/mail/attachment.txt")));
    message.Fields["http://schemas.microsoft.com/cdo/configuration/sendusing"] = 1;
    message.Fields["http://schemas.microsoft.com/cdo/configuration/smtpserverpickupdirectory"] = pickup;
    SmtpMail.Send(message);

    var smtp = (System.Net.Configuration.SmtpSection)System.Configuration.ConfigurationManager.GetSection("system.net/mailSettings/smtp");
    Response.Write("id=" + id + ";configured=" + smtp.From + "/" + smtp.Network.Host + ":" + smtp.Network.Port + ";");
%>
