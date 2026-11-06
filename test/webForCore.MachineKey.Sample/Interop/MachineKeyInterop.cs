// webForCore (#191): what is protected with the application's <machineKey>, and the check that the other runtime's
// output reads as it. Compiled into the sample (webForCore's System.Web, Katana and Identity) and into
// tools/machinekey/FrameworkMachineKey (.NET Framework 4.8's System.Web and Microsoft's Katana 4.2.3 and Identity 2.2.4
// packages), so both runtimes run the same calls:
//
//   forms-ticket   FormsAuthentication.Encrypt / Decrypt
//   protect        MachineKey.Protect / Unprotect, with and without purposes
//   encode         MachineKey.Encode / Decode (All, Encryption, Validation)
//   owin           Katana's cookie ticket (TicketDataFormat) protected as Microsoft.Owin.Host.SystemWeb protects it: with
//                  MachineKey.Protect and the purposes CookieAuthenticationMiddleware asks for. "Cookies" is a plain
//                  claims identity; "ApplicationCookie" is ASP.NET Identity's (UserManager.CreateIdentityAsync).
//
// On webForCore the owin cookies are also issued and read by the cookie middleware itself, through
// Microsoft.Owin.Host.SystemWeb (the sample's Startup); here they are the same tickets through the same TicketDataFormat.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using System.Web.Security;
using Microsoft.AspNet.Identity;
using Microsoft.Owin.Security;
using Microsoft.Owin.Security.Cookies;
using Microsoft.Owin.Security.DataHandler;
using Microsoft.Owin.Security.DataProtection;

namespace MachineKeyInterop
{
    public static class Interop
    {
        public static readonly byte[] Payload = Encoding.UTF8.GetBytes("webForCore #191: the same <machineKey>, the same bytes. é中");

        public static readonly string[] Purposes = { "MachineKeyInterop", "Purpose one", "Purpose two" };

        public static readonly DateTime IssueDate = new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc);

        // Far ahead, so that the committed .NET Framework cookies stay valid for the cookie middleware.
        public static readonly DateTime Expiration = new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public const string CookiesType = MachineKeyConfigs.CookiesType;

        public const string ApplicationCookieType = MachineKeyConfigs.ApplicationCookieType;

        public static readonly string[] OwinTypes = MachineKeyConfigs.OwinTypes;

        public static FormsAuthenticationTicket Ticket()
        {
            return new FormsAuthenticationTicket(2, "interop-user", IssueDate, Expiration, true, "roles=Admin;issue=191", "/");
        }

        // The data protector Microsoft.Owin.Host.SystemWeb gives the cookie middleware (MachineKeyDataProtector).
        public sealed class MachineKeyProtector : IDataProtector
        {
            private readonly string[] _purposes;

            public MachineKeyProtector(params string[] purposes)
            {
                _purposes = purposes;
            }

            public byte[] Protect(byte[] userData)
            {
                return MachineKey.Protect(userData, _purposes);
            }

            public byte[] Unprotect(byte[] protectedData)
            {
                return MachineKey.Unprotect(protectedData, _purposes);
            }
        }

        // CookieAuthenticationMiddleware's ticket format: app.CreateDataProtector(typeof(CookieAuthenticationMiddleware).FullName,
        // AuthenticationType, "v1").
        public static TicketDataFormat CookieFormat(string authenticationType)
        {
            return new TicketDataFormat(new MachineKeyProtector(typeof(CookieAuthenticationMiddleware).FullName, authenticationType, "v1"));
        }

        public static AuthenticationProperties OwinProperties()
        {
            var properties = new AuthenticationProperties
            {
                IssuedUtc = new DateTimeOffset(IssueDate),
                ExpiresUtc = new DateTimeOffset(Expiration),
                IsPersistent = true,
            };
            properties.Dictionary["interop"] = "191";
            return properties;
        }

        public static ClaimsIdentity OwinIdentity(string authenticationType)
        {
            if (authenticationType == ApplicationCookieType)
            {
                var manager = new UserManager<InteropUser>(new InteropUserStore());
                return Task.Run(() => manager.CreateIdentityAsync(InteropUserStore.User, authenticationType)).GetAwaiter().GetResult();
            }
            var identity = new ClaimsIdentity(authenticationType, ClaimTypes.Name, ClaimTypes.Role);
            identity.AddClaim(new Claim(ClaimTypes.Name, "interop-user"));
            identity.AddClaim(new Claim(ClaimTypes.Role, "Admin"));
            identity.AddClaim(new Claim("urn:webforcore:issue", "191", ClaimValueTypes.Integer32, "interop-issuer"));
            return identity;
        }

