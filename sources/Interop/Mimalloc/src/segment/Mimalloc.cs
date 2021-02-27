// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the segment.c file from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

using System.Runtime.CompilerServices;
using static TerraFX.Interop.mi_delayed_t;
using static TerraFX.Interop.mi_option_t;
using static TerraFX.Interop.mi_page_kind_t;

namespace TerraFX.Interop
{
    public static unsafe partial class Mimalloc
    {
        private const nuint MI_PAGE_HUGE_ALIGN = 256 * 1024;

        [return: NativeTypeName("uint8_t*")]
        private static partial byte* mi_segment_raw_page_start([NativeTypeName("const mi_segment_t*")] mi_segment_t* segment, [NativeTypeName("const mi_page_t*")] mi_page_t* page, [NativeTypeName("size_t*")] out nuint page_size);

        /* --------------------------------------------------------------------------------
          Segment allocation
          We allocate pages inside bigger "segments" (4mb on 64-bit). This is to avoid
          splitting VMA's on Linux and reduce fragmentation on other OS's.
          Each thread owns its own segments.

          Currently we have:
          - small pages (64kb), 64 in one segment
          - medium pages (512kb), 8 in one segment
          - large pages (4mb), 1 in one segment
          - huge blocks > MI_LARGE_OBJ_SIZE_MAX become large segment with 1 page

          In any case the memory for a segment is virtual and usually committed on demand.
          (i.e. we are careful to not touch the memory until we actually allocate a block there)

          If a  thread ends, it "abandons" pages with used blocks
          and there is an abandoned segment list whose segments can
          be reclaimed by still running threads, much like work-stealing.
        -------------------------------------------------------------------------------- */

        /* -----------------------------------------------------------
          Queue of segments containing free pages
        ----------------------------------------------------------- */

