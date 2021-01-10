// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the page.c file from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

using System;
using System.Runtime.CompilerServices;
using static TerraFX.Interop.mi_delayed_t;
using static TerraFX.Interop.mi_page_kind_t;

namespace TerraFX.Interop
{
    public static unsafe partial class Mimalloc
    {
        /* -----------------------------------------------------------
          Page helpers
        ----------------------------------------------------------- */

        // Index a block in a page
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static mi_block_t* mi_page_block_at([NativeTypeName("const mi_page_t*")] mi_page_t* page, void* page_start, [NativeTypeName("size_t")] nuint block_size, [NativeTypeName("size_t")] nuint i)
        {
            mi_assert_internal((MI_DEBUG > 1) && (page != null));
            mi_assert_internal((MI_DEBUG > 1) && (i <= page->reserved));

            return (mi_block_t*)((byte*)page_start + (i * block_size));
        }

        private static partial void mi_page_init(mi_heap_t* heap, mi_page_t* page, [NativeTypeName("size_t")] nuint size, mi_tld_t* tld);

        private static partial void mi_page_extend_free(mi_heap_t* heap, mi_page_t* page, mi_tld_t* tld);

        [return: NativeTypeName("size_t")]
        private static nuint mi_page_list_count(mi_page_t* page, mi_block_t* head)
        {
            mi_assert_internal((MI_DEBUG > 1) && (MI_DEBUG >= 3));
            nuint count = 0;

            while (head != null)
            {
                mi_assert_internal((MI_DEBUG > 1) && (page == _mi_ptr_page(head)));
                count++;
                head = mi_block_next(page, head);
            }

            return count;
        }

        // Start of the page available memory
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [return: NativeTypeName("uint8_t*")]
        private static byte* mi_page_area([NativeTypeName("const mi_page_t*")] mi_page_t* page)
        {
            mi_assert_internal((MI_DEBUG > 1) && (MI_DEBUG >= 3));
            return _mi_page_start(_mi_page_segment(page), page, out _);
        }

        private static bool mi_page_list_is_valid(mi_page_t* page, mi_block_t* p)
        {
            mi_assert_internal((MI_DEBUG > 1) && (MI_DEBUG >= 3));
            byte* page_area = _mi_page_start(_mi_page_segment(page), page, out nuint psize);

            mi_block_t* start = (mi_block_t*)page_area;
            mi_block_t* end = (mi_block_t*)(page_area + psize);

            while (p != null)
            {
                if (p < start || p >= end)
                {
                    return false;
                }
                p = mi_block_next(page, p);
            }

            return true;
        }

        private static bool mi_page_is_valid_init(mi_page_t* page)
        {
            mi_assert_internal((MI_DEBUG > 1) && (MI_DEBUG >= 3));
            mi_assert_internal((MI_DEBUG > 1) && (page->xblock_size > 0));
            mi_assert_internal((MI_DEBUG > 1) && (page->used <= page->capacity));
            mi_assert_internal((MI_DEBUG > 1) && (page->capacity <= page->reserved));

            nuint bsize = mi_page_block_size(page);

            mi_segment_t* segment = _mi_page_segment(page);
            byte* start = _mi_page_start(segment, page, out _);

            mi_assert_internal((MI_DEBUG > 1) && (start == _mi_segment_page_start(segment, page, bsize, out _, out _)));
            // mi_assert_internal((MI_DEBUG > 1) && ((start + page->capacity * page->block_size) == page->top));

            mi_assert_internal((MI_DEBUG > 1) && mi_page_list_is_valid(page, page->free));
            mi_assert_internal((MI_DEBUG > 1) && mi_page_list_is_valid(page, page->local_free));

            mi_block_t* tfree = mi_page_thread_free(page);
            mi_assert_internal((MI_DEBUG > 1) && mi_page_list_is_valid(page, tfree));

            // nuint tfree_count = mi_page_list_count(page, tfree);
            // mi_assert_internal((MI_DEBUG > 1) && (tfree_count <= page->thread_freed + 1));

            nuint free_count = mi_page_list_count(page, page->free) + mi_page_list_count(page, page->local_free);
            mi_assert_internal((MI_DEBUG > 1) && (page->used + free_count == page->capacity));

            return true;
        }

        private static partial bool _mi_page_is_valid(mi_page_t* page)
        {
            mi_assert_internal((MI_DEBUG > 1) && (MI_DEBUG >= 3));
            mi_assert_internal((MI_DEBUG > 1) && mi_page_is_valid_init(page));

            if (MI_SECURE != 0)
            {
                mi_assert_internal((MI_DEBUG > 1) && (page->keys.e0 != 0));
            }

            if (mi_page_heap(page) != null)
            {
                mi_segment_t* segment = _mi_page_segment(page);
                mi_assert_internal((MI_DEBUG > 1) && ((segment->thread_id == mi_page_heap(page)->thread_id) || (segment->thread_id == 0)));

                if (segment->page_kind != MI_PAGE_HUGE)
                {
                    mi_page_queue_t* pq = mi_page_queue_of(page);
                    mi_assert_internal((MI_DEBUG > 1) && mi_page_queue_contains(pq, page));

                    mi_assert_internal((MI_DEBUG > 1) && ((pq->block_size == mi_page_block_size(page)) || (mi_page_block_size(page) > MI_LARGE_OBJ_SIZE_MAX) || mi_page_is_in_full(page)));
                    mi_assert_internal((MI_DEBUG > 1) && mi_heap_contains_queue(mi_page_heap(page), pq));
                }
            }

            return true;
        }