        // What the cookie middleware read from a cookie, against the identity and properties that were protected. The
        // properties' times are checked for the plain cookie only: ASP.NET Identity's SecurityStampValidator signs the
        // identity in again when it validates it.
        public static string CheckOwin(string authenticationType, ClaimsIdentity identity, AuthenticationProperties properties)
        {
            if (identity == null) return "no identity";
            ClaimsIdentity expected = OwinIdentity(authenticationType);
            if (identity.AuthenticationType != authenticationType) return "authentication type " + identity.AuthenticationType;
            string actualClaims = Claims(identity), expectedClaims = Claims(expected);
            if (actualClaims != expectedClaims) return "claims " + actualClaims + ", expected " + expectedClaims;
            if (authenticationType == CookiesType)
            {
                if (properties == null) return "no properties";
                if (properties.IssuedUtc != new DateTimeOffset(IssueDate)) return "IssuedUtc " + properties.IssuedUtc;
                if (properties.ExpiresUtc != new DateTimeOffset(Expiration)) return "ExpiresUtc " + properties.ExpiresUtc;
                if (!properties.IsPersistent) return "not persistent";
                string interop;
                if (!properties.Dictionary.TryGetValue("interop", out interop) || interop != "191") return "property interop=" + interop;
            }
            return null;
        }

        private static string Claims(ClaimsIdentity identity)
        {
            return string.Join("; ", identity.Claims.Select(c => c.Type + "=" + c.Value + " (" + c.ValueType + ", " + c.Issuer + ")"));
        }

        // This runtime's outputs for the application's <machineKey>: each protected twice, to see whether the output is
        // deterministic (a random IV makes most differ). A call that throws gives "error: <exception type>" (both
        // runtimes must fail alike: MachineKey.Protect, and so Katana's cookies, reject a key with IsolateApps).
        public static List<InteropEntry> Produce(string config, bool owin)
        {
            var entries = new List<InteropEntry>();
            foreach (string[] output in Outputs(owin))
            {
                Func<string> protect = Protector(output[0], output[1]);
                string first = Run(protect), second = Run(protect);
                entries.Add(new InteropEntry { Config = config, Kind = output[0], Case = output[1], Value = first, Deterministic = first == second });
            }
            return entries;
        }

        private static IEnumerable<string[]> Outputs(bool owin)
        {
            yield return new[] { "forms-ticket", "ticket" };
            yield return new[] { "protect", "purposes" };
            yield return new[] { "protect", "no-purposes" };
#pragma warning disable 618 // MachineKey.Encode and Decode are obsolete
            foreach (MachineKeyProtection protection in new[] { MachineKeyProtection.All, MachineKeyProtection.Encryption, MachineKeyProtection.Validation })
            {
                yield return new[] { "encode", protection.ToString() };
            }
            if (owin)
            {
                foreach (string type in OwinTypes)
                {
                    yield return new[] { "owin", type };
                }
            }
        }

        private static Func<string> Protector(string kind, string name)
        {
            switch (kind)
            {
                case "forms-ticket":
                    return () => FormsAuthentication.Encrypt(Ticket());
                case "protect":
                    return () => Convert.ToBase64String(MachineKey.Protect(Payload, name == "purposes" ? Purposes : null));
                case "encode":
                    return () => MachineKey.Encode(Payload, (MachineKeyProtection)Enum.Parse(typeof(MachineKeyProtection), name));
                case "owin":
                    return () => CookieFormat(name).Protect(new AuthenticationTicket(OwinIdentity(name), OwinProperties()));
                default:
                    throw new ArgumentException(kind, nameof(kind));
            }
        }
#pragma warning restore 618

        public static string Error(Exception e)
        {
            return "error: " + e.GetType().FullName;
        }

        private static string Run(Func<string> protect)
        {
            try
            {
                return protect();
            }
            catch (Exception e)
            {
                return Error(e);
            }
        }

