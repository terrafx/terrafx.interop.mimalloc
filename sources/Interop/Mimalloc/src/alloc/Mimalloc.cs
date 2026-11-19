// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the alloc.c file from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

using System;
using System.Runtime.CompilerServices;
using static TerraFX.Interop.Mimalloc.mi_delayed_t;
using static TerraFX.Interop.Mimalloc.mi_option_t;
using static TerraFX.Interop.Mimalloc.mi_page_kind_t;

namespace TerraFX.Interop.Mimalloc;

public static unsafe partial class Mimalloc
{
    // ------------------------------------------------------
    // Allocation
    // ------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void _mi_memzero_aligned(void* p, nuint size)
    {
        mi_assert_internal((MI_DEBUG > 1) && (((nuint)p % MI_INTPTR_SIZE) == 0));
        _ = memset(p, 0, size);
    }

    // Fast allocation in a page: just pop from the free list.
    // Fall back to generic allocation only if the list is empty.
    private static partial void* _mi_page_malloc_zero(mi_heap_t* heap, mi_page_t* page, nuint size, bool zero)
    {
        mi_assert_internal((MI_DEBUG > 1) && ((page->block_size == 0) || (mi_page_block_size(page) >= size)));
        mi_block_t* block = page->free;

        if (mi_unlikely(block == null))
        {
            return _mi_malloc_generic(heap, size, zero, 0);
        }

        mi_assert_internal((MI_DEBUG > 1) && block != null && _mi_ptr_page(block) == page);

        // pop from the free list
        page->free = mi_block_next(page, block);
        page->used++;
        mi_assert_internal((MI_DEBUG > 1) && ((page->free == null) || (_mi_ptr_page(page->free) == page)));

        if (mi_unlikely(zero))
        {
            mi_assert_internal((MI_DEBUG > 1) && (page->block_size >= MI_PADDING_SIZE));

            if (page->free_is_zero)
            {
                block->next = 0;
            }
            else
            {
                nuint zsize = page->block_size - MI_PADDING_SIZE;
                _mi_memzero_aligned(block, zsize);
            }
        }

        if (MI_DEBUG > 0)
        {
            if (!zero)
            {
                _ = memset(block, MI_DEBUG_UNINIT, mi_page_usable_block_size(page));
            }
        }
        else if (MI_SECURE != 0)
        {
            // don't leak internal data
            if (!zero)
            {
                block->next = 0;
            }
        }

        if (MI_STAT > 0)
        {
            nuint bsize = mi_page_usable_block_size(page);

            if (bsize <= MI_LARGE_OBJ_SIZE_MAX)
            {
                mi_stat_increase(ref heap->tld->stats.normal, bsize);
                mi_stat_counter_increase(ref heap->tld->stats.normal_count, 1);

                if (MI_STAT > 1)
                {
                    nuint bin = _mi_bin(bsize);
                    mi_stat_increase(ref (&heap->tld->stats.normal_bins.e0)[bin], 1);
                    mi_stat_increase(ref heap->tld->stats.malloc_requested, size - MI_PADDING_SIZE);
                }
            }
        }

        if (MI_PADDING > 0)
        {
            mi_padding_t* padding = (mi_padding_t*)((byte*)block + mi_page_usable_block_size(page));
            nint delta = (nint)((nuint)padding - (nuint)block - (size - MI_PADDING_SIZE));

            mi_assert_internal((MI_DEBUG > 1) && (delta >= 0) && (mi_page_usable_block_size(page) >= (size - MI_PADDING_SIZE + (nuint)delta)));

            padding->canary = unchecked((uint)mi_ptr_encode(page, block, &page->keys.e0));
            padding->delta = (uint)delta;

            if (MI_PADDING_CHECK > 0)
            {
                byte* fill = (byte*)padding - delta;

                // set at most N initial padding bytes
                nuint maxpad = ((nuint)delta > MI_MAX_ALIGN_SIZE) ? MI_MAX_ALIGN_SIZE : (nuint)delta;

                for (nuint i = 0; i < maxpad; i++)
                {
                    fill[i] = MI_DEBUG_PADDING;
                }
            }
        }

        return block;
    }

    private static void* mi_block_ptr_set_guarded(mi_block_t* block, nuint obj_size)
    {
        mi_page_t* page = _mi_ptr_page(block);
        mi_page_set_has_aligned(page, true);
        block->next = nuint.MaxValue;

        mi_segment_t* segment = _mi_page_segment(page);
        nuint block_size = mi_page_block_size(page);
        nuint os_page_size = _mi_os_page_size();
        if (block_size < (obj_size + os_page_size + (nuint)SizeOf<mi_block_t>()))
        {
            mi_free(block);
            return null;
        }

        byte* guard_page = (byte*)block + block_size - os_page_size;
        if (segment->allow_decommit && (((nuint)guard_page % os_page_size) == 0))
        {
            if (!_mi_os_protect(guard_page, os_page_size))
            {
                _mi_warning_message("failed to set a guard page behind an object (object {0:X} of size {1})\n", (nuint)block, block_size);
            }
        }
        else
        {
            _mi_warning_message("unable to set a guard page behind an object due to pinned memory (object {0:X} of size {1})\n", (nuint)block, block_size);
        }

        nuint offset = block_size - os_page_size - obj_size;
        mi_assert_internal((MI_DEBUG > 1) && (offset > (nuint)SizeOf<mi_block_t>()));
        if (offset > MI_BLOCK_ALIGNMENT_MAX)
        {
            offset = MI_BLOCK_ALIGNMENT_MAX;
        }

        return (byte*)block + offset;
    }

