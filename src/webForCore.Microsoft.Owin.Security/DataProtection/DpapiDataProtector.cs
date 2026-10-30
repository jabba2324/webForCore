// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Security.Cryptography;

namespace Microsoft.Owin.Security.DataProtection
{
    internal class DpapiDataProtector : IDataProtector
    {
        // webForCore: System.Security.Cryptography.DpapiDataProtector is .NET Framework only. ProtectedData makes the same
        // DPAPI call (CurrentUser scope); it works on Windows and throws PlatformNotSupportedException elsewhere. The
        // entropy is a SHA-256 of the application name and purposes, so payloads are not interchangeable with .NET
        // Framework's. On System.Web the host supplies MachineKey data protection instead (OwinAppContext).
        private readonly byte[] _entropy;

        public DpapiDataProtector(string appName, string[] purposes)
        {
            string[] parts = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Concat(
                new[] { appName, "Microsoft.Owin.Security.IDataProtector" }, purposes ?? new string[0]));
            using (SHA256 sha = SHA256.Create())
            {
                _entropy = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(string.Join("\0", parts)));
            }
        }

        public byte[] Protect(byte[] userData)
        {
            return ProtectedData.Protect(userData, _entropy, DataProtectionScope.CurrentUser);
        }

        public byte[] Unprotect(byte[] protectedData)
        {
            return ProtectedData.Unprotect(protectedData, _entropy, DataProtectionScope.CurrentUser);
        }
    }
}
