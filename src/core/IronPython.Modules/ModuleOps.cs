// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

#if FEATURE_CTYPES

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;

using Microsoft.Scripting;
using Microsoft.Scripting.Utils;

using IronPython.Runtime;
using IronPython.Runtime.Exceptions;
using IronPython.Runtime.Operations;
using IronPython.Runtime.Types;
using System.Runtime.CompilerServices;

namespace IronPython.Modules {
    /// <summary>
    /// Provides helper functions which need to be called from generated code to implement various
    /// portions of modules.
    /// </summary>
    public static partial class ModuleOps {
        public static IntPtr StringToHGlobalAnsi(string str) {
            if (str == null) {
                return IntPtr.Zero;
            }

            return Marshal.StringToHGlobalAnsi(str);
        }

        public static IntPtr StringToHGlobalUni(string str) {
            if (str == null) {
                return IntPtr.Zero;
            }

            return Marshal.StringToHGlobalUni(str);
        }

        public static object DoErrorCheck(object errCheckFunc, object result, object func, object[] arguments) {
            return PythonCalls.Call(errCheckFunc, result, func, PythonTuple.Make(arguments));
        }

        public static object CreateMemoryHolder(IntPtr data, int size) {
            var res = new MemoryHolder(size);
            res.CopyFrom(data, new IntPtr(size));
            return res;
        }

        public static object CreateNativeWrapper(PythonType type, object holder) {
            Debug.Assert(holder is MemoryHolder);

            CTypes.CData data = (CTypes.CData)type.CreateInstance(type.Context.SharedContext);
            data.MemHolder = (MemoryHolder)holder;
            return data;
        }

        public static object CreateCData(IntPtr dataAddress, PythonType type) {
            CTypes.INativeType nativeType = (CTypes.INativeType)type;
            CTypes.CData data = (CTypes.CData)type.CreateInstance(type.Context.SharedContext);
            data.MemHolder = new MemoryHolder(nativeType.Size);
            data.MemHolder.CopyFrom(dataAddress, new IntPtr(nativeType.Size));
            return data;
        }

        public static object CreateCFunction(IntPtr address, PythonType type) {
            return type.CreateInstance(type.Context.SharedContext, address);
        }

        public static CTypes.CData CheckSimpleCDataType(object o, object type) {
            CTypes.SimpleCData res = o as CTypes.SimpleCData;
            if (res == null && PythonOps.TryGetBoundAttr(o, "_as_parameter_", out object asParam)) {
                res = asParam as CTypes.SimpleCData;
            }

            if (res != null && res.NativeType != type) {
                throw PythonOps.TypeErrorForTypeMismatch(((PythonType)type).Name, o);
            }

            return res;
        }

        public static CTypes.CData/*!*/ CheckCDataType(object o, object type) {
            CTypes.CData res = o as CTypes.CData;
            if (res == null && PythonOps.TryGetBoundAttr(o, "_as_parameter_", out object asParam)) {
                res = asParam as CTypes.CData;
            }

            bool valid = true;
            // if we have an array, we can also send a pointer as long as the element types
            // for the pointer and array are the same
            if(res != null && res.NativeType is CTypes.ArrayType at) {
                valid = ((type is CTypes.ArrayType t) && (t.ElementType == at.ElementType)) ||
                    ((type is CTypes.PointerType p) && (p._type == at.ElementType));
            }

            if (res == null || !valid) {
                throw ArgumentError(type, ((PythonType)type).Name, o);
            }

            return res;
        }

        public static IntPtr/*!*/ GetFunctionPointerValue(object o, object type) {
            CTypes._CFuncPtr res = o as CTypes._CFuncPtr;
            if (res == null && PythonOps.TryGetBoundAttr(o, "_as_parameter_", out object asParam)) {
                res = asParam as CTypes._CFuncPtr;
            }

            if (res == null || res.NativeType != type) {
                throw ArgumentError(type, ((PythonType)type).Name, o);
            }

            return res.addr;
        }

