// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static TerraFX.Interop.mi_option_t;

namespace TerraFX.Interop
{
    public static unsafe partial class Mimalloc
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int get_last_errno() => last_errno;

        [ModuleInitializer]
        internal static void Initialize()
        {
            NativeLibrary.SetDllImportResolver(Assembly.GetExecutingAssembly(), OnDllImport);

            mi_assert_internal((MI_DEBUG > 1) && (MI_LARGE_OBJ_WSIZE_MAX < 655360));

            if (Environment.Is64BitProcess)
            {
                mi_assert_internal((MI_DEBUG > 1) && (SizeOf<mi_heap_t._pages_free_direct_e__FixedBuffer>() == (SizeOf<nuint>() * (MI_PAGES_DIRECT + 1))));
            }
            else
            {
                mi_assert_internal((MI_DEBUG > 1) && (SizeOf<mi_heap_t._pages_free_direct_e__FixedBuffer>() == (SizeOf<nuint>() * MI_PAGES_DIRECT)));
            }

            mi_assert_internal((MI_DEBUG > 1) && (SizeOf<mi_heap_t._pages_e__FixedBuffer>() == (SizeOf<mi_page_queue_t>() * (MI_BIN_FULL + 1))));
            mi_assert_internal((MI_DEBUG > 1) && (SizeOf<mi_stats_t._normal_e__FixedBuffer>() == (SizeOf<mi_stat_count_t>() * (MI_BIN_HUGE + 1))));

            atexit(mi_process_done);

            _mi_options_init();
            mi_process_init();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void abort() => Environment.Exit(-1);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void atexit(EventHandler func) => AppDomain.CurrentDomain.ProcessExit += func;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static nuint* create_mi_arenas() => (nuint*)RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(Mimalloc), (int)(SizeOf<nuint>() * MI_MAX_ARENAS));

        private static mi_heap_t* create_mi_heap_default()
        {
            mi_heap_t* mi_heap_default;

            if (_mi_is_main_thread())
            {
                mi_assert_internal((MI_DEBUG > 1) && (_mi_heap_main->cookie != 0));
                mi_heap_default = _mi_heap_main;
                mi_assert_internal((MI_DEBUG > 1) && (mi_heap_default->tld->heap_backing == _mi_heap_main));
            }
            else
            {
                // use `_mi_os_alloc` to allocate directly from the OS
                mi_thread_data_t* td = (mi_thread_data_t*)_mi_os_alloc(SizeOf<mi_thread_data_t>(), ref _mi_stats_main); // Todo: more efficient allocation?

                if (td == null)
                {
                    // if this fails, try once more. (issue #257)
                    td = (mi_thread_data_t*)_mi_os_alloc(SizeOf<mi_thread_data_t>(), ref _mi_stats_main);

                    if (td == null)
                    {
                        // really out of memory
                        _mi_error_message(ENOMEM, "unable to allocate thread local heap metadata ({0} bytes)\n", sizeof(mi_thread_data_t));
                        mi_heap_default = null;
                    }
                }

                mi_heap_default = &td->heap;
                init_mi_heap(mi_heap_default, &td->tld);
            }

            _mi_stat_increase(ref _mi_stats_main.threads, 1);
            _mi_verbose_message("thread init: 0x{0:X}\n", _mi_thread_id());

            return mi_heap_default;
        }

