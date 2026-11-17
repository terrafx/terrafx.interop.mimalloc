// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the arena.c file from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

using System.Runtime.CompilerServices;
using static TerraFX.Interop.Mimalloc.mi_option_t;

namespace TerraFX.Interop.Mimalloc;

public static unsafe partial class Mimalloc
{
    /* ----------------------------------------------------------------------------
    "Arenas" are fixed area's of OS memory from which we can allocate
    large blocks (>= MI_ARENA_BLOCK_SIZE).
    In contrast to the rest of mimalloc, the arenas are shared between
    threads and need to be accessed using atomic operations.

    Arenas are used for large memory reservations and huge OS page (1GiB)
    allocations. They are shared between threads and use atomic bitmaps.

    Arena allocation is thread safe and uses an atomic bitmap that supports
    allocations spanning multiple fields.
    -----------------------------------------------------------------------------*/

    // os.c

    private static partial void* _mi_os_alloc_aligned([NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment, bool commit, bool allow_large, out mi_memid_t memid, [NativeTypeName("mi_stats_t*")] ref mi_stats_internal_t stats);

    private static partial void _mi_os_free_ex(void* p, [NativeTypeName("size_t")] nuint size, bool still_committed, mi_memid_t memid, [NativeTypeName("mi_stats_t*")] ref mi_stats_internal_t tld_stats);

    private static partial void* _mi_os_alloc_aligned_at_offset(nuint size, nuint alignment, nuint align_offset, bool commit, bool allow_large, out mi_memid_t memid, ref mi_stats_internal_t stats);

    private static partial void* _mi_os_alloc_huge_os_pages([NativeTypeName("size_t")] nuint pages, int numa_node, [NativeTypeName("mi_msecs_t")] long max_msecs, [NativeTypeName("size_t*")] out nuint pages_reserved, [NativeTypeName("size_t*")] out nuint psize, out mi_memid_t memid);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static mi_memid_t mi_arena_memid_none() => default;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static mi_memid_t mi_arena_memid_static()
    {
        mi_memid_t memid = default;
        memid.memkind = mi_memkind_t.MI_MEM_STATIC;
        return memid;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint mi_arena_id_index(int arena_id) => (arena_id <= 0) ? MI_MAX_ARENAS : (nuint)(arena_id - 1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int mi_arena_id_create(nuint arena_index)
    {
        mi_assert_internal((MI_DEBUG > 1) && (arena_index < MI_MAX_ARENAS));
        mi_assert_internal((MI_DEBUG > 1) && (MI_MAX_ARENAS <= byte.MaxValue));
        return (int)arena_index + 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static mi_memid_t mi_arena_memid_create(int arena_id, bool exclusive, [NativeTypeName("mi_bitmap_index_t")] nuint bitmap_index)
    {
        mi_assert_internal((MI_DEBUG > 1) && (arena_id >= 0) && (arena_id <= 0x7F));

        mi_memid_t memid = default;
        memid.memkind = mi_memkind_t.MI_MEM_ARENA;
        memid.mem.arena.block_index = bitmap_index;
        memid.mem.arena.id = arena_id;
        memid.mem.arena.is_exclusive = exclusive;
        return memid;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void mi_arena_memid_indices(mi_memid_t memid, out nuint arena_index, out nuint bitmap_index)
    {
        mi_assert_internal((MI_DEBUG > 1) && (memid.memkind == mi_memkind_t.MI_MEM_ARENA));
        arena_index = mi_arena_id_index(memid.mem.arena.id);
        bitmap_index = memid.mem.arena.block_index;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool mi_arena_id_is_suitable(int arena_id, bool exclusive, int req_arena_id)
        => ((!exclusive && (req_arena_id == 0)) || (arena_id == req_arena_id));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool mi_arena_memid_is_suitable(mi_memid_t memid, int req_arena_id)
        => mi_arena_id_is_suitable((memid.memkind == mi_memkind_t.MI_MEM_ARENA) ? memid.mem.arena.id : 0, (memid.memkind == mi_memkind_t.MI_MEM_ARENA) && memid.mem.arena.is_exclusive, req_arena_id);

    private static void* mi_arena_static_zalloc(nuint size, nuint alignment, out mi_memid_t memid)
    {
#pragma warning disable CS0420
        memid = mi_arena_memid_none();

        if ((size == 0) || (size > MI_ARENA_STATIC_MAX))
        {
            return null;
        }

        if (alignment == 0)
        {
            alignment = 1;
        }

        while (true)
        {
            nuint old_top = mi_atomic_load_relaxed(ref mi_arena_static_top);
            nuint start = _mi_align_up(old_top, alignment);

            if ((start > MI_ARENA_STATIC_MAX) || (size > (MI_ARENA_STATIC_MAX - start)))
            {
                return null;
            }

            nuint new_top = start + size;
            nuint expected = old_top;

            if (mi_atomic_cas_strong_acq_rel(ref mi_arena_static_top, ref expected, new_top))
            {
                void* p = mi_arena_static + start;
                _ = memset(p, 0, size);
                memid = mi_arena_memid_static();
                return p;
            }
        }
#pragma warning restore CS0420
    }

    private static void* _mi_arena_meta_zalloc(nuint size, out mi_memid_t memid, ref mi_stats_internal_t stats)
    {
        void* p = mi_arena_static_zalloc(size, MI_MAX_ALIGN_SIZE, out memid);
        if (p != null)
        {
            return p;
        }

        p = _mi_os_alloc(size, out memid, ref stats);

        if ((p != null) && !memid.initially_zero)
        {
            _mi_memzero_aligned(p, size);
        }

        return p;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void* mi_arena_block_start(mi_arena_t* arena, nuint bitmap_index)
        => (void*)(arena->start + (mi_bitmap_index_bit(bitmap_index) * MI_ARENA_BLOCK_SIZE));

    private static void _mi_arena_meta_free(void* p, nuint size, mi_memid_t memid, ref mi_stats_internal_t stats)
    {
        if (mi_memkind_is_os(memid.memkind))
        {
            _mi_os_free(p, size, memid, ref stats);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return: NativeTypeName("size_t")]
    private static nuint mi_block_count_of_size([NativeTypeName("size_t")] nuint size) => _mi_divide_up(size, MI_ARENA_BLOCK_SIZE);

    /* -----------------------------------------------------------
      Thread safe allocation in an arena
    ----------------------------------------------------------- */
    private static bool mi_arena_alloc(mi_arena_t* arena, [NativeTypeName("size_t")] nuint blocks, [NativeTypeName("mi_bitmap_index_t*")] out nuint bitmap_idx)
    {
#pragma warning disable CS0420
        // start from last search
        nuint idx = mi_atomic_load_acquire(ref arena->search_idx);

        if (mi_bitmap_try_find_from_claim_across(&arena->blocks_inuse.e0, arena->field_count, idx, blocks, out bitmap_idx))
        {
            // start search from here next time
            mi_atomic_store_release(ref arena->search_idx, idx);
            return true;
        }

        bitmap_idx = 0;
        return false;
#pragma warning restore CS0420
    }

    /* -----------------------------------------------------------
      Arena Allocation
    ----------------------------------------------------------- */

    private static void* mi_arena_alloc_from(mi_arena_t* arena, [NativeTypeName("size_t")] nuint arena_index, [NativeTypeName("size_t")] nuint needed_bcount, [NativeTypeName("bool*")] ref bool commit, [NativeTypeName("bool*")] ref bool large, [NativeTypeName("bool*")] out bool is_pinned, [NativeTypeName("bool*")] out bool is_zero, [NativeTypeName("mi_memid_t*")] out mi_memid_t memid, mi_os_tld_t* tld)
    {
        mi_assert_internal((MI_DEBUG > 1) && (arena->id == mi_arena_id_create(arena_index)));

        if (!mi_arena_alloc(arena, needed_bcount, out nuint bitmap_index))
        {
            is_pinned = false;
            memid = default;
            is_zero = false;
            return null;
        }

        // claimed it! set the dirty bits (todo: no need for an atomic op here?)
        void* p = mi_arena_block_start(arena, bitmap_index);

        memid = mi_arena_memid_create(arena->id, arena->exclusive, bitmap_index);
        is_zero = false;
        if (arena->memid.initially_zero && (arena->blocks_dirty != null))
        {
            is_zero = mi_bitmap_claim_across(arena->blocks_dirty, arena->field_count, needed_bcount, bitmap_index, out _);
        }
        large = arena->is_large;
        is_pinned = arena->memid.is_pinned;

        if (arena->blocks_purge != null)
        {
            _ = mi_bitmap_unclaim_across(arena->blocks_purge, arena->field_count, needed_bcount, bitmap_index);
        }

        if (arena->blocks_committed == null)
        {
            // always committed
            commit = true;
        }
        else if (commit)
        {
            // arena not committed as a whole, but commit requested: ensure commit now
            _ = mi_bitmap_claim_across(arena->blocks_committed, arena->field_count, needed_bcount, bitmap_index, out bool any_uncommitted);

            if (any_uncommitted)
            {
                bool committed = _mi_os_commit(p, needed_bcount * MI_ARENA_BLOCK_SIZE, out bool commit_zero, ref *tld->stats);

                if (!committed)
                {
                    _ = mi_bitmap_unclaim_across(arena->blocks_committed, arena->field_count, needed_bcount, bitmap_index);
                    commit = false;
                }
                else if (commit_zero)
                {
                    is_zero = true;
                }
            }
        }
        else
        {
            // no need to commit, but check if already fully committed
            commit = mi_bitmap_is_claimed_across(arena->blocks_committed, arena->field_count, needed_bcount, bitmap_index);
        }

        memid.is_pinned = arena->memid.is_pinned;

        memid.initially_committed = commit;
        memid.initially_zero = is_zero;
        return p;
    }

#pragma warning disable CS0420
    private static void* mi_arena_try_alloc_at_id(int arena_id, bool match_numa_node, int numa_node, nuint size, nuint alignment, bool allow_large, ref bool commit, ref bool large, out bool is_pinned, out bool is_zero, int req_arena_id, out mi_memid_t memid, mi_os_tld_t* tld)
    {
        nuint index = mi_arena_id_index(arena_id);
        nuint max_arena = mi_atomic_load_relaxed(ref mi_arena_count);

        mi_assert_internal((MI_DEBUG > 1) && (index < max_arena));

        if (index >= max_arena)
        {
            is_pinned = false;
            is_zero = false;
            memid = default;
            return null;
        }

        mi_arena_t* arena = mi_atomic_load_ptr_acquire<mi_arena_t>(ref mi_arenas[index]);

        if ((arena == null) || (!allow_large && arena->is_large) || !mi_arena_id_is_suitable(arena->id, arena->exclusive, req_arena_id))
        {
            is_pinned = false;
            is_zero = false;
            memid = default;
            return null;
        }

        if (req_arena_id == 0)
        {
            bool numa_suitable = (numa_node < 0) || (arena->numa_node < 0) || (arena->numa_node == numa_node);

            if (match_numa_node ? !numa_suitable : numa_suitable)
            {
                is_pinned = false;
                is_zero = false;
                memid = default;
                return null;
            }
        }

        nuint bcount = mi_block_count_of_size(size);
        mi_assert_internal((MI_DEBUG > 1) && (size <= (bcount * MI_ARENA_BLOCK_SIZE)));

        void* p = mi_arena_alloc_from(arena, index, bcount, ref commit, ref large, out is_pinned, out is_zero, out memid, tld);
        mi_assert_internal((MI_DEBUG > 1) && ((p == null) || (((nuint)p % alignment) == 0)));
        return p;
    }
#pragma warning restore CS0420

#pragma warning disable CS0420
    private static void* mi_arena_try_alloc(int numa_node, nuint size, nuint alignment, ref bool commit, ref bool large, out bool is_pinned, out bool is_zero, int req_arena_id, out mi_memid_t memid, mi_os_tld_t* tld)
    {
        nuint max_arena = mi_atomic_load_acquire(ref mi_arena_count);

        if (max_arena == 0)
        {
            is_pinned = false;
            is_zero = false;
            memid = default;
            return null;
        }
#pragma warning restore CS0420

        bool allow_large = large;
        nuint arena_index = mi_arena_id_index(req_arena_id);

        if (req_arena_id != 0)
        {
            if (arena_index < max_arena)
            {
                void* p = mi_arena_try_alloc_at_id(req_arena_id, true, numa_node, size, alignment, allow_large, ref commit, ref large, out is_pinned, out is_zero, req_arena_id, out memid, tld);

                if (p != null)
                {
                    return p;
                }
            }
        }
        else
        {
            for (nuint i = 0; i < max_arena; i++)
            {
                void* p = mi_arena_try_alloc_at_id(mi_arena_id_create(i), true, numa_node, size, alignment, allow_large, ref commit, ref large, out is_pinned, out is_zero, req_arena_id, out memid, tld);

                if (p != null)
                {
                    return p;
                }
            }

            if (numa_node >= 0)
            {
                for (nuint i = 0; i < max_arena; i++)
                {
                    void* p = mi_arena_try_alloc_at_id(mi_arena_id_create(i), false, numa_node, size, alignment, allow_large, ref commit, ref large, out is_pinned, out is_zero, req_arena_id, out memid, tld);

                    if (p != null)
                    {
                        return p;
                    }
                }
            }
        }

        is_pinned = false;
        is_zero = false;
        memid = default;
        return null;
    }

    private static bool mi_arena_reserve(nuint size, bool allow_large, int req_arena_id, out int arena_id)
    {
#pragma warning disable CS0420
        arena_id = 0;

        if (req_arena_id != 0)
        {
            return false;
        }

        nuint arena_count = mi_atomic_load_relaxed(ref mi_arena_count);
        if (arena_count > (MI_MAX_ARENAS - 4))
        {
            return false;
        }

        nuint reserve = mi_option_get_size(mi_option_arena_reserve);
        if (reserve == 0)
        {
            return false;
        }

        if (!_mi_os_has_virtual_reserve())
        {
            reserve /= 4;
        }

        reserve = _mi_align_up(reserve, MI_ARENA_BLOCK_SIZE);
        reserve = _mi_align_up(reserve, MI_SEGMENT_SIZE);

        if ((arena_count >= 8) && (arena_count <= 128))
        {
            nuint multiplier = (nuint)1 << (int)(arena_count / 8);

            if (reserve <= (SIZE_MAX / multiplier))
            {
                reserve *= multiplier;
            }
        }

        if (reserve < size)
        {
            return false;
        }

        int eager_commit = mi_option_get(mi_option_arena_eager_commit);
        bool commit = (eager_commit == 2)
            ? (_mi_os_has_overcommit() || mi_option_is_enabled(mi_option_allow_large_os_pages))
            : (eager_commit == 1);
        int reserved_arena_id = 0;
        int error = mi_reserve_os_memory_ex(reserve, commit, allow_large, false, &reserved_arena_id);
        arena_id = reserved_arena_id;
        return error == 0;
#pragma warning restore CS0420
    }

    private static partial void* _mi_arena_alloc_aligned(nuint size, nuint alignment, nuint align_offset, ref bool commit, ref bool large, out bool is_pinned, out bool is_zero, int req_arena_id, out mi_memid_t memid, mi_os_tld_t* tld)
    {
        mi_assert_internal((MI_DEBUG > 1) && (tld != null));
        mi_assert_internal((MI_DEBUG > 1) && (size > 0));

        memid = mi_arena_memid_none();
        is_pinned = false;
        is_zero = false;
        bool allow_large = large;

        // try to allocate in an arena if the alignment is small enough
        // and the object is not too large or too small.

#pragma warning disable CS0420
        if ((!mi_option_is_enabled(mi_option_disallow_arena_alloc) || (req_arena_id != 0)) &&
            (align_offset == 0) && (alignment <= MI_SEGMENT_ALIGN) && (size >= MI_ARENA_MIN_OBJ_SIZE))
        {
            nuint bcount = mi_block_count_of_size(size);
            int numa_node = _mi_os_numa_node(tld);

            mi_assert_internal((MI_DEBUG > 1) && (size <= (bcount * MI_ARENA_BLOCK_SIZE)));

            if (mi_atomic_load_relaxed(ref mi_arena_count) > 0)
            {
                void* p = mi_arena_try_alloc(numa_node, size, alignment, ref commit, ref large, out is_pinned, out is_zero, req_arena_id, out memid, tld);
                if (p != null)
                {
                    return p;
                }
            }

            if ((req_arena_id == 0) && mi_arena_reserve(size, allow_large, req_arena_id, out int arena_id))
            {
                mi_assert_internal((MI_DEBUG > 1) && (req_arena_id == 0));
                void* p = mi_arena_try_alloc_at_id(arena_id, true, numa_node, size, alignment, allow_large, ref commit, ref large, out is_pinned, out is_zero, req_arena_id, out memid, tld);
                if (p != null)
                {
                    return p;
                }
            }
        }

        // finally, fall back to the OS
        if (mi_option_is_enabled(mi_option_disallow_os_alloc) || (req_arena_id != 0))
        {
            last_errno = ENOMEM;
            return null;
        }

        void* result = (align_offset == 0) ? _mi_os_alloc_aligned(size, alignment, commit, allow_large, out memid, ref *tld->stats) : _mi_os_alloc_aligned_at_offset(size, alignment, align_offset, commit, allow_large, out memid, ref *tld->stats);
        if (result != null)
        {
            large = memid.is_pinned;
            is_pinned = memid.is_pinned;
            is_zero = memid.initially_zero;
        }
        return result;
#pragma warning restore CS0420
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static partial void* _mi_arena_alloc(nuint size, ref bool commit, ref bool large, out bool is_pinned, out bool is_zero, int req_arena_id, out mi_memid_t memid, mi_os_tld_t* tld)
        => _mi_arena_alloc_aligned(size, MI_ARENA_BLOCK_SIZE, 0, ref commit, ref large, out is_pinned, out is_zero, req_arena_id, out memid, tld);

    private static bool _mi_arena_contains(void* p)
    {
#pragma warning disable CS0420
        if (p == null)
        {
            return false;
        }

        nuint address = (nuint)p;
        nuint max_arena = mi_atomic_load_relaxed(ref mi_arena_count);

        for (nuint i = 0; i < max_arena; i++)
        {
            mi_arena_t* arena = mi_atomic_load_ptr_relaxed<mi_arena_t>(ref mi_arenas[i]);

            if (arena != null)
            {
                nuint start = arena->start;
                nuint end = start + (arena->block_count * MI_ARENA_BLOCK_SIZE);

                if ((address >= start) && (address < end))
                {
                    return true;
                }
            }
        }

        return false;
#pragma warning restore CS0420
    }

    public static partial bool mi_is_in_heap_region(void* p) => (_mi_segment_of(p) != null) || _mi_arena_contains(p);

#pragma warning disable CS0420
    private static bool mi_os_abandoned_lock_try_acquire(ref int gate)
        => System.Threading.Interlocked.CompareExchange(ref gate, 1, 0) == 0;

    private static void mi_os_abandoned_lock_acquire(ref int gate)
    {
        System.Threading.SpinWait spinner = default;
        while (!mi_os_abandoned_lock_try_acquire(ref gate))
        {
            spinner.SpinOnce();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void mi_os_abandoned_lock_release(ref int gate)
        => System.Threading.Volatile.Write(ref gate, 0);

    private static bool mi_arena_segment_os_clear_abandoned(mi_segment_t* segment, bool take_lock)
    {
        mi_assert_internal((MI_DEBUG > 1) && (segment->memid.memkind != mi_memkind_t.MI_MEM_ARENA));
        mi_subproc_t* subproc = segment->subproc;

        if (take_lock && !mi_os_abandoned_lock_try_acquire(ref subproc->abandoned_os_lock))
        {
            return false;
        }

        mi_segment_t* next = segment->abandoned_os_next;
        mi_segment_t* prev = segment->abandoned_os_prev;
        bool reclaimed = false;

        if ((next != null) || (prev != null) || (subproc->abandoned_os_list == segment))
        {
            if (prev != null)
            {
                prev->abandoned_os_next = next;
            }
            else
            {
                subproc->abandoned_os_list = next;
            }

            if (next != null)
            {
                next->abandoned_os_prev = prev;
            }
            else
            {
                subproc->abandoned_os_list_tail = prev;
            }

            segment->abandoned_os_next = null;
            segment->abandoned_os_prev = null;
            _ = mi_atomic_decrement_relaxed(ref mi_abandoned_segment_count);
            _ = mi_atomic_decrement_relaxed(ref subproc->abandoned_count);
            _ = mi_atomic_decrement_relaxed(ref subproc->abandoned_os_list_count);

            if (take_lock)
            {
                mi_atomic_store_release(ref segment->thread_id, _mi_thread_id());
            }

            reclaimed = true;
        }

        if (take_lock)
        {
            mi_os_abandoned_lock_release(ref subproc->abandoned_os_lock);
        }

        return reclaimed;
    }

    private static bool _mi_arena_segment_clear_abandoned(mi_segment_t* segment, mi_subproc_t* subproc)
    {
        if (segment->subproc != subproc)
        {
            return false;
        }

        if (segment->memid.memkind != mi_memkind_t.MI_MEM_ARENA)
        {
            return mi_arena_segment_os_clear_abandoned(segment, true);
        }

        mi_arena_memid_indices(segment->memid, out nuint arena_index, out nuint bitmap_index);
        mi_assert_internal((MI_DEBUG > 1) && (arena_index < MI_MAX_ARENAS));

        mi_arena_t* arena = mi_atomic_load_ptr_acquire<mi_arena_t>(ref mi_arenas[arena_index]);
        mi_assert_internal((MI_DEBUG > 1) && (arena != null));

        if (arena == null)
        {
            return false;
        }

        bool cleared = mi_bitmap_unclaim(arena->blocks_abandoned, arena->field_count, 1, bitmap_index);
        if (cleared)
        {
            mi_assert_internal((MI_DEBUG > 1) && (mi_atomic_load_acquire(ref segment->thread_id) == 0));
            _ = mi_atomic_decrement_relaxed(ref mi_abandoned_segment_count);
            _ = mi_atomic_decrement_relaxed(ref subproc->abandoned_count);
            mi_atomic_store_release(ref segment->thread_id, _mi_thread_id());
        }
        mi_assert_internal((MI_DEBUG > 1) && (!cleared || mi_bitmap_is_claimed(&arena->blocks_inuse.e0, arena->field_count, 1, bitmap_index)));

        return cleared;
    }

    private static void _mi_arena_segment_mark_abandoned(mi_segment_t* segment)
    {
        mi_atomic_store_release(ref segment->thread_id, 0);
        mi_assert_internal((MI_DEBUG > 1) && (segment->used == segment->abandoned));

        if (segment->memid.memkind != mi_memkind_t.MI_MEM_ARENA)
        {
            mi_subproc_t* subproc = segment->subproc;
            mi_os_abandoned_lock_acquire(ref subproc->abandoned_os_lock);

            mi_segment_t* prev = subproc->abandoned_os_list_tail;
            mi_assert_internal((MI_DEBUG > 1) && ((prev == null) || (prev->abandoned_os_next == null)));
            mi_assert_internal((MI_DEBUG > 1) && (segment->abandoned_os_prev == null) && (segment->abandoned_os_next == null));

            if (prev != null)
            {
                prev->abandoned_os_next = segment;
            }
            else
            {
                subproc->abandoned_os_list = segment;
            }

            subproc->abandoned_os_list_tail = segment;
            segment->abandoned_os_prev = prev;
            segment->abandoned_os_next = null;
            _ = mi_atomic_increment_relaxed(ref subproc->abandoned_os_list_count);
            _ = mi_atomic_increment_relaxed(ref subproc->abandoned_count);
            _ = mi_atomic_increment_relaxed(ref mi_abandoned_segment_count);

            mi_os_abandoned_lock_release(ref subproc->abandoned_os_lock);
            return;
        }

        mi_arena_memid_indices(segment->memid, out nuint arena_index, out nuint bitmap_index);
        mi_assert_internal((MI_DEBUG > 1) && (arena_index < MI_MAX_ARENAS));

        mi_arena_t* arena = mi_atomic_load_ptr_acquire<mi_arena_t>(ref mi_arenas[arena_index]);
        mi_assert_internal((MI_DEBUG > 1) && (arena != null));

        if (arena != null)
        {
            bool was_unset = mi_bitmap_claim(arena->blocks_abandoned, arena->field_count, 1, bitmap_index, out _);
            if (was_unset)
            {
                _ = mi_atomic_increment_relaxed(ref segment->subproc->abandoned_count);
                _ = mi_atomic_increment_relaxed(ref mi_abandoned_segment_count);
            }
            mi_assert_internal((MI_DEBUG > 1) && was_unset);
            mi_assert_internal((MI_DEBUG > 1) && mi_bitmap_is_claimed(&arena->blocks_inuse.e0, arena->field_count, 1, bitmap_index));
        }
    }

#pragma warning disable CS0420
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint _mi_arena_segment_abandoned_count()
        => mi_atomic_load_relaxed(ref mi_abandoned_segment_count);
#pragma warning restore CS0420

    private struct mi_arena_field_cursor_t
    {
        public mi_subproc_t* subproc;
        public nuint start;
        public nuint end;
        public nuint bitmap_index;
        public nuint os_list_count;
        public bool visit_all;
        public bool hold_visit_lock;
    }

#pragma warning disable CS0420
    private static void _mi_arena_field_cursor_init(mi_heap_t* heap, mi_subproc_t* subproc, bool visit_all, out mi_arena_field_cursor_t current)
    {
        current.subproc = (heap == null) ? subproc : heap->tld->segments.subproc;
        if (current.subproc == null)
        {
            current.subproc = _mi_subproc_default;
        }

        nuint max_arena = mi_atomic_load_relaxed(ref mi_arena_count);
        current.bitmap_index = 0;
        current.visit_all = visit_all;
        current.hold_visit_lock = false;

        if ((heap != null) && (heap->arena_id != 0))
        {
            current.start = mi_arena_id_index(heap->arena_id);
            current.end = current.start + 1;
            current.os_list_count = 0;
        }
        else
        {
            current.os_list_count = mi_atomic_load_relaxed(ref current.subproc->abandoned_os_list_count);
            if ((mi_atomic_load_relaxed(ref current.subproc->abandoned_count) > current.os_list_count) && (max_arena > 0))
            {
                current.start = (heap == null) ? 0 : (_mi_heap_random_next(heap) % max_arena);
                current.end = current.start + max_arena;
            }
            else
            {
                current.start = 0;
                current.end = 0;
            }
        }

        mi_assert_internal((MI_DEBUG > 1) && ((current.start >= current.end) || (current.start < max_arena)));
    }

    private static mi_segment_t* _mi_arena_segment_clear_abandoned_next_field(ref mi_arena_field_cursor_t previous)
    {
        nuint max_arena = mi_atomic_load_relaxed(ref mi_arena_count);
        nuint field_index = mi_bitmap_index_field(previous.bitmap_index);
        nuint bit_index = mi_bitmap_index_bit_in_field(previous.bitmap_index);

        for (; previous.start < previous.end; previous.start++, field_index = 0, bit_index = 0)
        {
            nuint arena_index = (previous.start >= max_arena) ? (previous.start % max_arena) : previous.start;

            mi_arena_t* arena = mi_atomic_load_ptr_acquire<mi_arena_t>(ref mi_arenas[arena_index]);

            if (arena == null)
            {
                continue;
            }

            bool has_lock = false;
            for (; field_index < arena->field_count; field_index++, bit_index = 0)
            {
                nuint field = mi_atomic_load_relaxed(ref arena->blocks_abandoned[field_index]);

                if (field == 0)
                {
                    continue;
                }

                if (!has_lock && mi_option_is_enabled(mi_option_visit_abandoned))
                {
                    if (previous.visit_all)
                    {
                        mi_os_abandoned_lock_acquire(ref arena->abandoned_visit_lock);
                        has_lock = true;
                    }
                    else
                    {
                        has_lock = mi_os_abandoned_lock_try_acquire(ref arena->abandoned_visit_lock);
                        if (!has_lock)
                        {
                            break;
                        }
                    }
                }

                for (; bit_index < MI_BITMAP_FIELD_BITS; bit_index++)
                {
                    nuint mask = (nuint)1 << (int)bit_index;

                    if ((field & mask) == 0)
                    {
                        continue;
                    }

                    nuint bitmap_index = mi_bitmap_index_create(field_index, bit_index);

                    if (mi_bitmap_unclaim(arena->blocks_abandoned, arena->field_count, 1, bitmap_index))
                    {
                        mi_segment_t* segment = (mi_segment_t*)mi_arena_block_start(arena, bitmap_index);
                        mi_assert_internal((MI_DEBUG > 1) && (mi_atomic_load_relaxed(ref segment->thread_id) == 0));
                        if (segment->subproc != previous.subproc)
                        {
                            _ = mi_bitmap_claim(arena->blocks_abandoned, arena->field_count, 1, bitmap_index, out _);
                            continue;
                        }

                        _ = mi_atomic_decrement_relaxed(ref mi_abandoned_segment_count);
                        _ = mi_atomic_decrement_relaxed(ref previous.subproc->abandoned_count);
                        previous.bitmap_index = (field_index * MI_BITMAP_FIELD_BITS) + bit_index + 1;
                        mi_assert_internal((MI_DEBUG > 1) && mi_bitmap_is_claimed(&arena->blocks_inuse.e0, arena->field_count, 1, bitmap_index));
                        if (has_lock)
                        {
                            mi_os_abandoned_lock_release(ref arena->abandoned_visit_lock);
                        }
                        return segment;
                    }
                }
            }

            if (has_lock)
            {
                mi_os_abandoned_lock_release(ref arena->abandoned_visit_lock);
            }
        }

        previous.bitmap_index = 0;
        return null;
    }

    private static mi_segment_t* _mi_arena_segment_clear_abandoned_next_list(ref mi_arena_field_cursor_t previous)
    {
        if (previous.os_list_count == 0)
        {
            return null;
        }

        if (!previous.hold_visit_lock)
        {
            if (previous.visit_all)
            {
                mi_os_abandoned_lock_acquire(ref previous.subproc->abandoned_os_visit_lock);
                previous.hold_visit_lock = true;
            }
            else
            {
                previous.hold_visit_lock = mi_os_abandoned_lock_try_acquire(ref previous.subproc->abandoned_os_visit_lock);
                if (!previous.hold_visit_lock)
                {
                    return null;
                }
            }
        }

        while (previous.os_list_count > 0)
        {
            previous.os_list_count--;
            mi_os_abandoned_lock_acquire(ref previous.subproc->abandoned_os_lock);
            mi_segment_t* segment = previous.subproc->abandoned_os_list;

            if ((segment == null) || mi_arena_segment_os_clear_abandoned(segment, false))
            {
                mi_os_abandoned_lock_release(ref previous.subproc->abandoned_os_lock);
                return segment;
            }

            mi_os_abandoned_lock_release(ref previous.subproc->abandoned_os_lock);
        }

        return null;
    }

    private static mi_segment_t* _mi_arena_segment_clear_abandoned_next(ref mi_arena_field_cursor_t previous)
    {
        if (mi_atomic_load_relaxed(ref previous.subproc->abandoned_count) == 0)
        {
            return null;
        }

        if (previous.start < previous.end)
        {
            mi_segment_t* segment = _mi_arena_segment_clear_abandoned_next_field(ref previous);
            if (segment != null)
            {
                return segment;
            }
        }

        mi_assert_internal((MI_DEBUG > 1) && (previous.start == previous.end));
        return _mi_arena_segment_clear_abandoned_next_list(ref previous);
    }

    private static void _mi_arena_field_cursor_done(ref mi_arena_field_cursor_t current)
    {
        if (current.hold_visit_lock)
        {
            mi_os_abandoned_lock_release(ref current.subproc->abandoned_os_visit_lock);
            current.hold_visit_lock = false;
        }
    }
#pragma warning restore CS0420
#pragma warning restore CS0420

    private static bool mi_arena_bitmap_try_claim(nuint* bitmap, nuint bitmap_fields, nuint blocks, nuint bitmap_index)
    {
        nuint field_idx = mi_bitmap_index_field(bitmap_index);
        nuint bit_idx = mi_bitmap_index_bit_in_field(bitmap_index);

        if ((field_idx >= bitmap_fields) || (blocks == 0) || (blocks > (MI_BITMAP_FIELD_BITS - bit_idx)))
        {
            return false;
        }

        nuint* field = bitmap + field_idx;
        nuint mask = mi_bitmap_mask_(blocks, bit_idx);
        nuint map = mi_atomic_load_relaxed(ref *field);

        while (true)
        {
            if ((map & mask) != 0)
            {
                return false;
            }

            nuint newmap = map | mask;
            if (mi_atomic_cas_strong_acq_rel(ref *field, ref map, newmap))
            {
                return true;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long mi_arena_purge_delay()
        => (long)mi_option_get(mi_option_purge_delay) * mi_option_get(mi_option_arena_purge_mult);

    private static void mi_arena_purge(mi_arena_t* arena, nuint bitmap_index, nuint blocks, ref mi_stats_internal_t stats)
    {
        mi_assert_internal((MI_DEBUG > 1) && (arena->blocks_committed != null));
        mi_assert_internal((MI_DEBUG > 1) && (arena->blocks_purge != null));
        mi_assert_internal((MI_DEBUG > 1) && !arena->memid.is_pinned);

        nuint size = blocks * MI_ARENA_BLOCK_SIZE;
        void* p = mi_arena_block_start(arena, bitmap_index);
        bool needs_recommit;

        if (mi_bitmap_is_claimed_across(arena->blocks_committed, arena->field_count, blocks, bitmap_index))
        {
            needs_recommit = _mi_os_purge(p, size, ref stats);
        }
        else
        {
            needs_recommit = _mi_os_purge_ex(p, size, false, ref stats);
            _mi_stat_increase(ref stats.committed, size);
        }

        _ = mi_bitmap_unclaim_across(arena->blocks_purge, arena->field_count, blocks, bitmap_index);

        if (needs_recommit)
        {
            _ = mi_bitmap_unclaim_across(arena->blocks_committed, arena->field_count, blocks, bitmap_index);
        }
    }

    private static void mi_arena_schedule_purge(mi_arena_t* arena, nuint bitmap_index, nuint blocks, ref mi_stats_internal_t stats)
    {
#pragma warning disable CS0420
        mi_assert_internal((MI_DEBUG > 1) && (arena->blocks_purge != null));

        long delay = mi_arena_purge_delay();
        if (delay < 0)
        {
            return;
        }
        if (delay <= 0)
        {
            mi_arena_purge(arena, bitmap_index, blocks, ref stats);
        }
        else
        {
            _ = mi_bitmap_claim_across(arena->blocks_purge, arena->field_count, blocks, bitmap_index, out _);

            long expire = mi_atomic_loadi64_relaxed(ref arena->purge_expire);

            if (expire != 0)
            {
                _ = mi_atomic_addi64_relaxed(ref arena->purge_expire, delay / 10);
            }
            else
            {
                mi_atomic_storei64_release(ref arena->purge_expire, _mi_clock_now() + delay);
            }
        }
#pragma warning restore CS0420
    }

    private static bool mi_arena_purge_range(mi_arena_t* arena, nuint field_idx, nuint start_idx, nuint bitlen, nuint purge, ref mi_stats_internal_t stats)
    {
        nuint end_idx = start_idx + bitlen;
        nuint bit_idx = start_idx;
        bool all_purged = true;

        while (bit_idx < end_idx)
        {
            if ((purge & ((nuint)1 << (int)bit_idx)) != 0)
            {
                nuint count = 0;

                while ((bit_idx + count < end_idx) && ((purge & ((nuint)1 << (int)(bit_idx + count))) != 0))
                {
                    count++;
                }

                nuint bitmap_index = mi_bitmap_index_create(field_idx, bit_idx);
                mi_arena_purge(arena, bitmap_index, count, ref stats);
                bit_idx += count;
            }
            else
            {
                all_purged = false;
                bit_idx++;
            }
        }

        return all_purged;
    }

    private static bool mi_arena_try_purge(mi_arena_t* arena, long now, bool force, ref mi_stats_internal_t stats)
    {
#pragma warning disable CS0420
        if (arena->memid.is_pinned || (arena->blocks_purge == null))
        {
            return false;
        }

        long expire = mi_atomic_loadi64_relaxed(ref arena->purge_expire);

        if ((expire == 0) || (!force && (expire > now)))
        {
            return false;
        }

        _ = mi_atomic_casi64_strong_acq_rel(ref arena->purge_expire, ref expire, 0);
        mi_stat_counter_increase(ref _mi_stats_main.arena_purges, 1);

        bool any_purged = false;
        bool full_purge = true;

        for (nuint i = 0; i < arena->field_count; i++)
        {
            nuint purge = mi_atomic_load_relaxed(ref arena->blocks_purge[i]);
            nuint bit_idx = 0;

            while ((purge != 0) && (bit_idx < MI_BITMAP_FIELD_BITS))
            {
                nuint bitlen = 0;

                while ((bit_idx + bitlen < MI_BITMAP_FIELD_BITS) && ((purge & ((nuint)1 << (int)(bit_idx + bitlen))) != 0))
                {
                    bitlen++;
                }

                if (bitlen > 0)
                {
                    nuint bitmap_index = mi_bitmap_index_create(i, bit_idx);

                    while ((bitlen > 0) && !mi_arena_bitmap_try_claim(&arena->blocks_inuse.e0, arena->field_count, bitlen, bitmap_index))
                    {
                        bitlen--;
                    }

                    if (bitlen > 0)
                    {
                        purge = mi_atomic_load_acquire(ref arena->blocks_purge[i]);

                        if (!mi_arena_purge_range(arena, i, bit_idx, bitlen, purge, ref stats))
                        {
                            full_purge = false;
                        }

                        any_purged = true;
                        _ = mi_bitmap_unclaim_across(&arena->blocks_inuse.e0, arena->field_count, bitlen, bitmap_index);
                    }
                }

                bit_idx += (bitlen > 0) ? bitlen : 1;
            }
        }

        if (!full_purge)
        {
            long delay = mi_arena_purge_delay();
            long expected = 0;
            _ = mi_atomic_casi64_strong_acq_rel(ref arena->purge_expire, ref expected, _mi_clock_now() + delay);
        }

        return any_purged;
#pragma warning restore CS0420
    }

    private static void mi_arenas_try_purge(bool force, bool visit_all, ref mi_stats_internal_t stats)
    {
#pragma warning disable CS0420
        if (mi_arena_purge_delay() <= 0)
        {
            return;
        }

        nuint max_arena = mi_atomic_load_acquire(ref mi_arena_count);
        if (max_arena == 0)
        {
            return;
        }

        nuint expected = 0;
        if (!mi_atomic_cas_strong_acq_rel(ref mi_arena_purge_guard, ref expected, 1))
        {
            return;
        }

        try
        {
            long now = _mi_clock_now();
            nuint max_purge_count = visit_all ? max_arena : 1;

            for (nuint i = 0; i < max_arena; i++)
            {
                mi_arena_t* arena = mi_atomic_load_ptr_acquire<mi_arena_t>(ref mi_arenas[i]);

                if ((arena != null) && mi_arena_try_purge(arena, now, force, ref stats))
                {
                    if (max_purge_count <= 1)
                    {
                        break;
                    }

                    max_purge_count--;
                }
            }
        }
        finally
        {
            mi_atomic_store_release(ref mi_arena_purge_guard, 0);
        }
#pragma warning restore CS0420
    }

    private static void _mi_arenas_collect(bool force_purge, ref mi_stats_internal_t stats)
    {
        mi_arenas_try_purge(force_purge, force_purge, ref stats);
    }

    /* -----------------------------------------------------------
      Arena free
    ----------------------------------------------------------- */

    private static partial void _mi_arena_free(void* p, nuint size, nuint alignment, nuint align_offset, mi_memid_t memid, nuint committed_size, ref mi_stats_internal_t stats)
    {
#pragma warning disable CS0420
        mi_assert_internal((MI_DEBUG > 1) && (size > 0));
        mi_assert_internal((MI_DEBUG > 1) && (committed_size <= size));

        if (p == null)
        {
            return;
        }

        if (size == 0)
        {
            return;
        }

        if (mi_memkind_is_os(memid.memkind))
        {
            // was a direct OS allocation, pass through
            bool all_committed = committed_size == size;

            if (!all_committed && (committed_size > 0))
            {
                _mi_stat_decrease(ref stats.committed, committed_size);
            }

            _mi_os_free_ex(p, size, all_committed, memid, ref stats);
        }
        else if (memid.memkind == mi_memkind_t.MI_MEM_STATIC)
        {
            return;
        }
        else if (memid.memkind == mi_memkind_t.MI_MEM_ARENA)
        {
            // allocated in an arena
            mi_assert_internal((MI_DEBUG > 1) && (align_offset == 0));

            mi_arena_memid_indices(memid, out nuint arena_idx, out nuint bitmap_idx);
            mi_assert_internal((MI_DEBUG > 1) && (arena_idx < MI_MAX_ARENAS));

            mi_arena_t* arena = mi_atomic_load_ptr_relaxed<mi_arena_t>(ref mi_arenas[arena_idx]);
            mi_assert_internal((MI_DEBUG > 1) && (arena != null));
            mi_assert_internal((MI_DEBUG > 1) && (arena != null && (arena->exclusive == memid.mem.arena.is_exclusive) && (arena->id == memid.mem.arena.id)));

            if (arena == null)
            {
                _mi_error_message(EINVAL, "trying to free from non-existent arena: {0:X}, size {1}, arena id: {2}\n", (nuint)p, size, memid.mem.arena.id);
                return;
            }

            mi_assert_internal((MI_DEBUG > 1) && (arena->field_count > mi_bitmap_index_field(bitmap_idx)));

            if (arena->field_count <= mi_bitmap_index_field(bitmap_idx))
            {
                _mi_error_message(EINVAL, "trying to free from non-existent arena block: {0}, size {1}, arena id: {2}, block index: {3}\n", (nuint)p, size, memid.mem.arena.id, bitmap_idx);
                return;
            }

            nuint blocks = mi_block_count_of_size(size);
            bool all_committed = committed_size == size;

            if (arena->memid.is_pinned || (arena->blocks_committed == null))
            {
                mi_assert_internal((MI_DEBUG > 1) && all_committed);
            }
            else
            {
                mi_assert_internal((MI_DEBUG > 1) && (arena->blocks_committed != null));

                if (!all_committed)
                {
                    _ = mi_bitmap_unclaim_across(arena->blocks_committed, arena->field_count, blocks, bitmap_idx);

                    if (committed_size > 0)
                    {
                        _mi_stat_decrease(ref stats.committed, committed_size);
                    }
                }

                mi_arena_schedule_purge(arena, bitmap_idx, blocks, ref stats);
            }

            bool ones = mi_bitmap_unclaim_across(&arena->blocks_inuse.e0, arena->field_count, blocks, bitmap_idx);

            if (!ones)
            {
                _mi_error_message(EAGAIN, "trying to free an already freed block: {0:X}, size {1}\n", (nuint)p, size);
                return;
            };

            mi_arenas_try_purge(false, false, ref stats);
        }
        else
        {
            _mi_error_message(EINVAL, "trying to free memory with invalid memory id kind: {0}, address: {1:X}, size: {2}\n", memid.memkind, (nuint)p, size);
        }
#pragma warning restore CS0420
    }

    /* -----------------------------------------------------------
      Add an arena.
    ----------------------------------------------------------- */

    private static bool mi_arena_add(mi_arena_t* arena, int* arena_id)
    {
#pragma warning disable CS0420
        mi_assert_internal((MI_DEBUG > 1) && (arena != null));
        mi_assert_internal((MI_DEBUG > 1) && (((nuint)mi_atomic_load_ptr_relaxed<byte>(ref arena->start) % MI_SEGMENT_ALIGN) == 0));
        mi_assert_internal((MI_DEBUG > 1) && (arena->block_count > 0));

        if (arena_id != null)
        {
            *arena_id = 0;
        }

        nuint i = mi_atomic_load_relaxed(ref mi_arena_count);
        while (i < MI_MAX_ARENAS)
        {
            if (mi_atomic_cas_strong_acq_rel(ref mi_arena_count, ref i, i + 1))
            {
                mi_stat_counter_increase(ref _mi_stats_main.arena_count, 1);
                arena->id = mi_arena_id_create(i);
                mi_atomic_store_ptr_release<mi_arena_t>(ref mi_arenas[i], arena);

                if (arena_id != null)
                {
                    *arena_id = arena->id;
                }

                return true;
            }
        }

        return false;
#pragma warning restore CS0420
    }

    private static bool mi_manage_os_memory_ex2(void* start, nuint size, bool is_large, int numa_node, bool exclusive, mi_memid_t memid, int* arena_id)
    {
#pragma warning disable CS0420
        if (arena_id != null)
        {
            *arena_id = 0;
        }

        if (size < MI_ARENA_BLOCK_SIZE)
        {
            _mi_warning_message("the arena size is too small (memory at {0:X} with size {1})\n", (nuint)start, size);
            return false;
        }

        if (size > MI_MAX_ALLOC_SIZE)
        {
            _mi_error_message(EOVERFLOW, "the arena size is too large (memory at {0:X} with size {1})\n", (nuint)start, size);
            return false;
        }

        mi_assert_internal((MI_DEBUG > 1) && (!is_large || (memid.initially_committed && memid.is_pinned)));

        nuint start_address = (nuint)start;
        nuint misalignment = start_address & (MI_SEGMENT_ALIGN - 1);
        if (misalignment != 0)
        {
            nuint diff = MI_SEGMENT_ALIGN - misalignment;
            if ((diff >= size) || ((size - diff) < MI_ARENA_BLOCK_SIZE) || (start_address > (nuint.MaxValue - diff)))
            {
                _mi_warning_message("after alignment, the arena size is too small (memory at {0:X} with size {1})\n", start_address, size);
                return false;
            }

            start = (void*)(start_address + diff);
            size -= diff;
        }

        nuint bcount = size / MI_ARENA_BLOCK_SIZE;
        nuint fields = _mi_divide_up(bcount, MI_BITMAP_FIELD_BITS);
        nuint bitmaps = memid.is_pinned ? 3u : 5u;
        nuint asize = SizeOf<mi_arena_t>() + (bitmaps * fields * SizeOf<nuint>());

        mi_memid_t meta_memid;
        mi_arena_t* arena = (mi_arena_t*)_mi_arena_meta_zalloc(asize, out meta_memid, ref _mi_stats_main);

        if (arena == null)
        {
            return false;
        }

        arena->id = 0;
        arena->memid = memid;
        arena->exclusive = exclusive;
        arena->meta_size = asize;
        arena->meta_memid = meta_memid;
        arena->block_count = bcount;
        arena->field_count = fields;
        arena->start = (nuint)start;
        arena->numa_node = numa_node;
        arena->is_large = is_large;
        arena->search_idx = 0;
        arena->purge_expire = 0;
        arena->blocks_dirty = &arena->blocks_inuse.e0 + fields;
        arena->blocks_abandoned = &arena->blocks_inuse.e0 + (2 * fields);
        arena->blocks_committed = memid.is_pinned ? null : &arena->blocks_inuse.e0 + (3 * fields);
        arena->blocks_purge = memid.is_pinned ? null : &arena->blocks_inuse.e0 + (4 * fields);

        if ((arena->blocks_committed != null) && memid.initially_committed)
        {
            for (nuint i = 0; i < fields; i++)
            {
                mi_atomic_store_relaxed(ref arena->blocks_committed[i], MI_BITMAP_FIELD_FULL);
            }
        }

        nint post = (nint)((fields * MI_BITMAP_FIELD_BITS) - bcount);
        mi_assert_internal((MI_DEBUG > 1) && (post >= 0));

        if (post > 0)
        {
            nuint postidx = mi_bitmap_index_create(fields - 1, MI_BITMAP_FIELD_BITS - (nuint)post);
            _ = mi_bitmap_claim(&arena->blocks_inuse.e0, fields, (nuint)post, postidx, out _);
        }

        if (!mi_arena_add(arena, arena_id))
        {
            _mi_arena_meta_free(arena, asize, meta_memid, ref _mi_stats_main);
            return false;
        }

        return true;
#pragma warning restore CS0420
    }

    public static partial bool mi_manage_os_memory_ex(void* start, nuint size, bool is_committed, bool is_large, bool is_zero, int numa_node, bool exclusive, int* arena_id)
    {
        mi_memid_t memid = mi_memid_create(mi_memkind_t.MI_MEM_EXTERNAL);
        memid.initially_committed = is_committed;
        memid.initially_zero = is_zero;
        memid.is_pinned = is_large;
        return mi_manage_os_memory_ex2(start, size, is_large, numa_node, exclusive, memid, arena_id);
    }

    public static partial bool mi_manage_os_memory(void* start, nuint size, bool is_committed, bool is_large, bool is_zero, int numa_node)
        => mi_manage_os_memory_ex(start, size, is_committed, is_large, is_zero, numa_node, exclusive: false, arena_id: null);

    public static partial void* mi_arena_area(int arena_id, nuint* size)
    {
        if (size != null)
        {
            *size = 0;
        }

        nuint arena_index = mi_arena_id_index(arena_id);
        if (arena_index >= MI_MAX_ARENAS)
        {
            return null;
        }

        mi_arena_t* arena = mi_atomic_load_ptr_acquire<mi_arena_t>(ref mi_arenas[arena_index]);
        if (arena == null)
        {
            return null;
        }

        if (size != null)
        {
            *size = arena->block_count * MI_ARENA_BLOCK_SIZE;
        }

        return (void*)arena->start;
    }

    private static nuint mi_debug_show_bitmap(string prefix, string header, nuint block_count, nuint* fields, nuint field_count)
    {
        _mi_fprintf(null, null, "{0}{1}:\n", prefix, header);

        nuint bit_count = 0;
        nuint inuse_count = 0;
        char* buffer = stackalloc char[(int)MI_BITMAP_FIELD_BITS + 1];

        for (nuint i = 0; i < field_count; i++)
        {
            nuint field = mi_atomic_load_relaxed(ref fields[i]);

            for (nuint bit = 0; bit < MI_BITMAP_FIELD_BITS; bit++, bit_count++)
            {
                if (bit_count < block_count)
                {
                    bool inuse = (field & ((nuint)1 << (int)bit)) != 0;
                    if (inuse)
                    {
                        inuse_count++;
                    }

                    buffer[(int)bit] = inuse ? 'x' : '.';
                }
                else
                {
                    buffer[(int)bit] = ' ';
                }
            }

            buffer[(int)MI_BITMAP_FIELD_BITS] = '\0';
            _mi_fprintf(null, null, "{0}  {1}\n", prefix, new string(buffer, 0, (int)MI_BITMAP_FIELD_BITS));
        }

        _mi_fprintf(null, null, "{0}  total ('x'): {1}\n", prefix, inuse_count);
        return inuse_count;
    }

    public static partial void mi_debug_show_arenas(bool show_inuse, bool show_abandoned, bool show_purge)
    {
#pragma warning disable CS0420
        nuint max_arena = mi_atomic_load_relaxed(ref mi_arena_count);
        nuint inuse_total = 0;
        nuint abandoned_total = 0;
        nuint purge_total = 0;

        for (nuint i = 0; i < max_arena; i++)
        {
            mi_arena_t* arena = mi_atomic_load_ptr_relaxed<mi_arena_t>(ref mi_arenas[i]);
            if (arena == null)
            {
                break;
            }

            _mi_fprintf(null, null, "arena {0}: {1} blocks of size {2} MiB (in {3} fields){4}\n", i, arena->block_count, MI_ARENA_BLOCK_SIZE / MiB, arena->field_count, arena->memid.is_pinned ? ", pinned" : "");

            if (show_inuse)
            {
                inuse_total += mi_debug_show_bitmap("  ", "inuse blocks", arena->block_count, &arena->blocks_inuse.e0, arena->field_count);
            }

            if (arena->blocks_committed != null)
            {
                _ = mi_debug_show_bitmap("  ", "committed blocks", arena->block_count, arena->blocks_committed, arena->field_count);
            }

            if (show_abandoned)
            {
                abandoned_total += mi_debug_show_bitmap("  ", "abandoned blocks", arena->block_count, arena->blocks_abandoned, arena->field_count);
            }

            if (show_purge && (arena->blocks_purge != null))
            {
                purge_total += mi_debug_show_bitmap("  ", "purgeable blocks", arena->block_count, arena->blocks_purge, arena->field_count);
            }
        }

        if (show_inuse)
        {
            _mi_fprintf(null, null, "total inuse blocks    : {0}\n", inuse_total);
        }
        if (show_abandoned)
        {
            _mi_fprintf(null, null, "total abandoned blocks: {0}\n", abandoned_total);
        }
        if (show_purge)
        {
            _mi_fprintf(null, null, "total purgeable blocks: {0}\n", purge_total);
        }
#pragma warning restore CS0420
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void mi_arenas_print() => mi_debug_show_arenas(true, true, true);

    public static partial int mi_reserve_os_memory_ex(nuint size, bool commit, bool allow_large, bool exclusive, int* arena_id)
    {
        if (arena_id != null)
        {
            *arena_id = 0;
        }

        if ((size < MI_MAX_ALLOC_SIZE) && (size > (SIZE_MAX - (MI_ARENA_BLOCK_SIZE - 1))))
        {
            return ENOMEM;
        }

        if (size < MI_MAX_ALLOC_SIZE)
        {
            size = _mi_align_up(size, MI_ARENA_BLOCK_SIZE);
        }

        if (size > MI_MAX_ALLOC_SIZE)
        {
            _mi_error_message(EOVERFLOW, "memory reservation request is too large (size {0})\n", size);
            return ENOMEM;
        }

        void* start = _mi_os_alloc_aligned(size, MI_SEGMENT_ALIGN, commit, allow_large, out mi_memid_t memid, ref _mi_stats_main);

        if (start == null)
        {
            return ENOMEM;
        }

        bool large = memid.is_pinned;
        if (!mi_manage_os_memory_ex2(start, size, large, -1, exclusive, memid, arena_id))
        {
            _mi_os_free_ex(start, size, commit, memid, ref _mi_stats_main);
            _mi_verbose_message("failed to reserve {0} k memory\n", _mi_divide_up(size, 1024));
            return ENOMEM;
        }

        _mi_verbose_message("reserved {0} kb memory{1}\n", _mi_divide_up(size, 1024), large ? " (in large os pages)" : "");
        return 0;
    }

    public static partial int mi_reserve_os_memory(nuint size, bool commit, bool allow_large)
        => mi_reserve_os_memory_ex(size, commit, allow_large, exclusive: false, arena_id: null);


    /* -----------------------------------------------------------
      Reserve a huge page arena.
    ----------------------------------------------------------- */

    // reserve at a specific numa node
    public static partial int mi_reserve_huge_os_pages_at_ex(nuint pages, int numa_node, nuint timeout_msecs, bool exclusive, int* arena_id)
    {
#pragma warning disable CS0420
        if (arena_id != null)
        {
            *arena_id = 0;
        }

        if (pages == 0)
        {
            return 0;
        }

        if (numa_node < -1)
        {
            numa_node = -1;
        }

        if (numa_node >= 0)
        {
            numa_node %= (int)_mi_os_numa_node_count();
        }

        void* p = _mi_os_alloc_huge_os_pages(pages, numa_node, (long)timeout_msecs, out nuint pages_reserved, out nuint hsize, out mi_memid_t memid);

        if ((p == null) || (pages_reserved == 0))
        {
            _mi_warning_message("failed to reserve {0} gb huge pages\n", pages);
            return ENOMEM;
        }

        _mi_verbose_message("numa node {0}: reserved {1} gb huge pages (of the {2} gb requested)\n", numa_node, pages_reserved, pages);

        if (!mi_manage_os_memory_ex2(p, hsize, true, numa_node, exclusive, memid, arena_id))
        {
            _mi_os_free(p, hsize, memid, ref _mi_stats_main);
            return ENOMEM;
        }
        return 0;
#pragma warning restore CS0420
    }

    public static partial int mi_reserve_huge_os_pages_at(nuint pages, int numa_node, nuint timeout_msecs)
        => mi_reserve_huge_os_pages_at_ex(pages, numa_node, timeout_msecs, exclusive: false, arena_id: null);

    // reserve huge pages evenly among the given number of numa nodes (or use the available ones as detected)
    public static partial int mi_reserve_huge_os_pages_interleave(nuint pages, nuint numa_nodes, nuint timeout_msecs)
    {
        if (pages == 0)
        {
            return 0;
        }

        // pages per numa node
        nuint numa_count = (numa_nodes > 0) ? numa_nodes : _mi_os_numa_node_count();

        if (numa_count <= 0)
        {
            numa_count = 1;
        }

        nuint pages_per = pages / numa_count;
        nuint pages_mod = pages % numa_count;
        nuint timeout_per = (timeout_msecs == 0) ? 0 : ((timeout_msecs / numa_count) + 50);

        // reserve evenly among numa nodes
        for (nuint numa_node = 0; (numa_node < numa_count) && (pages > 0); numa_node++)
        {
            // can be 0
            nuint node_pages = pages_per;

            if (numa_node < pages_mod)
            {
                node_pages++;
            }

            int err = mi_reserve_huge_os_pages_at(node_pages, (int)numa_node, timeout_per);

            if (err != 0)
            {
                return err;
            }

            if (pages < node_pages)
            {
                pages = 0;
            }
            else
            {
                pages -= node_pages;
            }
        }

        return 0;
    }

    public static partial int mi_reserve_huge_os_pages(nuint pages, double max_secs, nuint* pages_reserved)
    {
        _mi_warning_message("mi_reserve_huge_os_pages is deprecated: use mi_reserve_huge_os_pages_interleave/at instead\n");

        if (pages_reserved != null)
        {
            *pages_reserved = 0;
        }

        int err = mi_reserve_huge_os_pages_interleave(pages, 0, (nuint)(max_secs * 1000));

        if ((err == 0) && (pages_reserved != null))
        {
            *pages_reserved = pages;
        }

        return err;
    }
}
