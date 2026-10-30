#!/usr/bin/env python3
"""Replaces the sources of src/WebFormsForCore.Configuration that WebFormsForCore decompiled from .NET Framework
assemblies (System.Configuration.dll, System.dll) with their MIT-licensed originals from the .NET Framework Reference
Source (microsoft/referencesource), so that no decompiled code remains (docs/LICENSING.md).

Usage: lift_configuration.py <referencesource checkout> [ForkRoot]

The originals are copied from the pinned commit to src/WebFormsForCore.Configuration/ReferenceSource/<their path>,
byte for byte, then changed only by the FIXUPS below (each must match exactly the stated number of times, or the lift
stops). Types the Reference Source files define that .NET Framework's System.Net used internally (the *SectionInternal
classes, which applied <system.net> settings to System.Net; .NET's System.Net does not read configuration) are dropped.
Types with no MIT original (configuration builders, resource accessors) are webForCore's own, in
src/WebFormsForCore.Configuration/WebFormsForCore/. The per-file source is written to
tools/lift/manifests/System.Configuration.tsv.
"""
import os
import re
import subprocess
import sys

COMMIT = "ec9fa9ae770d522a5b5f0607898044b7478574a3"
PROJECT = "src/WebFormsForCore.Configuration"
DEST = "ReferenceSource"

