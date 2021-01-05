// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the alloc.c file from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

using System;
using System.Runtime.CompilerServices;
using static TerraFX.Interop.mi_delayed_t;
using static TerraFX.Interop.mi_page_kind_t;

namespace TerraFX.Interop
{
    public static unsafe partial class Mimalloc
    {
        // ------------------------------------------------------
        // Allocation
        // ------------------------------------------------------

        // Fast allocation in a page: just pop from the free list.
        // Fall back to generic allocation only if the list is empty.
        private static partial void* _mi_page_malloc(mi_heap_t* heap, mi_page_t* page, nuint size)
        {
            mi_assert_internal((MI_DEBUG > 1) && ((page->xblock_size == 0) || (mi_page_block_size(page) >= size)));
            mi_block_t* block = page->free;

            if (mi_unlikely(block == null))
            {
                return _mi_malloc_generic(heap, size);
            }

            mi_assert_internal((MI_DEBUG > 1) && block != null && _mi_ptr_page(block) == page);

            // pop from the free list
            page->free = mi_block_next(page, block);

            page->used++;
            mi_assert_internal((MI_DEBUG > 1) && ((page->free == null) || (_mi_ptr_page(page->free) == page)));

            if (MI_DEBUG > 0)
            {
                if (!page->is_zero)
                {
                    memset(block, MI_DEBUG_UNINIT, size);
                }
            }
            else if (MI_SECURE != 0)
            {
                // don't leak internal data
                block->next = 0;
            }

            if (MI_STAT > 1)
            {
                nuint bsize = mi_page_usable_block_size(page);

                if (bsize <= MI_LARGE_OBJ_SIZE_MAX)
                {
                    nuint bin = _mi_bin(bsize);
                    mi_stat_increase(ref (&heap->tld->stats.normal.e0)[bin], 1);
                }
            }

            if ((MI_PADDING > 0) && (MI_ENCODE_FREELIST != 0))
            {
                mi_padding_t* padding = (mi_padding_t*)((byte*)block + mi_page_usable_block_size(page));
                nint delta = (nint)((nuint)padding - (nuint)block - (size - MI_PADDING_SIZE));

                mi_assert_internal((MI_DEBUG > 1) && (delta >= 0) && (mi_page_usable_block_size(page) >= (size - MI_PADDING_SIZE + (nuint)delta)));

                padding->canary = unchecked((uint)mi_ptr_encode(page, block, &page->keys.e0));
                padding->delta = (uint)delta;

                byte* fill = (byte*)padding - delta;

                // set at most N initial padding bytes
                nuint maxpad = ((nuint)delta > MI_MAX_ALIGN_SIZE) ? MI_MAX_ALIGN_SIZE : (nuint)delta;

                for (nuint i = 0; i < maxpad; i++)
                {
                    fill[i] = MI_DEBUG_PADDING;
                }
            }

            return block;
        }

        // allocate a small block
        private static void* mi_heap_malloc_small(mi_heap_t* heap, [NativeTypeName("size_t")] nuint size)
        {
            mi_assert((MI_DEBUG != 0) && (heap != null));

            // heaps are thread local
            mi_assert((MI_DEBUG != 0) && ((heap->thread_id == 0) || (heap->thread_id == _mi_thread_id())));

            mi_assert((MI_DEBUG != 0) && (size <= MI_SMALL_SIZE_MAX));

            if ((MI_PADDING != 0) && (size == 0))
            {
                size = SizeOf<nuint>();
            }

            mi_page_t* page = _mi_heap_get_free_small_page(heap, size + MI_PADDING_SIZE);

            void* p = _mi_page_malloc(heap, page, size + MI_PADDING_SIZE);
            mi_assert_internal((MI_DEBUG > 1) && ((p == null) || (mi_usable_size(p) >= size)));

            if ((MI_STAT > 1) && (p != null))
            {
                mi_stat_increase(ref heap->tld->stats.malloc, mi_usable_size(p));
            }
            return p;
        }

