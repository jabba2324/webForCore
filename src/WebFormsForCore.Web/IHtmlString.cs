//------------------------------------------------------------------------------
// <copyright file="IHtmlString.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

// webForCore: compiled on .NET too, like Microsoft's HttpUtility (was #if NETFRAMEWORK, using .NET's System.Web.IHtmlString)
using System.Diagnostics.CodeAnalysis;

namespace System.Web {
    // Marker interface implemented by objects that should NOT be HTML encoded when <%: o %> is used
    public interface IHtmlString {
        string ToHtmlString();
    }
}
