// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the main.c file from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using NUnit.Framework;
using static TerraFX.Interop.Mimalloc.Mimalloc;

namespace TerraFX.Interop.Mimalloc.UnitTests;

/// <summary>Provides validation of the <see cref="Mimalloc" /> class.</summary>
public static unsafe partial class MimallocTests
{
    private const uint PAGE_NOACCESS = 0x01;

    [StructLayout(LayoutKind.Sequential)]
    private struct memory_basic_information
    {
        public void* base_address;
        public void* allocation_base;
        public uint allocation_protect;
        public ushort partition_id;
        public nuint region_size;
        public uint state;
        public uint protect;
        public uint type;
    }

    [DllImport("kernel32", ExactSpelling = true, SetLastError = true)]
    private static extern nuint VirtualQuery(void* address, memory_basic_information* information, nuint length);

    private static bool is_noaccess_page(void* address)
    {
        if (OperatingSystem.IsWindows())
        {
            memory_basic_information information = default;
            return (VirtualQuery(address, &information, (nuint)sizeof(memory_basic_information)) != 0)
                && ((information.protect & 0xFF) == PAGE_NOACCESS);
        }

        if (OperatingSystem.IsLinux())
        {
            ulong target = (ulong)(nuint)address;
            foreach (string line in File.ReadLines("/proc/self/maps"))
            {
                int separator = line.IndexOf('-');
                int space = line.IndexOf(' ');
                if ((separator <= 0) || (space <= separator))
                {
                    continue;
                }

                if (ulong.TryParse(line.AsSpan(0, separator), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong start)
                    && ulong.TryParse(line.AsSpan(separator + 1, space - separator - 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong end)
                    && (target >= start) && (target < end))
                {
                    return line.AsSpan(space + 1, 3).SequenceEqual("---");
                }
            }
        }

        Assert.Ignore("Guard-page protection inspection is only implemented for Windows and Linux.");
        return false;
    }

    private static void test_heap(void* p_out)
    {
        IntPtr heap = mi_heap_new();

        _ = mi_heap_malloc(heap, 32);
        _ = mi_heap_malloc(heap, 48);

        mi_free(p_out);
        mi_heap_destroy(heap);
    }

    private static void test_large()
    {
        const nuint N = 1000;

        for (nuint i = 0; i < N; ++i)
        {
            nuint sz = 1 << 21;
            sbyte* a = mi_mallocn_tp<sbyte>(sz);

            for (nuint k = 0; k < sz; k++)
            {
                a[k] = (sbyte)'x';
            }

            mi_free(a);
        }
    }

    /// <summary>Validates Windows realpath input and path-length errors use errno values.</summary>
    [Test]
    public static void WindowsRealpathErrorsTest()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("Windows GetFullPathName error mapping is platform-specific.");
        }

        Assert.That((nuint)mi_realpath(null, null), Is.EqualTo((nuint)0));
        Assert.That(get_last_errno(), Is.EqualTo(22));

        sbyte* empty = stackalloc sbyte[1];
        empty[0] = 0;
        Assert.That((nuint)mi_realpath(empty, null), Is.EqualTo((nuint)0));
        Assert.That(get_last_errno(), Is.EqualTo(22));

    }

    private static bool visit_heap_block(IntPtr heap, mi_heap_area_t* area, void* block, nuint block_size, void* arg) => true;

    /// <summary>Performs basic validation.</summary>
    [Test]
    public static void MainTest()
    {
        void* p1 = mi_malloc(16);
        void* p2 = mi_malloc(1000000);

        mi_free(p1);
        mi_free(p2);

        p1 = mi_malloc(16);
        p2 = mi_malloc(16);

        mi_free(p1);
        mi_free(p2);

        test_heap(mi_malloc(32));

        p1 = mi_malloc_aligned(64, 16);
        p2 = mi_malloc_aligned(160, 24);

        mi_free(p2);
        mi_free(p1);

        test_large();

        mi_collect(true);
        mi_stats_print(null);
    }

    /// <summary>Validates the public statistics snapshot and JSON APIs.</summary>
    [Test]
    public static void StatsSnapshotApiTest()
    {
        mi_stats_t stats = default;
        stats.size = (nuint)sizeof(mi_stats_t);
        stats.version = (nuint)MI_STAT_VERSION;

        Assert.That(mi_stats_get(&stats), Is.True);
        Assert.That(stats.size, Is.EqualTo((nuint)sizeof(mi_stats_t)));
        Assert.That(stats.version, Is.EqualTo((nuint)MI_STAT_VERSION));

        long page_bin_current = 0;
        mi_stat_count_t* page_bins = &stats.page_bins.e0;
        int bin_count = sizeof(mi_stats_t._bins_e__FixedBuffer) / sizeof(mi_stat_count_t);
        for (int i = 0; i < bin_count; i++)
        {
            page_bin_current += page_bins[i].current;
        }
        Assert.That(page_bin_current, Is.EqualTo(stats.pages.current));

        stats.version = 0;
        Assert.That(mi_stats_get(&stats), Is.False);

        sbyte* json = mi_stats_get_json(0, null);
        try
        {
            Assert.That((nuint)json, Is.Not.Zero);

            int length = 0;
            while (json[length] != 0)
            {
                length++;
            }

            byte[] payload = new byte[length];
            Marshal.Copy((IntPtr)json, payload, 0, length);
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement root = document.RootElement;

            Assert.That(root.GetProperty("stat_version").GetInt32(), Is.EqualTo(MI_STAT_VERSION));
            Assert.That(root.GetProperty("mimalloc_version").GetInt32(), Is.EqualTo(MI_MALLOC_VERSION));
            Assert.That(root.GetProperty("malloc_bins").GetArrayLength(), Is.GreaterThan(0));
            Assert.That(root.GetProperty("page_bins").GetArrayLength(), Is.GreaterThan(0));
        }
        finally
        {
            mi_free(json);
        }

        sbyte* supplied_buffer = stackalloc sbyte[65536];
        sbyte* supplied_result = mi_stats_get_json(65536, supplied_buffer);
        Assert.That((nuint)supplied_result, Is.EqualTo((nuint)supplied_buffer));

        sbyte* small_buffer = stackalloc sbyte[4];
        Assert.That((nuint)mi_stats_get_json(4, small_buffer), Is.EqualTo((nuint)0));
        Assert.That(small_buffer[0], Is.EqualTo((sbyte)0));
    }

#if DEBUG
    /// <summary>Validates requested-byte statistics remain cumulative after the allocation is freed.</summary>
    [Test]
    public static void StatsRequestedBytesTest()
    {
        mi_stats_merge();

        mi_stats_t before = default;
        before.size = (nuint)sizeof(mi_stats_t);
        before.version = (nuint)MI_STAT_VERSION;
        Assert.That(mi_stats_get(&before), Is.True);

        const nuint requested = 37;
        void* p = mi_malloc(requested);
        Assert.That((nuint)p, Is.Not.Zero);

        try
        {
            mi_stats_merge();

            mi_stats_t allocated = default;
            allocated.size = (nuint)sizeof(mi_stats_t);
            allocated.version = (nuint)MI_STAT_VERSION;
            Assert.That(mi_stats_get(&allocated), Is.True);
            Assert.That(allocated.malloc_requested.total - before.malloc_requested.total, Is.EqualTo((long)requested));

            mi_free(p);
            p = null;
            mi_stats_merge();

            mi_stats_t freed = default;
            freed.size = (nuint)sizeof(mi_stats_t);
            freed.version = (nuint)MI_STAT_VERSION;
            Assert.That(mi_stats_get(&freed), Is.True);
            Assert.That(freed.malloc_requested.total, Is.EqualTo(allocated.malloc_requested.total));
            Assert.That(freed.malloc_requested.current, Is.EqualTo(allocated.malloc_requested.current));
        }
        finally
        {
            mi_free(p);
        }
    }
