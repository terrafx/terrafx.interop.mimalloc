// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the alloc-posix.c file from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

using System.Runtime.CompilerServices;

namespace TerraFX.Interop
{
    public static unsafe partial class Mimalloc
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static partial nuint mi_malloc_size(void* p) => mi_usable_size(p);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static partial nuint mi_malloc_usable_size(void* p) => mi_usable_size(p);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static partial void mi_cfree(void* p)
        {
            if (mi_is_in_heap_region(p))
            {
                mi_free(p);
            }
        }

        public static partial int mi_posix_memalign(void** p, nuint alignment, nuint size)
        {
            // Note: The spec dictates we should not modify `*p` on an error. (issue#27)
            // <http://man7.org/linux/man-pages/man3/posix_memalign.3.html>

            if (p == null)
            {
                return EINVAL;
            }

            if ((alignment % SizeOf<nuint>()) != 0)
            {
                // natural alignment
                return EINVAL;
            }

            if (!_mi_is_power_of_two(alignment))
            {
                // not a power of 2
                return EINVAL;
            }

            void* q = mi_malloc_satisfies_alignment(alignment, size) ? mi_malloc(size) : mi_malloc_aligned(size, alignment);

            if ((q == null) && (size != 0))
            {
                return ENOMEM;
            }

            mi_assert_internal((MI_DEBUG > 1) && (((nuint)q % alignment) == 0));
            *p = q;

            return 0;
        }

        public static partial void* mi_memalign(nuint alignment, nuint size)
        {
            void* p = mi_malloc_satisfies_alignment(alignment, size) ? mi_malloc(size) : mi_malloc_aligned(size, alignment);
            mi_assert_internal((MI_DEBUG > 1) && (((nuint)p % alignment) == 0));
            return p;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static partial void* mi_valloc(nuint size) => mi_memalign(_mi_os_page_size(), size);

        public static partial void* mi_pvalloc(nuint size)
        {
            nuint psize = _mi_os_page_size();

            if (size >= (SIZE_MAX - psize))
            {
                // overflow
                return null;
            }

            nuint asize = _mi_align_up(size, psize);
            return mi_malloc_aligned(asize, psize);
        }

        public static partial void* mi_aligned_alloc(nuint alignment, nuint size)
        {
            if ((alignment == 0) || !_mi_is_power_of_two(alignment))
            {
                return null;
            }

            if ((size & (alignment - 1)) != 0)
            {
                // C11 requires integral multiple, see <https://en.cppreference.com/w/c/memory/aligned_alloc>
                return null;
            }

            void* p = mi_malloc_satisfies_alignment(alignment, size) ? mi_malloc(size) : mi_malloc_aligned(size, alignment);
            mi_assert_internal((MI_DEBUG > 1) && (((nuint)p % alignment) == 0));

            return p;
        }

        public static partial void* mi_reallocarray(void* p, nuint count, nuint size)
        {
            // BSD
            void* newp = mi_reallocn(p, count, size);

            if (newp == null)
            {
                last_errno = ENOMEM;
            }

            return newp;
        }

        public static partial void* mi__expand(void* p, nuint newsize)
        {
            // Microsoft
            void* res = mi_expand(p, newsize);

            if (res == null)
            {
                last_errno = ENOMEM;
            }

            return res;
        }

        public static partial ushort* mi_wcsdup(ushort* s)
        {
            if (s == null)
            {
                return null;
            }

            nuint len;

            for (len = 0; s[len] != 0; len++)
            {
            }

            nuint size = (len + 1) * sizeof(ushort);
            ushort* p = (ushort*)mi_malloc(size);

            if (p != null)
            {
                memcpy(p, s, size);
            }
            return p;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static partial byte* mi_mbsdup(byte* s) => (byte*)mi_strdup((sbyte*)s);

        public static partial int mi_dupenv_s(sbyte** buf, nuint* size, sbyte* name)
        {
            if ((buf == null) || (name == null))
            {
                return EINVAL;
            }

            if (size != null)
            {
                *size = 0;
            }

            // mscver warning 4996
            sbyte* p = getenv(name);

            if (p == null)
            {
                *buf = null;
            }
            else
            {
                *buf = mi_strdup(p);

                if (*buf == null)
                {
                    return ENOMEM;
                }

                if (size != null)
                {
                    *size = strlen(p);
                }
            }

            return 0;
        }

        public static partial int mi_wdupenv_s(ushort** buf, nuint* size, ushort* name)
        {
            if ((buf == null) || (name == null))
            {
                return EINVAL;
            }

            if (size != null)
            {
                *size = 0;
            }

            if (IsWindows)
            {
                // msvc warning 4996
                ushort* p = (ushort*)_wgetenv(name);

                if (p == null)
                {
                    *buf = null;
                }
                else
                {
                    *buf = mi_wcsdup(p);

                    if (*buf == null)
                    {
                        return ENOMEM;
                    }

                    if (size != null)
                    {
                        *size = wcslen(p);
                    }
                }
                return 0;
            }
            else
            {
                // not supported
                *buf = null;
                return EINVAL;
            }
        }

        // Microsoft
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static partial void* mi_aligned_offset_recalloc(void* p, nuint newcount, nuint size, nuint alignment, nuint offset) => mi_recalloc_aligned_at(p, newcount, size, alignment, offset);

        // Microsoft
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static partial void* mi_aligned_recalloc(void* p, nuint newcount, nuint size, nuint alignment) => mi_recalloc_aligned(p, newcount, size, alignment);
    }
}
