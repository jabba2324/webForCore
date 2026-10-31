using System;
using System.Collections.Specialized;
using System.Web.Security;

namespace IISSample
{
    // One member, ada, for the login controls that send mail (mail/Recover.aspx): reset passwords are mailed through
    // <system.net><mailSettings>.
    public class GateMembershipProvider : MembershipProvider
    {
        private const string UserName = "ada";
        private const string Email = "ada@example.com";

        public override string ApplicationName { get; set; } = "/";
        public override bool EnablePasswordReset => true;
        public override bool EnablePasswordRetrieval => false;
        public override bool RequiresQuestionAndAnswer => false;
        public override bool RequiresUniqueEmail => true;
        public override int MaxInvalidPasswordAttempts => 5;
        public override int PasswordAttemptWindow => 10;
        public override MembershipPasswordFormat PasswordFormat => MembershipPasswordFormat.Hashed;
        public override int MinRequiredPasswordLength => 6;
        public override int MinRequiredNonAlphanumericCharacters => 0;
        public override string PasswordStrengthRegularExpression => "";

        public override void Initialize(string name, NameValueCollection config) => base.Initialize(name ?? "Gate", config);

        public override MembershipUser GetUser(string username, bool userIsOnline) =>
            string.Equals(username, UserName, StringComparison.OrdinalIgnoreCase) ? User() : null;

        public override MembershipUser GetUser(object providerUserKey, bool userIsOnline) =>
            Equals(providerUserKey, 1) ? User() : null;

        public override string GetUserNameByEmail(string email) =>
            string.Equals(email, Email, StringComparison.OrdinalIgnoreCase) ? UserName : null;

        public override string ResetPassword(string username, string answer) => "Reset-" + username + "-42";

        private MembershipUser User() => new MembershipUser(Name, UserName, 1, Email, null, null, true, false,
            new DateTime(2026, 1, 1), DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, DateTime.MinValue);

        public override bool ValidateUser(string username, string password) => false;
        public override bool ChangePassword(string username, string oldPassword, string newPassword) => throw new NotSupportedException();
        public override bool ChangePasswordQuestionAndAnswer(string username, string password, string newPasswordQuestion, string newPasswordAnswer) => throw new NotSupportedException();
        public override MembershipUser CreateUser(string username, string password, string email, string passwordQuestion, string passwordAnswer, bool isApproved, object providerUserKey, out MembershipCreateStatus status) => throw new NotSupportedException();
        public override bool DeleteUser(string username, bool deleteAllRelatedData) => throw new NotSupportedException();
        public override MembershipUserCollection FindUsersByEmail(string emailToMatch, int pageIndex, int pageSize, out int totalRecords) => throw new NotSupportedException();
        public override MembershipUserCollection FindUsersByName(string usernameToMatch, int pageIndex, int pageSize, out int totalRecords) => throw new NotSupportedException();
        public override MembershipUserCollection GetAllUsers(int pageIndex, int pageSize, out int totalRecords) => throw new NotSupportedException();
        public override int GetNumberOfUsersOnline() => 0;
        public override string GetPassword(string username, string answer) => throw new NotSupportedException();
        public override bool UnlockUser(string userName) => throw new NotSupportedException();
        public override void UpdateUser(MembershipUser user) => throw new NotSupportedException();
    }
}
