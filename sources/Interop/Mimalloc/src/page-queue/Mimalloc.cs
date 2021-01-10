// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the page-queue.c file from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using static TerraFX.Interop.mi_delayed_t;
using static TerraFX.Interop.mi_page_kind_t;

namespace TerraFX.Interop
{
    public static unsafe partial class Mimalloc
    {
        /* -----------------------------------------------------------
          Queue query
        ----------------------------------------------------------- */

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool mi_page_queue_is_huge([NativeTypeName("const mi_page_queue_t*")] mi_page_queue_t* pq) => pq->block_size == (MI_LARGE_OBJ_SIZE_MAX + SizeOf<nuint>());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool mi_page_queue_is_full([NativeTypeName("const mi_page_queue_t*")] mi_page_queue_t* pq) => pq->block_size == (MI_LARGE_OBJ_SIZE_MAX + (2 * SizeOf<nuint>()));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool mi_page_queue_is_special([NativeTypeName("const mi_page_queue_t*")] mi_page_queue_t* pq) => pq->block_size > MI_LARGE_OBJ_SIZE_MAX;

        /* -----------------------------------------------------------
          Bins
        ----------------------------------------------------------- */

        // Bit scan reverse: return the index of the highest bit.

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [return: NativeTypeName("uint8_t")]
        private static byte mi_bsr32([NativeTypeName("uint32_t")] uint x) => (byte)(31 - BitOperations.LeadingZeroCount(x));

        // Bit scan reverse: return the index of the highest bit.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static partial byte _mi_bsr(nuint x)
        {
            if (Environment.Is64BitProcess)
            {
                return (byte)(63 - BitOperations.LeadingZeroCount(x));
            }
            else
            {
                return mi_bsr32((uint)x);
            }
        }

        // Return the bin for a given field size.
        // Returns MI_BIN_HUGE if the size is too large.
        // We use `wsize` for the size in "machine word sizes",
        // i.e. byte size == `wsize*sizeof(void*)`.
        private static partial byte _mi_bin(nuint size)
        {
            nuint wsize = _mi_wsize_from_size(size);
            byte bin;

            if (wsize <= 1)
            {
                bin = 1;
            }
            else if ((MI_ALIGN4W && (wsize <= 4)) || (MI_ALIGN2W && (wsize <= 8)))
            {
                // round to double word sizes
                bin = (byte)((wsize + 1) & ~(nuint)1);
            }
            else if (!MI_ALIGN4W && !MI_ALIGN2W && (wsize <= 8))
            {
                bin = (byte)wsize;
            }
            else if (wsize > MI_LARGE_OBJ_WSIZE_MAX)
            {
                bin = MI_BIN_HUGE;
            }
            else
            {
                if (MI_ALIGN4W && (wsize <= 16))
                {
                    // round to 4x word sizes
                    wsize = (wsize + 3) & ~(nuint)3;
                }

                wsize--;

                // find the highest bit
                byte b = mi_bsr32((uint)wsize);

                // and use the top 3 bits to determine the bin (~12.5% worst internal fragmentation).
                // - adjust with 3 because we use do not round the first 8 sizes
                //   which each get an exact bin
                bin = (byte)((b << 2) + (byte)((wsize >> (b - 2)) & 0x03) - 3);

                mi_assert_internal((MI_DEBUG > 1) && (bin < MI_BIN_HUGE));
            }

            mi_assert_internal((MI_DEBUG > 1) && (bin > 0) && (bin <= MI_BIN_HUGE));
            return bin;
        }

        /* -----------------------------------------------------------
          Queue of pages with free blocks
        ----------------------------------------------------------- */

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static partial nuint _mi_bin_size(byte bin) => (&_mi_heap_empty_pages->e0)[bin].block_size;

        // Good size for allocation
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static partial nuint mi_good_size(nuint size)
        {
            if (size <= MI_LARGE_OBJ_SIZE_MAX)
            {
                return _mi_bin_size(_mi_bin(size));
            }
            else
            {
                return _mi_align_up(size, _mi_os_page_size());
            }
        }

