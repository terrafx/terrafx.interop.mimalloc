// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Runtime.InteropServices;
using static TerraFX.Interop.mi_init_t;
using static TerraFX.Interop.mi_option_t;

namespace TerraFX.Interop
{
    public static unsafe partial class Mimalloc
    {
        public static event DllImportResolver? ResolveLibrary;

        // Helpers

        private static int last_errno;

        private static readonly bool IsMacOS = OperatingSystem.IsMacOS();

        private static readonly bool IsLinux = OperatingSystem.IsLinux();

        private static readonly bool IsUnix = IsLinux || IsMacOS;

        private static readonly bool IsWindows = OperatingSystem.IsWindows();

        private static readonly bool IsWindows10OrLater = OperatingSystem.IsWindowsVersionAtLeast(10);

        private static readonly void* MAP_FAILED = unchecked((void*)-1);

        // MI_DEBUG_FULL
        // MI_OVERRIDE

        private static readonly uint MI_SHOW_ERRORS = get_app_context_data(nameof(MI_SHOW_ERRORS), 0);

        private static readonly uint MI_XMALLOC = get_app_context_data(nameof(MI_XMALLOC), 0);

        private static readonly nint PTRDIFF_MAX = nint.MaxValue;

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
        // * MI_SECURE = 1  // guard page around metadata
        // * MI_SECURE = 2  // guard page around each mimalloc page
        // * MI_SECURE = 3  // encode free lists (detect corrupted free list (buffer overflow), and invalid pointer free)
        // * MI_SECURE = 4  // checks for double free. (may be more expensive)
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
        private static readonly uint MI_PADDING = get_app_context_data(nameof(MI_PADDING), (MI_DEBUG >= 1) ? 1 : 0);

        // Encoded free lists allow detection of corrupted free lists
        // and can detect buffer overflows, modify after free, and double `free`s.
        private static readonly uint MI_ENCODE_FREELIST = get_app_context_data(nameof(MI_ENCODE_FREELIST), ((MI_SECURE >= 3) || (MI_DEBUG >= 1) || (MI_PADDING > 0)) ? 1 : 0);

        private static readonly int MI_INTPTR_SHIFT = Environment.Is64BitProcess ? 3 : 2;

        private static readonly nuint MI_INTPTR_SIZE = (nuint)1 << MI_INTPTR_SHIFT;
        private static readonly nuint MI_INTPTR_BITS = MI_INTPTR_SIZE * 8;

        // Main tuning parameters for segment and page sizes
        // Sizes for 64-bit, divide by two for 32-bit

        private static readonly int MI_SMALL_PAGE_SHIFT = 13 + MI_INTPTR_SHIFT;      // 64kb
        private static readonly int MI_MEDIUM_PAGE_SHIFT = 3 + MI_SMALL_PAGE_SHIFT;  // 512kb
        private static readonly int MI_LARGE_PAGE_SHIFT = 3 + MI_MEDIUM_PAGE_SHIFT;  // 4mb
        private static readonly int MI_SEGMENT_SHIFT = MI_LARGE_PAGE_SHIFT;          // 4mb

        // Derived constants

        private static readonly nuint MI_SEGMENT_SIZE = (nuint)1 << MI_SEGMENT_SHIFT;
        private static readonly nuint MI_SEGMENT_MASK = MI_SEGMENT_SIZE - 1;

        private static readonly nuint MI_SMALL_PAGE_SIZE = (nuint)1 << MI_SMALL_PAGE_SHIFT;
        private static readonly nuint MI_MEDIUM_PAGE_SIZE = (nuint)1 << MI_MEDIUM_PAGE_SHIFT;
        private static readonly nuint MI_LARGE_PAGE_SIZE = (nuint)1 << MI_LARGE_PAGE_SHIFT;

        private static readonly nuint MI_SMALL_PAGES_PER_SEGMENT = MI_SEGMENT_SIZE / MI_SMALL_PAGE_SIZE;
        private static readonly nuint MI_MEDIUM_PAGES_PER_SEGMENT = MI_SEGMENT_SIZE / MI_MEDIUM_PAGE_SIZE;
        private static readonly nuint MI_LARGE_PAGES_PER_SEGMENT = MI_SEGMENT_SIZE / MI_LARGE_PAGE_SIZE;

        // The max object size are checked to not waste more than 12.5% internally over the page sizes.
        // (Except for large pages since huge objects are allocated in 4MiB chunks)

        private static readonly nuint MI_SMALL_OBJ_SIZE_MAX = MI_SMALL_PAGE_SIZE / 4;                    // 16kb
        private static readonly nuint MI_MEDIUM_OBJ_SIZE_MAX = MI_MEDIUM_PAGE_SIZE / 4;                  // 128kb
        private static readonly nuint MI_LARGE_OBJ_SIZE_MAX = MI_LARGE_PAGE_SIZE / 2;                    // 2mb
        private static readonly nuint MI_LARGE_OBJ_WSIZE_MAX = MI_LARGE_OBJ_SIZE_MAX / MI_INTPTR_SIZE;
        private static readonly nuint MI_HUGE_OBJ_SIZE_MAX = 2 * MI_INTPTR_SIZE * MI_SEGMENT_SIZE;       // (must match MI_REGION_MAX_ALLOC_SIZE in memory.c)

