// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the mimalloc-internal.h file from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

using System.Runtime.CompilerServices;

namespace TerraFX.Interop.Mimalloc;

public static unsafe partial class Mimalloc
{
    // Minimal alignment necessary. On most platforms 16 bytes are needed
    // due to SSE registers for example. This must be at least `MI_INTPTR_SIZE`
    private const nuint MI_MAX_ALIGN_SIZE = 16;

    // ------------------------------------------------------
    // Platform specific values
    // ------------------------------------------------------

    private const nuint KiB = 1024;
    private const nuint MiB = KiB * KiB;
    private const nuint GiB = MiB * KiB;

    // ------------------------------------------------------
    // Main internal data-structures
    // ------------------------------------------------------

    // Maximum number of size classes. (spaced exponentially in 12.5% increments)
    private const byte MI_BIN_HUGE = 73;

    private const byte MI_BIN_FULL = MI_BIN_HUGE + 1;

    // ------------------------------------------------------
    // Debug
    // ------------------------------------------------------

    private const byte MI_DEBUG_UNINIT = 0xD0;

    private const byte MI_DEBUG_FREED = 0xDF;

    private const byte MI_DEBUG_PADDING = 0xDE;

    // use our own assertion to print without memory allocation
    private static partial void _mi_assert_fail([NativeTypeName("const char*")] string assertion, [NativeTypeName("const char*")] string fname, [NativeTypeName("unsigned")] uint line, [NativeTypeName("const char*")] string func);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void mi_assert(bool expr, [CallerArgumentExpression("expr")] string assertion = "", [CallerFilePath] string fname = "", [CallerLineNumber] uint line = 0, [CallerMemberName] string func = "")
    {
        if ((MI_DEBUG != 0) && !expr)
        {
            _mi_assert_fail(assertion, fname, line, func);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void mi_assert_internal(bool expr, [CallerArgumentExpression("expr")] string assertion = "", [CallerFilePath] string fname = "", [CallerLineNumber] uint line = 0, [CallerMemberName] string func = "")
    {
        if (MI_DEBUG > 1)
        {
            mi_assert(expr, assertion, fname, line, func);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void mi_assert_expensive(bool expr, [CallerArgumentExpression("expr")] string assertion = "", [CallerFilePath] string fname = "", [CallerLineNumber] uint line = 0, [CallerMemberName] string func = "")
    {
        if (MI_DEBUG > 2)
        {
            mi_assert(expr, assertion, fname, line, func);
        }
    }

    private static partial void _mi_stat_increase([NativeTypeName("mi_stat_count_t*")] ref mi_stat_count_t stat, [NativeTypeName("size_t")] nuint amount);

    private static partial void _mi_stat_decrease([NativeTypeName("mi_stat_count_t*")] ref mi_stat_count_t stat, [NativeTypeName("size_t")] nuint amount);

    private static partial void _mi_stat_counter_increase([NativeTypeName("mi_stat_counter_t*")] ref mi_stat_counter_t stat, [NativeTypeName("size_t")] nuint amount);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void mi_stat_increase([NativeTypeName("mi_stat_count_t*")] ref mi_stat_count_t stat, [NativeTypeName("size_t")] nuint amount)
    {
        if (MI_STAT != 0)
        {
            _mi_stat_increase(ref stat, amount);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void mi_stat_decrease([NativeTypeName("mi_stat_count_t*")] ref mi_stat_count_t stat, [NativeTypeName("size_t")] nuint amount)
    {
        if (MI_STAT != 0)
        {
            _mi_stat_decrease(ref stat, amount);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void mi_stat_counter_increase([NativeTypeName("mi_stat_counter_t*")] ref mi_stat_counter_t stat, [NativeTypeName("size_t")] nuint amount)
    {
        if (MI_STAT != 0)
        {
            _mi_stat_counter_increase(ref stat, amount);
        }
    }
}
