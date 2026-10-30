//------------------------------------------------------------------------------
// <copyright file="RequestCachingSection.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

namespace System.Net.Configuration
{
    using Microsoft.Win32;
    using System.Configuration;
    using System.Globalization;
    using System.Net.Cache;
    using System.Threading;

    public sealed class RequestCachingSection : ConfigurationSection
    {
        public RequestCachingSection()
        {
            this.properties.Add(this.disableAllCaching);
            this.properties.Add(this.defaultPolicyLevel);
            this.properties.Add(this.isPrivateCache);
            this.properties.Add(this.defaultHttpCachePolicy);
            this.properties.Add(this.defaultFtpCachePolicy);
            this.properties.Add(this.unspecifiedMaximumAge);
        }

        [ConfigurationProperty(ConfigurationStrings.DefaultHttpCachePolicy)]
        public HttpCachePolicyElement DefaultHttpCachePolicy
        {
            get { return (HttpCachePolicyElement)this[this.defaultHttpCachePolicy]; }
        }

        [ConfigurationProperty(ConfigurationStrings.DefaultFtpCachePolicy)]
        public FtpCachePolicyElement DefaultFtpCachePolicy
        {
            get { return (FtpCachePolicyElement)this[this.defaultFtpCachePolicy]; }
        }

        [ConfigurationProperty(ConfigurationStrings.DefaultPolicyLevel, DefaultValue=(RequestCacheLevel) RequestCacheLevel.BypassCache)]
        public RequestCacheLevel DefaultPolicyLevel
        {
            get { return (RequestCacheLevel)this[this.defaultPolicyLevel]; }
            set { this[this.defaultPolicyLevel] = value; }
        }

#if !FEATURE_PAL // FEATURE_PAL - Caching is not supported by default
        [ConfigurationProperty(ConfigurationStrings.DisableAllCaching, DefaultValue=false)]
#else // !FEATURE_PAL
        [ConfigurationProperty(ConfigurationStrings.DisableAllCaching, DefaultValue=true)]
#endif // !FEATURE_PAL
        public bool DisableAllCaching
        {
            get { return (bool)this[this.disableAllCaching]; }
            set { this[this.disableAllCaching] = value; }
        }

        [ConfigurationProperty(ConfigurationStrings.IsPrivateCache, DefaultValue=true)]
        public bool IsPrivateCache
        {
            get { return (bool)this[this.isPrivateCache]; }
            set { this[this.isPrivateCache] = value; }
        }

        [ConfigurationProperty(ConfigurationStrings.UnspecifiedMaximumAge, DefaultValue = "1.00:00:00")]
        public TimeSpan UnspecifiedMaximumAge
        {
            get { return (TimeSpan)this[this.unspecifiedMaximumAge]; }
            set { this[this.unspecifiedMaximumAge] = value; }
        }

        //
        // If DisableAllCaching is set once to true it will not change.
        //
        protected internal override void DeserializeElement(System.Xml.XmlReader reader, bool serializeCollectionKey)
        {

            bool tempDisableAllCaching = this.DisableAllCaching;

            base.DeserializeElement(reader, serializeCollectionKey);
            if (tempDisableAllCaching)
            {
                this.DisableAllCaching = true;
            }
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
                                           ConfigurationStrings.RequestCachingSectionName),
                              exception);
            }
        }

        protected internal override ConfigurationPropertyCollection Properties
        {
            get { return this.properties; }
        }

        ConfigurationPropertyCollection properties = new ConfigurationPropertyCollection();

        readonly ConfigurationProperty defaultHttpCachePolicy =
            new ConfigurationProperty(ConfigurationStrings.DefaultHttpCachePolicy, 
                                      typeof(HttpCachePolicyElement), 
                                      null,
                                      ConfigurationPropertyOptions.None);

        readonly ConfigurationProperty defaultFtpCachePolicy =
            new ConfigurationProperty(ConfigurationStrings.DefaultFtpCachePolicy, 
                                      typeof(FtpCachePolicyElement), 
                                      null,
                                      ConfigurationPropertyOptions.None);

        readonly ConfigurationProperty defaultPolicyLevel =
            new ConfigurationProperty(ConfigurationStrings.DefaultPolicyLevel, 
                                      typeof(RequestCacheLevel), 
                                      RequestCacheLevel.BypassCache,
                                      ConfigurationPropertyOptions.None);

        readonly ConfigurationProperty disableAllCaching =
#if !FEATURE_PAL // FEATURE_PAL - Caching is not supported by default
            new ConfigurationProperty(ConfigurationStrings.DisableAllCaching, 
                                      typeof(bool), 
                                      false,
                                      ConfigurationPropertyOptions.None);
#else // !FEATURE_PAL
            new ConfigurationProperty(ConfigurationStrings.DisableAllCaching, 
                                      typeof(bool), 
                                      true,
                                      ConfigurationPropertyOptions.None);
#endif // !FEATURE_PAL

        readonly ConfigurationProperty isPrivateCache =
            new ConfigurationProperty(ConfigurationStrings.IsPrivateCache, 
                                      typeof(bool), 
                                      true,
                                      ConfigurationPropertyOptions.None);

        readonly ConfigurationProperty unspecifiedMaximumAge =
            new ConfigurationProperty(ConfigurationStrings.UnspecifiedMaximumAge, 
                                      typeof(TimeSpan), 
                                      TimeSpan.FromDays(1),
                                      ConfigurationPropertyOptions.None);
    }

    // webForCore: RequestCachingSectionInternal left out (.NET Framework's System.Net only; see lift_configuration.py).
}
