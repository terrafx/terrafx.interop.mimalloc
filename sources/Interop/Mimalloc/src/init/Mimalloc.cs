// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the init.c file from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

using System;
using System.Runtime.CompilerServices;
using static TerraFX.Interop.Mimalloc.mi_option_t;

namespace TerraFX.Interop.Mimalloc
{
    public static unsafe partial class Mimalloc
    {
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
                    _mi_os_free(heap_backing, SizeOf<mi_thread_data_t>(), ref _mi_stats_main);
                }
                else
                {
                    _mi_heap_destroy_pages(heap_backing);
                    mi_assert_internal((MI_DEBUG > 1) && (heap_backing->tld->heap_backing == _mi_heap_main));
                }
            }
            finally
            {
                init_mi_heap(heap, heap->tld);
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
        public static partial void mi_thread_done() => _mi_thread_done(mi_get_default_heap());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static partial void _mi_thread_done(mi_heap_t* heap)
        {
            _mi_stat_decrease(ref _mi_stats_main.threads, 1);

            // abandon the thread local heap
            _ = _mi_heap_done(heap);
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

            if (mi_option_is_enabled(mi_option_reserve_huge_os_pages))
            {
                nuint pages = (nuint)mi_option_get(mi_option_reserve_huge_os_pages);
                _ = mi_reserve_huge_os_pages_interleave(pages, 0, pages * 500);
            }
        }

        // Called when the process is done (through `at_exit`)
        private static partial void mi_process_done(object? sender, EventArgs e)
        {
            if (MI_DEBUG != 0)
            {
                // free all memory if possible on process exit. This is not needed for a stand-alone process
                // but should be done if mimalloc is statically linked into another shared library which
                // is repeatedly loaded/unloaded, see issue #281.
                mi_collect(force: true);
            }

            if (mi_option_is_enabled(mi_option_show_stats) || mi_option_is_enabled(mi_option_verbose))
            {
                mi_stats_print(null);
            }

            _mi_verbose_message("process done: 0x{0:X}\n", _mi_heap_main->thread_id);
        }
    }
}
