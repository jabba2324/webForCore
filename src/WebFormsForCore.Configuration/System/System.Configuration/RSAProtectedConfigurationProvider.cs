//------------------------------------------------------------------------------
// <copyright file="RsaProtectedConfigurationProvider.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

#if NET9_0_OR_GREATER

namespace System.Configuration
{
    using System.Collections.Specialized;
    using System.Runtime.Serialization;
    using System.Configuration.Provider;
    using System.Xml;
    using System.Security;
    using System.Security.Cryptography;
    using System.Security.Cryptography.Xml;
    using System.Linq;
    using System.IO;
    using System.Runtime.InteropServices;
    using Microsoft.Win32;
    using System.Security.Permissions;
    using System.Diagnostics.CodeAnalysis;

    [PermissionSet(SecurityAction.Demand, Name = "FullTrust")]
    public sealed class RsaProtectedConfigurationProvider : ProtectedConfigurationProvider
    {
        // Note: this name has to match the name used in RegiisUtility 
        const string DefaultRsaKeyContainerName = "NetFrameworkConfigurationKey";

        public override XmlNode Decrypt(XmlNode encryptedNode)
        {
            XmlDocument                 xmlDocument = new XmlDocument();
            EncryptedXml                exml        = null;
            RSACryptoServiceProvider    rsa         = GetCryptoServiceProvider(false, true);

            xmlDocument.PreserveWhitespace = true;
            ProtectedConfigurationProvider.LoadXml(xmlDocument, encryptedNode.OuterXml);
            exml = new FipsAwareEncryptedXml(xmlDocument);
            exml.AddKeyNameMapping(_KeyName, rsa);
            exml.DecryptDocument();
            rsa.Clear();
            return xmlDocument.DocumentElement;
        }

        public override XmlNode Encrypt(XmlNode node)
        {
            XmlDocument         xmlDocument;
            EncryptedXml        exml;
            byte[]              rgbOutput;
            EncryptedData       ed;
            KeyInfoName         kin;
            EncryptedKey        ek;
            KeyInfoEncryptedKey kek;
            XmlElement          inputElement;
            RSACryptoServiceProvider rsa = GetCryptoServiceProvider(false, false);

            // Encrypt the node with the new key
            xmlDocument = new XmlDocument();
            xmlDocument.PreserveWhitespace = true;
            ProtectedConfigurationProvider.LoadXml(xmlDocument, "<foo>" + node.OuterXml + "</foo>");
            exml = new EncryptedXml(xmlDocument);
            inputElement = xmlDocument.DocumentElement;

            using (SymmetricAlgorithm symAlg = GetSymAlgorithmProvider()) {
                rgbOutput = exml.EncryptData(inputElement, symAlg, true);
                ed = new EncryptedData();
                ed.Type = EncryptedXml.XmlEncElementUrl;
                ed.EncryptionMethod = GetSymEncryptionMethod();
                ed.KeyInfo = new KeyInfo();

                ek = new EncryptedKey();
                ek.EncryptionMethod = new EncryptionMethod(UseOAEP ? EncryptedXml.XmlEncRSAOAEPUrl : EncryptedXml.XmlEncRSA15Url);
                ek.KeyInfo = new KeyInfo();
                ek.CipherData = new CipherData();
                ek.CipherData.CipherValue = EncryptedXml.EncryptKey(symAlg.Key, rsa, UseOAEP);
            }

            kin = new KeyInfoName();
            kin.Value = _KeyName;
            ek.KeyInfo.AddClause(kin);
            kek = new KeyInfoEncryptedKey(ek);
            ed.KeyInfo.AddClause(kek);
            ed.CipherData = new CipherData();
            ed.CipherData.CipherValue = rgbOutput;
            EncryptedXml.ReplaceElement(inputElement, ed, true);

            rsa.Clear();

                // Get node from the document
            foreach (XmlNode node2 in xmlDocument.ChildNodes)
                if (node2.NodeType == XmlNodeType.Element)
                    foreach (XmlNode node3 in node2.ChildNodes) // node2 is the "foo" node
                        if (node3.NodeType == XmlNodeType.Element)
                            return node3; // node3 is the "EncryptedData" node
                return null;

        }

        public void AddKey(int keySize, bool exportable)
        {
            if (!OperatingSystem.IsWindows()) { KeyFiles.Add(this, keySize, exportable); return; }
            RSACryptoServiceProvider rsa = GetCryptoServiceProvider(exportable, false);
            rsa.KeySize = keySize;
            rsa.PersistKeyInCsp = true;
            rsa.Clear();
        }

        public void DeleteKey()
        {
            if (!OperatingSystem.IsWindows()) { KeyFiles.Delete(this); return; }
            RSACryptoServiceProvider rsa = GetCryptoServiceProvider(false, true);
            rsa.PersistKeyInCsp = false;
            rsa.Clear();
        }
        public void ImportKey(string xmlFileName, bool exportable)
        {
            if (!OperatingSystem.IsWindows()) { KeyFiles.Import(this, File.ReadAllText(xmlFileName), exportable); return; }
            RSACryptoServiceProvider rsa = GetCryptoServiceProvider(exportable, false);
            rsa.FromXmlString(File.ReadAllText(xmlFileName));
            rsa.PersistKeyInCsp = true;
            rsa.Clear();
        }