        private static bool mi_segment_queue_contains([NativeTypeName("const mi_segment_queue_t*")] mi_segment_queue_t* queue, [NativeTypeName("const mi_segment_t*")] mi_segment_t* segment)
        {
            mi_assert_internal((MI_DEBUG > 1) && (MI_DEBUG >= 3));
            mi_assert_internal((MI_DEBUG > 1) && (segment != null));

            mi_segment_t* list = queue->first;

            while (list != null)
            {
                if (list == segment)
                {
                    break;
                }

                mi_assert_internal((MI_DEBUG > 1) && ((list->next == null) || (list->next->prev == list)));
                mi_assert_internal((MI_DEBUG > 1) && ((list->prev == null) || (list->prev->next == list)));

                list = list->next;
            }

            return list == segment;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool mi_segment_queue_is_empty([NativeTypeName("const mi_segment_queue_t*")] mi_segment_queue_t* queue) => queue->first == null;

        private static void mi_segment_queue_remove(mi_segment_queue_t* queue, mi_segment_t* segment)
        {
            mi_assert_expensive((MI_DEBUG > 2) && mi_segment_queue_contains(queue, segment));

            if (segment->prev != null)
            {
                segment->prev->next = segment->next;
            }

            if (segment->next != null)
            {
                segment->next->prev = segment->prev;
            }

            if (segment == queue->first)
            {
                queue->first = segment->next;
            }

            if (segment == queue->last)
            {
                queue->last = segment->prev;
            }

            segment->next = null;
            segment->prev = null;
        }

        private static void mi_segment_enqueue(mi_segment_queue_t* queue, mi_segment_t* segment)
        {
            mi_assert_expensive((MI_DEBUG > 2) && !mi_segment_queue_contains(queue, segment));

            segment->next = null;
            segment->prev = queue->last;

            if (queue->last != null)
            {
                mi_assert_internal((MI_DEBUG > 1) && (queue->last->next == null));

                queue->last->next = segment;
                queue->last = segment;
            }
            else
            {
                queue->last = queue->first = segment;
            }
        }

        private static mi_segment_queue_t* mi_segment_free_queue_of_kind(mi_page_kind_t kind, mi_segments_tld_t* tld)
        {
            if (kind == MI_PAGE_SMALL)
            {
                return &tld->small_free;
            }
            else if (kind == MI_PAGE_MEDIUM)
            {
                return &tld->medium_free;
            }
            else
            {
                return null;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static mi_segment_queue_t* mi_segment_free_queue([NativeTypeName("const mi_segment_t*")] mi_segment_t* segment, mi_segments_tld_t* tld) => mi_segment_free_queue_of_kind(segment->page_kind, tld);

        // remove from free queue if it is in one
        private static void mi_segment_remove_from_free_queue(mi_segment_t* segment, mi_segments_tld_t* tld)
        {
            // may be null
            mi_segment_queue_t* queue = mi_segment_free_queue(segment, tld);

            bool in_queue = (queue != null) && ((segment->next != null) || (segment->prev != null) || (queue->first == segment));

            if (in_queue)
            {
                mi_segment_queue_remove(queue, segment);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void mi_segment_insert_in_free_queue(mi_segment_t* segment, mi_segments_tld_t* tld) => mi_segment_enqueue(mi_segment_free_queue(segment, tld), segment);

        /* -----------------------------------------------------------
         Invariant checking
        ----------------------------------------------------------- */

        private static bool mi_segment_is_in_free_queue([NativeTypeName("const mi_segment_t*")] mi_segment_t* segment, mi_segments_tld_t* tld)
        {
            mi_assert_internal((MI_DEBUG > 1) && (MI_DEBUG >= 2));

            mi_segment_queue_t* queue = mi_segment_free_queue(segment, tld);
            bool in_queue = (queue != null) && ((segment->next != null) || (segment->prev != null) || (queue->first == segment));

            if (in_queue)
            {
                mi_assert_expensive((MI_DEBUG > 2) && mi_segment_queue_contains(queue, segment));
            }

            return in_queue;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [return: NativeTypeName("size_t")]
        private static nuint mi_segment_page_size([NativeTypeName("const mi_segment_t*")] mi_segment_t* segment)
        {
            if (segment->capacity > 1)
            {
                mi_assert_internal((MI_DEBUG > 1) && (segment->page_kind <= MI_PAGE_MEDIUM));
                return (nuint)1 << (int)segment->page_shift;
            }
            else
            {
                mi_assert_internal((MI_DEBUG > 1) && (segment->page_kind >= MI_PAGE_LARGE));
                return segment->segment_size;
            }
        }

        private static bool mi_pages_reset_contains([NativeTypeName("const mi_page_t*")] mi_page_t* page, mi_segments_tld_t* tld)
        {
            mi_assert_internal((MI_DEBUG > 1) && (MI_DEBUG >= 2));
            mi_page_t* p = tld->pages_reset.first;

            while (p != null)
            {
                if (p == page)
                {
                    return true;
                }
                p = p->next;
            }

            return false;
        }

        private static bool mi_segment_is_valid([NativeTypeName("const mi_segment_t*")] mi_segment_t* segment, mi_segments_tld_t* tld)
        {
            mi_assert_internal((MI_DEBUG > 1) && (MI_DEBUG >= 3));
            mi_assert_internal((MI_DEBUG > 1) && (segment != null));
            mi_assert_internal((MI_DEBUG > 1) && (_mi_ptr_cookie(segment) == segment->cookie));
            mi_assert_internal((MI_DEBUG > 1) && (segment->used <= segment->capacity));
            mi_assert_internal((MI_DEBUG > 1) && (segment->abandoned <= segment->used));

            nuint nfree = 0;

            for (nuint i = 0; i < segment->capacity; i++)
            {
                mi_page_t* page = &segment->pages.e0 + i;

                if (!page->segment_in_use)
                {
                    nfree++;
                }

                if (page->segment_in_use || page->is_reset)
                {
                    mi_assert_expensive((MI_DEBUG > 2) && !mi_pages_reset_contains(page, tld));
                }
            }

            mi_assert_internal((MI_DEBUG > 1) && (nfree + segment->used == segment->capacity));
            mi_assert_internal((MI_DEBUG > 1) && ((segment->thread_id == _mi_thread_id()) || (segment->thread_id == 0)));
            mi_assert_internal((MI_DEBUG > 1) && ((segment->page_kind == MI_PAGE_HUGE) || ((mi_segment_page_size(segment) * segment->capacity) == segment->segment_size)));

            return true;
        }

        private static bool mi_page_not_in_queue([NativeTypeName("const mi_page_t*")] mi_page_t* page, mi_segments_tld_t* tld)
        {
            mi_assert_internal((MI_DEBUG > 1) && (page != null));

            if ((page->next != null) || (page->prev != null))
            {
                mi_assert_internal((MI_DEBUG > 1) && mi_pages_reset_contains(page, tld));
                return false;
            }
            else
            {
                // both next and prev are null, check for singleton list
                return (tld->pages_reset.first != page) && (tld->pages_reset.last != page);
            }
        }

        /* -----------------------------------------------------------
          Guard pages
        ----------------------------------------------------------- */

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void mi_segment_protect_range(void* p, [NativeTypeName("size_t")] nuint size, bool protect)
        {
            if (protect)
            {
                _mi_mem_protect(p, size);
            }
            else
            {
                _mi_mem_unprotect(p, size);
            }
        }

        private static void mi_segment_protect(mi_segment_t* segment, bool protect, mi_os_tld_t* tld)
        {
            // add/remove guard pages
            if (MI_SECURE != 0)
            {
                // in secure mode, we set up a protected page in between the segment info and the page data
                nuint os_psize = _mi_os_page_size();

                mi_assert_internal((MI_DEBUG > 1) && ((segment->segment_info_size - os_psize) >= (SizeOf<mi_segment_t>() + ((segment->capacity - 1) * SizeOf<mi_page_t>()))));
                mi_assert_internal((MI_DEBUG > 1) && ((((nuint)segment + segment->segment_info_size) % os_psize) == 0));

                mi_segment_protect_range((byte*)segment + segment->segment_info_size - os_psize, os_psize, protect);

                if ((MI_SECURE <= 1) || (segment->capacity == 1))
                {
                    // and protect the last (or only) page too
                    mi_assert_internal((MI_DEBUG > 1) && (MI_SECURE <= 1 || segment->page_kind >= MI_PAGE_LARGE));

                    byte* start = (byte*)segment + segment->segment_size - os_psize;

                    if (protect && !segment->mem_is_committed)
                    {
                        if (protect)
                        {
                            // ensure secure page is committed
                            if (_mi_mem_commit(start, os_psize, out _, tld))
                            {
                                // if this fails that is ok (as it is an unaccessible page)
                                mi_segment_protect_range(start, os_psize, protect);
                            }
                        }
                    }
                    else
                    {
                        mi_segment_protect_range(start, os_psize, protect);
                    }
                }
                else
                {
                    // or protect every page
                    nuint page_size = mi_segment_page_size(segment);

                    for (nuint i = 0; i < segment->capacity; i++)
                    {
                        if ((&segment->pages.e0)[i].is_committed)
                        {
                            mi_segment_protect_range((byte*)segment + (i + 1) * page_size - os_psize, os_psize, protect);
                        }
                    }
                }
            }
        }

        /* -----------------------------------------------------------
          Page reset
        ----------------------------------------------------------- */

        private static void mi_page_reset(mi_segment_t* segment, mi_page_t* page, [NativeTypeName("size_t")] nuint size, mi_segments_tld_t* tld)
        {
            mi_assert_internal((MI_DEBUG > 1) && page->is_committed);

            if (!mi_option_is_enabled(mi_option_page_reset))
            {
                return;
            }

            if (segment->mem_is_fixed || page->segment_in_use || !page->is_committed || page->is_reset)
            {
                return;
            }

            void* start = mi_segment_raw_page_start(segment, page, out nuint psize);

            page->is_reset = true;
            mi_assert_internal((MI_DEBUG > 1) && (size <= psize));

            nuint reset_size = ((size == 0) || (size > psize)) ? psize : size;

            if (reset_size > 0)
            {
                _mi_mem_reset(start, reset_size, tld->os);
            }
        }

        private static bool mi_page_unreset(mi_segment_t* segment, mi_page_t* page, [NativeTypeName("size_t")] nuint size, mi_segments_tld_t* tld)
        {
            mi_assert_internal((MI_DEBUG > 1) && page->is_reset);
            mi_assert_internal((MI_DEBUG > 1) && page->is_committed);
            mi_assert_internal((MI_DEBUG > 1) && (!segment->mem_is_fixed));

            if (segment->mem_is_fixed || !page->is_committed || !page->is_reset)
            {
                return true;
            }

            page->is_reset = false;

            byte* start = mi_segment_raw_page_start(segment, page, out nuint psize);
            nuint unreset_size = ((size == 0) || (size > psize)) ? psize : size;

            bool is_zero = false;
            bool ok = true;

            if (unreset_size > 0)
            {
                ok = _mi_mem_unreset(start, unreset_size, out is_zero, tld->os);
            }

            if (is_zero)
            {
                page->is_zero_init = true;
            }
            return ok;
        }

        /* -----------------------------------------------------------
          The free page queue
        ----------------------------------------------------------- */

        // we re-use the `used` field for the expiration counter. Since this is a
        // a 32-bit field while the clock is always 64-bit we need to guard
        // against overflow, we use substraction to check for expiry which work
        // as long as the reset delay is under (2^30 - 1) milliseconds (~12 days)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void mi_page_reset_set_expire(mi_page_t* page)
        {
            uint expire = (uint)_mi_clock_now() + (uint)mi_option_get(mi_option_reset_delay);
            page->used = expire;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool mi_page_reset_is_expired(mi_page_t* page, [NativeTypeName("mi_msecs_t")] long now)
        {
            int expire = (int)page->used;
            return ((int)now - expire) >= 0;
        }

        private static void mi_pages_reset_add(mi_segment_t* segment, mi_page_t* page, mi_segments_tld_t* tld)
        {
            mi_assert_internal((MI_DEBUG > 1) && (!page->segment_in_use || !page->is_committed));
            mi_assert_internal((MI_DEBUG > 1) && mi_page_not_in_queue(page, tld));
            mi_assert_expensive((MI_DEBUG > 2) && !mi_pages_reset_contains(page, tld));
            mi_assert_internal((MI_DEBUG > 1) && (_mi_page_segment(page) == segment));

            if (!mi_option_is_enabled(mi_option_page_reset))
            {
                return;
            }

            if (segment->mem_is_fixed || page->segment_in_use || !page->is_committed || page->is_reset)
            {
                return;
            }

            if (mi_option_get(mi_option_reset_delay) == 0)
            {
                // reset immediately?
                mi_page_reset(segment, page, 0, tld);
            }
            else
            {
                // otherwise push on the delayed page reset queue
                mi_page_queue_t* pq = &tld->pages_reset;

                // push on top
                mi_page_reset_set_expire(page);

                page->next = pq->first;
                page->prev = null;

                if (pq->first == null)
                {
                    mi_assert_internal((MI_DEBUG > 1) && (pq->last == null));
                    pq->first = pq->last = page;
                }
                else
                {
                    pq->first->prev = page;
                    pq->first = page;
                }
            }
        }

        private static void mi_pages_reset_remove(mi_page_t* page, mi_segments_tld_t* tld)
        {
            if (mi_page_not_in_queue(page, tld))
            {
                return;
            }

            mi_page_queue_t* pq = &tld->pages_reset;

            mi_assert_internal((MI_DEBUG > 1) && (pq != null));
            mi_assert_internal((MI_DEBUG > 1) && (!page->segment_in_use));
            mi_assert_internal((MI_DEBUG > 1) && mi_pages_reset_contains(page, tld));

            if (page->prev != null)
            {
                page->prev->next = page->next;
            }

            if (page->next != null)
            {
                page->next->prev = page->prev;
            }

            if (page == pq->last)
            {
                pq->last = page->prev;
            }

            if (page == pq->first)
            {
                pq->first = page->next;
            }

            page->next = page->prev = null;
            page->used = 0;
        }

        private static void mi_pages_reset_remove_all_in_segment(mi_segment_t* segment, bool force_reset, mi_segments_tld_t* tld)
        {
            if (segment->mem_is_fixed)
            {
                // never reset in huge OS pages
                return;
            }

            for (nuint i = 0; i < segment->capacity; i++)
            {
                mi_page_t* page = &segment->pages.e0 + i;

                if (!page->segment_in_use && page->is_committed && !page->is_reset)
                {
                    mi_pages_reset_remove(page, tld);
                    if (force_reset)
                    {
                        mi_page_reset(segment, page, 0, tld);
                    }
                }
                else
                {
                    mi_assert_internal((MI_DEBUG > 1) && mi_page_not_in_queue(page, tld));
                }
            }
        }

        private static void mi_reset_delayed(mi_segments_tld_t* tld)
        {
            if (!mi_option_is_enabled(mi_option_page_reset))
            {
                return;
            }

            long now = _mi_clock_now();
            mi_page_queue_t* pq = &tld->pages_reset;

            // from oldest up to the first that has not expired yet
            mi_page_t* page = pq->last;

            while ((page != null) && mi_page_reset_is_expired(page, now))
            {
                // save previous field
                mi_page_t* prev = page->prev;

                mi_page_reset(_mi_page_segment(page), page, 0, tld);

                page->used = 0;
                page->prev = page->next = null;

                page = prev;
            }

            // discard the reset pages from the queue
            pq->last = page;

            if (page != null)
            {
                page->next = null;
            }
            else
            {
                pq->first = null;
            }
        }

        /* -----------------------------------------------------------
         Segment size calculations
        ----------------------------------------------------------- */

        // Raw start of the page available memory; can be used on uninitialized pages (only `segment_idx` must be set)
        // The raw start is not taking aligned block allocation into consideration.
        private static partial byte* mi_segment_raw_page_start(mi_segment_t* segment, mi_page_t* page, out nuint page_size)
        {
            nuint psize = (segment->page_kind == MI_PAGE_HUGE) ? segment->segment_size : ((nuint)1 << (int)segment->page_shift);
            byte* p = (byte*)segment + page->segment_idx * psize;

            if (page->segment_idx == 0)
            {
                // the first page starts after the segment info (and possible guard page)
                p += segment->segment_info_size;
                psize -= segment->segment_info_size;
            }

            if ((MI_SECURE > 1) || ((MI_SECURE == 1) && (page->segment_idx == (segment->capacity - 1))))
            {
                // secure == 1: the last page has an os guard page at the end
                // secure >  1: every page has an os guard page
                psize -= _mi_os_page_size();
            }

            page_size = psize;

            mi_assert_internal((MI_DEBUG > 1) && ((page->xblock_size == 0) || (_mi_ptr_page(p) == page)));
            mi_assert_internal((MI_DEBUG > 1) && (_mi_ptr_segment(p) == segment));

            return p;
        }

        // Start of the page available memory; can be used on uninitialized pages (only `segment_idx` must be set)
        private static partial byte* _mi_segment_page_start(mi_segment_t* segment, mi_page_t* page, nuint block_size, out nuint page_size, out nuint pre_size)
        {
            byte* p = mi_segment_raw_page_start(segment, page, out nuint psize);
            pre_size = 0;

            if ((page->segment_idx == 0) && (block_size > 0) && (segment->page_kind <= MI_PAGE_MEDIUM))
            {
                // for small and medium objects, ensure the page start is aligned with the block size (PR#66 by kickunderscore)
                nuint adjust = block_size - ((nuint)p % block_size);

                if (adjust < block_size)
                {
                    p += adjust;
                    psize -= adjust;

                    pre_size = adjust;
                }

                mi_assert_internal((MI_DEBUG > 1) && (((nuint)p % block_size) == 0));
            }

            page_size = psize;

            mi_assert_internal((MI_DEBUG > 1) && ((page->xblock_size == 0) || (_mi_ptr_page(p) == page)));
            mi_assert_internal((MI_DEBUG > 1) && (_mi_ptr_segment(p) == segment));

            return p;
        }

        [return: NativeTypeName("size_t")]
        private static nuint mi_segment_size([NativeTypeName("size_t")] nuint capacity, [NativeTypeName("size_t")] nuint required, [NativeTypeName("size_t*")] out nuint pre_size, [NativeTypeName("size_t*")] out nuint info_size)
        {
            const int padding = 16;
            nuint minsize = SizeOf<mi_segment_t>() + ((capacity - 1) * SizeOf<mi_page_t>()) + padding;

            nuint guardsize = 0;
            nuint isize = 0;

            if (MI_SECURE == 0)
            {
                // normally no guard pages
                isize = _mi_align_up(minsize, 16 * MI_MAX_ALIGN_SIZE);
            }
            else
            {
                // in secure mode, we set up a protected page in between the segment info
                // and the page data (and one at the end of the segment)

                nuint page_size = _mi_os_page_size();
                isize = _mi_align_up(minsize, page_size);

                guardsize = page_size;
                required = _mi_align_up(required, page_size);
            }

            info_size = isize;
            pre_size = isize + guardsize;

            return (required == 0) ? MI_SEGMENT_SIZE : _mi_align_up(required + isize + 2 * guardsize, MI_PAGE_HUGE_ALIGN);
        }

        /* ----------------------------------------------------------------------------
        Segment caches
        We keep a small segment cache per thread to increase local
        reuse and avoid setting/clearing guard pages in secure mode.
        ------------------------------------------------------------------------------- */

        private static void mi_segments_track_size(long segment_size, mi_segments_tld_t* tld)
        {
            if (segment_size >= 0)
            {
                _mi_stat_increase(ref tld->stats->segments, 1);
            }
            else
            {
                _mi_stat_decrease(ref tld->stats->segments, 1);
            }

            unchecked
            {
                tld->count += (segment_size >= 0) ? 1 : (nuint)(-1);
            }

            if (tld->count > tld->peak_count)
            {
                tld->peak_count = tld->count;
            }

            unchecked
            {
                tld->current_size += (nuint)segment_size;
            }

            if (tld->current_size > tld->peak_size)
            {
                tld->peak_size = tld->current_size;
            }
        }

        private static void mi_segment_os_free(mi_segment_t* segment, [NativeTypeName("size_t")] nuint segment_size, mi_segments_tld_t* tld)
        {
            segment->thread_id = 0;
            mi_segments_track_size(-(long)segment_size, tld);

            if (MI_SECURE != 0)
            {
                mi_assert_internal((MI_DEBUG > 1) && (!segment->mem_is_fixed));
                mi_segment_protect(segment, false, tld->os); // ensure no more guard pages are set
            }

            bool any_reset = false;
            bool fully_committed = true;

            for (nuint i = 0; i < segment->capacity; i++)
            {
                mi_page_t* page = &segment->pages.e0 + i;

                if (!page->is_committed)
                {
                    fully_committed = false;
                }

                if (page->is_reset)
                {
                    any_reset = true;
                }
            }

            if (any_reset && mi_option_is_enabled(mi_option_reset_decommits))
            {
                fully_committed = false;
            }

            _mi_mem_free(segment, segment_size, segment->memid, fully_committed, any_reset, tld->os);
        }

        // The thread local segment cache is limited to be at most 1/8 of the peak size of segments in use,
        private const nuint MI_SEGMENT_CACHE_FRACTION = 8;

        // note: returned segment may be partially reset
        private static mi_segment_t* mi_segment_cache_pop([NativeTypeName("size_t")] nuint segment_size, mi_segments_tld_t* tld)
        {
            if ((segment_size != 0) && (segment_size != MI_SEGMENT_SIZE))
            {
                return null;
            }

            mi_segment_t* segment = tld->cache;

            if (segment == null)
            {
                return null;
            }

            tld->cache_count--;
            tld->cache = segment->next;

            segment->next = null;
            mi_assert_internal((MI_DEBUG > 1) && (segment->segment_size == MI_SEGMENT_SIZE));

            _mi_stat_decrease(ref tld->stats->segments_cache, 1);
            return segment;
        }

        private static bool mi_segment_cache_full(mi_segments_tld_t* tld)
        {
            nuint max_cache = (nuint)mi_option_get(mi_option_segment_cache);

            // at least allow a 1 element cache
            if ((tld->cache_count < max_cache) && (tld->cache_count < (1 + (tld->peak_count / MI_SEGMENT_CACHE_FRACTION))))
            {
                return false;
            }

            // take the opportunity to reduce the segment cache if it is too large (now)
            // TODO: this never happens as we check against peak usage, should we use current usage instead?

            while (tld->cache_count > max_cache)
            {
                mi_segment_t* segment = mi_segment_cache_pop(0, tld);
                mi_assert_internal((MI_DEBUG > 1) && (segment != null));

                if (segment != null)
                {
                    mi_segment_os_free(segment, segment->segment_size, tld);
                }
            }

            return true;
        }

        private static bool mi_segment_cache_push(mi_segment_t* segment, mi_segments_tld_t* tld)
        {
            mi_assert_internal((MI_DEBUG > 1) && (!mi_segment_is_in_free_queue(segment, tld)));
            mi_assert_internal((MI_DEBUG > 1) && (segment->next == null));

            if ((segment->segment_size != MI_SEGMENT_SIZE) || mi_segment_cache_full(tld))
            {
                return false;
            }

            mi_assert_internal((MI_DEBUG > 1) && (segment->segment_size == MI_SEGMENT_SIZE));
            segment->next = tld->cache;

            tld->cache = segment;
            tld->cache_count++;

            _mi_stat_increase(ref tld->stats->segments_cache, 1);
            return true;
        }

        // called by threads that are terminating to free cached segments
        private static partial void _mi_segment_thread_collect(mi_segments_tld_t* tld)
        {
            mi_segment_t* segment;

            while ((segment = mi_segment_cache_pop(0, tld)) != null)
            {
                mi_segment_os_free(segment, segment->segment_size, tld);
            }

            mi_assert_internal((MI_DEBUG > 1) && (tld->cache_count == 0));
            mi_assert_internal((MI_DEBUG > 1) && (tld->cache == null));

            if ((MI_DEBUG >= 2) && !_mi_is_main_thread())
            {
                mi_assert_internal((MI_DEBUG > 1) && (tld->pages_reset.first == null));
                mi_assert_internal((MI_DEBUG > 1) && (tld->pages_reset.last == null));
            }
        }

        /* -----------------------------------------------------------
           Segment allocation
        ----------------------------------------------------------- */

        // Allocate a segment from the OS aligned to `MI_SEGMENT_SIZE` .
        private static mi_segment_t* mi_segment_init(mi_segment_t* segment, [NativeTypeName("size_t")] nuint required, mi_page_kind_t page_kind, [NativeTypeName("size_t")] nuint page_shift, mi_segments_tld_t* tld, mi_os_tld_t* os_tld)
        {
#pragma warning disable CS0420
            // the segment parameter is non-null if it came from our cache
            mi_assert_internal((MI_DEBUG > 1) && ((segment == null) || ((required == 0) && (page_kind <= MI_PAGE_LARGE))));

            // calculate needed sizes first
            nuint capacity;

            if (page_kind == MI_PAGE_HUGE)
            {
                mi_assert_internal((MI_DEBUG > 1) && (page_shift == (nuint)MI_SEGMENT_SHIFT) && (required > 0));
                capacity = 1;
            }
            else
            {
                mi_assert_internal((MI_DEBUG > 1) && (required == 0));

                nuint page_size = (nuint)1 << (int)page_shift;
                capacity = MI_SEGMENT_SIZE / page_size;

                mi_assert_internal((MI_DEBUG > 1) && (MI_SEGMENT_SIZE % page_size == 0));
                mi_assert_internal((MI_DEBUG > 1) && (capacity >= 1) && (capacity <= MI_SMALL_PAGES_PER_SEGMENT));
            }

            nuint segment_size = mi_segment_size(capacity, required, out nuint pre_size, out nuint info_size);
            mi_assert_internal((MI_DEBUG > 1) && (segment_size >= required));

            // Initialize parameters
            bool eager_delayed = (page_kind <= MI_PAGE_MEDIUM) && (tld->count < (nuint)mi_option_get(mi_option_eager_commit_delay));
            bool eager = !eager_delayed && mi_option_is_enabled(mi_option_eager_commit);
            bool commit = eager;
            bool pages_still_good = false;
            bool is_zero = false;

            // Try to get it from our thread local cache first
            if (segment != null)
            {
                // came from cache
                mi_assert_internal((MI_DEBUG > 1) && (segment->segment_size == segment_size));

                if ((page_kind <= MI_PAGE_MEDIUM) && (segment->page_kind == page_kind) && (segment->segment_size == segment_size))
                {
                    pages_still_good = true;
                }
                else
                {
                    if (MI_SECURE != 0)
                    {
                        mi_assert_internal((MI_DEBUG > 1) && (!segment->mem_is_fixed));
                        mi_segment_protect(segment, false, tld->os); // reset protection if the page kind differs
                    }

                    // different page kinds; unreset any reset pages, and unprotect
                    // TODO: optimize cache pop to return fitting pages if possible?

                    for (nuint i = 0; i < segment->capacity; i++)
                    {
                        mi_page_t* page = &segment->pages.e0 + i;

                        if (page->is_reset)
                        {
                            if (!commit && mi_option_is_enabled(mi_option_reset_decommits))
                            {
                                page->is_reset = false;
                            }
                            else
                            {
                                // todo: only unreset the part that was reset? (instead of the full page)
                                mi_page_unreset(segment, page, 0, tld);
                            }
                        }
                    }

                    // ensure the initial info is committed
                    if (segment->capacity < capacity)
                    {
                        bool ok = _mi_mem_commit(segment, pre_size, out bool commit_zero, tld->os);

                        if (commit_zero)
                        {
                            is_zero = true;
                        }

                        if (!ok)
                        {
                            return null;
                        }
                    }
                }
            }
            else
            {
                // only allow large OS pages once we are no longer lazy
                bool mem_large = !eager_delayed && (MI_SECURE == 0);

                // Allocate the segment from the OS
                segment = (mi_segment_t*)_mi_mem_alloc_aligned(segment_size, MI_SEGMENT_SIZE, ref commit, ref mem_large, out is_zero, out nuint memid, os_tld);

                if (segment == null)
                {
                    // failed to allocate
                    return null;
                }

                if (!commit)
                {
                    // ensure the initial info is committed
                    bool ok = _mi_mem_commit(segment, pre_size, out bool commit_zero, tld->os);

                    if (commit_zero)
                    {
                        is_zero = true;
                    }

                    if (!ok)
                    {
                        // commit failed; we cannot touch the memory: free the segment directly and return `null`
                        _mi_mem_free(segment, MI_SEGMENT_SIZE, memid, false, false, os_tld);
                        return null;
                    }
                }

                segment->memid = memid;
                segment->mem_is_fixed = mem_large;
                segment->mem_is_committed = commit;

                mi_segments_track_size((long)segment_size, tld);
            }

            mi_assert_internal((MI_DEBUG > 1) && (segment != null) && (((nuint)segment % MI_SEGMENT_SIZE) == 0));
            mi_assert_internal((MI_DEBUG > 1) && (segment->mem_is_fixed ? segment->mem_is_committed : true));

            // tsan
            mi_atomic_store_ptr_release<mi_segment_t>(ref segment->abandoned_next, null);

            if (!pages_still_good)
            {
                // zero the segment info (but not the `mem` fields)

                nint ofs = (nint)offsetof(segment, &segment->next);
                memset((byte*)segment + ofs, 0, info_size - (nuint)ofs);

                // initialize pages info
                for (byte i = 0; i < capacity; i++)
                {
                    mi_page_t* page = &segment->pages.e0 + i;

                    page->segment_idx = i;
                    page->is_reset = false;
                    page->is_committed = commit;
                    page->is_zero_init = is_zero;
                }
            }
            else
            {
                // zero the segment info but not the pages info (and mem fields)
                nint ofs = (nint)offsetof(segment, &segment->next);
                memset((byte*)segment + ofs, 0, offsetof(segment, &segment->pages) - (nuint)ofs);
            }

            // initialize
            segment->page_kind = page_kind;
            segment->capacity = capacity;
            segment->page_shift = page_shift;
            segment->segment_size = segment_size;
            segment->segment_info_size = pre_size;
            segment->thread_id = _mi_thread_id();
            segment->cookie = _mi_ptr_cookie(segment);

            _mi_stat_increase(ref tld->stats->page_committed, segment->segment_info_size);

            // set protection
            mi_segment_protect(segment, true, tld->os);

            // insert in free lists for small and medium pages
            if (page_kind <= MI_PAGE_MEDIUM)
            {
                mi_segment_insert_in_free_queue(segment, tld);
            }

            _mi_verbose_message("mimalloc: alloc segment at {0:X}\n", (nuint)segment);
            return segment;
#pragma warning restore CS0420
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static mi_segment_t* mi_segment_alloc([NativeTypeName("size_t")] nuint required, mi_page_kind_t page_kind, [NativeTypeName("size_t")] nuint page_shift, mi_segments_tld_t* tld, mi_os_tld_t* os_tld)
            => mi_segment_init(null, required, page_kind, page_shift, tld, os_tld);

        private static void mi_segment_free(mi_segment_t* segment, bool force, mi_segments_tld_t* tld)
        {
            mi_assert((MI_DEBUG != 0) && (segment != null));

            // note: don't reset pages even on abandon as the whole segment is freed? (and ready for reuse)
            bool force_reset = force && mi_option_is_enabled(mi_option_abandoned_page_reset);

            mi_pages_reset_remove_all_in_segment(segment, force_reset, tld);
            mi_segment_remove_from_free_queue(segment, tld);

            mi_assert_expensive((MI_DEBUG > 2) && !mi_segment_queue_contains(&tld->small_free, segment));
            mi_assert_expensive((MI_DEBUG > 2) && !mi_segment_queue_contains(&tld->medium_free, segment));

            mi_assert((MI_DEBUG != 0) && (segment->next == null));
            mi_assert((MI_DEBUG != 0) && (segment->prev == null));

            _mi_stat_decrease(ref tld->stats->page_committed, segment->segment_info_size);

            if (!force && mi_segment_cache_push(segment, tld))
            {
                // it is put in our cache
            }
            else
            {
                // otherwise return it to the OS
                mi_segment_os_free(segment, segment->segment_size, tld);
            }
        }

        /* -----------------------------------------------------------
          Free page management inside a segment
        ----------------------------------------------------------- */

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool mi_segment_has_free([NativeTypeName("const mi_segment_t*")] mi_segment_t* segment) => segment->used < segment->capacity;

        private static bool mi_segment_page_claim(mi_segment_t* segment, mi_page_t* page, mi_segments_tld_t* tld)
        {
            mi_assert_internal((MI_DEBUG > 1) && (_mi_page_segment(page) == segment));
            mi_assert_internal((MI_DEBUG > 1) && (!page->segment_in_use));

            mi_pages_reset_remove(page, tld);

            // check commit
            if (!page->is_committed)
            {
                mi_assert_internal((MI_DEBUG > 1) && (!segment->mem_is_fixed));
                mi_assert_internal((MI_DEBUG > 1) && (!page->is_reset));

                byte* start = mi_segment_raw_page_start(segment, page, out nuint psize);
                nuint gsize = (MI_SECURE >= 2) ? _mi_os_page_size() : 0;

                bool ok = _mi_mem_commit(start, psize + gsize, out bool is_zero, tld->os);

                if (!ok)
                {
                    // failed to commit!
                    return false;
                }

                if (gsize > 0)
                {
                    mi_segment_protect_range(start + psize, gsize, true);
                }

                if (is_zero)
                {
                    page->is_zero_init = true;
                }

                page->is_committed = true;
            }

            // set in-use before doing unreset to prevent delayed reset
            page->segment_in_use = true;

            segment->used++;

            // check reset
            if (page->is_reset)
            {
                mi_assert_internal((MI_DEBUG > 1) && (!segment->mem_is_fixed));
                bool ok = mi_page_unreset(segment, page, 0, tld);

                if (!ok)
                {
                    page->segment_in_use = false;
                    segment->used--;
                    return false;
                }
            }

            mi_assert_internal((MI_DEBUG > 1) && page->segment_in_use);
            mi_assert_internal((MI_DEBUG > 1) && (segment->used <= segment->capacity));

            if ((segment->used == segment->capacity) && (segment->page_kind <= MI_PAGE_MEDIUM))
            {
                // if no more free pages, remove from the queue
                mi_assert_internal((MI_DEBUG > 1) && (!mi_segment_has_free(segment)));
                mi_segment_remove_from_free_queue(segment, tld);
            }

            return true;
        }

        /* -----------------------------------------------------------
           Free
        ----------------------------------------------------------- */

        private static partial void mi_segment_abandon(mi_segment_t* segment, mi_segments_tld_t* tld);

        // clear page data; can be called on abandoned segments
        private static void mi_segment_page_clear(mi_segment_t* segment, mi_page_t* page, bool allow_reset, mi_segments_tld_t* tld)
        {
            mi_assert_internal((MI_DEBUG > 1) && page->segment_in_use);
            mi_assert_internal((MI_DEBUG > 1) && mi_page_all_free(page));
            mi_assert_internal((MI_DEBUG > 1) && page->is_committed);
            mi_assert_internal((MI_DEBUG > 1) && mi_page_not_in_queue(page, tld));

            nuint inuse = page->capacity * mi_page_block_size(page);

            _mi_stat_decrease(ref tld->stats->page_committed, inuse);
            _mi_stat_decrease(ref tld->stats->pages, 1);

            // calculate the used size from the raw (non-aligned) start of the page
            // _mi_segment_page_start(segment, page, page->block_size, null, out nuint pre_size);
            // size_t used_size = pre_size + (page->capacity * page->block_size);

            page->is_zero_init = false;
            page->segment_in_use = false;

            // reset the page memory to reduce memory pressure?
            // note: must come after setting `segment_in_use` to false but before block_size becomes 0
            // mi_page_reset(segment, page, used_size: 0, tld);

            // zero the page data, but not the segment fields and capacity, and block_size (for page size calculations)
            uint block_size = page->xblock_size;
            ushort capacity = page->capacity;
            ushort reserved = page->reserved;

            nint ofs = (nint)offsetof(page, &page->capacity);
            memset((byte*)page + ofs, 0, SizeOf<mi_page_t>() - (nuint)ofs);

            page->capacity = capacity;
            page->reserved = reserved;
            page->xblock_size = block_size;

            segment->used--;

            // add to the free page list for reuse/reset
            if (allow_reset)
            {
                mi_pages_reset_add(segment, page, tld);
            }

            // after reset these can be zero'd now
            page->capacity = 0;
            page->reserved = 0;
        }

        private static partial void _mi_segment_page_free(mi_page_t* page, bool force, mi_segments_tld_t* tld)
        {
            mi_assert((MI_DEBUG != 0) && (page != null));

            mi_segment_t* segment = _mi_page_segment(page);
            mi_assert_expensive((MI_DEBUG > 2) && mi_segment_is_valid(segment, tld));

            mi_reset_delayed(tld);

            // mark it as free now
            mi_segment_page_clear(segment, page, true, tld);

            if (segment->used == 0)
            {
                // no more used pages; remove from the free list and free the segment
                mi_segment_free(segment, force, tld);
            }
            else
            {
                if (segment->used == segment->abandoned)
                {
                    // only abandoned pages; remove from free list and abandon
                    mi_segment_abandon(segment, tld);
                }
                else if (segment->used + 1 == segment->capacity)
                {
                    // for now we only support small and medium pages
                    mi_assert_internal((MI_DEBUG > 1) && (segment->page_kind <= MI_PAGE_MEDIUM));

                    // move back to segments  free list
                    mi_segment_insert_in_free_queue(segment, tld);
                }
            }
        }

        /* -----------------------------------------------------------
        Abandonment

        When threads terminate, they can leave segments with
        live blocks (reached through other threads). Such segments
        are "abandoned" and will be reclaimed by other threads to
        reuse their pages and/or free them eventually

        We maintain a global list of abandoned segments that are
        reclaimed on demand. Since this is shared among threads
        the implementation needs to avoid the A-B-A problem on
        popping abandoned segments: <https://en.wikipedia.org/wiki/ABA_problem>
        We use tagged pointers to avoid accidentially identifying
        reused segments, much like stamped references in Java.
        Secondly, we maintain a reader counter to avoid resetting
        or decommitting segments that have a pending read operation.

        Note: the current implementation is one possible design;
        another way might be to keep track of abandoned segments
        in the regions. This would have the advantage of keeping
        all concurrent code in one place and not needing to deal
        with ABA issues. The drawback is that it is unclear how to
        scan abandoned segments efficiently in that case as they
        would be spread among all other segments in the regions.
        ----------------------------------------------------------- */

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static mi_segment_t* mi_tagged_segment_ptr([NativeTypeName("mi_tagged_segment_t")] nuint ts) => (mi_segment_t*)(ts & ~MI_TAGGED_MASK);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [return: NativeTypeName("mi_tagged_segment_t")]
        private static nuint mi_tagged_segment(mi_segment_t* segment, [NativeTypeName("mi_tagged_segment_t")] nuint ts)
        {
            mi_assert_internal((MI_DEBUG > 1) && (((nuint)segment & MI_TAGGED_MASK) == 0));
            nuint tag = ((ts & MI_TAGGED_MASK) + 1) & MI_TAGGED_MASK;
            return (nuint)segment | tag;
        }

        // This is a list of visited abandoned pages that were full at the time.
        // this list migrates to `abandoned` when that becomes null. The use of
        // this list reduces contention and the rate at which segments are visited.
        [NativeTypeName("std::atomic<mi_segment_t*>")]
        private static volatile nuint abandoned_visited;

        // The abandoned page list (tagged as it supports pop)
        [NativeTypeName("std::atomic<mi_tagged_segment_t>")]
        private static volatile nuint abandoned;

        // Maintain these for debug purposes (these counts may be a bit off)

        [NativeTypeName("std::atomic<uintptr_t>")]
        private static volatile nuint abandoned_count;

        [NativeTypeName("std::atomic<uintptr_t>")]
        private static volatile nuint abandoned_visited_count;

        // We also maintain a count of current readers of the abandoned list
        // in order to prevent resetting/decommitting segment memory if it might
        // still be read.
        [NativeTypeName("std::atomic<uintptr_t>")]
        private static volatile nuint abandoned_readers;

        // Push on the visited list
        private static void mi_abandoned_visited_push(mi_segment_t* segment)
        {
#pragma warning disable CS0420
            mi_assert_internal((MI_DEBUG > 1) && (segment->thread_id == 0));
            mi_assert_internal((MI_DEBUG > 1) && (mi_atomic_load_ptr_relaxed<mi_segment_t>(ref segment->abandoned_next) == null));
            mi_assert_internal((MI_DEBUG > 1) && segment->next == null && segment->prev == null);
            mi_assert_internal((MI_DEBUG > 1) && (segment->used > 0));

            nuint anext = (nuint)mi_atomic_load_ptr_relaxed<mi_segment_t>(ref abandoned_visited);

            do
            {
                mi_atomic_store_ptr_release(ref segment->abandoned_next, (mi_segment_t*)anext);
            }
            while (!mi_atomic_cas_ptr_weak_release(ref abandoned_visited, ref anext, segment));

            mi_atomic_increment_relaxed(ref abandoned_visited_count);
#pragma warning restore CS0420
        }

        // Move the visited list to the abandoned list.
        private static bool mi_abandoned_visited_revisit()
        {
#pragma warning disable CS0420
            // quick check if the visited list is empty
            if (mi_atomic_load_ptr_relaxed<mi_segment_t>(ref abandoned_visited) == null)
            {
                return false;
            }

            // grab the whole visited list
            mi_segment_t* first = mi_atomic_exchange_ptr_acq_rel<mi_segment_t>(ref abandoned_visited, null);

            if (first == null)
            {
                return false;
            }

            // first try to swap directly if the abandoned list happens to be null
            nuint afirst, count;
            nuint ts = mi_atomic_load_relaxed(ref abandoned);

            if (mi_tagged_segment_ptr(ts) == null)
            {
                count = mi_atomic_load_relaxed(ref abandoned_visited_count);
                afirst = mi_tagged_segment(first, ts);

                if (mi_atomic_cas_strong_acq_rel(ref abandoned, ref ts, afirst))
                {
                    mi_atomic_add_relaxed(ref abandoned_count, count);
                    mi_atomic_sub_relaxed(ref abandoned_visited_count, count);

                    return true;
                }
            }

            // find the last element of the visited list: O(n)

            mi_segment_t* last = first;
            mi_segment_t* next;

            while ((next = mi_atomic_load_ptr_relaxed<mi_segment_t>(ref last->abandoned_next)) != null)
            {
                last = next;
            }

            // and atomically prepend to the abandoned list
            // (no need to increase the readers as we don't access the abandoned segments)

            nuint anext = mi_atomic_load_relaxed(ref abandoned);

            do
            {
                count = mi_atomic_load_relaxed(ref abandoned_visited_count);
                mi_atomic_store_ptr_release(ref last->abandoned_next, mi_tagged_segment_ptr(anext));
                afirst = mi_tagged_segment(first, anext);
            }
            while (!mi_atomic_cas_weak_release(ref abandoned, ref anext, afirst));

            mi_atomic_add_relaxed(ref abandoned_count, count);
            mi_atomic_sub_relaxed(ref abandoned_visited_count, count);

            return true;
#pragma warning restore CS0420
        }

        // Push on the abandoned list.
        private static void mi_abandoned_push(mi_segment_t* segment)
        {
#pragma warning disable CS0420
            mi_assert_internal((MI_DEBUG > 1) && (segment->thread_id == 0));
            mi_assert_internal((MI_DEBUG > 1) && (mi_atomic_load_ptr_relaxed<mi_segment_t>(ref segment->abandoned_next) == null));
            mi_assert_internal((MI_DEBUG > 1) && segment->next == null && segment->prev == null);
            mi_assert_internal((MI_DEBUG > 1) && (segment->used > 0));

            nuint next;
            nuint ts = mi_atomic_load_relaxed(ref abandoned);

            do
            {
                mi_atomic_store_ptr_release(ref segment->abandoned_next, mi_tagged_segment_ptr(ts));
                next = mi_tagged_segment(segment, ts);
            }
            while (!mi_atomic_cas_weak_release(ref abandoned, ref ts, next));

            mi_atomic_increment_relaxed(ref abandoned_count);
#pragma warning restore CS0420
        }

        // Wait until there are no more pending reads on segments that used to be in the abandoned list
        private static partial void _mi_abandoned_await_readers()
        {
#pragma warning disable CS0420
            nuint n;

            do
            {
                n = mi_atomic_load_acquire(ref abandoned_readers);

                if (n != 0)
                {
                    mi_atomic_yield();
                }
            }
            while (n != 0);
#pragma warning restore CS0420
        }

        // Pop from the abandoned list
        private static mi_segment_t* mi_abandoned_pop()
        {
#pragma warning disable CS0420
            mi_segment_t* segment;

            // Check efficiently if it is empty (or if the visited list needs to be moved)
            nuint ts = mi_atomic_load_relaxed(ref abandoned);

            segment = mi_tagged_segment_ptr(ts);

            if (mi_likely(segment == null) && mi_likely(!mi_abandoned_visited_revisit()))
            {
                // try to swap in the visited list on null
                return null;
            }

            // Do a pop. We use a reader count to prevent
            // a segment to be decommitted while a read is still pending,
            // and a tagged pointer to prevent A-B-A link corruption.
            // (this is called from `region.c:_mi_mem_free` for example)

            // ensure no segment gets decommitted
            mi_atomic_increment_relaxed(ref abandoned_readers);

            nuint next = 0;
            ts = mi_atomic_load_acquire(ref abandoned);

            do
            {
                segment = mi_tagged_segment_ptr(ts);

                if (segment != null)
                {
                    mi_segment_t* anext = mi_atomic_load_ptr_relaxed<mi_segment_t>(ref segment->abandoned_next);

                    // note: reads the segment's `abandoned_next` field so should not be decommitted
                    next = mi_tagged_segment(anext, ts);
                }
            }
            while ((segment != null) && !mi_atomic_cas_weak_acq_rel(ref abandoned, ref ts, next));

            // release reader lock
            mi_atomic_decrement_relaxed(ref abandoned_readers);

            if (segment != null)
            {
                mi_atomic_store_ptr_release(ref segment->abandoned_next, null);
                mi_atomic_decrement_relaxed(ref abandoned_count);
            }

            return segment;
#pragma warning restore CS0420
        }

        /* -----------------------------------------------------------
           Abandon segment/page
        ----------------------------------------------------------- */

        private static partial void mi_segment_abandon(mi_segment_t* segment, mi_segments_tld_t* tld)
        {
#pragma warning disable CS0420
            mi_assert_internal((MI_DEBUG > 1) && (segment->used == segment->abandoned));
            mi_assert_internal((MI_DEBUG > 1) && (segment->used > 0));
            mi_assert_internal((MI_DEBUG > 1) && (mi_atomic_load_ptr_relaxed<mi_segment_t>(ref segment->abandoned_next) == null));
            mi_assert_expensive((MI_DEBUG > 2) && mi_segment_is_valid(segment, tld));

            // remove the segment from the free page queue if needed

            mi_reset_delayed(tld);
            mi_pages_reset_remove_all_in_segment(segment, mi_option_is_enabled(mi_option_abandoned_page_reset), tld);
            mi_segment_remove_from_free_queue(segment, tld);
            mi_assert_internal((MI_DEBUG > 1) && segment->next == null && segment->prev == null);

            // all pages in the segment are abandoned; add it to the abandoned list

            _mi_stat_increase(ref tld->stats->segments_abandoned, 1);
            mi_segments_track_size(-(long)segment->segment_size, tld);

            segment->thread_id = 0;
            segment->abandoned_visits = 0;

            mi_atomic_store_ptr_release<mi_segment_t>(ref segment->abandoned_next, null);
            mi_abandoned_push(segment);
#pragma warning restore CS0420
        }

        private static partial void _mi_segment_page_abandon(mi_page_t* page, mi_segments_tld_t* tld)
        {
            mi_assert((MI_DEBUG != 0) && (page != null));
            mi_assert_internal((MI_DEBUG > 1) && (mi_page_thread_free_flag(page) == MI_NEVER_DELAYED_FREE));
            mi_assert_internal((MI_DEBUG > 1) && (mi_page_heap(page) == null));

            mi_segment_t* segment = _mi_page_segment(page);

            mi_assert_expensive((MI_DEBUG > 2) && !mi_pages_reset_contains(page, tld));
            mi_assert_expensive((MI_DEBUG > 2) && mi_segment_is_valid(segment, tld));

            segment->abandoned++;
            _mi_stat_increase(ref tld->stats->pages_abandoned, 1);

            mi_assert_internal((MI_DEBUG > 1) && (segment->abandoned <= segment->used));

            if (segment->used == segment->abandoned)
            {
                // all pages are abandoned, abandon the entire segment
                mi_segment_abandon(segment, tld);
            }
        }

        /* -----------------------------------------------------------
          Reclaim abandoned pages
        ----------------------------------------------------------- */

        // Possibly clear pages and check if free space is available
        private static bool mi_segment_check_free(mi_segment_t* segment, [NativeTypeName("size_t")] nuint block_size, [NativeTypeName("bool*")] out bool all_pages_free)
        {
            mi_assert_internal((MI_DEBUG > 1) && (block_size < MI_HUGE_BLOCK_SIZE));
            bool has_page = false;

            nuint pages_used = 0;
            nuint pages_used_empty = 0;

            for (nuint i = 0; i < segment->capacity; i++)
            {
                mi_page_t* page = &segment->pages.e0 + i;

                if (page->segment_in_use)
                {
                    pages_used++;

                    // ensure used count is up to date and collect potential concurrent frees
                    _mi_page_free_collect(page, false);

                    if (mi_page_all_free(page))
                    {
                        // if everything free already, page can be reused for some block size
                        // note: don't clear the page yet as we can only OS reset it once it is reclaimed

                        pages_used_empty++;
                        has_page = true;
                    }
                    else if ((page->xblock_size == block_size) && mi_page_has_any_available(page))
                    {
                        // a page has available free blocks of the right size
                        has_page = true;
                    }
                }
                else
                {
                    // whole empty page
                    has_page = true;
                }
            }

            mi_assert_internal((MI_DEBUG > 1) && pages_used == segment->used && pages_used >= pages_used_empty);
            all_pages_free = (pages_used - pages_used_empty) == 0;

            return has_page;
        }

        // Reclaim a segment; returns null if the segment was freed
        // set `right_page_reclaimed` to `true` if it reclaimed a page of the right `block_size` that was not full.
        private static mi_segment_t* mi_segment_reclaim(mi_segment_t* segment, mi_heap_t* heap, [NativeTypeName("size_t")] nuint requested_block_size, [NativeTypeName("bool*")] out bool right_page_reclaimed, mi_segments_tld_t* tld)
        {
#pragma warning disable CS0420
            mi_assert_internal((MI_DEBUG > 1) && (mi_atomic_load_ptr_relaxed<mi_segment_t>(ref segment->abandoned_next) == null));
            right_page_reclaimed = false;

            segment->thread_id = _mi_thread_id();
            segment->abandoned_visits = 0;

            mi_segments_track_size((long)segment->segment_size, tld);

            mi_assert_internal((MI_DEBUG > 1) && segment->next == null && segment->prev == null);
            mi_assert_expensive((MI_DEBUG > 2) && mi_segment_is_valid(segment, tld));

            _mi_stat_decrease(ref tld->stats->segments_abandoned, 1);

            for (nuint i = 0; i < segment->capacity; i++)
            {
                mi_page_t* page = &segment->pages.e0 + i;

                if (page->segment_in_use)
                {
                    mi_assert_internal((MI_DEBUG > 1) && (!page->is_reset));
                    mi_assert_internal((MI_DEBUG > 1) && page->is_committed);
                    mi_assert_internal((MI_DEBUG > 1) && mi_page_not_in_queue(page, tld));
                    mi_assert_internal((MI_DEBUG > 1) && (mi_page_thread_free_flag(page) == MI_NEVER_DELAYED_FREE));
                    mi_assert_internal((MI_DEBUG > 1) && (mi_page_heap(page) == null));

                    segment->abandoned--;

                    mi_assert((MI_DEBUG != 0) && (page->next == null));

                    _mi_stat_decrease(ref tld->stats->pages_abandoned, 1);

                    // set the heap again and allow heap thread delayed free again.
                    mi_page_set_heap(page, heap);

                    // override never (after heap is set)
                    _mi_page_use_delayed_free(page, MI_USE_DELAYED_FREE, true);

                    // TODO: should we not collect again given that we just collected in `check_free`?

                    // ensure used count is up to date
                    _mi_page_free_collect(page, false);

                    if (mi_page_all_free(page))
                    {
                        // if everything free already, clear the page directly; reset is ok now
                        mi_segment_page_clear(segment, page, true, tld);
                    }
                    else
                    {
                        // otherwise reclaim it into the heap
                        _mi_page_reclaim(heap, page);

                        if ((requested_block_size == page->xblock_size) && mi_page_has_any_available(page))
                        {
                            right_page_reclaimed = true;
                        }
                    }
                }
                else if (page->is_committed && !page->is_reset)
                {
                    // not in-use, and not reset yet
                    // note: do not reset as this includes pages that were not touched before
                    // mi_pages_reset_add(segment, page, tld);
                }
            }

            mi_assert_internal((MI_DEBUG > 1) && (segment->abandoned == 0));

            if (segment->used == 0)
            {
                mi_assert_internal((MI_DEBUG > 1) && (!right_page_reclaimed));

                mi_segment_free(segment, false, tld);
                return null;
            }
            else
            {
                if ((segment->page_kind <= MI_PAGE_MEDIUM) && mi_segment_has_free(segment))
                {
                    mi_segment_insert_in_free_queue(segment, tld);
                }
                return segment;
            }
#pragma warning restore CS0420
        }

        private static partial void _mi_abandoned_reclaim_all(mi_heap_t* heap, mi_segments_tld_t* tld)
        {
            mi_segment_t* segment;

            while ((segment = mi_abandoned_pop()) != null)
            {
                mi_segment_reclaim(segment, heap, 0, out _, tld);
            }
        }

        private static mi_segment_t* mi_segment_try_reclaim(mi_heap_t* heap, [NativeTypeName("size_t")] nuint block_size, mi_page_kind_t page_kind, [NativeTypeName("bool*")] out bool reclaimed, mi_segments_tld_t* tld)
        {
            reclaimed = false;
            mi_segment_t* segment;

            // limit the work to bound allocation times
            int max_tries = 8;

            while ((max_tries-- > 0) && ((segment = mi_abandoned_pop()) != null))
            {
                segment->abandoned_visits++;

                // try to free up pages (due to concurrent frees)
                bool has_page = mi_segment_check_free(segment, block_size, out bool all_pages_free);

                if (all_pages_free)
                {
                    // free the segment (by forced reclaim) to make it available to other threads.
                    // note1: we prefer to free a segment as that might lead to reclaiming another
                    // segment that is still partially used.
                    // note2: we could in principle optimize this by skipping reclaim and directly
                    // freeing but that would violate some invariants temporarily)
                    mi_segment_reclaim(segment, heap, 0, out _, tld);
                }
                else if (has_page && segment->page_kind == page_kind)
                {
                    // found a free page of the right kind, or page of the right block_size with free space
                    // we return the result of reclaim (which is usually `segment`) as it might free
                    // the segment due to concurrent frees (in which case `null` is returned).
                    return mi_segment_reclaim(segment, heap, block_size, out reclaimed, tld);
                }
                else if (segment->abandoned_visits >= 3)
                {
                    // always reclaim on 3rd visit to limit the list length.
                    mi_segment_reclaim(segment, heap, 0, out _, tld);
                }
                else
                {
                    // otherwise, push on the visited list so it gets not looked at too quickly again
                    mi_abandoned_visited_push(segment);
                }
            }

            return null;
        }

        /* -----------------------------------------------------------
           Reclaim or allocate
        ----------------------------------------------------------- */

        private static mi_segment_t* mi_segment_reclaim_or_alloc(mi_heap_t* heap, [NativeTypeName("size_t")] nuint block_size, mi_page_kind_t page_kind, [NativeTypeName("size_t")] nuint page_shift, mi_segments_tld_t* tld, mi_os_tld_t* os_tld)
        {
            mi_assert_internal((MI_DEBUG > 1) && (page_kind <= MI_PAGE_LARGE));
            mi_assert_internal((MI_DEBUG > 1) && (block_size < MI_HUGE_BLOCK_SIZE));

            // 1. try to get a segment from our cache
            mi_segment_t* segment = mi_segment_cache_pop(MI_SEGMENT_SIZE, tld);

            if (segment != null)
            {
                mi_segment_init(segment, 0, page_kind, page_shift, tld, os_tld);
                return segment;
            }

            // 2. try to reclaim an abandoned segment
            segment = mi_segment_try_reclaim(heap, block_size, page_kind, out bool reclaimed, tld);

            if (reclaimed)
            {
                // reclaimed the right page right into the heap
                mi_assert_internal((MI_DEBUG > 1) && (segment != null) && (segment->page_kind == page_kind) && (page_kind <= MI_PAGE_LARGE));

                // pretend out-of-memory as the page will be in the page queue of the heap with available blocks
                return null;
            }
            else if (segment != null)
            {
                // reclaimed a segment with empty pages (of `page_kind`) in it
                return segment;
            }

            // 3. otherwise allocate a fresh segment
            return mi_segment_alloc(0, page_kind, page_shift, tld, os_tld);
        }

        /* -----------------------------------------------------------
           Small page allocation
        ----------------------------------------------------------- */

        private static mi_page_t* mi_segment_find_free(mi_segment_t* segment, mi_segments_tld_t* tld)
        {
            mi_assert_internal((MI_DEBUG > 1) && mi_segment_has_free(segment));
            mi_assert_expensive((MI_DEBUG > 2) && mi_segment_is_valid(segment, tld));

            for (nuint i = 0; i < segment->capacity; i++)
            {
                // TODO: use a bitmap instead of search?
                mi_page_t* page = &segment->pages.e0 + i;

                if (!page->segment_in_use)
                {
                    bool ok = mi_segment_page_claim(segment, page, tld);

                    if (ok)
                    {
                        return page;
                    }
                }
            }

            mi_assert((MI_DEBUG != 0) && false);
            return null;
        }

        // Allocate a page inside a segment. Requires that the page has free pages
        private static mi_page_t* mi_segment_page_alloc_in(mi_segment_t* segment, mi_segments_tld_t* tld)
        {
            mi_assert_internal((MI_DEBUG > 1) && mi_segment_has_free(segment));
            return mi_segment_find_free(segment, tld);
        }

        private static mi_page_t* mi_segment_page_alloc(mi_heap_t* heap, [NativeTypeName("size_t")] nuint block_size, mi_page_kind_t kind, [NativeTypeName("size_t")] nuint page_shift, mi_segments_tld_t* tld, mi_os_tld_t* os_tld)
        {
            // find an available segment the segment free queue
            mi_segment_queue_t* free_queue = mi_segment_free_queue_of_kind(kind, tld);

            if (mi_segment_queue_is_empty(free_queue))
            {
                // possibly allocate or reclaim a fresh segment
                mi_segment_t* segment = mi_segment_reclaim_or_alloc(heap, block_size, kind, page_shift, tld, os_tld);

                if (segment == null)
                {
                    // return null if out-of-memory (or reclaimed)
                    return null;
                }

                mi_assert_internal((MI_DEBUG > 1) && (free_queue->first == segment));
                mi_assert_internal((MI_DEBUG > 1) && (segment->page_kind == kind));
                mi_assert_internal((MI_DEBUG > 1) && (segment->used < segment->capacity));
            }

            mi_assert_internal((MI_DEBUG > 1) && (free_queue->first != null));

            mi_page_t* page = mi_segment_page_alloc_in(free_queue->first, tld);
            mi_assert_internal((MI_DEBUG > 1) && (page != null));

            if (MI_DEBUG >= 2)
            {
                // verify it is committed
                _mi_segment_page_start(_mi_page_segment(page), page, SizeOf<nuint>(), out _, out _)[0] = 0;
            }

            return page;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static mi_page_t* mi_segment_small_page_alloc(mi_heap_t* heap, [NativeTypeName("size_t")] nuint block_size, mi_segments_tld_t* tld, mi_os_tld_t* os_tld)
            => mi_segment_page_alloc(heap, block_size, MI_PAGE_SMALL, (nuint)MI_SMALL_PAGE_SHIFT, tld, os_tld);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static mi_page_t* mi_segment_medium_page_alloc(mi_heap_t* heap, [NativeTypeName("size_t")] nuint block_size, mi_segments_tld_t* tld, mi_os_tld_t* os_tld)
            => mi_segment_page_alloc(heap, block_size, MI_PAGE_MEDIUM, (nuint)MI_MEDIUM_PAGE_SHIFT, tld, os_tld);

        /* -----------------------------------------------------------
           large page allocation
        ----------------------------------------------------------- */

        private static mi_page_t* mi_segment_large_page_alloc(mi_heap_t* heap, [NativeTypeName("size_t")] nuint block_size, mi_segments_tld_t* tld, mi_os_tld_t* os_tld)
        {
            mi_segment_t* segment = mi_segment_reclaim_or_alloc(heap, block_size, MI_PAGE_LARGE, (nuint)MI_LARGE_PAGE_SHIFT, tld, os_tld);

            if (segment == null)
            {
                return null;
            }

            mi_page_t* page = mi_segment_find_free(segment, tld);
            mi_assert_internal((MI_DEBUG > 1) && (page != null));

            if (MI_DEBUG >= 2)
            {
                _mi_segment_page_start(segment, page, SizeOf<nuint>(), out _, out _)[0] = 0;
            }

            return page;
        }

        private static mi_page_t* mi_segment_huge_page_alloc([NativeTypeName("size_t")] nuint size, mi_segments_tld_t* tld, mi_os_tld_t* os_tld)
        {
            mi_segment_t* segment = mi_segment_alloc(size, MI_PAGE_HUGE, (nuint)MI_SEGMENT_SHIFT, tld, os_tld);

            if (segment == null)
            {
                return null;
            }

            mi_assert_internal((MI_DEBUG > 1) && ((mi_segment_page_size(segment) - segment->segment_info_size - (2 * ((MI_SECURE == 0) ? 0 : _mi_os_page_size()))) >= size));

            // huge pages are immediately abandoned
            segment->thread_id = 0;

            mi_segments_track_size(-(long)segment->segment_size, tld);
            mi_page_t* page = mi_segment_find_free(segment, tld);

            mi_assert_internal((MI_DEBUG > 1) && (page != null));
            return page;
        }

        // free huge block from another thread
        private static partial void _mi_segment_huge_page_free(mi_segment_t* segment, mi_page_t* page, mi_block_t* block)
        {
#pragma warning disable CS0420
            // huge page segments are always abandoned and can be freed immediately by any thread

            mi_assert_internal((MI_DEBUG > 1) && (segment->page_kind == MI_PAGE_HUGE));
            mi_assert_internal((MI_DEBUG > 1) && (segment == _mi_page_segment(page)));
            mi_assert_internal((MI_DEBUG > 1) && (mi_atomic_load_relaxed(ref segment->thread_id) == 0));

            // claim it and free
            mi_heap_t* heap = (mi_heap_t*)mi_heap_get_default();

            nuint expected_tid = 0;
            if (mi_atomic_cas_strong_acq_rel(ref segment->thread_id, ref expected_tid, heap->thread_id))
            {
                mi_block_set_next(page, block, page->free);

                page->free = block;
                page->used--;
                page->is_zero = false;

                mi_assert((MI_DEBUG != 0) && (page->used == 0));

                mi_tld_t* tld = heap->tld;
                nuint bsize = mi_page_usable_block_size(page);

                if (bsize > MI_HUGE_OBJ_SIZE_MAX)
                {
                    _mi_stat_decrease(ref tld->stats.giant, bsize);
                }
                else
                {
                    _mi_stat_decrease(ref tld->stats.huge, bsize);
                }

                mi_segments_track_size((long)segment->segment_size, &tld->segments);
                _mi_segment_page_free(page, true, &tld->segments);
            }
            else if (MI_DEBUG != 0)
            {
                mi_assert_internal((MI_DEBUG > 1) && false);
            }
#pragma warning restore CS0420
        }

        /* -----------------------------------------------------------
           Page allocation
        ----------------------------------------------------------- */

        private static partial mi_page_t* _mi_segment_page_alloc(mi_heap_t* heap, nuint block_size, mi_segments_tld_t* tld, mi_os_tld_t* os_tld)
        {
            mi_page_t* page;

            if (block_size <= MI_SMALL_OBJ_SIZE_MAX)
            {
                page = mi_segment_small_page_alloc(heap, block_size, tld, os_tld);
            }
            else if (block_size <= MI_MEDIUM_OBJ_SIZE_MAX)
            {
                page = mi_segment_medium_page_alloc(heap, block_size, tld, os_tld);
            }
            else if (block_size <= MI_LARGE_OBJ_SIZE_MAX)
            {
                page = mi_segment_large_page_alloc(heap, block_size, tld, os_tld);
            }
            else
            {
                page = mi_segment_huge_page_alloc(block_size, tld, os_tld);
            }

            mi_assert_expensive((MI_DEBUG > 2) && ((page == null) || mi_segment_is_valid(_mi_page_segment(page), tld)));
            mi_assert_internal((MI_DEBUG > 1) && ((page == null) || mi_segment_page_size(_mi_page_segment(page)) - (MI_SECURE == 0 ? 0 : _mi_os_page_size()) >= block_size));

            mi_reset_delayed(tld);

            mi_assert_internal((MI_DEBUG > 1) && ((page == null) || mi_page_not_in_queue(page, tld)));
            return page;
        }
    }
}
