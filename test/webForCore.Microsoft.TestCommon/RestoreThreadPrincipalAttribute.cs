// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Security.Principal;
using System.Threading;

namespace Microsoft.TestCommon
{
    public class RestoreThreadPrincipalAttribute : Xunit.Sdk.BeforeAfterTestAttribute
    {
        private IPrincipal _originalPrincipal;
        // webForCore: the policy is process-wide and decides what a null principal reads as on .NET; one test at a time.
        private static readonly SemaphoreSlim s_policy = new SemaphoreSlim(1, 1);

        public override void Before(MethodInfo methodUnderTest)
        {
            s_policy.Wait();
            _originalPrincipal = Thread.CurrentPrincipal;

            AppDomain.CurrentDomain.SetPrincipalPolicy(PrincipalPolicy.NoPrincipal);

            Thread.CurrentPrincipal = null;
        }

        public override void After(MethodInfo methodUnderTest)
        {
            Thread.CurrentPrincipal = _originalPrincipal;

            AppDomain.CurrentDomain.SetPrincipalPolicy(PrincipalPolicy.UnauthenticatedPrincipal);

            s_policy.Release();
        }
    }
}
