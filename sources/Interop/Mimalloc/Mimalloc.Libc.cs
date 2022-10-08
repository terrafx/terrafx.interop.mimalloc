// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace TerraFX.Interop.Mimalloc;

public static unsafe partial class Mimalloc
{
    private static int errno => Marshal.GetLastWin32Error();

    [DllImport("libc", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("wchar_t*")]
    private static extern ushort* _wgetenv([NativeTypeName("const wchar_t*")] ushort* name);

    [DllImport("libc", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("char*")]
    private static extern sbyte* getenv([NativeTypeName("const char*")] sbyte* name);

    [DllImport("libc", CallingConvention = CallingConvention.Cdecl, EntryPoint = "_ZSt15get_new_handlerv", ExactSpelling = true)]
    [return: NativeTypeName("std::new_handler")]
    private static extern delegate* unmanaged[Cdecl]<void> unix_std_get_new_handler();

    [DllImport("libc", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern void* memcpy(void* destination, [NativeTypeName("const void*")] void* source, [NativeTypeName("size_t")] nuint num);

    [DllImport("libc", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern void* memchr(void* ptr, int value, [NativeTypeName("size_t")] nuint num);

    [DllImport("libc", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern void* memset(void* ptr, int value, [NativeTypeName("size_t")] nuint num);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint offsetof(void* type, void* member) => (nuint)member - (nuint)type;

    [DllImport("libc", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("size_t")]
    private static extern nuint strlen([NativeTypeName("const char*")] sbyte* str);

    private static int VM_MAKE_TAG(int tag)
    {
        mi_assert_internal(IsMacOS);
        return tag << 24;
    }

    [DllImport("libc", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern nuint wcslen([NativeTypeName("const wchar_t*")] ushort* str);

    [DllImport("libc", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?get_new_handler@std@@YAP6AXXZXZ", ExactSpelling = true)]
    [return: NativeTypeName("std::new_handler")]
    private static extern delegate* unmanaged[Cdecl]<void> win32_std_get_new_handler();
}
