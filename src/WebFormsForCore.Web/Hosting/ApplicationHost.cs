//------------------------------------------------------------------------------
// <copyright file="ApplicationHost.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>                                                                
//------------------------------------------------------------------------------

namespace System.Web.Hosting {

    using System;
    using System.IO;
    using System.Collections;
    using System.Globalization;
    using System.Runtime.InteropServices;
    using System.Runtime.Remoting;
    using System.Web;
    using System.Web.Configuration;
    using System.Web.Util;
    using System.Security.Permissions;


    /// <devdoc>
    ///    <para>[To be supplied.]</para>
    /// </devdoc>
    public sealed class ApplicationHost {

        private ApplicationHost() {
        }

        /*
         * Creates new app domain for hosting of ASP.NET apps with a
         * user defined 'host' object in it.  The host is needed to make
         * cross-domain calls to process requests in the host's app domain
         */

        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        [SecurityPermission(SecurityAction.Demand, Unrestricted=true)]
        public static Object CreateApplicationHost(Type hostType, String virtualDir, String physicalDir) {
#if NETFRAMEWORK
#if !FEATURE_PAL // FEATURE_PAL does not require PlatformID.Win32NT
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                throw new PlatformNotSupportedException(SR.GetString(SR.RequiresNT));
#else // !FEATURE_PAL
            // FEATURE_PAL
#endif // !FEATURE_PAL
#endif

            if (!StringUtil.StringEndsWith(physicalDir, Path.DirectorySeparatorChar))
                physicalDir = physicalDir + Path.DirectorySeparatorChar;

#if NETCOREAPP
            // webForCore: the new AppDomain is a PseudoAppDomain (AspNetCoreHosting/PseudoAppDomain.cs) with the
            // application directory as its base; the host is created there by that domain's copy of System.Web. The
            // result is the domain's object (no remoting proxy): callers reach it through reflection or dynamic.
            if (PseudoAppDomain.IsSupported) {
                var domain = new PseudoAppDomain(ApplicationManager.CreateApplicationId(virtualDir, physicalDir), physicalDir);
                return domain.InvokeStatic(typeof(ApplicationHost), nameof(CreateApplicationHostInCurrentDomain),
                    domain.GetDomainType(hostType), virtualDir, physicalDir);
            }
            return CreateApplicationHostInCurrentDomain(hostType, virtualDir, physicalDir);
        }

        internal static Object CreateApplicationHostInCurrentDomain(Type hostType, String virtualDir, String physicalDir) {
#endif
            ApplicationManager appManager = ApplicationManager.GetApplicationManager();

            String appId = ApplicationManager.CreateApplicationId(virtualDir, physicalDir);

#if NETFRAMEWORK
            ObjectHandle h = appManager.CreateInstanceInNewWorkerAppDomain(
                                hostType, appId, VirtualPath.CreateNonRelative(virtualDir), physicalDir);

            return h.Unwrap();
#else
            var host = appManager.CreateInstanceInNewWorkerLoadContext(hostType, appId,
                VirtualPath.CreateNonRelative(virtualDir), physicalDir);
            return host;
#endif
        }
    }
}