        public static CTypes.CData TryCheckCDataPointerType(object o, object type) {
            CTypes.CData res = o as CTypes.CData;
            if (res == null && PythonOps.TryGetBoundAttr(o, "_as_parameter_", out object asParam)) {
                res = asParam as CTypes.CData;
            }

            bool valid = true;

            // if we have an array, we can also send a pointer as long as the element types
            // for the pointer and array are the same
            if (res != null && res.NativeType is CTypes.ArrayType at) {
                valid = ((type is CTypes.ArrayType t) && (t.ElementType == at.ElementType)) ||
                    ((type is CTypes.PointerType p) && (p._type == at.ElementType));
            }

            if (res != null && !valid) {
                throw ArgumentError(type, ((PythonType)((CTypes.PointerType)type)._type).Name, o);
            }

            return res;
        }

        public static CTypes._Array TryCheckCharArray(object o) {
            if (o is CTypes._Array array) {
                if (((CTypes.ArrayType)array.NativeType).ElementType is CTypes.SimpleType elemType && elemType._type == CTypes.SimpleTypeKind.Char) {
                    return array;
                }
            }

            return null;
        }

        private static readonly byte[] FakeZeroLength = { 42 };

        public static byte[] TryCheckBytes(object o) {
            if (o is Bytes bytes) {
                if (bytes.Count == 0) {
                    // OpCodes.Ldelema refuses to get address of empty array
                    // So we feed it with a fake one (cp34892)
                    return FakeZeroLength;
                }
                return bytes.UnsafeByteArray;
            }
            return null;
        }

        public static byte[] GetBytes(Bytes bytes) {
            return bytes.UnsafeByteArray;
        }

        public static CTypes._Array TryCheckWCharArray(object o) {
            if (o is CTypes._Array array) {
                if (((CTypes.ArrayType)array.NativeType).ElementType is CTypes.SimpleType elemType && elemType._type == CTypes.SimpleTypeKind.WChar) {
                    return array;
                }
            }

            return null;
        }

        public static object CreateSubclassInstance(object type, object instance) {
            return PythonCalls.Call(type, instance);
        }

        public static void CallbackException(Exception e, CodeContext/*!*/ context) {
            PythonContext pc = context.LanguageContext;
            object stderr = pc.SystemStandardError;
            PythonOps.PrintWithDest(context, stderr, pc.FormatException(e));
        }

        private static Exception ArgumentError(object type, string expected, object got) {
            PythonContext pc = ((PythonType)type).Context;
            return PythonExceptions.CreateThrowable(
                (PythonType)pc.GetModuleState("ArgumentError"),
                string.Format("expected {0}, got {1}", expected, PythonOps.GetPythonTypeName(got))
            );
        }

        public static CTypes.CData CheckNativeArgument(object o, object type) {
            if (o is CTypes.NativeArgument arg) {
                if (((CTypes.PointerType)type)._type != DynamicHelpers.GetPythonType(arg._obj)) {
                    throw ArgumentError(type, ((PythonType)type).Name, o);
                }
                return arg._obj;
            }

            return null;
        }

        public static Bytes CharToBytes(byte c) {
            return Bytes.FromByte(c);
        }

        public static string WCharToString(char c) {
            return new string(c, 1);
        }

        public static char StringToChar(string s) {
            return s[0];
        }

        public static string EnsureString(object o) {
            if (!(o is string res)) {
                throw PythonOps.TypeErrorForTypeMismatch("str", o);
            }

            return res;
        }

        public static bool CheckFunctionId(CTypes._CFuncPtr func, int id) {
            return func.Id == id;
        }

        public static Bytes IntPtrToBytes(IntPtr ptr) {
            // TODO: optimize this?
            var str = Marshal.PtrToStringAnsi(ptr);
            if (str is null) return null;
            return Bytes.Make(str.MakeByteArray());
        }

        public static object IntPtrToObject(IntPtr address) {
            GCHandle handle = GCHandle.FromIntPtr(address);

            object res = handle.Target;
            handle.Free();
            return res;
        }

        public static byte GetBoolean(object value, object type) {
            if (value is bool) {
                return ((bool)value) ? (byte)1 : (byte)0;
            } else if (value is int) {
                return ((int)value) != 0 ? (byte)1 : (byte)0;
            }

            if (PythonOps.TryGetBoundAttr(value, "_as_parameter_", out object asParam)) {
                return GetBoolean(asParam, type);
            }

            throw PythonOps.TypeErrorForTypeMismatch("bool", value);
        }

