// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Runtime.InteropServices;
using static TerraFX.Interop.Mimalloc.mi_init_t;
using static TerraFX.Interop.Mimalloc.mi_option_t;

namespace TerraFX.Interop.Mimalloc;

public static unsafe partial class Mimalloc
{
    public static event DllImportResolver? ResolveLibrary;

    // general helpers

    private static int last_errno;

    private static readonly bool IsMacOS = OperatingSystem.IsMacOS();

    private static readonly bool IsLinux = OperatingSystem.IsLinux();

    private static readonly bool IsUnix = IsLinux || IsMacOS;

    private static readonly bool IsWindows = OperatingSystem.IsWindows();

    private static readonly bool os_overcommit = get_os_overcommit();

    private static readonly bool IsWindows10OrLater = OperatingSystem.IsWindowsVersionAtLeast(10);

    private static bool get_os_overcommit()
    {
        if (!IsUnix)
        {
            return false;
        }

        if (!IsLinux)
        {
            return true;
        }

        try
        {
            using FileStream stream = File.OpenRead("/proc/sys/vm/overcommit_memory");
            int mode = stream.ReadByte();
            return (mode < 0) || (mode == '0') || (mode == '1');
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static nuint thread_count;

    [ThreadStatic]
    private static bool thread_done_in_progress;

    [NativeTypeName("std::atomic<mi_thread_data_t*>[32]")]
    private static readonly nuint* mi_thread_data_cache = create_mi_thread_data_cache();

    // MI_DEBUG_FULL
    // MI_OVERRIDE

    private static readonly uint MI_SHOW_ERRORS = get_app_context_data(nameof(MI_SHOW_ERRORS), 0);

    private static readonly uint MI_XMALLOC = get_app_context_data(nameof(MI_XMALLOC), 0);

    private static readonly nint PTRDIFF_MAX = nint.MaxValue;

    private static readonly nuint MI_MAX_ALLOC_SIZE = (nuint)PTRDIFF_MAX;

    private static readonly nuint SIZE_MAX = nuint.MaxValue;

    private static readonly nuint UINTPTR_MAX = nuint.MaxValue;

    private static readonly delegate* unmanaged<PROCESSOR_NUMBER*, void> GetCurrentProcessorNumberEx
        = (delegate* unmanaged<PROCESSOR_NUMBER*, void>)get_export("kernel32", nameof(GetCurrentProcessorNumberEx));

    private static readonly delegate* unmanaged<ushort, GROUP_AFFINITY*, int> GetNumaNodeProcessorMaskEx
        = (delegate* unmanaged<ushort, GROUP_AFFINITY*, int>)get_export("kernel32", nameof(GetNumaNodeProcessorMaskEx));

    private static readonly delegate* unmanaged<PROCESSOR_NUMBER*, ushort*, int> GetNumaProcessorNodeEx
        = (delegate* unmanaged<PROCESSOR_NUMBER*, ushort*, int>)get_export("kernel32", nameof(GetNumaProcessorNodeEx));

    private static readonly delegate* unmanaged<IntPtr, void**, nuint*, uint, uint, MEM_EXTENDED_PARAMETER*, uint, int> NtAllocateVirtualMemoryEx
        = (delegate* unmanaged<IntPtr, void**, nuint*, uint, uint, MEM_EXTENDED_PARAMETER*, uint, int>)get_export("ntdll", nameof(NtAllocateVirtualMemoryEx));

    private static readonly delegate* unmanaged<IntPtr, void*, nuint, uint, uint, MEM_EXTENDED_PARAMETER*, uint, void*> VirtualAlloc2FromApp
        = (delegate* unmanaged<IntPtr, void*, nuint, uint, uint, MEM_EXTENDED_PARAMETER*, uint, void*>)get_export("kernel32", nameof(VirtualAlloc2FromApp));

    private static readonly delegate* unmanaged<IntPtr, void*, nuint, uint, uint, MEM_EXTENDED_PARAMETER*, uint, void*> VirtualAlloc2
        = (delegate* unmanaged<IntPtr, void*, nuint, uint, uint, MEM_EXTENDED_PARAMETER*, uint, void*>)((VirtualAlloc2FromApp == null) ? get_export("kernel32", nameof(VirtualAlloc2)) : VirtualAlloc2FromApp);

    private static readonly delegate*<delegate* unmanaged[Cdecl]<void>> std_get_new_handler
        = IsWindows ? &win32_std_get_new_handler : (IsUnix ? &unix_std_get_new_handler : null);

    //
    // libc values
    //

    private static readonly int _PC_PATH_MAX = IsLinux ? 4 : (IsMacOS ? 5 : 0);

    private static readonly int _SC_PAGESIZE = IsLinux ? 30 : (IsMacOS ? 29 : 0);

    private static readonly int HUGETLB_FLAG_ENCODE_1GB = IsLinux ? (30 << HUGETLB_FLAG_ENCODE_SHIFT) : 0;

    private static readonly int HUGETLB_FLAG_ENCODE_2MB = IsLinux ? (21 << HUGETLB_FLAG_ENCODE_SHIFT) : 0;

    private static readonly int HUGETLB_FLAG_ENCODE_SHIFT = IsLinux ? 26 : 0;

    private const int RUSAGE_SELF = 0;

    private static readonly int MADV_DONTNEED = IsUnix ? 4 : 0;

    private static readonly int MADV_FREE = IsLinux ? 8 : (IsMacOS ? 5 : 0);

    private static readonly int MADV_FREE_REUSABLE = IsMacOS ? 7 : 0;

    private static readonly int MADV_FREE_REUSE = IsMacOS ? 8 : 0;

    private static readonly int MADV_HUGEPAGE = IsLinux ? 14 : 0;

    private static readonly int MAP_ANONYMOUS = IsLinux ? 0x20 : (IsMacOS ? 0x1000 : 0);

    // private static readonly int MAP_ANON = MAP_ANONYMOUS;

    private static readonly void* MAP_FAILED = unchecked((void*)-1);

    private static readonly int MAP_FIXED = IsUnix ? 0x10 : 0;

    private static readonly int MAP_PRIVATE = IsUnix ? 0x02 : 0;

    private static readonly int MAP_HUGE_1GB = IsLinux ? HUGETLB_FLAG_ENCODE_1GB : 0;

    private static readonly int MAP_HUGE_2MB = IsLinux ? HUGETLB_FLAG_ENCODE_2MB : 0;

    private static readonly int MAP_HUGETLB = IsLinux ? 0x040000 : 0;

    private static readonly int MAP_NORESERVE = IsLinux ? 0x4000 : (IsMacOS ? 0x40 : 0);

    private static readonly nuint MPOL_PREFERRED = IsLinux ? 1u : 0u;

    private const int PROT_NONE = 0x0;

    private static readonly int PROT_READ = IsUnix ? 0x1 : 0;

    private static readonly int PROT_WRITE = IsUnix ? 0x2 : 0;

    // private static readonly int R_OK = IsUnix ? 4 : 0;

    private static readonly int SUPERPAGE_SIZE_2MB = IsMacOS ? 2 : 0;

    private static readonly int VM_FLAGS_SUPERPAGE_SHIFT = IsMacOS ? 16 : 0;

    private static readonly int VM_FLAGS_SUPERPAGE_SIZE_2MB = IsMacOS ? (SUPERPAGE_SIZE_2MB << VM_FLAGS_SUPERPAGE_SHIFT) : 0;

    //
    // mimalloc.h
    //

    public static readonly nuint MI_SMALL_WSIZE_MAX = 128;

    public static readonly nuint MI_SMALL_SIZE_MAX = MI_SMALL_WSIZE_MAX * SizeOf<nuint>();

    //
    // mimalloc-internal.h
    //

    // sqrt(SIZE_MAX)
    private static readonly nuint MI_MUL_NO_OVERFLOW = (nuint)1 << (4 * sizeof(nuint));

    //
    // mimalloc-types.h
    //

    // Define MI_SECURE to enable security mitigations
    // * MI_SECURE = 1  // guard page around metadata and randomize arena allocation addresses
    // * MI_SECURE = 2  // randomize relative allocation addresses within mimalloc pages
    // * MI_SECURE = 3  // encode free lists (detect corrupted free lists and invalid pointer frees)
    // * MI_SECURE = 4  // check for double frees
    // * MI_SECURE = 5  // guard page at the end of each mimalloc page
    private static readonly uint MI_SECURE = get_app_context_data(nameof(MI_SECURE), 0);

    // Define MI_DEBUG for debug mode
    // * MI_DEBUG = 1  // basic assertion checks and statistics, check double free, corrupted free list, and invalid pointer free.
    // * MI_DEBUG = 2  // + internal assertion checks
    // * MI_DEBUG = 3  // + extensive internal invariant checking (cmake -DMI_DEBUG_FULL=ON)
#if DEBUG
    private static readonly uint MI_DEBUG = get_app_context_data(nameof(MI_DEBUG), 2);
#else
    private static readonly uint MI_DEBUG = get_app_context_data(nameof(MI_DEBUG), 0);
#endif

    // Reserve extra padding at the end of each block to be more resilient against heap block overflows.
    // The padding can detect byte-precise buffer overflow on free.
    private static readonly uint MI_PADDING = get_app_context_data(nameof(MI_PADDING), ((MI_SECURE >= 3) || (MI_DEBUG >= 1)) ? 1u : 0u);

    private static readonly uint MI_PADDING_CHECK = get_app_context_data(nameof(MI_PADDING_CHECK), ((MI_PADDING > 0) && ((MI_SECURE >= 3) || (MI_DEBUG >= 1))) ? 1u : 0u);

    // Encoded free lists allow detection of corrupted free lists
    // and can detect buffer overflows, modify after free, and double `free`s.
    private static readonly uint MI_ENCODE_FREELIST = get_app_context_data(nameof(MI_ENCODE_FREELIST), ((MI_SECURE >= 3) || (MI_DEBUG >= 1) || (MI_PADDING > 0)) ? 1u : 0u);

    private static readonly int MI_INTPTR_SHIFT = Environment.Is64BitProcess ? 3 : 2;

    private static readonly nuint MI_INTPTR_SIZE = (nuint)1 << MI_INTPTR_SHIFT;
    // private static readonly nuint MI_INTPTR_BITS = MI_INTPTR_SIZE * 8;

    // Main tuning parameters for segment and page sizes
    // Sizes for 64-bit, divide by two for 32-bit

    private static readonly int MI_SMALL_PAGE_SHIFT = 13 + MI_INTPTR_SHIFT;      // 64kb
    private static readonly int MI_MEDIUM_PAGE_SHIFT = 3 + MI_SMALL_PAGE_SHIFT;  // 512kb
    private static readonly int MI_LARGE_PAGE_SHIFT = 3 + MI_MEDIUM_PAGE_SHIFT;  // 4mb
    private static readonly int MI_SEGMENT_SHIFT = MI_LARGE_PAGE_SHIFT;          // 4mb

    // Derived constants

    private static readonly nuint MI_SEGMENT_SIZE = (nuint)1 << MI_SEGMENT_SHIFT;
    private static readonly nuint MI_SEGMENT_MASK = MI_SEGMENT_SIZE - 1;
    private static readonly nuint MI_BLOCK_ALIGNMENT_MAX = MI_SEGMENT_SIZE / 2;
    private static readonly nuint MI_SEGMENT_MAP_MAX_ADDRESS = Environment.Is64BitProcess ? ((nuint)48 << 40) : uint.MaxValue;
    private static readonly nuint MI_SEGMENT_MAP_WSIZE = (MI_SEGMENT_MAP_MAX_ADDRESS / MI_SEGMENT_SIZE) / (MI_INTPTR_SIZE * 8);

    private static readonly nuint MI_SMALL_PAGE_SIZE = (nuint)1 << MI_SMALL_PAGE_SHIFT;
    private static readonly nuint MI_MEDIUM_PAGE_SIZE = (nuint)1 << MI_MEDIUM_PAGE_SHIFT;
    private static readonly nuint MI_LARGE_PAGE_SIZE = (nuint)1 << MI_LARGE_PAGE_SHIFT;

    private static readonly nuint MI_SMALL_PAGES_PER_SEGMENT = MI_SEGMENT_SIZE / MI_SMALL_PAGE_SIZE;
    // private static readonly nuint MI_MEDIUM_PAGES_PER_SEGMENT = MI_SEGMENT_SIZE / MI_MEDIUM_PAGE_SIZE;
    // private static readonly nuint MI_LARGE_PAGES_PER_SEGMENT = MI_SEGMENT_SIZE / MI_LARGE_PAGE_SIZE;

    // The max object size are checked to not waste more than 12.5% internally over the page sizes.
    // (Except for large pages since huge objects are allocated in 4MiB chunks)

    private static readonly nuint MI_SMALL_OBJ_SIZE_MAX = MI_SMALL_PAGE_SIZE / 8;                    // 8kb
    private static readonly nuint MI_MEDIUM_OBJ_SIZE_MAX = MI_MEDIUM_PAGE_SIZE / 8;                  // 64kb
    private static readonly nuint MI_LARGE_OBJ_SIZE_MAX = MI_LARGE_PAGE_SIZE / 4;                   // 1mb
    private static readonly nuint MI_LARGE_OBJ_WSIZE_MAX = MI_LARGE_OBJ_SIZE_MAX / MI_INTPTR_SIZE;
    private static readonly nuint MI_MAX_ALIGN_GUARANTEE = MI_MEDIUM_OBJ_SIZE_MAX;
    private static readonly nuint MI_PADDING_SIZE = (MI_PADDING != 0) ? SizeOf<mi_padding_t>() : 0;
    private static readonly nuint MI_PADDING_WSIZE = (MI_PADDING != 0) ? (MI_PADDING_SIZE + MI_INTPTR_SIZE - 1) / MI_INTPTR_SIZE : 0;

    private static readonly nuint MI_PAGES_DIRECT = MI_SMALL_WSIZE_MAX + MI_PADDING_WSIZE + 1;

    // Define MI_STAT as 1 to maintain statistics; set it to 2 to have detailed statistics (but costs some performance).
    private static readonly uint MI_STAT = get_app_context_data(nameof(MI_STAT), (MI_DEBUG > 0) ? 2u : 0u);

    //
    // options.c
    //

    // --------------------------------------------------------
    // Options
    // These can be accessed by multiple threads and may be
    // concurrently initialized, but an initializing data race
    // is ok since they resolve to the same value.
    // --------------------------------------------------------

    private static readonly mi_option_desc_t[] options = [
        // stable options

        new mi_option_desc_t { value = ((MI_DEBUG != 0) || (MI_SHOW_ERRORS != 0)) ? 1 : 0, init = UNINIT, option = mi_option_show_errors, name = "show_errors" },

        new mi_option_desc_t { value = 0, init = UNINIT, option = mi_option_show_stats, name = "show_stats" },

        new mi_option_desc_t { value = 0, init = UNINIT, option = mi_option_verbose, name = "verbose" },

        // the following options are experimental and not all combinations make sense.

        // commit per segment directly (4MiB)  (but see also `eager_commit_delay`)
        new mi_option_desc_t { value = 1, init = UNINIT, option = mi_option_eager_commit, name = "eager_commit" },

        new mi_option_desc_t { value = 2, init = UNINIT, option = mi_option_arena_eager_commit, name = "arena_eager_commit" },

        // reset decommits memory
        new mi_option_desc_t { value = 1, init = UNINIT, option = mi_option_purge_decommits, name = "purge_decommits" },

        // use large OS pages, use only with eager commit to prevent fragmentation of VMA's
        new mi_option_desc_t { value = 0, init = UNINIT, option = mi_option_allow_large_os_pages, name = "allow_large_os_pages" },

        new mi_option_desc_t { value = 0, init = UNINIT, option = mi_option_reserve_huge_os_pages, name = "reserve_huge_os_pages" },

        new mi_option_desc_t { value = -1, init = UNINIT, option = mi_option_reserve_huge_os_pages_at, name = "reserve_huge_os_pages_at" },

        new mi_option_desc_t { value = 0, init = UNINIT, option = mi_option_reserve_os_memory, name = "reserve_os_memory" },

        // deprecated: previously cached N segments per thread
        new mi_option_desc_t { value = 0, init = UNINIT, option = mi_option_deprecated_segment_cache, name = "deprecated_segment_cache" },

        // reset page memory on free
        new mi_option_desc_t { value = 0, init = UNINIT, option = mi_option_deprecated_page_reset, name = "deprecated_page_reset" },

        // reset free page memory when a thread terminates
        new mi_option_desc_t { value = 0, init = UNINIT, option = mi_option_abandoned_page_purge, name = "abandoned_page_purge" },

        // reset segment memory on free (needs eager commit)
        new mi_option_desc_t { value = 0, init = UNINIT, option = mi_option_deprecated_segment_reset, name = "deprecated_segment_reset" },

        // the first N segments per thread are not eagerly committed
        new mi_option_desc_t { value = 1, init = UNINIT, option = mi_option_eager_commit_delay, name = "eager_commit_delay" },

        // reset delay in milli-seconds
        new mi_option_desc_t { value = 10, init = UNINIT, option = mi_option_purge_delay, name = "purge_delay" },

        // 0 = use available numa nodes, otherwise use at most N nodes.
        new mi_option_desc_t { value = 0,   init = UNINIT, option = mi_option_use_numa_nodes, name = "use_numa_nodes" },

        // 1 = do not use OS memory for allocation (but only reserved arenas)
        new mi_option_desc_t { value = 0,   init = UNINIT, option = mi_option_disallow_os_alloc, name = "disallow_os_alloc" },

        // only apple specific for now but might serve more or less related purpose
        new mi_option_desc_t { value = 100, init = UNINIT, option = mi_option_os_tag, name = "os_tag" },

        // maximum errors that are output
        new mi_option_desc_t { value = 32, init = UNINIT, option = mi_option_max_errors, name = "max_errors" },

        // maximum warnings that are output
        new mi_option_desc_t { value = 32,  init = UNINIT, option = mi_option_max_warnings, name = "max_warnings" },

        new mi_option_desc_t { value = 10, init = UNINIT, option = mi_option_max_segment_reclaim, name = "max_segment_reclaim" },

        new mi_option_desc_t { value = 0, init = UNINIT, option = mi_option_destroy_on_exit, name = "destroy_on_exit" },

        // reserve memory N KiB at a time
        new mi_option_desc_t { value = Environment.Is64BitProcess ? (1024 * 1024) : (128 * 1024), init = UNINIT, option = mi_option_arena_reserve, name = "arena_reserve" },

        // reset/decommit delay in milliseconds for arena allocations
        new mi_option_desc_t { value = 10, init = UNINIT, option = mi_option_arena_purge_mult, name = "arena_purge_mult" },

        new mi_option_desc_t { value = 1, init = UNINIT, option = mi_option_purge_extend_delay, name = "purge_extend_delay" },

        new mi_option_desc_t { value = 0, init = UNINIT, option = mi_option_abandoned_reclaim_on_free, name = "abandoned_reclaim_on_free" },

        new mi_option_desc_t { value = 0, init = UNINIT, option = mi_option_disallow_arena_alloc, name = "disallow_arena_alloc" },

        new mi_option_desc_t { value = 400, init = UNINIT, option = mi_option_retry_on_oom, name = "retry_on_oom" },

        new mi_option_desc_t { value = 0, init = UNINIT, option = mi_option_visit_abandoned, name = "visit_abandoned" },

        new mi_option_desc_t { value = 0, init = UNINIT, option = mi_option_guarded_min, name = "guarded_min" },

        new mi_option_desc_t { value = (int)GiB, init = UNINIT, option = mi_option_guarded_max, name = "guarded_max" },

        new mi_option_desc_t { value = 0, init = UNINIT, option = mi_option_guarded_precise, name = "guarded_precise" },

        new mi_option_desc_t { value = 0, init = UNINIT, option = mi_option_guarded_sample_rate, name = "guarded_sample_rate" },

        new mi_option_desc_t { value = 0, init = UNINIT, option = mi_option_guarded_sample_seed, name = "guarded_sample_seed" },

        new mi_option_desc_t { value = 0, init = UNINIT, option = mi_option_target_segments_per_thread, name = "target_segments_per_thread" },

        new mi_option_desc_t { value = 10000, init = UNINIT, option = mi_option_generic_collect, name = "generic_collect" },

        new mi_option_desc_t { value = OperatingSystem.IsAndroid() ? 0 : 1, init = UNINIT, option = mi_option_allow_thp, name = "allow_thp" }
    ];

    // stop outputting errors and warnings after their respective limits
    [NativeTypeName("long")]
    private static readonly long mi_max_error_count = mi_option_get(mi_option_max_errors);

    [NativeTypeName("long")]
    private static readonly long mi_max_warning_count = mi_option_get(mi_option_max_warnings);

    // For now, don't register output from multiple threads.

    [NativeTypeName("mi_output_fun* volatile")]
    private static volatile mi_output_fun? mi_out_default = mi_out_stderr;

    [NativeTypeName("std::atomic<void*>")]
    private static volatile nuint mi_out_arg = 0;

    // when MAX_ERROR_COUNT stop emitting errors and warnings
    [NativeTypeName("std::atomic<uintptr_t>")]
    private static volatile nuint error_count = 0;

    [NativeTypeName("std::atomic<uintptr_t>")]
    private static volatile nuint warning_count = 0;

    [NativeTypeName("mi_error_fun* volatile")]

    private static volatile mi_error_fun mi_error_handler = mi_error_default;

    [NativeTypeName("std::atomic<void*>")]
    private static volatile nuint mi_error_arg = 0;

    //
    // init.c
    //

    // Empty page used to initialize the small free pages array
    [NativeTypeName("const mi_page_t")]
    private static readonly mi_page_t* _mi_page_empty = create_mi_page_empty();

    // --------------------------------------------------------
    // Statically allocate an empty heap as the initial
    // thread local value for the default heap,
    // and statically allocate the backing heap for the main
    // thread so it can function without doing any allocation
    // itself (as accessing a thread local for the first time
    // may lead to allocation itself on some platforms)
    // --------------------------------------------------------

    private static mi_stats_internal_t _mi_stats_main;

    private static readonly mi_subproc_t* _mi_subproc_default = create_mi_subproc_default();

    private static readonly mi_heap_t._pages_free_direct_e__FixedBuffer* _mi_heap_empty_pages_free_direct = create_mi_heap_empty_pages_free_direct();

    private static readonly mi_heap_t._pages_e__FixedBuffer* _mi_heap_empty_pages = create_mi_heap_empty_pages();

    [NativeTypeName("mi_tld_t")]
    private static readonly mi_tld_t* tld_main = create_tld();

    private static readonly mi_heap_t* _mi_heap_main = create_mi_heap_main();

    // the thread-local default heap for allocation
    [ThreadStatic]
    private static mi_heap_t* _mi_heap_default;

    //
    // alloc.c
    //

    [NativeTypeName("size_t")]
    private static readonly nuint path_max = get_path_max();

    //
    // arena.c
    //

    /* -----------------------------------------------------------
      Arena allocation
    ----------------------------------------------------------- */

    private static readonly nuint MI_ARENA_BLOCK_SIZE = MI_SEGMENT_SIZE;
    private static readonly nuint MI_ARENA_MIN_OBJ_SIZE = MI_ARENA_BLOCK_SIZE / 2;

    // Limited as the reservation exponentially increases and takes up static memory
    private const nuint MI_MAX_ARENAS = 255;

    private static readonly nuint MI_ARENA_STATIC_MAX = Environment.Is64BitProcess ? 8u * 1024u : 4u * 1024u;

    private static readonly byte* mi_arena_static = create_mi_arena_static();

    [NativeTypeName("std::atomic<size_t>")]
    private static volatile nuint mi_arena_static_top;

    // The available arenas
    [NativeTypeName("std::atomic<mi_arena_t*>")]
    private static readonly nuint* mi_arenas = create_mi_arenas();

    private static readonly nuint* mi_segment_map = create_mi_segment_map();

    [NativeTypeName("std::atomic<uintptr_t>")]
    private static volatile nuint mi_arena_count = 0;

    [NativeTypeName("std::atomic<size_t>")]
    private static volatile nuint mi_abandoned_segment_count;

    private static volatile nuint mi_arena_purge_guard;

    //
    // bitmap.inc
    //

    private static readonly nuint MI_BITMAP_FIELD_BITS = 8 * MI_INTPTR_SIZE;

    // all bits set
    private static readonly nuint MI_BITMAP_FIELD_FULL = ~(nuint)0;

    //
    // heap.c
    //

    private static readonly nuint MI_MAX_BLOCKS = MI_SMALL_PAGE_SIZE / SizeOf<nuint>();

    //
    // os.c
    //

    // page size (initialized properly in `os_init`)
    [NativeTypeName("size_t")]
    private static readonly nuint os_page_size = get_os_page_size();

    // minimal allocation granularity
    // [NativeTypeName("size_t")]
    // private static readonly nuint os_alloc_granularity = get_os_alloc_granularity();

    // if non-zero, use large page allocation
    [NativeTypeName("size_t")]
    private static readonly nuint large_os_page_size = get_large_os_page_size();

    private static readonly nuint os_virtual_address_bits = get_os_virtual_address_bits();

    [NativeTypeName("std::atomic<uintptr_t>")]
    private static volatile nuint large_page_try_ok = 0;

    private static volatile int mi_huge_pages_available = (!IsUnix || (MAP_HUGE_1GB != 0)) ? 1 : 0;

    [NativeTypeName("std::atomic<uintptr_t>")]
    private static volatile nuint advice = (nuint)((MADV_FREE_REUSABLE != 0) ? MADV_FREE_REUSABLE : MADV_FREE);

    // To ensure proper alignment, use our own area for huge OS pages

    [NativeTypeName("std::atomic<uintptr_t>")]
    private static volatile nuint mi_huge_start = 0;

    // cache the node count
    [NativeTypeName("size_t")]
    private static readonly nuint _mi_numa_node_count = get_mi_numa_node_count();

    //
    // page.c
    //

    private static readonly nuint MI_MAX_RETIRE_SIZE = MI_LARGE_OBJ_SIZE_MAX;

    private static readonly nuint MI_MAX_SLICES = (nuint)1 << MI_MAX_SLICE_SHIFT;

    // extend at least by this many
    private static readonly nuint MI_MIN_EXTEND = (MI_SECURE >= 2) ? (8 * MI_SECURE) : 1;

    [NativeTypeName("mi_deferred_free_fun* volatile")]
    private static volatile mi_deferred_free_fun? deferred_free = null;

    [NativeTypeName("std::atomic<void*>")]
    private static nuint deferred_arg = 0;

    //
    // page-queue.c
    //

    /* -----------------------------------------------------------
      Minimal alignment in machine words (i.e. `sizeof(void*)`)
    ----------------------------------------------------------- */

    private static readonly bool MI_ALIGN4W = MI_MAX_ALIGN_SIZE > (2 * MI_INTPTR_SIZE);

    private static readonly bool MI_ALIGN2W = !MI_ALIGN4W && (MI_MAX_ALIGN_SIZE > MI_INTPTR_SIZE);

    private static readonly nuint MI_SEGMENT_ALIGN = MI_SEGMENT_SIZE;

    //
    // segment.c
    //

    // Use the bottom 20-bits (on 64-bit) of the aligned segment pointers
    // to put in a tag that increments on update to avoid the A-B-A problem.

    //
    // stats.c
    //

    [NativeTypeName("mi_msecs_t")]
    private static readonly long mi_clock_diff = get_mi_clock_diff();

    [NativeTypeName("mi_msecs_t")]
    private static readonly long mi_process_start = _mi_clock_start();
}
