// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.ComponentModel;
// webForCore: System.Data.Linq (LINQ to SQL) does not exist on .NET; its Binary support is compiled out.
#if NETFRAMEWORK
using System.Data.Linq;
#endif
using System.Linq;
using System.Reflection;
using System.Threading;

namespace System.Web.Mvc
{
    public static class ModelBinders
    {
        private static readonly ModelBinderDictionary _binders = CreateDefaultBinderDictionary();

        public static ModelBinderDictionary Binders
        {
            get { return _binders; }
        }

        internal static IModelBinder GetBinderFromAttributes(Type type, Action<Type> errorAction)
        {
            AttributeList allAttrs = new AttributeList(TypeDescriptorHelper.Get(type).GetAttributes());
            CustomModelBinderAttribute binder = allAttrs.SingleOfTypeDefaultOrError<Attribute, CustomModelBinderAttribute, Type>(errorAction, type);
            return binder == null ? null : binder.GetBinder();
        }

        internal static IModelBinder GetBinderFromAttributes(ICustomAttributeProvider element, Action<ICustomAttributeProvider> errorAction)
        {
            CustomModelBinderAttribute[] attrs = (CustomModelBinderAttribute[])element.GetCustomAttributes(typeof(CustomModelBinderAttribute), true /* inherit */);
            // For compatibility, return null if no attributes.
            if (attrs == null)
            {                
                return null;
            }
            CustomModelBinderAttribute binder = attrs.SingleDefaultOrError(errorAction, element);
            return binder == null ? null : binder.GetBinder();
        }

        private static ModelBinderDictionary CreateDefaultBinderDictionary()
        {
            // We can't add a binder to the HttpPostedFileBase type as an attribute, so we'll just
            // prepopulate the dictionary as a convenience to users.

            ModelBinderDictionary binders = new ModelBinderDictionary()
            {
                { typeof(HttpPostedFileBase), new HttpPostedFileBaseModelBinder() },
                { typeof(byte[]), new ByteArrayModelBinder() },
#if NETFRAMEWORK
                { typeof(Binary), new LinqBinaryModelBinder() },
#endif
                { typeof(CancellationToken), new CancellationTokenModelBinder() }
            };
            return binders;
        }
    }
}
