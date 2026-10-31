<%@ Page Language="C#" %>
<!DOCTYPE html>
<html>
<body>
    <%-- PasswordRecovery mails the reset password with a SmtpClient set up from <system.net><mailSettings>, and
         MailDefinition's From defaults to <smtp from>. --%>
    <form id="form1" runat="server">
        <asp:PasswordRecovery ID="Recovery" runat="server" SuccessText="mail sent">
            <MailDefinition Subject="Your new password" />
        </asp:PasswordRecovery>
    </form>
</body>
</html>
