// webForCore: replaces Microsoft.TestCommon's RuntimeEnvironment.cs (it reads the .NET Framework version from the
// registry). The fork's reference behaviour is .NET Framework 4.8 (4.5 or later).
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.TestCommon
{
    public static class RuntimeEnvironment
    {
        public static bool IsVersion45Installed
        {
            get { return true; }
        }
    }
}