# Decompiled files (no header; #nullable disable, decompiler casts) and dead copies, relative to the project.
REMOVE = [
    "AssemblyRef.cs",
    "System/Configuration/AppSettingsReader.cs",
    "System/Configuration/ApplicationScopedSettingAttribute.cs",
    "System/Configuration/ApplicationSettingsBase.cs",
    "System/Configuration/ApplicationSettingsGroup.cs",
    "System/Configuration/ClientSettingsSection.cs",
    "System/Configuration/ClientSettingsStore.cs",
    "System/Configuration/CommonConfigurationStrings.cs",
    "System/Configuration/ConfigXmlAttribute.cs",
    "System/Configuration/ConfigXmlCDataSection.cs",
    "System/Configuration/ConfigXmlComment.cs",
    "System/Configuration/ConfigXmlDocument.cs",
    "System/Configuration/ConfigXmlElement.cs",
    "System/Configuration/ConfigXmlSignificantWhitespace.cs",
    "System/Configuration/ConfigXmlText.cs",
    "System/Configuration/ConfigXmlWhitespace.cs",
    "System/Configuration/ConfigurationException.cs",
    "System/Configuration/ConfigurationManagerInternalFactory.cs",
    "System/Configuration/ConfigurationSettings.cs",
    "System/Configuration/DefaultSettingValueAttribute.cs",
    "System/Configuration/DictionarySectionHandler.cs",
    "System/Configuration/HandlerBase.cs",
    "System/Configuration/IApplicationSettingsProvider.cs",
    "System/Configuration/IConfigurationSectionHandler.cs",
    "System/Configuration/IConfigurationSystem.cs",
    "System/Configuration/IPersistComponentSettings.cs",
    "System/Configuration/ISettingsProviderService.cs",
    "System/Configuration/IdnElement.cs",
    "System/Configuration/IgnoreSectionHandler.cs",
    "System/Configuration/Internal/ConfigurationManagerHelper.cs",
    "System/Configuration/IriParsingElement.cs",
    "System/Configuration/LocalFileSettingsProvider.cs",
    "System/Configuration/NameValueFileSectionHandler.cs",
    "System/Configuration/NameValueSectionHandler.cs",
    "System/Configuration/NoSettingsVersionUpgradeAttribute.cs",
    "System/Configuration/PrivilegedConfigurationManager.cs",
    "System/Configuration/ReadOnlyNameValueCollection.cs",
    "System/Configuration/SchemeSettingElement.cs",
    "System/Configuration/SchemeSettingElementCollection.cs",
    "System/Configuration/SchemeSettingInternal.cs",
    "System/Configuration/SecurityUtils.cs",
    "System/Configuration/SettingAttribute.cs",
    "System/Configuration/SettingChangingEventArgs.cs",
    "System/Configuration/SettingChangingEventHandler.cs",
    "System/Configuration/SettingElement.cs",
    "System/Configuration/SettingElementCollection.cs",
    "System/Configuration/SettingValueElement.cs",
    "System/Configuration/SettingsAttributeDictionary.cs",
    "System/Configuration/SettingsBase.cs",
    "System/Configuration/SettingsContext.cs",
    "System/Configuration/SettingsDescriptionAttribute.cs",
    "System/Configuration/SettingsGroupDescriptionAttribute.cs",
    "System/Configuration/SettingsGroupNameAttribute.cs",
    "System/Configuration/SettingsLoadedEventArgs.cs",
    "System/Configuration/SettingsLoadedEventHandler.cs",
    "System/Configuration/SettingsManageability.cs",
    "System/Configuration/SettingsManageabilityAttribute.cs",
    "System/Configuration/SettingsProperty.cs",
    "System/Configuration/SettingsPropertyCollection.cs",
    "System/Configuration/SettingsPropertyIsReadOnlyException.cs",
    "System/Configuration/SettingsPropertyNotFoundException.cs",
    "System/Configuration/SettingsPropertyValue.cs",
    "System/Configuration/SettingsPropertyValueCollection.cs",
    "System/Configuration/SettingsPropertyWrongTypeException.cs",
    "System/Configuration/SettingsProvider.cs",
    "System/Configuration/SettingsProviderAttribute.cs",
    "System/Configuration/SettingsProviderCollection.cs",
    "System/Configuration/SettingsSavingEventHandler.cs",
    "System/Configuration/SettingsSerializeAs.cs",
    "System/Configuration/SettingsSerializeAsAttribute.cs",
    "System/Configuration/SingleTagSectionHandler.cs",
    "System/Configuration/SpecialSetting.cs",
    "System/Configuration/SpecialSettingAttribute.cs",
    "System/Configuration/StoredSetting.cs",
    "System/Configuration/TypeUtil.cs",
    "System/Configuration/UriSection.cs",
    "System/Configuration/UriSectionData.cs",
    "System/Configuration/UriSectionInternal.cs",
    "System/Configuration/UriSectionReader.cs",
    "System/Configuration/UserScopedSettingAttribute.cs",
    "System/Configuration/UserSettingsGroup.cs",
    "System/Net/Configuration/AuthenticationModuleElement.cs",
    "System/Net/Configuration/AuthenticationModuleElementCollection.cs",
    "System/Net/Configuration/AuthenticationModulesSection.cs",
    "System/Net/Configuration/AuthenticationModulesSectionInternal.cs",
    "System/Net/Configuration/BypassElement.cs",
    "System/Net/Configuration/BypassElementCollection.cs",
    "System/Net/Configuration/ConfigurationStrings.cs",
    "System/Net/Configuration/ConnectionManagementElement.cs",
    "System/Net/Configuration/ConnectionManagementElementCollection.cs",
    "System/Net/Configuration/ConnectionManagementSection.cs",
    "System/Net/Configuration/ConnectionManagementSectionInternal.cs",
    "System/Net/Configuration/DefaultProxySection.cs",
    "System/Net/Configuration/DefaultProxySectionInternal.cs",
    "System/Net/Configuration/FtpCachePolicyElement.cs",
    "System/Net/Configuration/HttpCachePolicyElement.cs",
    "System/Net/Configuration/HttpListenerElement.cs",
    "System/Net/Configuration/HttpListenerTimeoutsElement.cs",
    "System/Net/Configuration/HttpWebRequestElement.cs",
    "System/Net/Configuration/Ipv6Element.cs",
    "System/Net/Configuration/MailSettingsSectionGroup.cs",
    "System/Net/Configuration/MailSettingsSectionGroupInternal.cs",
    "System/Net/Configuration/ModuleElement.cs",
    "System/Net/Configuration/NetSectionGroup.cs",
    "System/Net/Configuration/PerformanceCountersElement.cs",
    "System/Net/Configuration/ProxyElement.cs",
    "System/Net/Configuration/RequestCachingSection.cs",
    "System/Net/Configuration/RequestCachingSectionInternal.cs",
    "System/Net/Configuration/ServicePointManagerElement.cs",
    "System/Net/Configuration/SettingsSection.cs",
    "System/Net/Configuration/SettingsSectionInternal.cs",
    "System/Net/Configuration/SmtpNetworkElement.cs",
    "System/Net/Configuration/SmtpNetworkElementInternal.cs",
    "System/Net/Configuration/SmtpSection.cs",
    "System/Net/Configuration/SmtpSectionInternal.cs",
    "System/Net/Configuration/SmtpSpecifiedPickupDirectoryElement.cs",
    "System/Net/Configuration/SmtpSpecifiedPickupDirectoryElementInternal.cs",
    "System/Net/Configuration/SocketElement.cs",
    "System/Net/Configuration/UnicodeDecodingConformance.cs",
    "System/Net/Configuration/UnicodeEncodingConformance.cs",
    "System/Net/Configuration/WebProxyScriptElement.cs",
    "System/Net/Configuration/WebRequestModuleElement.cs",
    "System/Net/Configuration/WebRequestModuleElementCollection.cs",
    "System/Net/Configuration/WebRequestModulesSection.cs",
    "System/Net/Configuration/WebRequestModulesSectionInternal.cs",
    "System/Net/Configuration/WebUtilityElement.cs",
    "System/Net/Configuration/WindowsAuthenticationElement.cs",
    "System/Net/DirectProxy.cs",
    "System/Net/EmptyWebProxy.cs",
    "System/Net/ExceptionHelper.cs",
    "System/Net/HttpAbortDelegate.cs",
    "System/Net/IAutoWebProxy.cs",
    "System/Net/NclUtilities.cs",
    "System/Net/ProxyChain.cs",
    "System/Net/SystemNetworkCredential.cs",
    "System/Net/TimeoutValidator.cs",
    "System/Net/WebProxyWrapper.cs",
    "System/Net/WebRequestPrefixElement.cs",
    "System/System.Configuration/ConfigurationBuilder.cs",
    "System/System.Configuration/ConfigurationBuilderChain.cs",
    "System/System.Configuration/ConfigurationBuilderCollection.cs",
    "System/System.Configuration/ConfigurationBuilderSettings.cs",
    "System/System.Configuration/ConfigurationBuildersSection.cs",
    "System/System.Configuration/ConfigurationException.cs",
    "System/System.Configuration/ConfigurationStringConstants.cs",
    "System/System.Configuration/IConfigurationSectionHandler.cs",
    "System/System.Configuration/IgnoreSectionHandler.cs",
    "System/System.Configuration/Internal/IInternalConfigurationBuilderHost.cs",
    "System/System.Configuration/SR.cs",
    "System/System.Configuration/SRCategoryAttribute.cs",
    "System/System.Configuration/SRDescriptionAttribute.cs",
    "System/UriIdnScope.cs",
]

