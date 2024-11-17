// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System;
using System.Reflection;

namespace IronPython.Runtime.Types {

    public class ExtensionPropertyInfo {
        private MethodInfo _getter, _setter, _deleter;
        private Type _declaringType;

        public ExtensionPropertyInfo(Type logicalDeclaringType, MethodInfo mi) {
            _declaringType = logicalDeclaringType;

            string methodName = mi.Name;
            string prefix = "";

#if FEATURE_REFEMIT
            if (methodName.StartsWith(NewTypeMaker.BaseMethodPrefix, StringComparison.Ordinal)) {
                methodName = methodName.Substring(NewTypeMaker.BaseMethodPrefix.Length);
                prefix = NewTypeMaker.BaseMethodPrefix;
            }
#endif

            if (methodName.StartsWith("Get", StringComparison.Ordinal) || methodName.StartsWith("Set", StringComparison.Ordinal)) {
                GetPropertyMethods(mi, methodName, prefix, "Get", "Set", "Delete");
            } else if (methodName.StartsWith("get_", StringComparison.Ordinal) || methodName.StartsWith("set_", StringComparison.Ordinal)) {
                GetPropertyMethods(mi, methodName, prefix, "get_", "set_", null);
            }
#if FEATURE_REFEMIT
            else if (methodName.StartsWith(NewTypeMaker.FieldGetterPrefix, StringComparison.Ordinal) || methodName.StartsWith(NewTypeMaker.FieldSetterPrefix, StringComparison.Ordinal)) {
                GetPropertyMethods(mi, methodName, prefix, NewTypeMaker.FieldGetterPrefix, NewTypeMaker.FieldSetterPrefix, null);
            }
#endif
        }

        private void GetPropertyMethods(MethodInfo mi, string methodName, string prefix, string get, string set, string delete) {
            string propname = methodName.Substring(get.Length);

            if (mi.Name.StartsWith(get, StringComparison.Ordinal)) {
                _getter = mi;
                _setter = mi.DeclaringType.GetMethod(prefix + set + propname);
            } else {
                _getter = mi.DeclaringType.GetMethod(prefix + get + propname);
                _setter = mi;
            }

            if (delete != null) {
                _deleter = mi.DeclaringType.GetMethod(prefix + delete + propname);
            }
        }

        public MethodInfo Getter {
            get { return _getter; }
        }

        public MethodInfo Setter {
            get { return _setter; }
        }

        public MethodInfo Deleter {
            get { return _deleter; }
        }

        public Type DeclaringType {
            get { return _declaringType; }
        }

        public string Name {
            get {
                // remove Get or Set from name
                if (_getter != null) return _getter.Name.Substring(3);
                return _setter.Name.Substring(3);
            }
        }
    }
}