        public static partial void* mi_heap_malloc_small(IntPtr heap, nuint size) => mi_heap_malloc_small((mi_heap_t*)heap, size);

        public static partial void* mi_malloc_small(nuint size) => mi_heap_malloc_small(mi_get_default_heap(), size);

        // The main allocation function
        private static void* mi_heap_malloc(mi_heap_t* heap, [NativeTypeName("size_t")] nuint size)
        {
            if (mi_likely(size <= MI_SMALL_SIZE_MAX))
            {
                return mi_heap_malloc_small(heap, size);
            }
            else
            {
                mi_assert((MI_DEBUG != 0) && (heap != null));

                // heaps are thread local
                mi_assert((MI_DEBUG != 0) && ((heap->thread_id == 0) || (heap->thread_id == _mi_thread_id())));

                // note: size can overflow but it is detected in malloc_generic

                void* p = _mi_malloc_generic(heap, size + MI_PADDING_SIZE);
                mi_assert_internal((MI_DEBUG > 1) && (p == null || mi_usable_size(p) >= size));

                if ((MI_STAT > 1) && (p != null))
                {
                    mi_stat_increase(ref heap->tld->stats.malloc, mi_usable_size(p));
                }
                return p;
            }
        }

        public static partial void* mi_heap_malloc(IntPtr heap, nuint size) => mi_heap_malloc((mi_heap_t*)heap, size);

        public static partial void* mi_malloc(nuint size) => mi_heap_malloc(mi_get_default_heap(), size);

        private static partial void _mi_block_zero_init(mi_page_t* page, void* p, nuint size)
        {
            // note: we need to initialize the whole usable block size to zero, not just the requested size,
            // or the recalloc/rezalloc functions cannot safely expand in place (see issue #63)

            mi_assert_internal((MI_DEBUG > 1) && (p != null));
            mi_assert_internal((MI_DEBUG > 1) && (mi_usable_size(p) >= size)); // size can be zero
            mi_assert_internal((MI_DEBUG > 1) && (_mi_ptr_page(p) == page));

            if (page->is_zero && (size > SizeOf<mi_block_t>()))
            {
                // already zero initialized memory

                // clear the free list pointer
                ((mi_block_t*)p)->next = 0;

                mi_assert_expensive((MI_DEBUG > 2) && mi_mem_is_zero(p, mi_usable_size(p)));
            }
            else
            {
                // otherwise memset
                memset(p, 0, mi_usable_size(p));
            }
        }

        // zero initialized small block
        public static partial void* mi_zalloc_small(nuint size)
        {
            void* p = mi_malloc_small(size);

            if (p != null)
            {
                // todo: can we avoid getting the page again?
                _mi_block_zero_init(_mi_ptr_page(p), p, size);
            }

            return p;
        }

        private static partial void* _mi_heap_malloc_zero(mi_heap_t* heap, nuint size, bool zero)
        {
            void* p = mi_heap_malloc(heap, size);

            if (zero && p != null)
            {
                // todo: can we avoid getting the page again?
                _mi_block_zero_init(_mi_ptr_page(p), p, size);
            }

            return p;
        }

        private static void* mi_heap_zalloc(mi_heap_t* heap, [NativeTypeName("size_t")] nuint size) => _mi_heap_malloc_zero(heap, size, true);

        public static partial void* mi_heap_zalloc(IntPtr heap, nuint size) => mi_heap_zalloc((mi_heap_t*)heap, size);

        public static partial void* mi_zalloc(nuint size) => mi_heap_zalloc(mi_get_default_heap(), size);

        // ------------------------------------------------------
        // Check for double free in secure and debug mode
        // This is somewhat expensive so only enabled for secure mode 4
        // ------------------------------------------------------