        public static int GetVariantBool(object value, object type) {
            return Converter.ConvertToBoolean(value) ? 1 : 0;
        }

        public static byte GetChar(object value, object type) {
            // TODO: .NET interop?
            if (value is Bytes bytes && bytes.Count == 1) {
                return ((IList<byte>)bytes)[0];
            }
            if (value is ByteArray bytearray && bytearray.Count == 1) {
                return ((IList<byte>)bytearray)[0];
            }
            if (value is int i) {
                try {
                    return checked((byte)i);
                } catch (OverflowException) {
                    throw PythonOps.TypeError("one character bytes, bytearray or integer expected");
                }
            }

            if (PythonOps.TryGetBoundAttr(value, "_as_parameter_", out object asParam)) {
                return GetChar(asParam, type);
            }

            throw PythonOps.TypeError("one character bytes, bytearray or integer expected");
        }

        public static char GetWChar(object value, object type) {
            if (value is string strVal) {
                if (strVal.Length != 1) throw PythonOps.TypeError("one character unicode string expected");
                return strVal[0];
            }

            if (PythonOps.TryGetBoundAttr(value, "_as_parameter_", out object asParam)) {
                return GetWChar(asParam, type);
            }

            throw PythonOps.TypeErrorForBadInstance("unicode string expected instead of {0} instance", value);
        }

        public static sbyte GetSignedByte(object value, object type) {
            if (TryToIntStrict(value, out BigInteger bi)) {
                return unchecked((sbyte)(byte)(bi & byte.MaxValue));
            }

            if (PythonOps.TryGetBoundAttr(value, "_as_parameter_", out object asParam)) {
                return GetSignedByte(asParam, type);
            }

            throw PythonOps.TypeErrorForTypeMismatch("signed byte", value);
        }

        public static byte GetUnsignedByte(object value, object type) {
            if (TryToIntStrict(value, out BigInteger bi)) {
                return (byte)(bi & byte.MaxValue);
            }

            if (PythonOps.TryGetBoundAttr(value, "_as_parameter_", out object asParam)) {
                return GetUnsignedByte(asParam, type);
            }

            throw PythonOps.TypeErrorForTypeMismatch("unsigned byte", value);
        }


        public static short GetSignedShort(object value, object type) {
            if (TryToIntStrict(value, out BigInteger bi)) {
                return unchecked((short)(ushort)(bi & ushort.MaxValue));
            }

            if (PythonOps.TryGetBoundAttr(value, "_as_parameter_", out object asParam)) {
                return GetSignedShort(asParam, type);
            }

            throw PythonOps.TypeErrorForTypeMismatch("signed short", value);
        }

        public static ushort GetUnsignedShort(object value, object type) {
            if (TryToIntStrict(value, out BigInteger bi)) {
                return (ushort)(bi & ushort.MaxValue);
            }

            if (PythonOps.TryGetBoundAttr(value, "_as_parameter_", out object asParam)) {
                return GetUnsignedShort(asParam, type);
            }

            throw PythonOps.TypeErrorForTypeMismatch("unsigned short", value);
        }

        public static int GetSignedInt(object value, object type) {
            if (TryToIntStrict(value, out BigInteger bi)) {
                return unchecked((int)(uint)(bi & uint.MaxValue));
            }


            if (PythonOps.TryGetBoundAttr(value, "_as_parameter_", out object asParam)) {
                return GetSignedInt(asParam, type);
            }

            throw PythonOps.TypeErrorForTypeMismatch("signed int", value);
        }

        public static uint GetUnsignedInt(object value, object type) {
            if (TryToIntStrict(value, out BigInteger bi)) {
                return (uint)(bi & uint.MaxValue);
            }

            if (PythonOps.TryGetBoundAttr(value, "_as_parameter_", out object asParam)) {
                return GetUnsignedInt(asParam, type);
            }

            throw PythonOps.TypeErrorForTypeMismatch("unsigned int", value);
        }