        public void ExportKey(string xmlFileName, bool includePrivateParameters)
        {
            if (!OperatingSystem.IsWindows()) { File.WriteAllText(xmlFileName, KeyFiles.Export(this, includePrivateParameters)); return; }
            RSACryptoServiceProvider rsa = GetCryptoServiceProvider(false, false);
            string xmlString = rsa.ToXmlString(includePrivateParameters);
            File.WriteAllText(xmlFileName, xmlString);
            rsa.Clear();
        }

        public string   KeyContainerName    { get { return _KeyContainerName; } }
        public string   CspProviderName     { get { return _CspProviderName; } }
        public bool     UseMachineContainer { get { return _UseMachineContainer; } }
        public bool UseOAEP { get { return _UseOAEP; } }
        public bool UseFIPS { get { return _UseFIPS; } }

        public override void Initialize(string name, NameValueCollection configurationValues)
        {
            base.Initialize(name, configurationValues);

            _KeyName = "Rsa Key";
            _KeyContainerName = configurationValues["keyContainerName"];
            configurationValues.Remove("keyContainerName");
            if (_KeyContainerName == null || _KeyContainerName.Length < 1)
                _KeyContainerName = DefaultRsaKeyContainerName;

            _CspProviderName = configurationValues["cspProviderName"];
            configurationValues.Remove("cspProviderName");
            _UseMachineContainer = GetBooleanValue(configurationValues, "useMachineContainer", true);
            _UseOAEP = GetBooleanValue(configurationValues, "useOAEP", true);
            _UseFIPS = GetBooleanValue(configurationValues, "useFIPS", true);
            if (configurationValues.Count > 0)
                throw new ConfigurationErrorsException(SR.GetString(SR.Unrecognized_initialization_value, configurationValues.GetKey(0)));
        }


        private string _KeyName;
        private string _KeyContainerName;
        private string _CspProviderName;
        private bool _UseMachineContainer;
        private bool _UseOAEP;
        private bool _UseFIPS;

        public RSAParameters RsaPublicKey { get { return GetCryptoServiceProvider(false, false).ExportParameters(false); } }

        private RSACryptoServiceProvider GetCryptoServiceProvider(bool exportable, bool keyMustExist)
        {
            if (!OperatingSystem.IsWindows())
                return KeyFiles.Open(this, exportable, keyMustExist);
            try {
                CspParameters csp = new CspParameters();
                csp.KeyContainerName = KeyContainerName;
                csp.KeyNumber = 1;
                csp.ProviderType = 1; // Dev10 Bug #548719: Explicitly require "RSA Full (Signature and Key Exchange)"

                if (CspProviderName != null && CspProviderName.Length > 0)
                    csp.ProviderName = CspProviderName;

                if (UseMachineContainer)
                    csp.Flags |= CspProviderFlags.UseMachineKeyStore;
                if (!exportable && !keyMustExist)
                    csp.Flags |= CspProviderFlags.UseNonExportableKey;
                if (keyMustExist)
                    csp.Flags |= CspProviderFlags.UseExistingKey;

                return new RSACryptoServiceProvider(2048, csp);

            } catch {
                ThrowBetterException(keyMustExist);

                // If a better exception can't be found, this will propagate
                // the original one
                throw;
            }
        }

