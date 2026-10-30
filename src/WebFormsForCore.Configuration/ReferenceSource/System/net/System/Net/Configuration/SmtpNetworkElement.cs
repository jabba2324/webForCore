//------------------------------------------------------------------------------
// <copyright file="SmtpNetworkElement.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

namespace System.Net.Configuration
{
    using System;
    using System.Configuration;
    using System.Net;
    using System.Net.Mail;
    using System.Reflection;
    using System.Security.Permissions;

    public sealed class SmtpNetworkElement : ConfigurationElement
    {
        public SmtpNetworkElement()
        {
            this.properties.Add(this.defaultCredentials);
            this.properties.Add(this.host);
            this.properties.Add(this.clientDomain);
            this.properties.Add(this.password);
            this.properties.Add(this.port);
            this.properties.Add(this.userName);
            this.properties.Add(this.targetName);
            this.properties.Add(this.enableSsl);
        }

        protected override void PostDeserialize()
        {
            // Perf optimization. If the configuration is coming from machine.config
            // It is safe and we don't need to check for permissions.
            if (EvaluationContext.IsMachineLevel)
                return;

            PropertyInformation portPropertyInfo = ElementInformation.Properties[ConfigurationStrings.Port];

            if (portPropertyInfo.ValueOrigin == PropertyValueOrigin.SetHere &&
                (int)portPropertyInfo.Value != (int)portPropertyInfo.DefaultValue)
            {
                try {
                    (new SmtpPermission(SmtpAccess.ConnectToUnrestrictedPort)).Demand();
                } catch (Exception exception) {
                    throw new ConfigurationErrorsException(
                                  SR.GetString(SR.net_config_property_permission, 
                                               portPropertyInfo.Name),
                                  exception);
                }
            }
        }

        protected internal override ConfigurationPropertyCollection Properties 
        {
            get 
            {
                return this.properties;
            }
        }

        [ConfigurationProperty(ConfigurationStrings.DefaultCredentials, DefaultValue = false)]
        public bool DefaultCredentials
        {
            get { return (bool)this[this.defaultCredentials]; }
            set { this[this.defaultCredentials] = value; }
        }

        [ConfigurationProperty(ConfigurationStrings.Host)]
        public string Host
        {
            get { return (string)this[this.host]; }
            set { this[this.host] = value; }
        }

        [ConfigurationProperty(ConfigurationStrings.TargetName)]
        public string TargetName
        {
            get { return (string)this[this.targetName]; }
            set { this[this.targetName] = value; }
        }

        [ConfigurationProperty(ConfigurationStrings.ClientDomain)]
        public string ClientDomain
        {
            get { return (string)this[this.clientDomain]; }
            set { this[this.clientDomain] = value; }
        }

        [ConfigurationProperty(ConfigurationStrings.Password)]
        public string Password
        {
            get { return (string)this[this.password]; }
            set { this[this.password] = value; }
        }

        [ConfigurationProperty(ConfigurationStrings.Port, DefaultValue = 25)]
        public int Port
        {
            get { return (int)this[this.port]; }
            set 
            { 
                // 
                this[this.port] = value; 
            }
        }

        [ConfigurationProperty(ConfigurationStrings.UserName)]
        public string UserName
        {
            get { return (string)this[this.userName]; }
            set { this[this.userName] = value; }
        }

        [ConfigurationProperty(ConfigurationStrings.EnableSsl, DefaultValue = false)]
        public bool EnableSsl
        {
            get { return (bool)this[this.enableSsl]; }
            set { this[this.enableSsl] = value; }
        }

	        
        // 



        ConfigurationPropertyCollection properties = new ConfigurationPropertyCollection();

        readonly ConfigurationProperty defaultCredentials =
            new ConfigurationProperty(ConfigurationStrings.DefaultCredentials, typeof(bool), false,
                    ConfigurationPropertyOptions.None);

        readonly ConfigurationProperty host =
            new ConfigurationProperty(ConfigurationStrings.Host, typeof(string), null,
                    ConfigurationPropertyOptions.None);

        readonly ConfigurationProperty clientDomain =
            new ConfigurationProperty(ConfigurationStrings.ClientDomain, typeof(string), null,
                    ConfigurationPropertyOptions.None);

        readonly ConfigurationProperty password =
            new ConfigurationProperty(ConfigurationStrings.Password, typeof(string), null,
                    ConfigurationPropertyOptions.None);

        readonly ConfigurationProperty port =
            new ConfigurationProperty(ConfigurationStrings.Port, typeof(int), 25,
                    null, new IntegerValidator(IPEndPoint.MinPort+1, IPEndPoint.MaxPort), ConfigurationPropertyOptions.None);

        readonly ConfigurationProperty userName =
            new ConfigurationProperty(ConfigurationStrings.UserName, typeof(string), null,
                    ConfigurationPropertyOptions.None);

        readonly ConfigurationProperty targetName =
            new ConfigurationProperty(ConfigurationStrings.TargetName, typeof(string), null, ConfigurationPropertyOptions.None);

        readonly ConfigurationProperty enableSsl =
            new ConfigurationProperty(ConfigurationStrings.EnableSsl, typeof(bool), false,
                    ConfigurationPropertyOptions.None);

    }

    // webForCore: SmtpNetworkElementInternal left out (.NET Framework's System.Net only; see lift_configuration.py).
}

