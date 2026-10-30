//------------------------------------------------------------------------------
// <copyright file="GCUtil.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>                                                                
//------------------------------------------------------------------------------

namespace System.Web.Util {
    using System;
    using System.Runtime.InteropServices;
    using System.Security.Permissions;

    // Provides helper methods for wrapping managed objects so that they can be passed to and from unmanaged code.
    // Unmanaged code can't actually inspect these objects at all since they're just GCHandles. And these handles
    // cannot be reused (they're single-shot).

    [SecurityPermission(SecurityAction.LinkDemand, UnmanagedCode = true)]
    internal static class GCUtil {

        public static IntPtr RootObject(object obj) {
            return (obj != null)
                ? (IntPtr)Alloc(obj)
                : IntPtr.Zero;
        }

#if NETCOREAPP
        // webForCore: AppDomain.Unload freed the domain's GC handles. In a PseudoAppDomain the strong handles System.Web
        // allocates are cleared when the domain unloads: a handle still allocated then (a timer, a cache, a file watcher
        // callback; most were for IIS's native code) would keep the domain from being collected. Cleared, not freed: the
        // owner may still read it while the domain drains, and a freed handle's slot is reused. Its later Free is skipped.
        static readonly System.Collections.Concurrent.ConcurrentDictionary<IntPtr, GCHandle> s_domainHandles =
            InitDomainHandles();

        static System.Collections.Concurrent.ConcurrentDictionary<IntPtr, GCHandle> InitDomainHandles() {
            var handles = new System.Collections.Concurrent.ConcurrentDictionary<IntPtr, GCHandle>();
            return handles;
        }

        // Called by the PseudoAppDomain when it unloads, and while it is not collected (handles allocated meanwhile).
        internal static int ReleaseDomainHandles() {
            int released = 0;
            foreach (var key in s_domainHandles.Keys) {
                if (s_domainHandles.TryRemove(key, out GCHandle handle)) {
                    handle.Target = null;
                    released++;
                }
            }
            return released;
        }

        static readonly bool s_inDomain = System.Web.Hosting.PseudoAppDomain.GetDomain(typeof(GCUtil).Assembly) != null;

        public static GCHandle Alloc(object obj) {
            GCHandle handle = GCHandle.Alloc(obj);
            if (s_inDomain) {
                s_domainHandles[(IntPtr)handle] = handle;
            }
            return handle;
        }

        // Frees a handle from Alloc (it leaves the registry first: a freed handle's slot is reused).
        public static void Free(GCHandle handle) {
            if (s_inDomain && !s_domainHandles.TryRemove((IntPtr)handle, out _)) {
                return; // cleared by the unload, which owns it now
            }
            handle.Free();
        }
#else
        public static GCHandle Alloc(object obj) => GCHandle.Alloc(obj);
        public static void Free(GCHandle handle) => handle.Free();
#endif

        public static object UnrootObject(IntPtr pointer) {
            if (pointer != IntPtr.Zero) {
                GCHandle gcHandle = (GCHandle)pointer;
                if (gcHandle.IsAllocated) {
                    object target = gcHandle.Target;
                    Free(gcHandle);
                    return target;
                }
            }

            return null;
        }

    }

    // This wrapper around a managed object is opaque to SizedReference GC handle
    // and therefore helps with calculating size of only relevant graph of objects
    internal class DisposableGCHandleRef<T> : IDisposable
    where T : class, IDisposable {
        GCHandle _handle;
        [PermissionSet(SecurityAction.Assert, Unrestricted = true)]
        public DisposableGCHandleRef(T t) {
            Debug.Assert(t != null);
            _handle = GCUtil.Alloc(t);
        }

        public T Target {
            [PermissionSet(SecurityAction.Assert, Unrestricted = true)]
            get {
                Debug.Assert(_handle.IsAllocated);
                return (T)_handle.Target;
            }
        }

        [PermissionSet(SecurityAction.Assert, Unrestricted = true)]
        public void Dispose() {
            Target.Dispose();
            Debug.Assert(_handle.IsAllocated);
            if (_handle.IsAllocated) {
                GCUtil.Free(_handle);
            }
        }
    }
}
