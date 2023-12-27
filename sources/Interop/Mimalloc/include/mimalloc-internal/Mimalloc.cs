// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the mimalloc-internal.h file from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using static TerraFX.Interop.Mimalloc.mi_delayed_t;
using static TerraFX.Interop.Mimalloc.mi_page_kind_t;

namespace TerraFX.Interop.Mimalloc;

public static unsafe partial class Mimalloc
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void mi_trace_message([NativeTypeName("const char*")] string fmt, params object[] args)
    {
        if (MI_DEBUG > 0)
        {
            _mi_trace_message(fmt, args);
        }
    }

    // private const ushort MI_CACHE_LINE = 64;

    // "options.c"

    private static partial void _mi_fputs([NativeTypeName("mi_output_fun*")] mi_output_fun? @out, void* arg, [NativeTypeName("const char*")] string prefix, [NativeTypeName("const char*")] string message);

    private static partial void _mi_fprintf([NativeTypeName("mi_output_fun*")] mi_output_fun? @out, void* arg, [NativeTypeName("const char*")] string fmt, params object[] args);

    private static partial void _mi_warning_message([NativeTypeName("const char*")] string fmt, params object[] args);

    private static partial void _mi_verbose_message([NativeTypeName("const char*")] string fmt, params object[] args);

    private static partial void _mi_trace_message([NativeTypeName("const char*")] string fmt, params object[] args);

    private static partial void _mi_options_init();

    private static partial void _mi_error_message(int err, [NativeTypeName("const char*")] string fmt, params object[] args);

    // random.c

    private static partial void _mi_random_init([NativeTypeName("mi_random_ctx_t*")] out mi_random_ctx_t ctx);

    private static partial void _mi_random_split([NativeTypeName("mi_random_ctx_t*")] in mi_random_ctx_t ctx, [NativeTypeName("mi_random_ctx_t*")] out mi_random_ctx_t ctx_new);

    [return: NativeTypeName("uintptr_t")]
    private static partial nuint _mi_random_next([NativeTypeName("mi_random_ctx_t*")] ref mi_random_ctx_t ctx);

    [return: NativeTypeName("uintptr_t")]
    private static partial nuint _mi_heap_random_next(mi_heap_t* heap);

    // The following members have not been ported as they aren't needed for .NET:
    //  * uintptr_t _os_random_weak(uintptr_t)

    [return: NativeTypeName("uintptr_t")]
    private static partial nuint _mi_random_shuffle([NativeTypeName("uintptr_t")] nuint x);

    // init.c

    private static partial bool _mi_is_main_thread();

    // The following members have not been ported as they aren't needed for .NET:
    //  * bool _mi_preloading()

    // os.c

    [return: NativeTypeName("size_t")]
    private static partial nuint _mi_os_page_size();

    // The following members have not been ported as they aren't needed for .NET:
    //  * void _mi_os_init()

    // to allocate thread local data
    private static partial void* _mi_os_alloc([NativeTypeName("size_t")] nuint size, [NativeTypeName("mi_stats_t*")] ref mi_stats_t tld_stats);

    // to free thread local data
    private static partial void _mi_os_free(void* p, [NativeTypeName("size_t")] nuint size, [NativeTypeName("mi_stats_t*")] ref mi_stats_t stats);

    [return: NativeTypeName("size_t")]
    private static partial nuint _mi_os_good_alloc_size([NativeTypeName("size_t")] nuint size);

    // memory.c

    private static partial void* _mi_mem_alloc_aligned([NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment, [NativeTypeName("bool*")] ref bool commit, [NativeTypeName("bool*")] ref bool large, [NativeTypeName("bool*")] out bool is_zero, [NativeTypeName("size_t*")] out nuint memid, mi_os_tld_t* tld);

    private static partial void _mi_mem_free(void* p, [NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint id, bool full_commit, bool any_reset, mi_os_tld_t* tld);

    private static partial bool _mi_mem_reset(void* p, [NativeTypeName("size_t")] nuint size, mi_os_tld_t* tld);

    private static partial bool _mi_mem_unreset(void* p, [NativeTypeName("size_t")] nuint size, [NativeTypeName("bool*")] out bool is_zero, mi_os_tld_t* tld);

    private static partial bool _mi_mem_commit(void* p, [NativeTypeName("size_t")] nuint size, [NativeTypeName("bool*")] out bool is_zero, mi_os_tld_t* tld);

    private static partial bool _mi_mem_protect(void* p, [NativeTypeName("size_t")] nuint size);

    private static partial bool _mi_mem_unprotect(void* p, [NativeTypeName("size_t")] nuint size);

    private static partial void _mi_mem_collect(mi_os_tld_t* tld);

    // "segment.c"

    private static partial mi_page_t* _mi_segment_page_alloc(mi_heap_t* heap, [NativeTypeName("size_t")] nuint block_size, mi_segments_tld_t* tld, mi_os_tld_t* os_tld);

    private static partial void _mi_segment_page_free(mi_page_t* page, bool force, mi_segments_tld_t* tld);

    private static partial void _mi_segment_page_abandon(mi_page_t* page, mi_segments_tld_t* tld);

    // page start for any page
    [return: NativeTypeName("uint8_t*")]
    private static partial byte* _mi_segment_page_start([NativeTypeName("const mi_segment_t*")] mi_segment_t* segment, [NativeTypeName("const mi_page_t*")] mi_page_t* page, [NativeTypeName("size_t")] nuint block_size, [NativeTypeName("size_t*")] out nuint page_size, [NativeTypeName("size_t*")] out nuint pre_size);

    private static partial void _mi_segment_huge_page_free(mi_segment_t* segment, mi_page_t* page, mi_block_t* block);

    private static partial void _mi_segment_thread_collect(mi_segments_tld_t* tld);

    private static partial void _mi_abandoned_reclaim_all(mi_heap_t* heap, mi_segments_tld_t* tld);

    private static partial void _mi_abandoned_await_readers();

    // "page.c"

    private static partial void* _mi_malloc_generic(mi_heap_t* heap, [NativeTypeName("size_t")] nuint size);

    // free the page if there are no other pages with many free blocks
    private static partial void _mi_page_retire(mi_page_t* page);

    private static partial void _mi_page_unfull(mi_page_t* page);

    // free the page
    private static partial void _mi_page_free(mi_page_t* page, mi_page_queue_t* pq, bool force);

    // abandon the page, to be picked up by another thread...
    private static partial void _mi_page_abandon(mi_page_t* page, mi_page_queue_t* pq);

    private static partial void _mi_heap_delayed_free(mi_heap_t* heap);

    private static partial void _mi_heap_collect_retired(mi_heap_t* heap, bool force);

    private static partial void _mi_page_use_delayed_free(mi_page_t* page, mi_delayed_t delay, bool override_never);

    [return: NativeTypeName("size_t")]
    private static partial nuint _mi_page_queue_append(mi_heap_t* heap, mi_page_queue_t* pq, mi_page_queue_t* append);

    private static partial void _mi_deferred_free(mi_heap_t* heap, bool force);

    private static partial void _mi_page_free_collect(mi_page_t* page, bool force);

    // callback from segments
    private static partial void _mi_page_reclaim(mi_heap_t* heap, mi_page_t* page);

    // for stats
    [return: NativeTypeName("size_t")]
    private static partial nuint _mi_bin_size([NativeTypeName("uint8_t")] byte bin);

    // for stats
    [return: NativeTypeName("uint8_t")]
    private static partial byte _mi_bin([NativeTypeName("size_t")] nuint size);

    // bit-scan-right, used on BSD in "os.c"
    [return: NativeTypeName("uint8_t")]
    private static partial byte _mi_bsr([NativeTypeName("uintptr_t")] nuint x);

    // "heap.c"

    private static partial void _mi_heap_destroy_pages(mi_heap_t* heap);

    private static partial void _mi_heap_collect_abandon(mi_heap_t* heap);

    private static partial void _mi_heap_set_default_direct(mi_heap_t* heap);

    // "stats.c"

    private static partial void _mi_stats_done([NativeTypeName("mi_stats_t*")] ref mi_stats_t stats);

    [return: NativeTypeName("mi_msecs_t")]
    private static partial long _mi_clock_now();

    [return: NativeTypeName("mi_msecs_t")]
    private static partial long _mi_clock_end([NativeTypeName("mi_msecs_t")] long start);

    [return: NativeTypeName("mi_msecs_t")]
    private static partial long _mi_clock_start();

    // "alloc.c"

    // called from `_mi_malloc_generic`
    private static partial void* _mi_page_malloc(mi_heap_t* heap, mi_page_t* page, [NativeTypeName("size_t")] nuint size);

    private static partial void* _mi_heap_malloc_zero(mi_heap_t* heap, [NativeTypeName("size_t")] nuint size, bool zero);

    private static partial void* _mi_heap_realloc_zero(mi_heap_t* heap, void* p, [NativeTypeName("size_t")] nuint newsize, bool zero);

    private static partial mi_block_t* _mi_page_ptr_unalign([NativeTypeName("const mi_segment_t*")] mi_segment_t* segment, [NativeTypeName("const mi_page_t*")] mi_page_t* page, [NativeTypeName("const void*")] void* p);

    private static partial bool _mi_free_delayed_block(mi_block_t* block);

    private static partial void _mi_block_zero_init([NativeTypeName("const mi_page_t*")] mi_page_t* page, void* p, [NativeTypeName("size_t")] nuint size);

    private static partial bool _mi_page_is_valid(mi_page_t* page);

    // ------------------------------------------------------
    // Branches
    // ------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool mi_unlikely(bool x) => x;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool mi_likely(bool x) => x;

    /* -----------------------------------------------------------
      Error codes passed to `_mi_fatal_error`
      All are recoverable but EFAULT is a serious error and aborts by default in secure mode.
      For portability define undefined error codes using common Unix codes:
      <https://www-numi.fnal.gov/offline_software/srt_private_context/WebDocs/Errors/unix_system_errors.html>
    ----------------------------------------------------------- */

    // double free
    private const int EAGAIN = 11;

    // out of memory
    private const int ENOMEM = 12;

    // corrupted free-list or meta-data
    private const int EFAULT = 14;

    // trying to free an invalid pointer
    private const int EINVAL = 22;

    // count*size overflow
    private const int EOVERFLOW = 75;

    /* -----------------------------------------------------------
      Inlined definitions
    ----------------------------------------------------------- */

    // Is `x` a power of two? (0 is considered a power of two)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool _mi_is_power_of_two([NativeTypeName("uintptr_t")] nuint x) => (x & (x - 1)) == 0;

    // Align upwards
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return: NativeTypeName("uintptr_t")]
    private static nuint _mi_align_up([NativeTypeName("uintptr_t")] nuint sz, [NativeTypeName("size_t")] nuint alignment)
    {
        mi_assert_internal((MI_DEBUG > 1) && (alignment != 0));
        nuint mask = alignment - 1;

        if ((alignment & mask) == 0)    // power of two?
        {
            return (sz + mask) & ~mask;
        }
        else
        {
            return (sz + mask) / alignment * alignment;
        }
    }

    // Divide upwards: `s <= _mi_divide_up(s,d)*d < s+d`.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return: NativeTypeName("uintptr_t")]
    private static nuint _mi_divide_up([NativeTypeName("uintptr_t")] nuint size, [NativeTypeName("size_t")] nuint divider)
    {
        mi_assert_internal((MI_DEBUG > 1) && (divider != 0));
        return (divider == 0) ? size : ((size + divider - 1) / divider);
    }

    // Is memory zero initialized?
    private static bool mi_mem_is_zero(void* p, [NativeTypeName("size_t")] nuint size)
    {
        for (nuint i = 0; i < size; i++)
        {
            if (((byte*)p)[i] != 0)
            {
                return false;
            }
        }

        return true;
    }

    // Align a byte size to a size in _machine words_,
    // i.e. byte size == `wsize*sizeof(void*)`.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return: NativeTypeName("size_t")]
    private static nuint _mi_wsize_from_size([NativeTypeName("size_t")] nuint size)
    {
        mi_assert_internal((MI_DEBUG > 1) && (size <= (SIZE_MAX - SizeOf<nuint>())));
        return (size + SizeOf<nuint>() - 1) / SizeOf<nuint>();
    }

    // Does malloc satisfy the alignment constraints already?
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool mi_malloc_satisfies_alignment([NativeTypeName("size_t")] nuint alignment, [NativeTypeName("size_t")] nuint size)
        => (alignment == SizeOf<nuint>()) || ((alignment == MI_MAX_ALIGN_SIZE) && (size > (MI_MAX_ALIGN_SIZE / 2)));

    // Overflow detecting multiply
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool mi_mul_overflow([NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t*")] out nuint total)
    {
        total = count * size;
        return ((size >= MI_MUL_NO_OVERFLOW) || (count >= MI_MUL_NO_OVERFLOW)) && (size > 0) && ((SIZE_MAX / size) < count);
    }

    // Safe multiply `count*size` into `total`; return `true` on overflow.
    private static bool mi_count_size_overflow([NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t*")] out nuint total)
    {
        if (count == 1) // quick check for the case where count is one (common for C++ allocators)
        {
            total = size;
            return false;
        }
        else if (mi_unlikely(mi_mul_overflow(count, size, out total)))
        {
            _mi_error_message(EOVERFLOW, "allocation request is too large ({0} * {1} bytes)\n", count, size);
            total = SIZE_MAX;
            return true;
        }
        else
        {
            return false;
        }
    }

    /* ----------------------------------------------------------------------------------------
      The thread local default heap: `_mi_get_default_heap` returns the thread local heap.
    ------------------------------------------------------------------------------------------- */

    // statically allocated main backing heap
    private static partial mi_heap_t* _mi_heap_main_get();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static mi_heap_t* mi_get_default_heap()
    {
        var mi_heap_default = _mi_heap_default;

        if (mi_unlikely(mi_heap_default == null))
        {
            mi_heap_default = create_mi_heap_default();
            _mi_heap_default = mi_heap_default;
        }
        return mi_heap_default;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool mi_heap_is_default([NativeTypeName("const mi_heap_t")] mi_heap_t* heap) => heap == mi_get_default_heap();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool mi_heap_is_backing([NativeTypeName("const mi_heap_t")] mi_heap_t* heap) => heap->tld->heap_backing == heap;

    // The following members have not been ported as they aren't needed for .NET:
    //  * bool mi_heap_is_initialized(mi_heap_t*)

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return: NativeTypeName("uintptr_t")]
    private static nuint _mi_ptr_cookie([NativeTypeName("const void*")] void* p)
    {
        mi_assert_internal((MI_DEBUG > 1) && (_mi_heap_main->cookie != 0));
        return (nuint)p ^ _mi_heap_main->cookie;
    }

    /* -----------------------------------------------------------
      Pages
    ----------------------------------------------------------- */

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static mi_page_t* _mi_heap_get_free_small_page(mi_heap_t* heap, [NativeTypeName("size_t")] nuint size)
    {
        mi_assert_internal((MI_DEBUG > 1) && (size <= (MI_SMALL_SIZE_MAX + MI_PADDING_SIZE)));
        nuint idx = _mi_wsize_from_size(size);

        mi_assert_internal((MI_DEBUG > 1) && (idx < MI_PAGES_DIRECT));
        return (&heap->pages_free_direct.e0)[idx];
    }

    // Get the page belonging to a certain size class
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static mi_page_t* _mi_get_free_small_page([NativeTypeName("size_t")] nuint size) => _mi_heap_get_free_small_page(mi_get_default_heap(), size);

    // Segment that contains the pointer
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static mi_segment_t* _mi_ptr_segment([NativeTypeName("const void*")] void* p) => (mi_segment_t*)((nuint)p & ~MI_SEGMENT_MASK);

    // Segment belonging to a page
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static mi_segment_t* _mi_page_segment([NativeTypeName("const mi_page_t*")] mi_page_t* page)
    {
        mi_segment_t* segment = _mi_ptr_segment(page);
        mi_assert_internal((MI_DEBUG > 1) && ((segment == null) || (page == (&segment->pages.e0 + page->segment_idx))));
        return segment;
    }

    // used internally
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return: NativeTypeName("uintptr_t")]
    private static nuint _mi_segment_page_idx_of([NativeTypeName("const mi_segment_t*")] mi_segment_t* segment, [NativeTypeName("const void*")] void* p)
    {
        nint diff = (nint)((nuint)p - (nuint)segment);
        mi_assert_internal((MI_DEBUG > 1) && (diff >= 0) && ((nuint)diff < MI_SEGMENT_SIZE));

        nuint idx = (nuint)diff >> (int)segment->page_shift;

        mi_assert_internal((MI_DEBUG > 1) && (idx < segment->capacity));
        mi_assert_internal((MI_DEBUG > 1) && ((segment->page_kind <= MI_PAGE_MEDIUM) || (idx == 0)));

        return idx;
    }

    // Get the page containing the pointer
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static mi_page_t* _mi_segment_page_of([NativeTypeName("const mi_segment_t*")] mi_segment_t* segment, [NativeTypeName("const void*")] void* p)
    {
        nuint idx = _mi_segment_page_idx_of(segment, p);
        return &segment->pages.e0 + idx;
    }

    // Quick page start for initialized pages
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return: NativeTypeName("uint8_t*")]
    private static byte* _mi_page_start([NativeTypeName("const mi_segment_t*")] mi_segment_t* segment, [NativeTypeName("const mi_page_t*")] mi_page_t* page, [NativeTypeName("size_t*")] out nuint page_size)
    {
        nuint bsize = page->xblock_size;
        mi_assert_internal((MI_DEBUG > 1) && (bsize > 0) && (bsize % SizeOf<nuint>()) == 0);
        return _mi_segment_page_start(segment, page, bsize, out page_size, out _);
    }

    // Get the page containing the pointer
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static mi_page_t* _mi_ptr_page(void* p) => _mi_segment_page_of(_mi_ptr_segment(p), p);

    // Get the block size of a page (special cased for huge objects)
    [return: NativeTypeName("size_t")]
    private static nuint mi_page_block_size([NativeTypeName("const mi_page_t*")] mi_page_t* page)
    {
        nuint bsize = page->xblock_size;
        mi_assert_internal((MI_DEBUG > 1) && (bsize > 0));

        if (mi_likely(bsize < MI_HUGE_BLOCK_SIZE))
        {
            return bsize;
        }
        else
        {
            _ = _mi_segment_page_start(_mi_page_segment(page), page, bsize, out nuint psize, out _);
            return psize;
        }
    }

    // Get the usable block size of a page without fixed padding.
    // This may still include internal padding due to alignment and rounding up size classes.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return: NativeTypeName("size_t")]
    private static nuint mi_page_usable_block_size([NativeTypeName("const mi_page_t*")] mi_page_t* page) => mi_page_block_size(page) - MI_PADDING_SIZE;

    // Thread free access

#pragma warning disable CS0420
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static mi_block_t* mi_page_thread_free([NativeTypeName("const mi_page_t*")] mi_page_t* page)
       => (mi_block_t*)(mi_atomic_load_relaxed(ref page->xthread_free) & ~(nuint)3);
#pragma warning restore CS0420

#pragma warning disable CS0420
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static mi_delayed_t mi_page_thread_free_flag([NativeTypeName("const mi_page_t*")] mi_page_t* page)
        => (mi_delayed_t)(mi_atomic_load_relaxed(ref page->xthread_free) & 3);
#pragma warning restore CS0420

    // Heap access

#pragma warning disable CS0420
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static mi_heap_t* mi_page_heap([NativeTypeName("const mi_page_t*")] mi_page_t* page)
        => (mi_heap_t*)mi_atomic_load_relaxed(ref page->xheap);
#pragma warning restore CS0420

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void mi_page_set_heap(mi_page_t* page, mi_heap_t* heap)
    {
#pragma warning disable CS0420
        mi_assert_internal((MI_DEBUG > 1) && (mi_page_thread_free_flag(page) != MI_DELAYED_FREEING));
        mi_atomic_store_release(ref page->xheap, (nuint)heap);
#pragma warning restore CS0420
    }

    // Thread free flag helpers

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static mi_block_t* mi_tf_block([NativeTypeName("mi_thread_free_t")] nuint tf) => (mi_block_t*)(tf & ~(nuint)0x03);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static mi_delayed_t mi_tf_delayed([NativeTypeName("mi_thread_free_t")] nuint tf) => (mi_delayed_t)(tf & 0x03);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return: NativeTypeName("mi_thread_free_t")]
    private static nuint mi_tf_make(mi_block_t* block, mi_delayed_t delayed) => (nuint)block | (nuint)delayed;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return: NativeTypeName("mi_thread_free_t")]
    private static nuint mi_tf_set_delayed([NativeTypeName("mi_thread_free_t")] nuint tf, mi_delayed_t delayed) => mi_tf_make(mi_tf_block(tf), delayed);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return: NativeTypeName("mi_thread_free_t")]
    private static nuint mi_tf_set_block([NativeTypeName("mi_thread_free_t")] nuint tf, mi_block_t* block) => mi_tf_make(block, mi_tf_delayed(tf));

    // are all blocks in a page freed?
    // note: needs up-to-date used count, (as the `xthread_free` list may not be empty). see `_mi_page_collect_free`.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool mi_page_all_free([NativeTypeName("const mi_page_t*")] mi_page_t* page)
    {
        mi_assert_internal((MI_DEBUG > 1) && (page != null));
        return page->used == 0;
    }

    // are there any available blocks?
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool mi_page_has_any_available([NativeTypeName("const mi_page_t*")] mi_page_t* page)
    {
        mi_assert_internal((MI_DEBUG > 1) && (page != null) && (page->reserved > 0));
        return (page->used < page->reserved) || (mi_page_thread_free(page) != null);
    }

    // are there immediately available blocks, i.e. blocks available on the free list.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool mi_page_immediate_available([NativeTypeName("const mi_page_t*")] mi_page_t* page)
    {
        mi_assert_internal((MI_DEBUG > 1) && (page != null));
        return page->free != null;
    }

    // is more than 7/8th of a page in use?
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool mi_page_mostly_used([NativeTypeName("const mi_page_t*")] mi_page_t* page)
    {
        if (page == null)
        {
            return true;
        }

        ushort frac = (ushort)(page->reserved / 8u);
        return (page->reserved - page->used) <= frac;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static mi_page_queue_t* mi_page_queue([NativeTypeName("const mi_heap_t*")] mi_heap_t* heap, [NativeTypeName("size_t")] nuint size) => &heap->pages.e0 + _mi_bin(size);

    //-----------------------------------------------------------
    // Page flags
    //-----------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool mi_page_is_in_full([NativeTypeName("const mi_page_t*")] mi_page_t* page) => page->flags.x.in_full;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void mi_page_set_in_full(mi_page_t* page, bool in_full) => page->flags.x.in_full = in_full;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool mi_page_has_aligned([NativeTypeName("const mi_page_t*")] mi_page_t* page) => page->flags.x.has_aligned;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void mi_page_set_has_aligned(mi_page_t* page, bool has_aligned) => page->flags.x.has_aligned = has_aligned;

    /* -------------------------------------------------------------------
    Encoding/Decoding the free list next pointers

    This is to protect against buffer overflow exploits where the
    free list is mutated. Many hardened allocators xor the next pointer `p`
    with a secret key `k1`, as `p^k1`. This prevents overwriting with known
    values but might be still too weak: if the attacker can guess
    the pointer `p` this  can reveal `k1` (since `p^k1^p == k1`).
    Moreover, if multiple blocks can be read as well, the attacker can
    xor both as `(p1^k1) ^ (p2^k1) == p1^p2` which may reveal a lot
    about the pointers (and subsequently `k1`).

    Instead mimalloc uses an extra key `k2` and encodes as `((p^k2)<<<k1)+k1`.
    Since these operations are not associative, the above approaches do not
    work so well any more even if the `p` can be guesstimated. For example,
    for the read case we can subtract two entries to discard the `+k1` term,
    but that leads to `((p1^k2)<<<k1) - ((p2^k2)<<<k1)` at best.
    We include the left-rotation since xor and addition are otherwise linear
    in the lowest bit. Finally, both keys are unique per page which reduces
    the re-use of keys by a large factor.

    We also pass a separate `null` value to be used as `NULL` or otherwise
    `(k2<<<k1)+k1` would appear (too) often as a sentinel value.
    ------------------------------------------------------------------- */

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool mi_is_in_same_segment([NativeTypeName("const void*")] void* p, [NativeTypeName("const void*")] void* q) => _mi_ptr_segment(p) == _mi_ptr_segment(q);

    private static bool mi_is_in_same_page([NativeTypeName("const void*")] void* p, [NativeTypeName("const void*")] void* q)
    {
        mi_segment_t* segmentp = _mi_ptr_segment(p);
        mi_segment_t* segmentq = _mi_ptr_segment(q);

        if (segmentp != segmentq)
        {
            return false;
        }

        nuint idxp = _mi_segment_page_idx_of(segmentp, p);
        nuint idxq = _mi_segment_page_idx_of(segmentq, q);

        return idxp == idxq;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return: NativeTypeName("uintptr_t")]
    private static nuint mi_rotl([NativeTypeName("uintptr_t")] nuint x, [NativeTypeName("uintptr_t")] nuint shift)
    {
        // We use the .NET rotate APIs rather than porting the native implementation

        if (Environment.Is64BitProcess)
        {
            return (nuint)BitOperations.RotateLeft(x, unchecked((int)shift));
        }
        else
        {
            return BitOperations.RotateLeft((uint)x, unchecked((int)shift));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return: NativeTypeName("uintptr_t")]
    private static nuint mi_rotr([NativeTypeName("uintptr_t")] nuint x, [NativeTypeName("uintptr_t")] nuint shift)
    {
        // We use the .NET rotate APIs rather than porting the native implementation

        if (Environment.Is64BitProcess)
        {
            return (nuint)BitOperations.RotateRight(x, unchecked((int)shift));
        }
        else

        {
            return BitOperations.RotateRight((uint)x, unchecked((int)shift));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void* mi_ptr_decode([NativeTypeName("const void*")] void* @null, [NativeTypeName("const mi_encoded_t")] nuint x, [NativeTypeName("const uintptr_t*")] nuint* keys)
    {
        void* p = (void*)(mi_rotr(unchecked(x - keys[0]), keys[0]) ^ keys[1]);
        return mi_unlikely(p == @null) ? null : p;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint mi_ptr_encode([NativeTypeName("const void*")] void* @null, [NativeTypeName("const void*")] void* p, [NativeTypeName("const uintptr_t*")] nuint* keys)
    {
        nuint x = (nuint)(mi_unlikely(p == null) ? @null : p);
        return unchecked(mi_rotl(x ^ keys[1], keys[0]) + keys[0]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static mi_block_t* mi_block_nextx([NativeTypeName("const void*")] void* @null, [NativeTypeName("const mi_block_t*")] mi_block_t* block, [NativeTypeName("const uintptr_t*")] nuint* keys)
    {
        if (MI_ENCODE_FREELIST != 0)
        {
            return (mi_block_t*)mi_ptr_decode(@null, block->next, keys);
        }
        else
        {
            return (mi_block_t*)block->next;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void mi_block_set_nextx([NativeTypeName("const void*")] void* @null, mi_block_t* block, [NativeTypeName("const mi_block_t*")] mi_block_t* next, [NativeTypeName("const uintptr_t*")] nuint* keys)
    {
        if (MI_ENCODE_FREELIST != 0)
        {
            block->next = mi_ptr_encode(@null, next, keys);
        }
        else
        {
            block->next = (nuint)next;
        }
    }

    private static mi_block_t* mi_block_next([NativeTypeName("const mi_page_t*")] mi_page_t* page, [NativeTypeName("const mi_block_t*")] mi_block_t* block)
    {
        if (MI_ENCODE_FREELIST != 0)
        {
            mi_block_t* next = mi_block_nextx(page, block, &page->keys.e0);

            // check for free list corruption: is `next` at least in the same page?
            // TODO: check if `next` is `page->block_size` aligned?

            if (mi_unlikely((next != null) && !mi_is_in_same_page(block, next)))
            {
                _mi_error_message(EFAULT, "corrupted free list entry of size {0}b at {1:X}: value 0x{2:X}\n", mi_page_block_size(page), (nuint)block, (nuint)next);
                next = null;
            }

            return next;
        }
        else
        {
            return mi_block_nextx(page, block, null);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void mi_block_set_next([NativeTypeName("const mi_page_t*")] mi_page_t* page, mi_block_t* block, [NativeTypeName("const mi_block_t*")] mi_block_t* next)
    {
        if (MI_ENCODE_FREELIST != 0)
        {
            mi_block_set_nextx(page, block, next, &page->keys.e0);
        }
        else
        {
            mi_block_set_nextx(page, block, next, null);
        }
    }

    // -------------------------------------------------------------------
    // Fast "random" shuffle
    // -------------------------------------------------------------------

    private static partial nuint _mi_random_shuffle(nuint x)
    {
        if (x == 0) // ensure we don't get stuck in generating zeros
        {
            x = 17;
        }

        if (MI_INTPTR_SIZE == 8)
        {
            // by Sebastiano Vigna, see: <http://xoshiro.di.unimi.it/splitmix64.c>

            x ^= x >> 30;
            x *= unchecked((nuint)0xBF58476D1CE4E5B9);
            x ^= x >> 27;
            x *= unchecked((nuint)0x94D049BB133111EB);
            x ^= x >> 31;
        }
        else
        {
            // by Chris Wellons, see: <https://nullprogram.com/blog/2018/07/31/>

            x ^= x >> 16;
            x *= 0x7FEB352D;
            x ^= x >> 15;
            x *= 0x846CA68B;
            x ^= x >> 16;
        }

        return x;
    }

    // -------------------------------------------------------------------
    // Optimize numa node access for the common case (= one node)
    // -------------------------------------------------------------------

    private static partial int _mi_os_numa_node_get(mi_os_tld_t* tld);

    [return: NativeTypeName("size_t")]
    private static partial nuint _mi_os_numa_node_count_get();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int _mi_os_numa_node(mi_os_tld_t* tld)
    {
        if (mi_likely(_mi_numa_node_count == 1))
        {
            return 0;
        }
        else
        {
            return _mi_os_numa_node_get(tld);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return: NativeTypeName("size_t")]
    private static nuint _mi_os_numa_node_count()
    {
        if (mi_likely(_mi_numa_node_count > 0))
        {
            return _mi_numa_node_count;
        }
        else
        {
            return _mi_os_numa_node_count_get();
        }
    }

    // -------------------------------------------------------------------
    // Getting the thread id should be performant as it is called in the
    // fast path of `_mi_free` and we specialize for various platforms.
    // -------------------------------------------------------------------

    // We use the .NET thread id rather than porting the native implementation
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint _mi_thread_id() => (nuint)Environment.CurrentManagedThreadId;
}