# Reference Source files, relative to its repository.
COPY = [
    "System/sys/system/configuration/SettingsSerializeAs.cs",
    "System/net/System/UriEnumTypes.cs",
    "System/compmod/system/configuration/appsettingsreader.cs",
    "System.Configuration/System/Configuration/ConfigXmlAttribute.cs",
    "System.Configuration/System/Configuration/ConfigXmlCDataSection.cs",
    "System.Configuration/System/Configuration/ConfigXmlComment.cs",
    "System.Configuration/System/Configuration/ConfigXmlElement.cs",
    "System.Configuration/System/Configuration/ConfigXmlSignificantWhitespace.cs",
    "System.Configuration/System/Configuration/ConfigXmlText.cs",
    "System.Configuration/System/Configuration/ConfigXmlWhitespace.cs",
    "System/misc/SecurityUtils.cs",
    "System/net/System/Net/Configuration/AuthenticationModuleElementCollection.cs",
    "System/net/System/Net/Configuration/AuthenticationModuleElement.cs",
    "System/net/System/Net/Configuration/AuthenticationModulesSection.cs",
    "System/net/System/Net/Configuration/BypassElementCollection.cs",
    "System/net/System/Net/Configuration/BypassElement.cs",
    "System/net/System/Net/Configuration/ConfigurationStrings.cs",
    "System/net/System/Net/Configuration/ConnectionManagementElementCollection.cs",
    "System/net/System/Net/Configuration/ConnectionManagementElement.cs",
    "System/net/System/Net/Configuration/ConnectionManagementSection.cs",
    "System/net/System/Net/Configuration/DefaultProxySection.cs",
    "System/net/System/Net/Configuration/FtpCachePolicyElement.cs",
    "System/net/System/Net/Configuration/HttpCachePolicyElement.cs",
    "System/net/System/Net/Configuration/HttpListenerElement.cs",
    "System/net/System/Net/Configuration/HttpListenerTimeoutsElement.cs",
    "System/net/System/Net/Configuration/HttpWebRequestElement.cs",
    "System/net/System/Net/Configuration/IdnElement.cs",
    "System/net/System/Net/Configuration/Ipv6Element.cs",
    "System/net/System/Net/Configuration/IriParsingElement.cs",
    "System/net/System/Net/Configuration/MailSettingsSection.cs",
    "System/net/System/Net/Configuration/ModuleElement.cs",
    "System/net/System/Net/Configuration/NetSectionGroup.cs",
    "System/net/System/Net/Configuration/PerformanceCountersElement.cs",
    "System/net/System/Net/Configuration/ProxyElement.cs",
    "System/net/System/Net/Configuration/RequestCachingSection.cs",
    "System/net/System/Net/Configuration/SchemeSettingElementCollection.cs",
    "System/net/System/Net/Configuration/SchemeSettingElement.cs",
    "System/net/System/Net/Configuration/ServicePointManagerElement.cs",
    "System/net/System/Net/Configuration/SettingsSection.cs",
    "System/net/System/Net/Configuration/SmtpNetworkElement.cs",
    "System/net/System/Net/Configuration/SmtpSection.cs",
    "System/net/System/Net/Configuration/SmtpSpecifiedPickupDirectoryElement.cs",
    "System/net/System/Net/Configuration/SocketElement.cs",
    "System/net/System/Net/Configuration/TimeoutValidationAttribute.cs",
    "System/net/System/Net/Configuration/UriSection.cs",
    "System/net/System/Net/Configuration/UriSectionData.cs",
    "System/net/System/Net/Configuration/UriSectionReader.cs",
    "System/net/System/Net/Configuration/WebProxyScriptElement.cs",
    "System/net/System/Net/Configuration/WebRequestModuleElementCollection.cs",
    "System/net/System/Net/Configuration/WebRequestModuleElement.cs",
    "System/net/System/Net/Configuration/WebRequestModulesSection.cs",
    "System/net/System/Net/Configuration/WebUtilityElement.cs",
    "System/net/System/Net/UnicodeDecodingConformance.cs",
    "System/net/System/Net/UnicodeEncodingConformance.cs",
    "System/sys/system/configuration/ApplicationSettingsBase.cs",
    "System/sys/system/configuration/ClientSettingsStore.cs",
    "System/sys/system/configuration/ConfigurationException.cs",
    "System/sys/system/configuration/ConfigurationManagerInternalFactory.cs",
    "System/sys/system/configuration/ConfigurationSettings.cs",
    "System/sys/system/configuration/ConfigXmlDocument.cs",
    "System/sys/system/configuration/DictionarySectionHandler.cs",
    "System/sys/system/configuration/HandlerBase.cs",
    "System/sys/system/configuration/IApplicationSettingsProvider.cs",
    "System/sys/system/configuration/IConfigurationSectionHandler.cs",
    "System/sys/system/configuration/IConfigurationSystem.cs",
    "System/sys/system/configuration/IgnoreSectionHandler.cs",
    "System/sys/system/configuration/internal/ConfigurationManagerHelper.cs",
    "System/sys/system/configuration/IPersistSettings.cs",
    "System/sys/system/configuration/ISettingsProviderService.cs",
    "System/sys/system/configuration/LocalFileSettingsProvider.cs",
    "System/sys/system/configuration/NameValueFileSectionHandler.cs",
    "System/sys/system/configuration/NameValueSectionHandler.cs",
    "System/sys/system/configuration/ReadOnlyNameValueCollection.cs",
    "System/sys/system/configuration/sections/ClientSettingsGroups.cs",
    "System/sys/system/configuration/sections/ClientSettingsSection.cs",
    "System/sys/system/configuration/SettingsAttributeDictionary.cs",
    "System/sys/system/configuration/SettingsAttributes.cs",
    "System/sys/system/configuration/SettingsBase.cs",
    "System/sys/system/configuration/SettingsContext.cs",
    "System/sys/system/configuration/SettingsPropertyCollection.cs",
    "System/sys/system/configuration/SettingsProperty.cs",
    "System/sys/system/configuration/SettingsPropertyIsReadOnlyException.cs",
    "System/sys/system/configuration/SettingsPropertyNotFoundException.cs",
    "System/sys/system/configuration/SettingsPropertyValueCollection.cs",
    "System/sys/system/configuration/SettingsPropertyValue.cs",
    "System/sys/system/configuration/SettingsPropertyWrongTypeException.cs",
    "System/sys/system/configuration/SettingsProviderCollection.cs",
    "System/sys/system/configuration/SettingsProvider.cs",
    "System/sys/system/configuration/SingleTagSectionHandler.cs",
]