        private void ThrowBetterException(bool keyMustExist)
        {
            SafeCryptContextHandle hProv = null;
            int success = 0;
            try {
                success = UnsafeNativeMethods.CryptAcquireContext(out hProv, KeyContainerName, CspProviderName, PROV_Rsa_FULL, UseMachineContainer ? CRYPT_MACHINE_KEYSET : 0);
                if (success != 0)
                    return; // propagate original exception

                int hr = Marshal.GetHRForLastWin32Error();
                if (hr == HResults.NteBadKeySet && !keyMustExist) {
                    return; // propagate original exception
                }

                switch (hr) {
                    case HResults.NteBadKeySet:
                    case HResults.Win32AccessDenied:
                    case HResults.Win32InvalidHandle:
                        throw new ConfigurationErrorsException(SR.GetString(SR.Key_container_doesnt_exist_or_access_denied));

                    default:
                        Marshal.ThrowExceptionForHR(hr);
                        break;
                }
            } finally {
                if (!(hProv == null || hProv.IsInvalid))
                    hProv.Dispose();
            }
        }
        // webForCore: CAPI key containers are Windows-only. Elsewhere a container is a file in the key directory of its
        // scope (useMachineContainer: machine; see WebFormsForCore.Security.LocalDataProtection.KeyDirectory), named
        // after the container, holding the key in the XML of aspnet_regiis -px (an exported Windows container can be
        // copied in, or imported with ImportKey). As with CAPI, encrypting with a container that does not exist creates
        // it (2048-bit), and a non-exportable key's private part cannot be exported.
        static class KeyFiles
        {
            static string FileOf(RsaProtectedConfigurationProvider p, bool create)
            {
                var name = string.Concat(p.KeyContainerName.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' ? c.ToString() : "%" + ((int)c).ToString("X2")));
                return Path.Combine(WebFormsForCore.Security.LocalDataProtection.KeyDirectory(p.UseMachineContainer, create), name + ".rsakey.xml");
            }

            static (string Xml, bool Exportable)? Read(RsaProtectedConfigurationProvider p)
            {
                var file = FileOf(p, create: false);
                if (!File.Exists(file)) return null;
                var document = new XmlDocument();
                document.Load(file);
                var root = document.DocumentElement;
                if (root.Name == "RSAKeyValue") return (root.OuterXml, true); // as aspnet_regiis -px writes it
                return (root["RSAKeyValue"].OuterXml, root.GetAttribute("exportable") != "false");
            }

            static void Write(RsaProtectedConfigurationProvider p, string keyXml, bool exportable)
            {
                var file = FileOf(p, create: true);
                var content = "<KeyContainer name=\"" + System.Security.SecurityElement.Escape(p.KeyContainerName) + "\" exportable=\"" + (exportable ? "true" : "false") + "\">" + keyXml + "</KeyContainer>";
                File.Delete(file);
                WebFormsForCore.Security.LocalDataProtection.ReadOrCreate(file, () => System.Text.Encoding.UTF8.GetBytes(content), b => b.Length > 0);
            }

            public static RSACryptoServiceProvider Open(RsaProtectedConfigurationProvider p, bool exportable, bool keyMustExist)
            {
                var key = Read(p);
                var rsa = new RSACryptoServiceProvider();
                if (key == null) {
                    if (keyMustExist)
                        throw new ConfigurationErrorsException(SR.GetString(SR.Key_container_doesnt_exist_or_access_denied) + " (" + FileOf(p, create: false) + ")");
                    rsa = new RSACryptoServiceProvider(2048);
                    Write(p, rsa.ToXmlString(true), exportable);
                    return rsa;
                }
                rsa.FromXmlString(key.Value.Xml);
                return rsa;
            }

            public static void Add(RsaProtectedConfigurationProvider p, int keySize, bool exportable)
            {
                if (Read(p) != null) return; // as CAPI: an existing container keeps its key
                using var rsa = new RSACryptoServiceProvider(keySize);
                Write(p, rsa.ToXmlString(true), exportable);
            }

            public static void Delete(RsaProtectedConfigurationProvider p)
            {
                var file = FileOf(p, create: false);
                if (!File.Exists(file)) throw new ConfigurationErrorsException(SR.GetString(SR.Key_container_doesnt_exist_or_access_denied) + " (" + file + ")");
                File.Delete(file);
            }

            public static void Import(RsaProtectedConfigurationProvider p, string xml, bool exportable)
            {
                using var rsa = new RSACryptoServiceProvider();
                rsa.FromXmlString(xml); // validates it
                Write(p, rsa.ToXmlString(!rsa.PublicOnly), exportable);
            }

            public static string Export(RsaProtectedConfigurationProvider p, bool includePrivateParameters)
            {
                var key = Read(p) ?? throw new ConfigurationErrorsException(SR.GetString(SR.Key_container_doesnt_exist_or_access_denied) + " (" + FileOf(p, create: false) + ")");
                if (includePrivateParameters && !key.Exportable) throw new CryptographicException("Key not valid for use in specified state.");
                using var rsa = new RSACryptoServiceProvider();
                rsa.FromXmlString(key.Xml);
                return rsa.ToXmlString(includePrivateParameters);
            }
        }

        const uint PROV_Rsa_FULL = 1;
        const uint CRYPT_MACHINE_KEYSET = 0x00000020;

        private static bool GetBooleanValue(NameValueCollection configurationValues, string valueName, bool defaultValue) {
            string s = configurationValues[valueName];
            if (s == null)
                return defaultValue;
            configurationValues.Remove(valueName);
            if (s == "true")
                return true;
            if (s == "false")
                return false;
            throw new ConfigurationErrorsException(SR.GetString(SR.Config_invalid_boolean_attribute, valueName));
        }

        [SuppressMessage("Microsoft.Cryptographic.Standard", "CA5353:TripleDESCannotBeUsed", Justification = @"AES is the default. For legacy compat, 3DES must be explicitly opted into.")]
        private SymmetricAlgorithm GetSymAlgorithmProvider() {
            SymmetricAlgorithm symAlg;

            if (UseFIPS) {
                // AesCryptoServiceProvider implementation is FIPS certified
                symAlg = new AesCryptoServiceProvider();
            }
            else {
                // Use the 3DES. FIPS obsolated 3DES
                symAlg = new TripleDESCryptoServiceProvider();
            }

            return symAlg;
        }

        private EncryptionMethod GetSymEncryptionMethod() {
            return UseFIPS ? new EncryptionMethod(EncryptedXml.XmlEncAES256Url) :
                             new EncryptionMethod(EncryptedXml.XmlEncTripleDESUrl);
        }
    }
}
#endif