        private static partial void _mi_page_use_delayed_free(mi_page_t* page, mi_delayed_t delay, bool override_never)
        {
#pragma warning disable CS0420
            nuint tfreex;
            mi_delayed_t old_delay;
            nuint tfree;

            do
            {
                // note: must acquire as we can break/repeat this loop and not do a CAS;
                tfree = mi_atomic_load_acquire(ref page->xthread_free);

                tfreex = mi_tf_set_delayed(tfree, delay);
                old_delay = mi_tf_delayed(tfree);

                if (mi_unlikely(old_delay == MI_DELAYED_FREEING))
                {
                    // delay until outstanding MI_DELAYED_FREEING are done.
                    mi_atomic_yield();

                    // will cause CAS to busy fail
                    // tfree = mi_tf_set_delayed(tfree, MI_NO_DELAYED_FREE);
                }
                else if (delay == old_delay)
                {
                    // avoid atomic operation if already equal
                    break;
                }
                else if (!override_never && (old_delay == MI_NEVER_DELAYED_FREE))
                {
                    // leave never-delayed flag set
                    break;
                }
            }
            while ((old_delay == MI_DELAYED_FREEING) || !mi_atomic_cas_weak_release(ref page->xthread_free, ref tfree, tfreex));
#pragma warning restore CS0420
        }

        /* -----------------------------------------------------------
          Page collect the `local_free` and `thread_free` lists
        ----------------------------------------------------------- */

        // Collect the local `thread_free` list using an atomic exchange.
        // Note: The exchange must be done atomically as this is used right after
        // moving to the full list in `mi_page_collect_ex` and we need to
        // ensure that there was no race where the page became unfull just before the move.
        private static void _mi_page_thread_free_collect(mi_page_t* page)
        {
#pragma warning disable CS0420
            mi_block_t* head;

            nuint tfreex;
            nuint tfree = mi_atomic_load_relaxed(ref page->xthread_free);

            do
            {
                head = mi_tf_block(tfree);
                tfreex = mi_tf_set_block(tfree, null);
            }
            while (!mi_atomic_cas_weak_acq_rel(ref page->xthread_free, ref tfree, tfreex));

            if (head == null)
            {
                // return if the list is empty
                return;
            }

            // find the tail -- also to get a proper count (without data races)

            // cannot collect more than capacity
            uint max_count = page->capacity;

            uint count = 1;

            mi_block_t* tail = head;
            mi_block_t* next;

            while (((next = mi_block_next(page, tail)) != null) && (count <= max_count))
            {
                count++;
                tail = next;
            }

            if (count > max_count)
            {
                // if `count > max_count` there was a memory corruption (possibly infinite list due to double multi-threaded free)
                _mi_error_message(EFAULT, "corrupted thread-free list\n");

                // the thread-free items cannot be freed
                return;
            }

            // and append the current local free list
            mi_block_set_next(page, tail, page->local_free);
            page->local_free = head;

            // update counts now
            page->used -= count;
#pragma warning restore CS0420
        }

        private static partial void _mi_page_free_collect(mi_page_t* page, bool force)
        {
            mi_assert_internal((MI_DEBUG > 1) && (page != null));

            // collect the thread free list
            if (force || (mi_page_thread_free(page) != null))
            {
                // quick test to avoid an atomic operation
                _mi_page_thread_free_collect(page);
            }

            // and the local free list
            if (page->local_free != null)
            {
                if (mi_likely(page->free == null))
                {
                    // usual case
                    page->free = page->local_free;
                    page->local_free = null;
                    page->is_zero = false;
                }
                else if (force)
                {
                    // append -- only on shutdown (force) as this is a linear operation

                    mi_block_t* tail = page->local_free;
                    mi_block_t* next;

                    while ((next = mi_block_next(page, tail)) != null)
                    {
                        tail = next;
                    }

                    mi_block_set_next(page, tail, page->free);
                    page->free = page->local_free;

                    page->local_free = null;
                    page->is_zero = false;
                }
            }

            mi_assert_internal((MI_DEBUG > 1) && (!force || (page->local_free == null)));
        }

        /* -----------------------------------------------------------
          Page fresh and retire
        ----------------------------------------------------------- */