# Types left out of a copied file (System.Net's internal configuration plumbing).
DROP_TYPES = {
    "System/net/System/Net/Configuration/AuthenticationModulesSection.cs": ["AuthenticationModulesSectionInternal"],
    "System/net/System/Net/Configuration/ConnectionManagementSection.cs": ["ConnectionManagementSectionInternal"],
    "System/net/System/Net/Configuration/DefaultProxySection.cs": ["DefaultProxySectionInternal"],
    "System/net/System/Net/Configuration/MailSettingsSection.cs": ["MailSettingsSectionGroupInternal"],
    "System/net/System/Net/Configuration/RequestCachingSection.cs": ["RequestCachingSectionInternal"],
    "System/net/System/Net/Configuration/SettingsSection.cs": ["SettingsSectionInternal"],
    "System/net/System/Net/Configuration/SmtpNetworkElement.cs": ["SmtpNetworkElementInternal"],
    "System/net/System/Net/Configuration/SmtpSection.cs": ["SmtpSectionInternal"],
    "System/net/System/Net/Configuration/SmtpSpecifiedPickupDirectoryElement.cs": ["SmtpSpecifiedPickupDirectoryElementInternal"],
    "System/net/System/Net/Configuration/WebRequestModulesSection.cs": ["WebRequestModulesSectionInternal"],
}

