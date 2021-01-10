// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the arena.c file from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

using System.Runtime.CompilerServices;

namespace TerraFX.Interop
{
    public static unsafe partial class Mimalloc
    {
        /* ----------------------------------------------------------------------------
        "Arenas" are fixed area's of OS memory from which we can allocate
        large blocks (>= MI_ARENA_BLOCK_SIZE, 32MiB).
        In contrast to the rest of mimalloc, the arenas are shared between
        threads and need to be accessed using atomic operations.

        Currently arenas are only used to for huge OS page (1GiB) reservations,
        otherwise it delegates to direct allocation from the OS.
        In the future, we can expose an API to manually add more kinds of arenas
        which is sometimes needed for embedded devices or shared memory for example.
        (We can also employ this with WASI or `sbrk` systems to reserve large arenas
         on demand and be able to reuse them efficiently).

        The arena allocation needs to be thread safe and we use an atomic
        bitmap to allocate. The current implementation of the bitmap can
        only do this within a field (`uintptr_t`) so we can allocate at most
        blocks of 2GiB (64*32MiB) and no object can cross the boundary. This
        can lead to fragmentation but fortunately most objects will be regions
        of 256MiB in practice.
        -----------------------------------------------------------------------------*/

        // os.c

        private static partial void* _mi_os_alloc_aligned([NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment, bool commit, [NativeTypeName("bool*")] ref bool large, mi_os_tld_t* tld);

        private static partial void _mi_os_free_ex(void* p, [NativeTypeName("size_t")] nuint size, bool was_committed, [NativeTypeName("mi_stats_t*")] ref mi_stats_t stats);

        private static partial void* _mi_os_alloc_huge_os_pages([NativeTypeName("size_t")] nuint pages, int numa_node, [NativeTypeName("mi_msecs_t")] long max_secs, [NativeTypeName("size_t*")] out nuint pages_reserved, [NativeTypeName("size_t*")] out nuint psize);

        private static partial void _mi_os_free_huge_pages(void* p, [NativeTypeName("size_t")] nuint size, [NativeTypeName("mi_stats_t*")] ref mi_stats_t stats);

        /* -----------------------------------------------------------
          Arena allocations get a memory id where the lower 8 bits are
          the arena index +1, and the upper bits the block index.
        ----------------------------------------------------------- */

        // Use `0` as a special id for direct OS allocated memory.
        private const nuint MI_MEMID_OS = 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [return: NativeTypeName("size_t")]
        private static nuint mi_arena_id_create([NativeTypeName("size_t")] nuint arena_index, [NativeTypeName("mi_bitmap_index_t")] nuint bitmap_index)
        {
            mi_assert_internal((MI_DEBUG > 1) && (arena_index < 0xFE));

            // no overflow?
            mi_assert_internal((MI_DEBUG > 1) && (((bitmap_index << 8) >> 8) == bitmap_index));

            return (bitmap_index << 8) | ((arena_index + 1) & 0xFF);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void mi_arena_id_indices([NativeTypeName("size_t")] nuint memid, [NativeTypeName("size_t*")] out nuint arena_index, [NativeTypeName("mi_bitmap_index_t*")] out nuint bitmap_index)
        {
            mi_assert_internal((MI_DEBUG > 1) && (memid != MI_MEMID_OS));
            arena_index = (memid & 0xFF) - 1;
            bitmap_index = memid >> 8;
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
            nuint fcount = arena->field_count;

            // start from last search
            nuint idx = mi_atomic_load_acquire(ref arena->search_idx);

            for (nuint visited = 0; visited < fcount; visited++, idx++)
            {
                if (idx >= fcount)
                {
                    // wrap around
                    idx = 0;
                }

                // try to atomically claim a range of bits

                if (mi_bitmap_try_find_claim_field(&arena->blocks_inuse.e0, idx, blocks, out bitmap_idx))
                {
                    // start search from here next time
                    mi_atomic_store_release(ref arena->search_idx, idx);
                    return true;
                }
            }

            bitmap_idx = 0;
            return false;
#pragma warning restore CS0420
        }

        /* -----------------------------------------------------------
          Arena Allocation
        ----------------------------------------------------------- */

        private static void* mi_arena_alloc_from(mi_arena_t* arena, [NativeTypeName("size_t")] nuint arena_index, [NativeTypeName("size_t")] nuint needed_bcount, [NativeTypeName("bool*")] ref bool commit, [NativeTypeName("bool*")] ref bool large, [NativeTypeName("bool*")] out bool is_zero, [NativeTypeName("size_t*")] out nuint memid, mi_os_tld_t* tld)
        {
            if (!mi_arena_alloc(arena, needed_bcount, out nuint bitmap_index))
            {
                memid = 0;
                is_zero = false;
                return null;
            }

            // claimed it! set the dirty bits (todo: no need for an atomic op here?)
            void* p = (void*)(arena->start + (mi_bitmap_index_bit(bitmap_index) * MI_ARENA_BLOCK_SIZE));

            memid = mi_arena_id_create(arena_index, bitmap_index);
            is_zero = mi_bitmap_claim(arena->blocks_dirty, arena->field_count, needed_bcount, bitmap_index, out _);
            large = arena->is_large;

            if (arena->is_committed)
            {
                // always committed
                commit = true;
            }
            else if (commit)
            {
                // arena not committed as a whole, but commit requested: ensure commit now
                mi_bitmap_claim(arena->blocks_committed, arena->field_count, needed_bcount, bitmap_index, out bool any_uncommitted);

                if (any_uncommitted)
                {
                    _mi_os_commit(p, needed_bcount * MI_ARENA_BLOCK_SIZE, out bool commit_zero, ref *tld->stats);

                    if (commit_zero)
                    {
                        is_zero = true;
                    }
                }
            }
            else
            {
                // no need to commit, but check if already fully committed
                commit = mi_bitmap_is_claimed(arena->blocks_committed, arena->field_count, needed_bcount, bitmap_index);
            }

            return p;
        }

        private static partial void* _mi_arena_alloc_aligned(nuint size, nuint alignment, ref bool commit, ref bool large, out bool is_zero, out nuint memid, mi_os_tld_t* tld)
        {
            mi_assert_internal((MI_DEBUG > 1) && (tld != null));
            mi_assert_internal((MI_DEBUG > 1) && (size > 0));

            memid = MI_MEMID_OS;
            is_zero = false;

            // try to allocate in an arena if the alignment is small enough
            // and the object is not too large or too small.

            if ((alignment <= MI_SEGMENT_ALIGN) && (size <= MI_ARENA_MAX_OBJ_SIZE) && (size >= MI_ARENA_MIN_OBJ_SIZE))
            {
                nuint bcount = mi_block_count_of_size(size);

                // current numa node
                int numa_node = _mi_os_numa_node(tld);

                mi_assert_internal((MI_DEBUG > 1) && (size <= (bcount * MI_ARENA_BLOCK_SIZE)));

                // try numa affine allocation
                for (nuint i = 0; i < MI_MAX_ARENAS; i++)
                {
                    mi_arena_t* arena = mi_atomic_load_ptr_relaxed<mi_arena_t>(ref mi_arenas[i]);

                    if (arena == null)
                    {
                        // end reached
                        break;
                    }

                    // numa local, large OS pages allowed, or arena is not large OS pages

                    if (((arena->numa_node < 0) || (arena->numa_node == numa_node)) && (large || !arena->is_large))
                    {
                        void* p = mi_arena_alloc_from(arena, i, bcount, ref commit, ref large, out is_zero, out memid, tld);
                        mi_assert_internal((MI_DEBUG > 1) && (((nuint)p % alignment) == 0));

                        if (p != null)
                        {
                            return p;
                        }
                    }
                }

                // try from another numa node instead..

                for (nuint i = 0; i < MI_MAX_ARENAS; i++)
                {
                    mi_arena_t* arena = mi_atomic_load_ptr_relaxed<mi_arena_t>(ref mi_arenas[i]);

                    if (arena == null)
                    {
                        // end reached
                        break;
                    }

                    // not numa local, large OS pages allowed, or arena is not large OS pages
                    if ((arena->numa_node >= 0) && (arena->numa_node != numa_node) && (large || !arena->is_large))
                    {
                        void* p = mi_arena_alloc_from(arena, i, bcount, ref commit, ref large, out is_zero, out memid, tld);
                        mi_assert_internal((MI_DEBUG > 1) && (((nuint)p % alignment) == 0));

                        if (p != null)
                        {
                            return p;
                        }
                    }
                }
            }

            // finally, fall back to the OS

            is_zero = true;
            memid = MI_MEMID_OS;

            return _mi_os_alloc_aligned(size, alignment, commit, ref large, tld);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static partial void* _mi_arena_alloc(nuint size, ref bool commit, ref bool large, out bool is_zero, out nuint memid, mi_os_tld_t* tld)
            => _mi_arena_alloc_aligned(size, MI_ARENA_BLOCK_SIZE, ref commit, ref large, out is_zero, out memid, tld);

        /* -----------------------------------------------------------
          Arena free
        ----------------------------------------------------------- */

        private static partial void _mi_arena_free(void* p, nuint size, nuint memid, bool all_committed, ref mi_stats_t stats)
        {
#pragma warning disable CS0420
            mi_assert_internal((MI_DEBUG > 1) && (size > 0));

            if (p == null)
            {
                return;
            }

            if (size == 0)
            {
                return;
            }

            if (memid == MI_MEMID_OS)
            {
                // was a direct OS allocation, pass through
                _mi_os_free_ex(p, size, all_committed, ref stats);
            }
            else
            {
                // allocated in an arena

                mi_arena_id_indices(memid, out nuint arena_idx, out nuint bitmap_idx);
                mi_assert_internal((MI_DEBUG > 1) && (arena_idx < MI_MAX_ARENAS));

                mi_arena_t* arena = mi_atomic_load_ptr_relaxed<mi_arena_t>(ref mi_arenas[arena_idx]);
                mi_assert_internal((MI_DEBUG > 1) && (arena != null));

                if (arena == null)
                {
                    _mi_error_message(EINVAL, "trying to free from non-existent arena: {0:X}, size {1}, memid: 0x{2:X}\n", (nuint)p, size, memid);
                    return;
                }

                mi_assert_internal((MI_DEBUG > 1) && (arena->field_count > mi_bitmap_index_field(bitmap_idx)));

                if (arena->field_count <= mi_bitmap_index_field(bitmap_idx))
                {
                    _mi_error_message(EINVAL, "trying to free from non-existent arena block: {0}, size {1}, memid: 0x{2:X}\n", (nuint)p, size, memid);
                    return;
                }

                nuint blocks = mi_block_count_of_size(size);
                bool ones = mi_bitmap_unclaim(&arena->blocks_inuse.e0, arena->field_count, blocks, bitmap_idx);

                if (!ones)
                {
                    _mi_error_message(EAGAIN, "trying to free an already freed block: {0:X}, size {1}\n", (nuint)p, size);
                    return;
                };
            }
#pragma warning restore CS0420
        }

        /* -----------------------------------------------------------
          Add an arena.
        ----------------------------------------------------------- */

        private static bool mi_arena_add(mi_arena_t* arena)
        {
#pragma warning disable CS0420
            mi_assert_internal((MI_DEBUG > 1) && (arena != null));
            mi_assert_internal((MI_DEBUG > 1) && (((nuint)mi_atomic_load_ptr_relaxed<byte>(ref arena->start) % MI_SEGMENT_ALIGN) == 0));
            mi_assert_internal((MI_DEBUG > 1) && (arena->block_count > 0));

            nuint i = mi_atomic_increment_acq_rel(ref mi_arena_count);

            if (i >= MI_MAX_ARENAS)
            {
                mi_atomic_decrement_acq_rel(ref mi_arena_count);
                return false;
            }

            mi_atomic_store_ptr_release<mi_arena_t>(ref mi_arenas[i], arena);
            return true;
#pragma warning restore CS0420
        }


        /* -----------------------------------------------------------
          Reserve a huge page arena.
        ----------------------------------------------------------- */

        // reserve at a specific numa node
        public static partial int mi_reserve_huge_os_pages_at(nuint pages, int numa_node, nuint timeout_msecs)
        {
#pragma warning disable CS0420
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
                numa_node = numa_node % (int)_mi_os_numa_node_count();
            }

            void* p = _mi_os_alloc_huge_os_pages(pages, numa_node, (long)timeout_msecs, out nuint pages_reserved, out nuint hsize);

            if ((p == null) || (pages_reserved == 0))
            {
                _mi_warning_message("failed to reserve {0} gb huge pages\n", pages);
                return ENOMEM;
            }

            _mi_verbose_message("numa node {0}: reserved {1} gb huge pages (of the {2} gb requested)\n", numa_node, pages_reserved, pages);

            nuint bcount = mi_block_count_of_size(hsize);
            nuint fields = _mi_divide_up(bcount, MI_BITMAP_FIELD_BITS);
            nuint asize = SizeOf<mi_arena_t>() + (2 * fields * SizeOf<nuint>());

            // TODO: can we avoid allocating from the OS?
            mi_arena_t* arena = (mi_arena_t*)_mi_os_alloc(asize, ref _mi_stats_main);

            if (arena == null)
            {
                _mi_os_free_huge_pages(p, hsize, ref _mi_stats_main);
                return ENOMEM;
            }

            arena->block_count = bcount;
            arena->field_count = fields;

            arena->start = (nuint)p;

            // TODO: or get the current numa node if -1? (now it allows anyone to allocate on -1)
            arena->numa_node = numa_node;

            arena->is_large = true;
            arena->is_zero_init = true;
            arena->is_committed = true;
            arena->search_idx = 0;

            // just after inuse bitmap
            arena->blocks_dirty = &arena->blocks_inuse.e0 + fields;

            arena->blocks_committed = null;

            // the bitmaps are already zero initialized due to os_alloc
            // just claim leftover blocks if needed

            nint post = (nint)((fields * MI_BITMAP_FIELD_BITS) - bcount);
            mi_assert_internal((MI_DEBUG > 1) && (post >= 0));

            if (post > 0)
            {
                // don't use leftover bits at the end
                nuint postidx = mi_bitmap_index_create(fields - 1, MI_BITMAP_FIELD_BITS - (nuint)post);
                mi_bitmap_claim(&arena->blocks_inuse.e0, fields, (nuint)post, postidx, out _);
            }

            mi_arena_add(arena);
            return 0;
#pragma warning restore CS0420
        }

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
}