        // called from segments when reclaiming abandoned pages
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static partial void _mi_page_reclaim(mi_heap_t* heap, mi_page_t* page)
        {
            mi_assert_expensive((MI_DEBUG > 2) && mi_page_is_valid_init(page));
            mi_assert_internal((MI_DEBUG > 1) && (mi_page_heap(page) == heap));
            mi_assert_internal((MI_DEBUG > 1) && (mi_page_thread_free_flag(page) != MI_NEVER_DELAYED_FREE));
            mi_assert_internal((MI_DEBUG > 1) && (_mi_page_segment(page)->page_kind != MI_PAGE_HUGE));
            mi_assert_internal((MI_DEBUG > 1) && (!page->is_reset));

            // TODO: push on full queue immediately if it is full?

            mi_page_queue_t* pq = mi_page_queue(heap, mi_page_block_size(page));
            mi_page_queue_push(heap, pq, page);

            mi_assert_expensive((MI_DEBUG > 2) && _mi_page_is_valid(page));
        }

        // allocate a fresh page from a segment
        private static mi_page_t* mi_page_fresh_alloc(mi_heap_t* heap, mi_page_queue_t* pq, [NativeTypeName("size_t")] nuint block_size)
        {
            mi_assert_internal((MI_DEBUG > 1) && ((pq == null) || mi_heap_contains_queue(heap, pq)));
            mi_assert_internal((MI_DEBUG > 1) && ((pq == null) || (block_size == pq->block_size)));

            mi_page_t* page = _mi_segment_page_alloc(heap, block_size, &heap->tld->segments, &heap->tld->os);

            if (page == null)
            {
                // this may be out-of-memory, or an abandoned page was reclaimed (and in our queue)
                return null;
            }

            // a fresh page was found, initialize it
            mi_assert_internal((MI_DEBUG > 1) && (pq == null || _mi_page_segment(page)->page_kind != MI_PAGE_HUGE));

            mi_page_init(heap, page, block_size, heap->tld);
            _mi_stat_increase(ref heap->tld->stats.pages, 1);

            if (pq != null)
            {
                // huge pages use pq==null
                mi_page_queue_push(heap, pq, page);
            }

            mi_assert_expensive((MI_DEBUG > 2) && _mi_page_is_valid(page));
            return page;
        }

        // Get a fresh page to use
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static mi_page_t* mi_page_fresh(mi_heap_t* heap, mi_page_queue_t* pq)
        {
            mi_assert_internal((MI_DEBUG > 1) && mi_heap_contains_queue(heap, pq));
            mi_page_t* page = mi_page_fresh_alloc(heap, pq, pq->block_size);

            if (page == null)
            {
                return null;
            }

            mi_assert_internal((MI_DEBUG > 1) && (pq->block_size == mi_page_block_size(page)));
            mi_assert_internal((MI_DEBUG > 1) && (pq == mi_page_queue(heap, mi_page_block_size(page))));

            return page;
        }

        /* -----------------------------------------------------------
           Do any delayed frees
           (put there by other threads if they deallocated in a full page)
        ----------------------------------------------------------- */
        private static partial void _mi_heap_delayed_free(mi_heap_t* heap)
        {
#pragma warning disable CS0420
            // take over the list (note: no atomic exchange since it is often null)
            nuint block = (nuint)mi_atomic_load_ptr_relaxed<mi_block_t>(ref heap->thread_delayed_free);

            while ((block != 0) && !mi_atomic_cas_ptr_weak_acq_rel<mi_block_t>(ref heap->thread_delayed_free, ref block, null))
            {
                /* nothing */
            }

            // and free them all
            while (block != 0)
            {
                mi_block_t* next = mi_block_nextx(heap, (mi_block_t*)block, &heap->keys.e0);

                // use internal free instead of regular one to keep stats etc correct
                if (!_mi_free_delayed_block((mi_block_t*)block))
                {
                    // we might already start delayed freeing while another thread has not yet
                    // reset the delayed_freeing flag; in that case delay it further by reinserting.
                    nuint dfree = (nuint)mi_atomic_load_ptr_relaxed<mi_block_t>(ref heap->thread_delayed_free);

                    do
                    {
                        mi_block_set_nextx(heap, (mi_block_t*)block, (mi_block_t*)dfree, &heap->keys.e0);
                    }
                    while (!mi_atomic_cas_ptr_weak_release(ref heap->thread_delayed_free, ref dfree, (mi_block_t*)block));
                }

                block = (nuint)next;
            }
#pragma warning restore CS0420
        }

        /* -----------------------------------------------------------
          Unfull, abandon, free and retire
        ----------------------------------------------------------- */

        // Move a page from the full list back to a regular list
        private static partial void _mi_page_unfull(mi_page_t* page)
        {
            mi_assert_internal((MI_DEBUG > 1) && (page != null));
            mi_assert_expensive((MI_DEBUG > 2) && _mi_page_is_valid(page));
            mi_assert_internal((MI_DEBUG > 1) && mi_page_is_in_full(page));

            if (!mi_page_is_in_full(page))
            {
                return;
            }

            mi_heap_t* heap = mi_page_heap(page);
            mi_page_queue_t* pqfull = &heap->pages.e0 + MI_BIN_FULL;

            // to get the right queue
            mi_page_set_in_full(page, false);

            mi_page_queue_t* pq = mi_heap_page_queue_of(heap, page);
            mi_page_set_in_full(page, true);

            mi_page_queue_enqueue_from(pq, pqfull, page);
        }

