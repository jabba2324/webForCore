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
    using System.Reflection;
    using System.Security.Permissions;

    public sealed class SmtpSpecifiedPickupDirectoryElement : ConfigurationElement
    {
        public SmtpSpecifiedPickupDirectoryElement()
        {
            this.properties.Add(this.pickupDirectoryLocation);
        }

        protected internal override ConfigurationPropertyCollection Properties 
        {
            get 
            {
                return this.properties;
            }
        }

        [ConfigurationProperty(ConfigurationStrings.PickupDirectoryLocation)]
        public string PickupDirectoryLocation
        {
            get { return (string)this[this.pickupDirectoryLocation]; }
            set { this[this.pickupDirectoryLocation] = value; }
        }

	        
        // 



        ConfigurationPropertyCollection properties = new ConfigurationPropertyCollection();

        readonly ConfigurationProperty pickupDirectoryLocation =
            new ConfigurationProperty(ConfigurationStrings.PickupDirectoryLocation, typeof(string), null,
                    ConfigurationPropertyOptions.None);
    }

    // webForCore: SmtpSpecifiedPickupDirectoryElementInternal left out (.NET Framework's System.Net only; see lift_configuration.py).
}

