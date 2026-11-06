// webForCore (#213): applies <system.diagnostics> to .NET's System.Diagnostics. MIT license (webForCore).
//
// On .NET Framework, System.dll's Trace, TraceSource and Switch read the section themselves (DiagnosticsConfiguration),
// through whatever configuration system the process has: ASP.NET's, in an application, so Web.config's. .NET's
// System.Diagnostics has no configuration system; it raises public events instead (Trace.Refreshing,
// TraceSource.Initializing, Switch.Initializing, .NET 7+) for a configuration system to handle, and .NET's
// System.Configuration.ConfigurationManager handles them once TraceConfiguration.Register() is called. webForCore's
// System.Configuration.ConfigurationManager, .NET Framework's System.Configuration, has the same public
// TraceConfiguration.Register(), and System.Web calls it when the application starts.
//
// What each handler does is .NET Framework's: the reference source's TraceInternal.Listeners and InitializeSettings,
// TraceSource.Initialize and Refresh, and Switch.InitializeWithStatus (microsoft/referencesource
// System/compmod/system/diagnostics), over the reference source's configuration classes (SystemDiagnosticsSection,
// ListenerElementsCollection, ...), which webForCore compiles from the external/referencesource submodule. Where the
// reference source used System.dll's internals, these use .NET's public members:
// - TraceListener.initializeData and TraceFilter.initializeData, which ListenerElement and FilterElement compare on a
//   refresh, are kept here (TraceInitializeData);
// - attributes are copied to the public Attributes; .NET Framework first checked them against GetSupportedAttributes(),
//   which is protected and which .NET doesn't call either, so an attribute the type doesn't support isn't an error;
// - XmlWriterTraceListener's shouldRespectFilterOnTraceTransfer (a configured filter also applies to TraceTransfer) has
//   no public counterpart.
// .NET's Trace.Refresh() raises Trace.Refreshing first, then refreshes switches and sources (which raise their events),
// and last resets Trace's listeners to a DefaultTraceListener, and AutoFlush and IndentSize to their defaults. No event
// follows, so after Trace.Refresh() <trace> isn't applied again (docs/NAMESPACES.md).

namespace System.Diagnostics {
    using System.Collections;
    using System.Collections.Generic;
    using System.Configuration;
    using System.Runtime.CompilerServices;

    /// <summary>
    /// Registers the configuration system to apply the &lt;system.diagnostics&gt; section of the application's configuration
    /// file to <see cref="Trace"/>, <see cref="TraceSource"/> and <see cref="Switch"/>, as .NET's
    /// System.Configuration.ConfigurationManager does.
    /// </summary>
    public static class TraceConfiguration {
        private static readonly object s_lock = new object();
        private static volatile bool s_registered;

        // TraceSources this has initialized: the next Initializing event for one is a refresh (TraceSource.Refresh).
        private static readonly ConditionalWeakTable<TraceSource, object> s_initializedSources = new ConditionalWeakTable<TraceSource, object>();

        /// <summary>
        /// Register the configuration system to apply settings from configuration files to
        /// <see cref="TraceSource"/> and related classes.
        /// </summary>
        public static void Register() {
            if (s_registered)
                return;

            lock (s_lock) {
                if (s_registered)
                    return;

                Trace.Refreshing += RefreshingConfiguration;
                Switch.Initializing += InitializingSwitch;
                TraceSource.Initializing += InitializingTraceSource;
                s_registered = true;

                // .NET Framework read <trace> when Trace was first used; .NET raises no event for it, so it is applied now.
                ConfigureTrace();
            }
        }

        // Trace.Refresh(): the reference source's Trace.Refresh() refreshed DiagnosticsConfiguration first.
        private static void RefreshingConfiguration(object sender, EventArgs e) {
            DiagnosticsConfiguration.Refresh();
        }

        // The reference source's TraceInternal.InitializeSettings and TraceInternal.Listeners.
        private static void ConfigureTrace() {
            SystemDiagnosticsSection configSectionSav = DiagnosticsConfiguration.SystemDiagnosticsSection;
            if (configSectionSav == null)
                return;

            Trace.IndentSize = DiagnosticsConfiguration.IndentSize;
            Trace.AutoFlush = DiagnosticsConfiguration.AutoFlush;
            Trace.UseGlobalLock = DiagnosticsConfiguration.UseGlobalLock;

            // .NET Framework's Trace.Listeners was the configured listeners when first used. Here the process may have run
            // code before (the application's Program.cs): a listener it added stays, after the configured ones; .NET's
            // own default listener is replaced by configuration's.
            List<TraceListener> listeners = configSectionSav.Trace.Listeners.GetRuntimeObject();
            foreach (TraceListener existing in Trace.Listeners) {
                if (!(existing is DefaultTraceListener && existing.Name == "Default"))
                    listeners.Add(existing);
            }
            Trace.Listeners.Clear();
            foreach (TraceListener listener in listeners)
                Trace.Listeners.Add(listener);
        }

