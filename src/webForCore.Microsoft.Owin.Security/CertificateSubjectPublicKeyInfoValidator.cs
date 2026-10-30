// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Net.Security;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32;

namespace Microsoft.Owin.Security
{
    /// <summary>
    /// Implements a cert pinning validator passed on 
    /// http://datatracker.ietf.org/doc/draft-ietf-websec-key-pinning/?include_text=1
    /// </summary>
    public class CertificateSubjectPublicKeyInfoValidator : ICertificateValidator
    {
        private readonly HashSet<string> _validBase64EncodedSubjectPublicKeyInfoHashes;

        private readonly SubjectPublicKeyInfoAlgorithm _algorithm;

        /// <summary>
        /// Initializes a new instance of the <see cref="CertificateSubjectPublicKeyInfoValidator"/> class.
        /// </summary>
        /// <param name="validBase64EncodedSubjectPublicKeyInfoHashes">A collection of valid base64 encoded hashes of the certificate public key information blob.</param>
        /// <param name="algorithm">The algorithm used to generate the hashes.</param>
        public CertificateSubjectPublicKeyInfoValidator(IEnumerable<string> validBase64EncodedSubjectPublicKeyInfoHashes, SubjectPublicKeyInfoAlgorithm algorithm)
        {
            if (validBase64EncodedSubjectPublicKeyInfoHashes == null)
            {
                throw new ArgumentNullException("validBase64EncodedSubjectPublicKeyInfoHashes");
            }

            _validBase64EncodedSubjectPublicKeyInfoHashes = new HashSet<string>(validBase64EncodedSubjectPublicKeyInfoHashes);

            if (_validBase64EncodedSubjectPublicKeyInfoHashes.Count == 0)
            {
                throw new ArgumentOutOfRangeException("validBase64EncodedSubjectPublicKeyInfoHashes");
            }

            if (_algorithm != SubjectPublicKeyInfoAlgorithm.Sha1 && _algorithm != SubjectPublicKeyInfoAlgorithm.Sha256)
            {
                throw new ArgumentOutOfRangeException("algorithm");
            }

            _algorithm = algorithm;
        }

        /// <summary>
        /// Validates at least one SPKI hash is known.
        /// </summary>
        /// <param name="sender">An object that contains state information for this validation.</param>
        /// <param name="certificate">The certificate used to authenticate the remote party.</param>
        /// <param name="chain">The chain of certificate authorities associated with the remote certificate.</param>
        /// <param name="sslPolicyErrors">One or more errors associated with the remote certificate.</param>
        /// <returns>A Boolean value that determines whether the specified certificate is accepted for authentication.</returns>
        public bool Validate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors != SslPolicyErrors.None)
            {
                return false;
            }

            if (chain == null)
            {
                throw new ArgumentNullException("chain");
            }

            if (chain.ChainElements.Count < 2)
            {
                return false;
            }

            using (HashAlgorithm algorithm = CreateHashAlgorithm())
            {
                foreach (var chainElement in chain.ChainElements)
                {
                    X509Certificate2 chainedCertificate = chainElement.Certificate;
                    string base64Spki = Convert.ToBase64String(algorithm.ComputeHash(ExtractSpkiBlob(chainedCertificate)));
                    if (_validBase64EncodedSubjectPublicKeyInfoHashes.Contains(base64Spki))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static byte[] ExtractSpkiBlob(X509Certificate2 certificate)
        {
            // webForCore: the original reads the CryptoAPI CERT_CONTEXT behind certificate.Handle and DER-encodes its
            // SubjectPublicKeyInfo with CryptEncodeObject(X509_PUBLIC_KEY_INFO). Outside Windows the handle is not a
            // CERT_CONTEXT; ExportSubjectPublicKeyInfo returns the same DER encoding.
            return certificate.PublicKey.ExportSubjectPublicKeyInfo();
        }

        [SuppressMessage("Microsoft.Security.Cryptography", "CA5354:SHA1CannotBeUsed", Justification = "Only used to verify cert hashes.")]
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope", Justification = "Caller is responsible for disposal.")]
        private HashAlgorithm CreateHashAlgorithm()
        {
            return _algorithm == SubjectPublicKeyInfoAlgorithm.Sha1 ? (HashAlgorithm)new SHA1CryptoServiceProvider() : new SHA256CryptoServiceProvider(); // CodeQL [SM02196] Only used to validate hashes.
        }
    }
}
