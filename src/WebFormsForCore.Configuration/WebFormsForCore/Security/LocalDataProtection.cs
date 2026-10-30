// webForCore: DPAPI and CAPI key containers where Windows has them and Linux does not.
// Licensed under the MIT license. This file is compiled into System.Configuration (public) and, copied by
// tools/lift/lift_project.py, into Microsoft.Owin.Security (internal); both read the same key files.
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace WebFormsForCore.Security
{
	// DPAPI (CryptProtectData, ProtectedData) on Windows. Elsewhere, its emulation: data is encrypted with a master key
	// kept in a file, one per scope, as DPAPI keeps one per machine and per user:
	//   LocalMachine: $WEBFORCORE_KEYS/machine.dpapi, else /var/lib/webforcore/keys/machine.dpapi (shared by the
	//                 machine's apps; create the directory for them), else the user's directory below;
	//   CurrentUser:  $WEBFORCORE_KEYS/user.dpapi, else ~/.webforcore/keys/user.dpapi.
	// A key is created (32 random bytes, readable by its owner only) the first time it is needed. Each payload is
	// AES-256-CBC with an HMAC-SHA256, both keyed (HKDF) from the master key, a random salt and the entropy, so the
	// wrong entropy or key fails as DPAPI does. Windows DPAPI blobs cannot be read here: their keys never leave the
	// machine (or user profile) that made them.
#if SYSTEM_CONFIGURATION
	public
#else
	internal
#endif
	static class LocalDataProtection
	{
		static readonly byte[] Magic = Encoding.ASCII.GetBytes("WFCDPAPI1");
		// DPAPI blobs start with dwVersion 1 and the provider GUID df9d8cd0-1501-11d1-8c7a-00c04fc297eb.
		static readonly byte[] WindowsBlob = { 1, 0, 0, 0, 0xd0, 0x8c, 0x9d, 0xdf, 0x01, 0x15, 0xd1, 0x11, 0x8c, 0x7a, 0x00, 0xc0, 0x4f, 0xc2, 0x97, 0xeb };

		public static bool IsWindowsDpapiBlob(byte[] data) => data != null && data.Length > WindowsBlob.Length && data.Take(WindowsBlob.Length).SequenceEqual(WindowsBlob);

		public static byte[] Protect(byte[] userData, byte[] entropy, bool machineScope)
		{
			if (userData == null) throw new ArgumentNullException(nameof(userData));
#pragma warning disable CA1416
			if (OperatingSystem.IsWindows()) return ProtectedData.Protect(userData, entropy, machineScope ? DataProtectionScope.LocalMachine : DataProtectionScope.CurrentUser);
#pragma warning restore CA1416
			var salt = RandomNumberGenerator.GetBytes(16);
			var (encryption, mac) = Keys(MasterKey(machineScope), salt, entropy);
			using var aes = Aes.Create();
			aes.Key = encryption;
			aes.GenerateIV();
			var cipher = aes.EncryptCbc(userData, aes.IV);
			var body = Magic.Concat(new[] { (byte)(machineScope ? 1 : 0) }).Concat(salt).Concat(aes.IV).Concat(cipher).ToArray();
			return body.Concat(HMACSHA256.HashData(mac, body)).ToArray();
		}

		public static byte[] Unprotect(byte[] protectedData, byte[] entropy, bool machineScope)
		{
			if (protectedData == null) throw new ArgumentNullException(nameof(protectedData));
#pragma warning disable CA1416
			if (OperatingSystem.IsWindows()) return ProtectedData.Unprotect(protectedData, entropy, machineScope ? DataProtectionScope.LocalMachine : DataProtectionScope.CurrentUser);
#pragma warning restore CA1416
			if (IsWindowsDpapiBlob(protectedData))
				throw new CryptographicException("The data was protected with Windows DPAPI, whose keys stay on the machine (or user profile) that protected it. Unprotect it there and protect it again here, or use a portable key (RsaProtectedConfigurationProvider with an exported container, the machine key).");
			int header = Magic.Length + 1 + 16 + 16;
			if (protectedData.Length < header + 16 + 32 || !protectedData.Take(Magic.Length).SequenceEqual(Magic))
				throw new CryptographicException("The data is not protected data.");
			bool scope = protectedData[Magic.Length] == 1;
			var salt = protectedData.AsSpan(Magic.Length + 1, 16).ToArray();
			var iv = protectedData.AsSpan(Magic.Length + 17, 16).ToArray();
			var (encryption, mac) = Keys(MasterKey(scope), salt, entropy);
			var body = protectedData.AsSpan(0, protectedData.Length - 32);
			if (!CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(mac, body), protectedData.AsSpan(protectedData.Length - 32)))
				throw new CryptographicException("Key not valid for use in specified state."); // DPAPI's message for the wrong key or entropy
			using var aes = Aes.Create();
			aes.Key = encryption;
			return aes.DecryptCbc(protectedData.AsSpan(header, protectedData.Length - header - 32), iv);
		}

		static (byte[] Encryption, byte[] Mac) Keys(byte[] master, byte[] salt, byte[] entropy)
		{
			var info = Encoding.ASCII.GetBytes("webForCore DPAPI").Concat(entropy ?? Array.Empty<byte>()).ToArray();
			var keys = HKDF.DeriveKey(HashAlgorithmName.SHA256, master, 64, salt, info);
			return (keys.Take(32).ToArray(), keys.Skip(32).ToArray());
		}

		static byte[] MasterKey(bool machineScope)
		{
			var file = Path.Combine(KeyDirectory(machineScope, create: true), machineScope ? "machine.dpapi" : "user.dpapi");
			return ReadOrCreate(file, () => RandomNumberGenerator.GetBytes(32), bytes => bytes.Length == 32);
		}

		// The directory for a scope's keys (DPAPI master keys and RSA key containers).
		public static string KeyDirectory(bool machineScope, bool create)
		{
			var configured = Environment.GetEnvironmentVariable("WEBFORCORE_KEYS");
			if (!string.IsNullOrEmpty(configured)) return Ensure(configured, create) ?? configured;
			if (machineScope && Ensure("/var/lib/webforcore/keys", create) is string machine) return machine;
			var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			var user = Path.Combine(string.IsNullOrEmpty(home) ? Path.GetTempPath() : home, ".webforcore", "keys");
			return Ensure(user, create) ?? user;
		}

		static string Ensure(string directory, bool create)
		{
			try
			{
				if (Directory.Exists(directory)) return IsWritable(directory) || !create ? directory : null;
				if (!create) return null;
				Directory.CreateDirectory(directory);
				if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
				return directory;
			}
			catch (Exception e) when (e is UnauthorizedAccessException || e is IOException)
			{
				return null;
			}
		}

		static bool IsWritable(string directory)
		{
			try
			{
				var probe = Path.Combine(directory, ".webforcore-" + Guid.NewGuid().ToString("N"));
				File.WriteAllBytes(probe, Array.Empty<byte>());
				File.Delete(probe);
				return true;
			}
			catch (Exception e) when (e is UnauthorizedAccessException || e is IOException)
			{
				// read-only to this user: its existing keys can still be read
				return Directory.EnumerateFileSystemEntries(directory).Any();
			}
		}

		// Reads a key file, or creates it (exclusively, so concurrent first uses agree on one key), owner-only.
		public static byte[] ReadOrCreate(string file, Func<byte[]> create, Func<byte[], bool> valid)
		{
			for (int attempt = 0; attempt < 20; attempt++)
			{
				if (File.Exists(file))
				{
					var existing = File.ReadAllBytes(file);
					if (valid(existing)) return existing;
					System.Threading.Thread.Sleep(50); // being written by another process
					continue;
				}
				var bytes = create();
				var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
				using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
				{
					if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
					stream.Write(bytes, 0, bytes.Length);
				}
				try
				{
					File.Move(temporary, file, overwrite: false);
					return bytes;
				}
				catch (IOException)
				{
					File.Delete(temporary); // another process created it first
				}
			}
			throw new CryptographicException("The key file " + file + " could not be read.");
		}
	}
}
