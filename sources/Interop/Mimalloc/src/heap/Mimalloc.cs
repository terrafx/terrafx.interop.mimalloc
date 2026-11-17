// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the heap.c file from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static TerraFX.Interop.Mimalloc.mi_collect_t;
using static TerraFX.Interop.Mimalloc.mi_delayed_t;

namespace TerraFX.Interop.Mimalloc;

public static unsafe partial class Mimalloc
{
    /* -----------------------------------------------------------
      Helpers
    ----------------------------------------------------------- */

    // Visit all pages in a heap; returns `false` if break was called.
    private static bool mi_heap_visit_pages(mi_heap_t* heap, [NativeTypeName("heap_page_visitor_fun*")] heap_page_visitor_fun fn, bool include_full, void* arg1, void* arg2)
    {
        if ((heap == null) || (heap->page_count == 0))
        {
            return true;
        }

        nuint total = 0;

        // visit all pages
        if (MI_DEBUG > 1)
        {
            total = heap->page_count;
        }

        nuint count = 0;

        nuint max_bin = include_full ? MI_BIN_FULL : ((nuint)MI_BIN_FULL - 1);
        for (nuint i = 0; i <= max_bin; i++)
        {
            mi_page_queue_t* pq = &heap->pages.e0 + i;
            mi_page_t* page = pq->first;

            while (page != null)
            {
                // save next in case the page gets removed from the queue
                mi_page_t* next = page->next;

                mi_assert_internal((MI_DEBUG > 1) && (mi_page_heap(page) == heap));

                count++;

                if (!fn(heap, pq, page, arg1, arg2))
                {
                    return false;
                }

                page = next; // and continue
            }
        }

        mi_assert_internal((MI_DEBUG > 1) && (!include_full || (count == total)));
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]

