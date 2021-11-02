// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the main.c file from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

using System;
using NUnit.Framework;
using static TerraFX.Interop.Mimalloc;

namespace TerraFX.Interop.UnitTests
{
    /// <summary>Provides validation of the <see cref="Mimalloc" /> class.</summary>
    public static unsafe partial class MimallocTests
    {
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
    }
}
