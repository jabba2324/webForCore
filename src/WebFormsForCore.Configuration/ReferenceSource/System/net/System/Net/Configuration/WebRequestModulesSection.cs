//------------------------------------------------------------------------------
// <copyright file="WebRequestModulesSection.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

namespace System.Net.Configuration
{
    using System.Configuration;
    using System.Collections;
    using System.Globalization;
    using System.Net;    
    using System.Reflection;
    using System.Threading;
    public sealed class WebRequestModulesSection : ConfigurationSection
    {
        public WebRequestModulesSection() 
        {
            this.properties.Add(this.webRequestModules);
        }

        protected override void PostDeserialize()
        {
            // Perf optimization. If the configuration is coming from machine.config
            // It is safe and we don't need to check for permissions.
            if (EvaluationContext.IsMachineLevel)
                return;

            try {
                ExceptionHelper.WebPermissionUnrestricted.Demand();
            } catch (Exception exception) {
                throw new ConfigurationErrorsException(
                              SR.GetString(SR.net_config_section_permission, 
                                           ConfigurationStrings.WebRequestModulesSectionName),
                              exception);
            }
        }

        protected internal override void InitializeDefault()
        {
            this.WebRequestModules.Add(
                new WebRequestModuleElement(ConfigurationStrings.Https, Type.GetType("System.Net.HttpRequestCreator, " + AssemblyRef.System, false) ?? typeof(object)));
            this.WebRequestModules.Add(
                new WebRequestModuleElement(ConfigurationStrings.Http, Type.GetType("System.Net.HttpRequestCreator, " + AssemblyRef.System, false) ?? typeof(object)));
            this.WebRequestModules.Add(
                new WebRequestModuleElement(ConfigurationStrings.File, Type.GetType("System.Net.FileWebRequestCreator, " + AssemblyRef.System, false) ?? typeof(object)));
            this.WebRequestModules.Add(
                new WebRequestModuleElement(ConfigurationStrings.Ftp, Type.GetType("System.Net.FtpWebRequestCreator, " + AssemblyRef.System, false) ?? typeof(object)));
        }

        protected internal override ConfigurationPropertyCollection Properties
        {
            get { return this.properties; }
        }

        [ConfigurationProperty("", IsDefaultCollection=true )]
        public WebRequestModuleElementCollection WebRequestModules
        {
            get { return (WebRequestModuleElementCollection)this[this.webRequestModules]; }
        }

        ConfigurationPropertyCollection properties = new ConfigurationPropertyCollection();

        readonly ConfigurationProperty webRequestModules =
            new ConfigurationProperty(null, typeof(WebRequestModuleElementCollection), null,
                    ConfigurationPropertyOptions.IsDefaultCollection);
    }

    // webForCore: WebRequestModulesSectionInternal left out (.NET Framework's System.Net only; see lift_configuration.py).
}
