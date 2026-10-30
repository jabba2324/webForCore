// webForCore: added to the lifted Katana tests (not from aspnet/AspNetKatana).
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Owin.Security.DataProtection;

namespace Microsoft.Owin.Security.Tests
{
    // Katana's TestServer supplies no data protection, so the middleware falls back to DPAPI, which exists only on
    // Windows. The System.Web host supplies MachineKey protection instead. These tests are about the middleware, so
    // they get this provider: AES-CBC with HMAC-SHA256, keys derived per purpose from one random key per process.
    internal sealed class TestDataProtectionProvider : IDataProtectionProvider
    {
        private static readonly byte[] MasterKey = RandomNumberGenerator.GetBytes(32);

        public IDataProtector Create(params string[] purposes)
        {
            return new Protector(purposes);
        }

        private sealed class Protector : IDataProtector
        {
            private readonly byte[] _encryptionKey;
            private readonly byte[] _validationKey;

            public Protector(string[] purposes)
            {
                byte[] purpose = Encoding.UTF8.GetBytes(string.Join("\0", purposes ?? new string[0]));
                using (var hmac = new HMACSHA256(MasterKey))
                {
                    _encryptionKey = hmac.ComputeHash(new byte[] { 1 }.Concat(purpose).ToArray());
                    _validationKey = hmac.ComputeHash(new byte[] { 2 }.Concat(purpose).ToArray());
                }
            }

            public byte[] Protect(byte[] userData)
            {
                using (Aes aes = Aes.Create())
                {
                    aes.Key = _encryptionKey;
                    aes.GenerateIV();
                    byte[] cipher = aes.EncryptCbc(userData, aes.IV);
                    byte[] body = aes.IV.Concat(cipher).ToArray();
                    return body.Concat(HMACSHA256.HashData(_validationKey, body)).ToArray();
                }
            }

            public byte[] Unprotect(byte[] protectedData)
            {
                if (protectedData.Length < 16 + 32)
                {
                    throw new CryptographicException("The payload is too short.");
                }
                byte[] body = protectedData.Take(protectedData.Length - 32).ToArray();
                byte[] mac = protectedData.Skip(protectedData.Length - 32).ToArray();
                if (!CryptographicOperations.FixedTimeEquals(mac, HMACSHA256.HashData(_validationKey, body)))
                {
                    throw new CryptographicException("The payload was not protected by this provider.");
                }
                using (Aes aes = Aes.Create())
                {
                    aes.Key = _encryptionKey;
                    return aes.DecryptCbc(body.Skip(16).ToArray(), body.Take(16).ToArray());
                }
            }
        }
    }
}
