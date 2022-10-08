// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the mimalloc.h file from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

using System;
using System.Runtime.CompilerServices;

namespace TerraFX.Interop.Mimalloc;

public static unsafe partial class Mimalloc
{
    // major + 2 digits minor
    public static readonly int MI_MALLOC_VERSION = 167;

    // ------------------------------------------------------
    // Standard malloc interface
    // ------------------------------------------------------

    public static partial void* mi_malloc([NativeTypeName("size_t")] nuint size);

    public static partial void* mi_calloc([NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint size);

    public static partial void* mi_realloc(void* p, [NativeTypeName("size_t")] nuint newsize);

    public static partial void* mi_expand(void* p, [NativeTypeName("size_t")] nuint newsize);

    public static partial void mi_free(void* p);

    [return: NativeTypeName("char*")]
    public static partial sbyte* mi_strdup([NativeTypeName("const char*")] sbyte* s);

    [return: NativeTypeName("char*")]
    public static partial sbyte* mi_strndup([NativeTypeName("const char*")] sbyte* s, [NativeTypeName("size_t")] nuint n);

    [return: NativeTypeName("char*")]
    public static partial sbyte* mi_realpath([NativeTypeName("const char*")] sbyte* fname, [NativeTypeName("char*")] sbyte* resolved_name);

    // ------------------------------------------------------
    // Extended functionality
    // ------------------------------------------------------

    public static partial void* mi_malloc_small([NativeTypeName("size_t")] nuint size);

    public static partial void* mi_zalloc_small([NativeTypeName("size_t")] nuint size);

    public static partial void* mi_zalloc([NativeTypeName("size_t")] nuint size);

    public static partial void* mi_mallocn([NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint size);

    public static partial void* mi_reallocn(void* p, [NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint size);

    public static partial void* mi_reallocf(void* p, [NativeTypeName("size_t")] nuint newsize);

    [return: NativeTypeName("size_t")]
    public static partial nuint mi_usable_size([NativeTypeName("const void*")] void* p);

    [return: NativeTypeName("size_t")]
    public static partial nuint mi_good_size([NativeTypeName("size_t")] nuint size);

    // ------------------------------------------------------
    // Internals
    // ------------------------------------------------------

    public static partial void mi_register_deferred_free([NativeTypeName("mi_deferred_free_fun*")] mi_deferred_free_fun? fn, void* arg);

    public static partial void mi_register_output([NativeTypeName("mi_output_fun*")] mi_output_fun? @out, void* arg);

    public static partial void mi_register_error([NativeTypeName("mi_error_fun*")] mi_error_fun? fun, void* arg);

    public static partial void mi_collect(bool force);

    public static partial int mi_version();

    public static partial void mi_stats_reset();

    public static partial void mi_stats_merge();

    // backward compatibility: `out` is ignored and should be null
    public static partial void mi_stats_print([NativeTypeName("void*")] mi_output_fun? @out = null);

    public static partial void mi_stats_print_out([NativeTypeName("mi_output_fun*")] mi_output_fun? @out, void* arg);

    // The following members have not been ported as they aren't needed for .NET:
    //  * void mi_process_init()
    //  * void mi_thread_init()

    public static partial void mi_thread_done();

    public static partial void mi_thread_stats_print_out([NativeTypeName("mi_output_fun*")] mi_output_fun? @out, void* arg);

    public static partial void mi_process_info([NativeTypeName("size_t*")] nuint* elapsed_msecs, [NativeTypeName("size_t*")] nuint* user_msecs, [NativeTypeName("size_t*")] nuint* system_msecs, [NativeTypeName("size_t*")] nuint* current_rss, [NativeTypeName("size_t*")] nuint* peak_rss, [NativeTypeName("size_t*")] nuint* current_commit, [NativeTypeName("size_t*")] nuint* peak_commit, [NativeTypeName("size_t*")] nuint* page_faults);

    // -------------------------------------------------------------------------------------
    // Aligned allocation
    // Note that `alignment` always follows `size` for consistency with unaligned
    // allocation, but unfortunately this differs from `posix_memalign` and `aligned_alloc`.
    // -------------------------------------------------------------------------------------

    public static partial void* mi_malloc_aligned([NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment);

    public static partial void* mi_malloc_aligned_at([NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment, [NativeTypeName("size_t")] nuint offset);

    public static partial void* mi_zalloc_aligned([NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment);

    public static partial void* mi_zalloc_aligned_at([NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment, [NativeTypeName("size_t")] nuint offset);

    public static partial void* mi_calloc_aligned([NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment);

    public static partial void* mi_calloc_aligned_at([NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment, [NativeTypeName("size_t")] nuint offset);

    public static partial void* mi_mallocn_aligned([NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment);

    public static partial void* mi_mallocn_aligned_at([NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment, [NativeTypeName("size_t")] nuint offset);

    public static partial void* mi_realloc_aligned(void* p, [NativeTypeName("size_t")] nuint newsize, [NativeTypeName("size_t")] nuint alignment);

    public static partial void* mi_realloc_aligned_at(void* p, [NativeTypeName("size_t")] nuint newsize, [NativeTypeName("size_t")] nuint alignment, [NativeTypeName("size_t")] nuint offset);

    public static partial void* mi_reallocn_aligned(void* p, [NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment);

    public static partial void* mi_reallocn_aligned_at(void* p, [NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment, [NativeTypeName("size_t")] nuint offset);

    // -------------------------------------------------------------------------------------
    // Heaps: first-class, but can only allocate from the same thread that created it.
    // -------------------------------------------------------------------------------------

    [return: NativeTypeName("mi_heap_t*")]
    public static partial IntPtr mi_heap_new();

    public static partial void mi_heap_delete([NativeTypeName("mi_heap_t*")] IntPtr heap);

    public static partial void mi_heap_destroy([NativeTypeName("mi_heap_t*")] IntPtr heap);

    [return: NativeTypeName("mi_heap_t*")]
    public static partial IntPtr mi_heap_set_default([NativeTypeName("mi_heap_t*")] IntPtr heap);

    [return: NativeTypeName("mi_heap_t*")]
    public static partial IntPtr mi_heap_get_default();

    [return: NativeTypeName("mi_heap_t*")]
    public static partial IntPtr mi_heap_get_backing();

    public static partial void mi_heap_collect([NativeTypeName("mi_heap_t*")] IntPtr heap, bool force);

    public static partial void* mi_heap_malloc([NativeTypeName("mi_heap_t*")] IntPtr heap, [NativeTypeName("size_t")] nuint size);

    public static partial void* mi_heap_zalloc([NativeTypeName("mi_heap_t*")] IntPtr heap, [NativeTypeName("size_t")] nuint size);

    public static partial void* mi_heap_calloc([NativeTypeName("mi_heap_t*")] IntPtr heap, [NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint size);

    public static partial void* mi_heap_mallocn([NativeTypeName("mi_heap_t*")] IntPtr heap, [NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint size);

    public static partial void* mi_heap_malloc_small([NativeTypeName("mi_heap_t*")] IntPtr heap, [NativeTypeName("size_t")] nuint size);

    public static partial void* mi_heap_realloc([NativeTypeName("mi_heap_t*")] IntPtr heap, void* p, [NativeTypeName("size_t")] nuint newsize);

    public static partial void* mi_heap_reallocn([NativeTypeName("mi_heap_t*")] IntPtr heap, void* p, [NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint size);

    public static partial void* mi_heap_reallocf([NativeTypeName("mi_heap_t*")] IntPtr heap, void* p, [NativeTypeName("size_t")] nuint newsize);

    [return: NativeTypeName("char*")]
    public static partial sbyte* mi_heap_strdup([NativeTypeName("mi_heap_t*")] IntPtr heap, [NativeTypeName("const char*")] sbyte* s);

    [return: NativeTypeName("char*")]
    public static partial sbyte* mi_heap_strndup([NativeTypeName("mi_heap_t*")] IntPtr heap, [NativeTypeName("const char*")] sbyte* s, [NativeTypeName("size_t")] nuint n);

    [return: NativeTypeName("char*")]
    public static partial sbyte* mi_heap_realpath([NativeTypeName("mi_heap_t*")] IntPtr heap, [NativeTypeName("const char*")] sbyte* fname, [NativeTypeName("char*")] sbyte* resolved_name);

    public static partial void* mi_heap_malloc_aligned([NativeTypeName("mi_heap_t*")] IntPtr heap, [NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment);

    public static partial void* mi_heap_malloc_aligned_at([NativeTypeName("mi_heap_t*")] IntPtr heap, [NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment, [NativeTypeName("size_t")] nuint offset);

    public static partial void* mi_heap_zalloc_aligned([NativeTypeName("mi_heap_t*")] IntPtr heap, [NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment);

    public static partial void* mi_heap_zalloc_aligned_at([NativeTypeName("mi_heap_t*")] IntPtr heap, [NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment, [NativeTypeName("size_t")] nuint offset);

    public static partial void* mi_heap_calloc_aligned([NativeTypeName("mi_heap_t*")] IntPtr heap, [NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment);

    public static partial void* mi_heap_calloc_aligned_at([NativeTypeName("mi_heap_t*")] IntPtr heap, [NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment, [NativeTypeName("size_t")] nuint offset);

    public static partial void* mi_heap_mallocn_aligned([NativeTypeName("mi_heap_t*")] IntPtr heap, [NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment);

    public static partial void* mi_heap_mallocn_aligned_at([NativeTypeName("mi_heap_t*")] IntPtr heap, [NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment, [NativeTypeName("size_t")] nuint offset);

    public static partial void* mi_heap_realloc_aligned([NativeTypeName("mi_heap_t*")] IntPtr heap, void* p, [NativeTypeName("size_t")] nuint newsize, [NativeTypeName("size_t")] nuint alignment);

    public static partial void* mi_heap_realloc_aligned_at([NativeTypeName("mi_heap_t*")] IntPtr heap, void* p, [NativeTypeName("size_t")] nuint newsize, [NativeTypeName("size_t")] nuint alignment, [NativeTypeName("size_t")] nuint offset);

    public static partial void* mi_heap_reallocn_aligned([NativeTypeName("mi_heap_t*")] IntPtr heap, void* p, [NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment);

    public static partial void* mi_heap_reallocn_aligned_at([NativeTypeName("mi_heap_t*")] IntPtr heap, void* p, [NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment, [NativeTypeName("size_t")] nuint offset);

    // --------------------------------------------------------------------------------
    // Zero initialized re-allocation.
    // Only valid on memory that was originally allocated with zero initialization too.
    // e.g. `mi_calloc`, `mi_zalloc`, `mi_zalloc_aligned` etc.
    // see <https://github.com/microsoft/mimalloc/issues/63#issuecomment-508272992>
    // --------------------------------------------------------------------------------

    public static partial void* mi_rezalloc(void* p, [NativeTypeName("size_t")] nuint newsize);

    public static partial void* mi_recalloc(void* p, [NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint size);

    public static partial void* mi_rezalloc_aligned(void* p, [NativeTypeName("size_t")] nuint newsize, [NativeTypeName("size_t")] nuint alignment);

    public static partial void* mi_rezalloc_aligned_at(void* p, [NativeTypeName("size_t")] nuint newsize, [NativeTypeName("size_t")] nuint alignment, [NativeTypeName("size_t")] nuint offset);

    public static partial void* mi_recalloc_aligned(void* p, [NativeTypeName("size_t")] nuint newcount, [NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment);

    public static partial void* mi_recalloc_aligned_at(void* p, [NativeTypeName("size_t")] nuint newcount, [NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment, [NativeTypeName("size_t")] nuint offset);

    public static partial void* mi_heap_rezalloc([NativeTypeName("mi_heap_t*")] IntPtr heap, void* p, [NativeTypeName("size_t")] nuint newsize);

    public static partial void* mi_heap_recalloc([NativeTypeName("mi_heap_t*")] IntPtr heap, void* p, [NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint size);

    public static partial void* mi_heap_rezalloc_aligned([NativeTypeName("mi_heap_t*")] IntPtr heap, void* p, [NativeTypeName("size_t")] nuint newsize, [NativeTypeName("size_t")] nuint alignment);

    public static partial void* mi_heap_rezalloc_aligned_at([NativeTypeName("mi_heap_t*")] IntPtr heap, void* p, [NativeTypeName("size_t")] nuint newsize, [NativeTypeName("size_t")] nuint alignment, [NativeTypeName("size_t")] nuint offset);

    public static partial void* mi_heap_recalloc_aligned([NativeTypeName("mi_heap_t*")] IntPtr heap, void* p, [NativeTypeName("size_t")] nuint newcount, [NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment);

    public static partial void* mi_heap_recalloc_aligned_at([NativeTypeName("mi_heap_t*")] IntPtr heap, void* p, [NativeTypeName("size_t")] nuint newcount, [NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment, [NativeTypeName("size_t")] nuint offset);

    // ------------------------------------------------------
    // Analysis
    // ------------------------------------------------------

    public static partial bool mi_heap_contains_block([NativeTypeName("mi_heap_t*")] IntPtr heap, [NativeTypeName("const void*")] void* p);

    public static partial bool mi_heap_check_owned([NativeTypeName("mi_heap_t*")] IntPtr heap, [NativeTypeName("const void*")] void* p);

    public static partial bool mi_check_owned([NativeTypeName("const void*")] void* p);

    public static partial bool mi_heap_visit_blocks([NativeTypeName("const mi_heap_t*")] IntPtr heap, bool visit_blocks, mi_block_visit_fun visitor, void* arg);

    // Experimental
    public static partial bool mi_is_in_heap_region([NativeTypeName("const void*")] void* p);

    public static partial bool mi_is_redirected();

    public static partial int mi_reserve_huge_os_pages_interleave([NativeTypeName("size_t")] nuint pages, [NativeTypeName("size_t")] nuint numa_nodes, [NativeTypeName("size_t")] nuint timeout_msecs);

    public static partial int mi_reserve_huge_os_pages_at([NativeTypeName("size_t")] nuint pages, int numa_node, [NativeTypeName("size_t")] nuint timeout_msecs);

    // deprecated
    public static partial int mi_reserve_huge_os_pages([NativeTypeName("size_t")] nuint pages, double max_secs, [NativeTypeName("size_t*")] nuint* pages_reserved);

    // ------------------------------------------------------
    // Convenience
    // ------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T* mi_malloc_tp<T>()
        where T : unmanaged => (T*)mi_malloc(SizeOf<T>());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T* mi_zalloc_tp<T>()
        where T : unmanaged => (T*)mi_zalloc(SizeOf<T>());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T* mi_calloc_tp<T>([NativeTypeName("size_t")] nuint count)
        where T : unmanaged => (T*)mi_calloc(count, SizeOf<T>());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T* mi_mallocn_tp<T>([NativeTypeName("size_t")] nuint count)
        where T : unmanaged => (T*)mi_mallocn(count, SizeOf<T>());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T* mi_realloc_tp<T>(void* p)
        where T : unmanaged => (T*)mi_realloc(p, SizeOf<T>());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T* mi_reallocn_tp<T>(void* p, [NativeTypeName("size_t")] nuint count)
        where T : unmanaged => (T*)mi_reallocn(p, count, SizeOf<T>());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T* mi_rezalloc_tp<T>(void* p)
        where T : unmanaged => (T*)mi_rezalloc(p, SizeOf<T>());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T* mi_recalloc_tp<T>(void* p, [NativeTypeName("size_t")] nuint newcount)
        where T : unmanaged => (T*)mi_recalloc(p, newcount, SizeOf<T>());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T* mi_malloc_aligned_tp<T>([NativeTypeName("size_t")] nuint alignment)
        where T : unmanaged => (T*)mi_malloc_aligned(SizeOf<T>(), alignment);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T* mi_malloc_aligned_at_tp<T>([NativeTypeName("size_t")] nuint alignment, [NativeTypeName("size_t")] nuint offset)
        where T : unmanaged => (T*)mi_malloc_aligned_at(SizeOf<T>(), alignment, offset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T* mi_zalloc_aligned_tp<T>([NativeTypeName("size_t")] nuint alignment)
        where T : unmanaged => (T*)mi_zalloc_aligned(SizeOf<T>(), alignment);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T* mi_zalloc_aligned_at_tp<T>([NativeTypeName("size_t")] nuint alignment, [NativeTypeName("size_t")] nuint offset)
        where T : unmanaged => (T*)mi_zalloc_aligned_at(SizeOf<T>(), alignment, offset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T* mi_calloc_aligned_tp<T>([NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint alignment)
        where T : unmanaged => (T*)mi_calloc_aligned(count, SizeOf<T>(), alignment);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T* mi_calloc_aligned_at_tp<T>([NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint alignment, [NativeTypeName("size_t")] nuint offset)
        where T : unmanaged => (T*)mi_calloc_aligned_at(count, SizeOf<T>(), alignment, offset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T* mi_mallocn_aligned_tp<T>([NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint alignment)
        where T : unmanaged => (T*)mi_mallocn_aligned(count, SizeOf<T>(), alignment);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T* mi_mallocn_aligned_at_tp<T>([NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint alignment, [NativeTypeName("size_t")] nuint offset)
        where T : unmanaged => (T*)mi_mallocn_aligned_at(count, SizeOf<T>(), alignment, offset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T* mi_realloc_aligned_tp<T>(void* p, [NativeTypeName("size_t")] nuint alignment)
        where T : unmanaged => (T*)mi_realloc_aligned(p, SizeOf<T>(), alignment);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T* mi_realloc_aligned_at_tp<T>(void* p, [NativeTypeName("size_t")] nuint alignment, [NativeTypeName("size_t")] nuint offset)
        where T : unmanaged => (T*)mi_realloc_aligned_at(p, SizeOf<T>(), alignment, offset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T* mi_reallocn_aligned_tp<T>(void* p, [NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint alignment)
        where T : unmanaged => (T*)mi_reallocn_aligned(p, count, SizeOf<T>(), alignment);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T* mi_reallocn_aligned_at_tp<T>(void* p, [NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint alignment, [NativeTypeName("size_t")] nuint offset)
        where T : unmanaged => (T*)mi_reallocn_aligned_at(p, count, SizeOf<T>(), alignment, offset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T* mi_rezalloc_aligned_tp<T>(void* p, [NativeTypeName("size_t")] nuint alignment)
        where T : unmanaged => (T*)mi_rezalloc_aligned(p,  SizeOf<T>(), alignment);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T* mi_rezalloc_aligned_at_tp<T>(void* p, [NativeTypeName("size_t")] nuint alignment, [NativeTypeName("size_t")] nuint offset)
        where T : unmanaged => (T*)mi_rezalloc_aligned_at(p, SizeOf<T>(), alignment, offset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T* mi_recalloc_aligned_tp<T>(void* p, [NativeTypeName("size_t")] nuint newcount, [NativeTypeName("size_t")] nuint alignment)
        where T : unmanaged => (T*)mi_recalloc_aligned(p, newcount, SizeOf<T>(), alignment);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T* mi_recalloc_aligned_at_tp<T>(void* p, [NativeTypeName("size_t")] nuint newcount, [NativeTypeName("size_t")] nuint alignment, [NativeTypeName("size_t")] nuint offset)
        where T : unmanaged => (T*)mi_recalloc_aligned_at(p, newcount, SizeOf<T>(), alignment, offset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T* mi_heap_malloc_tp<T>([NativeTypeName("mi_heap_t*")] IntPtr heap)
        where T : unmanaged => (T*)mi_heap_malloc(heap, SizeOf<T>());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T* mi_heap_zalloc_tp<T>([NativeTypeName("mi_heap_t*")] IntPtr heap)
        where T : unmanaged => (T*)mi_heap_zalloc(heap, SizeOf<T>());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T* mi_heap_calloc_tp<T>([NativeTypeName("mi_heap_t*")] IntPtr heap, [NativeTypeName("size_t")] nuint count)
        where T : unmanaged => (T*)mi_heap_calloc(heap, count, SizeOf<T>());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T* mi_heap_mallocn_tp<T>([NativeTypeName("mi_heap_t*")] IntPtr heap, [NativeTypeName("size_t")] nuint count)
        where T : unmanaged => (T*)mi_heap_mallocn(heap, count, SizeOf<T>());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T* mi_heap_realloc_tp<T>([NativeTypeName("mi_heap_t*")] IntPtr heap, void* p)
        where T : unmanaged => (T*)mi_heap_realloc(heap, p, SizeOf<T>());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T* mi_heap_reallocn_tp<T>([NativeTypeName("mi_heap_t*")] IntPtr heap, void* p, [NativeTypeName("size_t")] nuint count)
        where T : unmanaged => (T*)mi_heap_reallocn(heap, p, count, SizeOf<T>());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T* mi_heap_rezalloc_tp<T>([NativeTypeName("mi_heap_t*")] IntPtr heap, void* p)
        where T : unmanaged => (T*)mi_heap_rezalloc(heap, p, SizeOf<T>());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T* mi_heap_recalloc_tp<T>([NativeTypeName("mi_heap_t*")] IntPtr heap, void* p, [NativeTypeName("size_t")] nuint newcount)
        where T : unmanaged => (T*)mi_heap_recalloc(heap, p, newcount, SizeOf<T>());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T* mi_heap_malloc_aligned_tp<T>([NativeTypeName("mi_heap_t*")] IntPtr heap, [NativeTypeName("size_t")] nuint alignment)
        where T : unmanaged => (T*)mi_heap_malloc_aligned(heap, SizeOf<T>(), alignment);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T* mi_heap_malloc_aligned_at_tp<T>([NativeTypeName("mi_heap_t*")] IntPtr heap, [NativeTypeName("size_t")] nuint alignment, [NativeTypeName("size_t")] nuint offset)
        where T : unmanaged => (T*)mi_heap_malloc_aligned_at(heap, SizeOf<T>(), alignment, offset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T* mi_heap_zalloc_aligned_tp<T>([NativeTypeName("mi_heap_t*")] IntPtr heap, [NativeTypeName("size_t")] nuint alignment)
        where T : unmanaged => (T*)mi_heap_zalloc_aligned(heap, SizeOf<T>(), alignment);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T* mi_heap_zalloc_aligned_at_tp<T>([NativeTypeName("mi_heap_t*")] IntPtr heap, [NativeTypeName("size_t")] nuint alignment, [NativeTypeName("size_t")] nuint offset)
        where T : unmanaged => (T*)mi_heap_zalloc_aligned_at(heap, SizeOf<T>(), alignment, offset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T* mi_heap_calloc_aligned_tp<T>([NativeTypeName("mi_heap_t*")] IntPtr heap, [NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint alignment)
        where T : unmanaged => (T*)mi_heap_calloc_aligned(heap, count, SizeOf<T>(), alignment);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T* mi_heap_calloc_aligned_at_tp<T>([NativeTypeName("mi_heap_t*")] IntPtr heap, [NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint alignment, [NativeTypeName("size_t")] nuint offset)
        where T : unmanaged => (T*)mi_heap_calloc_aligned_at(heap, count, SizeOf<T>(), alignment, offset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T* mi_heap_mallocn_aligned_tp<T>([NativeTypeName("mi_heap_t*")] IntPtr heap, [NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint alignment)
        where T : unmanaged => (T*)mi_heap_mallocn_aligned(heap, count, SizeOf<T>(), alignment);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T* mi_heap_mallocn_aligned_at_tp<T>([NativeTypeName("mi_heap_t*")] IntPtr heap, [NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint alignment, [NativeTypeName("size_t")] nuint offset)
        where T : unmanaged => (T*)mi_heap_mallocn_aligned_at(heap, count, SizeOf<T>(), alignment, offset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T* mi_heap_realloc_aligned_tp<T>([NativeTypeName("mi_heap_t*")] IntPtr heap, void* p, [NativeTypeName("size_t")] nuint alignment)
        where T : unmanaged => (T*)mi_heap_realloc_aligned(heap, p, SizeOf<T>(), alignment);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T* mi_heap_realloc_aligned_at_tp<T>([NativeTypeName("mi_heap_t*")] IntPtr heap, void* p, [NativeTypeName("size_t")] nuint alignment, [NativeTypeName("size_t")] nuint offset)
        where T : unmanaged => (T*)mi_heap_realloc_aligned_at(heap, p, SizeOf<T>(), alignment, offset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T* mi_heap_reallocn_aligned_tp<T>([NativeTypeName("mi_heap_t*")] IntPtr heap, void* p, [NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint alignment)
        where T : unmanaged => (T*)mi_heap_reallocn_aligned(heap, p, count, SizeOf<T>(), alignment);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T* mi_heap_reallocn_aligned_at_tp<T>([NativeTypeName("mi_heap_t*")] IntPtr heap, void* p, [NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint alignment, [NativeTypeName("size_t")] nuint offset)
        where T : unmanaged => (T*)mi_heap_reallocn_aligned_at(heap, p, count, SizeOf<T>(), alignment, offset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T* mi_heap_rezalloc_aligned_tp<T>([NativeTypeName("mi_heap_t*")] IntPtr heap, void* p, [NativeTypeName("size_t")] nuint alignment)
        where T : unmanaged => (T*)mi_heap_rezalloc_aligned(heap, p, SizeOf<T>(), alignment);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T* mi_heap_rezalloc_aligned_at_tp<T>([NativeTypeName("mi_heap_t*")] IntPtr heap, void* p, [NativeTypeName("size_t")] nuint alignment, [NativeTypeName("size_t")] nuint offset)
        where T : unmanaged => (T*)mi_heap_rezalloc_aligned_at(heap, p, SizeOf<T>(), alignment, offset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T* mi_heap_recalloc_aligned_tp<T>([NativeTypeName("mi_heap_t*")] IntPtr heap, void* p, [NativeTypeName("size_t")] nuint newcount, [NativeTypeName("size_t")] nuint alignment)
        where T : unmanaged => (T*)mi_heap_recalloc_aligned(heap, p, newcount, SizeOf<T>(), alignment);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T* mi_heap_recalloc_aligned_at_tp<T>([NativeTypeName("mi_heap_t*")] IntPtr heap, void* p, [NativeTypeName("size_t")] nuint newcount, [NativeTypeName("size_t")] nuint alignment, [NativeTypeName("size_t")] nuint offset)
        where T : unmanaged => (T*)mi_heap_recalloc_aligned_at(heap, p, newcount, SizeOf<T>(), alignment, offset);

    // ------------------------------------------------------
    // Options, all `false` by default
    // ------------------------------------------------------

    public static partial bool mi_option_is_enabled(mi_option_t option);

    public static partial void mi_option_enable(mi_option_t option);

    public static partial void mi_option_disable(mi_option_t option);

    public static partial void mi_option_set_enabled(mi_option_t option, bool enable);

    public static partial void mi_option_set_enabled_default(mi_option_t option, bool enable);

    [return: NativeTypeName("long")]
    public static partial int mi_option_get(mi_option_t option);

    public static partial void mi_option_set(mi_option_t option, [NativeTypeName("long")] int value);

    public static partial void mi_option_set_default(mi_option_t option, [NativeTypeName("long")] int value);

    // -------------------------------------------------------------------------------------------------------
    // "mi" prefixed implementations of various posix, Unix, Windows, and C++ allocation functions.
    // (This can be convenient when providing overrides of these functions as done in `mimalloc-override.h`.)
    // note: we use `mi_cfree` as "checked free" and it checks if the pointer is in our heap before free-ing.
    // -------------------------------------------------------------------------------------------------------

    public static partial void mi_cfree(void* p);

    public static partial void* mi__expand(void* p, [NativeTypeName("size_t")] nuint newsize);

    [return: NativeTypeName("size_t")]
    public static partial nuint mi_malloc_size([NativeTypeName("const void*")] void* p);

    [return: NativeTypeName("size_t")]
    public static partial nuint mi_malloc_usable_size([NativeTypeName("const void*")] void* p);

    public static partial int mi_posix_memalign(void** p, [NativeTypeName("size_t")] nuint alignment, [NativeTypeName("size_t")] nuint size);

    public static partial void* mi_memalign([NativeTypeName("size_t")] nuint alignment, [NativeTypeName("size_t")] nuint size);

    public static partial void* mi_valloc([NativeTypeName("size_t")] nuint size);

    public static partial void* mi_pvalloc([NativeTypeName("size_t")] nuint size);

    public static partial void* mi_aligned_alloc([NativeTypeName("size_t")] nuint alignment, [NativeTypeName("size_t")] nuint size);

    public static partial void* mi_reallocarray(void* p, [NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint size);

    public static partial void* mi_aligned_recalloc(void* p, [NativeTypeName("size_t")] nuint newcount, [NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment);

    public static partial void* mi_aligned_offset_recalloc(void* p, [NativeTypeName("size_t")] nuint newcount, [NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment, [NativeTypeName("size_t")] nuint offset);

    [return: NativeTypeName("unsigned short*")]
    public static partial ushort* mi_wcsdup([NativeTypeName("const unsigned short*")] ushort* s);

    [return: NativeTypeName("unsigned char*")]
    public static partial byte* mi_mbsdup([NativeTypeName("const unsigned char*")] byte* s);

    public static partial int mi_dupenv_s([NativeTypeName("char**")] sbyte** buf, [NativeTypeName("size_t*")] nuint* size, [NativeTypeName("const char*")] sbyte* name);

    public static partial int mi_wdupenv_s([NativeTypeName("unsigned short**")] ushort** buf, [NativeTypeName("size_t*")] nuint* size, [NativeTypeName("const unsigned short*")] ushort* name);

    public static partial void mi_free_size(void* p, [NativeTypeName("size_t")] nuint size);

    public static partial void mi_free_size_aligned(void* p, [NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment);

    public static partial void mi_free_aligned(void* p, [NativeTypeName("size_t")] nuint alignment);

    // The `mi_new` wrappers implement C++ semantics on out-of-memory instead of directly returning `NULL`.
    // (and call `std::get_new_handler` and potentially raise a `std::bad_alloc` exception).

    public static partial void* mi_new([NativeTypeName("size_t")] nuint size);

    public static partial void* mi_new_aligned([NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment);

    public static partial void* mi_new_nothrow([NativeTypeName("size_t")] nuint size);

    public static partial void* mi_new_aligned_nothrow([NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment);

    public static partial void* mi_new_n([NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint size);

    public static partial void* mi_new_realloc(void* p, [NativeTypeName("size_t")] nuint newsize);

    public static partial void* mi_new_reallocn(void* p, [NativeTypeName("size_t")] nuint newcount, [NativeTypeName("size_t")] nuint size);
}
