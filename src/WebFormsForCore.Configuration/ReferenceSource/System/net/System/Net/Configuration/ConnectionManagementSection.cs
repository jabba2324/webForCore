//------------------------------------------------------------------------------
// <copyright file="ConnectionManagementSection.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

namespace System.Net.Configuration
{
    using System.Configuration;
    using System.Collections;
    using System.Threading;    
    public sealed class ConnectionManagementSection : ConfigurationSection
    {
        public ConnectionManagementSection() 
        {
            this.properties.Add(this.connectionManagement);
        }

        [ConfigurationProperty("", IsDefaultCollection = true)]
        public ConnectionManagementElementCollection ConnectionManagement
        {
            get { return (ConnectionManagementElementCollection)this[this.connectionManagement]; }
        }

        protected internal override ConfigurationPropertyCollection Properties
        {
            get { return this.properties; }
        }

        ConfigurationPropertyCollection properties = new ConfigurationPropertyCollection();

        readonly ConfigurationProperty connectionManagement =
            new ConfigurationProperty(null, typeof(ConnectionManagementElementCollection), null,
                    ConfigurationPropertyOptions.IsDefaultCollection);
    }

    // webForCore: ConnectionManagementSectionInternal left out (.NET Framework's System.Net only; see lift_configuration.py).
}