        private static bool mi_page_queue_contains(mi_page_queue_t* queue, [NativeTypeName("const mi_page_t*")] mi_page_t* page)
        {
            mi_assert_internal((MI_DEBUG > 1) && (MI_DEBUG > 1));
            mi_assert_internal((MI_DEBUG > 1) && (page != null));

            mi_page_t* list = queue->first;

            while (list != null)
            {
                mi_assert_internal((MI_DEBUG > 1) && ((list->next == null) || (list->next->prev == list)));
                mi_assert_internal((MI_DEBUG > 1) && ((list->prev == null) || (list->prev->next == list)));

                if (list == page)
                {
                    break;
                }

                list = list->next;
            }

            return list == page;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool mi_heap_contains_queue([NativeTypeName("const mi_heap_t*")] mi_heap_t* heap, [NativeTypeName("const mi_page_queue_t*")] mi_page_queue_t* pq)
        {
            mi_assert_internal((MI_DEBUG > 1) && (MI_DEBUG > 1));
            return (pq >= &heap->pages.e0) && (pq <= (&heap->pages.e0 + MI_BIN_FULL));
        }

        private static mi_page_queue_t* mi_page_queue_of([NativeTypeName("const mi_page_t*")] mi_page_t* page)
        {
            byte bin = mi_page_is_in_full(page) ? MI_BIN_FULL : _mi_bin(page->xblock_size);
            mi_heap_t* heap = mi_page_heap(page);

            mi_assert_internal((MI_DEBUG > 1) && heap != null && bin <= MI_BIN_FULL);

            mi_page_queue_t* pq = &heap->pages.e0 + bin;

            mi_assert_internal((MI_DEBUG > 1) && (bin >= MI_BIN_HUGE || page->xblock_size == pq->block_size));
            mi_assert_expensive((MI_DEBUG > 2) && mi_page_queue_contains(pq, page));

            return pq;
        }

        private static mi_page_queue_t* mi_heap_page_queue_of(mi_heap_t* heap, [NativeTypeName("const mi_page_t*")] mi_page_t* page)
        {
            byte bin = mi_page_is_in_full(page) ? MI_BIN_FULL : _mi_bin(page->xblock_size);
            mi_assert_internal((MI_DEBUG > 1) && (bin <= MI_BIN_FULL));

            mi_page_queue_t* pq = &heap->pages.e0 + bin;
            mi_assert_internal((MI_DEBUG > 1) && (mi_page_is_in_full(page) || page->xblock_size == pq->block_size));

            return pq;
        }

        // The current small page array is for efficiency and for each
        // small size (up to 256) it points directly to the page for that
        // size without having to compute the bin. This means when the
        // current free page queue is updated for a small bin, we need to update a
        // range of entries in `_mi_page_small_free`.
        private static void mi_heap_queue_first_update(mi_heap_t* heap, [NativeTypeName("const mi_page_queue_t*")] mi_page_queue_t* pq)
        {
            mi_assert_internal((MI_DEBUG > 1) && mi_heap_contains_queue(heap, pq));
            nuint size = pq->block_size;

            if (size > MI_SMALL_SIZE_MAX)
            {
                return;
            }

            mi_page_t* page = pq->first;

            if (pq->first == null)
            {
                page = (mi_page_t*)_mi_page_empty;
            }

            // find index in the right direct page array
            nuint idx = _mi_wsize_from_size(size);

            nuint start;
            mi_page_t** pages_free = &heap->pages_free_direct.e0;

            if (pages_free[idx] == page)
            {
                // already set
                return;
            }

            // find start slot
            if (idx <= 1)
            {
                start = 0;
            }
            else
            {
                // find previous size; due to minimal alignment upto 3 previous bins may need to be skipped
                byte bin = _mi_bin(size);

                mi_page_queue_t* prev = pq - 1;

                while ((bin == _mi_bin(prev->block_size)) && (prev > &heap->pages.e0))
                {
                    prev--;
                }

                start = 1 + _mi_wsize_from_size(prev->block_size);

                if (start > idx)
                {
                    start = idx;
                }
            }

            // set size range to the right page
            mi_assert((MI_DEBUG != 0) && (start <= idx));

            for (nuint sz = start; sz <= idx; sz++)
            {
                pages_free[sz] = page;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool mi_page_queue_is_empty(mi_page_queue_t* queue) => queue->first == null;

        private static void mi_page_queue_remove(mi_page_queue_t* queue, mi_page_t* page)
        {
            mi_assert_internal((MI_DEBUG > 1) && (page != null));
            mi_assert_expensive((MI_DEBUG > 2) && mi_page_queue_contains(queue, page));
            mi_assert_internal((MI_DEBUG > 1) && ((page->xblock_size == queue->block_size) || ((page->xblock_size > MI_LARGE_OBJ_SIZE_MAX) && mi_page_queue_is_huge(queue)) || (mi_page_is_in_full(page) && mi_page_queue_is_full(queue))));

            mi_heap_t* heap = mi_page_heap(page);

            if (page->prev != null)
            {
                page->prev->next = page->next;
            }

            if (page->next != null)
            {
                page->next->prev = page->prev;
            }

            if (page == queue->last)
            {
                queue->last = page->prev;
            }

            if (page == queue->first)
            {
                queue->first = page->next;
                mi_assert_internal((MI_DEBUG > 1) && mi_heap_contains_queue(heap, queue));

                // update first
                mi_heap_queue_first_update(heap, queue);
            }

            heap->page_count--;

            page->next = null;
            page->prev = null;

            // mi_atomic_store_ptr_release(ref page->heap, null);
            mi_page_set_in_full(page, false);
        }

        private static void mi_page_queue_push(mi_heap_t* heap, mi_page_queue_t* queue, mi_page_t* page)
        {
            mi_assert_internal((MI_DEBUG > 1) && (mi_page_heap(page) == heap));
            mi_assert_internal((MI_DEBUG > 1) && (!mi_page_queue_contains(queue, page)));
            mi_assert_internal((MI_DEBUG > 1) && (_mi_page_segment(page)->page_kind != MI_PAGE_HUGE));
            mi_assert_internal((MI_DEBUG > 1) && ((page->xblock_size == queue->block_size) || ((page->xblock_size > MI_LARGE_OBJ_SIZE_MAX) && mi_page_queue_is_huge(queue)) || (mi_page_is_in_full(page) && mi_page_queue_is_full(queue))));

            mi_page_set_in_full(page, mi_page_queue_is_full(queue));
            // mi_atomic_store_ptr_release(ref page->heap, heap);

            page->next = queue->first;
            page->prev = null;

            if (queue->first != null)
            {
                mi_assert_internal((MI_DEBUG > 1) && (queue->first->prev == null));
                queue->first->prev = page;
                queue->first = page;
            }
            else
            {
                queue->first = queue->last = page;
            }

            // update direct
            mi_heap_queue_first_update(heap, queue);

            heap->page_count++;
        }

        private static void mi_page_queue_enqueue_from(mi_page_queue_t* to, mi_page_queue_t* from, mi_page_t* page)
        {
            mi_assert_internal((MI_DEBUG > 1) && (page != null));
            mi_assert_expensive((MI_DEBUG > 2) && mi_page_queue_contains(from, page));
            mi_assert_expensive((MI_DEBUG > 2) && !mi_page_queue_contains(to, page));
            mi_assert_internal((MI_DEBUG > 1) && (((page->xblock_size == to->block_size) && page->xblock_size == from->block_size) || ((page->xblock_size == to->block_size) && mi_page_queue_is_full(from)) || ((page->xblock_size == from->block_size) && mi_page_queue_is_full(to)) || ((page->xblock_size > MI_LARGE_OBJ_SIZE_MAX) && mi_page_queue_is_huge(to)) || ((page->xblock_size > MI_LARGE_OBJ_SIZE_MAX) && mi_page_queue_is_full(to))));

            mi_heap_t* heap = mi_page_heap(page);

            if (page->prev != null)
            {
                page->prev->next = page->next;
            }

            if (page->next != null)
            {
                page->next->prev = page->prev;
            }

            if (page == from->last)
            {
                from->last = page->prev;
            }

            if (page == from->first)
            {
                from->first = page->next;
                mi_assert_internal((MI_DEBUG > 1) && mi_heap_contains_queue(heap, from));

                // update first
                mi_heap_queue_first_update(heap, from);
            }

            page->prev = to->last;
            page->next = null;

            if (to->last != null)
            {
                mi_assert_internal((MI_DEBUG > 1) && (heap == mi_page_heap(to->last)));

                to->last->next = page;
                to->last = page;
            }
            else
            {
                to->first = page;
                to->last = page;

                mi_heap_queue_first_update(heap, to);
            }

            mi_page_set_in_full(page, mi_page_queue_is_full(to));
        }

        // Only called from `mi_heap_absorb`.
        private static partial nuint _mi_page_queue_append(mi_heap_t* heap, mi_page_queue_t* pq, mi_page_queue_t* append)
        {
#pragma warning disable CS0420
            mi_assert_internal((MI_DEBUG > 1) && mi_heap_contains_queue(heap, pq));
            mi_assert_internal((MI_DEBUG > 1) && (pq->block_size == append->block_size));

            if (append->first == null)
            {
                return 0;
            }

            // set append pages to new heap and count
            nuint count = 0;

            for (mi_page_t* page = append->first; page != null; page = page->next)
            {
                // inline `mi_page_set_heap` to avoid wrong assertion during absorption;
                // in this case it is ok to be delayed freeing since both "to" and "from" heap are still alive.
                mi_atomic_store_release(ref page->xheap, (nuint)heap);

                // set the flag to delayed free (not overriding NEVER_DELAYED_FREE) which has as a
                // side effect that it spins until any DELAYED_FREEING is finished. This ensures
                // that after appending only the new heap will be used for delayed free operations.
                _mi_page_use_delayed_free(page, MI_USE_DELAYED_FREE, false);

                count++;
            }

            if (pq->last == null)
            {
                // take over afresh
                mi_assert_internal((MI_DEBUG > 1) && (pq->first == null));

                pq->first = append->first;
                pq->last = append->last;

                mi_heap_queue_first_update(heap, pq);
            }
            else
            {
                // append to end

                mi_assert_internal((MI_DEBUG > 1) && (pq->last != null));
                mi_assert_internal((MI_DEBUG > 1) && (append->first != null));

                pq->last->next = append->first;
                append->first->prev = pq->last;

                pq->last = append->last;
            }

            return count;
#pragma warning restore CS0420
        }
    }
}