        // The reference source's TraceSource.Initialize, the first time; then TraceSource.Refresh.
        private static void InitializingTraceSource(object sender, InitializingTraceSourceEventArgs e) {
            TraceSource traceSource = e.TraceSource;
            bool refresh = s_initializedSources.TryGetValue(traceSource, out _);

            SourceElementsCollection sources = DiagnosticsConfiguration.Sources;
            if (!refresh) {
                if (sources != null) {
                    SourceElement sourceElement = sources[traceSource.Name];
                    if (sourceElement != null) {
                        if (!String.IsNullOrEmpty(sourceElement.SwitchName)) {
                            CreateSwitch(traceSource, sourceElement.SwitchType, sourceElement.SwitchName);
                        }
                        else {
                            CreateSwitch(traceSource, sourceElement.SwitchType, traceSource.Name);

                            if (!String.IsNullOrEmpty(sourceElement.SwitchValue))
                                traceSource.Switch.Level = (SourceLevels) Enum.Parse(typeof(SourceLevels), sourceElement.SwitchValue);
                        }

                        ReplaceListeners(traceSource, sourceElement.Listeners.GetRuntimeObject());
                        ReplaceAttributes(traceSource.Attributes, sourceElement.Attributes);
                        e.WasInitialized = true;
                    }
                }

                // Without configuration, .NET's TraceSource adds its DefaultTraceListener (the reference source's NoConfigInit).
                s_initializedSources.AddOrUpdate(traceSource, null);
                return;
            }

            e.WasInitialized = true;
            if (sources != null) {
                SourceElement sourceElement = sources[traceSource.Name];
                SourceSwitch internalSwitch = traceSource.Switch;
                if (sourceElement != null) {

                    // first check if the type changed
                    if ((String.IsNullOrEmpty(sourceElement.SwitchType) && internalSwitch.GetType() != typeof(SourceSwitch)) ||
                         (sourceElement.SwitchType != internalSwitch.GetType().AssemblyQualifiedName)) {

                        if (!String.IsNullOrEmpty(sourceElement.SwitchName)) {
                            CreateSwitch(traceSource, sourceElement.SwitchType, sourceElement.SwitchName);
                        }
                        else {
                            CreateSwitch(traceSource, sourceElement.SwitchType, traceSource.Name);

                            if (!String.IsNullOrEmpty(sourceElement.SwitchValue))
                                traceSource.Switch.Level = (SourceLevels) Enum.Parse(typeof(SourceLevels), sourceElement.SwitchValue);
                        }
                    }
                    else if (!String.IsNullOrEmpty(sourceElement.SwitchName)) {
                        // create a new switch if the name changed, otherwise just refresh.
                        if (sourceElement.SwitchName != internalSwitch.DisplayName)
                            CreateSwitch(traceSource, sourceElement.SwitchType, sourceElement.SwitchName);
                        else
                            internalSwitch.Refresh();
                    }
                    else {
                        // the switchValue changed.  Just update our internalSwitch.
                        if (!String.IsNullOrEmpty(sourceElement.SwitchValue))
                            internalSwitch.Level = (SourceLevels) Enum.Parse(typeof(SourceLevels), sourceElement.SwitchValue);
                        else
                            internalSwitch.Level = SourceLevels.Off;
                    }

                    TraceListenerCollection listeners = traceSource.Listeners;
                    List<TraceListener> newListenerCollection = new List<TraceListener>();
                    foreach (ListenerElement listenerElement in sourceElement.Listeners) {
                        TraceListener listener = listeners[listenerElement.Name];
                        if (listener != null) {
                            newListenerCollection.Add(listenerElement.RefreshRuntimeObject(listener));
                        }
                        else {
                            newListenerCollection.Add(listenerElement.GetRuntimeObject());
                        }
                    }

                    ReplaceAttributes(traceSource.Attributes, sourceElement.Attributes);
                    ReplaceListeners(traceSource, newListenerCollection);
                }
                else {
                    // there was no config, so clear whatever we have.
                    internalSwitch.Level = traceSource.DefaultLevel;
                    traceSource.Listeners.Clear();
                    traceSource.Attributes.Clear();
                }
            }
        }

