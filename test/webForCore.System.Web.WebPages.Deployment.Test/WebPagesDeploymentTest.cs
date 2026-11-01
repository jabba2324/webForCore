// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Reflection;
using System.Web.WebPages.TestUtils;
using Microsoft.TestCommon;

namespace System.Web.WebPages.Deployment.Test
{
    // We need to mark this type Serializable for TDD.Net to work with some of the AppDomain tests
    [Serializable]
    public class WebPagesDeploymentTest : IDisposable
    {
        private const string TestNamespacePrefix = "System.Web.WebPages.Deployment.Test.TestFiles.";

        private static readonly IDictionary<string, string> _deploymentPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { @"ConfigTestSites.CshtmlFileNoVersion.Default.cshtml", Path.Combine(@"ConfigTestSites", @"CshtmlFileNoVersion", @"Default.cshtml") },
            { @"ConfigTestSites.NoCshtmlWithEnabledSetting.Default.htm", Path.Combine(@"ConfigTestSites", @"NoCshtmlWithEnabledSetting", @"Default.htm") },
            { @"ConfigTestSites.NoCshtmlNoConfigSetting.web.config", Path.Combine(@"ConfigTestSites", @"NoCshtmlNoConfigSetting", @"web.config") },
            { @"ConfigTestSites.NoCshtmlWithEnabledSetting.web.config", Path.Combine(@"ConfigTestSites", @"NoCshtmlWithEnabledSetting", @"web.config") },
            { @"ConfigTestSites.NoCshtmlWithEnabledSettingFalse.Default.htm", Path.Combine(@"ConfigTestSites", @"NoCshtmlWithEnabledSettingFalse", @"Default.htm") },
            { @"ConfigTestAssemblies.V2_Unsigned.System.Web.WebPages.Deployment.dll", Path.Combine(@"ConfigTestAssemblies", @"V2_Unsigned", @"System.Web.WebPages.Deployment.dll") },
            { @"ConfigTestAssemblies.V2_Signed.System.Web.WebPages.Deployment.dll", Path.Combine(@"ConfigTestAssemblies", @"V2_Signed", @"System.Web.WebPages.Deployment.dll") },
            { @"ConfigTestSites.NoCshtmlWithEnabledSettingFalse.web.config", Path.Combine(@"ConfigTestSites", @"NoCshtmlWithEnabledSettingFalse", @"web.config") },
            { @"ConfigTestSites.CshtmlFileConfigV1.Default.cshtml", Path.Combine(@"ConfigTestSites", @"CshtmlFileConfigV1", @"Default.cshtml") },
            { @"ConfigTestSites.NoCshtml.Default.htm", Path.Combine(@"ConfigTestSites", @"NoCshtml", @"Default.htm") },
            { @"ConfigTestSites.NoCshtmlNoConfigSetting.Default.htm", Path.Combine(@"ConfigTestSites", @"NoCshtmlNoConfigSetting", @"Default.htm") },
            { @"ConfigTestSites.CshtmlFileConfigV1.web.config", Path.Combine(@"ConfigTestSites", @"CshtmlFileConfigV1", @"web.config") },
            { @"ConfigTestSites.NoCshtmlConfigV1.Default.htm", Path.Combine(@"ConfigTestSites", @"NoCshtmlConfigV1", @"Default.htm") },
            { @"ConfigTestSites.NoCshtmlConfigV1.web.config", Path.Combine(@"ConfigTestSites", @"NoCshtmlConfigV1", @"web.config") },
        };

        private readonly string _tempPath = GetTempPath();

