// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.TestCommon;

namespace System.Web.Razor.Test.Utils
{
    class MiscUtils
    {
        public const int TimeoutInSeconds = 1;

        public static string StripRuntimeVersion(string s)
        {
            // webForCore: .NET's System.CodeDom no longer writes the "Runtime Version:" header line that .NET Framework's did,
            // so drop the line itself (from generated code and baselines alike) rather than only its version number.
            return Regex.Replace(s, @"(//|')\s*Runtime Version:[^\r\n]*\r?\n", "");
        }

        // webForCore: Razor's generator writes Environment.NewLine into some snippets (\n on Linux) while the baselines were
        // recorded on Windows (\r\n). Product output keeps the platform newline, so compare generated code newline-agnostically.
        public static string NormalizeGeneratedCode(string s)
        {
            return StripRuntimeVersion(s).Replace("\r\n", "\n");
        }

        public static void DoWithTimeoutIfNotDebugging(Func<int, bool> withTimeout)
        {
#if DEBUG
            if (Debugger.IsAttached)
            {
                withTimeout(Timeout.Infinite);
            }
            else
            {
#endif
                Assert.True(withTimeout((int)TimeSpan.FromSeconds(TimeoutInSeconds).TotalMilliseconds), "Timeout expired!");
#if DEBUG
            }
#endif
        }
    }
}
