// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Security.Cryptography;

namespace Microsoft.Owin.Security.DataProtection
{
    internal class DpapiDataProtector : IDataProtector
    {
        // webForCore: System.Security.Cryptography.DpapiDataProtector is .NET Framework only. LocalDataProtection makes the
        // same DPAPI call (CurrentUser scope) on Windows and emulates it elsewhere (a per-user key file; see
        // WebFormsForCore/Security/LocalDataProtection.cs). The entropy is a SHA-256 of the application name and
        // purposes, so payloads are not interchangeable with .NET Framework's. On System.Web the host supplies MachineKey
        // data protection instead (OwinAppContext).
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
            return WebFormsForCore.Security.LocalDataProtection.Protect(userData, _entropy, machineScope: false);
        }

        public byte[] Unprotect(byte[] protectedData)
        {
            return WebFormsForCore.Security.LocalDataProtection.Unprotect(protectedData, _entropy, machineScope: false);
        }
    }
}