        public WebPagesDeploymentTest()
        {
            var assembly = typeof(WebPagesDeploymentTest).Assembly;
            foreach (var item in _deploymentPaths)
            {
                new TestFile(TestNamespacePrefix + item.Key, assembly).Save(Path.Combine(_tempPath, item.Value));
            }
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_tempPath, recursive: true);
            }
            catch
            {
            }
        }

        [Fact]
        public void IsEnabledReturnsFalseIfNoCshtmlOrConfigFile()
        {
            Assert.False(WebPagesDeployment.IsEnabled(Path.Combine(_tempPath, Path.Combine(@"ConfigTestSites", @"NoCshtml"))));
        }

        [Fact]
        public void IsEnabledReturnsFalseIfNoCshtmlAndNoConfigSetting()
        {
            Assert.False(WebPagesDeployment.IsEnabled(Path.Combine(_tempPath, Path.Combine(@"ConfigTestSites", @"NoCshtmlNoConfigSetting"))));
        }

        [Fact]
        public void IsEnabledReturnsTrueIfNoCshtmlAndEnabledConfigSetting()
        {
            Assert.True(WebPagesDeployment.IsEnabled(Path.Combine(_tempPath, Path.Combine(@"ConfigTestSites", @"NoCshtmlWithEnabledSetting"))));
        }

        [Fact]
        public void IsEnabledReturnsTrueIfCshtmlFilePresent()
        {
            Assert.True(WebPagesDeployment.IsEnabled(Path.Combine(_tempPath, Path.Combine(@"ConfigTestSites", @"CshtmlFileNoVersion"))));
        }

        [Fact]
        public void IsExplicitlyDisabledReturnsTrueIfNoCshtmlAndEnabledConfigSettingSetToFalse()
        {
            Assert.True(WebPagesDeployment.IsExplicitlyDisabled(Path.Combine(_tempPath, Path.Combine(@"ConfigTestSites", @"NoCshtmlWithEnabledSettingFalse"))));
        }

        [Fact]
        public void IsExplicitlyDisabledReturnsFalseIfNoCshtmlAndEnabledConfigSettingSetToTrue()
        {
            Assert.False(WebPagesDeployment.IsExplicitlyDisabled(Path.Combine(_tempPath, Path.Combine(@"ConfigTestSites", @"NoCshtmlWithEnabledSetting"))));
        }

        [Fact]
        public void IsExplicitlyDisabledReturnsFalseIfNoCshtmlAndNoConfigSetting()
        {
            Assert.False(WebPagesDeployment.IsExplicitlyDisabled(Path.Combine(_tempPath, Path.Combine(@"ConfigTestSites", @"NoCshtmlNoConfigSetting"))));
        }

        [Fact]
        public void IsExplicitlyDisabledReturnsFalseIfNoCshtmlOrConfigFile()
        {
            Assert.False(WebPagesDeployment.IsExplicitlyDisabled(Path.Combine(_tempPath, Path.Combine(@"ConfigTestSites", @"NoCshtml"))));
        }

        [Fact]
        public void GetVersionReturnsValueFromAppSettingsIfNotExplicitlyDisabled()
        {
            // Arrange
            var version = "1.2.3.4";
            var appSettings = new NameValueCollection { { "webPages:Version", version } };
            var maxVersion = new Version("2.0");

            // Act
            var actualVersion = WebPagesDeployment.GetVersionInternal(appSettings, binVersion: null, defaultVersion: null);

            // Assert
            Assert.Equal(new Version(version), actualVersion);
        }

        [Fact]
        public void GetVersionReturnsValueEvenIfExplicitlyDisabled()
        {
            // Arrange
            var version = "1.2.3.4";
            var appSettings = new NameValueCollection { { "webPages:Version", version }, { "webPages:Enabled", "False" } };
            var maxVersion = new Version("2.0");

            // Act
            var actualVersion = WebPagesDeployment.GetVersionInternal(appSettings, binVersion: null, defaultVersion: null);

            // Assert
            Assert.Equal(new Version(version), actualVersion);
        }

        [Fact(Skip = "webForCore: side-by-side Web Pages versions (another System.Web.WebPages.Deployment loaded next to this one, as from the GAC): .NET has no GAC and an AssemblyLoadContext holds one version of a name.")]
        public void GetVersionReturnsLowerVersionIfSpecifiedInConfig()
        {
#if NETFRAMEWORK
            AppDomainUtils.RunInSeparateAppDomain(() =>
            {
                // Arrange - Load v2 Config
                Assembly asm = Assembly.LoadFrom(Path.Combine(_tempPath, Path.Combine(@"ConfigTestAssemblies", @"V2_Signed", @"System.Web.WebPages.Deployment.dll")));
                Assert.Equal(new Version(2, 0, 0, 0), asm.GetName().Version);
                Assert.Equal("System.Web.WebPages.Deployment", asm.GetName().Name);

                using (WebUtils.CreateHttpRuntime(Path.Combine(@"~", @"foo"), "."))
                {
                    string path = Path.Combine(_tempPath, Path.Combine(@"ConfigTestSites", @"CshtmlFileConfigV1"));

                    // Act
                    Version ver = WebPagesDeployment.GetVersionWithoutEnabledCheck(path);
                    Version explicitVersion = WebPagesDeployment.GetExplicitWebPagesVersion(path);

                    // Assert
                    Assert.Equal(new Version(1, 0, 0, 0), ver);
                    Assert.Equal(new Version(1, 0, 0, 0), explicitVersion);
                }
            });
#endif
        }

        [Fact(Skip = "webForCore: side-by-side Web Pages versions (another System.Web.WebPages.Deployment loaded next to this one, as from the GAC): .NET has no GAC and an AssemblyLoadContext holds one version of a name.")]
        public void GetVersionReturnsLowerVersionIfSpecifiedInConfigAndNotExplicitlyDisabled()
        {
#if NETFRAMEWORK
            AppDomainUtils.RunInSeparateAppDomain(() =>
            {
                // Arrange - Load v2 Config
                Assembly asm = Assembly.LoadFrom(Path.Combine(_tempPath, Path.Combine(@"ConfigTestAssemblies", @"V2_Signed", @"System.Web.WebPages.Deployment.dll")));
                Assert.Equal(new Version(2, 0, 0, 0), asm.GetName().Version);
                Assert.Equal("System.Web.WebPages.Deployment", asm.GetName().Name);

                using (WebUtils.CreateHttpRuntime(Path.Combine(@"~", @"foo"), "."))
                {
                    string path = Path.Combine(_tempPath, Path.Combine(@"ConfigTestSites", @"NoCshtmlConfigV1"));

                    // Act
                    Version ver = WebPagesDeployment.GetVersionWithoutEnabledCheck(path);
                    Version explicitVersion = WebPagesDeployment.GetExplicitWebPagesVersion(path);

                    // Assert
                    Assert.Equal(new Version(1, 0, 0, 0), ver);
                    Assert.Equal(new Version(1, 0, 0, 0), explicitVersion);
                }
            });
#endif
        }

        [Fact(Skip = "webForCore: side-by-side Web Pages versions (another System.Web.WebPages.Deployment loaded next to this one, as from the GAC): .NET has no GAC and an AssemblyLoadContext holds one version of a name.")]
        public void GetVersionIgnoresUnsignedConfigDll()
        {
#if NETFRAMEWORK
            AppDomainUtils.RunInSeparateAppDomain(() =>
            {
                // Arrange - Load v2 Config
                Assembly asm = Assembly.LoadFrom(Path.Combine(_tempPath, Path.Combine(@"ConfigTestAssemblies", @"V2_Unsigned", @"System.Web.WebPages.Deployment.dll")));
                Assert.Equal(new Version(2, 0, 0, 0), asm.GetName().Version);
                Assert.Equal("System.Web.WebPages.Deployment", asm.GetName().Name);

                using (WebUtils.CreateHttpRuntime(Path.Combine(@"~", @"foo"), "."))
                {
                    // Act
                    Version ver = WebPagesDeployment.GetVersionWithoutEnabledCheck(Path.Combine(_tempPath, Path.Combine(@"ConfigTestSites", @"CshtmlFileNoVersion")));

                    // Assert
                    Assert.Equal(new Version("1.0.0.0"), ver);
                }
            });
#endif
        }

        [Fact(Skip = "webForCore: side-by-side Web Pages versions (another System.Web.WebPages.Deployment loaded next to this one, as from the GAC): .NET has no GAC and an AssemblyLoadContext holds one version of a name.")]
        public void GetVersionReturnsV1IfCshtmlFilePresentButNoVersionIsSpecifiedInConfigOrBin()
        {
#if NETFRAMEWORK
            AppDomainUtils.RunInSeparateAppDomain(() =>
            {
                // Arrange - Load v2 Config
                Assembly asm = Assembly.LoadFrom(Path.Combine(_tempPath, Path.Combine(@"ConfigTestAssemblies", @"V2_Signed", @"System.Web.WebPages.Deployment.dll")));
                Assert.Equal(new Version(2, 0, 0, 0), asm.GetName().Version);
                Assert.Equal("System.Web.WebPages.Deployment", asm.GetName().Name);

                using (WebUtils.CreateHttpRuntime(Path.Combine(@"~", @"foo"), "."))
                {
                    string path = Path.Combine(_tempPath, Path.Combine(@"ConfigTestSites", @"CshtmlFileNoVersion"));

                    // Act
                    Version ver = WebPagesDeployment.GetVersionWithoutEnabledCheck(path);
                    Version explicitVersion = WebPagesDeployment.GetExplicitWebPagesVersion(path);

                    // Assert
                    Assert.Equal(new Version("1.0.0.0"), ver);
                    Assert.Null(explicitVersion);
                }
            });
#endif
        }

        [Fact]
        public void GetVersionThrowsIfPathNullOrEmpty()
        {
            Assert.ThrowsArgumentNullOrEmptyString(() => WebPagesDeployment.GetVersionWithoutEnabledCheck(null), "path");
            Assert.ThrowsArgumentNullOrEmptyString(() => WebPagesDeployment.GetVersionWithoutEnabledCheck(String.Empty), "path");
        }

        [Fact]
        public void IsEnabledThrowsIfPathNullOrEmpty()
        {
            Assert.ThrowsArgumentNullOrEmptyString(() => WebPagesDeployment.IsEnabled(null), "path");
            Assert.ThrowsArgumentNullOrEmptyString(() => WebPagesDeployment.IsEnabled(String.Empty), "path");
        }

        [Theory]
        [InlineData(new object[] { null })]
        [InlineData(new object[] { "" })]
        public void ObsoleteGetVersionThrowsIfPathIsNullOrEmpty(string path)
        {
            // Arrange
            var fileSystem = new TestFileSystem();
            var configuration = new NameValueCollection();

            // Act and Assert
            Assert.ThrowsArgumentNullOrEmptyString(() => WebPagesDeployment.GetObsoleteVersionInternal(path, configuration, fileSystem), "path");
        }

        [Fact]
        public void ObsoleteGetVersionReturnsNullIfNoFilesInTheSite()
        {
            // Arrange
            var path = "blah";
            var fileSystem = new TestFileSystem();
            var configuration = new NameValueCollection();

            // Act
            var version = WebPagesDeployment.GetObsoleteVersionInternal(path, configuration, fileSystem);

            // Assert
            Assert.Null(version);
        }

        [Fact]
        public void ObsoleteGetVersionReturnsV1VersionIfNoValueInConfigNoFilesInBinSiteContainsCshtmlFiles()
        {
            // Arrange
            var path = "blah";
            var fileSystem = new TestFileSystem();
            fileSystem.AddFile(Path.Combine(@"blah", @"Foo.cshtml"));
            var configuration = new NameValueCollection();

            // Act
            var version = WebPagesDeployment.GetObsoleteVersionInternal(path, configuration, fileSystem);

            // Assert
            Assert.Equal(new Version("1.0.0.0"), version);
        }

        [Fact]
        public void ObsoleteGetVersionReturnsVersionFromConfigIfDisabled()
        {
            // Arrange
            var maxVersion = new Version("3.0.3.4");
            var fileSystem = new TestFileSystem();
            var configuration = new NameValueCollection();
            configuration["webPages:Enabled"] = "False";
            configuration["webPages:Version"] = "3.0";
            var path = "blah";

            // Act
            var version = WebPagesDeployment.GetObsoleteVersionInternal(path, configuration, fileSystem);

            // Assert
            Assert.Equal(new Version("3.0.0.0"), version);
        }

        private static string GetTempPath()
        {
            return Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        }
    }
}
