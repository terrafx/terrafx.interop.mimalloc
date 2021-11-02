// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the region.c file from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

using System;
using System.Runtime.CompilerServices;
using static TerraFX.Interop.mi_option_t;

namespace TerraFX.Interop
{
    public static unsafe partial class Mimalloc
    {
        /* ----------------------------------------------------------------------------
        This implements a layer between the raw OS memory (VirtualAlloc/mmap/sbrk/..)
        and the segment and huge object allocation by mimalloc. There may be multiple
        implementations of this (one could be the identity going directly to the OS,
        another could be a simple cache etc), but the current one uses large "regions".
        In contrast to the rest of mimalloc, the "regions" are shared between threads and
        need to be accessed using atomic operations.
        We need this memory layer between the raw OS calls because of:
        1. on `sbrk` like systems (like WebAssembly) we need our own memory maps in order
           to reuse memory effectively.
        2. It turns out that for large objects, between 1MiB and 32MiB (?), the cost of
           an OS allocation/free is still (much) too expensive relative to the accesses 
           in that object :-( (`malloc-large` tests this). This means we need a cheaper 
           way to reuse memory.
        3. This layer allows for NUMA aware allocation.

        Possible issues:
        - (2) can potentially be addressed too with a small cache per thread which is much
          simpler. Generally though that requires shrinking of huge pages, and may overuse
          memory per thread. (and is not compatible with `sbrk`).
        - Since the current regions are per-process, we need atomic operations to
          claim blocks which may be contended
        - In the worst case, we need to search the whole region map (16KiB for 256GiB)
          linearly. At what point will direct OS calls be faster? Is there a way to
          do this better without adding too much complexity?
        -----------------------------------------------------------------------------*/

        // Internal raw OS interface

        [return: NativeTypeName("size_t")]
        private static partial nuint _mi_os_large_page_size();

        private static partial bool _mi_os_protect(void* addr, [NativeTypeName("size_t")] nuint size);

        private static partial bool _mi_os_unprotect(void* addr, [NativeTypeName("size_t")] nuint size);

        private static partial bool _mi_os_commit(void* addr, [NativeTypeName("size_t")] nuint size, [NativeTypeName("bool*")] out bool is_zero, [NativeTypeName("mi_stats_t*")] ref mi_stats_t tld_stats);

        private static partial bool _mi_os_reset(void* addr, [NativeTypeName("size_t")] nuint size, [NativeTypeName("mi_stats_t*")] ref mi_stats_t tld_stats);

        private static partial bool _mi_os_unreset(void* addr, [NativeTypeName("size_t")] nuint size, [NativeTypeName("bool*")] out bool is_zero, [NativeTypeName("mi_stats_t*")] ref mi_stats_t tld_stats);

