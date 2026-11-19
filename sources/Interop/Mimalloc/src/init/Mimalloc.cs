// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the init.c file from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

using System;
using System.Runtime.CompilerServices;
using static TerraFX.Interop.Mimalloc.mi_option_t;

namespace TerraFX.Interop.Mimalloc;

public static unsafe partial class Mimalloc
{
    private static int mi_process_done_called;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static mi_page_t* MI_PAGE_EMPTY() => _mi_page_empty;

    // The following members have not been ported as they aren't needed for .NET:
    //  * bool _mi_process_is_initialized
    //  * void mi_heap_main_init()

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static partial mi_heap_t* _mi_heap_main_get()
    {
        mi_assert_internal((MI_DEBUG > 1) && (_mi_heap_main->cookie != 0));
        return _mi_heap_main;
    }

    /* -----------------------------------------------------------
      Initialization and freeing of the thread local heaps
    ----------------------------------------------------------- */

    // The following members have not been ported as they aren't needed for .NET:
    //  * bool _mi_heap_init()

    // Free the thread local default heap (called from `mi_thread_done`)
    private static bool _mi_heap_done(mi_heap_t* heap)
    {
        mi_assert_internal((MI_DEBUG > 1) && (heap != null));
        mi_thread_data_t* td_free = null;

        try
        {
            // switch to backing heap
            mi_heap_t* heap_backing = heap->tld->heap_backing;

            if (heap_backing == null)
            {
                return false;
            }

            // delete all non-backing heaps in this thread
            mi_heap_t* curr = heap_backing->tld->heaps;

            while (curr != null)
            {
                // save `next` as `curr` will be freed
                mi_heap_t* next = curr->next;

                if (curr != heap_backing)
                {
                    mi_assert_internal((MI_DEBUG > 1) && (!mi_heap_is_backing(curr)));
                    mi_heap_delete((IntPtr)curr);
                }

                curr = next;
            }

            mi_assert_internal((MI_DEBUG > 1) && (heap_backing->tld->heaps == heap_backing) && (heap_backing->next == null));
            mi_assert_internal((MI_DEBUG > 1) && mi_heap_is_backing(heap_backing));

            if (heap_backing != _mi_heap_main)
            {
                // collect if not the main thread
                _mi_heap_collect_abandon(heap_backing);
            }

            // merge stats
            _mi_stats_done(ref heap_backing->tld->stats);

            if (heap_backing != _mi_heap_main)
            {
                mi_assert_internal((MI_DEBUG > 1) && ((heap_backing->tld->segments.count == 0) || (heap_backing->thread_id != _mi_thread_id())));
                init_mi_heap(heap_backing, heap_backing->tld);
                _mi_heap_guarded_init(heap_backing);
                td_free = (mi_thread_data_t*)heap_backing;
            }
            else
            {
                _mi_heap_destroy_pages(heap_backing);
                init_mi_heap(heap_backing, heap_backing->tld);
                _mi_heap_guarded_init(heap_backing);
                mi_assert_internal((MI_DEBUG > 1) && (heap_backing->tld->heap_backing == _mi_heap_main));
                mi_thread_data_collect();
            }
        }
        finally
        {
            if (td_free != null)
            {
                mi_thread_data_free(td_free);
            }
        }

        return false;
    }

    private static partial void _mi_thread_done(mi_heap_t* heap);

    // The following members have not been ported as they aren't needed for .NET:
    //  * bool tls_initialized
    //  * void mi_process_setup_auto_thread_done()

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static partial bool _mi_is_main_thread()
    {
        mi_assert_internal((MI_DEBUG > 1) && (_mi_heap_main->thread_id != 0));
        return _mi_heap_main->thread_id == _mi_thread_id();
    }

    // The following members have not been ported as they aren't needed for .NET:
    //  * void mi_thread_init()

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void mi_thread_done() => _mi_thread_done(_mi_heap_default);