        public static long GetSignedLong(object value, object type) {
            if (TryToIntStrict(value, out BigInteger bi)) {
                return unchecked((long)(ulong)(bi & (TypecodeOps.IsCLong32Bit ? uint.MaxValue : ulong.MaxValue)));
            }

            if (PythonOps.TryGetBoundAttr(value, "_as_parameter_", out object asParam)) {
                return GetSignedLong(asParam, type);
            }

            throw PythonOps.TypeErrorForTypeMismatch("signed long", value);
        }

        public static ulong GetUnsignedLong(object value, object type) {
            if (TryToIntStrict(value, out BigInteger bi)) {
                return (ulong)(bi & (TypecodeOps.IsCLong32Bit ? uint.MaxValue : ulong.MaxValue));
            }

            if (PythonOps.TryGetBoundAttr(value, "_as_parameter_", out object asParam)) {
                return GetUnsignedLong(asParam, type);
            }

            throw PythonOps.TypeErrorForTypeMismatch("unsigned long", value);
        }

        public static long GetSignedLongLong(object value, object type) {
            if (TryToIntStrict(value, out BigInteger bi)) {
                return unchecked((long)(ulong)(bi & ulong.MaxValue));
            }

            if (PythonOps.TryGetBoundAttr(value, "_as_parameter_", out object asParam)) {
                return GetSignedLongLong(asParam, type);
            }

            throw PythonOps.TypeErrorForTypeMismatch("signed long long ", value);
        }

        public static ulong GetUnsignedLongLong(object value, object type) {
            if (TryToIntStrict(value, out BigInteger bi)) {
                return (ulong)(bi & ulong.MaxValue);
            }

            if (PythonOps.TryGetBoundAttr(value, "_as_parameter_", out object asParam)) {
                return GetUnsignedLongLong(asParam, type);
            }

            throw PythonOps.TypeErrorForTypeMismatch("unsigned long long", value);
        }

        public static float GetSingle(object value, object type) {
            if (value is double d) {
                return (float)d;
            } else if (value is float f) {
                return f;
            } else if (value is int i) {
                return (float)i;
            } else if (value is BigInteger bi) {
                return (float)bi.ToFloat64();
            }

            if (PythonOps.TryGetBoundAttr(value, "_as_parameter_", out object asParam)) {
                return GetSingle(asParam, type);
            }

            return (float)Converter.ConvertToDouble(value);
        }

        public static double GetDouble(object value, object type) {
            if (value is double d) {
                return d;
            } else if (value is float f) {
                return (double)f;
            } else if (value is int i) {
                return (double)i;
            } else if (value is BigInteger bi) {
                return bi.ToFloat64();
            }

            if (PythonOps.TryGetBoundAttr(value, "_as_parameter_", out object asParam)) {
                return GetDouble(asParam, type);
            }

            return Converter.ConvertToDouble(value);
        }

        public static int GetSingleBits(object value) {
            if (value is double) {
                return BitConverter.ToInt32(BitConverter.GetBytes((float)(double)value), 0);
            } else if (value is float) {
                return BitConverter.ToInt32(BitConverter.GetBytes((float)value), 0);
            } else if (value is int) {
                return BitConverter.ToInt32(BitConverter.GetBytes((float)(int)value), 0);
            } else if (value is BigInteger) {
                return BitConverter.ToInt32(BitConverter.GetBytes((float)((BigInteger)value).ToFloat64()), 0);
            }

            if (PythonOps.TryGetBoundAttr(value, "_as_parameter_", out object asParam)) {
                return GetSingleBits(asParam);
            }

            return BitConverter.ToInt32(BitConverter.GetBytes((float)Converter.ConvertToDouble(value)), 0);
        }

        public static long GetDoubleBits(object value) {
            if (value is double) {
                return BitConverter.ToInt64(BitConverter.GetBytes((double)value), 0);
            } else if (value is float) {
                return BitConverter.ToInt64(BitConverter.GetBytes((float)value), 0);
            } else if (value is int) {
                return BitConverter.ToInt64(BitConverter.GetBytes((double)(int)value), 0);
            } else if (value is BigInteger) {
                return BitConverter.ToInt64(BitConverter.GetBytes((double)((BigInteger)value).ToFloat64()), 0);
            }

            if (PythonOps.TryGetBoundAttr(value, "_as_parameter_", out object asParam)) {
                return GetDoubleBits(asParam);
            }

            return BitConverter.ToInt64(BitConverter.GetBytes(Converter.ConvertToDouble(value)), 0);
        }

