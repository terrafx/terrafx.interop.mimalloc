// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the stats.c file from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace TerraFX.Interop
{
    public static unsafe partial class Mimalloc
    {
        /* -----------------------------------------------------------
          Statistics operations
        ----------------------------------------------------------- */

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool mi_is_in_main([NativeTypeName("void*")] ref byte stat)
        {
            ref byte _mi_stats_main_start = ref Unsafe.As<mi_stats_t, byte>(ref _mi_stats_main);
            ref byte _mi_stats_main_end = ref Unsafe.AddByteOffset(ref _mi_stats_main_start, (nint)SizeOf<mi_stats_t>());
            return (Unsafe.IsAddressGreaterThan(ref stat, ref _mi_stats_main_start) || Unsafe.AreSame(ref stat, ref _mi_stats_main_start)) && Unsafe.IsAddressLessThan(ref stat, ref _mi_stats_main_end);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool mi_is_in_main([NativeTypeName("void*")] ref mi_stat_count_t stat) => mi_is_in_main(ref Unsafe.As<mi_stat_count_t, byte>(ref stat));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool mi_is_in_main([NativeTypeName("void*")] ref mi_stat_counter_t stat) => mi_is_in_main(ref Unsafe.As<mi_stat_counter_t, byte>(ref stat));

        private static void mi_stat_update([NativeTypeName("mi_stat_count_t*")] ref mi_stat_count_t stat, [NativeTypeName("int64_t")] long amount)
        {
            if (amount == 0)
            {
                return;
            }

            if (mi_is_in_main(ref stat))
            {
                // add atomically (for abandoned pages)

                long current = mi_atomic_addi64_relaxed(ref stat.current, amount);
                mi_atomic_maxi64_relaxed(ref stat.peak, current + amount);

                if (amount > 0)
                {
                    _ = mi_atomic_addi64_relaxed(ref stat.allocated, amount);
                }
                else
                {
                    _ = mi_atomic_addi64_relaxed(ref stat.freed, -amount);
                }
            }
            else
            {
                // add thread local
                stat.current += amount;

                if (stat.current > stat.peak)
                {
                    stat.peak = stat.current;
                }

                if (amount > 0)
                {
                    stat.allocated += amount;
                }
                else
                {
                    stat.freed += -amount;
                }
            }
        }

        private static partial void _mi_stat_counter_increase(ref mi_stat_counter_t stat, nuint amount)
        {
            if (mi_is_in_main(ref stat))
            {
                _ = mi_atomic_addi64_relaxed(ref stat.count, 1);
                _ = mi_atomic_addi64_relaxed(ref stat.total, (long)amount);
            }
            else
            {
                stat.count++;
                stat.total += (long)amount;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static partial void _mi_stat_increase(ref mi_stat_count_t stat, nuint amount) => mi_stat_update(ref stat, (long)amount);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static partial void _mi_stat_decrease(ref mi_stat_count_t stat, nuint amount) => mi_stat_update(ref stat, -(long)amount);

        // must be thread safe as it is called from stats_merge
        private static void mi_stat_add([NativeTypeName("mi_stat_count_t*")] ref mi_stat_count_t stat, [NativeTypeName("const mi_stat_count_t*")] in mi_stat_count_t src, [NativeTypeName("int64_t")] long unit)
        {
            if (Unsafe.AreSame(ref stat, ref Unsafe.AsRef(in src)))
            {
                return;
            }

            if ((src.allocated == 0) && (src.freed == 0))
            {
                return;
            }

            _ = mi_atomic_addi64_relaxed(ref stat.allocated, src.allocated * unit);
            _ = mi_atomic_addi64_relaxed(ref stat.current, src.current * unit);
            _ = mi_atomic_addi64_relaxed(ref stat.freed, src.freed * unit);

            // peak scores do not work across threads.. 
            _ = mi_atomic_addi64_relaxed(ref stat.peak, src.peak * unit);
        }

        private static void mi_stat_counter_add([NativeTypeName("mi_stat_count_t*")] ref mi_stat_counter_t stat, [NativeTypeName("const mi_stat_counter_t*")] in mi_stat_counter_t src, [NativeTypeName("int64_t")] long unit)
        {
            if (Unsafe.AreSame(ref stat, ref Unsafe.AsRef(in src)))
            {
                return;
            }

            _ = mi_atomic_addi64_relaxed(ref stat.total, src.total * unit);
            _ = mi_atomic_addi64_relaxed(ref stat.count, src.count * unit);
        }

        // must be thread safe as it is called from stats_merge
        private static void mi_stats_add(ref mi_stats_t stats, [NativeTypeName("const mi_stats_t*")] in mi_stats_t src)
        {
            if (Unsafe.AreSame(ref stats, ref Unsafe.AsRef(in src)))
            {
                return;
            }

            mi_stat_add(ref stats.segments, in src.segments, 1);
            mi_stat_add(ref stats.pages, in src.pages, 1);
            mi_stat_add(ref stats.reserved, in src.reserved, 1);
            mi_stat_add(ref stats.committed, in src.committed, 1);
            mi_stat_add(ref stats.reset, in src.reset, 1);
            mi_stat_add(ref stats.page_committed, in src.page_committed, 1);

            mi_stat_add(ref stats.pages_abandoned, in src.pages_abandoned, 1);
            mi_stat_add(ref stats.segments_abandoned, in src.segments_abandoned, 1);
            mi_stat_add(ref stats.threads, in src.threads, 1);

            mi_stat_add(ref stats.malloc, in src.malloc, 1);
            mi_stat_add(ref stats.segments_cache, in src.segments_cache, 1);
            mi_stat_add(ref stats.huge, in src.huge, 1);
            mi_stat_add(ref stats.giant, in src.giant, 1);

            mi_stat_counter_add(ref stats.pages_extended, in src.pages_extended, 1);
            mi_stat_counter_add(ref stats.mmap_calls, in src.mmap_calls, 1);
            mi_stat_counter_add(ref stats.commit_calls, in src.commit_calls, 1);

            mi_stat_counter_add(ref stats.page_no_retire, in src.page_no_retire, 1);
            mi_stat_counter_add(ref stats.searches, in src.searches, 1);
            mi_stat_counter_add(ref stats.huge_count, in src.huge_count, 1);
            mi_stat_counter_add(ref stats.giant_count, in src.giant_count, 1);

            if (MI_STAT > 1)
            {
                for (nuint i = 0; i <= MI_BIN_HUGE; i++)
                {
                    ref readonly mi_stat_count_t src_normal = ref Unsafe.Add(ref Unsafe.AsRef(in src.normal.e0), (nint)i);
                    ref mi_stat_count_t stats_normal = ref Unsafe.Add(ref stats.normal.e0, (nint)i);

                    if ((src_normal.allocated > 0) || (src_normal.freed > 0))
                    {
                        mi_stat_add(ref stats_normal, in src_normal, 1);
                    }
                }
            }
        }

        /* -----------------------------------------------------------
          Display statistics
        ----------------------------------------------------------- */

        // unit > 0 : size in binary bytes 
        // unit == 0: count as decimal
        // unit < 0 : count in binary
        private static void mi_printf_amount([NativeTypeName("int64_t")] long n, [NativeTypeName("int64_t")] long unit, [NativeTypeName("mi_output_fun*")] mi_output_fun? @out, void* arg, [NativeTypeName("const char*")] string fmt)
        {
            string buf;
            string suffix = (unit <= 0) ? " " : "b";

            long @base = (unit == 0) ? 1000 : 1024;

            if (unit > 0)
            {
                n *= unit;
            }

            long pos = (n < 0) ? -n : n;

            if (pos < @base)
            {
                buf = string.Format("{0} {1} ", (int)n, suffix);
            }
            else
            {
                long divider = @base;
                string magnitude = "k";

                if (pos >= divider * @base)
                {
                    divider *= @base;
                    magnitude = "m";
                }

                if (pos >= divider * @base)
                {
                    divider *= @base;
                    magnitude = "g";
                }

                long tens = n / (divider / 10);
                long whole = tens / 10;
                long frac1 = tens % 10;

                buf = string.Format("{0}.{1} {2}{3}", whole, frac1, magnitude, suffix);
            }

            _mi_fprintf(@out, arg, string.IsNullOrEmpty(fmt) ? "{0,-11}" : fmt, buf);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void mi_print_amount([NativeTypeName("int64_t")] long n, [NativeTypeName("int64_t")] long unit, [NativeTypeName("mi_output_fun*")] mi_output_fun? @out, void* arg) => mi_printf_amount(n, unit, @out, arg, "");

        private static void mi_print_count([NativeTypeName("int64_t")] long n, [NativeTypeName("int64_t")] long unit, [NativeTypeName("mi_output_fun*")] mi_output_fun? @out, void* arg)
        {
            if (unit == 1)
            {
                _mi_fprintf(@out, arg, "{0,-11}", " ");
            }
            else
            {
                mi_print_amount(n, 0, @out, arg);
            }
        }

        private static void mi_stat_print([NativeTypeName("const mi_stat_count_t*")] in mi_stat_count_t stat, [NativeTypeName("const char*")] string msg, [NativeTypeName("int64_t")] long unit, [NativeTypeName("mi_output_fun*")] mi_output_fun? @out, void* arg)
        {
            _mi_fprintf(@out, arg, "{0,-10}: ", msg);

            if (unit > 0)
            {
                mi_print_amount(stat.peak, unit, @out, arg);
                mi_print_amount(stat.allocated, unit, @out, arg);
                mi_print_amount(stat.freed, unit, @out, arg);
                mi_print_amount(unit, 1, @out, arg);
                mi_print_count(stat.allocated, unit, @out, arg);

                if (stat.allocated > stat.freed)
                {
                    _mi_fprintf(@out, arg, "  not all freed!\n");
                }
                else
                {
                    _mi_fprintf(@out, arg, "  ok\n");
                }
            }
            else if (unit < 0)
            {
                mi_print_amount(stat.peak, -1, @out, arg);
                mi_print_amount(stat.allocated, -1, @out, arg);
                mi_print_amount(stat.freed, -1, @out, arg);

                if (unit == -1)
                {
                    _mi_fprintf(@out, arg, "{0,-22}", "");
                }
                else
                {
                    mi_print_amount(-unit, 1, @out, arg);
                    mi_print_count(stat.allocated / -unit, 0, @out, arg);
                }

                if (stat.allocated > stat.freed)
                {
                    _mi_fprintf(@out, arg, "  not all freed!\n");
                }
                else
                {
                    _mi_fprintf(@out, arg, "  ok\n");
                }
            }
            else
            {
                mi_print_amount(stat.peak, 1, @out, arg);
                mi_print_amount(stat.allocated, 1, @out, arg);

                _mi_fprintf(@out, arg, "\n");
            }
        }

        private static void mi_stat_counter_print([NativeTypeName("const mi_stat_counter_t*")] in mi_stat_counter_t stat, [NativeTypeName("const char*")] string msg, [NativeTypeName("mi_output_fun*")] mi_output_fun? @out, void* arg)
        {
            _mi_fprintf(@out, arg, "{0,-10}:", msg);
            mi_print_amount(stat.total, -1, @out, arg);
            _mi_fprintf(@out, arg, "\n");
        }

        private static void mi_stat_counter_print_avg([NativeTypeName("const mi_stat_counter_t*")] in mi_stat_counter_t stat, [NativeTypeName("const char*")] string msg, [NativeTypeName("mi_output_fun*")] mi_output_fun? @out, void* arg)
        {
            long avg_tens = (stat.count == 0) ? 0 : (stat.total * 10 / stat.count);

            long avg_whole = avg_tens / 10;
            long avg_frac1 = avg_tens % 10;

            _mi_fprintf(@out, arg, "{0,-10}: {1,-5}.{2} avg\n", msg, avg_whole, avg_frac1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void mi_print_header([NativeTypeName("mi_output_fun*")] mi_output_fun? @out, void* arg) => _mi_fprintf(@out, arg, "\n{0,-10}: {1,-10} {2,-10} {3,-10} {4,-10} {5,-10}\n", "heap stats", "peak  ", "total  ", "freed  ", "unit  ", "count  ");

        private static void mi_stats_print_bins(ref mi_stat_count_t all, [NativeTypeName("const mi_stat_count_t*")] in mi_stat_count_t bins, [NativeTypeName("size_t")] nuint max, [NativeTypeName("const char*")] string fmt, [NativeTypeName("mi_output_fun*")] mi_output_fun? @out, void* arg)
        {
            if (MI_STAT > 1)
            {
                bool found = false;
                string buf;

                for (nuint i = 0; i <= max; i++)
                {
                    ref readonly mi_stat_count_t bin = ref Unsafe.Add(ref Unsafe.AsRef(in bins), (nint)i);

                    if (bin.allocated > 0)
                    {
                        found = true;

                        long unit = (long)_mi_bin_size((byte)i);
                        buf = string.Format("{0} {1,-3}", fmt, (long)i);

                        mi_stat_add(ref all, in bin, unit);
                        mi_stat_print(in bin, buf, unit, @out, arg);
                    }
                }

                if (found)
                {
                    _mi_fprintf(@out, arg, "\n");
                    mi_print_header(@out, arg);
                }
            }
        }

        //------------------------------------------------------------
        // Print statistics
        //------------------------------------------------------------

        private static partial void mi_stat_process_info([NativeTypeName("mi_msecs_t*")] out long elapsed, [NativeTypeName("mi_msecs_t*")] out long utime, [NativeTypeName("mi_msecs_t*")] out long stime, [NativeTypeName("size_t*")] out nuint current_rss, [NativeTypeName("size_t*")] out nuint peak_rss, [NativeTypeName("size_t*")] out nuint current_commit, [NativeTypeName("size_t*")] out nuint peak_commit, [NativeTypeName("size_t*")] out nuint page_faults);

        private static void _mi_stats_print([NativeTypeName("mi_stats_t*")] in mi_stats_t stats, [NativeTypeName("mi_output_fun*")] mi_output_fun? out0, void* arg0)
        {
            // and print using that
            mi_print_header(out0, arg0);

            if (MI_STAT > 1)
            {
                mi_stat_count_t normal = default;
                mi_stats_print_bins(ref normal, in stats.normal.e0, MI_BIN_HUGE, "normal", out0, arg0);
                mi_stat_print(in normal, "normal", 1, out0, arg0);

                mi_stat_print(in stats.huge, "huge", (stats.huge_count.count == 0) ? 1 : -(stats.huge.allocated / stats.huge_count.count), out0, arg0);
                mi_stat_print(in stats.giant, "giant", (stats.giant_count.count == 0) ? 1 : -(stats.giant.allocated / stats.giant_count.count), out0, arg0);

                mi_stat_count_t total = default;
                mi_stat_add(ref total, in normal, 1);
                mi_stat_add(ref total, in stats.huge, 1);
                mi_stat_add(ref total, in stats.giant, 1);
                mi_stat_print(in total, "total", 1, out0, arg0);

                _mi_fprintf(out0, arg0, "malloc requested:     ");
                mi_print_amount(stats.malloc.allocated, 1, out0, arg0);
                _mi_fprintf(out0, arg0, "\n\n");
            }

            mi_stat_print(in stats.reserved, "reserved", 1, out0, arg0);
            mi_stat_print(in stats.committed, "committed", 1, out0, arg0);
            mi_stat_print(in stats.reset, "reset", 1, out0, arg0);
            mi_stat_print(in stats.page_committed, "touched", 1, out0, arg0);
            mi_stat_print(in stats.segments, "segments", -1, out0, arg0);
            mi_stat_print(in stats.segments_abandoned, "-abandoned", -1, out0, arg0);
            mi_stat_print(in stats.segments_cache, "-cached", -1, out0, arg0);
            mi_stat_print(in stats.pages, "pages", -1, out0, arg0);
            mi_stat_print(in stats.pages_abandoned, "-abandoned", -1, out0, arg0);
            mi_stat_counter_print(in stats.pages_extended, "-extended", out0, arg0);
            mi_stat_counter_print(in stats.page_no_retire, "-noretire", out0, arg0);
            mi_stat_counter_print(in stats.mmap_calls, "mmaps", out0, arg0);
            mi_stat_counter_print(in stats.commit_calls, "commits", out0, arg0);
            mi_stat_print(in stats.threads, "threads", -1, out0, arg0);
            mi_stat_counter_print_avg(in stats.searches, "searches", out0, arg0);
            _mi_fprintf(out0, arg0, "{0,-10}: {1,-7}\n", "numa nodes", _mi_os_numa_node_count());

            mi_stat_process_info(out long elapsed, out long user_time, out long sys_time, out nuint current_rss, out nuint peak_rss, out nuint current_commit, out nuint peak_commit, out nuint page_faults);

            _mi_fprintf(out0, arg0, "{0,-10}: {1,7}.{2,-3} s\n", "elapsed", elapsed / 1000, elapsed % 1000);
            _mi_fprintf(out0, arg0, "{0,-10}: user: {1}.{2,-3} s, system: {3}.{4,-3} s, faults: {5}, rss: ", "process", user_time / 1000, user_time % 1000, sys_time / 1000, sys_time % 1000, (uint)page_faults);

            mi_printf_amount((long)peak_rss, 1, out0, arg0, "{0}");

            if (peak_commit > 0)
            {
                _mi_fprintf(out0, arg0, ", commit: ");
                mi_printf_amount((long)peak_commit, 1, out0, arg0, "{0}");
            }

            _mi_fprintf(out0, arg0, "\n");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [return: NativeTypeName("mi_stats_t*")]
        private static ref mi_stats_t mi_stats_get_default()
        {
            mi_heap_t* heap = (mi_heap_t*)mi_heap_get_default();
            return ref heap->tld->stats;
        }

        private static void mi_stats_merge_from([NativeTypeName("mi_stats_t*")] ref mi_stats_t stats)
        {
            if (!Unsafe.AreSame(ref stats, ref _mi_stats_main))
            {
                mi_stats_add(ref _mi_stats_main, stats);
                stats = default;
            }
        }

        public static partial void mi_stats_reset()
        {
            ref mi_stats_t stats = ref mi_stats_get_default();

            if (!Unsafe.AreSame(ref stats, ref _mi_stats_main))
            {
                stats = default;
            }

            _mi_stats_main = default;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static partial void mi_stats_merge() => mi_stats_merge_from(ref mi_stats_get_default());

        // called from `mi_thread_done`
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static partial void _mi_stats_done(ref mi_stats_t stats) => mi_stats_merge_from(ref stats);

        public static partial void mi_stats_print_out(mi_output_fun? @out, void* arg)
        {
            mi_stats_merge_from(ref mi_stats_get_default());
            _mi_stats_print(in _mi_stats_main, @out, arg);
        }

        // for compatibility there is an `out` parameter (which can be `stdout` or `stderr`)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static partial void mi_stats_print(mi_output_fun? @out) => mi_stats_print_out(@out, null);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static partial void mi_thread_stats_print_out(mi_output_fun? @out, void* arg) => _mi_stats_print(mi_stats_get_default(), @out, arg);

        // ----------------------------------------------------------------
        // Basic timer for convenience; use milli-seconds to avoid doubles
        // ----------------------------------------------------------------

        // The following members have not been ported as they aren't needed for .NET:
        //  * LARGE_INTEGER mfreq

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [return: NativeTypeName("mi_msecs_t")]
        private static long mi_to_msecs(long t) => t / Stopwatch.Frequency;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static partial long _mi_clock_now()
        {
            long t = Stopwatch.GetTimestamp();
            return mi_to_msecs(t);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static partial long _mi_clock_start() => _mi_clock_now();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static partial long _mi_clock_end(long start)
        {
            long end = _mi_clock_now();
            return end - start - mi_clock_diff;
        }

        // --------------------------------------------------------
        // Basic process statistics
        // --------------------------------------------------------

        [return: NativeTypeName("mi_msecs_t")]
        private static long filetime_msecs([NativeTypeName("const FILETIME*")] in FILETIME ftime)
        {
            mi_assert_internal((MI_DEBUG > 1) && IsWindows);
            ulong i = ((ulong)ftime.dwHighDateTime << 32) | ftime.dwLowDateTime;

            // FILETIME is in 100 nano seconds
            long msecs = (long)(i / 10000);

            return msecs;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [return: NativeTypeName("mi_msecs_t")]
        private static long timeval_secs([NativeTypeName("const struct timeval*")] in timeval tv) => ((long)tv.tv_sec * 1000) + ((long)tv.tv_usec / 1000);

        private static partial void mi_stat_process_info(out long elapsed, out long utime, out long stime, out nuint current_rss, out nuint peak_rss, out nuint current_commit, out nuint peak_commit, out nuint page_faults)
        {
            elapsed = _mi_clock_end(mi_process_start);

            if (IsWindows)
            {
                FILETIME ct, ut, st, et;
                _ = GetProcessTimes(GetCurrentProcess(), &ct, &et, &st, &ut);

                utime = filetime_msecs(in ut);
                stime = filetime_msecs(in st);

                PROCESS_MEMORY_COUNTERS info;
                _ = GetProcessMemoryInfo(GetCurrentProcess(), &info, SizeOf<PROCESS_MEMORY_COUNTERS>());

                current_rss = info.WorkingSetSize;
                peak_rss = info.PeakWorkingSetSize;
                current_commit = info.PagefileUsage;
                peak_commit = info.PeakPagefileUsage;
                page_faults = info.PageFaultCount;
            }
            else if (IsUnix)
            {
                elapsed = _mi_clock_end(mi_process_start);

                rusage rusage;
                _ = getrusage(RUSAGE_SELF, &rusage);

                utime = timeval_secs(in rusage.ru_utime);
                stime = timeval_secs(in rusage.ru_stime);

                page_faults = (nuint)rusage.ru_majflt;

                // estimate commit using our stats

                peak_commit = (nuint)mi_atomic_loadi64_relaxed(ref _mi_stats_main.committed.peak);
                current_commit = (nuint)mi_atomic_loadi64_relaxed(ref _mi_stats_main.committed.current);

                // estimate
                current_rss = current_commit;

                // Linux reports in KiB
                peak_rss = (nuint)(rusage.ru_maxrss * 1024);
            }
            else
            {
                elapsed = _mi_clock_end(mi_process_start);
                peak_commit = (nuint)mi_atomic_loadi64_relaxed(ref _mi_stats_main.committed.peak);
                current_commit = (nuint)mi_atomic_loadi64_relaxed(ref _mi_stats_main.committed.current);
                peak_rss = peak_commit;
                current_rss = current_commit;
                page_faults = 0;
                utime = 0;
                stime = 0;
            }
        }

        public static partial void mi_process_info(nuint* elapsed_msecs, nuint* user_msecs, nuint* system_msecs, nuint* current_rss, nuint* peak_rss, nuint* current_commit, nuint* peak_commit, nuint* page_faults)
        {
            mi_stat_process_info(out long elapsed, out long utime, out long stime, out nuint current_rss0, out nuint peak_rss0, out nuint current_commit0, out nuint peak_commit0, out nuint page_faults0);

            if (elapsed_msecs != null)
            {
                *elapsed_msecs = (elapsed < 0) ? 0 : ((elapsed < PTRDIFF_MAX) ? (nuint)elapsed : (nuint)PTRDIFF_MAX);
            }

            if (user_msecs != null)
            {
                *user_msecs = (utime < 0) ? 0 : ((utime < PTRDIFF_MAX) ? (nuint)utime : (nuint)PTRDIFF_MAX);
            }

            if (system_msecs != null)
            {
                *system_msecs = (stime < 0) ? 0 : ((stime < PTRDIFF_MAX) ? (nuint)stime : (nuint)PTRDIFF_MAX);
            }

            if (current_rss != null)
            {
                *current_rss = current_rss0;
            }

            if (peak_rss != null)
            {
                *peak_rss = peak_rss0;
            }

            if (current_commit != null)
            {
                *current_commit = current_commit0;
            }

            if (peak_commit != null)
            {
                *peak_commit = peak_commit0;
            }

            if (page_faults != null)
            {
                *page_faults = page_faults0;
            }
        }
    }
}