#endif

    /// <summary>Validates duplicated strings and environment sizes include their terminators.</summary>
    [Test]
    public static void DuplicationSizeIncludesNullTerminatorTest()
    {
        ushort* source = stackalloc ushort[3];
        source[0] = (ushort)'A';
        source[1] = (ushort)'B';
        source[2] = 0;

        ushort* duplicate = mi_wcsdup(source);
        try
        {
            Assert.That((nuint)duplicate, Is.Not.Zero);
            Assert.That(duplicate[0], Is.EqualTo((ushort)'A'));
            Assert.That(duplicate[1], Is.EqualTo((ushort)'B'));
            Assert.That(duplicate[2], Is.EqualTo((ushort)0));
        }
        finally
        {
            mi_free(duplicate);
        }

        if (Environment.GetEnvironmentVariable("PATH") is null)
        {
            Assert.Ignore("The process has no PATH environment variable.");
        }

        sbyte* name = stackalloc sbyte[5];
        name[0] = (sbyte)'P';
        name[1] = (sbyte)'A';
        name[2] = (sbyte)'T';
        name[3] = (sbyte)'H';
        name[4] = 0;

        sbyte* value = null;
        nuint size = 0;
        try
        {
            Assert.That(mi_dupenv_s(&value, &size, name), Is.Zero);
            Assert.That((nuint)value, Is.Not.Zero);

            nuint length = 0;
            while (value[length] != 0)
            {
                length++;
            }
            Assert.That(size, Is.EqualTo(length + 1));
        }
        finally
        {
            mi_free(value);
        }
    }

    /// <summary>Validates environment duplication clears the reported size before argument validation.</summary>
    [Test]
    public static void EnvironmentDuplicationInvalidArgumentsClearSizeTest()
    {
        sbyte* name = stackalloc sbyte[2];
        name[0] = (sbyte)'X';
        name[1] = 0;
        nuint size = 123;

        Assert.That(mi_dupenv_s(null, &size, name), Is.EqualTo(22));
        Assert.That(size, Is.EqualTo((nuint)0));

        ushort* wide_name = stackalloc ushort[2];
        wide_name[0] = (ushort)'X';
        wide_name[1] = 0;
        size = 123;

        Assert.That(mi_wdupenv_s(null, &size, wide_name), Is.EqualTo(22));
        Assert.That(size, Is.EqualTo((nuint)0));
    }

    /// <summary>Validates string duplication handles a maximum requested length safely.</summary>
    [Test]
    public static void StringDuplicationMaximumLengthTest()
    {
        sbyte* source = stackalloc sbyte[3];
        source[0] = (sbyte)'A';
        source[1] = (sbyte)'B';
        source[2] = 0;

        sbyte* duplicate = mi_strndup(source, nuint.MaxValue);
        try
        {
            Assert.That((nuint)duplicate, Is.Not.Zero);
            Assert.That(duplicate[0], Is.EqualTo((sbyte)'A'));
            Assert.That(duplicate[1], Is.EqualTo((sbyte)'B'));
            Assert.That(duplicate[2], Is.EqualTo((sbyte)0));
        }
        finally
        {
            mi_free(duplicate);
        }

        sbyte* empty = mi_strndup(source, 0);
        try
        {
            Assert.That((nuint)empty, Is.Not.Zero);
            Assert.That(empty[0], Is.EqualTo((sbyte)0));
        }
        finally
        {
            mi_free(empty);
        }
    }

    /// <summary>Validates allocation across bitmap fields in reserved memory.</summary>
    [Test]
    public static void ReserveOSMemoryTest()
    {
        if (!Environment.Is64BitProcess)
        {
            Assert.Ignore("The reserved arena exceeds the 32-bit address space.");
        }

        nuint field_capacity = (nuint)1 << 30;
        nuint size = field_capacity + 1;
        Assert.That(mi_manage_os_memory(null, 1, false, false, true, -1), Is.False);
        Assert.That(mi_reserve_os_memory(1, false, false), Is.Zero);
        Assert.That(mi_reserve_os_memory(nuint.MaxValue, false, false), Is.EqualTo(12));
        Assert.That(mi_reserve_os_memory(field_capacity * 2, false, false), Is.Zero);

        mi_option_t limitOption = mi_option_t.mi_option_limit_os_alloc;
        int previousLimit = mi_option_get(limitOption);
        mi_option_t purgeDelayOption = mi_option_t.mi_option_arena_purge_mult;
        int previousPurgeDelay = mi_option_get(purgeDelayOption);
        mi_option_set(limitOption, 1);
        mi_option_set(purgeDelayOption, 0);

        try
        {
            for (int i = 0; i < 2; i++)
            {
                void* p = mi_malloc(size);

                try
                {
                    Assert.That((nuint)p, Is.Not.Zero);
                    Assert.That(mi_usable_size(p), Is.GreaterThanOrEqualTo(size));

                    byte* data = (byte*)p;
                    data[0] = 0xA5;
                    data[size - 1] = 0x5A;

                    Assert.That(data[0], Is.EqualTo(0xA5));
                    Assert.That(data[size - 1], Is.EqualTo(0x5A));
                }
                finally
                {
                    mi_free(p);
                }

                mi_collect(force: true);
            }
        }
        finally
        {
            mi_option_set(purgeDelayOption, previousPurgeDelay);
            mi_option_set(limitOption, previousLimit);
        }
    }

    /// <summary>Validates huge-page reservation rejects overflowing address ranges.</summary>
    [Test]
    public static void ReserveHugePagesRejectsOverflowTest()
    {
        nuint hugePageSize = (nuint)1 << 30;
        Assert.That(mi_reserve_huge_os_pages_at_ex(nuint.MaxValue, -1, 0, false, null), Is.Not.Zero);
        Assert.That(mi_reserve_huge_os_pages_at_ex(nuint.MaxValue / hugePageSize, -1, 0, false, null), Is.Not.Zero);
    }

    /// <summary>Validates that an arena-specific heap allocates only from its exclusive arena.</summary>
    [Test]
    public static void ExclusiveArenaHeapTest()
    {
        int arenaId = 0;
        Assert.That(mi_reserve_os_memory_ex((nuint)64 << 20, commit: false, allow_large: false, exclusive: true, &arenaId), Is.Zero);
        Assert.That(arenaId, Is.GreaterThan(0));

        nuint arenaSize = 0;
        byte* arenaStart = (byte*)mi_arena_area(arenaId, &arenaSize);
        Assert.That((nuint)arenaStart, Is.Not.Zero);
        Assert.That(arenaSize, Is.GreaterThanOrEqualTo((nuint)64 << 20));

        IntPtr heap = mi_heap_new_in_arena(arenaId);
        Assert.That(heap, Is.Not.EqualTo(IntPtr.Zero));

        const nuint size = 2 * 1024 * 1024;
        mi_option_t disallowArenaOption = mi_option_t.mi_option_disallow_arena_alloc;
        int previousDisallowArena = mi_option_get(disallowArenaOption);

        try
        {
            mi_option_set(disallowArenaOption, 1);

            void* p = mi_heap_malloc(heap, size);
            try
            {
                Assert.That((nuint)p, Is.Not.Zero);
                Assert.That((nuint)p, Is.GreaterThanOrEqualTo((nuint)arenaStart));
                Assert.That((nuint)p + size, Is.LessThanOrEqualTo((nuint)arenaStart + arenaSize));
                Assert.That(mi_heap_contains_block(heap, p), Is.True);
            }
            finally
            {
                mi_free(p);
            }

            void* defaultHeapAllocation = mi_malloc(size);
            try
            {
                Assert.That((nuint)defaultHeapAllocation, Is.Not.Zero);
                Assert.That(((nuint)defaultHeapAllocation < (nuint)arenaStart) || ((nuint)defaultHeapAllocation >= ((nuint)arenaStart + arenaSize)), Is.True);
            }
            finally
            {
                mi_free(defaultHeapAllocation);
            }
        }
        finally
        {
            mi_option_set(disallowArenaOption, previousDisallowArena);
            mi_heap_delete(heap);
            mi_collect(force: true);
        }
    }

    /// <summary>Validates that externally managed memory is aligned and its usable size is reduced accordingly.</summary>
    [Test]
    public static void ManagedOsMemoryAlignmentTest()
    {
        nuint segmentSize = Environment.Is64BitProcess ? ((nuint)4 << 20) : ((nuint)2 << 20);
        nuint allocationSize = 2 * segmentSize;
        byte* memory = (byte*)NativeMemory.AlignedAlloc(allocationSize, segmentSize);
        Assert.That((nuint)memory, Is.Not.Zero);

        bool managed = false;
        try
        {
            int arenaId = 0;
            managed = mi_manage_os_memory_ex(memory + 1, allocationSize, is_committed: true, is_large: false, is_zero: false, numa_node: -1, exclusive: true, arena_id: &arenaId);
            Assert.That(managed, Is.True);

            nuint areaSize = 0;
            byte* area = (byte*)mi_arena_area(arenaId, &areaSize);
            Assert.That((nuint)area, Is.EqualTo((nuint)(memory + segmentSize)));
            Assert.That(areaSize, Is.EqualTo(segmentSize));
        }
        finally
        {
            if (!managed)
            {
                NativeMemory.AlignedFree(memory);
            }
        }
    }

    /// <summary>Validates that deleting an exclusive-arena heap does not transfer its pages to the backing heap.</summary>
    [Test]
    public static void IncompatibleArenaHeapDeleteTest()
    {
        int arenaId = 0;
        Assert.That(mi_reserve_os_memory_ex((nuint)64 << 20, commit: false, allow_large: false, exclusive: true, &arenaId), Is.Zero);
        Assert.That(arenaId, Is.GreaterThan(0));

        nuint arenaSize = 0;
        byte* arenaStart = (byte*)mi_arena_area(arenaId, &arenaSize);
        Assert.That((nuint)arenaStart, Is.Not.Zero);

        IntPtr heap = mi_heap_new_in_arena(arenaId);
        Assert.That(heap, Is.Not.EqualTo(IntPtr.Zero));

        const nuint size = 12345;
        void* arenaAllocation = null;
        void*[] defaultAllocations = new void*[128];
        int allocated = 0;

        try
        {
            arenaAllocation = mi_heap_malloc(heap, size);
            Assert.That((nuint)arenaAllocation, Is.Not.Zero);

            mi_heap_delete(heap);
            heap = IntPtr.Zero;

            for (int i = 0; i < defaultAllocations.Length; i++)
            {
                void* p = mi_malloc(size);
                Assert.That((nuint)p, Is.Not.Zero);
                defaultAllocations[i] = p;
                allocated++;

                Assert.That(((nuint)p < (nuint)arenaStart) || ((nuint)p >= ((nuint)arenaStart + arenaSize)), Is.True);
            }
        }
        finally
        {
            for (int i = 0; i < allocated; i++)
            {
                mi_free(defaultAllocations[i]);
            }

            mi_free(arenaAllocation);

            if (heap != IntPtr.Zero)
            {
                mi_heap_delete(heap);
            }

            mi_collect(force: true);
        }
    }

    /// <summary>Validates that reducing thread-owned segments abandons full pages without losing live blocks.</summary>
    [Test]
    public static void CollectReduceAbandonsFullSegmentsTest()
    {
        mi_option_t option = mi_option_t.mi_option_target_segments_per_thread;
        int previous = mi_option_get(option);
        IntPtr heap = mi_heap_get_default();
        const nuint size = 16 * 1024;
        const int blockCount = 256;
        void*[] blocks = new void*[blockCount];
        int allocated = 0;

        try
        {
            mi_option_set(option, 0);

            for (int i = 0; i < blocks.Length; i++)
            {
                void* p = mi_malloc(size);
                Assert.That((nuint)p, Is.Not.Zero);
                blocks[i] = p;
                allocated++;
                ((byte*)p)[0] = (byte)((i % 251) + 1);
                Assert.That(mi_heap_contains_block(heap, p), Is.True);
            }

            nuint segmentSize = Environment.Is64BitProcess ? ((nuint)4 << 20) : ((nuint)2 << 20);
            mi_collect_reduce(2 * segmentSize);

            bool abandoned = false;
            for (int i = 0; i < allocated; i++)
            {
                void* p = blocks[i];
                Assert.That(((byte*)p)[0], Is.EqualTo((byte)((i % 251) + 1)));
                abandoned |= !mi_heap_contains_block(heap, p);
            }

            Assert.That(abandoned, Is.True);
        }
        finally
        {
            for (int i = 0; i < allocated; i++)
            {
                mi_free(blocks[i]);
            }

            mi_collect(force: true);
            mi_option_set(option, previous);
        }
    }

    /// <summary>Validates creation of a tagged heap with both destruction policies.</summary>
    [TestCase(true)]
    [TestCase(false)]
    public static void HeapNewExTest(bool allowDestroy)
    {
        IntPtr heap = mi_heap_new_ex(heap_tag: 7, allow_destroy: allowDestroy, arena_id: 0);
        Assert.That(heap, Is.Not.EqualTo(IntPtr.Zero));
        void* p = null;

        try
        {
            p = mi_heap_malloc(heap, 64);
            Assert.That((nuint)p, Is.Not.Zero);
        }
        finally
        {
            mi_free(p);

            if (allowDestroy)
            {
                mi_heap_destroy(heap);
            }
            else
            {
                mi_heap_delete(heap);
            }
        }
    }

    /// <summary>Validates that allocator fallback to OS memory can be disabled.</summary>
    [Test]
    public static void LimitOSAllocationTest()
    {
        if (!Environment.Is64BitProcess)
        {
            Assert.Ignore("The test allocation exceeds the 32-bit address space.");
        }

        mi_option_t option = mi_option_t.mi_option_disallow_os_alloc;
        int previous = mi_option_get(option);
        mi_option_set(option, 1);
        void* p = null;

        try
        {
            p = mi_malloc((nuint)64 << 30);
            Assert.That((nuint)p == 0, Is.True);
            Assert.That(get_last_errno(), Is.EqualTo(12));
        }
        finally
        {
            mi_free(p);
            mi_option_set(option, previous);
        }
    }

    /// <summary>Validates oversized OS-memory registrations are rejected before size arithmetic.</summary>
    [Test]
    public static void ArenaMemorySizeLimitTest()
    {
        int arenaId = -1;
        bool managed = mi_manage_os_memory_ex(null, nuint.MaxValue, is_committed: false, is_large: false, is_zero: false, numa_node: -1, exclusive: false, arena_id: &arenaId);
        Assert.That(managed, Is.False);
        Assert.That(arenaId, Is.Zero);
        Assert.That(mi_reserve_os_memory(nuint.MaxValue, commit: false, allow_large: false), Is.EqualTo(12));
    }

    /// <summary>Validates that arena allocation can be disabled independently of OS allocation.</summary>
    [Test]
    public static void DisallowArenaAllocationTest()
    {
        Assert.That(mi_reserve_os_memory((nuint)64 << 20, commit: false, allow_large: false), Is.Zero);

        IntPtr arenaHeap = IntPtr.Zero;
        IntPtr noArenaHeap = IntPtr.Zero;
        mi_option_t disallowOSOption = mi_option_t.mi_option_disallow_os_alloc;
        mi_option_t disallowArenaOption = mi_option_t.mi_option_disallow_arena_alloc;
        int previousDisallowOS = mi_option_get(disallowOSOption);
        int previousDisallowArena = mi_option_get(disallowArenaOption);
        void* arenaAllocation = null;
        void* noArenaAllocation = null;

        try
        {
            arenaHeap = mi_heap_new();
            noArenaHeap = mi_heap_new();
            Assert.That(arenaHeap, Is.Not.EqualTo(IntPtr.Zero));
            Assert.That(noArenaHeap, Is.Not.EqualTo(IntPtr.Zero));

            mi_option_set(disallowOSOption, 1);
            mi_option_set(disallowArenaOption, 0);

            arenaAllocation = mi_heap_malloc(arenaHeap, (nuint)8 << 20);
            Assert.That((nuint)arenaAllocation, Is.Not.Zero);

            mi_option_set(disallowArenaOption, 1);
            noArenaAllocation = mi_heap_malloc(noArenaHeap, (nuint)16 << 20);
            Assert.That((nuint)noArenaAllocation == 0, Is.True);
        }
        finally
        {
            mi_free(noArenaAllocation);
            mi_free(arenaAllocation);
            mi_option_set(disallowArenaOption, previousDisallowArena);
            mi_option_set(disallowOSOption, previousDisallowOS);
            if (noArenaHeap != IntPtr.Zero)
            {
                mi_heap_destroy(noArenaHeap);
            }

            if (arenaHeap != IntPtr.Zero)
            {
                mi_heap_destroy(arenaHeap);
            }
            mi_collect(force: true);
        }
    }

    /// <summary>Validates conversion of size-valued options from KiB to bytes.</summary>
    [Test]
    public static void OptionGetSizeTest()
    {
        mi_option_t option = mi_option_t.mi_option_arena_reserve;
        int previous = mi_option_get(option);

        try
        {
            mi_option_set(option, 1024);
            Assert.That(mi_option_get_size(option), Is.EqualTo((nuint)1 << 20));

            mi_option_set(option, -1);
            Assert.That(mi_option_get_size(option), Is.EqualTo((nuint)0));

            mi_option_set(option, int.MaxValue);
            nuint maximumKib = (nuint)int.MaxValue;
            nuint expectedMaximum = (maximumKib > (nuint.MaxValue / 1024)) ? (nuint)nint.MaxValue : (maximumKib * 1024);
            Assert.That(mi_option_get_size(option), Is.EqualTo(expectedMaximum));
        }
        finally
        {
            mi_option_set(option, previous);
        }
    }

    /// <summary>Validates defaults for native-only options match the unguarded, single-process build.</summary>
    [Test]
    public static void NativeOnlyOptionDefaultsTest()
    {
        string[] option_names = [
            "abandoned_reclaim_on_free",
            "visit_abandoned",
            "guarded_min",
            "guarded_max",
            "guarded_precise",
            "guarded_sample_rate",
            "guarded_sample_seed",
        ];
        foreach (string option_name in option_names)
        {
            if (Environment.GetEnvironmentVariable($"mimalloc_{option_name}") is not null)
            {
                Assert.Ignore("Native-only option defaults are overridden by the process environment.");
            }
        }

        Assert.That(mi_option_get(mi_option_t.mi_option_abandoned_reclaim_on_free), Is.EqualTo(0));
        Assert.That(mi_option_get(mi_option_t.mi_option_visit_abandoned), Is.EqualTo(0));
        Assert.That(mi_option_get(mi_option_t.mi_option_guarded_min), Is.EqualTo(0));
        Assert.That(mi_option_get(mi_option_t.mi_option_guarded_max), Is.EqualTo(1 << 30));
        Assert.That(mi_option_get(mi_option_t.mi_option_guarded_precise), Is.EqualTo(0));
        Assert.That(mi_option_get(mi_option_t.mi_option_guarded_sample_rate), Is.EqualTo(0));
        Assert.That(mi_option_get(mi_option_t.mi_option_guarded_sample_seed), Is.EqualTo(0));
    }

    /// <summary>Validates integer options match strtol leading- and trailing-whitespace behavior.</summary>
    [Test]
    public static void OptionIntegerWhitespaceParsingTest()
    {
        int expected;
        switch (Environment.GetEnvironmentVariable("mimalloc_max_errors"))
        {
            case " 7":
                expected = 7;
                break;

            case "7 ":
                expected = 32;
                break;

            default:
                Assert.Ignore("Run with mimalloc_max_errors set to ' 7' or '7 ' to verify strict numeric parsing.");
                return;
        }

        Assert.That(mi_option_get(mi_option_t.mi_option_max_errors), Is.EqualTo(expected));
    }

    /// <summary>Validates supported aligned-allocation boundaries.</summary>
    [Test]
    public static void AlignedAllocationTest()
    {
        for (nuint alignment = MI_ALIGNED_MAX; alignment <= (8 * MI_ALIGNED_MAX); alignment *= 2)
        {
            void* aligned = mi_malloc_aligned(1024, alignment);
            try
            {
                Assert.That((nuint)aligned, Is.Not.Zero);
                Assert.That((nuint)aligned % alignment, Is.EqualTo((nuint)0));
            }
            finally
            {
                mi_free(aligned);
            }
        }

        nuint largeAlignment = (nuint)64 * 1024;
        void* zeroSized = mi_malloc_aligned(0, largeAlignment);
        try
        {
            Assert.That((nuint)zeroSized, Is.Not.Zero);
            Assert.That((nuint)zeroSized % largeAlignment, Is.EqualTo((nuint)0));
        }
        finally
        {
            mi_free(zeroSized);
        }

        Assert.That((nuint)mi_malloc_aligned(1024, 24), Is.EqualTo((nuint)0));
        Assert.That((nuint)mi_malloc_aligned(nuint.MaxValue, 16), Is.EqualTo((nuint)0));

        void* p = mi_aligned_alloc(64, 100);
        try
        {
            Assert.That((nuint)p, Is.Not.Zero);
            Assert.That((nuint)p % 64, Is.EqualTo((nuint)0));
        }
        finally
        {
            mi_free(p);
        }
    }

    /// <summary>Validates allocator errors set errno when no prior errno is available.</summary>
    [Test]
    public static void AllocatorErrorSetsErrnoWhenUnsetTest()
    {
        if (get_last_errno() != 0)
        {
            Assert.Ignore("The process has already recorded an allocator errno.");
        }

        Assert.That((nuint)mi_malloc_aligned(8, 3), Is.EqualTo((nuint)0));
        Assert.That(get_last_errno(), Is.EqualTo(22));
    }

    /// <summary>Validates zero-sized aligned allocation as the first allocator call on a thread.</summary>
    [Test]
    public static void ZeroSizedAlignedAllocationOnNewThreadTest()
    {
        bool allocations_valid = false;
        Thread worker = new(() =>
        {
            void* p = mi_malloc_aligned(0, 16);
            bool valid = ((nuint)p != 0) && (((nuint)p % 16) == 0);
            mi_free(p);

            p = mi_zalloc_aligned(0, 32);
            valid &= ((nuint)p != 0) && (((nuint)p % 32) == 0);
            mi_free(p);
            mi_thread_done();
            allocations_valid = valid;
        });

        worker.Start();
        worker.Join();

        Assert.That(allocations_valid, Is.True);
    }

    /// <summary>Validates that POSIX aligned allocation rejects a zero alignment.</summary>
    [Test]
    public static void PosixMemalignRejectsZeroAlignmentTest()
    {
        void* p = (void*)1;

        Assert.That(mi_posix_memalign(&p, 0, 64), Is.EqualTo(22));
        Assert.That((nuint)p, Is.EqualTo((nuint)1));
    }

    /// <summary>Validates alignment across size classes and free-list reuse.</summary>
    [TestCase(13, 8)]
    [TestCase(129, 32)]
    [TestCase(1024, 64)]
    [TestCase(8191, 4096)]
    [TestCase(16384, 8192)]
    public static void SmallAlignedAllocationTest(int size, int alignment)
    {
        void** allocations = stackalloc void*[10];
        nuint allocSize = (nuint)size;
        nuint allocAlignment = (nuint)alignment;
        int allocated = 0;

        try
        {
            for (int i = 0; i < 10; i++)
            {
                allocations[i] = mi_malloc_aligned(allocSize, allocAlignment);
                allocated++;
                Assert.That((nuint)allocations[i], Is.Not.Zero);
                Assert.That((nuint)allocations[i] % allocAlignment, Is.EqualTo((nuint)0));
            }
        }
        finally
        {
            for (int i = 0; i < allocated; i++)
            {
                mi_free(allocations[i]);
            }
        }
    }

    /// <summary>Validates that a huge aligned block can be freed by a non-owning thread.</summary>
    [Test]
    public static void HugeAlignedCrossThreadFreeTest()
    {
        void* p = mi_malloc_aligned(1024, 4 * MI_ALIGNED_MAX);
        Assert.That((nuint)p, Is.Not.Zero);
        Assert.That((nuint)p % (4 * MI_ALIGNED_MAX), Is.EqualTo((nuint)0));

        ((byte*)p)[0] = 0xA5;
        ((byte*)p)[1023] = 0x5A;

        Thread worker = new(() =>
        {
            mi_free(p);
            mi_thread_done();
        });

        worker.Start();
        worker.Join();
        mi_collect(force: true);
    }

    /// <summary>Validates ownership for a large allocation on an explicitly created heap.</summary>
    [Test]
    public static void HeapAlignedLargeAllocationTest()
    {
        IntPtr heap = mi_heap_new();

        try
        {
            Assert.That(heap, Is.Not.EqualTo(IntPtr.Zero));

            void* newAllocation = mi_heap_alloc_new(heap, 128);
            Assert.That((nuint)newAllocation, Is.Not.Zero);
            Assert.That(mi_heap_contains_block(heap, newAllocation), Is.True);
            mi_free(newAllocation);

            void* newArray = mi_heap_alloc_new_n(heap, 4, 32);
            Assert.That((nuint)newArray, Is.Not.Zero);
            Assert.That(mi_heap_contains_block(heap, newArray), Is.True);
            mi_free(newArray);

            const nuint size = 33554426;
            void* p = mi_heap_malloc_aligned(heap, size, 8);

            Assert.That((nuint)p, Is.Not.Zero);
            Assert.That((nuint)p % 8, Is.EqualTo((nuint)0));
            Assert.That(mi_heap_contains_block(heap, p), Is.True);
            Assert.That(mi_usable_size(p), Is.GreaterThanOrEqualTo(size));
        }
        finally
        {
            mi_heap_destroy(heap);
        }
    }

    /// <summary>Validates that zero-size reallocations return an allocation or null on error.</summary>
    [Test]
    public static void ReallocZeroSizeTest()
    {
        void* p = mi_realloc(null, 0);
        Assert.That((nuint)p, Is.Not.Zero);
        mi_free(p);

        p = mi_malloc(16);
        Assert.That((nuint)p, Is.Not.Zero);
        ((byte*)p)[0] = 0xA5;

        void* q = mi_realloc(p, 0);
        Assert.That((nuint)q, Is.Not.Zero);
        Assert.That(((byte*)q)[0], Is.Zero);
        mi_free((q == null) ? p : q);
    }

    /// <summary>Validates array reallocation error handling and zero-count cleanup.</summary>
    [Test]
    public static void ReallocArrayPointerTest()
    {
        Assert.That(mi_reallocarr(null, 1, 1), Is.Not.Zero);

        void* p = mi_malloc(32);
        try
        {
            Assert.That((nuint)p, Is.Not.Zero);
            ((byte*)p)[0] = 0xA5;
            void* original = p;

            Assert.That(mi_reallocarr(&p, 1, 0), Is.Not.Zero);
            Assert.That((nuint)p, Is.EqualTo((nuint)original));

            Assert.That(mi_reallocarr(&p, nuint.MaxValue, 2), Is.Not.Zero);
            Assert.That((nuint)p, Is.EqualTo((nuint)original));
            Assert.That(((byte*)p)[0], Is.EqualTo((byte)0xA5));

            Assert.That(mi_reallocarr(&p, 0, 1), Is.Zero);
            Assert.That((nuint)p, Is.EqualTo((nuint)0));
        }
        finally
        {
            mi_free(p);
        }
    }

    /// <summary>Validates memalign rejects zero and non-power-of-two alignments.</summary>
    [Test]
    public static void MemalignRejectsInvalidAlignmentTest()
    {
        Assert.That((nuint)mi_memalign(0, 32), Is.EqualTo((nuint)0));
        Assert.That((nuint)mi_memalign(3, 32), Is.EqualTo((nuint)0));
    }

    /// <summary>Validates that visiting an empty heap completes successfully.</summary>
    [Test]
    public static void VisitEmptyHeapTest()
    {
        IntPtr heap = mi_heap_new();
        Assert.That(heap, Is.Not.EqualTo(IntPtr.Zero));

        try
        {
            Assert.That(mi_heap_visit_blocks(heap, true, visit_heap_block, null), Is.True);
        }
        finally
        {
            mi_heap_destroy(heap);
        }
    }

    /// <summary>Validates that zeroed reallocation initializes the entire usable block.</summary>
    [Test]
    public static void ReallocationZeroesEntireUsableBlockTest()
    {
        const nuint old_size = 100;
        const nuint new_size = 8192;
        byte* p = (byte*)mi_malloc(old_size);

        try
        {
            Assert.That((nuint)p, Is.Not.Zero);

            nuint old_usable = mi_usable_size(p);
            Assert.That(old_usable, Is.GreaterThanOrEqualTo(old_size));

            for (nuint i = 0; i < old_usable; i++)
            {
                p[i] = 0xA5;
            }

            byte* resized = (byte*)mi_rezalloc(p, new_size);
            Assert.That((nuint)resized, Is.Not.Zero);
            p = resized;

            nuint new_usable = mi_usable_size(p);
            Assert.That(new_usable, Is.GreaterThanOrEqualTo(new_size));

            bool preserved = true;
            for (nuint i = 0; i < old_usable; i++)
            {
                if (p[i] != 0xA5)
                {
                    preserved = false;
                    break;
                }
            }

            for (nuint i = old_usable; i < new_usable; i++)
            {
                if (p[i] != 0)
                {
                    preserved = false;
                    break;
                }
            }

            Assert.That(preserved, Is.True);
        }
        finally
        {
            mi_free(p);
        }
    }

    /// <summary>Validates zero-initialized allocations through small, generic, and huge-page paths.</summary>
    [TestCase(64)]
    [TestCase(1048576)]
    [TestCase(3145728)]
    public static void ZeroAllocationTest(int size)
    {
        byte* p = (byte*)mi_zalloc((nuint)size);

        try
        {
            Assert.That((nuint)p, Is.Not.Zero);

            bool allZero = true;
            for (int i = 0; i < size; i++)
            {
                if (p[i] != 0)
                {
                    allZero = false;
                    break;
                }
            }

            Assert.That(allZero, Is.True);
        }
        finally
        {
            mi_free(p);
        }
    }

    /// <summary>Validates heap-local small zero allocation and free APIs.</summary>
    [Test]
    public static void HeapSmallAllocationApiTest()
    {
        IntPtr heap = mi_heap_new();
        Assert.That(heap, Is.Not.EqualTo(IntPtr.Zero));

        byte* p = (byte*)mi_heap_zalloc_small(heap, 32);
        try
        {
            Assert.That((nuint)p, Is.Not.Zero);

            for (int i = 0; i < 32; i++)
            {
                Assert.That(p[i], Is.Zero);
            }

            Assert.That(mi_unsafe_heap_page_is_under_utilized(heap, p, 100), Is.False);

            mi_free_small(p);
            p = null;
        }
        finally
        {
            mi_free(p);
            mi_heap_destroy(heap);
        }
    }

    /// <summary>Validates sampled heap allocations place a protected page after the reported usable bytes.</summary>
    [Test]
    public static void HeapGuardedAllocationTest()
    {
        IntPtr heap = mi_heap_new();
        Assert.That(heap, Is.Not.EqualTo(IntPtr.Zero));
        mi_heap_guarded_set_size_bound(heap, 1, 256);
        mi_heap_guarded_set_sample_rate(heap, 1, 1);

        void* p = null;
        void* aligned = null;
        void* zero = null;
        void* zero_sized = null;
        void* cross_thread = null;
        try
        {
            p = mi_heap_malloc(heap, 64);
            Assert.That((nuint)p, Is.Not.Zero);

            nuint usable = mi_usable_size(p);
            Assert.That(usable, Is.GreaterThanOrEqualTo((nuint)64));
            Assert.That(is_noaccess_page((byte*)p + usable), Is.True);
            ((byte*)p)[0] = 0xA5;

            aligned = mi_heap_malloc_aligned(heap, 128, 64);
            Assert.That((nuint)aligned % 64, Is.EqualTo((nuint)0));
            nuint aligned_usable = mi_usable_size(aligned);
            Assert.That(aligned_usable, Is.GreaterThanOrEqualTo((nuint)128));
            Assert.That(is_noaccess_page((byte*)aligned + aligned_usable), Is.True);

            zero = mi_heap_zalloc(heap, 32);
            Assert.That((nuint)zero, Is.Not.Zero);
            Assert.That(((byte*)zero)[0], Is.EqualTo((byte)0));
            nuint zero_usable = mi_usable_size(zero);
            Assert.That(is_noaccess_page((byte*)zero + zero_usable), Is.True);

            zero_sized = mi_heap_malloc(heap, 0);
            Assert.That((nuint)zero_sized, Is.Not.Zero);
            nuint zero_sized_usable = mi_usable_size(zero_sized);
            Assert.That(is_noaccess_page((byte*)zero_sized + zero_sized_usable), Is.True);

            cross_thread = mi_heap_malloc(heap, 48);
            Assert.That((nuint)cross_thread, Is.Not.Zero);
            IntPtr cross_thread_pointer = (IntPtr)cross_thread;
            Thread freeing_thread = new(() => mi_free((void*)cross_thread_pointer));
            freeing_thread.Start();
            freeing_thread.Join();
            cross_thread = null;
            mi_heap_collect(heap, force: true);

            void* resized = mi_heap_realloc(heap, p, 128);
            Assert.That((nuint)resized, Is.Not.Zero);
            p = resized;
            Assert.That(((byte*)p)[0], Is.EqualTo((byte)0xA5));
            nuint resized_usable = mi_usable_size(p);
            Assert.That(resized_usable, Is.GreaterThanOrEqualTo((nuint)128));
            Assert.That(is_noaccess_page((byte*)p + resized_usable), Is.True);

        }
        finally
        {
            mi_free(p);
            mi_free(aligned);
            mi_free(zero);
            mi_free(zero_sized);
            mi_free(cross_thread);
            mi_heap_destroy(heap);
        }
    }

    /// <summary>Validates subprocess heaps do not cross-reclaim and can visit their abandoned blocks.</summary>
    [Test]
    public static void SubprocessAbandonedVisitTest()
    {
        int previous_visit = mi_option_get(mi_option_t.mi_option_visit_abandoned);
        mi_option_set(mi_option_t.mi_option_visit_abandoned, 1);
        IntPtr subproc = mi_subproc_new();

        void* abandoned = null;
        int areas = 0;
        int blocks = 0;
        void* visited_block = null;
        mi_block_visit_fun visitor = (heap, area, block, block_size, arg) =>
        {
            if (block == null)
            {
                areas++;
            }
            else
            {
                blocks++;
                visited_block = block;
            }
            return true;
        };

        try
        {
            Assert.That(subproc, Is.Not.EqualTo(IntPtr.Zero));

            Thread owner = new(() =>
            {
                mi_subproc_add_current_thread(subproc);
                abandoned = mi_malloc(64);
                if (abandoned != null)
                {
                    ((byte*)abandoned)[0] = 0xA5;
                }
                mi_thread_done();
            });
            owner.Start();
            owner.Join();

            Assert.That((nuint)abandoned, Is.Not.Zero);
            mi_subproc_delete(subproc);
            Assert.That(mi_abandoned_visit_blocks(subproc, -1, true, visitor, null), Is.True);
            Assert.That(areas, Is.GreaterThan(0));
            Assert.That(blocks, Is.GreaterThan(0));
            Assert.That((nuint)visited_block, Is.EqualTo((nuint)abandoned));
            Assert.That(((byte*)visited_block)[0], Is.EqualTo((byte)0xA5));

            mi_free(abandoned);
            abandoned = null;
        }
        finally
        {
            mi_free(abandoned);

            if (subproc != IntPtr.Zero)
            {
                Thread collector = new(() =>
                {
                    mi_subproc_add_current_thread(subproc);
                    mi_collect(force: true);
                    mi_thread_done();
                });
                collector.Start();
                collector.Join();

                mi_subproc_delete(subproc);
            }
            mi_option_set(mi_option_t.mi_option_visit_abandoned, previous_visit);
        }
    }

    /// <summary>Validates abandoned visitation does not expose blocks from a different subprocess.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public static void SubprocessAbandonedVisitFiltersSubprocessTest(bool disallow_arena_alloc)
    {
        int previous_visit = mi_option_get(mi_option_t.mi_option_visit_abandoned);
        int previous_disallow_arena_alloc = mi_option_get(mi_option_t.mi_option_disallow_arena_alloc);
        mi_option_set(mi_option_t.mi_option_visit_abandoned, 1);
        mi_option_set(mi_option_t.mi_option_disallow_arena_alloc, disallow_arena_alloc ? 1 : 0);
        IntPtr first_subproc = mi_subproc_new();
        IntPtr second_subproc = mi_subproc_new();

        void* first_block = null;
        void* second_block = null;
        void* visited = null;
        int visited_count = 0;
        mi_block_visit_fun visitor = (heap, area, block, block_size, arg) =>
        {
            if (block != null)
            {
                visited = block;
                visited_count++;
            }
            return true;
        };

        void create_abandoned_block(IntPtr subproc, byte value, out void* block)
        {
            IntPtr result = IntPtr.Zero;
            Thread worker = new(() =>
            {
                mi_subproc_add_current_thread(subproc);
                void* allocated = mi_malloc(64);
                result = (IntPtr)allocated;
                if (allocated != null)
                {
                    ((byte*)allocated)[0] = value;
                }
                mi_thread_done();
            });
            worker.Start();
            worker.Join();
            block = (void*)result;
        }

        void collect_subproc(IntPtr subproc)
        {
            Thread collector = new(() =>
            {
                mi_subproc_add_current_thread(subproc);
                mi_collect(force: true);
                mi_thread_done();
            });
            collector.Start();
            collector.Join();
        }

        try
        {
            Assert.That(first_subproc, Is.Not.EqualTo(IntPtr.Zero));
            Assert.That(second_subproc, Is.Not.EqualTo(IntPtr.Zero));

            create_abandoned_block(first_subproc, 0xA5, out first_block);
            create_abandoned_block(second_subproc, 0x5A, out second_block);
            Assert.That((nuint)first_block, Is.Not.Zero);
            Assert.That((nuint)second_block, Is.Not.Zero);

            visited = null;
            visited_count = 0;
            Assert.That(mi_abandoned_visit_blocks(first_subproc, -1, true, visitor, null), Is.True);
            Assert.That(visited_count, Is.EqualTo(1));
            Assert.That((nuint)visited, Is.EqualTo((nuint)first_block));
            Assert.That(((byte*)visited)[0], Is.EqualTo((byte)0xA5));

            visited = null;
            visited_count = 0;
            Assert.That(mi_abandoned_visit_blocks(second_subproc, -1, true, visitor, null), Is.True);
            Assert.That(visited_count, Is.EqualTo(1));
            Assert.That((nuint)visited, Is.EqualTo((nuint)second_block));
            Assert.That(((byte*)visited)[0], Is.EqualTo((byte)0x5A));

            mi_free(first_block);
            first_block = null;
            mi_free(second_block);
            second_block = null;
            collect_subproc(first_subproc);
            collect_subproc(second_subproc);
        }
        finally
        {
            mi_free(first_block);
            mi_free(second_block);
            if (first_subproc != IntPtr.Zero)
            {
                collect_subproc(first_subproc);
                mi_subproc_delete(first_subproc);
            }
            if (second_subproc != IntPtr.Zero)
            {
                collect_subproc(second_subproc);
                mi_subproc_delete(second_subproc);
            }
            mi_option_set(mi_option_t.mi_option_disallow_arena_alloc, previous_disallow_arena_alloc);
            mi_option_set(mi_option_t.mi_option_visit_abandoned, previous_visit);
        }
    }

    /// <summary>Validates mimalloc v3 thread-heap aliases forward to the ordinary heap APIs.</summary>
    [Test]
    public static void ThreadHeapCompatibilityAliasesTest()
    {
        IntPtr heap = mi_theap_get_default();
        Assert.That(heap, Is.Not.EqualTo(IntPtr.Zero));
        Assert.That(mi_theap_set_default(heap), Is.Not.EqualTo(IntPtr.Zero));

        void* p = mi_theap_malloc(heap, 32);
        Assert.That((nuint)p, Is.Not.Zero);

        void* resized = null;
        try
        {
            resized = mi_theap_realloc(heap, p, 64);
            Assert.That((nuint)resized, Is.Not.Zero);
            p = null;

            void* zeroed = mi_theap_calloc(heap, 2, 8);
            Assert.That((nuint)zeroed, Is.Not.Zero);
            try
            {
                Assert.That(((byte*)zeroed)[0], Is.EqualTo((byte)0));
            }
            finally
            {
                mi_free(zeroed);
            }

            void* zero = mi_theap_zalloc(heap, 8);
            Assert.That((nuint)zero, Is.Not.Zero);
            try
            {
                Assert.That(((byte*)zero)[0], Is.EqualTo((byte)0));
            }
            finally
            {
                mi_free(zero);
            }

            void* small = mi_theap_malloc_small(heap, 8);
            Assert.That((nuint)small, Is.Not.Zero);
            mi_free(small);

            void* aligned = mi_theap_malloc_aligned(heap, 128, 64);
            Assert.That((nuint)aligned % 64, Is.EqualTo((nuint)0));
            mi_free(aligned);
        }
        finally
        {
            mi_free((resized != null) ? resized : p);
        }

        mi_theap_collect(heap, force: false);
    }

    /// <summary>Validates zero initialization across aligned reallocation.</summary>
    [Test]
    public static void AlignedZeroReallocationTest()
    {
        nuint size = MI_SMALL_SIZE_MAX / 2;
        nuint alignment = MI_ALIGNED_MAX * 2;
        byte* p = (byte*)mi_zalloc_aligned(size, alignment);

        try
        {
            Assert.That((nuint)p, Is.Not.Zero);
            Assert.That((nuint)p % alignment, Is.EqualTo((nuint)0));

            bool allZero = true;
            for (nuint i = 0; i < size; i++)
            {
                if (p[i] != 0)
                {
                    allZero = false;
                    break;
                }
            }
            Assert.That(allZero, Is.True);

            p[size - 1] = 0xA5;

            nuint new_size = size * 3;
            byte* resized = (byte*)mi_rezalloc_aligned(p, new_size, alignment);
            Assert.That((nuint)resized, Is.Not.Zero);
            p = resized;

            bool preserved = p[size - 1] == 0xA5;
            for (nuint i = 0; i < size - 1; i++)
            {
                if (p[i] != 0)
                {
                    preserved = false;
                    break;
                }
            }

            for (nuint i = size; i < new_size; i++)
            {
                if (p[i] != 0)
                {
                    preserved = false;
                    break;
                }
            }

            nuint usable_size = mi_usable_size(p);
            for (nuint i = new_size; i < usable_size; i++)
            {
                if (p[i] != 0)
                {
                    preserved = false;
                    break;
                }
            }
            Assert.That(preserved, Is.True);
        }
        finally
        {
            mi_free(p);
        }
    }

    /// <summary>Validates that aligned reallocation without an offset returns a naturally aligned pointer.</summary>
    [Test]
    public static void AlignedReallocationUsesZeroOffsetTest()
    {
        const nuint size = 128;
        const nuint alignment = 64;
        const nuint offset = 8;

        void* p = mi_malloc_aligned_at(size, alignment, offset);
        Assert.That((nuint)p, Is.Not.Zero);
        Assert.That(((nuint)p + offset) % alignment, Is.EqualTo((nuint)0));
        Assert.That((nuint)p % alignment, Is.Not.EqualTo((nuint)0));
        ((byte*)p)[0] = 0xA5;

        void* resized = null;
        try
        {
            resized = mi_realloc_aligned(p, size, alignment);
            Assert.That((nuint)resized, Is.Not.Zero);
            p = null;

            Assert.That((nuint)resized % alignment, Is.EqualTo((nuint)0));
            Assert.That(((byte*)resized)[0], Is.EqualTo((byte)0xA5));
        }
        finally
        {
            mi_free((resized != null) ? resized : p);
        }
    }

    /// <summary>Validates offset-aligned reallocation preserves data for unaligned block pointers.</summary>
    [Test]
    public static void AlignedAtReallocationPreservesContentsTest()
    {
        const nuint size = 128;
        const nuint newSize = 256;
        const nuint alignment = 64;
        const nuint offset = 1;

        void* p = mi_malloc_aligned_at(size, alignment, offset);
        Assert.That((nuint)p, Is.Not.Zero);
        Assert.That(((nuint)p + offset) % alignment, Is.EqualTo((nuint)0));
        Assert.That((nuint)p % (nuint)sizeof(nuint), Is.Not.Zero);
        NativeMemory.Fill(p, size, 0xA5);

        void* resized = null;
        try
        {
            resized = mi_realloc_aligned_at(p, newSize, alignment, offset);
            Assert.That((nuint)resized, Is.Not.Zero);
            p = null;

            Assert.That(((nuint)resized + offset) % alignment, Is.EqualTo((nuint)0));
            Assert.That((nuint)resized % (nuint)sizeof(nuint), Is.Not.Zero);
            for (nuint i = 0; i < size; i++)
            {
                Assert.That(((byte*)resized)[i], Is.EqualTo((byte)0xA5));
            }
        }
        finally
        {
            mi_free((resized != null) ? resized : p);
        }
    }

    /// <summary>Validates invalid aligned reallocations fail without freeing the original allocation.</summary>
    [Test]
    public static void AlignedReallocationRejectsInvalidAlignmentTest()
    {
        void* p = mi_malloc(64);
        Assert.That((nuint)p, Is.Not.Zero);

        try
        {
            Assert.That((nuint)mi_realloc_aligned(p, 32, 0), Is.EqualTo((nuint)0));
            Assert.That(mi_usable_size(p), Is.GreaterThanOrEqualTo((nuint)64));
        }
        finally
        {
            mi_free(p);
        }
    }

    /// <summary>Validates allocations that return the underlying block size.</summary>
    [Test]
    public static void BlockSizeAllocationTest()
    {
        nuint blockSize = 0;
        void* p = mi_umalloc(100, &blockSize);
        nuint blockSizePre = 0;
        nuint blockSizePost = 0;
        void* resized = null;
        try
        {
            Assert.That((nuint)p, Is.Not.Zero);
            Assert.That(blockSize, Is.GreaterThanOrEqualTo((nuint)100));
            Assert.That(blockSize, Is.GreaterThanOrEqualTo(mi_usable_size(p)));
            ((byte*)p)[0] = 0xA5;

            resized = mi_urealloc(p, 1024, &blockSizePre, &blockSizePost);
            if (resized != null)
            {
                p = null;
            }

            Assert.That((nuint)resized, Is.Not.Zero);
            Assert.That(blockSizePre, Is.GreaterThanOrEqualTo((nuint)100));
            Assert.That(blockSizePost, Is.GreaterThanOrEqualTo((nuint)1024));
            Assert.That(((byte*)resized)[0], Is.EqualTo((byte)0xA5));
        }
        finally
        {
            if (resized != null)
            {
                nuint freedBlockSize = 0;
                mi_ufree(resized, &freedBlockSize);
                Assert.That(freedBlockSize, Is.EqualTo(blockSizePost));
            }
            else
            {
                mi_free(p);
            }
        }

        void* zero = mi_ucalloc(8, 32, &blockSize);
        try
        {
            Assert.That((nuint)zero, Is.Not.Zero);
            Assert.That(blockSize, Is.GreaterThanOrEqualTo((nuint)256));
            Assert.That(((byte*)zero)[255], Is.EqualTo((byte)0));
        }
        finally
        {
            mi_ufree(zero, null);
        }

        nuint alignment = 64;
        void* aligned = mi_umalloc_aligned(129, alignment, &blockSize);
        try
        {
            Assert.That((nuint)aligned, Is.Not.Zero);
            Assert.That((nuint)aligned % alignment, Is.EqualTo((nuint)0));
            Assert.That(blockSize, Is.GreaterThanOrEqualTo((nuint)129));
        }
        finally
        {
            mi_ufree(aligned, null);
        }

        void* zeroAligned = mi_uzalloc_aligned(129, alignment, &blockSize);
        try
        {
            Assert.That((nuint)zeroAligned, Is.Not.Zero);
            Assert.That((nuint)zeroAligned % alignment, Is.EqualTo((nuint)0));
            Assert.That(blockSize, Is.GreaterThanOrEqualTo((nuint)129));
            Assert.That(((byte*)zeroAligned)[128], Is.EqualTo((byte)0));
        }
        finally
        {
            mi_ufree(zeroAligned, null);
        }

        void* smallAllocation = mi_umalloc_small(64, &blockSize);
        try
        {
            Assert.That((nuint)smallAllocation, Is.Not.Zero);
            Assert.That(blockSize, Is.GreaterThanOrEqualTo((nuint)64));
        }
        finally
        {
            mi_ufree(smallAllocation, null);
        }

        void* small = mi_uzalloc_small(64, &blockSize);
        try
        {
            Assert.That((nuint)small, Is.Not.Zero);
            Assert.That(blockSize, Is.GreaterThanOrEqualTo((nuint)64));
            Assert.That(((byte*)small)[0], Is.EqualTo((byte)0));
        }
        finally
        {
            mi_ufree(small, null);
        }
    }

    /// <summary>Validates allocator thread teardown while thread metadata is shared through the cache.</summary>
    [Test]
    public static void ThreadMetadataCacheTest()
    {
        using CountdownEvent ready = new(2);
        using ManualResetEventSlim resume = new();
        int allocationFailures = 0;

        void worker(byte value)
        {
            try
            {
                void* p = mi_malloc(64);

                if (p == null)
                {
                    Interlocked.Increment(ref allocationFailures);
                }
                else
                {
                    ((byte*)p)[0] = value;
                    mi_free(p);
                }
            }
            finally
            {
                ready.Signal();
                resume.Wait();
                mi_thread_done();
                mi_thread_done();
            }
        }

        Thread first = new(() => worker(0xA5));
        Thread second = new(() => worker(0x5A));
        first.Start();
        second.Start();

        bool bothReady = ready.Wait(TimeSpan.FromSeconds(10));
        resume.Set();
        first.Join();
        second.Join();

        Assert.That(bothReady, Is.True);
        Assert.That(allocationFailures, Is.Zero);
    }

    /// <summary>Validates that freeing an abandoned block reclaims its segment.</summary>
    [Test]
    public static void ReclaimAbandonedSegmentOnFreeTest()
    {
        IntPtr heap = mi_heap_get_default();
        mi_option_t reclaimOption = mi_option_t.mi_option_abandoned_reclaim_on_free;
        int previousReclaim = mi_option_get(reclaimOption);
        mi_option_set(reclaimOption, 1);
        void* p = null;

        try
        {
            Thread worker = new(() =>
            {
                p = mi_malloc(64);

                if (p != null)
                {
                    ((byte*)p)[0] = 0xA5;
                }

                mi_thread_done();
            });

            worker.Start();
            worker.Join();

            Assert.That((nuint)p, Is.Not.Zero);
            Assert.That(mi_heap_contains_block(heap, p), Is.False);

            void* freed = p;
            mi_free(p);
            p = null;

            Assert.That(mi_heap_contains_block(heap, freed), Is.True);
        }
        finally
        {
            mi_free(p);
            mi_option_set(reclaimOption, previousReclaim);
        }
    }

    /// <summary>Validates that an OS-backed abandoned segment can be reclaimed for a new allocation.</summary>
    [Test]
    public static void ReclaimAbandonedOSSegmentOnAllocationTest()
    {
        mi_collect(force: true);
        nuint segmentSize = Environment.Is64BitProcess ? ((nuint)4 << 20) : ((nuint)2 << 20);
        nuint largeSize = (segmentSize / 4) - 8192;

        mi_option_t option = mi_option_t.mi_option_disallow_arena_alloc;
        int previous = mi_option_get(option);
        mi_option_t reclaimOption = mi_option_t.mi_option_max_segment_reclaim;
        int previousReclaim = mi_option_get(reclaimOption);
        mi_option_set(option, 1);
        mi_option_set(reclaimOption, 100);

        IntPtr heap = IntPtr.Zero;
        IntPtr abandoned = IntPtr.Zero;
        void* allocation = null;

        try
        {
            heap = mi_heap_new();
            Assert.That(heap, Is.Not.EqualTo(IntPtr.Zero));

            Thread worker = new(() =>
            {
                void* p = mi_malloc(largeSize);

                if (p != null)
                {
                    ((byte*)p)[0] = 0xA5;
                    abandoned = (IntPtr)p;
                }

                mi_thread_done();
            });

            worker.Start();
            worker.Join();

            Assert.That(abandoned, Is.Not.EqualTo(IntPtr.Zero));
            Assert.That(mi_heap_contains_block(heap, (void*)abandoned), Is.False);

            allocation = mi_heap_malloc(heap, largeSize);
            Assert.That((nuint)allocation, Is.Not.Zero);
            Assert.That(mi_heap_contains_block(heap, (void*)abandoned), Is.True);
            Assert.That(((byte*)abandoned)[0], Is.EqualTo((byte)0xA5));
        }
        finally
        {
            mi_free(allocation);
            mi_free((void*)abandoned);

            if (heap != IntPtr.Zero)
            {
                mi_heap_destroy(heap);
            }

            mi_option_set(option, previous);
            mi_option_set(reclaimOption, previousReclaim);
            mi_collect(force: true);
        }
    }

    /// <summary>Validates parsing the reserve-memory environment option.</summary>
    [Test]
    public static void ReserveOSMemoryOptionTest()
    {
        string? value = Environment.GetEnvironmentVariable("mimalloc_reserve_os_memory");

        if (!string.Equals(value, "32M", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Ignore("Run with mimalloc_reserve_os_memory=32M to verify the option parser.");
        }

        Assert.That(mi_option_get(mi_option_t.mi_option_reserve_os_memory), Is.EqualTo(32 * 1024));
    }
}