        public static IntPtr GetObject(object value) {
            GCHandle handle = GCHandle.Alloc(value);

            // TODO: Need to free the handle at some point
            return GCHandle.ToIntPtr(handle);
        }

        public static IntPtr GetPointer(object value) {
            if (value == null) {
                return IntPtr.Zero;
            }

            if (value is int iVal) {
                return new IntPtr(iVal);
            }

            if (value is BigInteger bigVal) {
                if(bigVal > long.MaxValue) {
                    bigVal = -1;
                }
                return new IntPtr((long)bigVal);
            }

            if (value is long lVal) {
                return new IntPtr(lVal);
            }

            if (PythonOps.TryGetBoundAttr(value, "_as_parameter_", out object asParam)) {
                return GetPointer(asParam);
            }

            if (value is CTypes.SimpleCData sd) {
                CTypes.SimpleType simpType = (CTypes.SimpleType)sd.NativeType;
                if (simpType._type == CTypes.SimpleTypeKind.WCharPointer ||
                    simpType._type == CTypes.SimpleTypeKind.CharPointer) {
                    return sd.UnsafeAddress;
                } else if (simpType._type == CTypes.SimpleTypeKind.Pointer) {
                    return sd.MemHolder.ReadIntPtr(0);
                }
            }

            if (value is CTypes._Array arr) {
                return arr.UnsafeAddress;
            }

            if (value is CTypes._CFuncPtr func) {
                return func.UnsafeAddress;
            }

            if (value is CTypes.Pointer pointer) {
                return pointer.UnsafeAddress;
            }

            throw PythonOps.TypeErrorForTypeMismatch("pointer", value);
        }

        public static IntPtr GetInterfacePointer(IntPtr self, int offset) {
            var vtable = Marshal.ReadIntPtr(self);
            return Marshal.ReadIntPtr(vtable, offset * IntPtr.Size);
        }

        public static IntPtr GetCharPointer(object value) {
            if (value is Bytes bytes) {
                var data = bytes.UnsafeByteArray;
                var ptr = Marshal.AllocCoTaskMem(data.Length + 1);
                Marshal.Copy(data, 0, ptr, data.Length);
                Marshal.WriteByte(ptr, data.Length, 0);
                return ptr;
            }

            if (value == null) {
                return IntPtr.Zero;
            }

            if (PythonOps.TryGetBoundAttr(value, "_as_parameter_", out object asParam)) {
                return GetCharPointer(asParam);
            }

            throw PythonOps.TypeErrorForTypeMismatch("char pointer", value);
        }

        public static IntPtr GetWCharPointer(object value) {
            if (value is string strVal) {
                return Marshal.StringToCoTaskMemUni(strVal);
            }


            if (value == null) {
                return IntPtr.Zero;
            }

            if (PythonOps.TryGetBoundAttr(value, "_as_parameter_", out object asParam)) {
                return GetWCharPointer(asParam);
            }

            throw PythonOps.TypeErrorForTypeMismatch("wchar pointer", value);
        }

        public static IntPtr GetBSTR(object value) {
            if (value is string strVal) {
                return Marshal.StringToBSTR(strVal);
            }


            if (value == null) {
                return IntPtr.Zero;
            }

            if (PythonOps.TryGetBoundAttr(value, "_as_parameter_", out object asParam)) {
                return GetBSTR(asParam);
            }

            throw PythonOps.TypeErrorForTypeMismatch("BSTR", value);
        }

        internal static bool TryToIntStrict(object value, out BigInteger bi) {
            // When IronPython upgrades to Python 3.10, this method becomes obsolete
            // and can be replaced with PythonOps.TryToIndex(value, out bi)
            if (IsFloatingPoint(value)) {
                throw PythonOps.TypeErrorForBadInstance("int expected instead of {0}", value);
            }

            return PythonOps.TryToInt(value, out bi);
        }

        internal static bool IsFloatingPoint(object value) 
#if NET6_0_OR_GREATER
            => value is double or float or Half;
#else
            => value is double or float;
#endif
    }
}
#endif
