// webForCore: System.Security.Cryptography.DataProtector and DpapiDataProtector, which .NET does not have (they were in
// .NET Framework's System.Security.dll). Licensed under the MIT license; written from the documented public API, replacing
// a decompiled copy (docs/LICENSING.md). System.Web uses them for <machineKey dataProtectorType="...">.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace System.Security.Cryptography
{
	// The base class for data protectors: protection bound to an application name and purposes. Protect prepends a hash of
	// the purposes to the data (unless PrependHashedPurposeToPlaintext is false) and Unprotect checks it, so data protected
	// for one purpose cannot be unprotected for another.
	public abstract class DataProtector
	{
		private readonly string[] _specificPurposes;
		private byte[] _hashedPurpose;

		protected DataProtector(string applicationName, string primaryPurpose, string[] specificPurposes)
		{
			if (string.IsNullOrWhiteSpace(applicationName))
				throw new ArgumentException("The application name must be a non-empty string.", nameof(applicationName));
			if (string.IsNullOrWhiteSpace(primaryPurpose))
				throw new ArgumentException("The primary purpose must be a non-empty string.", nameof(primaryPurpose));
			if (specificPurposes != null && specificPurposes.Any(string.IsNullOrWhiteSpace))
				throw new ArgumentException("Each specific purpose must be a non-empty string.", nameof(specificPurposes));
			ApplicationName = applicationName;
			PrimaryPurpose = primaryPurpose;
			_specificPurposes = specificPurposes?.ToArray() ?? Array.Empty<string>();
		}

		protected string ApplicationName { get; }

		protected string PrimaryPurpose { get; }

		protected IEnumerable<string> SpecificPurposes => _specificPurposes;

		protected virtual bool PrependHashedPurposeToPlaintext => true;

		// Creates the protector named by providerClass: a name CryptoConfig knows, or a type name (the application's
		// assemblies included), constructed with (applicationName, primaryPurpose, specificPurposes).
		public static DataProtector Create(string providerClass, string applicationName, string primaryPurpose, params string[] specificPurposes)
		{
			if (providerClass == null)
				throw new ArgumentNullException(nameof(providerClass));
			object[] args = { applicationName, primaryPurpose, specificPurposes };
			if (IsDpapiDataProtector(providerClass))
				return new DpapiDataProtector(applicationName, primaryPurpose, specificPurposes);
			object created = CryptoConfig.CreateFromName(providerClass, args);
			if (created == null)
			{
				Type type = System.Web.Compilation.BuildManager.GetType(providerClass, throwOnError: false);
				if (type != null)
					created = Activator.CreateInstance(type, args);
			}
			return created as DataProtector
				?? throw new CryptographicException($"'{providerClass}' is not a DataProtector type that can be created.");
		}

		// .NET Framework's System.Security.Cryptography.DpapiDataProtector, by short or assembly-qualified name.
		private static bool IsDpapiDataProtector(string providerClass)
		{
			string name = providerClass.Split(',')[0].Trim();
			return name == "DpapiDataProtector" || name == "System.Security.Cryptography.DpapiDataProtector";
		}

		// SHA-256 over the application name and purposes, each written length-prefixed in UTF-8.
		protected virtual byte[] GetHashedPurpose()
		{
			if (_hashedPurpose == null)
			{
				using var stream = new MemoryStream();
				using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
				{
					writer.Write(ApplicationName);
					writer.Write(PrimaryPurpose);
					foreach (string purpose in _specificPurposes)
						writer.Write(purpose);
				}
				_hashedPurpose = SHA256.HashData(stream.ToArray());
			}
			return _hashedPurpose;
		}

		public abstract bool IsReprotectRequired(byte[] encryptedData);

		public byte[] Protect(byte[] userData)
		{
			if (userData == null)
				throw new ArgumentNullException(nameof(userData));
			if (!PrependHashedPurposeToPlaintext)
				return ProviderProtect(userData);
			byte[] hash = GetHashedPurpose();
			byte[] data = new byte[hash.Length + userData.Length];
			Buffer.BlockCopy(hash, 0, data, 0, hash.Length);
			Buffer.BlockCopy(userData, 0, data, hash.Length, userData.Length);
			return ProviderProtect(data);
		}

		public byte[] Unprotect(byte[] encryptedData)
		{
			if (encryptedData == null)
				throw new ArgumentNullException(nameof(encryptedData));
			byte[] data = ProviderUnprotect(encryptedData);
			if (!PrependHashedPurposeToPlaintext)
				return data;
			byte[] hash = GetHashedPurpose();
			if (data.Length < hash.Length || !CryptographicOperations.FixedTimeEquals(data.AsSpan(0, hash.Length), hash))
				throw new CryptographicException("The data was not protected for this application name and purpose.");
			return data.AsSpan(hash.Length).ToArray();
		}

		protected abstract byte[] ProviderProtect(byte[] userData);

		protected abstract byte[] ProviderUnprotect(byte[] encryptedData);
	}

	// DPAPI protection, as .NET Framework's DpapiDataProtector: ProtectedData on Windows, webForCore's DPAPI emulation
	// (WebFormsForCore.Security.LocalDataProtection, its key files) elsewhere. The purposes are bound through the hashed
	// purpose, not DPAPI entropy.
	public sealed class DpapiDataProtector : DataProtector
	{
		public DpapiDataProtector(string appName, string primaryPurpose, params string[] specificPurpose)
			: base(appName, primaryPurpose, specificPurpose)
		{
		}

		public DataProtectionScope Scope { get; set; } = DataProtectionScope.CurrentUser;

		public override bool IsReprotectRequired(byte[] encryptedData) => false;

		protected override byte[] ProviderProtect(byte[] userData) =>
			WebFormsForCore.Security.LocalDataProtection.Protect(userData, null, Scope == DataProtectionScope.LocalMachine);

		protected override byte[] ProviderUnprotect(byte[] encryptedData) =>
			WebFormsForCore.Security.LocalDataProtection.Unprotect(encryptedData, null, Scope == DataProtectionScope.LocalMachine);
	}
}