        // linear check if the free list contains a specific element
        private static bool mi_list_contains([NativeTypeName("const mi_page_t*")] mi_page_t* page, [NativeTypeName("const mi_block_t*")] mi_block_t* list, [NativeTypeName("const mi_block_t*")] mi_block_t* elem)
        {
            mi_assert_internal((MI_DEBUG > 1) && (MI_ENCODE_FREELIST != 0) && ((MI_SECURE >= 4) || (MI_DEBUG != 0)));

            while (list != null)
            {
                if (elem == list)
                {
                    return true;
                }
                list = mi_block_next(page, list);
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool mi_check_is_double_freex([NativeTypeName("const mi_page_t*")] mi_page_t* page, [NativeTypeName("const mi_block_t*")] mi_block_t* block)
        {
            mi_assert_internal((MI_DEBUG > 1) && (MI_ENCODE_FREELIST != 0) && ((MI_SECURE >= 4) || (MI_DEBUG != 0)));

            // The decoded value is in the same page (or null).
            // Walk the free lists to verify positively if it is already freed

            if (mi_list_contains(page, page->free, block) || mi_list_contains(page, page->local_free, block) || mi_list_contains(page, mi_page_thread_free(page), block))
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
            mi_assert_internal((MI_DEBUG > 1) && (MI_PADDING > 0) && (MI_ENCODE_FREELIST != 0));

            bsize = mi_page_usable_block_size(page);
            mi_padding_t* padding = (mi_padding_t*)((byte*)block + bsize);

            delta = padding->delta;
            return (unchecked((uint)mi_ptr_encode(page, block, &page->keys.e0)) == padding->canary) && (delta <= bsize);
        }

        // Return the exact usable size of a block.
        [return: NativeTypeName("size_t")]
        private static nuint mi_page_usable_size_of([NativeTypeName("const mi_page_t*")] mi_page_t* page, [NativeTypeName("const mi_block_t*")] mi_block_t* block)
        {
            if ((MI_PADDING > 0) && (MI_ENCODE_FREELIST != 0))
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
            mi_assert_internal((MI_DEBUG > 1) && (MI_PADDING > 0) && (MI_ENCODE_FREELIST != 0));

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
            nuint maxpad = (delta > MI_MAX_ALIGN_SIZE) ? MI_MAX_ALIGN_SIZE : delta;

            for (nuint i = 0; i < maxpad; i++)
            {
                if (fill[i] != MI_DEBUG_PADDING)
                {
                    wrong = bsize - delta + i;
                    return false;
                }
            }

            return true;
        }

        private static void mi_check_padding([NativeTypeName("const mi_page_t*")] mi_page_t* page, [NativeTypeName("const mi_block_t*")] mi_block_t* block)
        {
            if ((MI_PADDING > 0) && (MI_ENCODE_FREELIST != 0) && !mi_verify_padding(page, block, out nuint size, out nuint wrong))
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
            if ((MI_PADDING > 0) && (MI_ENCODE_FREELIST != 0))
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
        private static void _mi_free_block_mt(mi_page_t* page, mi_block_t* block)
        {
#pragma warning disable CS0420
            // The padding check may access the non-thread-owned page for the key values.
            // that is safe as these are constant and the page won't be freed (as the block is not freed yet).
            mi_check_padding(page, block);

            // for small size, ensure we can fit the delayed thread pointers without triggering overflow detection
            mi_padding_shrink(page, block, SizeOf<mi_block_t>());

            if (MI_DEBUG != 0)
            {
                memset(block, MI_DEBUG_FREED, mi_usable_size(block));
            }

            // huge page segments are always abandoned and can be freed immediately
            mi_segment_t* segment = _mi_page_segment(page);

            if (segment->page_kind == MI_PAGE_HUGE)
            {
                _mi_segment_huge_page_free(segment, page, block);
                return;
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
        private static void _mi_free_block(mi_page_t* page, bool local, mi_block_t* block)
        {
            // and push it on the free list
            if (mi_likely(local))
            {
                // owning thread can free a block directly

                if (mi_unlikely(mi_check_is_double_free(page, block)))
                {
                    return;
                }

                mi_check_padding(page, block);

                if (MI_DEBUG != 0)
                {
                    memset(block, MI_DEBUG_FREED, mi_page_block_size(page));
                }

                mi_block_set_next(page, block, page->local_free);

                page->local_free = block;
                page->used--;

                if (mi_unlikely(mi_page_all_free(page)))
                {
                    _mi_page_retire(page);
                }
                else if (mi_unlikely(mi_page_is_in_full(page)))
                {
                    _mi_page_unfull(page);
                }
            }
            else
            {
                _mi_free_block_mt(page, block);
            }
        }

        // Adjust a block that was allocated aligned, to the actual start of the block in the page.
        private static partial mi_block_t* _mi_page_ptr_unalign(mi_segment_t* segment, mi_page_t* page, void* p)
        {
            mi_assert_internal((MI_DEBUG > 1) && (page != null) && (p != null));

            nuint diff = (nuint)p - (nuint)_mi_page_start(segment, page, out _);
            nuint adjust = diff % mi_page_block_size(page);

            return (mi_block_t*)((nuint)p - adjust);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void mi_free_generic([NativeTypeName("const mi_segment_t*")] mi_segment_t* segment, bool local, void* p)
        {
            mi_page_t* page = _mi_segment_page_of(segment, p);
            mi_block_t* block = mi_page_has_aligned(page) ? _mi_page_ptr_unalign(segment, page, p) : (mi_block_t*)p;
            _mi_free_block(page, local, block);
        }

        // Get the segment data belonging to a pointer
        // This is just a single `and` in assembly but does further checks in debug mode
        // (and secure mode) if this was a valid pointer.
        private static mi_segment_t* mi_checked_ptr_segment([NativeTypeName("const void*")] void* p, [NativeTypeName("const char*")] string msg)
        {
            if ((MI_DEBUG > 0) && mi_unlikely(((nuint)p & (MI_INTPTR_SIZE - 1)) != 0))
            {
                _mi_error_message(EINVAL, "{0}: invalid (unaligned) pointer: {1:X}\n", msg, (nuint)p);
                return null;
            }

            mi_segment_t* segment = _mi_ptr_segment(p);

            if (mi_unlikely(segment == null))
            {
                // checks also for (p==null)
                return null;
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
            mi_block_t* block = (mi_block_t*)p;

            if (MI_STAT > 1)
            {
                mi_heap_t* heap = (mi_heap_t*)mi_heap_get_default();
                nuint bsize = mi_page_usable_block_size(page);

                mi_stat_decrease(ref heap->tld->stats.malloc, bsize);

                if (bsize <= MI_LARGE_OBJ_SIZE_MAX)
                {
                    // huge page stats are accounted for in `_mi_page_retire`
                    mi_stat_decrease(ref (&heap->tld->stats.normal.e0)[_mi_bin(bsize)], 1);
                }
            }

            if (mi_likely((tid == segment->thread_id) && (page->flags.full_aligned == 0)))
            {
                // the thread id matches and it is not a full page, nor has aligned blocks
                // local, and not full or aligned

                if (mi_unlikely(mi_check_is_double_free(page, block)))
                {
                    return;
                }

                mi_check_padding(page, block);

                if (MI_DEBUG != 0)
                {
                    memset(block, MI_DEBUG_FREED, mi_page_block_size(page));
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
            _mi_page_use_delayed_free(page, MI_USE_DELAYED_FREE, override_never: false);

            // collect all other non-local frees to ensure up-to-date `used` count
            _mi_page_free_collect(page, false);

            // and free the block (possibly freeing the page as well since used is updated)
            _mi_free_block(page, true, block);

            return true;
        }

        // Bytes available in a block
        [return: NativeTypeName("size_t")]
        private static nuint _mi_usable_size([NativeTypeName("const void*")] void* p, [NativeTypeName("const char*")] string msg)
        {
            mi_segment_t* segment = mi_checked_ptr_segment(p, msg);

            if (segment == null)
            {
                return 0;
            }

            mi_page_t* page = _mi_segment_page_of(segment, p);
            mi_block_t* block = (mi_block_t*)p;

            if (mi_unlikely(mi_page_has_aligned(page)))
            {
                block = _mi_page_ptr_unalign(segment, page, p);
                nuint size = mi_page_usable_size_of(page, block);

                nint adjust = (nint)((nuint)p - (nuint)block);

                mi_assert_internal((MI_DEBUG > 1) && (adjust >= 0) && ((nuint)adjust <= size));
                return size - (nuint)adjust;
            }
            else
            {
                return mi_page_usable_size_of(page, block);
            }
        }

        public static partial nuint mi_usable_size(void* p) => _mi_usable_size(p, "mi_usable_size");

        // ------------------------------------------------------
        // Allocation extensions
        // ------------------------------------------------------

        public static partial void mi_free_size(void* p, nuint size)
        {
            mi_assert((MI_DEBUG != 0) && ((p == null) || (size <= _mi_usable_size(p, "mi_free_size"))));
            mi_free(p);
        }

        public static partial void mi_free_size_aligned(void* p, nuint size, nuint alignment)
        {
            mi_assert((MI_DEBUG != 0) && (((nuint)p % alignment) == 0));
            mi_free_size(p, size);
        }

        public static partial void mi_free_aligned(void* p, nuint alignment)
        {
            mi_assert((MI_DEBUG != 0) && (((nuint)p % alignment) == 0));
            mi_free(p);
        }

        private static void* mi_heap_calloc(mi_heap_t* heap, [NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint size)
        {
            if (mi_count_size_overflow(count, size, out nuint total))
            {
                return null;
            }
            return mi_heap_zalloc(heap, total);
        }

        public static partial void* mi_heap_calloc(IntPtr heap, nuint count, nuint size) => mi_heap_calloc((mi_heap_t*)heap, count, size);

        public static partial void* mi_calloc(nuint count, nuint size) => mi_heap_calloc(mi_get_default_heap(), count, size);

        // Uninitialized `calloc`
        private static void* mi_heap_mallocn(mi_heap_t* heap, [NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint size)
        {
            if (mi_count_size_overflow(count, size, out nuint total))
            {
                return null;
            }
            return mi_heap_malloc(heap, total);
        }

        public static partial void* mi_heap_mallocn(IntPtr heap, nuint count, nuint size) => mi_heap_mallocn((mi_heap_t*)heap, count, size);

        public static partial void* mi_mallocn(nuint count, nuint size) => mi_heap_mallocn(mi_get_default_heap(), count, size);

        // Expand in place or fail
        public static partial void* mi_expand(void* p, nuint newsize)
        {
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
            if (p == null)
            {
                return _mi_heap_malloc_zero(heap, newsize, zero);
            }

            nuint size = _mi_usable_size(p, "mi_realloc");

            if ((newsize <= size) && (newsize >= (size / 2)))
            {
                // reallocation still fits and not more than 50% waste
                return p;
            }

            void* newp = mi_heap_malloc(heap, newsize);

            if (mi_likely(newp != null))
            {
                if (zero && newsize > size)
                {
                    // also set last word in the previous allocation to zero to ensure any padding is zero-initialized
                    nuint start = (size >= SizeOf<nuint>()) ? size - SizeOf<nuint>() : 0;
                    memset((byte*)newp + start, 0, newsize - start);
                }

                memcpy(newp, p, (newsize > size) ? size : newsize);

                // only free if successful
                mi_free(p);
            }

            return newp;
        }

        private static void* mi_heap_realloc(mi_heap_t* heap, void* p, [NativeTypeName("size_t")] nuint newsize) => _mi_heap_realloc_zero(heap, p, newsize, false);

        public static partial void* mi_heap_realloc(IntPtr heap, void* p, nuint newsize) => mi_heap_realloc((mi_heap_t*)heap, p, newsize);

        private static void* mi_heap_reallocn(mi_heap_t* heap, void* p, [NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint size)
        {
            if (mi_count_size_overflow(count, size, out nuint total))
            {
                return null;
            }
            return mi_heap_realloc(heap, p, total);
        }

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

        public static partial void* mi_heap_reallocf(IntPtr heap, void* p, nuint newsize) => mi_heap_reallocf((mi_heap_t*)heap, p, newsize);

        private static void* mi_heap_rezalloc(mi_heap_t* heap, void* p, [NativeTypeName("size_t")] nuint newsize) => _mi_heap_realloc_zero(heap, p, newsize, true);

        public static partial void* mi_heap_rezalloc(IntPtr heap, void* p, nuint newsize) => mi_heap_rezalloc((mi_heap_t*)heap, p, newsize);

        private static void* mi_heap_recalloc(mi_heap_t* heap, void* p, [NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint size)
        {
            if (mi_count_size_overflow(count, size, out nuint total))
            {
                return null;
            }
            return mi_heap_rezalloc(heap, p, total);
        }

        public static partial void* mi_heap_recalloc(IntPtr heap, void* p, nuint count, nuint size) => mi_heap_recalloc((mi_heap_t*)heap, p, count, size);

        public static partial void* mi_realloc(void* p, nuint newsize) => mi_heap_realloc(mi_get_default_heap(), p, newsize);

        public static partial void* mi_reallocn(void* p, nuint count, nuint size) => mi_heap_reallocn(mi_get_default_heap(), p, count, size);

        // Reallocate but free `p` on errors
        public static partial void* mi_reallocf(void* p, nuint newsize) => mi_heap_reallocf(mi_get_default_heap(), p, newsize);

        public static partial void* mi_rezalloc(void* p, nuint newsize) => mi_heap_rezalloc(mi_get_default_heap(), p, newsize);

        public static partial void* mi_recalloc(void* p, nuint count, nuint size) => mi_heap_recalloc(mi_get_default_heap(), p, count, size);

        // ------------------------------------------------------
        // strdup, strndup, and realpath
        // ------------------------------------------------------

        // `strdup` using mi_malloc
        [return: NativeTypeName("char*")]
        private static sbyte* mi_heap_strdup(mi_heap_t* heap, [NativeTypeName("const char*")] sbyte* s)
        {
            if (s == null)
            {
                return null;
            }
            nuint n = strlen(s);

            sbyte* t = (sbyte*)mi_heap_malloc(heap, n + 1);

            if (t != null)
            {
                memcpy(t, s, n + 1);
            }
            return t;
        }

        public static partial sbyte* mi_heap_strdup(IntPtr heap, sbyte* s) => mi_heap_strdup((mi_heap_t*)heap, s);

        public static partial sbyte* mi_strdup(sbyte* s) => mi_heap_strdup(mi_get_default_heap(), s);

        // `strndup` using mi_malloc
        [return: NativeTypeName("char*")]
        private static sbyte* mi_heap_strndup(mi_heap_t* heap, [NativeTypeName("const char*")] sbyte* s, [NativeTypeName("size_t")] nuint n)
        {
            if (s == null)
            {
                return null;
            }

            // find end of string in the first `n` characters (returns null if not found)
            sbyte* end = (sbyte*)memchr(s, 0, n);

            // `m` is the minimum of `n` or the end-of-string
            nuint m = (end != null) ? (nuint)(end - s) : n;

            mi_assert_internal((MI_DEBUG > 1) && (m <= n));
            sbyte* t = (sbyte*)mi_heap_malloc(heap, m + 1);

            if (t == null)
            {
                return null;
            }

            memcpy(t, s, m);
            t[m] = 0;

            return t;
        }

        public static partial sbyte* mi_heap_strndup(IntPtr heap, sbyte* s, nuint n) => mi_heap_strndup((mi_heap_t*)heap, s, n);

        public static partial sbyte* mi_strndup(sbyte* s, nuint n) => mi_heap_strndup(mi_get_default_heap(), s, n);

        [return: NativeTypeName("size_t")]
        private static nuint mi_path_max()
        {
            mi_assert_internal((MI_DEBUG > 1) && IsLinux);
            return path_max;
        }

        [return: NativeTypeName("char*")]
        private static sbyte* mi_heap_realpath(mi_heap_t* heap, [NativeTypeName("const char*")] sbyte* fname, [NativeTypeName("char*")] sbyte* resolved_name)
        {
            if (IsWindows)
            {
                sbyte* buf = stackalloc sbyte[PATH_MAX];
                uint res = GetFullPathName(fname, PATH_MAX, (resolved_name == null) ? buf : resolved_name, null);

                if (res == 0)
                {
                    last_errno = (int)GetLastError();
                    return null;
                }
                else if (res > PATH_MAX)
                {
                    last_errno = EINVAL;
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

        public static partial sbyte* mi_heap_realpath(IntPtr heap, sbyte* fname, sbyte* resolved_name) => mi_heap_realpath((mi_heap_t*)heap, fname, resolved_name);

        public static partial sbyte* mi_realpath(sbyte* fname, sbyte* resolved_name) => mi_heap_realpath(mi_get_default_heap(), fname, resolved_name);

        /*-------------------------------------------------------
        C++ new and new_aligned
        The standard requires calling into `get_new_handler` and
        throwing the bad_alloc exception on failure. If we compile
        with a C++ compiler we can implement this precisely. If we
        use a C compiler we cannot throw a `bad_alloc` exception
        but we call `exit` instead (i.e. not returning).
        -------------------------------------------------------*/

        private static bool mi_try_new_handler(bool nothrow)
        {
            delegate* unmanaged[Cdecl]<void> h = std_get_new_handler();

            if (h == null)
            {
                if (!nothrow)
                {
                    // cannot throw in plain C, use exit as we are out of memory anyway.
                    exit(ENOMEM);
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
        {
            void* p = null;

            while ((p == null) && mi_try_new_handler(nothrow))
            {
                p = mi_malloc(size);
            }

            return p;
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
            void* p;

            do
            {
                p = mi_malloc_aligned(size, alignment);
            }
            while ((p == null) && mi_try_new_handler(false));

            return p;
        }

        public static partial void* mi_new_aligned_nothrow(nuint size, nuint alignment)
        {
            void* p;

            do
            {
                p = mi_malloc_aligned(size, alignment);
            }
            while ((p == null) && mi_try_new_handler(true));

            return p;
        }

        public static partial void* mi_new_n(nuint count, nuint size)
        {
            if (mi_unlikely(mi_count_size_overflow(count, size, out nuint total)))
            {
                // on overflow we invoke the try_new_handler once to potentially throw std::bad_alloc
                mi_try_new_handler(false);

                return null;
            }
            else
            {
                return mi_new(total);
            }
        }

        public static partial void* mi_new_realloc(void* p, nuint newsize)
        {
            void* q;

            do
            {
                q = mi_realloc(p, newsize);
            }
            while ((q == null) && mi_try_new_handler(false));

            return q;
        }

        public static partial void* mi_new_reallocn(void* p, nuint newcount, nuint size)
        {
            if (mi_unlikely(mi_count_size_overflow(newcount, size, out nuint total)))
            {
                // on overflow we invoke the try_new_handler once to potentially throw std::bad_alloc
                mi_try_new_handler(false);

                return null;
            }
            else
            {
                return mi_new_realloc(p, total);
            }
        }
    }
}