    private static bool mi_heap_page_is_valid(mi_heap_t* heap, mi_page_queue_t* pq, mi_page_t* page, void* arg1, void* arg2)
    {
        mi_assert_internal((MI_DEBUG > 1) && (MI_DEBUG >= 2));
        mi_assert_internal((MI_DEBUG > 1) && (mi_page_heap(page) == heap));

        mi_segment_t* segment = _mi_page_segment(page);
        mi_assert_internal((MI_DEBUG > 1) && (segment->thread_id == heap->thread_id));

        mi_assert_expensive((MI_DEBUG > 2) && _mi_page_is_valid(page));
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool mi_heap_is_valid(mi_heap_t* heap)
    {
        mi_assert_internal((MI_DEBUG > 1) && (MI_DEBUG >= 3));
        mi_assert_internal((MI_DEBUG > 1) && (heap != null));

        _ = mi_heap_visit_pages(heap, mi_heap_page_is_valid, true, null, null);
        return true;
    }

    /* -----------------------------------------------------------
      "Collect" pages by migrating `local_free` and `thread_free`
      lists and freeing empty pages. This is done when a thread
      stops (and in that case abandons pages if there are still
      blocks alive)
    ----------------------------------------------------------- */

    private static bool mi_heap_page_collect(mi_heap_t* heap, mi_page_queue_t* pq, mi_page_t* page, void* arg_collect, void* arg2)
    {
        mi_assert_internal((MI_DEBUG > 1) && mi_heap_page_is_valid(heap, pq, page, null, null));

        mi_collect_t collect = *(mi_collect_t*)arg_collect;
        _mi_page_free_collect(page, collect >= MI_FORCE);

        if (mi_page_all_free(page))
        {
            // no more used blocks, free the page. 
            // note: this will free retired pages as well.
            _mi_page_free(page, pq, collect >= MI_FORCE);
        }
        else if (collect == MI_ABANDON)
        {
            // still used blocks but the thread is done; abandon the page
            _mi_page_abandon(page, pq);
        }

        // don't break
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool mi_heap_page_never_delayed_free(mi_heap_t* heap, mi_page_queue_t* pq, mi_page_t* page, void* arg1, void* arg2)
    {
        _mi_page_use_delayed_free(page, MI_NEVER_DELAYED_FREE, false);

        // don't break
        return true;
    }

    private static void mi_heap_collect_ex(mi_heap_t* heap, mi_collect_t collect)
    {
#pragma warning disable CS0420
        if ((heap == null) || !mi_heap_is_initialized(heap))
        {
            return;
        }

        _mi_deferred_free(heap, collect >= MI_FORCE);

        // note: never reclaim on collect but leave it to threads that need storage to reclaim 
        if (((MI_DEBUG == 0) ? (collect == MI_FORCE) : (collect >= MI_FORCE)) && _mi_is_main_thread() && mi_heap_is_backing(heap) && !heap->no_reclaim)
        {
            // the main thread is abandoned (end-of-program), try to reclaim all abandoned segments.
            // if all memory is freed by now, all segments should be freed.
            _mi_abandoned_reclaim_all(heap, &heap->tld->segments);
        }

        // if abandoning, mark all pages to no longer add to delayed_free
        if (collect == MI_ABANDON)
        {
            _ = mi_heap_visit_pages(heap, mi_heap_page_never_delayed_free, true, null, null);
        }

        // free thread delayed blocks.
        // (if abandoning, after this there are no more thread-delayed references into the pages.)
        if (collect >= MI_ABANDON)
        {
            _mi_heap_delayed_free_all(heap);
        }
        else
        {
            _ = _mi_heap_delayed_free_partial(heap);
        }

        // collect retired pages
        _mi_heap_collect_retired(heap, collect >= MI_FORCE);

        // collect all pages owned by this thread
        _ = mi_heap_visit_pages(heap, mi_heap_page_collect, collect != MI_NORMAL, &collect, null);

        mi_assert_internal((MI_DEBUG > 1) && ((collect != MI_ABANDON) || (mi_atomic_load_ptr_acquire<mi_block_t>(ref heap->thread_delayed_free) == null)));

        _mi_segments_collect(collect == MI_FORCE, &heap->tld->segments);
        _mi_arenas_collect(collect == MI_FORCE, ref heap->tld->stats);
#pragma warning restore CS0420
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static partial void _mi_heap_collect_abandon(mi_heap_t* heap) => mi_heap_collect_ex(heap, MI_ABANDON);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void mi_heap_collect(mi_heap_t* heap, bool force) => mi_heap_collect_ex(heap, force ? MI_FORCE : MI_NORMAL);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void mi_heap_collect(IntPtr heap, bool force) => mi_heap_collect((mi_heap_t*)heap, force);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial IntPtr mi_theap_get_default() => mi_heap_get_default();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial IntPtr mi_theap_set_default(IntPtr heap) => mi_heap_set_default(heap);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void mi_theap_collect(IntPtr heap, bool force) => mi_heap_collect(heap, force);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void mi_collect(bool force) => mi_heap_collect(mi_get_default_heap(), force);

    /* -----------------------------------------------------------
      Heap new
    ----------------------------------------------------------- */

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial IntPtr mi_heap_get_default() => (IntPtr)mi_get_default_heap();

    public static partial IntPtr mi_heap_get_backing()
    {
        mi_heap_t* heap = (mi_heap_t*)mi_heap_get_default();
        mi_assert_internal((MI_DEBUG > 1) && (heap != null));

        mi_heap_t* bheap = heap->tld->heap_backing;

        mi_assert_internal((MI_DEBUG > 1) && (bheap != null));
        mi_assert_internal((MI_DEBUG > 1) && (bheap->thread_id == _mi_thread_id()));

        return (IntPtr)bheap;
    }

    public static partial IntPtr mi_heap_new() => mi_heap_new_ex(0, allow_destroy: true, arena_id: 0);

    public static partial IntPtr mi_heap_new_in_arena(int arena_id) => mi_heap_new_ex(0, allow_destroy: false, arena_id: arena_id);

    public static partial IntPtr mi_heap_new_ex(int heap_tag, bool allow_destroy, int arena_id)
    {
        mi_heap_t* bheap = (mi_heap_t*)mi_heap_get_backing();

        // todo: OS allocate in secure mode?
        mi_heap_t* heap = mi_heap_malloc_tp<mi_heap_t>((IntPtr)bheap);

        if (heap == null)
        {
            return IntPtr.Zero;
        }

        mi_assert((MI_DEBUG != 0) && (heap_tag >= 0) && (heap_tag < 256));
        init_mi_heap(heap, bheap->tld, bheap, no_reclaim: allow_destroy, tag: (byte)heap_tag);
        heap->arena_id = arena_id;
        _mi_heap_guarded_init(heap);
        return (IntPtr)heap;
    }

    private static bool _mi_heap_memid_is_suitable(mi_heap_t* heap, mi_memid_t memid)
        => mi_arena_memid_is_suitable(memid, heap->arena_id);

    private static mi_heap_t* _mi_heap_by_tag(mi_heap_t* heap, byte tag)
    {
        if (heap->tag == tag)
        {
            return heap;
        }

        for (mi_heap_t* current = heap->tld->heaps; current != null; current = current->next)
        {
            if (current->tag == tag)
            {
                return current;
            }
        }

        return null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static partial nuint _mi_heap_random_next(mi_heap_t* heap) => _mi_random_next(ref heap->random);

    // zero out the page queues
    private static void mi_heap_reset_pages(mi_heap_t* heap)
    {
        mi_assert_internal((MI_DEBUG > 1) && (heap != null));
        mi_assert_internal((MI_DEBUG > 1) && mi_heap_is_initialized(heap));

        // TODO: copy full empty heap instead?

        heap->pages_free_direct = *_mi_heap_empty_pages_free_direct;
        heap->pages = *_mi_heap_empty_pages;

        heap->thread_delayed_free = 0;
        heap->page_count = 0;
        heap->pages_full_size = 0;
        heap->generic_count = 0;
        heap->generic_collect_count = 0;
    }

    // called from `mi_heap_destroy` and `mi_heap_delete` to free the internal heap resources.
    private static void mi_heap_free(mi_heap_t* heap)
    {
        mi_assert((MI_DEBUG != 0) && (heap != null));

        if ((heap == null) || !mi_heap_is_initialized(heap))
        {
            return;
        }

        if (mi_heap_is_backing(heap))
        {
            // dont free the backing heap
            return;
        }

        // reset default
        if (mi_heap_is_default(heap))
        {
            _mi_heap_set_default_direct(heap->tld->heap_backing);
        }

        // remove ourselves from the thread local heaps list
        // linear search but we expect the number of heaps to be relatively small

        mi_heap_t* prev = null;
        mi_heap_t* curr = heap->tld->heaps;

        while (curr != heap && curr != null)
        {
            prev = curr;
            curr = curr->next;
        }

        mi_assert_internal((MI_DEBUG > 1) && (curr == heap));

        if (curr == heap)
        {
            if (prev != null)
            {
                prev->next = heap->next;
            }
            else
            {
                heap->tld->heaps = heap->next;
            }
        }

        mi_assert_internal((MI_DEBUG > 1) && (heap->tld->heaps != null));

        // and free the used memory
        mi_free(heap);
    }

    /* -----------------------------------------------------------
      Heap destroy
    ----------------------------------------------------------- */

    private static bool _mi_heap_page_destroy(mi_heap_t* heap, mi_page_queue_t* pq, mi_page_t* page, void* arg1, void* arg2)
    {
        // ensure no more thread_delayed_free will be added
        _mi_page_use_delayed_free(page, MI_NEVER_DELAYED_FREE, false);

        // stats
        nuint bsize = mi_page_block_size(page);

        if (bsize > MI_LARGE_OBJ_SIZE_MAX)
        {
            _mi_stat_decrease(ref heap->tld->stats.huge, bsize);
        }

        if (MI_STAT > 0)
        {
            // update used count
            _mi_page_free_collect(page, false);

            nuint inuse = page->used;

            if (bsize <= MI_LARGE_OBJ_SIZE_MAX)
            {
                mi_stat_decrease(ref heap->tld->stats.normal, bsize * inuse);

                if (MI_STAT > 1)
                {
                    mi_stat_decrease(ref (&heap->tld->stats.normal_bins.e0)[_mi_bin(bsize)], inuse);
                }
            }

            // todo: off for aligned blocks...
            mi_stat_decrease(ref heap->tld->stats.malloc, bsize * inuse);
        }

        // pretend it is all free now
        mi_assert_internal((MI_DEBUG > 1) && (mi_page_thread_free(page) == null));

        page->used = 0;

        // and free the page
        // mi_page_free(page,false);

        page->next = null;
        page->prev = null;

        _mi_segment_page_free(page, force: false, &heap->tld->segments);

        // keep going
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static partial void _mi_heap_destroy_pages(mi_heap_t* heap)
    {
        _ = mi_heap_visit_pages(heap, _mi_heap_page_destroy, true, null, null);
        mi_heap_reset_pages(heap);
    }

    private static void mi_heap_destroy(mi_heap_t* heap)
    {
        mi_assert((MI_DEBUG != 0) && (heap != null));
        mi_assert((MI_DEBUG != 0) && heap->no_reclaim);
        mi_assert_expensive((MI_DEBUG > 2) && mi_heap_is_valid(heap));

        if ((heap == null) || !mi_heap_is_initialized(heap))
        {
            return;
        }

        if (!heap->no_reclaim)
        {
            // don't free in case it may contain reclaimed pages
            mi_heap_delete(heap);
        }
        else
        {
            // free all pages
            _mi_heap_destroy_pages(heap);
            mi_heap_free(heap);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void mi_heap_destroy(IntPtr heap) => mi_heap_destroy((mi_heap_t*)heap);

    /* -----------------------------------------------------------
      Safe Heap delete
    ----------------------------------------------------------- */

    // Tranfer the pages from one heap to the other
    private static void mi_heap_absorb(mi_heap_t* heap, mi_heap_t* from)
    {
        mi_assert_internal((MI_DEBUG > 1) && (heap != null));

        if ((from == null) || (from->page_count == 0))
        {
            return;
        }

        // reduce the size of the delayed frees
        _ = _mi_heap_delayed_free_partial(from);

        // transfer all pages by appending the queues; this will set a new heap field 
        // so threads may do delayed frees in either heap for a while.
        // note: appending waits for each page to not be in the `MI_DELAYED_FREEING` state
        // so after this only the new heap will get delayed frees

        for (nuint i = 0; i <= MI_BIN_FULL; i++)
        {
            mi_page_queue_t* pq = &heap->pages.e0 + i;
            mi_page_queue_t* append = &from->pages.e0 + i;

            nuint pcount = _mi_page_queue_append(heap, pq, append);

            heap->page_count += pcount;
            from->page_count -= pcount;
        }

        mi_assert_internal((MI_DEBUG > 1) && (from->page_count == 0));
        heap->pages_full_size += from->pages_full_size;
        from->pages_full_size = 0;

        // and do outstanding delayed frees in the `from` heap  
        // note: be careful here as the `heap` field in all those pages no longer point to `from`,
        // turns out to be ok as `_mi_heap_delayed_free` only visits the list and calls a 
        // the regular `_mi_free_delayed_block` which is safe.
        _mi_heap_delayed_free_all(from);

#pragma warning disable CS0420
        mi_assert_internal((MI_DEBUG > 1) && (mi_atomic_load_ptr_relaxed<mi_block_t>(ref from->thread_delayed_free) == null));
#pragma warning restore CS0420

        // and reset the `from` heap
        mi_heap_reset_pages(from);
    }

    private static bool mi_heaps_are_compatible(mi_heap_t* heap1, mi_heap_t* heap2)
        => (heap1->tag == heap2->tag) && (heap1->arena_id == heap2->arena_id);

    // Safe delete a heap without freeing any still allocated blocks in that heap.
    private static void mi_heap_delete(mi_heap_t* heap)
    {
        mi_assert((MI_DEBUG != 0) && (heap != null));
        mi_assert_expensive((MI_DEBUG > 2) && mi_heap_is_valid(heap));

        if ((heap == null) || !mi_heap_is_initialized(heap))
        {
            return;
        }

        mi_heap_t* backing_heap = heap->tld->heap_backing;

        if ((backing_heap != heap) && mi_heaps_are_compatible(backing_heap, heap))
        {
            // tranfer still used pages to the backing heap
            mi_heap_absorb(backing_heap, heap);
        }
        else
        {
            // the backing heap abandons its pages
            _mi_heap_collect_abandon(heap);
        }

        mi_assert_internal((MI_DEBUG > 1) && (heap->page_count == 0));
        mi_heap_free(heap);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void mi_heap_delete(IntPtr heap) => mi_heap_delete((mi_heap_t*)heap);

    private static mi_heap_t* mi_heap_set_default(mi_heap_t* heap)
    {
        mi_assert((MI_DEBUG != 0) && (heap != null));
        mi_assert((MI_DEBUG != 0) && mi_heap_is_initialized(heap));

        if ((heap == null) || !mi_heap_is_initialized(heap))
        {
            return null;
        }

        mi_assert_expensive((MI_DEBUG > 2) && mi_heap_is_valid(heap));
        mi_heap_t* old = mi_get_default_heap();

        _mi_heap_set_default_direct(heap);
        return old;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial IntPtr mi_heap_set_default(IntPtr heap) => (IntPtr)mi_heap_set_default((mi_heap_t*)heap);

    /* -----------------------------------------------------------
      Analysis
    ----------------------------------------------------------- */

    // static since it is not thread safe to access heaps from other threads.
    private static mi_heap_t* mi_heap_of_block([NativeTypeName("const void*")] void* p)
    {
        if (p == null)
        {
            return null;
        }

        mi_segment_t* segment = _mi_ptr_segment(p);

        bool valid = _mi_ptr_cookie(segment) == segment->cookie;
        mi_assert_internal((MI_DEBUG > 1) && valid);

        if (mi_unlikely(!valid))
        {
            return null;
        }

        return mi_page_heap(_mi_segment_page_of(segment, p));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool mi_heap_contains_block(mi_heap_t* heap, [NativeTypeName("const void*")] void* p)
    {
        mi_assert((MI_DEBUG != 0) && (heap != null));

        if ((heap == null) || !mi_heap_is_initialized(heap))
        {
            return false;
        }

        return heap == mi_heap_of_block(p);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial bool mi_heap_contains_block(IntPtr heap, void* p) => mi_heap_contains_block((mi_heap_t*)heap, p);

    // The caller must be the only thread accessing the page.
    public static partial bool mi_unsafe_heap_page_is_under_utilized(IntPtr heap, void* p, nuint perc_threshold)
    {
        if (p == null)
        {
            return false;
        }

        mi_segment_t* segment = _mi_ptr_segment(p);
        if ((segment == null) || ((MI_DEBUG != 0) && !mi_is_in_heap_region(p)) || (_mi_ptr_cookie(segment) != segment->cookie))
        {
            return false;
        }

        mi_page_t* page = _mi_segment_page_of(segment, p);
        if ((page == null) || (page->used == page->capacity) || (page->capacity < page->reserved) || (page->prev == null))
        {
            return false;
        }

        mi_heap_t* page_heap = mi_page_heap(page);
        if ((page_heap == null) || ((heap != IntPtr.Zero) && (page_heap != (mi_heap_t*)heap)) || (page->capacity == 0))
        {
            return false;
        }

        if (perc_threshold >= 100)
        {
            return true;
        }

        return perc_threshold >= (((nuint)100 * page->used) / page->capacity);
    }

    private static bool mi_heap_page_check_owned(mi_heap_t* heap, mi_page_queue_t* pq, mi_page_t* page, void* p, void* vfound)
    {
        bool* found = (bool*)vfound;
        mi_segment_t* segment = _mi_page_segment(page);

        void* start = mi_page_start(page);
        void* end = (byte*)start + (page->capacity * mi_page_block_size(page));

        *found = (p >= start) && (p < end);

        // continue if not found
        return !*found;
    }

    private static bool mi_heap_check_owned(mi_heap_t* heap, [NativeTypeName("const void*")] void* p)
    {
        mi_assert((MI_DEBUG != 0) && (heap != null));

        if ((heap == null) || !mi_heap_is_initialized(heap))
        {
            return false;
        }

        if (((nuint)p & (MI_INTPTR_SIZE - 1)) != 0)
        {
            // only aligned pointers
            return false;
        }

        bool found = false;
        _ = mi_heap_visit_pages(heap, mi_heap_page_check_owned, true, (void*)p, &found);
        return found;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial bool mi_heap_check_owned(IntPtr heap, void* p) => mi_heap_check_owned((mi_heap_t*)heap, p);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial bool mi_check_owned(void* p) => mi_heap_check_owned(mi_get_default_heap(), p);

    /* -----------------------------------------------------------
      Visit all heap blocks and areas
      Todo: enable visiting abandoned pages, and
            enable visiting all blocks of all heaps across threads
    ----------------------------------------------------------- */

    private static bool mi_heap_area_visit_blocks([NativeTypeName("const mi_heap_area_ex_t*")] mi_heap_area_ex_t* xarea, [NativeTypeName("mi_block_visit_fun*")] mi_block_visit_fun visitor, void* arg)
    {
        mi_assert((MI_DEBUG != 0) && (xarea != null));

        if (xarea == null)
        {
            return true;
        }

        mi_heap_area_t* area = &xarea->area;
        mi_page_t* page = xarea->page;

        mi_assert((MI_DEBUG != 0) && (page != null));

        if (page == null)
        {
            return true;
        }

        _mi_page_free_collect(page, true);
        mi_assert_internal((MI_DEBUG > 1) && (page->local_free == null));

        if (page->used == 0)
        {
            return true;
        }

        nuint bsize = mi_page_block_size(page);
        byte* pstart = _mi_segment_page_start(_mi_page_segment(page), page, out nuint psize, out _);

        if (page->capacity == 1)
        {
            // optimize page with one block
            mi_assert_internal((MI_DEBUG > 1) && page->used == 1 && page->free == null);
            return visitor((IntPtr)mi_page_heap(page), area, pstart, bsize, arg);
        }

        // create a bitmap of free blocks.

        nuint* free_map = stackalloc nuint[(int)(MI_MAX_BLOCKS / SizeOf<nuint>())];
        _ = memset(free_map, 0, MI_MAX_BLOCKS / SizeOf<nuint>());

        nuint free_count = 0;

        for (mi_block_t* block = page->free; block != null; block = mi_block_next(page, block))
        {
            free_count++;
            mi_assert_internal((MI_DEBUG > 1) && ((byte*)block >= pstart) && ((byte*)block < (pstart + psize)));

            nuint offset = (nuint)block - (nuint)pstart;
            mi_assert_internal((MI_DEBUG > 1) && (offset % bsize == 0));

            // Todo: avoid division?
            nuint blockidx = offset / bsize;

            mi_assert_internal((MI_DEBUG > 1) && (blockidx < MI_MAX_BLOCKS));

            nuint bitidx = blockidx / SizeOf<nuint>();
            nuint bit = blockidx - (bitidx * SizeOf<nuint>());

            free_map[bitidx] |= (nuint)1 << (int)bit;
        }

        mi_assert_internal((MI_DEBUG > 1) && (page->capacity == (free_count + page->used)));

        // walk through all blocks skipping the free ones
        nuint used_count = 0;

        for (nuint i = 0; i < page->capacity; i++)
        {
            nuint bitidx = i / SizeOf<nuint>();
            nuint bit = i - (bitidx * SizeOf<nuint>());
            nuint m = free_map[bitidx];

            if ((bit == 0) && (m == UINTPTR_MAX))
            {
                // skip a run of free blocks
                i += SizeOf<nuint>() - 1;
            }
            else if ((m & ((nuint)1 << (int)bit)) == 0)
            {
                used_count++;
                byte* block = pstart + (i * bsize);

                if (!visitor((IntPtr)mi_page_heap(page), area, block, bsize, arg))
                {
                    return false;
                }
            }
        }

        mi_assert_internal((MI_DEBUG > 1) && (page->used == used_count));
        return true;
    }

    private static bool mi_heap_visit_areas_page(mi_heap_t* heap, mi_page_queue_t* pq, mi_page_t* page, void* vfun, void* arg)
    {
        GCHandle handle = GCHandle.FromIntPtr((IntPtr)vfun);
        mi_heap_area_visit_fun fun = (mi_heap_area_visit_fun)handle.Target!;

        mi_heap_area_ex_t xarea;
        nuint bsize = mi_page_block_size(page);

        xarea.page = page;
        xarea.area.reserved = page->reserved * bsize;
        xarea.area.committed = page->capacity * bsize;
        xarea.area.blocks = mi_page_start(page);
        xarea.area.used = page->used;
        xarea.area.block_size = bsize;
        xarea.area.heap_tag = page->heap_tag;

        return fun(heap, &xarea, arg);
    }

    // Visit all heap pages as areas
    private static bool mi_heap_visit_areas([NativeTypeName("const mi_heap_t*")] mi_heap_t* heap, [NativeTypeName("mi_heap_area_visit_fun*")] mi_heap_area_visit_fun visitor, void* arg)
    {
        if (visitor == null)
        {
            _mi_error_message(EINVAL, "mi_abandoned_visit_blocks: visitor must not be null\n");
            return false;
        }

        // note: function pointer to void* :-{
        GCHandle handle = GCHandle.Alloc(visitor);

        try
        {
            return mi_heap_visit_pages(heap, mi_heap_visit_areas_page, true, (void*)GCHandle.ToIntPtr(handle), arg);
        }
        finally
        {
            handle.Free();
        }
    }

    private static bool mi_heap_area_visitor([NativeTypeName("const mi_heap_t*")] mi_heap_t* heap, [NativeTypeName("const mi_heap_area_ex_t*")] mi_heap_area_ex_t* xarea, void* arg)
    {
        mi_visit_blocks_args_t* args = (mi_visit_blocks_args_t*)arg;

        GCHandle handle = GCHandle.FromIntPtr((IntPtr)args->visitor);
        mi_block_visit_fun visitor = (mi_block_visit_fun)handle.Target!;

        if (!visitor((IntPtr)heap, &xarea->area, null, xarea->area.block_size, args->arg))
        {
            return false;
        }

        if (args->visit_blocks)
        {
            return mi_heap_area_visit_blocks(xarea, visitor, args->arg);
        }
        else
        {
            return true;
        }
    }

    // Visit all blocks in a heap
    private static bool mi_heap_visit_blocks([NativeTypeName("const mi_heap_t*")] mi_heap_t* heap, bool visit_blocks, [NativeTypeName("mi_block_visit_fun*")] mi_block_visit_fun visitor, void* arg)
    {
        // note: function pointer to void* :-{
        GCHandle handle = GCHandle.Alloc(visitor);

        try
        {
            mi_visit_blocks_args_t args = new mi_visit_blocks_args_t {
                visit_blocks = visit_blocks,
                visitor = (void*)GCHandle.ToIntPtr(handle),
                arg = arg,
            };

            return mi_heap_visit_areas(heap, mi_heap_area_visitor, &args);
        }
        finally
        {
            handle.Free();
        }
    }

    private static bool mi_segment_visit_page(mi_page_t* page, bool visit_blocks, mi_block_visit_fun visitor, void* arg)
    {
        nuint block_size = mi_page_block_size(page);
        mi_heap_area_ex_t area = default;
        area.page = page;
        area.area.reserved = page->reserved * block_size;
        area.area.committed = page->capacity * block_size;
        area.area.blocks = mi_page_start(page);
        area.area.used = page->used;
        area.area.block_size = block_size;
        area.area.heap_tag = page->heap_tag;

        if (!visitor(IntPtr.Zero, &area.area, null, block_size, arg))
        {
            return false;
        }

        return !visit_blocks || mi_heap_area_visit_blocks(&area, visitor, arg);
    }

    private static bool _mi_segment_visit_blocks(mi_segment_t* segment, int heap_tag, bool visit_blocks, mi_block_visit_fun visitor, void* arg)
    {
        for (nuint i = 0; i < segment->capacity; i++)
        {
            mi_page_t* page = &segment->pages.e0 + (nint)i;
            if (page->segment_in_use && ((heap_tag < 0) || (page->heap_tag == (byte)heap_tag)))
            {
                if (!mi_segment_visit_page(page, visit_blocks, visitor, arg))
                {
                    return false;
                }
            }
        }

        return true;
    }

    public static partial bool mi_abandoned_visit_blocks(IntPtr subproc_id, int heap_tag, bool visit_blocks, mi_block_visit_fun visitor, void* arg)
    {
        if (!mi_option_is_enabled(mi_option_t.mi_option_visit_abandoned))
        {
            _mi_error_message(EFAULT, "internal error: can only visit abandoned blocks when MIMALLOC_VISIT_ABANDONED=ON\n");
            return false;
        }

        if (visitor == null)
        {
            return false;
        }

        mi_subproc_t* subproc = _mi_subproc_from_id(subproc_id);
        _mi_arena_field_cursor_init(null, subproc, true, out mi_arena_field_cursor_t current);
        bool ok = true;

        try
        {
            mi_segment_t* segment;
            while (ok && ((segment = _mi_arena_segment_clear_abandoned_next(ref current)) != null))
            {
                try
                {
                    ok = _mi_segment_visit_blocks(segment, heap_tag, visit_blocks, visitor, arg);
                }
                finally
                {
                    _mi_arena_segment_mark_abandoned(segment);
                }
            }
        }
        finally
        {
            _mi_arena_field_cursor_done(ref current);
        }

        return ok;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial bool mi_heap_visit_blocks(IntPtr heap, bool visit_blocks, mi_block_visit_fun visitor, void* arg) => mi_heap_visit_blocks((mi_heap_t*)heap, visit_blocks, visitor, arg);
}