# Types kept from a copied file, the rest left out.
KEEP_TYPES = {
    "System/net/System/UriEnumTypes.cs": ["UriIdnScope"],
}

# (old, new, count) per copied file.
FIXUPS = {
    # .NET Framework wrote these defaults as typeof(System.dll-internal type); the same type names, as strings.
    "System/net/System/Net/Configuration/AuthenticationModulesSection.cs": [
        ("typeof(NegotiateClient).AssemblyQualifiedName", '"System.Net.NegotiateClient, " + AssemblyRef.System', 1),
        ("typeof(KerberosClient).AssemblyQualifiedName", '"System.Net.KerberosClient, " + AssemblyRef.System', 1),
        ("typeof(NtlmClient).AssemblyQualifiedName", '"System.Net.NtlmClient, " + AssemblyRef.System', 1),
        ("typeof(DigestClient).AssemblyQualifiedName", '"System.Net.DigestClient, " + AssemblyRef.System', 1),
        ("typeof(BasicClient).AssemblyQualifiedName", '"System.Net.BasicClient, " + AssemblyRef.System', 1),
    ],
    "System/net/System/Net/Configuration/WebRequestModulesSection.cs": [
        ("typeof(HttpRequestCreator)", 'Type.GetType("System.Net.HttpRequestCreator, " + AssemblyRef.System, false) ?? typeof(object)', 2),
        ("typeof(FileWebRequestCreator)", 'Type.GetType("System.Net.FileWebRequestCreator, " + AssemblyRef.System, false) ?? typeof(object)', 1),
        ("typeof(FtpWebRequestCreator)", 'Type.GetType("System.Net.FtpWebRequestCreator, " + AssemblyRef.System, false) ?? typeof(object)', 1),
    ],
    # Primes .NET Framework's System.Net with machine.config; .NET's System.Net does not read configuration.
    "System/net/System/Net/Configuration/SettingsSection.cs": [
        ("""            try {
                //AuthenticationModules section
                System.Net.AuthenticationManager.EnsureConfigLoaded();
                //Requestcachingsection section
                object o = System.Net.Cache.RequestCacheManager.IsCachingEnabled;
                //ConnectionManagement section
                o = System.Net.ServicePointManager.DefaultConnectionLimit;
                //Settings section
                o = System.Net.ServicePointManager.Expect100Continue;
                //webrequestmodules section
                o = System.Net.WebRequest.PrefixList;
                //DefaultProxy section
                o = System.Net.WebRequest.InternalDefaultWebProxy;
            }
            catch {
            }
""", "            // webForCore: nothing to prime; .NET's System.Net does not read <system.net>.\n", 1),
    ],
    # .NET's UriTypeConverter has no UriKind constructor.
    "System/net/System/Net/Configuration/ProxyElement.cs": [
        ("new UriTypeConverter(UriKind.Absolute)", "new UriTypeConverter()", 2),
    ],
    "System/net/System/Net/Configuration/UriSection.cs": [
        ("appConfigFilePath = AppDomain.CurrentDomain.SetupInformation.ConfigurationFile;",
         'appConfigFilePath = AppContext.GetData("APP_CONFIG_FILE") as string; // webForCore: no AppDomainSetup.ConfigurationFile', 1),
    ],
    # The file picks its namespace from a compilation symbol; System.dll compiled it in System.
    "System/misc/SecurityUtils.cs": [
        ("//------------------------------------------------------------------------------\n// <copyright file=\"SecurityUtils.cs\"",
         "#define SYSTEM_NAMESPACE // webForCore: as System.dll compiled it\n//------------------------------------------------------------------------------\n// <copyright file=\"SecurityUtils.cs\"", 1),
        ("""            PermissionSet targetGrantSet = assembly.PermissionSet;
            targetGrantSet.AddPermission(RestrictedMemberAccessPermission);
            targetGrantSet.Demand();
""", "            // webForCore: no Code Access Security (Assembly.PermissionSet) on .NET.\n", 1),
    ],
    # No ClickOnce (ActivationContext, System.Deployment) on .NET.
    "System/sys/system/configuration/ApplicationSettingsBase.cs": [
        ("""            // First check if the app is ClickOnce deployed
            ActivationContext actCtx = AppDomain.CurrentDomain.ActivationContext;
            
            if (IsClickOnceDeployed(AppDomain.CurrentDomain)) {
                // Now check if this is the first run since deployment or last upgrade
                return System.Deployment.Internal.InternalActivationContextHelper.IsFirstRun(actCtx);
            }

            return false;
""", "            return false; // webForCore: no ClickOnce on .NET\n", 1),
        ("""            ActivationContext actCtx = appDomain.ActivationContext;

            // Ensures the app is running with a context from the store.
            if (actCtx != null && actCtx.Form == ActivationContext.ContextForm.StoreBounded) {
                string fullAppId = actCtx.Identity.FullName;
                if (!String.IsNullOrEmpty(fullAppId)) {
                    return true;
                }
            }

            return false;
""", "            return false; // webForCore: no ClickOnce on .NET\n", 1),
    ],
    # .NET's BinaryFormatter throws; webForCore's port (WebFormsForCore.Serialization.Formatters) reads and writes the format.
    "System/sys/system/configuration/SettingsPropertyValue.cs": [
        ("(new BinaryFormatter())", "(new WebFormsForCore.Serialization.Formatters.Binary.BinaryFormatter())", 2),
        ("BinaryFormatter bf = new BinaryFormatter();", "var bf = new WebFormsForCore.Serialization.Formatters.Binary.BinaryFormatter();", 2),
    ],
}

