/*++
Copyright (c) 2003 Microsoft Corporation

Module Name:

    UriEnumTypes.cs

Abstract:

    Defines enum types used by System.Uri class

Author:
    Alexei Vopilov    Nov 21 2003

Revision History:

--*/
namespace System {
    // webForCore: only UriIdnScope kept from this file (see lift_configuration.py).

    // This is used to control when host names are converted to idn names and
    // vice versa
    public enum UriIdnScope
    {
        None,                   // Never use Idn
        AllExceptIntranet,      // Use Idn in Internet and not intranet
        All                     // Internet and intranet
    }
}
