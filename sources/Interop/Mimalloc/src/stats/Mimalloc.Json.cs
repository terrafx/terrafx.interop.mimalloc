// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the stats.c file from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace TerraFX.Interop.Mimalloc;

public static unsafe partial class Mimalloc
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static mi_stat_count_t mi_stats_snapshot(in mi_stat_count_internal_t stat)
        => new() { total = stat.allocated, peak = stat.peak, current = stat.current };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static mi_stat_counter_t mi_stats_snapshot(long total)
        => new() { total = total };

    public static partial bool mi_stats_get(mi_stats_t* stats)
    {
#pragma warning disable CS0420
        if ((stats == null) || (stats->size != (nuint)SizeOf<mi_stats_t>()) || (stats->version != (nuint)MI_STAT_VERSION))
        {
            return false;
        }

        ref readonly mi_stats_internal_t source = ref _mi_stats_main;
        mi_stats_t snapshot = default;
        snapshot.size = (nuint)SizeOf<mi_stats_t>();
        snapshot.version = (nuint)MI_STAT_VERSION;

        // Subprocess-only counters remain zero because subprocess ownership is not yet ported.
        snapshot.pages = mi_stats_snapshot(in source.pages);
        snapshot.reserved = mi_stats_snapshot(in source.reserved);
        snapshot.committed = mi_stats_snapshot(in source.committed);
        snapshot.reset = mi_stats_snapshot(source.reset.allocated);
        snapshot.purged = mi_stats_snapshot(source.purged.allocated);
        snapshot.page_committed = mi_stats_snapshot(in source.page_committed);
        snapshot.pages_abandoned = mi_stats_snapshot(in source.pages_abandoned);
        snapshot.threads = mi_stats_snapshot(in source.threads);
        snapshot.malloc_normal = mi_stats_snapshot(in source.normal);
        snapshot.malloc_huge = mi_stats_snapshot(in source.huge);
        snapshot.malloc_requested = mi_stats_snapshot(in source.malloc_requested);

        snapshot.mmap_calls = mi_stats_snapshot(source.mmap_calls.total);
        snapshot.commit_calls = mi_stats_snapshot(source.commit_calls.total);
        snapshot.reset_calls = mi_stats_snapshot(source.reset_calls.total);
        snapshot.purge_calls = mi_stats_snapshot(source.purge_calls.total);
        snapshot.arena_count = mi_stats_snapshot(source.arena_count.total);
        snapshot.malloc_normal_count = mi_stats_snapshot(source.normal_count.count);
        snapshot.malloc_huge_count = mi_stats_snapshot(source.huge_count.count);
        snapshot.malloc_guarded_count = mi_stats_snapshot(source.malloc_guarded_count.total);
        snapshot.pages_extended = mi_stats_snapshot(source.pages_extended.total);
        snapshot.arena_rollback_count = mi_stats_snapshot(source.arena_rollback_count.total);
        snapshot.arena_purges = mi_stats_snapshot(source.arena_purges.total);
        snapshot.pages_retire = mi_stats_snapshot(source.pages_retire.total);
        snapshot.page_searches = mi_stats_snapshot(source.searches.total);
        snapshot.page_searches_count = mi_stats_snapshot(source.searches.count);
        snapshot.segments = mi_stats_snapshot(in source.segments);
        snapshot.segments_abandoned = mi_stats_snapshot(in source.segments_abandoned);
        snapshot.segments_cache = mi_stats_snapshot(in source.segments_cache);

        for (nuint i = 0; i <= MI_BIN_HUGE; i++)
        {
            ref readonly mi_stat_count_internal_t malloc_bin = ref Unsafe.Add(ref Unsafe.AsRef(in source.normal_bins.e0), (nint)i);
            ref readonly mi_stat_count_internal_t page_bin = ref Unsafe.Add(ref Unsafe.AsRef(in source.page_bins.e0), (nint)i);
            ref mi_stat_count_t snapshot_malloc_bin = ref Unsafe.Add(ref snapshot.malloc_bins.e0, (nint)i);
            ref mi_stat_count_t snapshot_page_bin = ref Unsafe.Add(ref snapshot.page_bins.e0, (nint)i);

            snapshot_malloc_bin = mi_stats_snapshot(in malloc_bin);
            snapshot_page_bin = mi_stats_snapshot(in page_bin);
        }

        *stats = snapshot;
        return true;
#pragma warning restore CS0420
    }

    private static void mi_stats_json_write_count(Utf8JsonWriter writer, string name, in mi_stat_count_t stat)
    {
        writer.WritePropertyName(name);
        writer.WriteStartObject();
        writer.WriteNumber("total", stat.total);
        writer.WriteNumber("peak", stat.peak);
        writer.WriteNumber("current", stat.current);
        writer.WriteEndObject();
    }

    private static void mi_stats_json_write_counter(Utf8JsonWriter writer, string name, in mi_stat_counter_t stat)
        => writer.WriteNumber(name, stat.total);

    private static void mi_stats_json_write_bin(Utf8JsonWriter writer, in mi_stat_count_t stat, byte bin)
    {
        nuint block_size = _mi_bin_size(bin);
        nuint page_size = (block_size <= MI_SMALL_OBJ_SIZE_MAX)
            ? MI_SMALL_PAGE_SIZE
            : ((block_size <= MI_MEDIUM_OBJ_SIZE_MAX)
                ? MI_MEDIUM_PAGE_SIZE
                : ((block_size <= MI_LARGE_OBJ_SIZE_MAX) ? MI_LARGE_PAGE_SIZE : 0));

        writer.WriteStartObject();
        writer.WriteNumber("total", stat.total);
        writer.WriteNumber("peak", stat.peak);
        writer.WriteNumber("current", stat.current);
        writer.WriteNumber("block_size", (ulong)block_size);
        writer.WriteNumber("page_size", (ulong)page_size);
        writer.WriteEndObject();
    }

    public static partial sbyte* mi_stats_get_json(nuint buf_size, sbyte* buf)
    {
        if ((buf != null) && (buf_size > 0))
        {
            _ = memset(buf, 0, buf_size);
        }

        try
        {
            mi_stats_t stats = default;
            stats.size = (nuint)SizeOf<mi_stats_t>();
            stats.version = (nuint)MI_STAT_VERSION;

            if (!mi_stats_get(&stats))
            {
                return null;
            }

            ArrayBufferWriter<byte> output = new();
            using (Utf8JsonWriter writer = new(output, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                writer.WriteNumber("stat_version", MI_STAT_VERSION);
                writer.WriteNumber("mimalloc_version", MI_MALLOC_VERSION);

                writer.WritePropertyName("process");
                writer.WriteStartObject();
                nuint elapsed = 0;
                nuint user = 0;
                nuint system = 0;
                nuint rss_current = 0;
                nuint rss_peak = 0;
                nuint commit_current = 0;
                nuint commit_peak = 0;
                nuint page_faults = 0;
                mi_process_info(&elapsed, &user, &system, &rss_current, &rss_peak, &commit_current, &commit_peak, &page_faults);
                writer.WriteNumber("elapsed_msecs", (ulong)elapsed);
                writer.WriteNumber("user_msecs", (ulong)user);
                writer.WriteNumber("system_msecs", (ulong)system);
                writer.WriteNumber("page_faults", (ulong)page_faults);
                writer.WriteNumber("rss_current", (ulong)rss_current);
                writer.WriteNumber("rss_peak", (ulong)rss_peak);
                writer.WriteNumber("commit_current", (ulong)commit_current);
                writer.WriteNumber("commit_peak", (ulong)commit_peak);
                writer.WriteEndObject();

                mi_stats_json_write_count(writer, "pages", in stats.pages);
                mi_stats_json_write_count(writer, "reserved", in stats.reserved);
                mi_stats_json_write_count(writer, "committed", in stats.committed);
                mi_stats_json_write_counter(writer, "reset", in stats.reset);
                mi_stats_json_write_counter(writer, "purged", in stats.purged);
                mi_stats_json_write_count(writer, "page_committed", in stats.page_committed);
                mi_stats_json_write_count(writer, "pages_abandoned", in stats.pages_abandoned);
                mi_stats_json_write_count(writer, "threads", in stats.threads);
                mi_stats_json_write_count(writer, "malloc_normal", in stats.malloc_normal);
                mi_stats_json_write_count(writer, "malloc_huge", in stats.malloc_huge);
                mi_stats_json_write_count(writer, "malloc_requested", in stats.malloc_requested);
                mi_stats_json_write_counter(writer, "mmap_calls", in stats.mmap_calls);
                mi_stats_json_write_counter(writer, "commit_calls", in stats.commit_calls);
                mi_stats_json_write_counter(writer, "reset_calls", in stats.reset_calls);
                mi_stats_json_write_counter(writer, "purge_calls", in stats.purge_calls);
                mi_stats_json_write_counter(writer, "arena_count", in stats.arena_count);
                mi_stats_json_write_counter(writer, "malloc_normal_count", in stats.malloc_normal_count);
                mi_stats_json_write_counter(writer, "malloc_huge_count", in stats.malloc_huge_count);
                mi_stats_json_write_counter(writer, "malloc_guarded_count", in stats.malloc_guarded_count);
                mi_stats_json_write_counter(writer, "arena_rollback_count", in stats.arena_rollback_count);
                mi_stats_json_write_counter(writer, "arena_purges", in stats.arena_purges);
                mi_stats_json_write_counter(writer, "pages_extended", in stats.pages_extended);
                mi_stats_json_write_counter(writer, "pages_retire", in stats.pages_retire);
                mi_stats_json_write_counter(writer, "page_searches", in stats.page_searches);
                mi_stats_json_write_counter(writer, "page_searches_count", in stats.page_searches_count);
                mi_stats_json_write_count(writer, "segments", in stats.segments);
                mi_stats_json_write_count(writer, "segments_abandoned", in stats.segments_abandoned);
                mi_stats_json_write_count(writer, "segments_cache", in stats.segments_cache);
                mi_stats_json_write_count(writer, "_segments_reserved", in stats._segments_reserved);
                mi_stats_json_write_counter(writer, "pages_reclaim_on_alloc", in stats.pages_reclaim_on_alloc);
                mi_stats_json_write_counter(writer, "pages_reclaim_on_free", in stats.pages_reclaim_on_free);
                mi_stats_json_write_counter(writer, "pages_reabandon_full", in stats.pages_reabandon_full);
                mi_stats_json_write_counter(writer, "pages_unabandon_busy_wait", in stats.pages_unabandon_busy_wait);

                writer.WriteStartArray("malloc_bins");
                for (nuint i = 0; i <= MI_BIN_HUGE; i++)
                {
                    ref readonly mi_stat_count_t stat = ref Unsafe.Add(ref stats.malloc_bins.e0, (nint)i);
                    mi_stats_json_write_bin(writer, in stat, (byte)i);
                }
                writer.WriteEndArray();

                writer.WriteStartArray("page_bins");
                for (nuint i = 0; i <= MI_BIN_HUGE; i++)
                {
                    ref readonly mi_stat_count_t stat = ref Unsafe.Add(ref stats.page_bins.e0, (nint)i);
                    mi_stats_json_write_bin(writer, in stat, (byte)i);
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            ReadOnlySpan<byte> json = output.WrittenSpan;
            if ((buf != null) && (buf_size > 0))
            {
                if ((nuint)json.Length >= buf_size)
                {
                    return null;
                }

                fixed (byte* source = json)
                {
                    _ = memcpy(buf, source, (nuint)json.Length);
                }
                buf[json.Length] = 0;
                return buf;
            }

            sbyte* result = (sbyte*)mi_malloc((nuint)json.Length + 1);
            if (result == null)
            {
                return null;
            }

            fixed (byte* source = json)
            {
                _ = memcpy(result, source, (nuint)json.Length);
            }
            result[json.Length] = 0;
            return result;
        }
        catch (OutOfMemoryException)
        {
            return null;
        }
    }
}