        // Used as a special value to encode block sizes in 32 bits.
        private static readonly uint MI_HUGE_BLOCK_SIZE = (uint)MI_HUGE_OBJ_SIZE_MAX;

        private static readonly nuint MI_PADDING_SIZE = (MI_PADDING != 0) ? SizeOf<mi_padding_t>() : 0;
        private static readonly nuint MI_PADDING_WSIZE = (MI_PADDING != 0) ? (MI_PADDING_SIZE + MI_INTPTR_SIZE - 1) / MI_INTPTR_SIZE : 0;

        private static readonly nuint MI_PAGES_DIRECT = MI_SMALL_WSIZE_MAX + MI_PADDING_WSIZE + 1;

        // Define MI_STAT as 1 to maintain statistics; set it to 2 to have detailed statistics (but costs some performance).
        private static readonly uint MI_STAT = get_app_context_data(nameof(MI_STAT), (MI_DEBUG > 0) ? 2 : 0);

        //
        // options.c
        //

        // --------------------------------------------------------
        // Options
        // These can be accessed by multiple threads and may be
        // concurrently initialized, but an initializing data race
        // is ok since they resolve to the same value.
        // --------------------------------------------------------

        private static readonly mi_option_desc_t[] options = new mi_option_desc_t[(int)_mi_option_last] {
            // stable options

            new mi_option_desc_t { value = ((MI_DEBUG != 0) || (MI_SHOW_ERRORS != 0)) ? 1 : 0, init = UNINIT, option = mi_option_show_errors, name = "show_errors" },

            new mi_option_desc_t { value = 0, init = UNINIT, option = mi_option_show_stats, name = "show_stats" },

            new mi_option_desc_t { value = 0, init = UNINIT, option = mi_option_verbose, name = "verbose" },

            // the following options are experimental and not all combinations make sense.

            // commit per segment directly (4MiB)  (but see also `eager_commit_delay`)
            new mi_option_desc_t { value = 1, init = UNINIT, option = mi_option_eager_commit, name = "eager_commit" },

            new mi_option_desc_t { value = (IsWindows || !Environment.Is64BitProcess) ? 0 : 1, init = UNINIT, option = mi_option_eager_region_commit, name = "eager_region_commit" },

            // reset decommits memory
            new mi_option_desc_t { value = (IsWindows || !Environment.Is64BitProcess) ? 1 : 0, init = UNINIT, option = mi_option_reset_decommits, name = "reset_decommits" },

            // use large OS pages, use only with eager commit to prevent fragmentation of VMA's
            new mi_option_desc_t { value = 0, init = UNINIT, option = mi_option_large_os_pages, name = "large_os_pages" },

            new mi_option_desc_t { value = 0, init = UNINIT, option = mi_option_reserve_huge_os_pages, name = "reserve_huge_os_pages" },

            // cache N segments per thread
            new mi_option_desc_t { value = 0, init = UNINIT, option = mi_option_segment_cache, name = "segment_cache" },

            // reset page memory on free
            new mi_option_desc_t { value = 1, init = UNINIT, option = mi_option_page_reset, name = "page_reset" },

            // reset free page memory when a thread terminates
            new mi_option_desc_t { value = 0, init = UNINIT, option = mi_option_abandoned_page_reset, name = "abandoned_page_reset" },

            // reset segment memory on free (needs eager commit)
            new mi_option_desc_t { value = 0, init = UNINIT, option = mi_option_segment_reset, name = "segment_reset" },

            // the first N segments per thread are not eagerly committed
            new mi_option_desc_t { value = 1, init = UNINIT, option = mi_option_eager_commit_delay, name = "eager_commit_delay" },

            // reset delay in milli-seconds
            new mi_option_desc_t { value = 100, init = UNINIT, option = mi_option_reset_delay, name = "reset_delay" },

            // 0 = use available numa nodes, otherwise use at most N nodes.
            new mi_option_desc_t { value = 0,   init = UNINIT, option = mi_option_use_numa_nodes, name = "use_numa_nodes" },

            // only apple specific for now but might serve more or less related purpose
            new mi_option_desc_t { value = 100, init = UNINIT, option = mi_option_os_tag, name = "os_tag" },

            // maximum errors that are output
            new mi_option_desc_t { value = 16,  init = UNINIT, option = mi_option_max_errors, name = "max_errors" }
        };

        // stop outputting errors after this
        [NativeTypeName("uintptr_t")]
        private static readonly nuint mi_max_error_count = (nuint)mi_option_get(mi_option_max_errors);

