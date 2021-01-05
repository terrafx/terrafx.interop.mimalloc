// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the os.c file from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

using System;
using static TerraFX.Interop.mi_option_t;
using static TerraFX.Interop.Mimalloc.MEM_EXTENDED_PARAMETER_TYPE;

namespace TerraFX.Interop
{
    public static unsafe partial class Mimalloc
    {
        /* -----------------------------------------------------------
          Initialization.
          On windows initializes support for aligned allocation and
          large OS pages (if MIMALLOC_LARGE_OS_PAGES is true).
        ----------------------------------------------------------- */

        private static partial bool _mi_os_decommit(void* addr, [NativeTypeName("size_t")] nuint size, [NativeTypeName("mi_stats_t*")] ref mi_stats_t stats);

        private static void* mi_align_up_ptr(void* p, [NativeTypeName("size_t")] nuint alignment) => (void*)_mi_align_up((nuint)p, alignment);

        [return: NativeTypeName("uintptr_t")]
        private static nuint _mi_align_down([NativeTypeName("uintptr_t")] nuint sz, [NativeTypeName("size_t")] nuint alignment) => sz / alignment * alignment;

        private static void* mi_align_down_ptr(void* p, [NativeTypeName("size_t")] nuint alignment) => (void*)_mi_align_down((nuint)p, alignment);

        // OS (small) page size
        private static partial nuint _mi_os_page_size() => os_page_size;

        // if large OS pages are supported (2 or 4MiB), then return the size, otherwise return the small page size (4KiB)
        private static partial nuint _mi_os_large_page_size() => (large_os_page_size != 0) ? large_os_page_size : _mi_os_page_size();

