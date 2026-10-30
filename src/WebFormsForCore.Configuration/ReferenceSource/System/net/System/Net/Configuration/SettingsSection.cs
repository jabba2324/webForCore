//------------------------------------------------------------------------------
// <copyright file="SettingsSection.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

namespace System.Net.Configuration
{
    using System.Configuration;
    using System.Threading; 
    using System.Net.Security;
    using System.Net.Sockets;

    public sealed class SettingsSection : ConfigurationSection
    {

        // This gets called by the configuration system when the app.config file is located at an
        // http:// Uri.  It forces the System.Net config to be loaded based on only the machine.config
        // because the configuration system uses System.Net to download the app.config file.
        static internal void EnsureConfigLoaded() {
            // webForCore: nothing to prime; .NET's System.Net does not read <system.net>.
        }


        public SettingsSection() 
        {
            this.properties.Add(this.httpWebRequest);
            this.properties.Add(this.ipv6);
            this.properties.Add(this.servicePointManager);
            this.properties.Add(this.socket);
            this.properties.Add(this.webProxyScript);
            this.properties.Add(this.performanceCounters);
            this.properties.Add(this.httpListener);
            this.properties.Add(this.webUtility);
            this.properties.Add(this.windowsAuthentication);
        }


        [ConfigurationProperty(ConfigurationStrings.HttpWebRequest)]
        public HttpWebRequestElement HttpWebRequest
        {
            get { return (HttpWebRequestElement)this[this.httpWebRequest]; }
        }

        [ConfigurationProperty(ConfigurationStrings.Ipv6)]
        public Ipv6Element Ipv6
        {
            get { return (Ipv6Element)this[this.ipv6]; }
        }

        [ConfigurationProperty(ConfigurationStrings.ServicePointManager)]
        public ServicePointManagerElement ServicePointManager
        {
            get { return (ServicePointManagerElement)this[this.servicePointManager]; }
        }

        [ConfigurationProperty(ConfigurationStrings.Socket)]
        public SocketElement Socket
        {
            get { return (SocketElement)this[this.socket]; }
        }

        [ConfigurationProperty(ConfigurationStrings.WebProxyScript)]
        public WebProxyScriptElement WebProxyScript
        {
            get { return (WebProxyScriptElement) this[this.webProxyScript]; }
        }


        [ConfigurationProperty(ConfigurationStrings.PerformanceCounters)]
        public PerformanceCountersElement PerformanceCounters
        {
            get { return (PerformanceCountersElement) this[this.performanceCounters]; }
        }

        [ConfigurationProperty(ConfigurationStrings.HttpListener)]
        public HttpListenerElement HttpListener
        {
            get { return (HttpListenerElement)this[this.httpListener]; }
        }

        [ConfigurationProperty(ConfigurationStrings.WebUtility)]
        public WebUtilityElement WebUtility
        {
            get { return (WebUtilityElement)this[this.webUtility]; }
        }

        [ConfigurationProperty(ConfigurationStrings.WindowsAuthentication)]
        public WindowsAuthenticationElement WindowsAuthentication
        {
            get { return (WindowsAuthenticationElement)this[this.windowsAuthentication]; }
        }

        protected internal override ConfigurationPropertyCollection Properties
        {
            get { return this.properties; }
        }

        ConfigurationPropertyCollection properties = new ConfigurationPropertyCollection();

        readonly ConfigurationProperty httpWebRequest =
            new ConfigurationProperty(ConfigurationStrings.HttpWebRequest, typeof(HttpWebRequestElement), null,
                    ConfigurationPropertyOptions.None);

        readonly ConfigurationProperty ipv6 =
            new ConfigurationProperty(ConfigurationStrings.Ipv6, typeof(Ipv6Element), null,
                ConfigurationPropertyOptions.None);

        readonly ConfigurationProperty servicePointManager =
            new ConfigurationProperty(ConfigurationStrings.ServicePointManager, typeof(ServicePointManagerElement), null,
                ConfigurationPropertyOptions.None);

        readonly ConfigurationProperty socket =
            new ConfigurationProperty(ConfigurationStrings.Socket, typeof(SocketElement), null,
                ConfigurationPropertyOptions.None);

        readonly ConfigurationProperty webProxyScript =
            new ConfigurationProperty(ConfigurationStrings.WebProxyScript, typeof(WebProxyScriptElement), null,
                ConfigurationPropertyOptions.None);

        readonly ConfigurationProperty performanceCounters =
            new ConfigurationProperty(ConfigurationStrings.PerformanceCounters, typeof(PerformanceCountersElement), null,
                ConfigurationPropertyOptions.None);

        readonly ConfigurationProperty httpListener =
            new ConfigurationProperty(ConfigurationStrings.HttpListener, typeof(HttpListenerElement), null,
                ConfigurationPropertyOptions.None);

        readonly ConfigurationProperty webUtility =
            new ConfigurationProperty(ConfigurationStrings.WebUtility, typeof(WebUtilityElement), null,
                ConfigurationPropertyOptions.None);

        readonly ConfigurationProperty windowsAuthentication =
            new ConfigurationProperty(ConfigurationStrings.WindowsAuthentication, typeof(WindowsAuthenticationElement), null,
                ConfigurationPropertyOptions.None);
    }

    // webForCore: SettingsSectionInternal left out (.NET Framework's System.Net only; see lift_configuration.py).
}
