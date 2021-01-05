// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the mimalloc-atomic.h file from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

using System;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.X86;
using System.Threading;
using static TerraFX.Interop.mi_memory_order_t;

namespace TerraFX.Interop
{
    public static unsafe partial class Mimalloc
    {
        // Various defines for all used memory orders in mimalloc

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool mi_atomic_cas_weak(ref nuint p, ref nuint expected, nuint desired, mi_memory_order_t mem_success, mi_memory_order_t mem_fail)
            => mi_atomic_compare_exchange_weak_explicit(ref p, ref expected, desired, mem_success, mem_fail);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool mi_atomic_cas_strong(ref nuint p, ref nuint expected, nuint desired, mi_memory_order_t mem_success, mi_memory_order_t mem_fail)
            => mi_atomic_compare_exchange_strong_explicit(ref p, ref expected, desired, mem_success, mem_fail);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static nuint mi_atomic_load_acquire(ref nuint p) => mi_atomic_load_explicit(ref p, mi_memory_order_acquire);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static nuint mi_atomic_load_relaxed(ref nuint p) => mi_atomic_load_explicit(ref p, mi_memory_order_relaxed);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void mi_atomic_store_release(ref nuint p, nuint x) => mi_atomic_store_explicit(ref p, x, mi_memory_order_release);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void mi_atomic_store_relaxed(ref nuint p, nuint x) => mi_atomic_store_explicit(ref p, x, mi_memory_order_relaxed);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static nuint mi_atomic_exchange_release(ref nuint p, nuint x) => mi_atomic_exchange_explicit(ref p, x, mi_memory_order_release);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static nuint mi_atomic_exchange_acq_rel(ref nuint p, nuint x) => mi_atomic_exchange_explicit(ref p, x, mi_memory_order_acq_rel);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool mi_atomic_cas_weak_release(ref nuint p, ref nuint exp, nuint des) => mi_atomic_cas_weak(ref p, ref exp, des, mi_memory_order_release, mi_memory_order_relaxed);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool mi_atomic_cas_weak_acq_rel(ref nuint p, ref nuint exp, nuint des) => mi_atomic_cas_weak(ref p, ref exp, des, mi_memory_order_acq_rel, mi_memory_order_acquire);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool mi_atomic_cas_strong_release(ref nuint p, ref nuint exp, nuint des) => mi_atomic_cas_strong(ref p, ref exp, des, mi_memory_order_release, mi_memory_order_relaxed);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool mi_atomic_cas_strong_acq_rel(ref nuint p, ref nuint exp, nuint des) => mi_atomic_cas_strong(ref p, ref exp, des, mi_memory_order_acq_rel, mi_memory_order_acquire);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static nuint mi_atomic_add_relaxed(ref nuint p, nuint x) => mi_atomic_fetch_add_explicit(ref p, x, mi_memory_order_relaxed);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static nuint mi_atomic_sub_relaxed(ref nuint p, nuint x) => mi_atomic_fetch_sub_explicit(ref p, x, mi_memory_order_relaxed);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static nuint mi_atomic_add_acq_rel(ref nuint p, nuint x) => mi_atomic_fetch_add_explicit(ref p, x, mi_memory_order_acq_rel);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static nuint mi_atomic_sub_acq_rel(ref nuint p, nuint x) => mi_atomic_fetch_sub_explicit(ref p, x, mi_memory_order_acq_rel);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static nuint mi_atomic_and_acq_rel(ref nuint p, nuint x) => mi_atomic_fetch_and_explicit(ref p, x, mi_memory_order_acq_rel);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static nuint mi_atomic_or_acq_rel(ref nuint p, nuint x) => mi_atomic_fetch_or_explicit(ref p, x, mi_memory_order_acq_rel);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static nuint mi_atomic_increment_relaxed(ref nuint p) => mi_atomic_add_relaxed(ref p, 1);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static nuint mi_atomic_decrement_relaxed(ref nuint p) => mi_atomic_sub_relaxed(ref p, 1);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static nuint mi_atomic_increment_acq_rel(ref nuint p) => mi_atomic_add_acq_rel(ref p, 1);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static nuint mi_atomic_decrement_acq_rel(ref nuint p) => mi_atomic_sub_acq_rel(ref p, 1);

        private static partial void mi_atomic_yield();

        [return: NativeTypeName("intptr_t")]
        private static partial nint mi_atomic_addi([NativeTypeName("std::atomic<intptr_t>")] ref nint p, [NativeTypeName("intptr_t")] nint add);

        [return: NativeTypeName("intptr_t")]
        private static partial nint mi_atomic_subi([NativeTypeName("std::atomic<intptr_t>")] ref nint p, [NativeTypeName("intptr_t")] nint sub);