    public static partial void mi_thread_set_in_threadpool()
    {
        // The upstream API is currently a no-op; the managed port has no thread-pool tracking state.
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static partial void _mi_thread_done(mi_heap_t* heap)
    {
        if (thread_done_in_progress || !mi_heap_is_initialized(heap))
        {
            return;
        }

        thread_done_in_progress = true;

        try
        {
            _ = mi_atomic_decrement_relaxed(ref thread_count);
            _mi_stat_decrease(ref _mi_stats_main.threads, 1);

            // abandon the thread local heap
            _ = _mi_heap_done(heap);
            _mi_heap_default = null;
        }
        finally
        {
            thread_done_in_progress = false;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static partial void _mi_heap_set_default_direct(mi_heap_t* heap)
    {
        mi_assert_internal((MI_DEBUG > 1) && (heap != null));
        *_mi_heap_default = *heap;
    }

    // --------------------------------------------------------
    // Run functions on process init/done, and thread init/done
    // --------------------------------------------------------
    private static partial void mi_process_done(object? sender, EventArgs e);

    private static partial void _mi_heap_guarded_init(mi_heap_t* heap)
    {
        mi_heap_guarded_set_sample_rate(
            (IntPtr)heap,
            (nuint)mi_option_get_clamp(mi_option_guarded_sample_rate, 0, int.MaxValue),
            unchecked((nuint)mi_option_get(mi_option_guarded_sample_seed)));
        mi_heap_guarded_set_size_bound(
            (IntPtr)heap,
            (nuint)mi_option_get_clamp(mi_option_guarded_min, 0, int.MaxValue),
            (nuint)mi_option_get_clamp(mi_option_guarded_max, 0, int.MaxValue));
    }

    public static partial void mi_heap_guarded_set_sample_rate(IntPtr heap, nuint sample_rate, nuint seed)
    {
        mi_heap_t* heap_t = (mi_heap_t*)heap;
        if (heap_t == null)
        {
            return;
        }

        heap_t->guarded_sample_rate = sample_rate;
        heap_t->guarded_sample_count = sample_rate;
        if (sample_rate > 1)
        {
            if (seed == 0)
            {
                seed = _mi_heap_random_next(heap_t);
            }
            heap_t->guarded_sample_count = (seed % sample_rate) + 1;
        }
    }

    public static partial void mi_heap_guarded_set_size_bound(IntPtr heap, nuint min, nuint max)
    {
        mi_heap_t* heap_t = (mi_heap_t*)heap;
        if (heap_t == null)
        {
            return;
        }

        heap_t->guarded_size_min = min;
        heap_t->guarded_size_max = (min > max) ? min : max;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static mi_subproc_t* _mi_subproc_from_id(IntPtr subproc_id)
        => (subproc_id == IntPtr.Zero) ? _mi_subproc_default : (mi_subproc_t*)subproc_id;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial IntPtr mi_subproc_main() => IntPtr.Zero;

    public static partial IntPtr mi_subproc_new()
    {
        void* p = _mi_os_alloc((nuint)SizeOf<mi_subproc_t>(), out mi_memid_t memid, ref _mi_stats_main);
        if (p == null)
        {
            return IntPtr.Zero;
        }

        mi_subproc_t* subproc = (mi_subproc_t*)p;
        *subproc = default;
        subproc->memid = memid;
        return (IntPtr)subproc;
    }

    public static partial void mi_subproc_delete(IntPtr subproc)
    {
        if (subproc == IntPtr.Zero)
        {
            return;
        }

        mi_subproc_t* subproc_t = _mi_subproc_from_id(subproc);
        mi_os_abandoned_lock_acquire(ref subproc_t->abandoned_os_lock);
        bool safe_to_delete = (subproc_t->abandoned_os_list == null) && (mi_atomic_load_relaxed(ref subproc_t->abandoned_count) == 0);
        mi_os_abandoned_lock_release(ref subproc_t->abandoned_os_lock);
        if (!safe_to_delete)
        {
            return;
        }

        mi_memid_t memid = subproc_t->memid;
        _mi_os_free(subproc_t, (nuint)SizeOf<mi_subproc_t>(), memid, ref _mi_stats_main);
    }

    public static partial void mi_subproc_add_current_thread(IntPtr subproc)
    {
        mi_heap_t* heap = mi_get_default_heap();
        if (heap == null)
        {
            return;
        }

        mi_assert((MI_DEBUG != 0) && (heap->tld->segments.subproc == _mi_subproc_default));
        if (heap->tld->segments.subproc != _mi_subproc_default)
        {
            return;
        }

        heap->tld->segments.subproc = _mi_subproc_from_id(subproc);
    }

    // The following members have not been ported as they aren't needed for .NET:
    //  * bool os_preloading
    //  * bool mi_redirected
    //  * bool _mi_preloading()

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial bool mi_is_redirected() => false;

    // The following members have not been ported as they aren't needed for .NET:
    //  * bool mi_allocator_init(const char**)
    //  * void mi_allocator_done()
    //  * void mi_process_load()

    // Initialize the process; called by the process loader
    private static void mi_process_init()
    {
        // This ensures that create_mi_heap_default() is called and initializes the thread
        mi_heap_t* default_heap = mi_get_default_heap();
        mi_assert_internal((MI_DEBUG > 1) && (default_heap == _mi_heap_main_get()));

        _mi_verbose_message("process init: 0x{0:X}\n", _mi_thread_id());

        if (MI_DEBUG != 0)
        {
            _mi_verbose_message("debug level : {0}\n", MI_DEBUG);
        }

        _mi_verbose_message("secure level: {0}\n", MI_SECURE);

        // only call stat reset *after* thread init (or the heap tld == null)
        mi_stats_reset();
        _mi_heap_guarded_init(default_heap);

        if (mi_option_is_enabled(mi_option_reserve_huge_os_pages))
        {
            nuint pages = (nuint)mi_option_get_clamp(mi_option_reserve_huge_os_pages, 0, 128 * 1024);
            int numa_node = (int)mi_option_get_clamp(mi_option_reserve_huge_os_pages_at, -1, int.MaxValue);
            nuint timeout_msecs = pages * 500;

            if (numa_node >= 0)
            {
                _ = mi_reserve_huge_os_pages_at(pages, numa_node, timeout_msecs);
            }
            else
            {
                _ = mi_reserve_huge_os_pages_interleave(pages, 0, timeout_msecs);
            }
        }

        nuint reserve_os_memory = mi_option_get_size(mi_option_reserve_os_memory);

        if (reserve_os_memory > 0)
        {
            _ = mi_reserve_os_memory(reserve_os_memory, true, true);
        }
    }

    // Called when the process is done (through `at_exit`)
    private static partial void mi_process_done(object? sender, EventArgs e)
    {
        if (mi_option_get(mi_option_destroy_on_exit) > 1)
        {
            return;
        }

        mi_process_done();
    }

    public static partial void mi_process_done()
    {
        if (System.Threading.Interlocked.Exchange(ref mi_process_done_called, 1) != 0)
        {
            return;
        }

        if ((MI_DEBUG != 0) || mi_option_is_enabled(mi_option_destroy_on_exit))
        {
            // Managed static metadata cannot be destroyed, but a final collection can release unused pages.
            mi_collect(force: true);
        }

        if (mi_option_is_enabled(mi_option_show_stats) || mi_option_is_enabled(mi_option_verbose))
        {
            mi_stats_print(null);
        }

        _mi_verbose_message("process done: 0x{0:X}\n", _mi_heap_main->thread_id);
    }
}
