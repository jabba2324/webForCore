// webForCore: added to the lifted Web Stack code (not from aspnet/AspNetWebStack).
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net.Http;

namespace System.Net.Http.Internal
{
    // On .NET Framework, HttpResponseMessage.Content is null when no content was set. On .NET it never is: the getter
    // stores and returns an internal EmptyContent. Web API's code reads a null response content as "no body" (no
    // content headers, no buffering decision, nothing written), so it asks here instead of reading Content directly.
    internal static class ResponseContent
    {
        private static readonly Type EmptyContentType = GetEmptyContentType();

        public static HttpContent Get(HttpResponseMessage response)
        {
            HttpContent content = response.Content;
            return content != null && content.GetType() == EmptyContentType ? null : content;
        }

        private static Type GetEmptyContentType()
        {
            using (HttpResponseMessage response = new HttpResponseMessage())
            {
                // null on .NET Framework (and .NET Core before 5.0)
                return response.Content == null ? null : response.Content.GetType();
            }
        }
    }
}