# In System.dll the section classes override System.Configuration's protected internal members across assemblies, so
# they are declared protected; here they share the assembly and must be protected internal. The members are read from
# the project's own base classes (ConfigurationElement, ConfigurationElementCollection, ConfigurationSection, ...).
def protected_internal_members(project):
    names = set()
    for root, _, files in os.walk(project):
        if DEST in root.split(os.sep) or os.sep + "obj" in root or os.sep + "bin" in root:
            continue
        for f in files:
            if f.endswith(".cs"):
                text = open(os.path.join(root, f), encoding="utf-8-sig", errors="replace").read()
                for m in re.finditer(r"protected internal (?:virtual|abstract) [\w<>\[\], .]+?\b(\w+)\s*[({]", text):
                    names.add(m.group(1))
    return sorted(names)


def add_missing_strings(project, rs):
    """Adds the resource strings the code uses (SR.Name) but System/Configuration.resx lacks, with their text from the
    Reference Source's System/System.txt (System.dll's strings), then regenerates SR (tools/lift/gen_sr.py)."""
    resx = os.path.join(project, "System", "Configuration.resx")
    text = open(resx, encoding="utf-8-sig").read()
    have = set(re.findall(r'<data name="([^"]+)"', text))
    used = set()
    for root, _, files in os.walk(project):
        if os.sep + "obj" in root or os.sep + "bin" in root:
            continue
        for f in files:
            if f.endswith(".cs") and not f.endswith(".g.cs"):
                used |= set(re.findall(r"\bSR\.([A-Za-z_]\w*)\b", open(os.path.join(root, f), encoding="utf-8-sig", errors="replace").read()))
    used -= {"GetString", "GetObject", "Resources"}
    strings = {}
    for line in git_show(rs, "System/System.txt").decode("utf-8-sig", errors="replace").splitlines():
        if "=" in line and not line.lstrip().startswith((";", "#", "[")):
            name, value = line.split("=", 1)
            strings[name.strip()] = value
    entries = []
    for name in sorted(used - have):
        if name not in strings:
            sys.exit(f"resource string {name} is in neither Configuration.resx nor System.txt")
        value = strings[name].replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")
        entries.append(f'  <data name="{name}" xml:space="preserve">\n    <value>{value}</value>\n'
                       f'    <comment>webForCore: from microsoft/referencesource System/System.txt</comment>\n  </data>\n')
    if entries:
        text = text.replace("</root>", "".join(entries) + "</root>")
        with open(resx, "w", encoding="utf-8") as f:
            f.write(text)
    gen = os.path.join(os.path.dirname(os.path.abspath(__file__)), "gen_sr.py")
    subprocess.run([sys.executable, gen, resx, os.path.join(project, "WebFormsForCore", "SR.g.cs"),
                    "--namespace", "System.Configuration", "--class", "SR", "--resource", "System.Configuration"], check=True)
    return len(entries)