        private static void CreateSwitch(TraceSource traceSource, string typename, string name) {
            if (!String.IsNullOrEmpty(typename))
                traceSource.Switch = (SourceSwitch) TraceUtils.GetRuntimeObject(typename, typeof(SourceSwitch), name);
            else
                traceSource.Switch = new SourceSwitch(name, traceSource.DefaultLevel.ToString());
        }

        private static void ReplaceListeners(TraceSource traceSource, List<TraceListener> listeners) {
            traceSource.Listeners.Clear();
            foreach (TraceListener listener in listeners)
                traceSource.Listeners.Add(listener);
        }

        // The reference source's Switch.InitializeWithStatus, with configuration: the switch's value and attributes from
        // <switches>. Without an <add> for it, .NET's Switch takes its default value itself.
        private static void InitializingSwitch(object sender, InitializingSwitchEventArgs e) {
            Switch sw = e.Switch;
            SwitchElementsCollection switchSettings = DiagnosticsConfiguration.SwitchSettings;
            if (switchSettings == null)
                return;

            SwitchElement mySettings = switchSettings[sw.DisplayName];
            if (mySettings == null)
                return;

            string value = mySettings.Value;
            // The reference source's Switch.Value setter, which reports a value the switch rejects as a configuration error.
            try {
                sw.Value = value ?? sw.DefaultValue;
            }
            catch (ArgumentException ex) {
                throw new ConfigurationErrorsException(SR.GetString(SR.BadConfigSwitchValue, sw.DisplayName), ex);
            }
            catch (FormatException ex) {
                throw new ConfigurationErrorsException(SR.GetString(SR.BadConfigSwitchValue, sw.DisplayName), ex);
            }
            catch (OverflowException ex) {
                throw new ConfigurationErrorsException(SR.GetString(SR.BadConfigSwitchValue, sw.DisplayName), ex);
            }

            ReplaceAttributes(sw.Attributes, mySettings.Attributes);
        }

        // StringDictionary.ReplaceHashtable, which is internal: the dictionary gets the configuration's attributes.
        internal static void ReplaceAttributes(System.Collections.Specialized.StringDictionary attributes, Hashtable configured) {
            attributes.Clear();
            foreach (DictionaryEntry entry in configured)
                attributes[(string) entry.Key] = (string) entry.Value;
        }
    }

    // The initializeData each listener and filter was created with (TraceListener.initializeData and
    // TraceFilter.initializeData on .NET Framework), for ListenerElement.RefreshRuntimeObject and
    // FilterElement.RefreshRuntimeObject.
    internal static class TraceInitializeData {
        private static readonly ConditionalWeakTable<object, string> s_initData = new ConditionalWeakTable<object, string>();

        internal static void Set(object listenerOrFilter, string initData) {
            s_initData.AddOrUpdate(listenerOrFilter, initData);
        }

        internal static string Get(object listenerOrFilter) {
            return s_initData.TryGetValue(listenerOrFilter, out string initData) ? initData : null;
        }
    }

    // The type a <system.diagnostics> element names (a listener, filter or switch type). TraceUtils called Type.GetType in
    // System.dll, so a name without an assembly found System.dll's types (System.Diagnostics.TextWriterTraceListener,
    // EventTypeFilter, ...). On .NET they are in other assemblies, and .NET's System.dll forwards to them; the event log's
    // are in System.Diagnostics.EventLog, which it doesn't forward. Names with an assembly resolve as before: the
    // application's assemblies, and System.dll's types by their .NET Framework names (System, Version=4.0.0.0, ...).
    internal static class TraceConfigurationTypes {
        private static readonly string[] s_systemAssemblies = { "System", "System.Diagnostics.EventLog" };

        internal static Type GetType(string typeName) {
            Type type = Type.GetType(typeName);
            if (type != null || typeName.IndexOf(',') >= 0)
                return type;

            foreach (string assembly in s_systemAssemblies) {
                type = Type.GetType(typeName + ", " + assembly);
                if (type != null)
                    return type;
            }
            return null;
        }
    }
}
