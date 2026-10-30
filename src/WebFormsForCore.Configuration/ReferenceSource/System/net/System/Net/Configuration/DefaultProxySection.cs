//------------------------------------------------------------------------------
// <copyright file="DefaultProxySection.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

namespace System.Net.Configuration
{
    using System.Collections.Generic;
    using System.Configuration;
    using System.Globalization;
    using System.Net;
    using System.Reflection;
    using System.Threading;
    using System.Security;
    using System.Security.Permissions;
    using System.Security.Principal;
    using System.ComponentModel;


    public sealed class DefaultProxySection : ConfigurationSection
    {
        public DefaultProxySection()
        {
            this.properties.Add(this.bypasslist);
            this.properties.Add(this.module);
            this.properties.Add(this.proxy);
            this.properties.Add(this.enabled);
            this.properties.Add(this.useDefaultCredentials);
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
                                           ConfigurationStrings.DefaultProxySectionName),
                              exception);
            }
        }
         
        [ConfigurationProperty(ConfigurationStrings.BypassList)]
        public BypassElementCollection BypassList
        {
            get { return (BypassElementCollection)this[this.bypasslist]; }
        }

        [ConfigurationProperty(ConfigurationStrings.Module)]
        public ModuleElement Module
        {
            get { return (ModuleElement)this[this.module]; }
        }

        protected internal override ConfigurationPropertyCollection Properties
        {
            get { return this.properties; }
        }
        
        [ConfigurationProperty(ConfigurationStrings.Proxy)]
        public ProxyElement Proxy
        {
            get { return (ProxyElement)this[this.proxy]; }
        }

        [ConfigurationProperty(ConfigurationStrings.Enabled, DefaultValue = true)]
        public bool Enabled
        {
            get { return (bool) this[this.enabled]; }
            set { this[this.enabled] = value; }
        }

        [ConfigurationProperty(ConfigurationStrings.UseDefaultCredentials, DefaultValue = false)]
        public bool UseDefaultCredentials
        {
            get { return (bool) this[this.useDefaultCredentials]; }
            set { this[this.useDefaultCredentials] = value; }
        }

        ConfigurationPropertyCollection properties = new ConfigurationPropertyCollection();

        readonly ConfigurationProperty bypasslist =
            new ConfigurationProperty(ConfigurationStrings.BypassList, 
                                      typeof(BypassElementCollection), 
                                      null,
                                      ConfigurationPropertyOptions.None);

        readonly ConfigurationProperty module =
            new ConfigurationProperty(ConfigurationStrings.Module, 
                                      typeof(ModuleElement), 
                                      null,
                                      ConfigurationPropertyOptions.None);

        readonly ConfigurationProperty proxy =
            new ConfigurationProperty(ConfigurationStrings.Proxy, 
                                      typeof(ProxyElement), 
                                      null,
                                      ConfigurationPropertyOptions.None);

        readonly ConfigurationProperty enabled =
            new ConfigurationProperty(ConfigurationStrings.Enabled, 
                                      typeof(bool), 
                                      true, 
                                      ConfigurationPropertyOptions.None);

        readonly ConfigurationProperty useDefaultCredentials =
            new ConfigurationProperty(ConfigurationStrings.UseDefaultCredentials, 
                                      typeof(bool), 
                                      false,
                                      ConfigurationPropertyOptions.None);


        // This allows us to prevent parent settings (machine.config) from propegating to higher config (app.config), unless
        // the higher config doesn't contain the section at all.  That is, overriding defaultProxy is all-or-nothing.
        // Template from Microsoft.
        protected internal override void Reset(ConfigurationElement parentElement)
        {
            // Ignore the parentElement parameter by changing it to the default settings
            DefaultProxySection defaultElement = new DefaultProxySection();

            // Initialize the parentElement to the right set of defaults (not needed now,
            // but this will avoid errors in the future if SetDefaults is ever overridden in this class.
            // ConfigurationElement::InitializeDefault is a no-op, so you aren’t hurting perf by anything
            // measurable. 
            defaultElement.InitializeDefault();

            // Finally, pass it to the base class to do the “right things”
            base.Reset(defaultElement);
        }
    }

    // webForCore: DefaultProxySectionInternal left out (.NET Framework's System.Net only; see lift_configuration.py).
}