        private static void mi_page_to_full(mi_page_t* page, mi_page_queue_t* pq)
        {
            mi_assert_internal((MI_DEBUG > 1) && (pq == mi_page_queue_of(page)));
            mi_assert_internal((MI_DEBUG > 1) && (!mi_page_immediate_available(page)));
            mi_assert_internal((MI_DEBUG > 1) && (!mi_page_is_in_full(page)));

            if (mi_page_is_in_full(page))
            {
                return;
            }

            mi_page_queue_enqueue_from(&mi_page_heap(page)->pages.e0 + MI_BIN_FULL, pq, page);

            // try to collect right away in case another thread freed just before MI_USE_DELAYED_FREE was set
            _mi_page_free_collect(page, false);
        }

        // Abandon a page with used blocks at the end of a thread.
        // Note: only call if it is ensured that no references exist from
        // the `page->heap->thread_delayed_free` into this page.
        // Currently only called through `mi_heap_collect_ex` which ensures this.
        private static partial void _mi_page_abandon(mi_page_t* page, mi_page_queue_t* pq)
        {
            mi_assert_internal((MI_DEBUG > 1) && (page != null));
            mi_assert_expensive((MI_DEBUG > 2) && _mi_page_is_valid(page));
            mi_assert_internal((MI_DEBUG > 1) && (pq == mi_page_queue_of(page)));
            mi_assert_internal((MI_DEBUG > 1) && (mi_page_heap(page) != null));

            mi_heap_t* pheap = mi_page_heap(page);

            // remove from our page list
            mi_segments_tld_t* segments_tld = &pheap->tld->segments;
            mi_page_queue_remove(pq, page);

            // page is no longer associated with our heap
            mi_assert_internal((MI_DEBUG > 1) && (mi_page_thread_free_flag(page) == MI_NEVER_DELAYED_FREE));
            mi_page_set_heap(page, null);

            if (MI_DEBUG > 1)
            {
                // check there are no references left..
                for (mi_block_t* block = (mi_block_t*)pheap->thread_delayed_free; block != null; block = mi_block_nextx(pheap, block, &pheap->keys.e0))
                {
                    mi_assert_internal((MI_DEBUG > 1) && (_mi_ptr_page(block) != page));
                }
            }

            // and abandon it
            mi_assert_internal((MI_DEBUG > 1) && (mi_page_heap(page) == null));
            _mi_segment_page_abandon(page, segments_tld);
        }

        // Free a page with no more free blocks
        private static partial void _mi_page_free(mi_page_t* page, mi_page_queue_t* pq, bool force)
        {
            mi_assert_internal((MI_DEBUG > 1) && (page != null));
            mi_assert_expensive((MI_DEBUG > 2) && _mi_page_is_valid(page));
            mi_assert_internal((MI_DEBUG > 1) && (pq == mi_page_queue_of(page)));
            mi_assert_internal((MI_DEBUG > 1) && mi_page_all_free(page));
            mi_assert_internal((MI_DEBUG > 1) && (mi_page_thread_free_flag(page) != MI_DELAYED_FREEING));

            // no more aligned blocks in here
            mi_page_set_has_aligned(page, false);

            // remove from the page list
            // (no need to do _mi_heap_delayed_free first as all blocks are already free)
            mi_segments_tld_t* segments_tld = &mi_page_heap(page)->tld->segments;
            mi_page_queue_remove(pq, page);

            // and free it
            mi_page_set_heap(page, null);
            _mi_segment_page_free(page, force, segments_tld);
        }

        private const byte MI_RETIRE_CYCLES = 8;

        // Retire a page with no more used blocks
        // Important to not retire too quickly though as new
        // allocations might coming.
        // Note: called from `mi_free` and benchmarks often
        // trigger this due to freeing everything and then
        // allocating again so careful when changing this.
        private static partial void _mi_page_retire(mi_page_t* page)
        {
            mi_assert_internal((MI_DEBUG > 1) && (page != null));
            mi_assert_expensive((MI_DEBUG > 2) && _mi_page_is_valid(page));
            mi_assert_internal((MI_DEBUG > 1) && mi_page_all_free(page));

            mi_page_set_has_aligned(page, false);

            // don't retire too often..
            // (or we end up retiring and re-allocating most of the time)
            // NOTE: refine this more: we should not retire if this
            // is the only page left with free blocks. It is not clear
            // how to check this efficiently though...
            // for now, we don't retire if it is the only page left of this size class.
            mi_page_queue_t* pq = mi_page_queue_of(page);

            if (mi_likely((page->xblock_size <= MI_MAX_RETIRE_SIZE) && !mi_page_is_in_full(page)))
            {
                if (pq->last == page && pq->first == page)
                {
                    // the only page in the queue?
                    mi_stat_counter_increase(ref _mi_stats_main.page_no_retire, 1);

                    page->retire_expire = (page->xblock_size <= MI_SMALL_OBJ_SIZE_MAX) ? MI_RETIRE_CYCLES : (MI_RETIRE_CYCLES / 4);

                    mi_heap_t* heap = mi_page_heap(page);
                    mi_assert_internal((MI_DEBUG > 1) && (pq >= &heap->pages.e0));

                    nuint index = (nuint)(pq - &heap->pages.e0);
                    mi_assert_internal((MI_DEBUG > 1) && index < MI_BIN_FULL && index < MI_BIN_HUGE);

                    if (index < heap->page_retired_min)
                    {
                        heap->page_retired_min = index;
                    }

                    if (index > heap->page_retired_max)
                    {
                        heap->page_retired_max = index;
                    }

                    mi_assert_internal((MI_DEBUG > 1) && mi_page_all_free(page));

                    // dont't free after all
                    return;
                }
            }

            _mi_page_free(page, pq, false);
        }