def git_show(checkout, path):
    return subprocess.run(["git", "-C", checkout, "show", f"{COMMIT}:{path}"], capture_output=True, check=True).stdout


def type_span(text, name):
    """The span of a type declaration (with its attributes and doc comments) named name, or None."""
    m = re.search(r"^[ \t]*(?:\[[^\n]*\]\s*)*(?:(?:public|internal|private|protected|sealed|static|abstract|partial|unsafe)\s+)*"
                  r"(?:class|struct|interface|enum)\s+" + re.escape(name) + r"\b", text, re.M)
    if not m:
        m = re.search(r"^[ \t]*(?:(?:public|internal)\s+)?delegate\s+[^;]*\b" + re.escape(name) + r"\s*\([^;]*;[^\n]*\n", text, re.M)
        if not m:
            return None
        start = m.start()
        return start, m.end()
    start = m.start()
    # include preceding /// comments and attribute lines
    prev = text.rfind("\n", 0, start - 1)
    while prev >= 0:
        line = text[prev + 1:start].strip()
        if line.startswith("///") or line.startswith("[") or line.startswith("//"):
            start = prev + 1
            prev = text.rfind("\n", 0, start - 1)
        else:
            break
    i = text.index("{", m.end())
    depth, j = 0, i
    in_str = in_chr = False
    while j < len(text):
        c = text[j]
        if text.startswith("//", j) and not in_str and not in_chr:
            j = text.index("\n", j)
            continue
        if text.startswith("/*", j) and not in_str and not in_chr:
            j = text.index("*/", j) + 2
            continue
        if c == '"' and not in_chr:
            if in_str and text[j - 1] == "\\":
                pass
            else:
                in_str = not in_str
        elif c == "'" and not in_str:
            in_chr = not in_chr if text[j - 1] != "\\" else in_chr
        elif not in_str and not in_chr:
            if c == "{":
                depth += 1
            elif c == "}":
                depth -= 1
                if depth == 0:
                    end = text.index("\n", j) + 1 if "\n" in text[j:] else j + 1
                    return start, end
        j += 1
    raise ValueError(f"unbalanced braces in {name}")