    private static void* mi_heap_malloc_guarded(mi_heap_t* heap, nuint size, bool zero)
    {
        if (size >= (MI_MAX_ALLOC_SIZE - MI_PADDING_SIZE))
        {
            _mi_error_message(EOVERFLOW, "(guarded) allocation request is too large ({0} bytes)\n", size);
            return null;
        }

        nuint os_page_size = _mi_os_page_size();
        nuint obj_size = mi_option_is_enabled(mi_option_guarded_precise)
            ? size
            : _mi_align_up(size, MI_MAX_ALIGN_SIZE);

        nuint header_size = (nuint)SizeOf<mi_block_t>();
        nuint max_size = SIZE_MAX - os_page_size;
        if ((obj_size > (max_size - header_size)) || (obj_size + header_size > (SIZE_MAX - os_page_size - (os_page_size - 1))))
        {
            _mi_error_message(EOVERFLOW, "(guarded) allocation request is too large ({0} bytes)\n", size);
            return null;
        }

        nuint block_size = _mi_align_up(_mi_align_up(obj_size, MI_MAX_ALIGN_SIZE) + header_size, MI_MAX_ALIGN_SIZE);
        nuint req_size = _mi_align_up(block_size + os_page_size, os_page_size);
        mi_block_t* block = (mi_block_t*)_mi_malloc_generic(heap, req_size, false, 0);
        if (block == null)
        {
            return null;
        }

        void* p = mi_block_ptr_set_guarded(block, obj_size);
        if (p == null)
        {
            return null;
        }

        if (zero)
        {
            _ = memset(p, 0, obj_size);
        }

        nuint usable = mi_usable_size(p);
        if (MI_STAT > 1)
        {
            mi_stat_adjust_decrease(ref heap->tld->stats.malloc_requested, req_size - MI_PADDING_SIZE);
            mi_stat_increase(ref heap->tld->stats.malloc_requested, size);
            mi_stat_increase(ref heap->tld->stats.malloc, usable);
        }

        mi_stat_counter_increase(ref heap->tld->stats.malloc_guarded_count, 1);
        return p;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static partial void* _mi_page_malloc(mi_heap_t* heap, mi_page_t* page, nuint size)
        => _mi_page_malloc_zero(heap, page, size, false);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static partial void* _mi_page_malloc_zeroed(mi_heap_t* heap, mi_page_t* page, nuint size)
        => _mi_page_malloc_zero(heap, page, size, true);

    // allocate a small block
    private static void* mi_heap_malloc_small_zero(mi_heap_t* heap, [NativeTypeName("size_t")] nuint size, bool zero)
    {
        mi_assert((MI_DEBUG != 0) && (heap != null));

        // heaps are thread local
        mi_assert((MI_DEBUG != 0) && ((heap->thread_id == 0) || (heap->thread_id == _mi_thread_id())));

        mi_assert((MI_DEBUG != 0) && (size <= MI_SMALL_SIZE_MAX));

        if (((MI_PADDING != 0) || (heap->guarded_sample_rate != 0)) && (size == 0))
        {
            size = SizeOf<nuint>();
        }

        if (mi_heap_malloc_use_guarded(heap, size))
        {
            return mi_heap_malloc_guarded(heap, size, zero);
        }

        mi_page_t* page = _mi_heap_get_free_small_page(heap, size + MI_PADDING_SIZE);

        void* p = _mi_page_malloc_zero(heap, page, size + MI_PADDING_SIZE, zero);
        mi_assert_internal((MI_DEBUG > 1) && ((p == null) || (mi_usable_size(p) >= size)));

        if ((MI_STAT > 1) && (p != null))
        {
            mi_stat_increase(ref heap->tld->stats.malloc, mi_usable_size(p));
        }
        return p;
    }

    private static void* mi_heap_malloc_small(mi_heap_t* heap, nuint size) => mi_heap_malloc_small_zero(heap, size, false);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void* mi_heap_malloc_small(IntPtr heap, nuint size) => mi_heap_malloc_small((mi_heap_t*)heap, size);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void* mi_theap_malloc_small(IntPtr heap, nuint size) => mi_heap_malloc_small(heap, size);

    private static void* mi_heap_zalloc_small(mi_heap_t* heap, nuint size) => mi_heap_malloc_small_zero(heap, size, true);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void* mi_heap_zalloc_small(IntPtr heap, nuint size) => mi_heap_zalloc_small((mi_heap_t*)heap, size);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void* mi_malloc_small(nuint size) => mi_heap_malloc_small(mi_get_default_heap(), size);

    // The main allocation function
    private static void* _mi_heap_malloc_zero_ex(mi_heap_t* heap, nuint size, bool zero, nuint huge_alignment)
    {
        if (mi_likely(size <= MI_SMALL_SIZE_MAX))
        {
            mi_assert_internal((MI_DEBUG > 1) && (huge_alignment == 0));
            return mi_heap_malloc_small_zero(heap, size, zero);
        }
        else
        {
            mi_assert((MI_DEBUG != 0) && (heap != null));

            // heaps are thread local
            mi_assert((MI_DEBUG != 0) && ((heap->thread_id == 0) || (heap->thread_id == _mi_thread_id())));

            if ((huge_alignment == 0) && mi_heap_malloc_use_guarded(heap, size))
            {
                return mi_heap_malloc_guarded(heap, size, zero);
            }

            // note: size can overflow but it is detected in malloc_generic

            void* p = _mi_malloc_generic(heap, size + MI_PADDING_SIZE, zero, huge_alignment);
            mi_assert_internal((MI_DEBUG > 1) && (p == null || mi_usable_size(p) >= size));

            if ((MI_STAT > 1) && (p != null))
            {
                mi_stat_increase(ref heap->tld->stats.malloc, mi_usable_size(p));
            }
            return p;
        }
    }

    private static void* mi_heap_malloc_zero(mi_heap_t* heap, nuint size, bool zero) => _mi_heap_malloc_zero_ex(heap, size, zero, 0);

    private static void* mi_heap_malloc(mi_heap_t* heap, nuint size) => mi_heap_malloc_zero(heap, size, false);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void* mi_heap_malloc(IntPtr heap, nuint size) => mi_heap_malloc((mi_heap_t*)heap, size);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void* mi_theap_malloc(IntPtr heap, nuint size) => mi_heap_malloc(heap, size);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void* mi_malloc(nuint size) => mi_heap_malloc(mi_get_default_heap(), size);

    // zero initialized small block
    public static partial void* mi_zalloc_small(nuint size) => mi_heap_malloc_small_zero(mi_get_default_heap(), size, true);

    private static partial void* _mi_heap_malloc_zero(mi_heap_t* heap, nuint size, bool zero) => mi_heap_malloc_zero(heap, size, zero);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void* mi_heap_zalloc(mi_heap_t* heap, [NativeTypeName("size_t")] nuint size) => _mi_heap_malloc_zero(heap, size, true);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void* mi_heap_zalloc(IntPtr heap, nuint size) => mi_heap_zalloc((mi_heap_t*)heap, size);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void* mi_theap_zalloc(IntPtr heap, nuint size) => mi_heap_zalloc(heap, size);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void* mi_theap_calloc(IntPtr heap, nuint count, nuint size) => mi_heap_calloc(heap, count, size);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void* mi_zalloc(nuint size) => mi_heap_zalloc(mi_get_default_heap(), size);

    // ------------------------------------------------------
    // Check for double free in secure and debug mode
    // This is somewhat expensive so only enabled for secure mode 4
    // ------------------------------------------------------

    // linear check if the free list contains a specific element
    private static bool mi_list_contains([NativeTypeName("const mi_page_t*")] mi_page_t* page, [NativeTypeName("const mi_block_t*")] mi_block_t* list, [NativeTypeName("const mi_block_t*")] mi_block_t* elem, string list_kind)
    {
        mi_assert_internal((MI_DEBUG > 1) && (MI_ENCODE_FREELIST != 0) && ((MI_SECURE >= 4) || (MI_DEBUG != 0)));

        nuint max_count = page->capacity;
        nuint count = 0;
        while ((list != null) && (count <= max_count))
        {
            if (elem == list)
            {
                return true;
            }
            list = mi_block_next(page, list);
            count++;
        }

        if (count > max_count)
        {
            _mi_error_message(EFAULT, "corrupted {0} list (possibly due to a double free)\n", list_kind);
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool mi_check_is_double_freex([NativeTypeName("const mi_page_t*")] mi_page_t* page, [NativeTypeName("const mi_block_t*")] mi_block_t* block)
    {
        mi_assert_internal((MI_DEBUG > 1) && (MI_ENCODE_FREELIST != 0) && ((MI_SECURE >= 4) || (MI_DEBUG != 0)));

        // The decoded value is in the same page (or null).
        // Walk the free lists to verify positively if it is already freed

        if (mi_list_contains(page, page->free, block, "free") || mi_list_contains(page, page->local_free, block, "local free") || mi_list_contains(page, mi_page_thread_free(page), block, "thread free"))
        {
            _mi_error_message(EAGAIN, "double free detected of block {0:X} with size {1}\n", (nuint)block, mi_page_block_size(page));
            return true;
        }

        return false;
    }

    private static bool mi_check_is_double_free([NativeTypeName("const mi_page_t*")] mi_page_t* page, [NativeTypeName("const mi_block_t*")] mi_block_t* block)
    {
        if ((MI_ENCODE_FREELIST != 0) && ((MI_SECURE >= 4) || (MI_DEBUG != 0)))
        {
            // pretend it is freed, and get the decoded first field
            mi_block_t* n = mi_block_nextx(page, block, &page->keys.e0);

            // quick check: aligned pointer && in same page or null
            if ((((nuint)n & (MI_INTPTR_SIZE - 1)) == 0) && ((n == null) || mi_is_in_same_page(block, n)))
            {
                // Suspicous: decoded value a in block is in the same page (or null) -- maybe a double free?
                // (continue in separate function to improve code generation)
                return mi_check_is_double_freex(page, block);
            }
        }

        return false;
    }

    // ---------------------------------------------------------------------------
    // Check for heap block overflow by setting up padding at the end of the block
    // ---------------------------------------------------------------------------

    private static bool mi_page_decode_padding([NativeTypeName("const mi_page_t*")] mi_page_t* page, [NativeTypeName("const mi_block_t*")] mi_block_t* block, [NativeTypeName("size_t*")] out nuint delta, [NativeTypeName("size_t*")] out nuint bsize)
    {
        mi_assert_internal((MI_DEBUG > 1) && (MI_PADDING > 0));

        bsize = mi_page_usable_block_size(page);
        mi_padding_t* padding = (mi_padding_t*)((byte*)block + bsize);

        delta = padding->delta;
        return (unchecked((uint)mi_ptr_encode(page, block, &page->keys.e0)) == padding->canary) && (delta <= bsize);
    }

    // Return the exact usable size of a block.
    [return: NativeTypeName("size_t")]
    private static nuint mi_page_usable_size_of([NativeTypeName("const mi_page_t*")] mi_page_t* page, [NativeTypeName("const mi_block_t*")] mi_block_t* block, bool is_guarded = false)
    {
        if (is_guarded || mi_block_is_guarded((mi_block_t*)block))
        {
            nuint block_size = mi_page_block_size(page);
            nuint page_size = _mi_os_page_size();
            return (block_size >= page_size) ? (block_size - page_size) : 0;
        }

        if (MI_PADDING > 0)
        {
            bool ok = mi_page_decode_padding(page, block, out nuint delta, out nuint bsize);

            mi_assert_internal((MI_DEBUG > 1) && ok);
            mi_assert_internal((MI_DEBUG > 1) && (delta <= bsize));

            return ok ? (bsize - delta) : 0;
        }
        else
        {
            return mi_page_usable_block_size(page);
        }
    }

    private static bool mi_verify_padding([NativeTypeName("const mi_page_t*")] mi_page_t* page, [NativeTypeName("const mi_block_t*")] mi_block_t* block, [NativeTypeName("size_t*")] out nuint size, [NativeTypeName("size_t*")] out nuint wrong)
    {
        mi_assert_internal((MI_DEBUG > 1) && (MI_PADDING > 0) && (MI_PADDING_CHECK > 0));

        bool ok = mi_page_decode_padding(page, block, out nuint delta, out nuint bsize);
        size = wrong = bsize;

        if (!ok)
        {
            return false;
        }

        mi_assert_internal((MI_DEBUG > 1) && (bsize >= delta));

        size = bsize - delta;
        byte* fill = (byte*)block + bsize - delta;

        // check at most the first N padding bytes
        if (MI_PADDING_CHECK > 0)
        {
            nuint maxpad = (delta > MI_MAX_ALIGN_SIZE) ? MI_MAX_ALIGN_SIZE : delta;

            for (nuint i = 0; i < maxpad; i++)
            {
                if (fill[i] != MI_DEBUG_PADDING)
                {
                    wrong = bsize - delta + i;
                    return false;
                }
            }
        }

        return true;
    }

    private static void mi_check_padding([NativeTypeName("const mi_page_t*")] mi_page_t* page, [NativeTypeName("const mi_block_t*")] mi_block_t* block)
    {
        if ((MI_PADDING > 0) && (MI_PADDING_CHECK > 0) && !mi_verify_padding(page, block, out nuint size, out nuint wrong))
        {
            _mi_error_message(EFAULT, "buffer overflow in heap block {0:X} of size {1}: write after {2} bytes\n", (nuint)block, size, wrong);
        }
    }

    // When a non-thread-local block is freed, it becomes part of the thread delayed free
    // list that is freed later by the owning heap. If the exact usable size is too small to
    // contain the pointer for the delayed list, then shrink the padding (by decreasing delta)
    // so it will later not trigger an overflow error in `mi_free_block`.
    private static void mi_padding_shrink([NativeTypeName("const mi_page_t*")] mi_page_t* page, [NativeTypeName("const mi_block_t*")] mi_block_t* block, [NativeTypeName("const size_t")] nuint min_size)
    {
        if (MI_PADDING > 0)
        {
            bool ok = mi_page_decode_padding(page, block, out nuint delta, out nuint bsize);
            mi_assert_internal((MI_DEBUG > 1) && ok);

            if (!ok || ((bsize - delta) >= min_size))
            {
                // usually already enough space
                return;
            }

            mi_assert_internal((MI_DEBUG > 1) && (bsize >= min_size));

            if (bsize < min_size)
            {
                // should never happen
                return;
            }

            nuint new_delta = bsize - min_size;
            mi_assert_internal((MI_DEBUG > 1) && (new_delta < bsize));

            mi_padding_t* padding = (mi_padding_t*)((byte*)block + bsize);
            padding->delta = (uint)new_delta;
        }
    }

    // ------------------------------------------------------
    // Free
    // ------------------------------------------------------

    // multi-threaded free
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool mi_block_check_unguard(mi_page_t* page, mi_block_t* block, void* p)
    {
        if (!mi_block_ptr_is_guarded(block, p))
        {
            return false;
        }

        nuint block_size = mi_page_block_size(page);
        nuint page_size = _mi_os_page_size();
        void* guard_page = (byte*)block + block_size - page_size;
        _ = _mi_os_unprotect(guard_page, page_size);
        return true;
    }

    private static void _mi_free_block_mt(mi_segment_t* segment, mi_page_t* page, mi_block_t* block, bool was_guarded)
    {
#pragma warning disable CS0420
        // Do not create a new default heap when freeing after this thread has been torn down.
        mi_heap_t* reclaim_heap = _mi_heap_default;
        if ((reclaim_heap != null) && (reclaim_heap->tld->segments.subproc == segment->subproc) &&
            mi_option_is_enabled(mi_option_abandoned_reclaim_on_free) && (mi_atomic_load_relaxed(ref segment->thread_id) == 0))
        {
            if (_mi_segment_attempt_reclaim(reclaim_heap, segment))
            {
                mi_assert_internal((MI_DEBUG > 1) && (_mi_thread_id() == mi_atomic_load_relaxed(ref segment->thread_id)));
                mi_free(block);
                return;
            }
        }

        // The padding check may access the non-thread-owned page for the key values.
        // that is safe as these are constant and the page won't be freed (as the block is not freed yet).
        if (!was_guarded)
        {
            mi_check_padding(page, block);
        }
        mi_stat_free(page, block, was_guarded);

        // for small size, ensure we can fit the delayed thread pointers without triggering overflow detection
        if (!was_guarded)
        {
            mi_padding_shrink(page, block, SizeOf<mi_block_t>());
        }

        if (segment->page_kind == MI_PAGE_HUGE)
        {
            _mi_segment_huge_page_reset(segment, page, block);
        }
        else if (MI_DEBUG != 0)
        {
            nuint debug_size = mi_page_usable_size_of(page, block, was_guarded);
            if (debug_size > MiB)
            {
                debug_size = MiB;
            }

            _ = memset(block, MI_DEBUG_FREED, debug_size);
        }

        // Try to put the block on either the page-local thread free list, or the heap delayed free list.
        nuint tfreex;

        bool use_delayed;
        nuint tfree = mi_atomic_load_relaxed(ref page->xthread_free);

        do
        {
            use_delayed = mi_tf_delayed(tfree) == MI_USE_DELAYED_FREE;

            if (mi_unlikely(use_delayed))
            {
                // unlikely: this only happens on the first concurrent free in a page that is in the full list
                tfreex = mi_tf_set_delayed(tfree, MI_DELAYED_FREEING);
            }
            else
            {
                // usual: directly add to page thread_free list
                mi_block_set_next(page, block, mi_tf_block(tfree));
                tfreex = mi_tf_set_block(tfree, block);
            }
        }
        while (!mi_atomic_cas_weak_release(ref page->xthread_free, ref tfree, tfreex));

        if (mi_unlikely(use_delayed))
        {
            // racy read on `heap`, but ok because MI_DELAYED_FREEING is set (see `mi_heap_delete` and `mi_heap_collect_abandon`)
            mi_heap_t* heap = (mi_heap_t*)mi_atomic_load_acquire(ref page->xheap);

            mi_assert_internal((MI_DEBUG > 1) && (heap != null));

            if (heap != null)
            {
                // add to the delayed free list of this heap. (do this atomically as the lock only protects heap memory validity)
                nuint dfree = (nuint)mi_atomic_load_ptr_relaxed<mi_block_t>(ref heap->thread_delayed_free);

                do
                {
                    mi_block_set_nextx(heap, block, (mi_block_t*)dfree, &heap->keys.e0);
                }
                while (!mi_atomic_cas_ptr_weak_release(ref heap->thread_delayed_free, ref dfree, block));
            }

            // and reset the MI_DELAYED_FREEING flag
            tfree = mi_atomic_load_relaxed(ref page->xthread_free);

            do
            {
                tfreex = tfree;
                mi_assert_internal((MI_DEBUG > 1) && (mi_tf_delayed(tfree) == MI_DELAYED_FREEING));
                tfreex = mi_tf_set_delayed(tfree, MI_NO_DELAYED_FREE);
            }
            while (!mi_atomic_cas_weak_release(ref page->xthread_free, ref tfree, tfreex));
        }
#pragma warning restore CS0420
    }

    // regular free
    private static void _mi_free_block_local(mi_page_t* page, mi_block_t* block, bool was_guarded, bool update_stats)
    {
        was_guarded |= mi_block_is_guarded(block);

        // owning thread can free a block directly
        if (mi_unlikely(mi_check_is_double_free(page, block)))
        {
            return;
        }

        if (!was_guarded)
        {
            mi_check_padding(page, block);
        }

        if (update_stats)
        {
            mi_stat_free(page, block, was_guarded);
        }

        if ((MI_DEBUG != 0) && !mi_page_is_huge(page))
        {
            nuint debug_size = mi_page_usable_size_of(page, block, was_guarded);
            if (debug_size > MiB)
            {
                debug_size = MiB;
            }

            _ = memset(block, MI_DEBUG_FREED, debug_size);
        }

        mi_block_set_next(page, block, page->local_free);
        page->local_free = block;

        if (mi_unlikely(--page->used == 0))
        {
            _mi_page_retire(page);
        }
        else if (mi_unlikely(mi_page_is_in_full(page)))
        {
            _mi_page_unfull(page);
        }
    }

    private static void mi_stat_free(mi_page_t* page, mi_block_t* block, bool was_guarded = false)
    {
        was_guarded |= mi_block_is_guarded(block);

        if (MI_STAT > 0)
        {
            mi_heap_t* heap = (mi_heap_t*)mi_heap_get_default();
            nuint bsize = mi_page_usable_block_size(page);

            if (bsize <= MI_LARGE_OBJ_SIZE_MAX)
            {
                mi_stat_decrease(ref heap->tld->stats.normal, bsize);

                if (MI_STAT > 1)
                {
                    nuint usize = mi_page_usable_size_of(page, block, was_guarded);
                    if (!was_guarded)
                    {
                        mi_stat_decrease(ref heap->tld->stats.malloc, usize);
                    }
                    mi_stat_decrease(ref (&heap->tld->stats.normal_bins.e0)[_mi_bin(bsize)], 1);
                }
            }
            else
            {
                if (mi_page_is_huge(page))
                {
                    nuint huge_size = mi_page_block_size(page);
                    mi_stat_decrease(ref heap->tld->stats.huge, huge_size);
                }

                if (MI_STAT > 1)
                {
                    if (!was_guarded)
                    {
                        mi_stat_decrease(ref heap->tld->stats.malloc, mi_page_usable_size_of(page, block, was_guarded));
                    }
                }
            }
        }
    }

    // Adjust a block that was allocated aligned, to the actual start of the block in the page.
    // This can run on a non-owning thread; page_start and block_size remain stable while the block is live.
    private static partial mi_block_t* _mi_page_ptr_unalign(mi_page_t* page, void* p)
    {
        mi_assert_internal((MI_DEBUG > 1) && (page != null) && (p != null));

        nuint diff = (nuint)p - (nuint)page->page_start;
        nuint adjust = (page->block_size_shift != 0)
            ? (diff & (((nuint)1 << page->block_size_shift) - 1))
            : (diff % mi_page_block_size(page));

        return (mi_block_t*)((nuint)p - adjust);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static mi_block_t* mi_cast_ptr_to_block(mi_page_t* page, void* p)
    {
        if (MI_SECURE >= 3)
        {
            return _mi_page_ptr_unalign(page, p);
        }

        mi_assert_internal((MI_DEBUG > 1) && (_mi_page_ptr_unalign(page, p) == (mi_block_t*)p));
        return (mi_block_t*)p;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void mi_free_generic([NativeTypeName("const mi_segment_t*")] mi_segment_t* segment, bool local, void* p)
    {
        mi_page_t* page = _mi_segment_page_of(segment, p);

        if (local)
        {
            mi_block_t* block = mi_page_has_aligned(page) ? _mi_page_ptr_unalign(page, p) : mi_cast_ptr_to_block(page, p);
            bool was_guarded = mi_block_check_unguard(page, block, p) || mi_block_is_guarded(block);
            _mi_free_block_local(page, block, was_guarded, update_stats: true);
        }
        else
        {
            mi_block_t* block = _mi_page_ptr_unalign(page, p);
            bool was_guarded = mi_block_check_unguard(page, block, p) || mi_block_is_guarded(block);
            _mi_free_block_mt(segment, page, block, was_guarded);
        }
    }

    // Get the segment data belonging to a pointer
    // This is just a single `and` in assembly but does further checks in debug mode
    // (and secure mode) if this was a valid pointer.
    private static mi_segment_t* mi_checked_ptr_segment([NativeTypeName("const void*")] void* p, [NativeTypeName("const char*")] string msg)
    {
        if (p == null)
        {
            return null;
        }

        mi_segment_t* segment = _mi_ptr_segment(p);

        if (mi_unlikely(segment == null))
        {
            // checks also for (p==null)
            return null;
        }

        if ((MI_DEBUG > 0) && mi_unlikely(((nuint)p & (MI_INTPTR_SIZE - 1)) != 0))
        {
            if (mi_unlikely(_mi_ptr_cookie(segment) != segment->cookie))
            {
                _mi_error_message(EINVAL, "pointer does not point to a valid heap space: {0:X}\n", (nuint)p);
                return null;
            }

            mi_page_t* page = _mi_segment_page_of(segment, p);
            if ((page == null) || !mi_page_has_aligned(page))
            {
                _mi_error_message(EINVAL, "{0}: invalid (unaligned) pointer: {1:X}\n", msg, (nuint)p);
                return null;
            }
        }

        if ((MI_DEBUG > 0) && mi_unlikely(!mi_is_in_heap_region(p)))
        {
            _mi_warning_message("{0}: pointer might not point to a valid heap region: {1:X}\n(this may still be a valid very large allocation (over 64MiB))\n", msg, (nuint)p);

            if (mi_likely(_mi_ptr_cookie(segment) == segment->cookie))
            {
                _mi_warning_message("(yes, the previous pointer {0:X} was valid after all)\n", (nuint)p);
            }
        }

        if (((MI_DEBUG > 0) || (MI_SECURE >= 4)) && mi_unlikely(_mi_ptr_cookie(segment) != segment->cookie))
        {
            _mi_error_message(EINVAL, "pointer does not point to a valid heap space: {0:X}\n", (nuint)p);
            return null;
        }

        return segment;
    }

    // Free a block
    public static partial void mi_free(void* p)
    {
        mi_segment_t* segment = mi_checked_ptr_segment(p, "mi_free");

        if (mi_unlikely(segment == null))
        {
            return;
        }

        nuint tid = _mi_thread_id();
        mi_page_t* page = _mi_segment_page_of(segment, p);

        if (mi_likely((tid == segment->thread_id) && (page->flags.full_aligned == 0)))
        {
            // the thread id matches and it is not a full page, nor has aligned blocks
            // local, and not full or aligned
            mi_block_t* block = mi_cast_ptr_to_block(page, p);

            if (mi_unlikely(mi_check_is_double_free(page, block)))
            {
                return;
            }

            mi_check_padding(page, block);
            mi_stat_free(page, block);

            if (MI_DEBUG != 0)
            {
                nuint debug_size = mi_usable_size(block);
                if (debug_size > MiB)
                {
                    debug_size = MiB;
                }

                _ = memset(block, MI_DEBUG_FREED, debug_size);
            }

            mi_block_set_next(page, block, page->local_free);
            page->local_free = block;

            if (mi_unlikely(--page->used == 0))
            {
                // using this expression generates better code than: page->used--; if (mi_page_all_free(page))    
                _mi_page_retire(page);
            }
        }

        else
        {
            // non-local, aligned blocks, or a full page; use the more generic path
            // note: recalc page in generic to improve code generation
            mi_free_generic(segment, tid == segment->thread_id, p);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void mi_free_small(void* p) => mi_free(p);

    private static partial bool _mi_free_delayed_block(mi_block_t* block)
    {
        // get segment and page
        mi_segment_t* segment = _mi_ptr_segment(block);

        mi_assert_internal((MI_DEBUG > 1) && (_mi_ptr_cookie(segment) == segment->cookie));
        mi_assert_internal((MI_DEBUG > 1) && (_mi_thread_id() == segment->thread_id));

        mi_page_t* page = _mi_segment_page_of(segment, block);

        // Clear the no-delayed flag so delayed freeing is used again for this page.
        // This must be done before collecting the free lists on this page -- otherwise
        // some blocks may end up in the page `thread_free` list with no blocks in the
        // heap `thread_delayed_free` list which may cause the page to be never freed!
        // (it would only be freed if we happen to scan it in `mi_page_queue_find_free_ex`)
        if (!_mi_page_try_use_delayed_free(page, MI_USE_DELAYED_FREE, override_never: false))
        {
            return false;
        }

        // collect all other non-local frees to ensure up-to-date `used` count
        _mi_page_free_collect(page, false);

        // and free the block (possibly freeing the page as well since used is updated)
        _mi_free_block_local(page, block, was_guarded: false, update_stats: false);

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static nuint mi_page_usable_aligned_size_of(mi_page_t* page, void* p)
    {
        mi_block_t* block = _mi_page_ptr_unalign(page, p);
        nuint size = mi_page_usable_size_of(page, block, mi_block_ptr_is_guarded(block, p));
        mi_assert_internal((MI_DEBUG > 1) && ((nuint)p >= (nuint)block));
        nuint adjust = unchecked((nuint)p - (nuint)block);

        mi_assert_internal((MI_DEBUG > 1) && (adjust <= size));
        return (adjust <= size) ? (size - adjust) : 0;
    }

    // Bytes available in a block
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return: NativeTypeName("size_t")]
    private static nuint _mi_usable_size([NativeTypeName("const void*")] void* p, [NativeTypeName("const char*")] string msg)
    {
        mi_segment_t* segment = mi_checked_ptr_segment(p, msg);

        if (segment == null)
        {
            return 0;
        }

        mi_page_t* page = _mi_segment_page_of(segment, p);

        if (mi_likely(!mi_page_has_aligned(page)))
        {
            return mi_page_usable_size_of(page, mi_cast_ptr_to_block(page, p));
        }
        else
        {
            return mi_page_usable_aligned_size_of(page, p);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial nuint mi_usable_size(void* p) => _mi_usable_size(p, "mi_usable_size");

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint mi_ublock_size_of(void* p, string msg)
    {
        if (p == null)
        {
            return 0;
        }

        mi_segment_t* segment = mi_checked_ptr_segment(p, msg);
        return (segment == null) ? 0 : mi_page_block_size(_mi_segment_page_of(segment, p));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void* mi_ublock_size(void* p, nuint* block_size)
    {
        if ((p != null) && (block_size != null))
        {
            *block_size = mi_ublock_size_of(p, "mi_umalloc");
        }

        return p;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void* mi_umalloc(nuint size, nuint* block_size) => mi_ublock_size(mi_malloc(size), block_size);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void* mi_ucalloc(nuint count, nuint size, nuint* block_size)
        => mi_ublock_size(mi_calloc(count, size), block_size);

    public static partial void* mi_urealloc(void* p, nuint newsize, nuint* block_size_pre, nuint* block_size_post)
    {
        if (block_size_pre != null)
        {
            *block_size_pre = mi_ublock_size_of(p, "mi_urealloc");
        }

        if (block_size_post != null)
        {
            *block_size_post = 0;
        }

        void* resized = mi_realloc(p, newsize);
        if ((resized != null) && (block_size_post != null))
        {
            *block_size_post = mi_ublock_size_of(resized, "mi_urealloc");
        }

        return resized;
    }

    public static partial void mi_ufree(void* p, nuint* block_size)
    {
        if (block_size != null)
        {
            *block_size = mi_ublock_size_of(p, "mi_ufree");
        }

        mi_free(p);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void* mi_umalloc_aligned(nuint size, nuint alignment, nuint* block_size)
        => mi_ublock_size(mi_malloc_aligned(size, alignment), block_size);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void* mi_uzalloc_aligned(nuint size, nuint alignment, nuint* block_size)
        => mi_ublock_size(mi_zalloc_aligned(size, alignment), block_size);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void* mi_umalloc_small(nuint size, nuint* block_size)
        => mi_ublock_size(mi_malloc_small(size), block_size);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void* mi_uzalloc_small(nuint size, nuint* block_size)
        => mi_ublock_size(mi_zalloc_small(size), block_size);

    // ------------------------------------------------------
    // Allocation extensions
    // ------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void mi_free_size(void* p, nuint size)
    {
        if (MI_DEBUG != 0)
        {
            nuint available = _mi_usable_size(p, "mi_free_size");
            mi_assert((MI_DEBUG != 0) && ((p == null) || (size <= available) || (available == 0)));
        }

        mi_free(p);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void mi_free_size_aligned(void* p, nuint size, nuint alignment)
    {
        mi_assert((MI_DEBUG != 0) && (((nuint)p % alignment) == 0));
        mi_free_size(p, size);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void mi_free_aligned(void* p, nuint alignment)
    {
        mi_assert((MI_DEBUG != 0) && (((nuint)p % alignment) == 0));
        mi_free(p);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void* mi_heap_calloc(mi_heap_t* heap, [NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint size)
    {
        if (mi_count_size_overflow(count, size, out nuint total))
        {
            return null;
        }
        return mi_heap_zalloc(heap, total);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void* mi_heap_calloc(IntPtr heap, nuint count, nuint size) => mi_heap_calloc((mi_heap_t*)heap, count, size);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void* mi_calloc(nuint count, nuint size) => mi_heap_calloc(mi_get_default_heap(), count, size);

    // Uninitialized `calloc`
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void* mi_heap_mallocn(mi_heap_t* heap, [NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint size)
    {
        if (mi_count_size_overflow(count, size, out nuint total))
        {
            return null;
        }
        return mi_heap_malloc(heap, total);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void* mi_heap_mallocn_aligned(mi_heap_t* heap, [NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment)
    {
        if (mi_count_size_overflow(count, size, out nuint total))
        {
            return null;
        }
        return mi_heap_malloc_aligned(heap, total, alignment);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void* mi_heap_mallocn_aligned_at(mi_heap_t* heap, [NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment, [NativeTypeName("size_t")] nuint offset)
    {
        if (mi_count_size_overflow(count, size, out nuint total))
        {
            return null;
        }
        return mi_heap_malloc_aligned_at(heap, total, alignment, offset);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void* mi_heap_mallocn(IntPtr heap, nuint count, nuint size) => mi_heap_mallocn((mi_heap_t*)heap, count, size);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void* mi_heap_mallocn_aligned(IntPtr heap, nuint count, nuint size, nuint alignment) => mi_heap_mallocn_aligned((mi_heap_t*)heap, count, size, alignment);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void* mi_heap_mallocn_aligned_at(IntPtr heap, nuint count, nuint size, nuint alignment, nuint offset) => mi_heap_mallocn_aligned_at((mi_heap_t*)heap, count, size, alignment, offset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void* mi_mallocn(nuint count, nuint size) => mi_heap_mallocn(mi_get_default_heap(), count, size);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void* mi_mallocn_aligned(nuint count, nuint size, nuint alignment) => mi_heap_mallocn_aligned(mi_get_default_heap(), count, size, alignment);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void* mi_mallocn_aligned_at(nuint count, nuint size, nuint alignment, nuint offset) => mi_heap_mallocn_aligned_at(mi_get_default_heap(), count, size, alignment, offset);

    // Expand (or shrink) in place or fail
    public static partial void* mi_expand(void* p, nuint newsize)
    {
        if (MI_PADDING != 0)
        {
            return null;
        }

        if (p == null)
        {
            return null;
        }

        nuint size = _mi_usable_size(p, "mi_expand");

        if (newsize > size)
        {
            return null;
        }

        // it fits
        return p;
    }

    private static partial void* _mi_heap_realloc_zero(mi_heap_t* heap, void* p, nuint newsize, bool zero)
    {
        if ((p != null) && (mi_checked_ptr_segment(p, "mi_realloc") == null))
        {
            return null;
        }

        nuint size = _mi_usable_size(p, "mi_realloc");

        if (mi_unlikely((newsize <= size) && (newsize >= (size / 2)) && (newsize > 0)))
        {
            // reallocation still fits and not more than 50% waste
            return p;
        }

        void* newp = mi_heap_malloc(heap, newsize);

        if (mi_likely(newp != null))
        {
            nuint copy_size = (newsize > size) ? size : newsize;
            nuint zero_start = 0;
            nuint usable = 0;
            if (zero)
            {
                zero_start = (copy_size >= SizeOf<nuint>()) ? copy_size - SizeOf<nuint>() : 0;
                zero_start = _mi_align_down(zero_start, SizeOf<nuint>());
                usable = mi_usable_size(newp);
                mi_assert_internal((MI_DEBUG > 1) && (usable >= newsize));
            }

            if (zero && (usable > zero_start))
            {
                // Include the word containing the old tail so any padding bytes remain initialized.
                _ = memset((byte*)newp + zero_start, 0, usable - zero_start);
            }
            else if (newsize == 0)
            {
                ((byte*)newp)[0] = 0;
            }

            if (mi_likely(p != null))
            {
                _ = memcpy(newp, p, copy_size);

                // only free the original pointer if successful
                mi_free(p);
            }
        }

        return newp;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void* mi_heap_realloc(mi_heap_t* heap, void* p, [NativeTypeName("size_t")] nuint newsize) => _mi_heap_realloc_zero(heap, p, newsize, false);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void* mi_heap_realloc(IntPtr heap, void* p, nuint newsize) => mi_heap_realloc((mi_heap_t*)heap, p, newsize);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void* mi_theap_realloc(IntPtr heap, void* p, nuint newsize) => mi_heap_realloc(heap, p, newsize);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void* mi_heap_reallocn(mi_heap_t* heap, void* p, [NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint size)
    {
        if (mi_count_size_overflow(count, size, out nuint total))
        {
            return null;
        }
        return mi_heap_realloc(heap, p, total);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void* mi_heap_reallocn(IntPtr heap, void* p, nuint count, nuint size) => mi_heap_reallocn((mi_heap_t*)heap, p, count, size);

    // Reallocate but free `p` on errors
    private static void* mi_heap_reallocf(mi_heap_t* heap, void* p, [NativeTypeName("size_t")] nuint newsize)
    {
        void* newp = mi_heap_realloc(heap, p, newsize);

        if ((newp == null) && (p != null))
        {
            mi_free(p);
        }

        return newp;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void* mi_heap_reallocf(IntPtr heap, void* p, nuint newsize) => mi_heap_reallocf((mi_heap_t*)heap, p, newsize);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void* mi_heap_rezalloc(mi_heap_t* heap, void* p, [NativeTypeName("size_t")] nuint newsize) => _mi_heap_realloc_zero(heap, p, newsize, true);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void* mi_heap_rezalloc(IntPtr heap, void* p, nuint newsize) => mi_heap_rezalloc((mi_heap_t*)heap, p, newsize);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void* mi_heap_recalloc(mi_heap_t* heap, void* p, [NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint size)
    {
        if (mi_count_size_overflow(count, size, out nuint total))
        {
            return null;
        }
        return mi_heap_rezalloc(heap, p, total);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void* mi_heap_recalloc(IntPtr heap, void* p, nuint count, nuint size) => mi_heap_recalloc((mi_heap_t*)heap, p, count, size);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void* mi_realloc(void* p, nuint newsize) => mi_heap_realloc(mi_get_default_heap(), p, newsize);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void* mi_reallocn(void* p, nuint count, nuint size) => mi_heap_reallocn(mi_get_default_heap(), p, count, size);

    // Reallocate but free `p` on errors
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void* mi_reallocf(void* p, nuint newsize) => mi_heap_reallocf(mi_get_default_heap(), p, newsize);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void* mi_rezalloc(void* p, nuint newsize) => mi_heap_rezalloc(mi_get_default_heap(), p, newsize);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void* mi_recalloc(void* p, nuint count, nuint size) => mi_heap_recalloc(mi_get_default_heap(), p, count, size);

    // ------------------------------------------------------
    // strdup, strndup, and realpath
    // ------------------------------------------------------

    private static nuint mi_strnlen(sbyte* s, nuint max_len)
    {
        nuint len = 0;
        while ((len < max_len) && (s[len] != 0))
        {
            len++;
        }
        return len;
    }

    // `strdup` using mi_malloc
    [return: NativeTypeName("char*")]
    private static sbyte* mi_heap_strdup(mi_heap_t* heap, [NativeTypeName("const char*")] sbyte* s)
    {
        if (s == null)
        {
            return null;
        }
        nuint n = mi_strnlen(s, MI_MAX_ALLOC_SIZE);
        if (n >= MI_MAX_ALLOC_SIZE)
        {
            return null;
        }

        sbyte* t = (sbyte*)mi_heap_malloc(heap, n + 1);

        if (t != null)
        {
            _ = memcpy(t, s, n);
            t[n] = 0;
        }
        return t;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial sbyte* mi_heap_strdup(IntPtr heap, sbyte* s) => mi_heap_strdup((mi_heap_t*)heap, s);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial sbyte* mi_strdup(sbyte* s) => mi_heap_strdup(mi_get_default_heap(), s);

    // `strndup` using mi_malloc
    [return: NativeTypeName("char*")]
    private static sbyte* mi_heap_strndup(mi_heap_t* heap, [NativeTypeName("const char*")] sbyte* s, [NativeTypeName("size_t")] nuint n)
    {
        if (s == null)
        {
            return null;
        }

        nuint max_len = (n < MI_MAX_ALLOC_SIZE) ? n : MI_MAX_ALLOC_SIZE;
        nuint m = mi_strnlen(s, max_len);
        if (m >= MI_MAX_ALLOC_SIZE)
        {
            return null;
        }

        mi_assert_internal((MI_DEBUG > 1) && (m <= n));
        sbyte* t = (sbyte*)mi_heap_malloc(heap, m + 1);

        if (t == null)
        {
            return null;
        }

        _ = memcpy(t, s, m);
        t[m] = 0;

        return t;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial sbyte* mi_heap_strndup(IntPtr heap, sbyte* s, nuint n) => mi_heap_strndup((mi_heap_t*)heap, s, n);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial sbyte* mi_strndup(sbyte* s, nuint n) => mi_heap_strndup(mi_get_default_heap(), s, n);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return: NativeTypeName("size_t")]
    private static nuint mi_path_max()
    {
        mi_assert_internal((MI_DEBUG > 1) && IsUnix);
        return path_max;
    }

    [return: NativeTypeName("char*")]
    private static sbyte* mi_heap_realpath(mi_heap_t* heap, [NativeTypeName("const char*")] sbyte* fname, [NativeTypeName("char*")] sbyte* resolved_name)
    {
        if (IsWindows)
        {
            if ((fname == null) || (fname[0] == 0))
            {
                last_errno = EINVAL;
                return null;
            }

            sbyte* buf = stackalloc sbyte[PATH_MAX];
            uint res = GetFullPathName(fname, PATH_MAX, (resolved_name == null) ? buf : resolved_name, null);

            if (res == 0)
            {
                last_errno = GetLastError() switch
                {
                    ERROR_LOCK_VIOLATION or ERROR_SHARING_VIOLATION or ERROR_INVALID_ACCESS => EACCES,
                    ERROR_INVALID_HANDLE or ERROR_INVALID_FUNCTION => EINVAL,
                    ERROR_PATH_NOT_FOUND => ENOTDIR,
                    ERROR_FILE_NOT_FOUND => ENOENT,
                    ERROR_NOT_ENOUGH_MEMORY => ENOMEM,
                    _ => EIO,
                };
                return null;
            }
            else if (res > PATH_MAX)
            {
                last_errno = ENAMETOOLONG;
                return null;
            }
            else if (resolved_name != null)
            {
                return resolved_name;
            }
            else
            {
                return mi_heap_strndup(heap, buf, PATH_MAX);
            }
        }
        else
        {
            if (resolved_name != null)
            {
                return realpath(fname, resolved_name);
            }
            else
            {
                nuint n = mi_path_max();
                sbyte* buf = (sbyte*)mi_malloc(n + 1);

                if (buf == null)
                {
                    return null;
                }

                sbyte* rname = realpath(fname, buf);
                sbyte* result = mi_heap_strndup(heap, rname, n);

                mi_free(buf);
                return result;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial sbyte* mi_heap_realpath(IntPtr heap, sbyte* fname, sbyte* resolved_name) => mi_heap_realpath((mi_heap_t*)heap, fname, resolved_name);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial sbyte* mi_realpath(sbyte* fname, sbyte* resolved_name) => mi_heap_realpath(mi_get_default_heap(), fname, resolved_name);

    /*-------------------------------------------------------
    C++ new and new_aligned
    The standard requires calling into `get_new_handler` and
    throwing the bad_alloc exception on failure. If we compile
    with a C++ compiler we can implement this precisely. If we
    use a C compiler we cannot throw a `bad_alloc` exception
    but we call `exit` instead (i.e. not returning).
    -------------------------------------------------------*/

    private const int MI_TRY_NEW_MAX = 4;

    private static bool mi_try_new_handler(bool nothrow)
    {
        delegate* unmanaged[Cdecl]<void> h = std_get_new_handler();

        if (h == null)
        {
            if (!nothrow)
            {
                _mi_error_message(EFAULT, "out of memory in 'new' call");
                abort();
            }
            return false;
        }
        else
        {
            h();
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void* mi_try_new([NativeTypeName("size_t")] nuint size, bool nothrow)
        => mi_heap_try_new(mi_get_default_heap(), size, nothrow);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void* mi_heap_try_new(mi_heap_t* heap, nuint size, bool nothrow)
    {
        void* p = null;

        for (int i = 0; (i < MI_TRY_NEW_MAX) && (p == null) && mi_try_new_handler(nothrow); i++)
        {
            if (size > MI_MAX_ALLOC_SIZE)
            {
                return null;
            }

            p = mi_heap_malloc(heap, size);
        }

        return p;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void* mi_try_new_aligned(nuint size, nuint alignment, bool nothrow)
    {
        void* p = null;

        for (int i = 0; (i < MI_TRY_NEW_MAX) && (p == null) && mi_try_new_handler(nothrow); i++)
        {
            if ((alignment == 0) || !_mi_is_power_of_two(alignment))
            {
                return null;
            }

            p = mi_malloc_aligned(size, alignment);
        }

        return p;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void* mi_try_new_realloc(void* p, nuint newsize)
    {
        void* q = null;

        for (int i = 0; (i < MI_TRY_NEW_MAX) && (q == null) && mi_try_new_handler(false); i++)
        {
            if (newsize > MI_MAX_ALLOC_SIZE)
            {
                return null;
            }

            q = mi_realloc(p, newsize);
        }

        return q;
    }

    public static partial void* mi_heap_alloc_new(IntPtr heap, nuint size)
    {
        mi_heap_t* mi_heap = (mi_heap_t*)heap;
        void* p = mi_heap_malloc(mi_heap, size);
        return mi_unlikely(p == null) ? mi_heap_try_new(mi_heap, size, false) : p;
    }

    public static partial void* mi_heap_alloc_new_n(IntPtr heap, nuint count, nuint size)
    {
        if (mi_unlikely(mi_count_size_overflow(count, size, out nuint total)))
        {
            _ = mi_try_new_handler(false);
            return null;
        }

        return mi_heap_alloc_new(heap, total);
    }

    public static partial void* mi_new(nuint size)
    {
        void* p = mi_malloc(size);

        if (mi_unlikely(p == null))
        {
            return mi_try_new(size, false);
        }

        return p;
    }

    public static partial void* mi_new_nothrow(nuint size)
    {
        void* p = mi_malloc(size);

        if (mi_unlikely(p == null))
        {
            return mi_try_new(size, true);
        }

        return p;
    }

    public static partial void* mi_new_aligned(nuint size, nuint alignment)
    {
        void* p = mi_malloc_aligned(size, alignment);
        return mi_unlikely(p == null) ? mi_try_new_aligned(size, alignment, false) : p;
    }

    public static partial void* mi_new_aligned_nothrow(nuint size, nuint alignment)
    {
        void* p = mi_malloc_aligned(size, alignment);
        return mi_unlikely(p == null) ? mi_try_new_aligned(size, alignment, true) : p;
    }

    public static partial void* mi_new_n(nuint count, nuint size)
    {
        if (mi_unlikely(mi_count_size_overflow(count, size, out nuint total)))
        {
            // on overflow we invoke the try_new_handler once to potentially throw std::bad_alloc
            _ = mi_try_new_handler(false);

            return null;
        }
        else
        {
            return mi_heap_alloc_new((IntPtr)mi_get_default_heap(), total);
        }
    }

    public static partial void* mi_new_realloc(void* p, nuint newsize)
    {
        void* q = mi_realloc(p, newsize);
        return mi_unlikely(q == null) ? mi_try_new_realloc(p, newsize) : q;
    }

    public static partial void* mi_new_reallocn(void* p, nuint newcount, nuint size)
    {
        if (mi_unlikely(mi_count_size_overflow(newcount, size, out nuint total)))
        {
            // on overflow we invoke the try_new_handler once to potentially throw std::bad_alloc
            _ = mi_try_new_handler(false);

            return null;
        }
        else
        {
            return mi_new_realloc(p, total);
        }
    }
}