        // free retired pages: we don't need to look at the entire queues
        // since we only retire pages that are at the head position in a queue.
        private static partial void _mi_heap_collect_retired(mi_heap_t* heap, bool force)
        {
            nuint min = MI_BIN_FULL;
            nuint max = 0;

            for (nuint bin = heap->page_retired_min; bin <= heap->page_retired_max; bin++)
            {
                mi_page_queue_t* pq = &heap->pages.e0 + bin;
                mi_page_t* page = pq->first;

                if ((page != null) && (page->retire_expire != 0))
                {
                    if (mi_page_all_free(page))
                    {
                        page->retire_expire--;

                        if (force || (page->retire_expire == 0))
                        {
                            _mi_page_free(pq->first, pq, force);
                        }
                        else
                        {
                            // keep retired, update min/max

                            if (bin < min)
                            {
                                min = bin;
                            }

                            if (bin > max)
                            {
                                max = bin;
                            }
                        }
                    }
                    else
                    {
                        page->retire_expire = 0;
                    }
                }
            }

            heap->page_retired_min = min;
            heap->page_retired_max = max;
        }

        /* -----------------------------------------------------------
          Initialize the initial free list in a page.
          In secure mode we initialize a randomized list by
          alternating between slices.
        ----------------------------------------------------------- */

        // at most 64 slices
        private const int MI_MAX_SLICE_SHIFT = 6;

        private const nuint MI_MIN_SLICES = 2;

