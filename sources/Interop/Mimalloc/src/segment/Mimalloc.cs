// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the segment.c file from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

using System.Runtime.CompilerServices;
using static TerraFX.Interop.Mimalloc.mi_delayed_t;
using static TerraFX.Interop.Mimalloc.mi_option_t;
using static TerraFX.Interop.Mimalloc.mi_page_kind_t;

namespace TerraFX.Interop.Mimalloc;

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

    private static bool mi_pages_purge_contains([NativeTypeName("const mi_page_t*")] mi_page_t* page, mi_segments_tld_t* tld)
    {
        mi_assert_internal((MI_DEBUG > 1) && (MI_DEBUG >= 2));
        mi_page_t* p = tld->pages_purge.first;

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

            if (page->segment_in_use)
            {
                mi_assert_expensive((MI_DEBUG > 2) && !mi_pages_purge_contains(page, tld));
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
            mi_assert_internal((MI_DEBUG > 1) && mi_pages_purge_contains(page, tld));
            return false;
        }
        else
        {
            // both next and prev are null, check for singleton list
            return (tld->pages_purge.first != page) && (tld->pages_purge.last != page);
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
            if (!_mi_os_protect(p, size))
            {
                _mi_error_message(EFAULT, "unable to protect segment memory at {0:X}\n", (nuint)p);
            }
        }
        else
        {
            _ = _mi_os_unprotect(p, size);
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

            if ((MI_SECURE < 5) || (segment->capacity == 1))
            {
                // and protect the last (or only) page too
                mi_assert_internal((MI_DEBUG > 1) && (MI_SECURE >= 1 || segment->page_kind >= MI_PAGE_LARGE));

                byte* start = (byte*)segment + segment->segment_size - os_psize;

                if (protect && !segment->mem_is_committed)
                {
                    if (protect)
                    {
                        // ensure secure page is committed
                        if (_mi_os_commit(start, os_psize, out _, ref *tld->stats))
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
                        mi_segment_protect_range((byte*)segment + ((i + 1) * page_size) - os_psize, os_psize, protect);
                    }
                }
            }
        }
    }

    private static void mi_page_purge(mi_segment_t* segment, mi_page_t* page, mi_segments_tld_t* tld)
    {
        mi_assert_internal((MI_DEBUG > 1) && !page->segment_in_use);

        if (!segment->allow_purge || !page->is_committed)
        {
            return;
        }

        mi_assert_internal((MI_DEBUG > 1) && (page->used == 0));
        mi_assert_internal((MI_DEBUG > 1) && (page->free == null));
        mi_assert_expensive((MI_DEBUG > 2) && !mi_pages_purge_contains(page, tld));

        void* start = mi_segment_raw_page_start(segment, page, out nuint page_size);
        bool needs_recommit = _mi_os_purge(start, page_size, ref *tld->os->stats);

        if (needs_recommit)
        {
            page->is_committed = false;
        }

    }

    private static bool mi_page_ensure_committed(mi_segment_t* segment, mi_page_t* page, mi_segments_tld_t* tld)
    {
        if (page->is_committed)
        {
            return true;
        }

        mi_assert_internal((MI_DEBUG > 1) && segment->allow_decommit);
        mi_assert_expensive((MI_DEBUG > 2) && !mi_pages_purge_contains(page, tld));

        byte* start = mi_segment_raw_page_start(segment, page, out nuint page_size);
        nuint guard_size = (MI_SECURE >= 5) ? _mi_os_page_size() : 0;

        if (!_mi_os_commit(start, page_size + guard_size, out bool is_zero, ref *tld->os->stats))
        {
            return false;
        }

        page->is_committed = true;
        page->used = 0;
        page->free = null;
        page->is_zero_init = is_zero;

        if (guard_size > 0)
        {
            mi_segment_protect_range(start + page_size, guard_size, true);
        }

        return true;
    }

    /* -----------------------------------------------------------
      The delayed page purge queue
    ----------------------------------------------------------- */

    // we re-use the `free` field for the expiration counter. Since this is a
    // a pointer-sized field while the clock is always 64-bit we need to guard
    // against overflow, we use substraction to check for expiry which work
    // as long as the reset delay is under (2^30 - 1) milliseconds (~12 days)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint mi_page_get_expire(mi_page_t* page)
        => (uint)(nuint)page->free;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void mi_page_set_expire(mi_page_t* page, uint expire)
        => page->free = (mi_block_t*)(nuint)expire;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void mi_page_purge_set_expire(mi_page_t* page)
    {
        mi_assert_internal((MI_DEBUG > 1) && (mi_page_get_expire(page) == 0));
        uint expire = unchecked((uint)_mi_clock_now() + (uint)mi_option_get(mi_option_purge_delay));
        mi_page_set_expire(page, expire);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool mi_page_purge_is_expired(mi_page_t* page, [NativeTypeName("mi_msecs_t")] long now)
    {
        int expire = unchecked((int)mi_page_get_expire(page));
        return unchecked((int)now - expire) >= 0;
    }

    private static void mi_segment_schedule_purge(mi_segment_t* segment, mi_page_t* page, mi_segments_tld_t* tld)
    {
        mi_assert_internal((MI_DEBUG > 1) && !page->segment_in_use);
        mi_assert_internal((MI_DEBUG > 1) && mi_page_not_in_queue(page, tld));
        mi_assert_expensive((MI_DEBUG > 2) && !mi_pages_purge_contains(page, tld));
        mi_assert_internal((MI_DEBUG > 1) && (_mi_page_segment(page) == segment));

        int purge_delay = mi_option_get(mi_option_purge_delay);

        if (!segment->allow_purge || (purge_delay < 0))
        {
            return;
        }

        if (purge_delay == 0)
        {
            mi_page_purge(segment, page, tld);
        }
        else
        {
            mi_page_queue_t* pq = &tld->pages_purge;

            mi_page_purge_set_expire(page);

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

    private static void mi_page_purge_remove(mi_page_t* page, mi_segments_tld_t* tld)
    {
        if (mi_page_not_in_queue(page, tld))
        {
            return;
        }

        mi_page_queue_t* pq = &tld->pages_purge;

        mi_assert_internal((MI_DEBUG > 1) && (pq != null));
        mi_assert_internal((MI_DEBUG > 1) && (!page->segment_in_use));
        mi_assert_internal((MI_DEBUG > 1) && (mi_page_get_expire(page) != 0));
        mi_assert_internal((MI_DEBUG > 1) && mi_pages_purge_contains(page, tld));

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
        mi_page_set_expire(page, 0);
    }

    private static void mi_segment_remove_all_purges(mi_segment_t* segment, bool force_purge, mi_segments_tld_t* tld)
    {
        if (segment->mem_is_pinned)
        {
            // never purge huge OS pages
            return;
        }

        for (nuint i = 0; i < segment->capacity; i++)
        {
            mi_page_t* page = &segment->pages.e0 + i;

            if (!page->segment_in_use)
            {
                mi_page_purge_remove(page, tld);
                if (force_purge && page->is_committed)
                {
                    mi_page_purge(segment, page, tld);
                }
            }
            else
            {
                mi_assert_internal((MI_DEBUG > 1) && mi_page_not_in_queue(page, tld));
            }
        }
    }

    private static void mi_pages_try_purge(bool force, mi_segments_tld_t* tld)
    {
        if (mi_option_get(mi_option_purge_delay) < 0)
        {
            return;
        }

        long now = _mi_clock_now();
        // from oldest up to the first that has not expired yet
        mi_page_t* page = tld->pages_purge.last;

        while ((page != null) && (force || mi_page_purge_is_expired(page, now)))
        {
            // save previous field
            mi_page_t* prev = page->prev;

            mi_page_purge_remove(page, tld);
            mi_page_purge(_mi_page_segment(page), page, tld);

            page = prev;
        }
    }

    /* -----------------------------------------------------------
     Segment size calculations
    ----------------------------------------------------------- */

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint mi_segment_raw_page_size(mi_segment_t* segment)
        => (segment->page_kind == MI_PAGE_HUGE) ? segment->segment_size : ((nuint)1 << (int)segment->page_shift);

    // Raw start of the page available memory; can be used on uninitialized pages (only `segment_idx` must be set)
    // The raw start is not taking aligned block allocation into consideration.
    private static partial byte* mi_segment_raw_page_start(mi_segment_t* segment, mi_page_t* page, out nuint page_size)
    {
        nuint psize = mi_segment_raw_page_size(segment);
        byte* p = (byte*)segment + (page->segment_idx * psize);

        if (page->segment_idx == 0)
        {
            // the first page starts after the segment info (and possible guard page)
            p += segment->segment_info_size;
            psize -= segment->segment_info_size;
        }

        if (MI_SECURE >= 5)
        {
            psize -= _mi_os_page_size();
        }
        else if ((MI_SECURE >= 1) && (page->segment_idx == (segment->capacity - 1)))
        {
            psize -= _mi_os_page_size();
        }

        page_size = psize;

        mi_assert_internal((MI_DEBUG > 1) && ((page->block_size == 0) || (_mi_ptr_page(p) == page)));
        mi_assert_internal((MI_DEBUG > 1) && (_mi_ptr_segment(p) == segment));

        return p;
    }

    // Start of the page area, adjusted for block-size alignment when needed
    private static partial byte* _mi_segment_page_start(mi_segment_t* segment, mi_page_t* page, out nuint page_size, out nuint pre_size)
    {
        byte* p = mi_segment_raw_page_start(segment, page, out nuint psize);
        nuint block_size = page->block_size;
        pre_size = 0;
        mi_assert_internal((MI_DEBUG > 1) && (block_size > 0));

        if ((block_size > 0) && (block_size <= MI_MAX_ALIGN_GUARANTEE))
        {
            mi_assert_internal((MI_DEBUG > 1) && (segment->page_kind <= MI_PAGE_MEDIUM));

            // for small and medium objects, ensure the page start is aligned with the block size (PR#66 by kickunderscore)
            nuint adjust = block_size - ((nuint)p % block_size);

            if ((adjust < block_size) && (psize >= (block_size + adjust)))
            {
                p += adjust;
                psize -= adjust;
                pre_size = adjust;

                mi_assert_internal((MI_DEBUG > 1) && (((nuint)p % block_size) == 0));
            }
        }

        page_size = psize;

        mi_assert_internal((MI_DEBUG > 1) && ((page->block_size == 0) || (_mi_ptr_page(p) == page)));
        mi_assert_internal((MI_DEBUG > 1) && (_mi_ptr_segment(p) == segment));
        mi_assert_internal((MI_DEBUG > 1) && (((nuint)p % MI_MAX_ALIGN_SIZE) == 0));
        mi_assert_internal((MI_DEBUG > 1) && ((block_size > MI_MAX_ALIGN_GUARANTEE) || (((nuint)p % block_size) == 0)));

        return p;
    }

    [return: NativeTypeName("size_t")]
    private static nuint mi_segment_size([NativeTypeName("size_t")] nuint capacity, [NativeTypeName("size_t")] nuint required, [NativeTypeName("size_t*")] out nuint pre_size, [NativeTypeName("size_t*")] out nuint info_size)
    {
        const int padding = 16;
        nuint minsize = SizeOf<mi_segment_t>() + ((capacity - 1) * SizeOf<mi_page_t>()) + padding;

        nuint guardsize = 0;
        nuint isize;
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

        return (required == 0) ? MI_SEGMENT_SIZE : _mi_align_up(required + isize + (2 * guardsize), MI_PAGE_HUGE_ALIGN);
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
        _mi_segment_map_freed_at(segment);
        mi_segments_track_size(-(long)segment_size, tld);
        if (segment->was_reclaimed)
        {
            tld->reclaim_count--;
            segment->was_reclaimed = false;
        }

        if (MI_SECURE != 0)
        {
            mi_assert_internal((MI_DEBUG > 1) && (!segment->mem_is_pinned));
            mi_segment_protect(segment, false, tld->os); // ensure no more guard pages are set
        }

        nuint committed_size = 0;
        nuint page_size = mi_segment_raw_page_size(segment);

        for (nuint i = 0; i < segment->capacity; i++)
        {
            mi_page_t* page = &segment->pages.e0 + i;

            if (page->is_committed)
            {
                committed_size += page_size;
            }
        }

        mi_assert_internal((MI_DEBUG > 1) && ((committed_size == segment_size) || (committed_size < segment_size)));
        _mi_arena_free(segment, segment_size, segment->mem_alignment, segment->mem_align_offset, segment->memid, committed_size, ref *tld->os->stats);
    }

    // Called from heap collection to purge expired segment pages.
    private static partial void _mi_segments_collect(bool force, mi_segments_tld_t* tld)
    {
        mi_pages_try_purge(force, tld);

        if ((MI_DEBUG >= 2) && !_mi_is_main_thread())
        {
            mi_assert_internal((MI_DEBUG > 1) && (tld->pages_purge.first == null));
            mi_assert_internal((MI_DEBUG > 1) && (tld->pages_purge.last == null));
        }
    }

    /* -----------------------------------------------------------
       Segment allocation
    ----------------------------------------------------------- */

    // Allocate a segment from the OS aligned to `MI_SEGMENT_SIZE` .
    private static mi_segment_t* mi_segment_init(mi_segment_t* segment, nuint required, mi_page_kind_t page_kind, nuint page_shift, nuint page_alignment, int req_arena_id, mi_segments_tld_t* tld, mi_os_tld_t* os_tld)
    {
#pragma warning disable CS0420
        // the segment parameter is non-null if a segment is being reused
        mi_assert_internal((MI_DEBUG > 1) && ((segment == null) || ((required == 0) && (page_kind <= MI_PAGE_LARGE))));

        // calculate needed sizes first
        nuint capacity;

        if (page_kind == MI_PAGE_HUGE)
        {
            mi_assert_internal((MI_DEBUG > 1) && (page_shift == (nuint)(MI_SEGMENT_SHIFT + 1)) && (required > 0));
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
        nuint alignment = MI_SEGMENT_SIZE;
        nuint align_offset = 0;

        if (page_alignment > 0)
        {
            alignment = page_alignment;
            align_offset = _mi_align_up(pre_size, MI_SEGMENT_SIZE);
            segment_size += align_offset - pre_size;
        }

        // Initialize parameters
        bool eager_delayed = (page_kind <= MI_PAGE_MEDIUM) && (_mi_current_thread_count() > 1) && (tld->count < (nuint)mi_option_get(mi_option_eager_commit_delay));
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
                    mi_assert_internal((MI_DEBUG > 1) && (!segment->mem_is_pinned));
                    mi_segment_protect(segment, false, tld->os); // reset protection if the page kind differs
                }

                // different page kinds; unreset any reset pages, and unprotect
                // TODO: optimize cache pop to return fitting pages if possible?

                for (nuint i = 0; i < segment->capacity; i++)
                {
                    mi_page_t* page = &segment->pages.e0 + i;

                    mi_page_purge_remove(page, tld);

                    if (!page->is_committed && commit)
                    {
                        if (!mi_page_ensure_committed(segment, page, tld))
                        {
                            return null;
                        }

                        is_zero |= page->is_zero_init;
                    }
                }

                // ensure the initial info is committed
                if (segment->capacity < capacity)
                {
                    bool ok = _mi_os_commit(segment, pre_size, out bool commit_zero, ref *tld->os->stats);

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
            segment = (mi_segment_t*)_mi_arena_alloc_aligned(segment_size, alignment, align_offset, ref commit, ref mem_large, out bool is_pinned, out is_zero, req_arena_id, out mi_memid_t memid, os_tld);

            if (segment == null)
            {
                // failed to allocate
                return null;
            }

            if (!commit)
            {
                // ensure the initial info is committed
                mi_assert_internal((MI_DEBUG > 1) && !is_pinned);
                bool ok = _mi_os_commit(segment, pre_size, out bool commit_zero, ref *tld->os->stats);

                if (commit_zero)
                {
                    is_zero = true;
                }

                if (!ok)
                {
                    // commit failed; we cannot touch the memory: free the segment directly and return `null`
                    _mi_arena_free(segment, segment_size, alignment, align_offset, memid, 0, ref *os_tld->stats);
                    return null;
                }
            }

            segment->memid = memid;
            segment->mem_alignment = alignment;
            segment->mem_align_offset = align_offset;
            segment->mem_is_pinned = is_pinned;
            segment->mem_is_large = mem_large;
            segment->mem_is_committed = commit;
            segment->allow_decommit = !segment->mem_is_pinned;
            segment->allow_purge = segment->allow_decommit && (mi_option_get(mi_option_purge_delay) >= 0);

            mi_segments_track_size((long)segment_size, tld);
        }

        mi_assert_internal((MI_DEBUG > 1) && (segment != null) && (((nuint)segment % MI_SEGMENT_SIZE) == 0));
        mi_assert_internal((MI_DEBUG > 1) && (!segment->mem_is_pinned || segment->mem_is_committed));

        if (!pages_still_good)
        {
            // zero the segment info (but not the `mem` fields)

            nint ofs = (nint)offsetof(segment, &segment->next);
            _ = memset((byte*)segment + ofs, 0, info_size - (nuint)ofs);

            // initialize pages info
            for (nuint i = 0; i < capacity; i++)
            {
                mi_page_t* page = &segment->pages.e0 + i;

                page->segment_idx = (byte)i;
                page->is_committed = commit;
                page->is_zero_init = is_zero;
                page->is_huge = page_kind == MI_PAGE_HUGE;
            }
        }
        else
        {
            // zero the segment info but not the pages info (and mem fields)
            nint ofs = (nint)offsetof(segment, &segment->next);
            _ = memset((byte*)segment + ofs, 0, offsetof(segment, &segment->pages) - (nuint)ofs);
        }

        // initialize
        segment->page_kind = page_kind;
        segment->capacity = capacity;
        segment->page_shift = page_shift;
        segment->segment_size = segment_size;
        segment->segment_info_size = pre_size;
        segment->thread_id = _mi_thread_id();
        segment->cookie = _mi_ptr_cookie(segment);
        segment->subproc = tld->subproc;

        _mi_segment_map_allocated_at(segment);
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
    private static mi_segment_t* mi_segment_alloc(nuint required, mi_page_kind_t page_kind, nuint page_shift, nuint page_alignment, int req_arena_id, mi_segments_tld_t* tld, mi_os_tld_t* os_tld)
        => mi_segment_init(null, required, page_kind, page_shift, page_alignment, req_arena_id, tld, os_tld);

    private static void mi_segment_free(mi_segment_t* segment, bool force, mi_segments_tld_t* tld)
    {
        mi_assert((MI_DEBUG != 0) && (segment != null));
        if (segment->dont_free)
        {
            return;
        }

        _ = force;

        mi_segment_remove_all_purges(segment, false, tld);
        mi_segment_remove_from_free_queue(segment, tld);

        mi_assert_expensive((MI_DEBUG > 2) && !mi_segment_queue_contains(&tld->small_free, segment));
        mi_assert_expensive((MI_DEBUG > 2) && !mi_segment_queue_contains(&tld->medium_free, segment));

        mi_assert((MI_DEBUG != 0) && (segment->next == null));
        mi_assert((MI_DEBUG != 0) && (segment->prev == null));

        _mi_stat_decrease(ref tld->stats->page_committed, segment->segment_info_size);

        mi_segment_os_free(segment, segment->segment_size, tld);
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

        mi_page_purge_remove(page, tld);
        if (!mi_page_ensure_committed(segment, page, tld))
        {
            return false;
        }

        page->segment_in_use = true;
        segment->used++;
        mi_assert_internal((MI_DEBUG > 1) && page->segment_in_use && page->is_committed && (page->used == 0) && !mi_pages_purge_contains(page, tld));
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
    private static void mi_segment_page_clear(mi_segment_t* segment, mi_page_t* page, mi_segments_tld_t* tld)
    {
        mi_assert_internal((MI_DEBUG > 1) && page->segment_in_use);
        mi_assert_internal((MI_DEBUG > 1) && mi_page_all_free(page));
        mi_assert_internal((MI_DEBUG > 1) && page->is_committed);
        mi_assert_internal((MI_DEBUG > 1) && mi_page_not_in_queue(page, tld));

        nuint inuse = page->capacity * mi_page_block_size(page);

        _mi_stat_decrease(ref tld->stats->page_committed, inuse);
        _mi_stat_decrease(ref tld->stats->pages, 1);
        _mi_stat_decrease(ref (&tld->stats->page_bins.e0)[_mi_page_stats_bin(page)], 1);

        // calculate the used size from the raw (non-aligned) start of the page
        page->is_zero_init = false;
        page->segment_in_use = false;

        // zero the page data, but not the segment fields, capacity, and page metadata
        nuint block_size = page->block_size;
        byte block_size_shift = page->block_size_shift;
        byte heap_tag = page->heap_tag;
        byte* page_start = page->page_start;
        ushort capacity = page->capacity;
        ushort reserved = page->reserved;

        nint ofs = (nint)offsetof(page, &page->capacity);
        _ = memset((byte*)page + ofs, 0, SizeOf<mi_page_t>() - (nuint)ofs);

        page->capacity = capacity;
        page->reserved = reserved;
        page->block_size = block_size;
        page->block_size_shift = block_size_shift;
        page->heap_tag = heap_tag;
        page->page_start = page_start;

        segment->used--;

        mi_segment_schedule_purge(segment, page, tld);

        // after purge these can be zeroed now
        page->capacity = 0;
        page->reserved = 0;
    }

    private static partial void _mi_segment_page_free(mi_page_t* page, bool force, mi_segments_tld_t* tld)
    {
        mi_assert((MI_DEBUG != 0) && (page != null));

        mi_segment_t* segment = _mi_page_segment(page);
        mi_assert_expensive((MI_DEBUG > 2) && mi_segment_is_valid(segment, tld));

        mi_pages_try_purge(false, tld);

        // mark it as free now
        mi_segment_page_clear(segment, page, tld);

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

                if (segment->page_kind <= MI_PAGE_MEDIUM)
                {
                    // move back to segments  free list
                    mi_segment_insert_in_free_queue(segment, tld);
                }
            }
        }
    }

    /* -----------------------------------------------------------
    Abandonment

    When threads terminate, they can leave segments with live
    blocks. Arena-backed abandoned segments are tracked in an
    arena bitmap and reclaimed by other threads on demand.
    ----------------------------------------------------------- */

    private static partial void _mi_abandoned_await_readers()
    {
    }

    /* -----------------------------------------------------------
       Abandon segment/page
    ----------------------------------------------------------- */

    private static partial void mi_segment_abandon(mi_segment_t* segment, mi_segments_tld_t* tld)
    {
#pragma warning disable CS0420
        mi_assert_internal((MI_DEBUG > 1) && (segment->used == segment->abandoned));
        mi_assert_internal((MI_DEBUG > 1) && (segment->used > 0));
        mi_assert_expensive((MI_DEBUG > 2) && mi_segment_is_valid(segment, tld));

        // Force-purge non-arena segments on abandonment; arena pages follow the option.

        mi_pages_try_purge(false, tld);
        bool force_purge = (segment->memid.memkind != mi_memkind_t.MI_MEM_ARENA) || mi_option_is_enabled(mi_option_abandoned_page_purge);
        mi_segment_remove_all_purges(segment, force_purge, tld);

        // remove the segment from the free page queue if needed
        mi_segment_remove_from_free_queue(segment, tld);
        mi_assert_internal((MI_DEBUG > 1) && segment->next == null && segment->prev == null);

        // all pages in the segment are abandoned; mark the segment

        _mi_stat_increase(ref tld->stats->segments_abandoned, 1);
        mi_segments_track_size(-(long)segment->segment_size, tld);
        if (segment->was_reclaimed)
        {
            tld->reclaim_count--;
            segment->was_reclaimed = false;
        }

        segment->abandoned_visits = 0;

        _mi_arena_segment_mark_abandoned(segment);
#pragma warning restore CS0420
    }

    private static partial void _mi_segment_page_abandon(mi_page_t* page, mi_segments_tld_t* tld)
    {
        mi_assert((MI_DEBUG != 0) && (page != null));
        mi_assert_internal((MI_DEBUG > 1) && (mi_page_thread_free_flag(page) == MI_NEVER_DELAYED_FREE));
        mi_assert_internal((MI_DEBUG > 1) && (mi_page_heap(page) == null));

        mi_segment_t* segment = _mi_page_segment(page);

        mi_assert_expensive((MI_DEBUG > 2) && !mi_pages_purge_contains(page, tld));
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
        mi_assert_internal((MI_DEBUG > 1) && (block_size <= MI_LARGE_OBJ_SIZE_MAX));
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
                else if ((mi_page_block_size(page) == block_size) && mi_page_has_any_available(page))
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
        right_page_reclaimed = false;

        mi_assert_internal((MI_DEBUG > 1) && ((mi_atomic_load_relaxed(ref segment->thread_id) == 0) || (mi_atomic_load_relaxed(ref segment->thread_id) == _mi_thread_id())));
        mi_atomic_store_release(ref segment->thread_id, _mi_thread_id());
        segment->abandoned_visits = 0;
        segment->was_reclaimed = true;
        tld->reclaim_count++;

        mi_segments_track_size((long)segment->segment_size, tld);

        mi_assert_internal((MI_DEBUG > 1) && segment->next == null && segment->prev == null);
        mi_assert_expensive((MI_DEBUG > 2) && mi_segment_is_valid(segment, tld));

        _mi_stat_decrease(ref tld->stats->segments_abandoned, 1);

        for (nuint i = 0; i < segment->capacity; i++)
        {
            mi_page_t* page = &segment->pages.e0 + i;

            if (page->segment_in_use)
            {
                mi_assert_internal((MI_DEBUG > 1) && page->is_committed);
                mi_assert_internal((MI_DEBUG > 1) && mi_page_not_in_queue(page, tld));
                mi_assert_internal((MI_DEBUG > 1) && (mi_page_thread_free_flag(page) == MI_NEVER_DELAYED_FREE));
                mi_assert_internal((MI_DEBUG > 1) && (mi_page_heap(page) == null));

                segment->abandoned--;

                mi_assert((MI_DEBUG != 0) && (page->next == null));

                _mi_stat_decrease(ref tld->stats->pages_abandoned, 1);

                mi_heap_t* target_heap = _mi_heap_by_tag(heap, page->heap_tag);
                if (target_heap == null)
                {
                    target_heap = heap;
                    _mi_error_message(EINVAL, "page with tag {0} cannot be reclaimed by a heap with the same tag (using heap tag {1} instead)\n", page->heap_tag, heap->tag);
                }

                // set the heap again and allow heap thread delayed free again.
                mi_page_set_heap(page, target_heap);

                // override never (after heap is set)
                _mi_page_use_delayed_free(page, MI_USE_DELAYED_FREE, true);

                // TODO: should we not collect again given that we just collected in `check_free`?

                // ensure used count is up to date
                _mi_page_free_collect(page, false);

                if (mi_page_all_free(page))
                {
                    // if everything free already, clear the page directly; reset is ok now
                    mi_segment_page_clear(segment, page, tld);
                }
                else
                {
                    // otherwise reclaim it into the heap
                    _mi_page_reclaim(target_heap, page);

                    if ((requested_block_size == mi_page_block_size(page)) && mi_page_has_any_available(page) && (heap == target_heap))
                    {
                        right_page_reclaimed = true;
                    }
                }
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

    private static bool _mi_segment_attempt_reclaim(mi_heap_t* heap, mi_segment_t* segment)
    {
#pragma warning disable CS0420
        if (mi_atomic_load_relaxed(ref segment->thread_id) != 0)
        {
            return false;
        }

        if (!_mi_heap_memid_is_suitable(heap, segment->memid))
        {
            return false;
        }

        int target = mi_option_get(mi_option_target_segments_per_thread);
        if ((target > 0) && ((nuint)target <= heap->tld->segments.count))
        {
            return false;
        }

        if ((segment->memid.memkind == mi_memkind_t.MI_MEM_ARENA) &&
            ((heap->tld->segments.reclaim_count * 2) > heap->tld->segments.count))
        {
            return false;
        }

        if (_mi_arena_segment_clear_abandoned(segment, heap->tld->segments.subproc))
        {
            mi_segment_t* result = mi_segment_reclaim(segment, heap, 0, out _, &heap->tld->segments);
            mi_assert_internal((MI_DEBUG > 1) && (result == segment));
            return result != null;
        }

        return false;
#pragma warning restore CS0420
    }

#pragma warning disable CS0420
    private static partial void _mi_abandoned_reclaim_all(mi_heap_t* heap, mi_segments_tld_t* tld)
    {
        _mi_arena_field_cursor_init(heap, heap->tld->segments.subproc, true, out mi_arena_field_cursor_t current);
        mi_segment_t* segment;

        while ((segment = _mi_arena_segment_clear_abandoned_next(ref current)) != null)
        {
            _ = mi_segment_reclaim(segment, heap, 0, out _, tld);
        }

        _mi_arena_field_cursor_done(ref current);
    }
#pragma warning restore CS0420

    private static bool mi_segment_count_is_within_target(mi_segments_tld_t* tld, out nuint target)
    {
        target = (nuint)mi_option_get_clamp(mi_option_target_segments_per_thread, 0, 1024);
        return (target == 0) || (tld->count < target);
    }

    private static int mi_segment_get_reclaim_tries(mi_segments_tld_t* tld)
    {
        int percentage = mi_option_get_clamp(mi_option_max_segment_reclaim, 0, 100);
        if (percentage <= 0)
        {
            return 0;
        }

        nuint abandoned_count = _mi_arena_segment_abandoned_count();
        if (abandoned_count == 0)
        {
            return 0;
        }

        nuint relative_count = (abandoned_count > 10000)
            ? (abandoned_count / 100) * (nuint)percentage
            : (abandoned_count * (nuint)percentage) / 100;
        int max_tries = (int)((relative_count <= 1) ? 1 : ((relative_count > 1024) ? 1024 : relative_count));
        if ((max_tries < 8) && (abandoned_count > 8))
        {
            max_tries = 8;
        }

        return max_tries;
    }

#pragma warning disable CS0420
    private static mi_segment_t* mi_segment_try_reclaim(mi_heap_t* heap, [NativeTypeName("size_t")] nuint block_size, mi_page_kind_t page_kind, [NativeTypeName("bool*")] out bool reclaimed, mi_segments_tld_t* tld)
    {
        reclaimed = false;

        int max_tries = mi_segment_get_reclaim_tries(tld);
        if (max_tries <= 0)
        {
            return null;
        }

        _mi_arena_field_cursor_init(heap, heap->tld->segments.subproc, false, out mi_arena_field_cursor_t current);
        mi_segment_t* segment;
        mi_segment_t* result = null;

        while (mi_segment_count_is_within_target(tld, out _) &&
               (max_tries-- > 0) &&
               ((segment = _mi_arena_segment_clear_abandoned_next(ref current)) != null))
        {
            segment->abandoned_visits++;
            bool is_suitable = _mi_heap_memid_is_suitable(heap, segment->memid);

            // try to free up pages (due to concurrent frees)
            bool has_page = mi_segment_check_free(segment, block_size, out bool all_pages_free);

            if (all_pages_free)
            {
                // free the segment (by forced reclaim) to make it available to other threads.
                // note1: we prefer to free a segment as that might lead to reclaiming another
                // segment that is still partially used.
                // note2: we could in principle optimize this by skipping reclaim and directly
                // freeing but that would violate some invariants temporarily)
                _ = mi_segment_reclaim(segment, heap, 0, out _, tld);
            }
            else if (has_page && (segment->page_kind == page_kind) && is_suitable)
            {
                // found a free page of the right kind, or page of the right block_size with free space
                // we return the result of reclaim (which is usually `segment`) as it might free
                // the segment due to concurrent frees (in which case `null` is returned).
                result = mi_segment_reclaim(segment, heap, block_size, out reclaimed, tld);
                break;
            }
            else if ((segment->abandoned_visits > 3) && is_suitable)
            {
                // always reclaim on 3rd visit to limit the list length.
                _ = mi_segment_reclaim(segment, heap, 0, out _, tld);
            }
            else
            {
                // otherwise, mark it back as abandoned
                max_tries++;
                _mi_arena_segment_mark_abandoned(segment);
            }
        }

        _mi_arena_field_cursor_done(ref current);
        return result;
    }
#pragma warning restore CS0420

    private static void mi_segment_force_abandon(mi_segment_t* segment, mi_segments_tld_t* tld)
    {
        mi_assert_internal((MI_DEBUG > 1) && (segment->abandoned < segment->used));
        mi_assert_internal((MI_DEBUG > 1) && !segment->dont_free);

        segment->dont_free = true;

        for (nuint i = 0; i < segment->capacity; i++)
        {
            mi_page_t* page = &segment->pages.e0 + i;
            if (!page->segment_in_use)
            {
                continue;
            }

            mi_assert_internal((MI_DEBUG > 1) && (segment->used > 0));

            if (segment->used == (segment->abandoned + 1))
            {
                segment->dont_free = false;
                _mi_page_force_abandon(page);
                return;
            }

            _mi_page_force_abandon(page);
        }

        segment->dont_free = false;
        mi_assert((MI_DEBUG != 0) && (segment->used == segment->abandoned));
        mi_assert((MI_DEBUG != 0) && (segment->used == 0));

        if (segment->used == 0)
        {
            mi_segment_free(segment, false, tld);
        }
        else
        {
            mi_pages_try_purge(false, tld);
        }
    }

    private static void mi_segments_try_abandon_to_target(mi_heap_t* heap, nuint target, mi_segments_tld_t* tld)
    {
        if (target <= 1)
        {
            return;
        }

        nuint min_target = (target > 4) ? ((target * 3) / 4) : target;
        mi_page_queue_t* full_queue = &heap->pages.e0 + MI_BIN_FULL;

        for (int i = 0; (i < 64) && (tld->count >= min_target); i++)
        {
            mi_page_t* page = full_queue->first;
            while ((page != null) && mi_page_is_huge(page))
            {
                page = page->next;
            }

            if (page == null)
            {
                break;
            }

            mi_segment_force_abandon(_mi_page_segment(page), tld);
            mi_assert_internal((MI_DEBUG > 1) && (page != full_queue->first));
        }
    }

    private static void mi_segments_try_abandon(mi_heap_t* heap, mi_segments_tld_t* tld)
    {
        if (mi_segment_count_is_within_target(tld, out nuint target))
        {
            return;
        }

        mi_segments_try_abandon_to_target(heap, target, tld);
    }

    public static partial void mi_collect_reduce(nuint target_thread_owned)
    {
        mi_collect(force: true);

        mi_heap_t* heap = (mi_heap_t*)mi_heap_get_default();
        mi_segments_tld_t* tld = &heap->tld->segments;
        nuint target = target_thread_owned / MI_SEGMENT_SIZE;

        if (target == 0)
        {
            target = (nuint)mi_option_get_clamp(mi_option_target_segments_per_thread, 1, 1024);
        }

        mi_segments_try_abandon_to_target(heap, target, tld);
    }

    /* -----------------------------------------------------------
       Reclaim or allocate
    ----------------------------------------------------------- */

    private static mi_segment_t* mi_segment_reclaim_or_alloc(mi_heap_t* heap, [NativeTypeName("size_t")] nuint block_size, mi_page_kind_t page_kind, [NativeTypeName("size_t")] nuint page_shift, mi_segments_tld_t* tld, mi_os_tld_t* os_tld)
    {
        mi_assert_internal((MI_DEBUG > 1) && (page_kind <= MI_PAGE_LARGE));
        mi_assert_internal((MI_DEBUG > 1) && (block_size <= MI_LARGE_OBJ_SIZE_MAX));

        mi_segments_try_abandon(heap, tld);

        // try to reclaim an abandoned segment
        mi_segment_t* segment = mi_segment_try_reclaim(heap, block_size, page_kind, out bool reclaimed, tld);
        mi_assert_internal((MI_DEBUG > 1) && ((segment == null) || _mi_heap_memid_is_suitable(heap, segment->memid)));

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

        // otherwise allocate a fresh segment
        return mi_segment_alloc(0, page_kind, page_shift, 0, heap->arena_id, tld, os_tld);
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

    private static mi_page_t* mi_segment_page_try_alloc_in_queue(mi_heap_t* heap, mi_page_kind_t kind, mi_segments_tld_t* tld)
    {
        mi_segment_queue_t* free_queue = mi_segment_free_queue_of_kind(kind, tld);

        for (mi_segment_t* segment = free_queue->first; segment != null; segment = segment->next)
        {
            if (_mi_heap_memid_is_suitable(heap, segment->memid) && mi_segment_has_free(segment))
            {
                return mi_segment_page_alloc_in(segment, tld);
            }
        }

        return null;
    }

    private static mi_page_t* mi_segment_page_alloc(mi_heap_t* heap, nuint block_size, nuint page_alignment, mi_page_kind_t kind, nuint page_shift, mi_segments_tld_t* tld, mi_os_tld_t* os_tld)
    {
        mi_assert_internal((MI_DEBUG > 1) && (page_alignment == 0));
        mi_page_t* page = mi_segment_page_try_alloc_in_queue(heap, kind, tld);

        if (page == null)
        {
            // possibly allocate or reclaim a fresh segment
            mi_segment_t* segment = mi_segment_reclaim_or_alloc(heap, block_size, kind, page_shift, tld, os_tld);

            if (segment == null)
            {
                // return null if out-of-memory (or reclaimed)
                return null;
            }

            mi_assert_internal((MI_DEBUG > 1) && (segment->page_kind == kind));
            mi_assert_internal((MI_DEBUG > 1) && (segment->used < segment->capacity));
            mi_assert_internal((MI_DEBUG > 1) && _mi_heap_memid_is_suitable(heap, segment->memid));

            page = mi_segment_page_try_alloc_in_queue(heap, kind, tld);
        }

        mi_assert_internal((MI_DEBUG > 1) && (page != null));
        if (page == null)
        {
            return null;
        }

        if (MI_DEBUG >= 2)
        {
            // verify it is committed
            mi_segment_raw_page_start(_mi_page_segment(page), page, out _)[0] = 0;
        }

        return page;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static mi_page_t* mi_segment_small_page_alloc(mi_heap_t* heap, nuint block_size, mi_segments_tld_t* tld, mi_os_tld_t* os_tld)
        => mi_segment_page_alloc(heap, block_size, 0, MI_PAGE_SMALL, (nuint)MI_SMALL_PAGE_SHIFT, tld, os_tld);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static mi_page_t* mi_segment_medium_page_alloc(mi_heap_t* heap, nuint block_size, mi_segments_tld_t* tld, mi_os_tld_t* os_tld)
        => mi_segment_page_alloc(heap, block_size, 0, MI_PAGE_MEDIUM, (nuint)MI_MEDIUM_PAGE_SHIFT, tld, os_tld);

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
            mi_segment_raw_page_start(segment, page, out _)[0] = 0;
        }

        return page;
    }

    private static mi_page_t* mi_segment_huge_page_alloc(nuint size, nuint page_alignment, int req_arena_id, mi_segments_tld_t* tld, mi_os_tld_t* os_tld)
    {
        mi_segment_t* segment = mi_segment_alloc(size, MI_PAGE_HUGE, (nuint)(MI_SEGMENT_SHIFT + 1), page_alignment, req_arena_id, tld, os_tld);

        if (segment == null)
        {
            return null;
        }

        mi_assert_internal((MI_DEBUG > 1) && ((mi_segment_page_size(segment) - segment->segment_info_size - (2 * ((MI_SECURE == 0) ? 0 : _mi_os_page_size()))) >= size));

        mi_page_t* page = mi_segment_find_free(segment, tld);

        mi_assert_internal((MI_DEBUG > 1) && (page != null));

        nuint psize;
        byte* start = mi_segment_raw_page_start(segment, page, out psize);
        page->block_size = psize;

        if ((page_alignment > 0) && segment->allow_decommit && page->is_committed)
        {
            byte* aligned_p = (byte*)mi_align_up_ptr(start, page_alignment);
            mi_assert_internal((MI_DEBUG > 1) && (((nuint)aligned_p % page_alignment) == 0));
            mi_assert_internal((MI_DEBUG > 1) && ((nuint)aligned_p >= (nuint)start));
            mi_assert_internal((MI_DEBUG > 1) && (psize - ((nuint)aligned_p - (nuint)start) >= size));

            byte* decommit_start = start + SizeOf<mi_block_t>();
            if ((nuint)aligned_p > (nuint)decommit_start)
            {
                _ = _mi_os_reset(decommit_start, (nuint)aligned_p - (nuint)decommit_start, ref *os_tld->stats);
            }
        }

        return page;
    }

    private static partial void _mi_segment_huge_page_reset(mi_segment_t* segment, mi_page_t* page, mi_block_t* block)
    {
        mi_assert_internal((MI_DEBUG > 1) && (segment->page_kind == MI_PAGE_HUGE));
        mi_assert_internal((MI_DEBUG > 1) && (segment == _mi_page_segment(page)));
        mi_assert_internal((MI_DEBUG > 1) && (page->used == 1) && (page->free == null));

        if (segment->allow_decommit && page->is_committed)
        {
            nuint usize = mi_page_usable_size_of(page, block);
            if (usize > SizeOf<mi_block_t>())
            {
                mi_tld_t* tld = mi_page_heap(page)->tld;
                _ = _mi_os_reset((byte*)block + SizeOf<mi_block_t>(), usize - SizeOf<mi_block_t>(), ref *tld->os.stats);
            }
        }
    }

    /* -----------------------------------------------------------
       Page allocation
    ----------------------------------------------------------- */

    private static partial mi_page_t* _mi_segment_page_alloc(mi_heap_t* heap, nuint block_size, nuint page_alignment, mi_segments_tld_t* tld, mi_os_tld_t* os_tld)
    {
        mi_page_t* page;

        if (page_alignment > MI_BLOCK_ALIGNMENT_MAX)
        {
            mi_assert_internal((MI_DEBUG > 1) && _mi_is_power_of_two(page_alignment));
            mi_assert_internal((MI_DEBUG > 1) && (page_alignment >= MI_SEGMENT_SIZE));
            page = mi_segment_huge_page_alloc(block_size, page_alignment, heap->arena_id, tld, os_tld);
        }
        else if (block_size <= MI_SMALL_OBJ_SIZE_MAX)
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
            page = mi_segment_huge_page_alloc(block_size, 0, heap->arena_id, tld, os_tld);
        }

        mi_assert_expensive((MI_DEBUG > 2) && ((page == null) || mi_segment_is_valid(_mi_page_segment(page), tld)));
        mi_assert_internal((MI_DEBUG > 1) && ((page == null) || mi_segment_page_size(_mi_page_segment(page)) - (MI_SECURE == 0 ? 0 : _mi_os_page_size()) >= block_size));

        mi_assert_internal((MI_DEBUG > 1) && ((page == null) || mi_page_not_in_queue(page, tld)));
        return page;
    }
}