        [return: NativeTypeName("uintptr_t")]
        private static nuint mi_atomic_fetch_add_explicit([NativeTypeName("std::atomic<uintptr_t>*")] ref nuint p, [NativeTypeName("uintptr_t")] nuint add, mi_memory_order_t mo)
        {
            nuint value = p;

            if (Environment.Is64BitProcess)
            {
                Interlocked.Add(ref Unsafe.As<nuint, ulong>(ref p), add);
            }
            else
            {
                Interlocked.Add(ref Unsafe.As<nuint, uint>(ref p), (uint)add);
            }

            return value;
        }

        [return: NativeTypeName("uintptr_t")]
        private static nuint mi_atomic_fetch_sub_explicit([NativeTypeName("std::atomic<uintptr_t>*")] ref nuint p, [NativeTypeName("uintptr_t")] nuint sub, mi_memory_order_t mo)
            => mi_atomic_fetch_add_explicit(ref p, (nuint)(-(nint)sub), mo);

        [return: NativeTypeName("uintptr_t")]
        private static nuint mi_atomic_fetch_and_explicit([NativeTypeName("std::atomic<uintptr_t>*")] ref nuint p, [NativeTypeName("uintptr_t")] nuint x, mi_memory_order_t mo)
        {
            if (Environment.Is64BitProcess)
            {
                return (nuint)Interlocked.And(ref Unsafe.As<nuint, ulong>(ref p), x);
            }
            else
            {
                return Interlocked.And(ref Unsafe.As<nuint, uint>(ref p), (uint)x);
            }
        }

        [return: NativeTypeName("uintptr_t")]
        private static nuint mi_atomic_fetch_or_explicit([NativeTypeName("std::atomic<uintptr_t>*")] ref nuint p, [NativeTypeName("uintptr_t")] nuint x, mi_memory_order_t mo)
        {
            if (Environment.Is64BitProcess)
            {
                return (nuint)Interlocked.Or(ref Unsafe.As<nuint, ulong>(ref p), x);
            }
            else
            {
                return Interlocked.Or(ref Unsafe.As<nuint, uint>(ref p), (uint)x);
            }
        }

        private static bool mi_atomic_compare_exchange_strong_explicit([NativeTypeName("std::atomic<uintptr_t>*")] ref nuint p, [NativeTypeName("uintptr_t*")] ref nuint expected, [NativeTypeName("uintptr_t")] nuint desired, mi_memory_order_t mo1, mi_memory_order_t mo2)
        {
            nuint read;

            if (Environment.Is64BitProcess)
            {
                read = (nuint)Interlocked.CompareExchange(ref Unsafe.As<nuint, ulong>(ref p), desired, expected);
            }
            else
            {
                read = Interlocked.CompareExchange(ref Unsafe.As<nuint, uint>(ref p), (uint)desired, (uint)expected);
            }

            if (read == expected)
            {
                return true;
            }
            else
            {
                expected = read;
                return false;
            }
        }

        private static bool mi_atomic_compare_exchange_weak_explicit([NativeTypeName("std::atomic<uintptr_t>*")] ref nuint p, [NativeTypeName("uintptr_t*")] ref nuint expected, [NativeTypeName("uintptr_t")] nuint desired, mi_memory_order_t mo1, mi_memory_order_t mo2)
            => mi_atomic_compare_exchange_strong_explicit(ref p, ref expected, desired, mo1, mo2);

        [return: NativeTypeName("uintptr_t")]
        private static nuint mi_atomic_exchange_explicit([NativeTypeName("std::atomic<uintptr_t>*")] ref nuint p, [NativeTypeName("uintptr_t")] nuint exchange, mi_memory_order_t mo)
        {
            if (Environment.Is64BitProcess)
            {
                return (nuint)Interlocked.Exchange(ref Unsafe.As<nuint, ulong>(ref p), exchange);
            }
            else
            {
                return Interlocked.Exchange(ref Unsafe.As<nuint, uint>(ref p), (uint)exchange);
            }
        }

        private static void mi_atomic_thread_fence(mi_memory_order_t mo)
        {
            nuint x = 0;
            mi_atomic_exchange_explicit(ref x, 1, mo);
        }

        [return: NativeTypeName("uintptr_t")]
        private static nuint mi_atomic_load_explicit([NativeTypeName("std::atomic<uintptr_t> const*")] ref nuint p, mi_memory_order_t mo)
        {
            if (X86Base.IsSupported)
            {
                return p;
            }
            else
            {
                nuint x = p;

                if (mo > mi_memory_order_relaxed)
                {
                    while (!mi_atomic_compare_exchange_weak_explicit(ref p, ref x, x, mo, mi_memory_order_relaxed))
                    {
                        /* nothing */
                    }
                }

                return x;
            }
        }

        private static void mi_atomic_store_explicit([NativeTypeName("std::atomic<uintptr_t>*")] ref nuint p, [NativeTypeName("uintptr_t")] nuint x, mi_memory_order_t mo)
        {
            if (X86Base.IsSupported)
            {
                p = x;
            }
            else
            {
                mi_atomic_exchange_explicit(ref p, x, mo);
            }
        }