def main():
    rs = sys.argv[1]
    fork = sys.argv[2] if len(sys.argv) > 2 else os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    project = os.path.join(fork, PROJECT)
    for rel in REMOVE:
        path = os.path.join(project, rel)
        if os.path.exists(path):
            os.remove(path)
    protected = protected_internal_members(project)
    manifest = [f"# microsoft/referencesource {COMMIT[:7]}: source path (relative to repo root)\tdestination (relative to fork root)"]
    for src in COPY:
        raw = git_show(rs, src)
        bom = raw.startswith(b"\xef\xbb\xbf")
        try:
            text, encoding = raw.decode("utf-8-sig"), "utf-8-sig" if bom else "utf-8"
        except UnicodeDecodeError:
            text, encoding = raw.decode("cp1252"), "cp1252"  # some Reference Source files are Windows-1252
        crlf = "\r\n" in text
        text = text.replace("\r\n", "\n")
        for name in DROP_TYPES.get(src, []):
            span = type_span(text, name)
            if span is None:
                sys.exit(f"{src}: type {name} not found")
            text = text[:span[0]] + f"    // webForCore: {name} left out (.NET Framework's System.Net only; see lift_configuration.py).\n" + text[span[1]:]
        if src in KEEP_TYPES:
            keep = []
            for name in KEEP_TYPES[src]:
                span = type_span(text, name)
                if span is None:
                    sys.exit(f"{src}: type {name} not found")
                keep.append(text[span[0]:span[1]])
            head = text[:text.index("namespace")]
            ns = re.search(r"namespace\s+[\w.]+", text).group(0)
            text = head + ns + " {\n    // webForCore: only " + ", ".join(KEEP_TYPES[src]) + " kept from this file (see lift_configuration.py).\n\n" + "\n".join(keep) + "}\n"
        if "/net/" in src or "/sys/" in src or "/compmod/" in src:
            pattern = r"\bprotected override (?=[\w<>\[\], .]*\b(" + "|".join(protected) + r")\b\s*[({])"
            text = re.sub(pattern, "protected internal override ", text)
        for old, new, count in FIXUPS.get(src, []):
            found = text.count(old)
            if found != count:
                sys.exit(f"{src}: fixup matched {found} times, expected {count}: {old!r}")
            text = text.replace(old, new)
        dest = os.path.join(project, DEST, src)
        os.makedirs(os.path.dirname(dest), exist_ok=True)
        if crlf:
            text = text.replace("\n", "\r\n")
        with open(dest, "w", encoding=encoding, newline="") as f:
            f.write(text)
        manifest.append(f"{src}\t{PROJECT}/{DEST}/{src}")
    added = add_missing_strings(project, rs)
    print(f"{added} resource strings added from System.txt")
    with open(os.path.join(fork, "tools", "lift", "manifests", "System.Configuration.tsv"), "w", encoding="utf-8") as f:
        f.write("\n".join(manifest) + "\n")
    print(f"{len(REMOVE)} decompiled files removed, {len(COPY)} Reference Source files copied")


if __name__ == "__main__":
    main()