        // Reads the other runtime's outputs for this <machineKey> (owin entries too when owin is true). Where the other
        // runtime failed, this one must fail with the same exception.
        public static List<InteropResult> Consume(IEnumerable<InteropEntry> entries, bool owin)
        {
            var results = new List<InteropResult>();
            foreach (InteropEntry entry in entries)
            {
                if (entry.Kind == "owin" && !owin) continue;
                string problem;
                if (entry.Value.StartsWith("error: ", StringComparison.Ordinal))
                {
                    string mine = Run(Protector(entry.Kind, entry.Case));
                    problem = mine == entry.Value ? null : "the other runtime gave " + entry.Value + ", this one " + (mine.StartsWith("error: ", StringComparison.Ordinal) ? mine : "output");
                }
                else
                {
                    try
                    {
                        problem = Check(entry);
                    }
                    catch (Exception e)
                    {
                        problem = e.GetType().Name + ": " + e.Message;
                    }
                }
                results.Add(new InteropResult { Config = entry.Config, Kind = entry.Kind, Case = entry.Case, Problem = problem });
            }
            return results;
        }

        private static string Check(InteropEntry entry)
        {
            switch (entry.Kind)
            {
                case "forms-ticket":
                    return CheckTicket(FormsAuthentication.Decrypt(entry.Value));
                case "protect":
                    byte[] unprotected = MachineKey.Unprotect(Convert.FromBase64String(entry.Value), entry.Case == "purposes" ? Purposes : null);
                    return CheckPayload(unprotected);
#pragma warning disable 618
                case "encode":
                    return CheckPayload(MachineKey.Decode(entry.Value, (MachineKeyProtection)Enum.Parse(typeof(MachineKeyProtection), entry.Case)));
#pragma warning restore 618
                case "owin":
                    AuthenticationTicket ticket = CookieFormat(entry.Case).Unprotect(entry.Value);
                    return ticket == null ? "TicketDataFormat.Unprotect returned null" : CheckOwin(entry.Case, ticket.Identity, ticket.Properties);
                default:
                    return "unknown kind " + entry.Kind;
            }
        }

        private static string CheckTicket(FormsAuthenticationTicket ticket)
        {
            if (ticket == null) return "FormsAuthentication.Decrypt returned null";
            FormsAuthenticationTicket expected = Ticket();
            if (ticket.Version != expected.Version) return "Version " + ticket.Version;
            if (ticket.Name != expected.Name) return "Name " + ticket.Name;
            if (ticket.IssueDate.ToUniversalTime() != IssueDate) return "IssueDate " + ticket.IssueDate.ToUniversalTime().ToString("o");
            if (ticket.Expiration.ToUniversalTime() != Expiration) return "Expiration " + ticket.Expiration.ToUniversalTime().ToString("o");
            if (ticket.IsPersistent != expected.IsPersistent) return "IsPersistent " + ticket.IsPersistent;
            if (ticket.UserData != expected.UserData) return "UserData " + ticket.UserData;
            if (ticket.CookiePath != expected.CookiePath) return "CookiePath " + ticket.CookiePath;
            return null;
        }

        private static string CheckPayload(byte[] data)
        {
            if (data == null) return "null";
            return data.SequenceEqual(Payload) ? null : "different data: " + Convert.ToBase64String(data);
        }
    }

    public sealed class InteropUser : IUser<string>
    {
        public string Id { get; set; }
        public string UserName { get; set; }
        public string SecurityStamp { get; set; }
    }

    // A user store with the one user, and its security stamp (ASP.NET Identity adds it to the identity, and
    // SecurityStampValidator checks it).
    public sealed class InteropUserStore : IUserStore<InteropUser>, IUserSecurityStampStore<InteropUser>
    {
        public static readonly InteropUser User = new InteropUser { Id = "191", UserName = "interop-user", SecurityStamp = "8e2f6a3c-191-stamp" };

        public Task CreateAsync(InteropUser user) { throw new NotSupportedException(); }
        public Task UpdateAsync(InteropUser user) { return Task.FromResult(0); }
        public Task DeleteAsync(InteropUser user) { throw new NotSupportedException(); }
        public Task<InteropUser> FindByIdAsync(string userId) { return Task.FromResult(userId == User.Id ? User : null); }
        public Task<InteropUser> FindByNameAsync(string userName) { return Task.FromResult(userName == User.UserName ? User : null); }
        public Task SetSecurityStampAsync(InteropUser user, string stamp) { throw new NotSupportedException(); }
        public Task<string> GetSecurityStampAsync(InteropUser user) { return Task.FromResult(user.SecurityStamp); }
        public void Dispose() { }
    }
}
