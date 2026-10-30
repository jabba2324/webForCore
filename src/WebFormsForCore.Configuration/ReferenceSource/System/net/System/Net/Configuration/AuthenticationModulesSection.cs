//------------------------------------------------------------------------------
// <copyright file="AuthenticationModulesSection.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

namespace System.Net.Configuration
{
    using System.Configuration;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Net;
    using System.Threading;

    public sealed class AuthenticationModulesSection : ConfigurationSection
    {
        public AuthenticationModulesSection()
        {
            this.properties.Add(this.authenticationModules);
        }

        protected override void PostDeserialize()
        {
            // Perf optimization. If the configuration is coming from machine.config
            // It is safe and we don't need to check for permissions.
            if (EvaluationContext.IsMachineLevel)
                return;

            try {
                ExceptionHelper.UnmanagedPermission.Demand();
            } catch (Exception exception) {
                throw new ConfigurationErrorsException(
                              SR.GetString(SR.net_config_section_permission, 
                                           ConfigurationStrings.AuthenticationModulesSectionName),
                              exception);
            }
        }

        [ConfigurationProperty("", IsDefaultCollection = true)]
        public AuthenticationModuleElementCollection AuthenticationModules
        {
            get { return (AuthenticationModuleElementCollection)this[this.authenticationModules]; }
        }

        protected internal override void InitializeDefault()
        {
#if !FEATURE_PAL // Security
            this.AuthenticationModules.Add(
                new AuthenticationModuleElement("System.Net.NegotiateClient, " + AssemblyRef.System));
            this.AuthenticationModules.Add(
                new AuthenticationModuleElement("System.Net.KerberosClient, " + AssemblyRef.System));
            this.AuthenticationModules.Add(
                new AuthenticationModuleElement("System.Net.NtlmClient, " + AssemblyRef.System));
            this.AuthenticationModules.Add(
                new AuthenticationModuleElement("System.Net.DigestClient, " + AssemblyRef.System));
            this.AuthenticationModules.Add(
                new AuthenticationModuleElement("System.Net.BasicClient, " + AssemblyRef.System));
#endif // !FEATURE_PAL // Security
        }

        protected internal override ConfigurationPropertyCollection Properties
        {
            get { return this.properties; }
        }

        ConfigurationPropertyCollection properties = new ConfigurationPropertyCollection();

        readonly ConfigurationProperty authenticationModules =
            new ConfigurationProperty(null, typeof(AuthenticationModuleElementCollection), null,
                    ConfigurationPropertyOptions.IsDefaultCollection);
    }

    // webForCore: AuthenticationModulesSectionInternal left out (.NET Framework's System.Net only; see lift_configuration.py).
}