        [return: NativeTypeName("int64_t")]
        private static long mi_atomic_loadi64_explicit([NativeTypeName("std::atomic<int64_t>*")] ref long p, mi_memory_order_t mo)
        {
            if (X86Base.X64.IsSupported)
            {
                return p;
            }
            else
            {
                long old = p;
                long x = old;

                while ((old = Interlocked.CompareExchange(ref p, x, old)) != x)
                {
                    x = old;
                }

                return x;
            }
        }

        private static void mi_atomic_storei64_explicit([NativeTypeName("std::atomic<int64_t>*")] ref long p, [NativeTypeName("int64_t")] long x, mi_memory_order_t mo)
        {
            if (X86Base.IsSupported)
            {
                p = x;
            }
            else
            {
                Interlocked.Exchange(ref p, x);
            }
        }

        // These are used by the statistics
        [return: NativeTypeName("int64_t")]
        private static long mi_atomic_addi64_relaxed([NativeTypeName("volatile std::atomic<int64_t>*")] ref long p, [NativeTypeName("int64_t")] long add)
        {
            if (Environment.Is64BitProcess)
            {
                return mi_atomic_addi(ref Unsafe.As<long, nint>(ref p), (nint)add);
            }
            else
            {
                long current, sum;

                do
                {
                    current = p;
                    sum = current + add;
                }
                while (Interlocked.CompareExchange(ref p, sum, current) != current);

                return current;
            }
        }

        private static void mi_atomic_maxi64_relaxed([NativeTypeName("std::atomic<int64_t>*")] ref long p, [NativeTypeName("int64_t")] long x)
        {
            long current;

            do
            {
                current = p;
            }
            while ((current < x) && (Interlocked.CompareExchange(ref p, x, current) != current));
        }

        // The pointer macros cast to `uintptr_t`.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void* mi_atomic_load_ptr_acquire(ref nuint p) => (void*)mi_atomic_load_acquire(ref p);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static T* mi_atomic_load_ptr_acquire<T>(ref nuint p)
            where T : unmanaged => (T*)mi_atomic_load_acquire(ref p);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void* mi_atomic_load_ptr_relaxed(ref nuint p) => (void*)mi_atomic_load_relaxed(ref p);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static T* mi_atomic_load_ptr_relaxed<T>(ref nuint p)
            where T : unmanaged => (T*)mi_atomic_load_relaxed(ref p);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void mi_atomic_store_ptr_release(ref nuint p, void* x) => mi_atomic_store_release(ref p, (nuint)x);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void mi_atomic_store_ptr_release<T>(ref nuint p, T* x)
            where T : unmanaged => mi_atomic_store_release(ref p, (nuint)x);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void mi_atomic_store_ptr_relaxed(ref nuint p, void* x) => mi_atomic_store_relaxed(ref p, (nuint)x);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void mi_atomic_store_ptr_relaxed<T>(ref nuint p, T* x)
            where T : unmanaged => mi_atomic_store_relaxed(ref p, (nuint)x);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool mi_atomic_cas_ptr_weak_release<T>(ref nuint p, ref nuint exp, T* des)
            where T : unmanaged => mi_atomic_cas_weak_release(ref p, ref exp, (nuint)des);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool mi_atomic_cas_ptr_weak_acq_rel<T>(ref nuint p, ref nuint exp, T* des)
            where T : unmanaged => mi_atomic_cas_weak_acq_rel(ref p, ref exp, (nuint)des);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool mi_atomic_cas_ptr_strong_release<T>(ref nuint p, ref nuint exp, T* des)
            where T : unmanaged => mi_atomic_cas_strong_release(ref p, ref exp, (nuint)des);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static T* mi_atomic_exchange_ptr_release<T>(ref nuint p, T* x)
            where T : unmanaged => (T*)mi_atomic_exchange_release(ref p, (nuint)x);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static T* mi_atomic_exchange_ptr_acq_rel<T>(ref nuint p, T* x)
            where T : unmanaged => (T*)mi_atomic_exchange_acq_rel(ref p, (nuint)x);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long mi_atomic_loadi64_acquire(ref long p) => mi_atomic_loadi64_explicit(ref p, mi_memory_order_acquire);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long mi_atomic_loadi64_relaxed(ref long p) => mi_atomic_loadi64_explicit(ref p, mi_memory_order_relaxed);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void mi_atomic_storei64_release(ref long p, long x) => mi_atomic_storei64_explicit(ref p, x, mi_memory_order_release);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void mi_atomic_storei64_relaxed(ref long p, long x) => mi_atomic_storei64_explicit(ref p, x, mi_memory_order_relaxed);

        // Atomically add a signed value; returns the previous value.
        private static partial nint mi_atomic_addi(ref nint p, nint add) => unchecked((nint)mi_atomic_add_acq_rel(ref Unsafe.As<nint, nuint>(ref p), (nuint)add));

        // Atomically subtract a signed value; returns the previous value.
        private static partial nint mi_atomic_subi(ref nint p, nint sub) => mi_atomic_addi(ref p, -sub);

        // Yield 
        private static partial void mi_atomic_yield() => Thread.Yield();
    }
}