        // For now, don't register output from multiple threads.

        [NativeTypeName("mi_output_fun* volatile")]
        private static volatile mi_output_fun mi_out_default = mi_out_stderr;

        [NativeTypeName("std::atomic<void*>")]
        private static volatile nuint mi_out_arg = 0;

        // when MAX_ERROR_COUNT stop emitting errors and warnings
        [NativeTypeName("std::atomic<uintptr_t>")]
        private static volatile nuint error_count = 0;

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

        private static mi_stats_t _mi_stats_main = default;

        private static readonly mi_heap_t._pages_free_direct_e__FixedBuffer* _mi_heap_empty_pages_free_direct = create_mi_heap_empty_pages_free_direct();

        private static readonly mi_heap_t._pages_e__FixedBuffer* _mi_heap_empty_pages = create_mi_heap_empty_pages();

        [NativeTypeName("mi_tld_t")]
        private static readonly mi_tld_t* tld_main = create_tld();

        private static readonly mi_heap_t* _mi_heap_main = create_mi_heap_main();

        // the thread-local default heap for allocation
        [ThreadStatic]
        private static readonly mi_heap_t* _mi_heap_default = create_mi_heap_default();

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

        private static readonly nuint MI_ARENA_BLOCK_SIZE = 8 * MI_SEGMENT_ALIGN;                           // 32MiB
        private static readonly nuint MI_ARENA_MAX_OBJ_SIZE = MI_BITMAP_FIELD_BITS * MI_ARENA_BLOCK_SIZE;   // 2GiB
        private static readonly nuint MI_ARENA_MIN_OBJ_SIZE = MI_ARENA_BLOCK_SIZE / 2;                      // 16MiB

        // not more than 256 (since we use 8 bits in the memid)
        private const nuint MI_MAX_ARENAS = 64;

        // The available arenas
        [NativeTypeName("std::atomic<mi_arena_t*>")]
        private static readonly nuint* mi_arenas = create_mi_arenas();

        [NativeTypeName("std::atomic<uintptr_t>")]
        private static volatile nuint mi_arena_count = 0;

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
        [NativeTypeName("size_t")]
        private static readonly nuint os_alloc_granularity = get_os_alloc_granularity();

        // if non-zero, use large page allocation
        [NativeTypeName("size_t")]
        private static readonly nuint large_os_page_size = get_large_os_page_size();

        [NativeTypeName("std::atomic<uintptr_t>")]
        private static volatile nuint large_page_try_ok = 0;

        private static bool mi_huge_pages_available = true;

        [NativeTypeName("std::atomic<uintptr_t>")]
        private static volatile nuint advice = MADV_FREE;

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
        private static readonly nuint MI_MIN_EXTEND = (MI_SECURE > 0) ? (8 * MI_SECURE) : 1;

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

        //
        // region.c
        //

        // Constants

        // 64KiB || 1KiB for the region map 
        private static readonly nuint MI_HEAP_REGION_MAX_SIZE = Environment.Is64BitProcess ? unchecked(256 * GiB) : (3 * GiB);

        private static readonly nuint MI_SEGMENT_ALIGN = MI_SEGMENT_SIZE;

        private static readonly nuint MI_REGION_MAX_BLOCKS = MI_BITMAP_FIELD_BITS;
        private static readonly nuint MI_REGION_SIZE = MI_SEGMENT_SIZE * MI_BITMAP_FIELD_BITS;              // 256MiB  (64MiB on 32 bits)
        private static readonly nuint MI_REGION_MAX = MI_HEAP_REGION_MAX_SIZE / MI_REGION_SIZE;             // 1024  (48 on 32 bits)
        private static readonly nuint MI_REGION_MAX_OBJ_BLOCKS = MI_REGION_MAX_BLOCKS / 4;                  // 64MiB
        private static readonly nuint MI_REGION_MAX_OBJ_SIZE = MI_REGION_MAX_OBJ_BLOCKS * MI_SEGMENT_SIZE;

        // The region map
        [NativeTypeName("mem_region_t [MI_REGION_MAX]")]
        private static readonly mem_region_t* regions = create_regions();

        // Allocated regions
        [NativeTypeName("std::atomic<uintptr_t>")]
        private static volatile nuint regions_count = 0;

        //
        // segment.c
        //

        // Use the bottom 20-bits (on 64-bit) of the aligned segment pointers
        // to put in a tag that increments on update to avoid the A-B-A problem.
        private static readonly nuint MI_TAGGED_MASK = MI_SEGMENT_MASK;

        //
        // stats.c
        //

        [NativeTypeName("mi_msecs_t")]
        private static readonly long mi_clock_diff = get_mi_clock_diff();

        [NativeTypeName("mi_msecs_t")]
        private static readonly long mi_process_start = _mi_clock_start();
    }
}