        private static mi_heap_t* create_mi_heap_main()
        {
            mi_heap_t* mi_heap_main = (mi_heap_t*)RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(Mimalloc), sizeof(mi_heap_t));
            init_mi_heap(mi_heap_main, tld_main);
            return mi_heap_main;
        }

        private static mi_page_t* create_mi_page_empty()
        {
            mi_page_t* mi_page_empty = (mi_page_t*)RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(Mimalloc), sizeof(mi_page_t));
            *mi_page_empty = default;
            return mi_page_empty;
        }

        private static mi_heap_t._pages_e__FixedBuffer* create_mi_heap_empty_pages()
        {
            mi_heap_t._pages_e__FixedBuffer* mi_heap_empty_pages = (mi_heap_t._pages_e__FixedBuffer*)RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(Mimalloc), sizeof(mi_heap_t._pages_e__FixedBuffer));
            init_mi_heap_empty_pages(mi_heap_empty_pages);
            return mi_heap_empty_pages;
        }

        private static mi_heap_t._pages_free_direct_e__FixedBuffer* create_mi_heap_empty_pages_free_direct()
        {
            mi_heap_t._pages_free_direct_e__FixedBuffer* mi_heap_empty_pages_free_direct = (mi_heap_t._pages_free_direct_e__FixedBuffer*)RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(Mimalloc), sizeof(mi_heap_t._pages_free_direct_e__FixedBuffer));
            init_mi_heap_empty_pages_free_direct(mi_heap_empty_pages_free_direct);
            return mi_heap_empty_pages_free_direct;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static mem_region_t* create_regions() => (mem_region_t*)RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(Mimalloc), (int)(SizeOf<mem_region_t>() * MI_REGION_MAX));

        private static mi_tld_t* create_tld()
        {
            mi_tld_t* tld = (mi_tld_t*)RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(Mimalloc), sizeof(mi_tld_t));
            *tld = default;
            return tld;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void exit(int status) => Environment.Exit(status);

        private static uint get_app_context_data(string name, uint defaultValue)
        {
            var data = AppContext.GetData(name);

            if (data is uint value)
            {
                return value;
            }
            else if ((data is string s) && uint.TryParse(s, out var result))
            {
                return result;
            }
            else
            {
                return defaultValue;
            }
        }

        private static void* get_export(string libraryName, string exportName)
        {
            void* export = null;

            if (NativeLibrary.TryLoad(libraryName, out IntPtr libraryHandle))
            {
                if (NativeLibrary.TryGetExport(libraryHandle, exportName, out IntPtr exportAddress))
                {
                    export = (void*)exportAddress;
                }
                NativeLibrary.Free(libraryHandle);
            }

            return export;
        }

        [return: NativeTypeName("size_t")]
        private static nuint get_large_os_page_size()
        {
            nuint large_os_page_size = 0;

            if (IsWindows)
            {
                // Try to see if large OS pages are supported
                // To use large pages on Windows, we first need access permission
                // Set "Lock pages in memory" permission in the group policy editor
                // <https://devblogs.microsoft.com/oldnewthing/20110128-00/?p=11643>

                uint err = 0;

                IntPtr token = IntPtr.Zero;
                bool ok = OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, &token) != 0;

                if (ok)
                {
                    TOKEN_PRIVILEGES tp;

                    fixed (char* lpName = "SeLockMemoryPrivilege")
                    {
                        ok = LookupPrivilegeValue(null, (ushort*)lpName, &tp.Privileges.e0.Luid) != 0;
                    }

                    if (ok)
                    {
                        tp.PrivilegeCount = 1;
                        tp.Privileges.e0.Attributes = SE_PRIVILEGE_ENABLED;

                        ok = AdjustTokenPrivileges(token, FALSE, &tp, 0, null, null) != 0;

                        if (ok)
                        {
                            err = GetLastError();
                            ok = err == ERROR_SUCCESS;

                            if (ok)
                            {
                                large_os_page_size = GetLargePageMinimum();
                            }
                        }
                    }

                    CloseHandle(token);
                }

                if (!ok)
                {
                    if (err == 0)
                    {
                        err = GetLastError();
                    }
                    _mi_warning_message("cannot enable large OS page support, error {0}\n", err);
                }
            }
            else
            {
                // TODO: can we query the OS for this?
                large_os_page_size = 2 * MiB;
            }

            return large_os_page_size;
        }

        private static long get_mi_clock_diff()
        {
            long t0 = _mi_clock_now();
            return _mi_clock_now() - t0;
        }

        [return: NativeTypeName("size_t")]
        private static nuint get_mi_numa_node_count()
        {
            // given explicitly?
            int ncount = mi_option_get(mi_option_use_numa_nodes);

            if (ncount <= 0)
            {
                // or detect dynamically
                ncount = (int)mi_os_numa_node_countx();
            }

            nuint mi_numa_node_count = (nuint)((ncount <= 0) ? 1 : ncount);
            _mi_verbose_message("using {0} numa regions\n", mi_numa_node_count);
            return mi_numa_node_count;
        }

        [return: NativeTypeName("size_t")]
        private static nuint get_os_page_size()
        {
            nuint os_page_size = 4096;

            if (IsWindows)
            {
                SYSTEM_INFO si;
                GetSystemInfo(&si);

                if (si.dwPageSize > 0)
                {
                    os_page_size = si.dwPageSize;
                }
            }
            else
            {
                nint result = sysconf(_SC_PAGESIZE);

                if (result > 0)
                {
                    os_page_size = (nuint)result;
                }
            }

            return os_page_size;
        }

        [return: NativeTypeName("size_t")]
        private static nuint get_os_alloc_granularity()
        {
            nuint os_alloc_granularity = 4096;

            if (IsWindows)
            {
                SYSTEM_INFO si;
                GetSystemInfo(&si);

                if (si.dwAllocationGranularity > 0)
                {
                    os_alloc_granularity = si.dwAllocationGranularity;
                }
            }
            else
            {
                nint result = sysconf(_SC_PAGESIZE);

                if (result > 0)
                {
                    os_alloc_granularity = os_page_size;
                }
            }

            return os_alloc_granularity;
        }

        private static void init_mi_heap(mi_heap_t* heap, mi_tld_t* tld, mi_heap_t* bheap = null)
        {
            *heap = new mi_heap_t {
                tld = tld,
                pages_free_direct = *_mi_heap_empty_pages_free_direct,
                pages = *_mi_heap_empty_pages,
                thread_id = _mi_thread_id(),
                page_retired_min = MI_BIN_FULL,
            };

            if (bheap != null)
            {
                _mi_random_split(in bheap->random, out heap->random);

                // don't reclaim abandoned pages or otherwise destroy is unsafe
                heap->no_reclaim = true;

                heap->next = heap->tld->heaps;
            }
            else
            {
                _mi_random_init(out heap->random);

                tld->heap_backing = heap;
                tld->segments.stats = &tld->stats;
                tld->segments.os = &tld->os;
                tld->os.stats = &tld->stats;
            }

            heap->cookie = _mi_heap_random_next(heap) | 1;

            heap->keys.e0 = _mi_heap_random_next(heap);
            heap->keys.e1 = _mi_heap_random_next(heap);

            tld->heaps = heap;
        }

        private static void init_mi_heap_empty_pages(mi_heap_t._pages_e__FixedBuffer* pages)
        {
            (&pages->e0)[0x00] = new mi_page_queue_t { block_size = 1 * SizeOf<nuint>() };

            (&pages->e0)[0x01] = new mi_page_queue_t { block_size = 1 * SizeOf<nuint>() };
            (&pages->e0)[0x02] = new mi_page_queue_t { block_size = 2 * SizeOf<nuint>() };
            (&pages->e0)[0x03] = new mi_page_queue_t { block_size = 3 * SizeOf<nuint>() };
            (&pages->e0)[0x04] = new mi_page_queue_t { block_size = 4 * SizeOf<nuint>() };
            (&pages->e0)[0x05] = new mi_page_queue_t { block_size = 5 * SizeOf<nuint>() };
            (&pages->e0)[0x06] = new mi_page_queue_t { block_size = 6 * SizeOf<nuint>() };
            (&pages->e0)[0x07] = new mi_page_queue_t { block_size = 7 * SizeOf<nuint>() };
            (&pages->e0)[0x08] = new mi_page_queue_t { block_size = 8 * SizeOf<nuint>() };

            (&pages->e0)[0x09] = new mi_page_queue_t { block_size = 10 * SizeOf<nuint>() };
            (&pages->e0)[0x0A] = new mi_page_queue_t { block_size = 12 * SizeOf<nuint>() };
            (&pages->e0)[0x0B] = new mi_page_queue_t { block_size = 14 * SizeOf<nuint>() };
            (&pages->e0)[0x0C] = new mi_page_queue_t { block_size = 16 * SizeOf<nuint>() };
            (&pages->e0)[0x0D] = new mi_page_queue_t { block_size = 20 * SizeOf<nuint>() };
            (&pages->e0)[0x0E] = new mi_page_queue_t { block_size = 24 * SizeOf<nuint>() };
            (&pages->e0)[0x0F] = new mi_page_queue_t { block_size = 28 * SizeOf<nuint>() };
            (&pages->e0)[0x10] = new mi_page_queue_t { block_size = 32 * SizeOf<nuint>() };

            (&pages->e0)[0x11] = new mi_page_queue_t { block_size = 40 * SizeOf<nuint>() };
            (&pages->e0)[0x12] = new mi_page_queue_t { block_size = 48 * SizeOf<nuint>() };
            (&pages->e0)[0x13] = new mi_page_queue_t { block_size = 56 * SizeOf<nuint>() };
            (&pages->e0)[0x14] = new mi_page_queue_t { block_size = 64 * SizeOf<nuint>() };
            (&pages->e0)[0x15] = new mi_page_queue_t { block_size = 80 * SizeOf<nuint>() };
            (&pages->e0)[0x16] = new mi_page_queue_t { block_size = 96 * SizeOf<nuint>() };
            (&pages->e0)[0x17] = new mi_page_queue_t { block_size = 112 * SizeOf<nuint>() };
            (&pages->e0)[0x18] = new mi_page_queue_t { block_size = 128 * SizeOf<nuint>() };

            (&pages->e0)[0x19] = new mi_page_queue_t { block_size = 160 * SizeOf<nuint>() };
            (&pages->e0)[0x1A] = new mi_page_queue_t { block_size = 192 * SizeOf<nuint>() };
            (&pages->e0)[0x1B] = new mi_page_queue_t { block_size = 224 * SizeOf<nuint>() };
            (&pages->e0)[0x1C] = new mi_page_queue_t { block_size = 256 * SizeOf<nuint>() };
            (&pages->e0)[0x1D] = new mi_page_queue_t { block_size = 320 * SizeOf<nuint>() };
            (&pages->e0)[0x1E] = new mi_page_queue_t { block_size = 384 * SizeOf<nuint>() };
            (&pages->e0)[0x1F] = new mi_page_queue_t { block_size = 448 * SizeOf<nuint>() };
            (&pages->e0)[0x20] = new mi_page_queue_t { block_size = 512 * SizeOf<nuint>() };

            (&pages->e0)[0x21] = new mi_page_queue_t { block_size = 640 * SizeOf<nuint>() };
            (&pages->e0)[0x22] = new mi_page_queue_t { block_size = 768 * SizeOf<nuint>() };
            (&pages->e0)[0x23] = new mi_page_queue_t { block_size = 896 * SizeOf<nuint>() };
            (&pages->e0)[0x24] = new mi_page_queue_t { block_size = 1024 * SizeOf<nuint>() };
            (&pages->e0)[0x25] = new mi_page_queue_t { block_size = 1280 * SizeOf<nuint>() };
            (&pages->e0)[0x26] = new mi_page_queue_t { block_size = 1536 * SizeOf<nuint>() };
            (&pages->e0)[0x27] = new mi_page_queue_t { block_size = 1792 * SizeOf<nuint>() };
            (&pages->e0)[0x28] = new mi_page_queue_t { block_size = 2048 * SizeOf<nuint>() };

            (&pages->e0)[0x29] = new mi_page_queue_t { block_size = 2560 * SizeOf<nuint>() };
            (&pages->e0)[0x2A] = new mi_page_queue_t { block_size = 3072 * SizeOf<nuint>() };
            (&pages->e0)[0x2B] = new mi_page_queue_t { block_size = 3584 * SizeOf<nuint>() };
            (&pages->e0)[0x2C] = new mi_page_queue_t { block_size = 4096 * SizeOf<nuint>() };
            (&pages->e0)[0x2D] = new mi_page_queue_t { block_size = 5120 * SizeOf<nuint>() };
            (&pages->e0)[0x2E] = new mi_page_queue_t { block_size = 6144 * SizeOf<nuint>() };
            (&pages->e0)[0x2F] = new mi_page_queue_t { block_size = 7168 * SizeOf<nuint>() };
            (&pages->e0)[0x30] = new mi_page_queue_t { block_size = 8192 * SizeOf<nuint>() };

            (&pages->e0)[0x31] = new mi_page_queue_t { block_size = 10240 * SizeOf<nuint>() };
            (&pages->e0)[0x32] = new mi_page_queue_t { block_size = 12288 * SizeOf<nuint>() };
            (&pages->e0)[0x33] = new mi_page_queue_t { block_size = 14336 * SizeOf<nuint>() };
            (&pages->e0)[0x34] = new mi_page_queue_t { block_size = 16384 * SizeOf<nuint>() };
            (&pages->e0)[0x35] = new mi_page_queue_t { block_size = 20480 * SizeOf<nuint>() };
            (&pages->e0)[0x36] = new mi_page_queue_t { block_size = 24576 * SizeOf<nuint>() };
            (&pages->e0)[0x37] = new mi_page_queue_t { block_size = 28672 * SizeOf<nuint>() };
            (&pages->e0)[0x38] = new mi_page_queue_t { block_size = 32768 * SizeOf<nuint>() };

            (&pages->e0)[0x39] = new mi_page_queue_t { block_size = 40960 * SizeOf<nuint>() };
            (&pages->e0)[0x3A] = new mi_page_queue_t { block_size = 49152 * SizeOf<nuint>() };
            (&pages->e0)[0x3B] = new mi_page_queue_t { block_size = 57344 * SizeOf<nuint>() };
            (&pages->e0)[0x3C] = new mi_page_queue_t { block_size = 65536 * SizeOf<nuint>() };
            (&pages->e0)[0x3D] = new mi_page_queue_t { block_size = 81920 * SizeOf<nuint>() };
            (&pages->e0)[0x3E] = new mi_page_queue_t { block_size = 98304 * SizeOf<nuint>() };
            (&pages->e0)[0x3F] = new mi_page_queue_t { block_size = 114688 * SizeOf<nuint>() };
            (&pages->e0)[0x40] = new mi_page_queue_t { block_size = 131072 * SizeOf<nuint>() };

            (&pages->e0)[0x41] = new mi_page_queue_t { block_size = 163840 * SizeOf<nuint>() };
            (&pages->e0)[0x42] = new mi_page_queue_t { block_size = 196608 * SizeOf<nuint>() };
            (&pages->e0)[0x43] = new mi_page_queue_t { block_size = 229376 * SizeOf<nuint>() };
            (&pages->e0)[0x44] = new mi_page_queue_t { block_size = 262144 * SizeOf<nuint>() };
            (&pages->e0)[0x45] = new mi_page_queue_t { block_size = 327680 * SizeOf<nuint>() };
            (&pages->e0)[0x46] = new mi_page_queue_t { block_size = 393216 * SizeOf<nuint>() };
            (&pages->e0)[0x47] = new mi_page_queue_t { block_size = 458752 * SizeOf<nuint>() };
            (&pages->e0)[0x48] = new mi_page_queue_t { block_size = 524288 * SizeOf<nuint>() };

            (&pages->e0)[0x49] = new mi_page_queue_t { block_size = (MI_LARGE_OBJ_WSIZE_MAX + 1) * SizeOf<nuint>() };
            (&pages->e0)[0x4A] = new mi_page_queue_t { block_size = (MI_LARGE_OBJ_WSIZE_MAX + 2) * SizeOf<nuint>() };

            mi_assert_internal((MI_DEBUG > 1) && ((&pages->e0)[MI_BIN_FULL].block_size == ((MI_LARGE_OBJ_WSIZE_MAX + 2) * SizeOf<nuint>())));
        }

        private static void init_mi_heap_empty_pages_free_direct(mi_heap_t._pages_free_direct_e__FixedBuffer* pages_free_direct)
        {
            for (nuint i = 0; i < MI_PAGES_DIRECT; i++)
            {
                (&pages_free_direct->e0)[i] = MI_PAGE_EMPTY();
            }
        }

        private static IntPtr OnDllImport(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            IntPtr nativeLibrary;

            if (TryResolveLibrary(libraryName, assembly, searchPath, out nativeLibrary))
            {
                return nativeLibrary;
            }

            if (libraryName.Equals("libc") && TryResolveLibc(assembly, searchPath, out nativeLibrary))
            {
                return nativeLibrary;
            }

            return IntPtr.Zero;
        }

        private static bool TryResolveLibc(Assembly assembly, DllImportSearchPath? searchPath, out IntPtr nativeLibrary)
        {
            if (IsWindows)
            {
                if (NativeLibrary.TryLoad("msvcrt", assembly, searchPath, out nativeLibrary))
                {
                    return true;
                }
            }
            else if (IsLinux)
            {
                if (NativeLibrary.TryLoad("libc.so.6", assembly, searchPath, out nativeLibrary))
                {
                    return true;
                }
            }
            else if (IsMacOS)
            {
                if (NativeLibrary.TryLoad("libSystem", assembly, searchPath, out nativeLibrary))
                {
                    return true;
                }
            }

            return NativeLibrary.TryLoad("libc", assembly, searchPath, out nativeLibrary);
        }

        private static bool TryResolveLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath, out IntPtr nativeLibrary)
        {
            var resolveLibrary = ResolveLibrary;

            if (resolveLibrary != null)
            {
                var resolvers = resolveLibrary.GetInvocationList();

                foreach (DllImportResolver resolver in resolvers)
                {
                    nativeLibrary = resolver(libraryName, assembly, searchPath);

                    if (nativeLibrary != IntPtr.Zero)
                    {
                        return true;
                    }
                }
            }

            nativeLibrary = IntPtr.Zero;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint SizeOf<T>() => (uint)Unsafe.SizeOf<T>();
    }
}