        private static void mi_page_free_list_extend_secure([NativeTypeName("mi_heap_t* const")] mi_heap_t* heap, [NativeTypeName("mi_page_t* const")] mi_page_t* page, [NativeTypeName("const size_t")] nuint bsize, [NativeTypeName("const size_t")] nuint extend, [NativeTypeName("mi_stats_t* const")] in mi_stats_t stats)
        {
            if (MI_SECURE <= 2)
            {
                mi_assert_internal((MI_DEBUG > 1) && (page->free == null));
                mi_assert_internal((MI_DEBUG > 1) && (page->local_free == null));
            }

            mi_assert_internal((MI_DEBUG > 1) && (page->capacity + extend <= page->reserved));
            mi_assert_internal((MI_DEBUG > 1) && (bsize == mi_page_block_size(page)));

            void* page_area = _mi_page_start(_mi_page_segment(page), page, out _);

            // initialize a randomized free list
            // set up `slice_count` slices to alternate between
            nuint shift = MI_MAX_SLICE_SHIFT;

            while ((extend >> (int)shift) == 0)
            {
                shift--;
            }

            nuint slice_count = (nuint)1 << (int)shift;
            nuint slice_extend = extend / slice_count;

            mi_assert_internal((MI_DEBUG > 1) && (slice_extend >= 1));

            // current start of the slice
            mi_block_t** blocks = stackalloc mi_block_t*[(int)MI_MAX_SLICES];

            // available objects in the slice
            nuint* counts = stackalloc nuint[(int)MI_MAX_SLICES];

            for (nuint i = 0; i < slice_count; i++)
            {
                blocks[i] = mi_page_block_at(page, page_area, bsize, page->capacity + (i * slice_extend));
                counts[i] = slice_extend;
            }

            // final slice holds the modulus too (todo: distribute evenly?)
            counts[slice_count - 1] += extend % slice_count;

            // and initialize the free list by randomly threading through them
            // set up first element

            nuint r = _mi_heap_random_next(heap);
            nuint current = r % slice_count;

            counts[current]--;
            mi_block_t* free_start = blocks[current];

            // and iterate through the rest; use `random_shuffle` for performance

            // ensure not 0
            nuint rnd = _mi_random_shuffle(r | 1);

            for (nuint i = 1; i < extend; i++)
            {
                // call random_shuffle only every INTPTR_SIZE rounds
                nuint round = i % MI_INTPTR_SIZE;

                if (round == 0)
                {
                    rnd = _mi_random_shuffle(rnd);
                }

                // select a random next slice index
                nuint next = (rnd >> (int)(8 * round)) & (slice_count - 1);

                while (counts[next] == 0)
                {
                    // ensure it still has space
                    next++;

                    if (next == slice_count)
                    {
                        next = 0;
                    }
                }

                // and link the current block to it
                counts[next]--;

                mi_block_t* block = blocks[current];

                // bump to the following block
                blocks[current] = (mi_block_t*)((byte*)block + bsize);

                // and set next; note: we may have `current == next`
                mi_block_set_next(page, block, blocks[next]);

                current = next;
            }

            // prepend to the free list (usually null)

            // end of the list
            mi_block_set_next(page, blocks[current], page->free);

            page->free = free_start;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void mi_page_free_list_extend([NativeTypeName("mi_page_t* const")] mi_page_t* page, [NativeTypeName("const size_t")] nuint bsize, [NativeTypeName("const size_t")] nuint extend, [NativeTypeName("mi_stats_t* const")] in mi_stats_t stats)
        {
            if (MI_SECURE <= 2)
            {
                mi_assert_internal((MI_DEBUG > 1) && (page->free == null));
                mi_assert_internal((MI_DEBUG > 1) && (page->local_free == null));
            }

            mi_assert_internal((MI_DEBUG > 1) && (page->capacity + extend <= page->reserved));
            mi_assert_internal((MI_DEBUG > 1) && (bsize == mi_page_block_size(page)));

            void* page_area = _mi_page_start(_mi_page_segment(page), page, out _);

            mi_block_t* start = mi_page_block_at(page, page_area, bsize, page->capacity);

            // initialize a sequential free list
            mi_block_t* last = mi_page_block_at(page, page_area, bsize, page->capacity + extend - 1);

            mi_block_t* block = start;

            while (block <= last)
            {
                mi_block_t* next = (mi_block_t*)((byte*)block + bsize);
                mi_block_set_next(page, block, next);
                block = next;
            }

            // prepend to free list (usually `null`)
            mi_block_set_next(page, last, page->free);

            page->free = start;
        }

        /* -----------------------------------------------------------
          Page initialize and extend the capacity
        ----------------------------------------------------------- */

        // heuristic, one OS page seems to work well.
        private const nuint MI_MAX_EXTEND_SIZE = 4 * 1024;

        // Extend the capacity (up to reserved) by initializing a free list
        // We do at most `MI_MAX_EXTEND` to avoid touching too much memory
        // Note: we also experimented with "bump" allocation on the first
        // allocations but this did not speed up any benchmark (due to an
        // extra test in malloc? or cache effects?)
        private static partial void mi_page_extend_free(mi_heap_t* heap, mi_page_t* page, mi_tld_t* tld)
        {
            mi_assert_expensive((MI_DEBUG > 2) && mi_page_is_valid_init(page));

            if (MI_SECURE <= 2)
            {
                mi_assert((MI_DEBUG != 0) && (page->free == null));
                mi_assert((MI_DEBUG != 0) && (page->local_free == null));

                if (page->free != null)
                {
                    return;
                }
            }

            if (page->capacity >= page->reserved)
            {
                return;
            }

            _mi_page_start(_mi_page_segment(page), page, out nuint page_size);
            mi_stat_counter_increase(ref tld->stats.pages_extended, 1);

            // calculate the extend count
            nuint bsize = (page->xblock_size < MI_HUGE_BLOCK_SIZE) ? page->xblock_size : page_size;

            nuint extend = (nuint)(page->reserved - page->capacity);
            nuint max_extend = (bsize >= MI_MAX_EXTEND_SIZE) ? MI_MIN_EXTEND : (MI_MAX_EXTEND_SIZE / (uint)bsize);

            if (max_extend < MI_MIN_EXTEND)
            {
                max_extend = MI_MIN_EXTEND;
            }

            if (extend > max_extend)
            {
                // ensure we don't touch memory beyond the page to reduce page commit.
                // the `lean` benchmark tests this. Going from 1 to 8 increases rss by 50%.
                extend = (max_extend == 0) ? 1 : max_extend;
            }

            mi_assert_internal((MI_DEBUG > 1) && (extend > 0) && ((extend + page->capacity) <= page->reserved));
            mi_assert_internal((MI_DEBUG > 1) && (extend < ((nuint)1 << 16)));

            // and append the extend the free list
            if ((extend < MI_MIN_SLICES) || (MI_SECURE == 0))
            {
                mi_page_free_list_extend(page, bsize, extend, in tld->stats);
            }
            else
            {
                mi_page_free_list_extend_secure(heap, page, bsize, extend, in tld->stats);
            }

            // enable the new free list
            page->capacity += (ushort)extend;

            mi_stat_increase(ref tld->stats.page_committed, extend * bsize);

            // extension into zero initialized memory preserves the zero'd free list
            if (!page->is_zero_init)
            {
                page->is_zero = false;
            }

            mi_assert_expensive((MI_DEBUG > 2) && mi_page_is_valid_init(page));
        }

        // Initialize a fresh page
        private static partial void mi_page_init(mi_heap_t* heap, mi_page_t* page, nuint block_size, mi_tld_t* tld)
        {
            mi_assert((MI_DEBUG != 0) && (page != null));
            mi_segment_t* segment = _mi_page_segment(page);

            mi_assert((MI_DEBUG != 0) && (segment != null));
            mi_assert_internal((MI_DEBUG > 1) && (block_size > 0));

            // set fields
            mi_page_set_heap(page, heap);

            _mi_segment_page_start(segment, page, block_size, out nuint page_size, out _);
            page->xblock_size = (block_size < MI_HUGE_BLOCK_SIZE) ? (uint)block_size : MI_HUGE_BLOCK_SIZE;

            mi_assert_internal((MI_DEBUG > 1) && (page_size / block_size < (1 << 16)));
            page->reserved = (ushort)(page_size / block_size);

            if (MI_ENCODE_FREELIST != 0)
            {
                page->keys.e0 = _mi_heap_random_next(heap);
                page->keys.e1 = _mi_heap_random_next(heap);
            }

            page->is_zero = page->is_zero_init;

            mi_assert_internal((MI_DEBUG > 1) && (page->capacity == 0));
            mi_assert_internal((MI_DEBUG > 1) && (page->free == null));
            mi_assert_internal((MI_DEBUG > 1) && (page->used == 0));
            mi_assert_internal((MI_DEBUG > 1) && (page->xthread_free == 0));
            mi_assert_internal((MI_DEBUG > 1) && (page->next == null));
            mi_assert_internal((MI_DEBUG > 1) && (page->prev == null));
            mi_assert_internal((MI_DEBUG > 1) && (page->retire_expire == 0));
            mi_assert_internal((MI_DEBUG > 1) && (!mi_page_has_aligned(page)));

            if (MI_ENCODE_FREELIST != 0)
            {
                mi_assert_internal((MI_DEBUG > 1) && (page->keys.e0 != 0));
                mi_assert_internal((MI_DEBUG > 1) && (page->keys.e1 != 0));
            }

            mi_assert_expensive((MI_DEBUG > 2) && mi_page_is_valid_init(page));

            // initialize an initial free list
            mi_page_extend_free(heap, page, tld);

            mi_assert((MI_DEBUG != 0) && mi_page_immediate_available(page));
        }

        /* -----------------------------------------------------------
          Find pages with free blocks
        -------------------------------------------------------------*/

        // Find a page with free blocks of `page->block_size`.
        private static mi_page_t* mi_page_queue_find_free_ex(mi_heap_t* heap, mi_page_queue_t* pq, bool first_try)
        {
            // search through the pages in "next fit" order
            nuint count = 0;

            mi_page_t* page = pq->first;

            while (page != null)
            {
                // remember next
                mi_page_t* next = page->next;

                count++;

                // 0. collect freed blocks by us and other threads
                _mi_page_free_collect(page, false);

                // 1. if the page contains free blocks, we are done
                if (mi_page_immediate_available(page))
                {
                    // pick this one
                    break;
                }

                // 2. Try to extend
                if (page->capacity < page->reserved)
                {
                    mi_page_extend_free(heap, page, heap->tld);
                    mi_assert_internal((MI_DEBUG > 1) && mi_page_immediate_available(page));
                    break;
                }

                // 3. If the page is completely full, move it to the `mi_pages_full`
                // queue so we don't visit long-lived pages too often.
                mi_assert_internal((MI_DEBUG > 1) && !mi_page_is_in_full(page) && !mi_page_immediate_available(page));
                mi_page_to_full(page, pq);

                page = next;
            }

            mi_stat_counter_increase(ref heap->tld->stats.searches, count);

            if (page == null)
            {
                // perhaps make a page available
                _mi_heap_collect_retired(heap, false);

                page = mi_page_fresh(heap, pq);

                if (page == null && first_try)
                {
                    // out-of-memory _or_ an abandoned page with free blocks was reclaimed, try once again
                    page = mi_page_queue_find_free_ex(heap, pq, false);
                }
            }
            else
            {
                mi_assert((MI_DEBUG != 0) && (pq->first == page));
                page->retire_expire = 0;
            }

            mi_assert_internal((MI_DEBUG > 1) && ((page == null) || mi_page_immediate_available(page)));
            return page;
        }

        // Find a page with free blocks of `size`.
        private static mi_page_t* mi_find_free_page(mi_heap_t* heap, [NativeTypeName("size_t")] nuint size)
        {
            mi_page_queue_t* pq = mi_page_queue(heap, size);
            mi_page_t* page = pq->first;

            if (page != null)
            {
                if ((MI_SECURE >= 3) && (page->capacity < page->reserved) && ((_mi_heap_random_next(heap) & 1) == 1))
                {
                    // in secure mode, we extend half the time to increase randomness
                    mi_page_extend_free(heap, page, heap->tld);

                    mi_assert_internal((MI_DEBUG > 1) && mi_page_immediate_available(page));
                }
                else
                {
                    _mi_page_free_collect(page, false);
                }

                if (mi_page_immediate_available(page))
                {
                    page->retire_expire = 0;

                    // fast path
                    return page;
                }
            }

            return mi_page_queue_find_free_ex(heap, pq, true);
        }

        /* -----------------------------------------------------------
          Users can register a deferred free function called
          when the `free` list is empty. Since the `local_free`
          is separate this is deterministically called after
          a certain number of allocations.
        ----------------------------------------------------------- */

        private static partial void _mi_deferred_free(mi_heap_t* heap, bool force)
        {
            heap->tld->heartbeat++;

            if ((deferred_free != null) && !heap->tld->recurse)
            {
                heap->tld->recurse = true;
                deferred_free(force, heap->tld->heartbeat, mi_atomic_load_ptr_relaxed(ref deferred_arg));
                heap->tld->recurse = false;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static partial void mi_register_deferred_free(mi_deferred_free_fun? fn, void* arg)
        {
            deferred_free = fn;
            mi_atomic_store_ptr_release(ref deferred_arg, arg);
        }

        /* -----------------------------------------------------------
          General allocation
        ----------------------------------------------------------- */

        // A huge page is allocated directly without being in a queue.
        // Because huge pages contain just one block, and the segment contains
        // just that page, we always treat them as abandoned and any thread
        // that frees the block can free the whole page and segment directly.
        private static mi_page_t* mi_huge_page_alloc(mi_heap_t* heap, [NativeTypeName("size_t")] nuint size)
        {
            nuint block_size = _mi_os_good_alloc_size(size);
            mi_assert_internal((MI_DEBUG > 1) && (_mi_bin(block_size) == MI_BIN_HUGE));

            mi_page_t* page = mi_page_fresh_alloc(heap, null, block_size);

            if (page != null)
            {
                // note: not `mi_page_usable_block_size` as `size` includes padding already
                nuint bsize = mi_page_block_size(page);

                mi_assert_internal((MI_DEBUG > 1) && (bsize >= size));
                mi_assert_internal((MI_DEBUG > 1) && mi_page_immediate_available(page));
                mi_assert_internal((MI_DEBUG > 1) && (_mi_page_segment(page)->page_kind == MI_PAGE_HUGE));
                mi_assert_internal((MI_DEBUG > 1) && (_mi_page_segment(page)->used == 1));

                // abandoned, not in the huge queue
                mi_assert_internal((MI_DEBUG > 1) && (_mi_page_segment(page)->thread_id == 0));

                mi_page_set_heap(page, null);

                if (bsize > MI_HUGE_OBJ_SIZE_MAX)
                {
                    _mi_stat_increase(ref heap->tld->stats.giant, bsize);
                    _mi_stat_counter_increase(ref heap->tld->stats.giant_count, 1);
                }
                else
                {
                    _mi_stat_increase(ref heap->tld->stats.huge, bsize);
                    _mi_stat_counter_increase(ref heap->tld->stats.huge_count, 1);
                }
            }

            return page;
        }

        // Allocate a page
        // Note: in debug mode the size includes MI_PADDING_SIZE and might have overflowed.
        private static mi_page_t* mi_find_page(mi_heap_t* heap, [NativeTypeName("size_t")] nuint size)
        {
            // correct for padding_size in case of an overflow on `size`  
            nuint req_size = size - MI_PADDING_SIZE;

            // huge allocation?
            if (mi_unlikely(req_size > (MI_LARGE_OBJ_SIZE_MAX - MI_PADDING_SIZE)))
            {
                if (mi_unlikely(req_size > (nuint)PTRDIFF_MAX))
                {
                    // we don't allocate more than PTRDIFF_MAX (see <https://sourceware.org/ml/libc-announce/2019/msg00001.html>)
                    _mi_error_message(EOVERFLOW, "allocation request is too large ({0} bytes)\n", req_size);

                    return null;
                }
                else
                {
                    return mi_huge_page_alloc(heap, size);
                }
            }
            else
            {
                // otherwise find a page with free blocks in our size segregated queues
                mi_assert_internal((MI_DEBUG > 1) && (size >= MI_PADDING_SIZE));
                return mi_find_free_page(heap, size);
            }
        }

        // Generic allocation routine if the fast path (`alloc.c:mi_page_malloc`) does not succeed.
        // Note: in debug mode the size includes MI_PADDING_SIZE and might have overflowed.
        private static partial void* _mi_malloc_generic(mi_heap_t* heap, nuint size)
        {
            mi_assert_internal((MI_DEBUG > 1) && (heap != null));

            // call potential deferred free routines
            _mi_deferred_free(heap, false);

            // free delayed frees from other threads
            _mi_heap_delayed_free(heap);

            // find (or allocate) a page of the right size
            mi_page_t* page = mi_find_page(heap, size);

            if (mi_unlikely(page == null))
            {
                // first time out of memory, try to collect and retry the allocation once more
                mi_heap_collect((IntPtr)heap, force: true);

                page = mi_find_page(heap, size);
            }

            if (mi_unlikely(page == null))
            {
                // out of memory

                // correct for padding_size in case of an overflow on `size`  
                nuint req_size = size - MI_PADDING_SIZE;

                _mi_error_message(ENOMEM, "unable to allocate memory ({0} bytes)\n", req_size);
                return null;
            }

            mi_assert_internal((MI_DEBUG > 1) && mi_page_immediate_available(page));
            mi_assert_internal((MI_DEBUG > 1) && (mi_page_block_size(page) >= size));

            // and try again, this time succeeding! (i.e. this should never recurse)
            return _mi_page_malloc(heap, page, size);
        }
    }
}