        // arena.c
        private static partial void _mi_arena_free(void* p, [NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint memid, bool all_committed, [NativeTypeName("mi_stats_t*")] ref mi_stats_t stats);

        private static partial void* _mi_arena_alloc([NativeTypeName("size_t")] nuint size, [NativeTypeName("bool*")] ref bool commit, [NativeTypeName("bool*")] ref bool large, [NativeTypeName("bool*")] out bool is_zero, [NativeTypeName("size_t*")] out nuint memid, mi_os_tld_t* tld);

        private static partial void* _mi_arena_alloc_aligned([NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment, [NativeTypeName("bool*")] ref bool commit, [NativeTypeName("bool*")] ref bool large, [NativeTypeName("bool*")] out bool is_zero, [NativeTypeName("size_t*")] out nuint memid, mi_os_tld_t* tld);

        /* ----------------------------------------------------------------------------
        Utility functions
        -----------------------------------------------------------------------------*/

        // Blocks (of 4MiB) needed for the given size.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [return: NativeTypeName("size_t")]
        private static nuint mi_region_block_count([NativeTypeName("size_t")] nuint size) => _mi_divide_up(size, MI_SEGMENT_SIZE);

        // Return a rounded commit/reset size such that we don't fragment large OS pages into small ones.
        [return: NativeTypeName("size_t")]
        private static nuint mi_good_commit_size([NativeTypeName("size_t")] nuint size)
        {
            if (size > (SIZE_MAX - _mi_os_large_page_size()))
            {
                return size;
            }
            return _mi_align_up(size, _mi_os_large_page_size());
        }

        // Return if a pointer points into a region reserved by us.
        public static partial bool mi_is_in_heap_region(void* p)
        {
#pragma warning disable CS0420
            if (p == null)
            {
                return false;
            }

            nuint count = mi_atomic_load_relaxed(ref regions_count);

            for (nuint i = 0; i < count; i++)
            {
                byte* start = (byte*)mi_atomic_load_ptr_relaxed<byte>(ref regions[i].start);

                if ((start != null) && ((byte*)p >= start) && ((byte*)p < (start + MI_REGION_SIZE)))
                {
                    return true;
                }
            }

            return false;
#pragma warning restore CS0420
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void* mi_region_blocks_start([NativeTypeName("const mem_region_t*")] mem_region_t* region, [NativeTypeName("mi_bitmap_index_t")] nuint bit_idx)
        {
#pragma warning disable CS0420
            byte* start = mi_atomic_load_ptr_acquire<byte>(ref region->start);
            mi_assert_internal((MI_DEBUG > 1) && (start != null));
            return start + (bit_idx * MI_SEGMENT_SIZE);
#pragma warning restore CS0420
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [return: NativeTypeName("size_t")]
        private static nuint mi_memid_create(mem_region_t* region, [NativeTypeName("mi_bitmap_index_t")] nuint bit_idx)
        {
            mi_assert_internal((MI_DEBUG > 1) && (bit_idx < MI_BITMAP_FIELD_BITS));
            nuint idx = (nuint)(region - regions);

            mi_assert_internal((MI_DEBUG > 1) && (&regions[idx] == region));
            return ((idx * MI_BITMAP_FIELD_BITS) + bit_idx) << 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [return: NativeTypeName("size_t")]
        private static nuint mi_memid_create_from_arena([NativeTypeName("size_t")] nuint arena_memid) => (arena_memid << 1) | 1;

        private static bool mi_memid_is_arena([NativeTypeName("size_t")] nuint id, out mem_region_t* region, [NativeTypeName("mi_bitmap_index_t*")] out nuint bit_idx, [NativeTypeName("size_t*")] out nuint arena_memid)
        {
            if ((id & 1) == 1)
            {
                region = default;
                bit_idx = 0;

                arena_memid = id >> 1;
                return true;
            }
            else
            {
                arena_memid = 0;

                nuint idx = (id >> 1) / MI_BITMAP_FIELD_BITS;
                bit_idx = (id >> 1) % MI_BITMAP_FIELD_BITS;

                region = &regions[idx];
                return false;
            }
        }

        /* ----------------------------------------------------------------------------
          Allocate a region is allocated from the OS (or an arena)
        -----------------------------------------------------------------------------*/

        private static bool mi_region_try_alloc_os([NativeTypeName("size_t")] nuint blocks, bool commit, bool allow_large, out mem_region_t* region, [NativeTypeName("mi_bitmap_index_t*")] out nuint bit_idx, mi_os_tld_t* tld)
        {
#pragma warning disable CS0420
            // not out of regions yet?
            if (mi_atomic_load_relaxed(ref regions_count) >= (MI_REGION_MAX - 1))
            {
                region = default;
                bit_idx = 0;
                return false;
            }

            // try to allocate a fresh region from the OS

            bool region_commit = commit && mi_option_is_enabled(mi_option_eager_region_commit);
            bool region_large = commit && allow_large;

            void* start = _mi_arena_alloc_aligned(MI_REGION_SIZE, MI_SEGMENT_ALIGN, ref region_commit, ref region_large, out bool is_zero, out nuint arena_memid, tld);

            if (start == null)
            {
                region = default;
                bit_idx = 0;
                return false;
            }

            mi_assert_internal((MI_DEBUG > 1) && (!(region_large && !allow_large)));
            mi_assert_internal((MI_DEBUG > 1) && (!region_large || region_commit));

            // claim a fresh slot
            nuint idx = mi_atomic_increment_acq_rel(ref regions_count);

            if (idx >= MI_REGION_MAX)
            {
                _ = mi_atomic_decrement_acq_rel(ref regions_count);

                _mi_arena_free(start, MI_REGION_SIZE, arena_memid, region_commit, ref *tld->stats);
                _mi_warning_message("maximum regions used: {0} GiB (perhaps recompile with a larger setting for MI_HEAP_REGION_MAX_SIZE)", _mi_divide_up(MI_HEAP_REGION_MAX_SIZE, GiB));

                region = default;
                bit_idx = 0;

                return false;
            }

            // allocated, initialize and claim the initial blocks

            mem_region_t* r = &regions[idx];
            r->arena_memid = arena_memid;

            mi_atomic_store_release(ref r->in_use, 0);
            mi_atomic_store_release(ref r->dirty, is_zero ? 0 : MI_BITMAP_FIELD_FULL);
            mi_atomic_store_release(ref r->commit, region_commit ? MI_BITMAP_FIELD_FULL : 0);
            mi_atomic_store_release(ref r->reset, 0);

            bit_idx = 0;

            _ = mi_bitmap_claim(&r->in_use, 1, blocks, bit_idx, out _);
            mi_atomic_store_ptr_release(ref r->start, start);

            // and share it

            mi_region_info_t info;

            // initialize the full union to zero
            info.value = 0;

            info.x.valid = true;
            info.x.is_large = region_large;
            info.x.numa_node = (short)_mi_os_numa_node(tld);

            // now make it available to others
            mi_atomic_store_release(ref r->info, info.value);
            region = r;

            return true;
#pragma warning restore CS0420
        }

        /* ----------------------------------------------------------------------------
          Try to claim blocks in suitable regions
        -----------------------------------------------------------------------------*/

        private static bool mi_region_is_suitable([NativeTypeName("const mem_region_t*")] mem_region_t* region, int numa_node, bool allow_large)
        {
#pragma warning disable CS0420
            // initialized at all?

            Unsafe.SkipInit(out mi_region_info_t info);
            info.value = mi_atomic_load_relaxed(ref region->info);

            if (info.value == 0)
            {
                return false;
            }

            if (numa_node >= 0)
            {
                // numa correct
                // use negative numa node to always succeed

                int rnode = info.x.numa_node;

                if ((rnode >= 0) && (rnode != numa_node))
                {
                    return false;
                }
            }

            // check allow-large

            if (!allow_large && info.x.is_large)
            {
                return false;
            }

            return true;
#pragma warning restore CS0420
        }

        private static bool mi_region_try_claim(int numa_node, [NativeTypeName("size_t")] nuint blocks, bool allow_large, out mem_region_t* region, [NativeTypeName("mi_bitmap_index_t*")] out nuint bit_idx, mi_os_tld_t* tld)
        {
#pragma warning disable CS0420
            // try all regions for a free slot

            // monotonic, so ok to be relaxed
            nuint count = mi_atomic_load_relaxed(ref regions_count);

            // Or start at 0 to reuse low addresses? Starting at 0 seems to increase latency though
            nuint idx = tld->region_idx;

            for (nuint visited = 0; visited < count; visited++, idx++)
            {
                if (idx >= count)
                {
                    // wrap around
                    idx = 0;
                }

                mem_region_t* r = &regions[idx];

                if (mi_region_is_suitable(r, numa_node, allow_large))
                {
                    // if this region suits our demand (numa node matches, large OS page matches)
                    // then try to atomically claim a segment(s) in this region

                    if (mi_bitmap_try_find_claim_field(&r->in_use, 0, blocks, out bit_idx))
                    {
                        // remember the last found position
                        tld->region_idx = idx;

                        region = r;
                        return true;
                    }
                }
            }

            region = default;
            bit_idx = 0;
            return false;
#pragma warning restore CS0420
        }

        private static void* mi_region_try_alloc([NativeTypeName("size_t")] nuint blocks, [NativeTypeName("bool*")] ref bool commit, [NativeTypeName("bool*")] ref bool is_large, [NativeTypeName("bool*")] out bool is_zero, [NativeTypeName("size_t*")] out nuint memid, mi_os_tld_t* tld)
        {
#pragma warning disable CS0420
            mi_assert_internal((MI_DEBUG > 1) && (blocks <= MI_BITMAP_FIELD_BITS));
            int numa_node = _mi_os_numa_node_count() <= 1 ? -1 : _mi_os_numa_node(tld);

            // try to claim in existing regions
            if (!mi_region_try_claim(numa_node, blocks, is_large, out mem_region_t* region, out nuint bit_idx, tld))
            {
                // otherwise try to allocate a fresh region and claim in there
                if (!mi_region_try_alloc_os(blocks, commit, is_large, out region, out bit_idx, tld))
                {
                    // out of regions or memory

                    is_zero = false;
                    memid = 0;

                    return null;
                }
            }

            // ------------------------------------------------
            // found a region and claimed `blocks` at `bit_idx`, initialize them now

            mi_assert_internal((MI_DEBUG > 1) && (region != null));
            mi_assert_internal((MI_DEBUG > 1) && mi_bitmap_is_claimed(&region->in_use, 1, blocks, bit_idx));

            Unsafe.SkipInit(out mi_region_info_t info);
            info.value = mi_atomic_load_acquire(ref region->info);

            byte* start = (byte*)mi_atomic_load_ptr_acquire<byte>(ref region->start);

            mi_assert_internal((MI_DEBUG > 1) && (!(info.x.is_large && !is_large)));
            mi_assert_internal((MI_DEBUG > 1) && (start != null));

            is_zero = mi_bitmap_claim(&region->dirty, 1, blocks, bit_idx, out _);
            is_large = info.x.is_large;
            memid = mi_memid_create(region, bit_idx);

            void* p = start + (mi_bitmap_index_bit_in_field(bit_idx) * MI_SEGMENT_SIZE);

            if (commit)
            {
                // ensure commit
                _ = mi_bitmap_claim(&region->commit, 1, blocks, bit_idx, out bool any_uncommitted);

                if (any_uncommitted)
                {
                    mi_assert_internal((MI_DEBUG > 1) && (!info.x.is_large));

                    if (!_mi_mem_commit(p, blocks * MI_SEGMENT_SIZE, out bool commit_zero, tld))
                    {
                        // failed to commit! unclaim and return
                        _ = mi_bitmap_unclaim(&region->in_use, 1, blocks, bit_idx);
                        return null;
                    }

                    if (commit_zero)
                    {
                        is_zero = true;
                    }
                }
            }
            else
            {
                // no need to commit, but check if already fully committed
                commit = mi_bitmap_is_claimed(&region->commit, 1, blocks, bit_idx);
            }

            mi_assert_internal((MI_DEBUG > 1) && (!commit || mi_bitmap_is_claimed(&region->commit, 1, blocks, bit_idx)));

            // unreset reset blocks
            if (mi_bitmap_is_any_claimed(&region->reset, 1, blocks, bit_idx))
            {
                // some blocks are still reset

                mi_assert_internal((MI_DEBUG > 1) && (!info.x.is_large));
                mi_assert_internal((MI_DEBUG > 1) && (!mi_option_is_enabled(mi_option_eager_commit) || commit || (mi_option_get(mi_option_eager_commit_delay) > 0)));

                _ = mi_bitmap_unclaim(&region->reset, 1, blocks, bit_idx);

                if (commit || !mi_option_is_enabled(mi_option_reset_decommits))
                {
                    // only if needed
                    _ = _mi_mem_unreset(p, blocks * MI_SEGMENT_SIZE, out bool reset_zero, tld);

                    if (reset_zero)
                    {
                        is_zero = true;
                    }
                }
            }

            mi_assert_internal((MI_DEBUG > 1) && (!mi_bitmap_is_any_claimed(&region->reset, 1, blocks, bit_idx)));

            if ((MI_DEBUG >= 2) && commit)
            {
                ((byte*)p)[0] = 0;
            }

            // and return the allocation  
            mi_assert_internal((MI_DEBUG > 1) && (p != null));

            return p;
#pragma warning restore CS0420
        }

        /* ----------------------------------------------------------------------------
         Allocation
        -----------------------------------------------------------------------------*/

        // Allocate `size` memory aligned at `alignment`. Return non null on success, with a given memory `id`.
        // (`id` is abstract, but `id = idx*MI_REGION_MAP_BITS + bitidx`)
        private static partial void* _mi_mem_alloc_aligned(nuint size, nuint alignment, ref bool commit, ref bool large, out bool is_zero, out nuint memid, mi_os_tld_t* tld)
        {
            mi_assert_internal((MI_DEBUG > 1) && (tld != null));
            mi_assert_internal((MI_DEBUG > 1) && (size > 0));

            memid = 0;
            is_zero = false;

            if (size == 0)
            {
                return null;
            }

            size = _mi_align_up(size, _mi_os_page_size());

            // allocate from regions if possible

            void* p = null;
            nuint blocks = mi_region_block_count(size);

            if ((blocks <= MI_REGION_MAX_OBJ_BLOCKS) && (alignment <= MI_SEGMENT_ALIGN))
            {
                p = mi_region_try_alloc(blocks, ref commit, ref large, out is_zero, out memid, tld);

                if (p == null)
                {
                    _mi_warning_message("unable to allocate from region: size {0}\n", size);
                }
            }

            if (p == null)
            {
                // and otherwise fall back to the OS
                p = _mi_arena_alloc_aligned(size, alignment, ref commit, ref large, out is_zero, out nuint arena_memid, tld);
                memid = mi_memid_create_from_arena(arena_memid);
            }

            if (p != null)
            {
                mi_assert_internal((MI_DEBUG > 1) && (((nuint)p % alignment) == 0));

                if ((MI_DEBUG >= 2) && commit)
                {
                    // ensure the memory is committed
                    ((byte*)p)[0] = 0;
                }
            }

            return p;
        }

        /* ----------------------------------------------------------------------------
        Free
        -----------------------------------------------------------------------------*/

        // Free previously allocated memory with a given id.
        private static partial void _mi_mem_free(void* p, nuint size, nuint id, bool full_commit, bool any_reset, mi_os_tld_t* tld)
        {
#pragma warning disable CS0420
            mi_assert_internal((MI_DEBUG > 1) && (size > 0) && (tld != null));

            if (p == null)
            {
                return;
            }

            if (size == 0)
            {
                return;
            }

            size = _mi_align_up(size, _mi_os_page_size());

            if (mi_memid_is_arena(id, out mem_region_t* region, out nuint bit_idx, out nuint arena_memid))
            {
                // was a direct arena allocation, pass through
                _mi_arena_free(p, size, arena_memid, full_commit, ref *tld->stats);
            }
            else
            {
                // allocated in a region
                mi_assert_internal((MI_DEBUG > 1) && (size <= MI_REGION_MAX_OBJ_SIZE));

                if (size > MI_REGION_MAX_OBJ_SIZE)
                {
                    return;
                }

                nuint blocks = mi_region_block_count(size);
                mi_assert_internal((MI_DEBUG > 1) && (blocks + bit_idx <= MI_BITMAP_FIELD_BITS));

                Unsafe.SkipInit(out mi_region_info_t info);
                info.value = mi_atomic_load_acquire(ref region->info);

                mi_assert_internal((MI_DEBUG > 1) && (info.value != 0));
                void* blocks_start = mi_region_blocks_start(region, bit_idx);

                // not a pointer in our area?
                mi_assert_internal((MI_DEBUG > 1) && (blocks_start == p));
                mi_assert_internal((MI_DEBUG > 1) && (bit_idx + blocks <= MI_BITMAP_FIELD_BITS));

                if ((blocks_start != p) || ((bit_idx + blocks) > MI_BITMAP_FIELD_BITS))
                {
                    // or `abort`?
                    return;
                }

                // committed?
                if (full_commit && ((size % MI_SEGMENT_SIZE) == 0))
                {
                    _ = mi_bitmap_claim(&region->commit, 1, blocks, bit_idx, out _);
                }

                if (any_reset)
                {
                    // set the is_reset bits if any pages were reset
                    _ = mi_bitmap_claim(&region->reset, 1, blocks, bit_idx, out _);
                }

                // reset the blocks to reduce the working set.
                // cannot reset halfway committed segments, use only `option_page_reset` instead

                if (!info.x.is_large && mi_option_is_enabled(mi_option_segment_reset) && (mi_option_is_enabled(mi_option_eager_commit) || mi_option_is_enabled(mi_option_reset_decommits)))
                {
                    _ = mi_bitmap_claim(&region->reset, 1, blocks, bit_idx, out bool any_unreset);

                    if (any_unreset)
                    {
                        // ensure no more pending write (in case reset = decommit)
                        _mi_abandoned_await_readers();
                        _ = _mi_mem_reset(p, blocks * MI_SEGMENT_SIZE, tld);
                    }
                }

                // and unclaim
                bool all_unclaimed = mi_bitmap_unclaim(&region->in_use, 1, blocks, bit_idx);

                mi_assert_internal((MI_DEBUG > 1) && all_unclaimed);
            }
#pragma warning restore CS0420
        }

        /* ----------------------------------------------------------------------------
          collection
        -----------------------------------------------------------------------------*/
        private static partial void _mi_mem_collect(mi_os_tld_t* tld)
        {
#pragma warning disable CS0420
            // free every region that has no segments in use.
            nuint rcount = mi_atomic_load_relaxed(ref regions_count);

            for (nuint i = 0; i < rcount; i++)
            {
                mem_region_t* region = &regions[i];

                if (mi_atomic_load_relaxed(ref region->info) != 0)
                {
                    // if no segments used, try to claim the whole region
                    nuint m = mi_atomic_load_relaxed(ref region->in_use);

                    while ((m == 0) && !mi_atomic_cas_weak_release(ref region->in_use, ref m, MI_BITMAP_FIELD_FULL))
                    {
                        /* nothing */
                    }

                    if (m == 0)
                    {
                        // on success, free the whole region
                        byte* start = mi_atomic_load_ptr_acquire<byte>(ref regions[i].start);

                        nuint arena_memid = mi_atomic_load_relaxed(ref regions[i].arena_memid);
                        nuint commit = mi_atomic_load_relaxed(ref regions[i].commit);

                        regions[i] = default;

                        // and release the whole region
                        mi_atomic_store_release(ref region->info, 0);

                        if (start != null)
                        {
                            // ensure no pending reads
                            _mi_abandoned_await_readers();
                            _mi_arena_free(start, MI_REGION_SIZE, arena_memid, ~commit == 0, ref *tld->stats);
                        }
                    }
                }
            }
#pragma warning restore CS0420
        }

        /* ----------------------------------------------------------------------------
          Other
        -----------------------------------------------------------------------------*/

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static partial bool _mi_mem_reset(void* p, nuint size, mi_os_tld_t* tld) => _mi_os_reset(p, size, ref *tld->stats);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static partial bool _mi_mem_unreset(void* p, nuint size, out bool is_zero, mi_os_tld_t* tld) => _mi_os_unreset(p, size, out is_zero, ref *tld->stats);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static partial bool _mi_mem_commit(void* p, nuint size, out bool is_zero, mi_os_tld_t* tld) => _mi_os_commit(p, size, out is_zero, ref *tld->stats);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool _mi_mem_decommit(void* p, [NativeTypeName("size_t")] nuint size, mi_os_tld_t* tld) => _mi_os_decommit(p, size, ref *tld->stats);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static partial bool _mi_mem_protect(void* p, nuint size) => _mi_os_protect(p, size);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static partial bool _mi_mem_unprotect(void* p, nuint size) => _mi_os_unprotect(p, size);
    }
}