        private static bool use_large_os_page([NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment)
        {
            // if we have access, check the size and alignment requirements

            if ((large_os_page_size == 0) || !mi_option_is_enabled(mi_option_large_os_pages))
            {
                return false;
            }

            return ((size % large_os_page_size) == 0) && ((alignment % large_os_page_size) == 0);
        }

        // round to a good OS allocation size (bounded by max 12.5% waste)
        private static partial nuint _mi_os_good_alloc_size(nuint size)
        {
            nuint align_size;

            if (size < 512 * KiB)
            {
                align_size = _mi_os_page_size();
            }
            else if (size < 2 * MiB)
            {
                align_size = 64 * KiB;
            }
            else if (size < 8 * MiB)
            {
                align_size = 256 * KiB;
            }
            else if (size < 32 * MiB)
            {
                align_size = 1 * MiB;
            }
            else
            {
                align_size = 4 * MiB;
            }

            if (size >= (SIZE_MAX - align_size))
            {
                // possible overflow?
                return size;
            }

            return _mi_align_up(size, align_size);
        }

        // The following members have not been ported as they aren't needed for .NET:
        //  * bool mi_win_enable_large_os_pages()
        //  * void _mi_os_init()

        /* -----------------------------------------------------------
          Raw allocation on Windows (VirtualAlloc) and Unix's (mmap).
        ----------------------------------------------------------- */

        private static bool mi_os_mem_free(void* addr, [NativeTypeName("size_t")] nuint size, bool was_committed, [NativeTypeName("mi_stats_t*")] ref mi_stats_t stats)
        {
            if ((addr == null) || (size == 0))
            {
                return true;
            }

            bool err = false;

            if (IsWindows)
            {
                err = VirtualFree(addr, 0, MEM_RELEASE) == 0;
            }
            else
            {
                err = munmap(addr, size) == -1;
            }

            if (was_committed)
            {
                _mi_stat_decrease(ref stats.committed, size);
            }

            _mi_stat_decrease(ref stats.reserved, size);

            if (err)
            {
                _mi_warning_message("munmap failed: {0}, addr 0x{1,-8:X}, size {2}\n", errno, (nuint)addr, size);
                return false;
            }
            else
            {
                return true;
            }
        }

        private static partial void* mi_os_get_aligned_hint([NativeTypeName("size_t")] nuint try_alignment, [NativeTypeName("size_t")] nuint size);

        private static void* mi_win_virtual_allocx(void* addr, [NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint try_alignment, [NativeTypeName("DWORD")] uint flags)
        {
            mi_assert_internal((MI_DEBUG > 1) && IsWindows);

            if (Environment.Is64BitProcess)
            {
                // on 64-bit systems, try to use the virtual address area after 4TiB for 4MiB aligned allocations
                void* hint;

                if ((addr == null) && (hint = mi_os_get_aligned_hint(try_alignment, size)) != null)
                {
                    void* p = VirtualAlloc(hint, size, flags, PAGE_READWRITE);

                    if (p != null)
                    {
                        return p;
                    }

                    uint err = GetLastError();

                    // If linked with multiple instances, we may have tried to allocate at an already allocated area (#210)
                    if ((err != ERROR_INVALID_ADDRESS) && (err != ERROR_INVALID_PARAMETER))
                    {
                        // Windows7 instability (#230)
                        return null;
                    }

                    // fall through
                }
            }

            if (IsWindows10OrLater)
            {
                // on modern Windows try use VirtualAlloc2 for aligned allocation
                if ((try_alignment > 0) && ((try_alignment % _mi_os_page_size()) == 0) && (VirtualAlloc2 != null))
                {
                    MEM_ADDRESS_REQUIREMENTS reqs = default;
                    reqs.Alignment = try_alignment;

                    MEM_EXTENDED_PARAMETER param = default;
                    param.Anonymous1.Type = (ulong)MemExtendedParameterAddressRequirements;
                    param.Anonymous2.Pointer = &reqs;

                    return VirtualAlloc2(GetCurrentProcess(), addr, size, flags, PAGE_READWRITE, &param, 1);
                }
            }

            // last resort
            return VirtualAlloc(addr, size, flags, PAGE_READWRITE);
        }

        private static void* mi_win_virtual_alloc(void* addr, [NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint try_alignment, [NativeTypeName("DWORD")] uint flags, bool large_only, bool allow_large, [NativeTypeName("bool*")] out bool is_large)
        {
#pragma warning disable CS0420
            mi_assert_internal((MI_DEBUG > 1) && IsWindows);
            mi_assert_internal((MI_DEBUG > 1) && (!(large_only && !allow_large)));

            void* p = null;
            is_large = false;

            if ((large_only || use_large_os_page(size, try_alignment)) && allow_large && (flags & MEM_COMMIT) != 0 && (flags & MEM_RESERVE) != 0)
            {
                nuint try_ok = mi_atomic_load_acquire(ref large_page_try_ok);

                if (!large_only && (try_ok > 0))
                {
                    // if a large page allocation fails, it seems the calls to VirtualAlloc get very expensive.
                    // therefore, once a large page allocation failed, we don't try again for `large_page_try_ok` times.
                    mi_atomic_cas_strong_acq_rel(ref large_page_try_ok, ref try_ok, try_ok - 1);
                }
                else
                {
                    // large OS pages must always reserve and commit.
                    is_large = true;

                    p = mi_win_virtual_allocx(addr, size, try_alignment, flags | MEM_LARGE_PAGES);

                    if (large_only)
                    {
                        return p;
                    }

                    // fall back to non-large page allocation on error (`p == null`).
                    if (p == null)
                    {
                        // on error, don't try again for the next N allocations
                        mi_atomic_store_release(ref large_page_try_ok, 10u);
                    }
                }
            }

            if (p == null)
            {
                is_large = (flags & MEM_LARGE_PAGES) != 0;
                p = mi_win_virtual_allocx(addr, size, try_alignment, flags);
            }

            if (p == null)
            {
                _mi_warning_message("unable to allocate OS memory ({0} bytes, error code: {1}, address: {2:X}, large only: {3}, allow large: {4})\n", size, GetLastError(), (nuint)addr, large_only, allow_large);
            }

            return p;
#pragma warning restore CS0420
        }

        private static void* mi_unix_mmapx(void* addr, [NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint try_alignment, int protect_flags, int flags, int fd)
        {
            mi_assert_internal((MI_DEBUG > 1) && IsUnix);
            void* p = null;

            if (Environment.Is64BitProcess)
            {
                // on 64-bit systems, use the virtual address area after 4TiB for 4MiB aligned allocations
                void* hint;

                if ((addr == null) && ((hint = mi_os_get_aligned_hint(try_alignment, size)) != null))
                {
                    p = mmap(hint, size, protect_flags, flags, fd, 0);

                    if (p == MAP_FAILED)
                    {
                        // fall back to regular mmap
                        p = null;
                    }
                }
            }

            if (p == null)
            {
                p = mmap(addr, size, protect_flags, flags, fd, 0);

                if (p == MAP_FAILED)
                {
                    p = null;
                }
            }

            return p;
        }

        private static void* mi_unix_mmap(void* addr, [NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint try_alignment, int protect_flags, bool large_only, bool allow_large, [NativeTypeName("bool*")] out bool is_large)
        {
#pragma warning disable CS0420
            mi_assert_internal((MI_DEBUG > 1) && IsUnix);

            void* p = null;
            is_large = false;

            int flags = MAP_PRIVATE | MAP_ANONYMOUS | MAP_NORESERVE;
            int fd = -1;

            if (IsMacOS)
            {
                // macOS: tracking anonymous page with a specific ID. (All up to 98 are taken officially but LLVM sanitizers had taken 99)
                int os_tag = (int)mi_option_get(mi_option_os_tag);

                if ((os_tag < 100) || (os_tag > 255))
                {
                    os_tag = 100;
                }
                fd = VM_MAKE_TAG(os_tag);
            }

            if ((large_only || use_large_os_page(size, try_alignment)) && allow_large)
            {
                nuint try_ok = mi_atomic_load_acquire(ref large_page_try_ok);

                if (!large_only && (try_ok > 0))
                {
                    // If the OS is not configured for large OS pages, or the user does not have
                    // enough permission, the `mmap` will always fail (but it might also fail for other reasons).
                    // Therefore, once a large page allocation failed, we don't try again for `large_page_try_ok` times
                    // to avoid too many failing calls to mmap.
                    mi_atomic_cas_strong_acq_rel(ref large_page_try_ok, ref try_ok, try_ok - 1);
                }
                else
                {
                    // using NORESERVE on huge pages seems to fail on Linux

                    int lflags = flags & ~MAP_NORESERVE;
                    int lfd = fd;

                    lflags |= MAP_HUGETLB;

                    if (((size % GiB) == 0) && mi_huge_pages_available)
                    {
                        lflags |= MAP_HUGE_1GB;
                    }
                    else
                    {
                        lflags |= MAP_HUGE_2MB;
                    }

                    lfd |= VM_FLAGS_SUPERPAGE_SIZE_2MB;

                    if (large_only || (lflags != flags))
                    {
                        // try large OS page allocation
                        is_large = true;

                        p = mi_unix_mmapx(addr, size, try_alignment, protect_flags, lflags, lfd);
                        
                        if ((p == null) && ((lflags & MAP_HUGE_1GB) != 0))
                        {
                            // don't try huge 1GiB pages again
                            mi_huge_pages_available = false;

                            _mi_warning_message("unable to allocate huge (1GiB) page, trying large (2MiB) pages instead (error {0})\n", errno);

                            lflags = (lflags & ~MAP_HUGE_1GB) | MAP_HUGE_2MB;
                            p = mi_unix_mmapx(addr, size, try_alignment, protect_flags, lflags, lfd);
                        }

                        if (large_only)
                        {
                            return p;
                        }

                        if (p == null)
                        {
                            // on error, don't try again for the next N allocations
                            mi_atomic_store_release(ref large_page_try_ok, 10u);
                        }
                    }
                }
            }

            if (p == null)
            {
                is_large = false;
                p = mi_unix_mmapx(addr, size, try_alignment, protect_flags, flags, fd);

                if (MADV_HUGEPAGE != 0)
                {
                    // Many Linux systems don't allow MAP_HUGETLB but they support instead
                    // transparent huge pages (THP). It is not required to call `madvise` with MADV_HUGE
                    // though since properly aligned allocations will already use large pages if available
                    // in that case -- in particular for our large regions (in `memory.c`).
                    // However, some systems only allow THP if called with explicit `madvise`, so
                    // when large OS pages are enabled for mimalloc, we call `madvice` anyways.

                    if (allow_large && use_large_os_page(size, try_alignment))
                    {
                        if (posix_madvise(p, size, MADV_HUGEPAGE) == 0)
                        {
                            // possibly
                            is_large = true;
                        };
                    }
                }
            }

            if (p == null)
            {
                _mi_warning_message("unable to allocate OS memory ({0} bytes, error code: {1}, address: {2:X}, large only: {3}, allow large: {4})\n", size, errno, (nuint)addr, large_only, allow_large);
            }

            return p;
#pragma warning restore CS0420
        }

        [NativeTypeName("std::atomic<uintptr_t>")]
        private static volatile nuint aligned_base;

        private static partial void* mi_os_get_aligned_hint(nuint try_alignment, nuint size)
        {
#pragma warning disable CS0420
            // On 64-bit systems, we can do efficient aligned allocation by using
            // the 4TiB to 30TiB area to allocate them.

            if (Environment.Is64BitProcess)
            {
                // Return a 4MiB aligned address that is probably available

                if ((try_alignment == 0) || (try_alignment > MI_SEGMENT_SIZE))
                {
                    return null;
                }

                if ((size % MI_SEGMENT_SIZE) != 0)
                {
                    return null;
                }

                nuint hint = mi_atomic_add_acq_rel(ref aligned_base, size);

                if ((hint == 0) || (hint > ((nuint)30 << 40)))
                {
                    // try to wrap around after 30TiB (area after 32TiB is used for huge OS pages)

                    // start at 4TiB area
                    nuint init = (nuint)4 << 40;

                    if ((MI_SECURE > 0) || (MI_DEBUG == 0))
                    {
                        // security: randomize start of aligned allocations unless in debug mode
                        nuint r = _mi_heap_random_next(mi_get_default_heap());

                        // (randomly 20 bits)*4MiB == 0 to 4TiB
                        init = init + (MI_SEGMENT_SIZE * ((r >> 17) & 0xFFFFF));
                    }

                    nuint expected = hint + size;
                    mi_atomic_cas_strong_acq_rel(ref aligned_base, ref expected, init);

                    // this may still give 0 or > 30TiB but that is ok, it is a hint after all
                    hint = mi_atomic_add_acq_rel(ref aligned_base, size);
                }

                if ((hint % try_alignment) != 0)
                {
                    return null;
                }

                return (void*)hint;
            }

            return null;
#pragma warning restore CS0420
        }

        // Primitive allocation from the OS.
        // Note: the `try_alignment` is just a hint and the returned pointer is not guaranteed to be aligned.
        private static void* mi_os_mem_alloc([NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint try_alignment, bool commit, bool allow_large, [NativeTypeName("bool*")] out bool is_large, [NativeTypeName("mi_stats_t*")] ref mi_stats_t stats)
        {
            mi_assert_internal((MI_DEBUG > 1) && size > 0 && (size % _mi_os_page_size()) == 0);

            if (size == 0)
            {
                is_large = false;
                return null;
            }

            if (!commit)
            {
                allow_large = false;
            }

            void* p = null;

            if (IsWindows)
            {
                uint flags = MEM_RESERVE;

                if (commit)
                {
                    flags |= MEM_COMMIT;
                }

                p = mi_win_virtual_alloc(null, size, try_alignment, flags, false, allow_large, out is_large);
            }
            else
            {
                int protect_flags = commit ? (PROT_WRITE | PROT_READ) : PROT_NONE;
                p = mi_unix_mmap(null, size, try_alignment, protect_flags, false, allow_large, out is_large);
            }

            mi_stat_counter_increase(ref stats.mmap_calls, 1);

            if (p != null)
            {
                _mi_stat_increase(ref stats.reserved, size);

                if (commit)
                {
                    _mi_stat_increase(ref stats.committed, size);
                }
            }
            return p;
        }

        // Primitive aligned allocation from the OS.
        // This function guarantees the allocated memory is aligned.
        private static void* mi_os_mem_alloc_aligned([NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment, bool commit, bool allow_large, [NativeTypeName("bool*")] out bool is_large, [NativeTypeName("mi_stats_t*")] ref mi_stats_t stats)
        {
            mi_assert_internal((MI_DEBUG > 1) && (alignment >= _mi_os_page_size()) && ((alignment & (alignment - 1)) == 0));
            mi_assert_internal((MI_DEBUG > 1) && (size > 0) && ((size % _mi_os_page_size()) == 0));

            if (!commit)
            {
                allow_large = false;
            }

            if (!((alignment >= _mi_os_page_size()) && ((alignment & (alignment - 1)) == 0)))
            {
                is_large = false;
                return null;
            }

            size = _mi_align_up(size, _mi_os_page_size());

            // try first with a hint (this will be aligned directly on Win 10+ or BSD)
            void* p = mi_os_mem_alloc(size, alignment, commit, allow_large, out is_large, ref stats);

            if (p == null)
            {
                return null;
            }

            // if not aligned, free it, overallocate, and unmap around it
            if ((nuint)p % alignment != 0)
            {
                mi_os_mem_free(p, size, commit, ref stats);

                if (size >= (SIZE_MAX - alignment))
                {
                    // overflow
                    return null;
                }

                nuint over_size = size + alignment;

                if (IsWindows)
                {
                    // over-allocate and than re-allocate exactly at an aligned address in there.
                    // this may fail due to threads allocating at the same time so we
                    // retry this at most 3 times before giving up.
                    // (we can not decommit around the overallocation on Windows, because we can only
                    //  free the original pointer, not one pointing inside the area)

                    uint flags = MEM_RESERVE;

                    if (commit)
                    {
                        flags |= MEM_COMMIT;
                    }

                    for (int tries = 0; tries < 3; tries++)
                    {
                        // over-allocate to determine a virtual memory range
                        p = mi_os_mem_alloc(over_size, alignment, commit, false, out is_large, ref stats);

                        if (p == null)
                        {
                            // error
                            return null;
                        }

                        if (((nuint)p % alignment) == 0)
                        {
                            // if p happens to be aligned, just decommit the left-over area
                            _mi_os_decommit((byte*)p + size, over_size - size, ref stats);
                            break;
                        }
                        else
                        {
                            // otherwise free and allocate at an aligned address in there
                            mi_os_mem_free(p, over_size, commit, ref stats);

                            void* aligned_p = mi_align_up_ptr(p, alignment);
                            p = mi_win_virtual_alloc(aligned_p, size, alignment, flags, false, allow_large, out is_large);

                            if (p == aligned_p)
                            {
                                // success!
                                break;
                            }

                            if (p != null)
                            {
                                // should not happen?
                                mi_os_mem_free(p, size, commit, ref stats);
                                p = null;
                            }
                        }
                    }
                }
                else
                {
                    // overallocate...
                    p = mi_os_mem_alloc(over_size, alignment, commit, false, out is_large, ref stats);

                    if (p == null)
                    {
                        return null;
                    }

                    // and selectively unmap parts around the over-allocated area.
                    void* aligned_p = mi_align_up_ptr(p, alignment);

                    nuint pre_size = (nuint)((byte*)aligned_p - (byte*)p);
                    nuint mid_size = _mi_align_up(size, _mi_os_page_size());
                    nuint post_size = over_size - pre_size - mid_size;

                    mi_assert_internal((MI_DEBUG > 1) && pre_size < over_size && post_size < over_size && mid_size >= size);

                    if (pre_size > 0)
                    {
                        mi_os_mem_free(p, pre_size, commit, ref stats);
                    }

                    if (post_size > 0)
                    {
                        mi_os_mem_free((byte*)aligned_p + mid_size, post_size, commit, ref stats);
                    }

                    // we can return the aligned pointer on `mmap` systems
                    p = aligned_p;
                }
            }

            mi_assert_internal((MI_DEBUG > 1) && ((p == null) || ((p != null) && (((nuint)p % alignment) == 0))));
            return p;
        }

        /* -----------------------------------------------------------
          OS API: alloc, free, alloc_aligned
        ----------------------------------------------------------- */

        private static partial void* _mi_os_alloc(nuint size, ref mi_stats_t tld_stats)
        {
            ref mi_stats_t stats = ref _mi_stats_main;

            if (size == 0)
            {
                return null;
            }

            size = _mi_os_good_alloc_size(size);
            bool is_large = false;

            return mi_os_mem_alloc(size, 0, true, false, out is_large, ref stats);
        }

        private static partial void _mi_os_free_ex(void* p, nuint size, bool was_committed, ref mi_stats_t tld_stats)
        {
            ref mi_stats_t stats = ref _mi_stats_main;

            if ((size == 0) || (p == null))
            {
                return;
            }

            size = _mi_os_good_alloc_size(size);
            mi_os_mem_free(p, size, was_committed, ref stats);
        }

        private static partial void _mi_os_free(void* p, nuint size, ref mi_stats_t stats) => _mi_os_free_ex(p, size, true, ref stats);

        private static partial void* _mi_os_alloc_aligned(nuint size, nuint alignment, bool commit, ref bool large, mi_os_tld_t* tld)
        {
            if (size == 0)
            {
                return null;
            }

            size = _mi_os_good_alloc_size(size);
            alignment = _mi_align_up(alignment, _mi_os_page_size());

            bool allow_large = false;

            allow_large = large;
            large = false;

            return mi_os_mem_alloc_aligned(size, alignment, commit, allow_large, out large, ref _mi_stats_main);
        }

        /* -----------------------------------------------------------
          OS memory API: reset, commit, decommit, protect, unprotect.
        ----------------------------------------------------------- */

        // OS page align within a given area, either conservative (pages inside the area only),
        // or not (straddling pages outside the area is possible)
        private static void* mi_os_page_align_areax(bool conservative, void* addr, [NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t*")] out nuint newsize)
        {
            mi_assert((MI_DEBUG != 0) && (addr != null) && (size > 0));
            newsize = 0;

            if ((size == 0) || (addr == null))
            {
                return null;
            }

            // page align conservatively within the range

            void* start = conservative ? mi_align_up_ptr(addr, _mi_os_page_size()) : mi_align_down_ptr(addr, _mi_os_page_size());
            void* end = conservative ? mi_align_down_ptr((byte*)addr + size, _mi_os_page_size()) : mi_align_up_ptr((byte*)addr + size, _mi_os_page_size());

            nint diff = (nint)((byte*)end - (byte*)start);

            if (diff <= 0)
            {
                return null;
            }

            mi_assert_internal((MI_DEBUG > 1) && ((conservative && ((nuint)diff <= size)) || (!conservative && ((nuint)diff >= size))));
            newsize = (nuint)diff;

            return start;
        }

        private static void* mi_os_page_align_area_conservative(void* addr, [NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t*")] out nuint newsize) => mi_os_page_align_areax(true, addr, size, out newsize);

        private static void mi_mprotect_hint(int err)
        {
            if (IsUnix && (MI_SECURE >= 2) && (err == ENOMEM))
            {
                // guard page around every mimalloc page
                _mi_warning_message("the previous warning may have been caused by a low memory map limit.\n  On Linux this is controlled by the vm.max_map_count. For example:\n  > sudo sysctl -w vm.max_map_count=262144\n");
            }
        }

        // Commit/Decommit memory.
        // Usually commit is aligned liberal, while decommit is aligned conservative.
        // (but not for the reset version where we want commit to be conservative as well)
        private static bool mi_os_commitx(void* addr, [NativeTypeName("size_t")] nuint size, bool commit, bool conservative, [NativeTypeName("bool*")] out bool is_zero, [NativeTypeName("mi_stats_t*")] ref mi_stats_t stats)
        {
            // page align in the range, commit liberally, decommit conservative
            is_zero = false;

            void* start = mi_os_page_align_areax(conservative, addr, size, out nuint csize);

            if (csize == 0)
            {
                return true;
            }

            int err = 0;

            if (commit)
            {
                // use size for precise commit vs. decommit
                _mi_stat_increase(ref stats.committed, size);
                _mi_stat_counter_increase(ref stats.commit_calls, 1);
            }
            else
            {
                _mi_stat_decrease(ref stats.committed, size);
            }

            if (IsWindows)
            {
                if (commit)
                {
                    // if the memory was already committed, the call succeeds but it is not zero'd
                    void* p = VirtualAlloc(start, csize, MEM_COMMIT, PAGE_READWRITE);
                    err = (p == start) ? 0 : (int)GetLastError();
                }
                else
                {
                    bool ok = VirtualFree(start, csize, MEM_DECOMMIT) != 0;
                    err = ok ? 0 : (int)GetLastError();
                }
            }
            else
            {
                if (!commit)
                {
                    // use mmap with MAP_FIXED to discard the existing memory (and reduce commit charge)
                    void* p = mmap(start, csize, PROT_NONE, MAP_FIXED | MAP_PRIVATE | MAP_ANONYMOUS | MAP_NORESERVE, -1, 0);

                    if (p != start)
                    {
                        err = errno;
                    }
                }
                else
                {
                    // for commit, just change the protection
                    err = mprotect(start, csize, PROT_READ | PROT_WRITE);

                    if (err != 0)
                    {
                        err = errno;
                    }
                }
            }

            if (err != 0)
            {
                _mi_warning_message("{0} error: start: {1:X}, csize: 0x{2:X}, err: {3}\n", commit ? "commit" : "decommit", (nuint)start, csize, err);
                mi_mprotect_hint(err);
            }

            mi_assert_internal((MI_DEBUG > 1) && (err == 0));
            return err == 0;
        }

        private static partial bool _mi_os_commit(void* addr, nuint size, out bool is_zero, ref mi_stats_t tld_stats)
        {
            ref mi_stats_t stats = ref _mi_stats_main;
            return mi_os_commitx(addr, size, true, conservative: false, out is_zero, ref stats);
        }

        private static partial bool _mi_os_decommit(void* addr, nuint size, ref mi_stats_t tld_stats)
        {
            ref mi_stats_t stats = ref _mi_stats_main;
            return mi_os_commitx(addr, size, false, conservative: true, out bool is_zero, ref stats);
        }

        private static bool mi_os_commit_unreset(void* addr, [NativeTypeName("size_t")] nuint size, [NativeTypeName("bool*")] out bool is_zero, [NativeTypeName("mi_stats_t*")] ref mi_stats_t stats) => mi_os_commitx(addr, size, true, conservative: true, out is_zero, ref stats);

        // Signal to the OS that the address range is no longer in use
        // but may be used later again. This will release physical memory
        // pages and reduce swapping while keeping the memory committed.
        // We page align to a conservative area inside the range to reset.
        private static bool mi_os_resetx(void* addr, [NativeTypeName("size_t")] nuint size, bool reset, [NativeTypeName("mi_stats_t*")] ref mi_stats_t stats)
        {
#pragma warning disable CS0420
            // page align conservatively within the range
            void* start = mi_os_page_align_area_conservative(addr, size, out nuint csize);

            if (csize == 0)
            {
                return true;
            }

            if (reset)
            {
                _mi_stat_increase(ref stats.reset, csize);
            }
            else
            {
                _mi_stat_decrease(ref stats.reset, csize);
            }

            if (!reset)
            {
                // nothing to do on unreset!
                return true;
            }

            if ((MI_DEBUG > 1) && (MI_SECURE == 0))
            {
                // pretend it is eagerly reset
                memset(start, 0, csize);
            }

            if (IsWindows)
            {
                // Testing shows that for us (on `malloc-large`) MEM_RESET is 2x faster than DiscardVirtualMemory
                void* p = VirtualAlloc(start, csize, MEM_RESET, PAGE_READWRITE);

                mi_assert_internal((MI_DEBUG > 1) && (p == start));

                if ((p == start) && (start != null))
                {
                    // VirtualUnlock after MEM_RESET removes the memory from the working set
                    VirtualUnlock(start, csize);
                }

                if (p != start)
                {
                    return false;
                }
            }
            else
            {
                int err;

                if (MADV_FREE != 0)
                {
                    err = posix_madvise(start, csize, (int)mi_atomic_load_relaxed(ref advice));

                    if ((err != 0) && (errno == EINVAL) && (advice == (nuint)MADV_FREE))
                    {
                        // if MADV_FREE is not supported, fall back to MADV_DONTNEED from now on
                        mi_atomic_store_release(ref advice, (nuint)MADV_DONTNEED);
                        err = posix_madvise(start, csize, MADV_DONTNEED);
                    }
                }
                else
                {
                    err = posix_madvise(start, csize, MADV_DONTNEED);
                }

                if (err != 0)
                {
                    _mi_warning_message("madvise reset error: start: {0:X}, csize: 0x{1:X}, errno: {2}\n", (nuint)start, csize, errno);
                }

                if (err != 0)
                {
                    return false;
                }
            }

            return true;
#pragma warning restore CS0420
        }

        // Signal to the OS that the address range is no longer in use
        // but may be used later again. This will release physical memory
        // pages and reduce swapping while keeping the memory committed.
        // We page align to a conservative area inside the range to reset.
        private static partial bool _mi_os_reset(void* addr, nuint size, ref mi_stats_t tld_stats)
        {
            ref mi_stats_t stats = ref _mi_stats_main;

            if (mi_option_is_enabled(mi_option_reset_decommits))
            {
                return _mi_os_decommit(addr, size, ref stats);
            }
            else
            {
                return mi_os_resetx(addr, size, true, ref stats);
            }
        }

        private static partial bool _mi_os_unreset(void* addr, nuint size, out bool is_zero, ref mi_stats_t tld_stats)
        {
            ref mi_stats_t stats = ref _mi_stats_main;

            if (mi_option_is_enabled(mi_option_reset_decommits))
            {
                // re-commit it (conservatively!)
                return mi_os_commit_unreset(addr, size, out is_zero, ref stats);
            }
            else
            {
                is_zero = false;
                return mi_os_resetx(addr, size, false, ref stats);
            }
        }


        // Protect a region in memory to be not accessible.
        private static bool mi_os_protectx(void* addr, [NativeTypeName("size_t")] nuint size, bool protect)
        {
            // page align conservatively within the range
            void* start = mi_os_page_align_area_conservative(addr, size, out nuint csize);

            if (csize == 0)
            {
                return false;
            }

            int err = 0;

            if (IsWindows)
            {
                uint oldprotect = 0;
                bool ok = VirtualProtect(start, csize, protect ? PAGE_NOACCESS : PAGE_READWRITE, &oldprotect) != 0;
                err = ok ? 0 : (int)GetLastError();
            }
            else
            {
                err = mprotect(start, csize, protect ? PROT_NONE : (PROT_READ | PROT_WRITE));

                if (err != 0)
                {
                    err = errno;
                }
            }

            if (err != 0)
            {
                _mi_warning_message("mprotect error: start: {0:X}, csize: 0x{1:X}, err: {2}\n", (nuint)start, csize, err);
                mi_mprotect_hint(err);
            }
            return err == 0;
        }

        private static partial bool _mi_os_protect(void* addr, nuint size) => mi_os_protectx(addr, size, true);

        private static partial bool _mi_os_unprotect(void* addr, nuint size) => mi_os_protectx(addr, size, false);

        private static bool _mi_os_shrink(void* p, [NativeTypeName("size_t")] nuint oldsize, [NativeTypeName("size_t")] nuint newsize, [NativeTypeName("mi_stats_t*")] ref mi_stats_t stats)
        {
            // page align conservatively within the range
            mi_assert_internal((MI_DEBUG > 1) && (oldsize > newsize) && (p != null));

            if ((oldsize < newsize) || (p == null))
            {
                return false;
            }

            if (oldsize == newsize)
            {
                return true;
            }

            // oldsize and newsize should be page aligned or we cannot shrink precisely
            void* addr = (byte*)p + newsize;

            void* start = mi_os_page_align_area_conservative(addr, oldsize - newsize, out nuint size);

            if ((size == 0) || (start != addr))
            {
                return false;
            }

            if (IsWindows)
            {
                // we cannot shrink on windows, but we can decommit
                return _mi_os_decommit(start, size, ref stats);
            }
            else
            {
                return mi_os_mem_free(start, size, true, ref stats);
            }
        }

        /* ----------------------------------------------------------------------------
        Support for allocating huge OS pages (1Gib) that are reserved up-front
        and possibly associated with a specific NUMA node. (use `numa_node>=0`)
        -----------------------------------------------------------------------------*/

        private const nuint MI_HUGE_OS_PAGE_SIZE = GiB;

        [return: NativeTypeName("long")]
        private static nint mi_os_mbind(void* start, [NativeTypeName("unsigned long")] nuint len, [NativeTypeName("unsigned long")] nuint mode, [NativeTypeName("const unsigned long*")] nuint* nmask, [NativeTypeName("unsigned long")] nuint maxnode, [NativeTypeName("unsigned")] uint flags)
        {
            mi_assert_internal((MI_DEBUG > 1) && IsUnix);
            return 0;
        }

        private static void* mi_os_alloc_huge_os_pagesx(void* addr, [NativeTypeName("size_t")] nuint size, int numa_node)
        {
            if (!Environment.Is64BitProcess)
            {
                return null;
            }

            if (IsWindows)
            {
                mi_assert_internal((MI_DEBUG > 1) && ((size % GiB) == 0));
                mi_assert_internal((MI_DEBUG > 1) && (addr != null));

                uint flags = MEM_LARGE_PAGES | MEM_COMMIT | MEM_RESERVE;

                if (IsWindows10OrLater)
                {
                    MEM_EXTENDED_PARAMETER* @params = stackalloc MEM_EXTENDED_PARAMETER[3] {
                        default,
                        default,
                        default,
                    };

                    // on modern Windows try use NtAllocateVirtualMemoryEx for 1GiB huge pages
                    if ((NtAllocateVirtualMemoryEx != null) && mi_huge_pages_available)
                    {
                        @params[0].Anonymous1.Type = (ulong)MemExtendedParameterAttributeFlags;
                        @params[0].Anonymous2.ULong64 = MEM_EXTENDED_PARAMETER_NONPAGED_HUGE;

                        uint param_count = 1;

                        if (numa_node >= 0)
                        {
                            param_count++;

                            @params[1].Anonymous1.Type = (ulong)MemExtendedParameterNumaNode;
                            @params[1].Anonymous2.ULong = (uint)numa_node;
                        }

                        nuint psize = size;
                        void* @base = addr;

                        int err = NtAllocateVirtualMemoryEx(GetCurrentProcess(), &@base, &psize, flags, PAGE_READWRITE, @params, param_count);

                        if ((err == 0) && (@base != null))
                        {
                            return @base;
                        }
                        else
                        {
                            // fall back to regular large pages

                            // don't try further huge pages
                            mi_huge_pages_available = false;

                            _mi_warning_message("unable to allocate using huge (1gb) pages, trying large (2mb) pages instead (status 0x{0:X})\n", err);
                        }
                    }
                    // on modern Windows try use VirtualAlloc2 for numa aware large OS page allocation
                    if ((VirtualAlloc2 != null) && (numa_node >= 0))
                    {
                        @params[0].Anonymous1.Type = (ulong)MemExtendedParameterNumaNode;
                        @params[0].Anonymous2.ULong = (uint)numa_node;

                        return VirtualAlloc2(GetCurrentProcess(), addr, size, flags, PAGE_READWRITE, @params, 1);
                    }
                }

                // otherwise use regular virtual alloc on older windows
                return VirtualAlloc(addr, size, flags, PAGE_READWRITE);
            }
            else
            {
                mi_assert_internal((MI_DEBUG > 1) && ((size % GiB) == 0));
                void* p = mi_unix_mmap(addr, size, MI_SEGMENT_SIZE, PROT_READ | PROT_WRITE, true, true, out bool is_large);

                if (p == null)
                {
                    return null;
                }

                if ((numa_node >= 0) && ((uint)numa_node < (8 * MI_INTPTR_SIZE)))
                {
                    // at most 64 nodes
                    nuint numa_mask = 1u << numa_node;

                    // TODO: does `mbind` work correctly for huge OS pages? should we
                    // use `set_mempolicy` before calling mmap instead?
                    // see: <https://lkml.org/lkml/2017/2/9/875>
                    nint err = mi_os_mbind(p, size, MPOL_PREFERRED, &numa_mask, 8 * MI_INTPTR_SIZE, 0);

                    if (err != 0)
                    {
                        _mi_warning_message("failed to bind huge (1gb) pages to numa node {0}: {1}\n", numa_node, errno);
                    }
                }

                return p;
            }
        }

        // Claim an aligned address range for huge pages
        [return: NativeTypeName("uint8_t*")]
        private static byte* mi_os_claim_huge_pages([NativeTypeName("size_t")] nuint pages, [NativeTypeName("size_t*")] out nuint total_size)
        {
#pragma warning disable CS0420
            total_size = 0;

            if (Environment.Is64BitProcess)
            {
                nuint size = pages * MI_HUGE_OS_PAGE_SIZE;

                nuint start = 0;
                nuint end = 0;
                nuint huge_start = mi_atomic_load_relaxed(ref mi_huge_start);

                do
                {
                    start = huge_start;
                    if (start == 0)
                    {
                        // Initialize the start address after the 32TiB area

                        // 32TiB virtual start address
                        start = (nuint)32 << 40;

                        if ((MI_SECURE > 0) || (MI_DEBUG == 0))
                        {
                            // security: randomize start of huge pages unless in debug mode
                            nuint r = _mi_heap_random_next(mi_get_default_heap());

                            // (randomly 12bits)*1GiB == between 0 to 4TiB
                            start = start + (MI_HUGE_OS_PAGE_SIZE * ((r >> 17) & 0x0FFF));
                        }
                    }

                    end = start + size;
                    mi_assert_internal((MI_DEBUG > 1) && (end % MI_SEGMENT_SIZE == 0));
                }
                while (!mi_atomic_cas_strong_acq_rel(ref mi_huge_start, ref huge_start, end));

                total_size = size;
                return (byte*)start;
            }
            else
            {
                return null;
            }
#pragma warning restore CS0420
        }

        // Allocate MI_SEGMENT_SIZE aligned huge pages
        private static partial void* _mi_os_alloc_huge_os_pages(nuint pages, int numa_node, long max_msecs, out nuint pages_reserved, out nuint psize)
        {
            psize = 0;
            pages_reserved = 0;

            byte* start = mi_os_claim_huge_pages(pages, out nuint size);

            if (start == null)
            {
                // or 32-bit systems
                return null;
            }

            // Allocate one page at the time but try to place them contiguously
            // We allocate one page at the time to be able to abort if it takes too long
            // or to at least allocate as many as available on the system.

            long start_t = _mi_clock_start();
            nuint  page;

            for (page = 0; page < pages; page++)
            {
                // allocate a page
                void* addr = start + (page * MI_HUGE_OS_PAGE_SIZE);
                void* p = mi_os_alloc_huge_os_pagesx(addr, MI_HUGE_OS_PAGE_SIZE, numa_node);

                // Did we succeed at a contiguous address?
                if (p != addr)
                {
                    // no success, issue a warning and break
                    if (p != null)
                    {
                        _mi_warning_message("could not allocate contiguous huge page {0} at {1:X}\n", page, (nuint)addr);
                        _mi_os_free(p, MI_HUGE_OS_PAGE_SIZE, ref _mi_stats_main);
                    }
                    break;
                }

                // success, record it
                _mi_stat_increase(ref _mi_stats_main.committed, MI_HUGE_OS_PAGE_SIZE);
                _mi_stat_increase(ref _mi_stats_main.reserved, MI_HUGE_OS_PAGE_SIZE);

                // check for timeout
                if (max_msecs > 0)
                {
                    long elapsed = _mi_clock_end(start_t);

                    if (page >= 1)
                    {
                        long estimate = elapsed / (long)(page + 1) * (long)pages;

                        if (estimate > (2 * max_msecs))
                        {
                            // seems like we are going to timeout, break
                            elapsed = max_msecs + 1;
                        }
                    }

                    if (elapsed > max_msecs)
                    {
                        _mi_warning_message("huge page allocation timed out\n");
                        break;
                    }
                }
            }

            mi_assert_internal((MI_DEBUG > 1) && ((page * MI_HUGE_OS_PAGE_SIZE) <= size));

            pages_reserved = page;
            psize = page * MI_HUGE_OS_PAGE_SIZE;

            return (page == 0) ? null : start;
        }

        // free every huge page in a range individually (as we allocated per page)
        // note: needed with VirtualAlloc but could potentially be done in one go on mmap'd systems.
        private static partial void _mi_os_free_huge_pages(void* p, nuint size, ref mi_stats_t stats)
        {
            if ((p == null) || (size == 0))
            {
                return;
            }

            byte* @base = (byte*)p;

            while (size >= MI_HUGE_OS_PAGE_SIZE)
            {
                _mi_os_free(@base, MI_HUGE_OS_PAGE_SIZE, ref stats);
                size -= MI_HUGE_OS_PAGE_SIZE;
            }
        }

        /* ----------------------------------------------------------------------------
        Support NUMA aware allocation
        -----------------------------------------------------------------------------*/

        [return: NativeTypeName("size_t")]
        private static nuint mi_os_numa_nodex()
        {
            if (IsWindows)
            {
                ushort numa_node = 0;
                if ((GetCurrentProcessorNumberEx != null) && (GetNumaProcessorNodeEx != null))
                {
                    // Extended API is supported

                    PROCESSOR_NUMBER pnum;
                    GetCurrentProcessorNumberEx(&pnum);

                    ushort nnode = 0;
                    bool ok = GetNumaProcessorNodeEx(&pnum, &nnode) != 0;

                    if (ok)
                    {
                        numa_node = nnode;
                    }
                }
                else
                {
                    // Vista or earlier, use older API that is limited to 64 processors. Issue #277
                    uint pnum = GetCurrentProcessorNumber();

                    byte nnode = 0;
                    bool ok = GetNumaProcessorNode((byte)pnum, &nnode) != 0;

                    if (ok)
                    {
                        numa_node = nnode;
                    }
                }

                return numa_node;
            }
            else
            {
                return 0;
            }
        }

        [return: NativeTypeName("size_t")]
        private static nuint mi_os_numa_node_countx()
        {
            if (IsWindows)
            {
                uint numa_max = 0;
                GetNumaHighestNodeNumber(&numa_max);

                // find the highest node number that has actual processors assigned to it. Issue #282
                while (numa_max > 0)
                {
                    if (GetNumaNodeProcessorMaskEx != null)
                    {
                        // Extended API is supported
                        GROUP_AFFINITY affinity;

                        if (GetNumaNodeProcessorMaskEx((ushort)numa_max, &affinity) != 0)
                        {
                            if (affinity.Mask != 0)
                            {
                                // found the maximum non-empty node
                                break;
                            }
                        }
                    }
                    else
                    {
                        // Vista or earlier, use older API that is limited to 64 processors.
                        ulong mask;

                        if (GetNumaNodeProcessorMask((byte)numa_max, &mask) != 0)
                        {
                            if (mask != 0)
                            {
                                // found the maximum non-empty node
                                break;
                            }
                        };
                    }

                    // max node was invalid or had no processor assigned, try again
                    numa_max--;
                }

                return (nuint)numa_max + 1;
            }
            else
            {
                return 1;
            }
        }

        private static partial nuint _mi_os_numa_node_count_get()
        {
            mi_assert_internal((MI_DEBUG > 1) && (_mi_numa_node_count >= 1));
            return _mi_numa_node_count;
        }

        private static partial int _mi_os_numa_node_get(mi_os_tld_t* tld)
        {
            nuint numa_count = _mi_os_numa_node_count();

            if (numa_count <= 1)
            {
                // optimize on single numa node systems: always node 0
                // never more than the node count and >= 0
                return 0;
            }

            nuint numa_node = mi_os_numa_nodex();

            if (numa_node >= numa_count)
            {
                numa_node = numa_node % numa_count;
            }

            return (int)numa_node;
        }
    }
}
