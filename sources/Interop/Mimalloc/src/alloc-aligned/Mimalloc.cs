// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the alloc-aligned.c file from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

using System;
using System.Runtime.CompilerServices;

namespace TerraFX.Interop
{
    public static unsafe partial class Mimalloc
    {
        // ------------------------------------------------------
        // Aligned Allocation
        // ------------------------------------------------------

        private static void* mi_heap_malloc_zero_aligned_at([NativeTypeName("mi_heap_t* const")] mi_heap_t* heap, [NativeTypeName("const size_t")] nuint size, [NativeTypeName("const size_t")] nuint alignment, [NativeTypeName("const size_t")] nuint offset, [NativeTypeName("const bool")] bool zero)
        {
            void* p;

            // note: we don't require `size > offset`, we just guarantee that
            // the address at offset is aligned regardless of the allocated size.
            mi_assert((MI_DEBUG != 0) && (alignment > 0));

            if (mi_unlikely(size > (nuint)PTRDIFF_MAX))
            {
                // we don't allocate more than PTRDIFF_MAX (see <https://sourceware.org/ml/libc-announce/2019/msg00001.html>)
                return null;
            }

            if (mi_unlikely((alignment == 0) || !_mi_is_power_of_two(alignment)))
            {
                // require power-of-two (see <https://en.cppreference.com/w/c/memory/aligned_alloc>)
                return null;
            }

            // for any x, `(x & align_mask) == (x % alignment)`
            nuint align_mask = alignment - 1;

            // try if there is a small block available with just the right alignment
            nuint padsize = size + MI_PADDING_SIZE;

            if (mi_likely(padsize <= MI_SMALL_SIZE_MAX))
            {
                mi_page_t* page = _mi_heap_get_free_small_page(heap, padsize);
                bool is_aligned = (((nuint)page->free + offset) & align_mask) == 0;

                if (mi_likely(page->free != null && is_aligned))
                {
                    if (MI_STAT > 1)
                    {
                        mi_stat_increase(ref heap->tld->stats.malloc, size);
                    }

                    // TODO: inline _mi_page_malloc
                    p = _mi_page_malloc(heap, page, padsize);

                    mi_assert_internal((MI_DEBUG > 1) && (p != null));
                    mi_assert_internal((MI_DEBUG > 1) && (((nuint)p + offset) % alignment == 0));

                    if (zero)
                    {
                        _mi_block_zero_init(page, p, size);
                    }
                    return p;
                }
            }

            // use regular allocation if it is guaranteed to fit the alignment constraints
            if ((offset == 0) && (alignment <= padsize) && (padsize <= MI_MEDIUM_OBJ_SIZE_MAX) && ((padsize & align_mask) == 0))
            {
                p = _mi_heap_malloc_zero(heap, size, zero);
                mi_assert_internal((MI_DEBUG > 1) && ((p == null) || (((nuint)p % alignment) == 0)));
                return p;
            }

            // otherwise over-allocate
            p = _mi_heap_malloc_zero(heap, size + alignment - 1, zero);

            if (p == null)
            {
                return null;
            }

            // .. and align within the allocation
            nuint adjust = alignment - (((nuint)p + offset) & align_mask);

            mi_assert_internal((MI_DEBUG > 1) && (adjust <= alignment));

            void* aligned_p = adjust == alignment ? p : (void*)((nuint)p + adjust);

            if (aligned_p != p)
            {
                mi_page_set_has_aligned(_mi_ptr_page(p), true);
            }

            mi_assert_internal((MI_DEBUG > 1) && (((nuint)aligned_p + offset) % alignment == 0));
            mi_assert_internal((MI_DEBUG > 1) && (p == _mi_page_ptr_unalign(_mi_ptr_segment(aligned_p), _mi_ptr_page(aligned_p), aligned_p)));

            return aligned_p;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void* mi_heap_malloc_aligned_at(mi_heap_t* heap, [NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment, [NativeTypeName("size_t")] nuint offset)
            => mi_heap_malloc_zero_aligned_at(heap, size, alignment, offset, false);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static partial void* mi_heap_malloc_aligned_at(IntPtr heap, nuint size, nuint alignment, nuint offset) => mi_heap_malloc_aligned_at((mi_heap_t*)heap, size, alignment, offset);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void* mi_heap_malloc_aligned(mi_heap_t* heap, [NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment) => mi_heap_malloc_aligned_at(heap, size, alignment, 0);

        public static partial void* mi_heap_malloc_aligned(IntPtr heap, nuint size, nuint alignment) => mi_heap_malloc_aligned((mi_heap_t*)heap, size, alignment);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void* mi_heap_zalloc_aligned_at(mi_heap_t* heap, [NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment, [NativeTypeName("size_t")] nuint offset)
            => mi_heap_malloc_zero_aligned_at(heap, size, alignment, offset, true);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static partial void* mi_heap_zalloc_aligned_at(IntPtr heap, nuint size, nuint alignment, nuint offset) => mi_heap_zalloc_aligned_at((mi_heap_t*)heap, size, alignment, offset);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void* mi_heap_zalloc_aligned(mi_heap_t* heap, [NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment) => mi_heap_zalloc_aligned_at(heap, size, alignment, 0);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static partial void* mi_heap_zalloc_aligned(IntPtr heap, nuint size, nuint alignment) => mi_heap_zalloc_aligned((mi_heap_t*)heap, size, alignment);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void* mi_heap_calloc_aligned_at(mi_heap_t* heap, [NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment, [NativeTypeName("size_t")] nuint offset)
        {
            if (mi_count_size_overflow(count, size, out nuint total))
            {
                return null;
            }
            return mi_heap_zalloc_aligned_at(heap, total, alignment, offset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static partial void* mi_heap_calloc_aligned_at(IntPtr heap, nuint count, nuint size, nuint alignment, nuint offset) => mi_heap_calloc_aligned_at((mi_heap_t*)heap, count, size, alignment, offset);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void* mi_heap_calloc_aligned(mi_heap_t* heap, [NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment) => mi_heap_calloc_aligned_at(heap, count, size, alignment, 0);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static partial void* mi_heap_calloc_aligned(IntPtr heap, nuint count, nuint size, nuint alignment) => mi_heap_calloc_aligned((mi_heap_t*)heap, count, size, alignment);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static partial void* mi_malloc_aligned_at(nuint size, nuint alignment, nuint offset) => mi_heap_malloc_aligned_at(mi_get_default_heap(), size, alignment, offset);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static partial void* mi_malloc_aligned(nuint size, nuint alignment) => mi_heap_malloc_aligned(mi_get_default_heap(), size, alignment);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static partial void* mi_zalloc_aligned_at(nuint size, nuint alignment, nuint offset) => mi_heap_zalloc_aligned_at(mi_get_default_heap(), size, alignment, offset);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static partial void* mi_zalloc_aligned(nuint size, nuint alignment) => mi_heap_zalloc_aligned(mi_get_default_heap(), size, alignment);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static partial void* mi_calloc_aligned_at(nuint count, nuint size, nuint alignment, nuint offset) => mi_heap_calloc_aligned_at(mi_get_default_heap(), count, size, alignment, offset);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static partial void* mi_calloc_aligned(nuint count, nuint size, nuint alignment) => mi_heap_calloc_aligned(mi_get_default_heap(), count, size, alignment);

        private static void* mi_heap_realloc_zero_aligned_at(mi_heap_t* heap, void* p, [NativeTypeName("size_t")] nuint newsize, [NativeTypeName("size_t")] nuint alignment, [NativeTypeName("size_t")] nuint offset, bool zero)
        {
            mi_assert((MI_DEBUG != 0) && (alignment > 0));

            if (alignment <= SizeOf<nuint>())
            {
                return _mi_heap_realloc_zero(heap, p, newsize, zero);
            }

            if (p == null)
            {
                return mi_heap_malloc_zero_aligned_at(heap, newsize, alignment, offset, zero);
            }

            nuint size = mi_usable_size(p);

            if ((newsize <= size) && (newsize >= (size - (size / 2))) && ((((nuint)p + offset) % alignment) == 0))
            {
                // reallocation still fits, is aligned and not more than 50% waste
                return p;
            }
            else
            {
                void* newp = mi_heap_malloc_aligned_at(heap, newsize, alignment, offset);

                if (newp != null)
                {
                    if (zero && newsize > size)
                    {
                        mi_page_t* page = _mi_ptr_page(newp);

                        if (page->is_zero)
                        {
                            // already zero initialized
                            mi_assert_expensive((MI_DEBUG > 2) && mi_mem_is_zero(newp, newsize));
                        }
                        else
                        {
                            // also set last word in the previous allocation to zero to ensure any padding is zero-initialized
                            nuint start = size >= SizeOf<nuint>() ? size - SizeOf<nuint>() : 0;
                            memset((byte*)newp + start, 0, newsize - start);
                        }
                    }

                    memcpy(newp, p, (newsize > size) ? size : newsize);

                    // only free if successful
                    mi_free(p);
                }

                return newp;
            }
        }

        private static void* mi_heap_realloc_zero_aligned(mi_heap_t* heap, void* p, [NativeTypeName("size_t")] nuint newsize, [NativeTypeName("size_t")] nuint alignment, bool zero)
        {
            mi_assert((MI_DEBUG != 0) && (alignment > 0));

            if (alignment <= SizeOf<nuint>())
            {
                return _mi_heap_realloc_zero(heap, p, newsize, zero);
            }

            // use offset of previous allocation (p can be null)
            nuint offset = (nuint)p % alignment;

            return mi_heap_realloc_zero_aligned_at(heap, p, newsize, alignment, offset, zero);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void* mi_heap_realloc_aligned_at(mi_heap_t* heap, void* p, [NativeTypeName("size_t")] nuint newsize, [NativeTypeName("size_t")] nuint alignment, [NativeTypeName("size_t")] nuint offset)
            => mi_heap_realloc_zero_aligned_at(heap, p, newsize, alignment, offset, false);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static partial void* mi_heap_realloc_aligned_at(IntPtr heap, void* p, nuint newsize, nuint alignment, nuint offset) => mi_heap_realloc_aligned_at((mi_heap_t*)heap, p, newsize, alignment, offset);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void* mi_heap_realloc_aligned(mi_heap_t* heap, void* p, [NativeTypeName("size_t")] nuint newsize, [NativeTypeName("size_t")] nuint alignment) => mi_heap_realloc_zero_aligned(heap, p, newsize, alignment, false);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static partial void* mi_heap_realloc_aligned(IntPtr heap, void* p, nuint newsize, nuint alignment) => mi_heap_realloc_aligned((mi_heap_t*)heap, p, newsize, alignment);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void* mi_heap_reallocn_aligned_at(mi_heap_t* heap, void* p, [NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment, [NativeTypeName("size_t")] nuint offset)
        {
            if (mi_count_size_overflow(count, size, out nuint total))
            {
                return null;
            }
            return mi_heap_realloc_aligned_at(heap, p, total, alignment, offset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static partial void* mi_heap_reallocn_aligned_at(IntPtr heap, void* p, nuint count, nuint size, nuint alignment, nuint offset) => mi_heap_reallocn_aligned_at((mi_heap_t*)heap, p, count, size, alignment, offset);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void* mi_heap_reallocn_aligned(mi_heap_t* heap, void* p, [NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment)
            => mi_heap_reallocn_aligned_at(heap, p, count, size, alignment, 0);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static partial void* mi_heap_reallocn_aligned(IntPtr heap, void* p, nuint count, nuint size, nuint alignment) => mi_heap_reallocn_aligned((mi_heap_t*)heap, p, count, size, alignment);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void* mi_heap_rezalloc_aligned_at(mi_heap_t* heap, void* p, [NativeTypeName("size_t")] nuint newsize, [NativeTypeName("size_t")] nuint alignment, [NativeTypeName("size_t")] nuint offset)
            => mi_heap_realloc_zero_aligned_at(heap, p, newsize, alignment, offset, true);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static partial void* mi_heap_rezalloc_aligned_at(IntPtr heap, void* p, nuint newsize, nuint alignment, nuint offset) => mi_heap_rezalloc_aligned_at((mi_heap_t*)heap, p, newsize, alignment, offset);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void* mi_heap_rezalloc_aligned(mi_heap_t* heap, void* p, [NativeTypeName("size_t")] nuint newsize, [NativeTypeName("size_t")] nuint alignment) => mi_heap_realloc_zero_aligned(heap, p, newsize, alignment, true);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static partial void* mi_heap_rezalloc_aligned(IntPtr heap, void* p, nuint newsize, nuint alignment) => mi_heap_rezalloc_aligned((mi_heap_t*)heap, p, newsize, alignment);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void* mi_heap_recalloc_aligned_at(mi_heap_t* heap, void* p, [NativeTypeName("size_t")] nuint newcount, [NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment, [NativeTypeName("size_t")] nuint offset)
        {
            if (mi_count_size_overflow(newcount, size, out nuint total))
            {
                return null;
            }
            return mi_heap_rezalloc_aligned_at(heap, p, total, alignment, offset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static partial void* mi_heap_recalloc_aligned_at(IntPtr heap, void* p, nuint newcount, nuint size, nuint alignment, nuint offset) => mi_heap_recalloc_aligned_at((mi_heap_t*)heap, p, newcount, size, alignment, offset);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void* mi_heap_recalloc_aligned(mi_heap_t* heap, void* p, [NativeTypeName("size_t")] nuint newcount, [NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment)
            => mi_heap_recalloc_aligned_at(heap, p, newcount, size, alignment, 0);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static partial void* mi_heap_recalloc_aligned(IntPtr heap, void* p, nuint newcount, nuint size, nuint alignment) => mi_heap_recalloc_aligned((mi_heap_t*)heap, p, newcount, size, alignment);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static partial void* mi_realloc_aligned_at(void* p, nuint newsize, nuint alignment, nuint offset) => mi_heap_realloc_aligned_at(mi_get_default_heap(), p, newsize, alignment, offset);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static partial void* mi_realloc_aligned(void* p, nuint newsize, nuint alignment) => mi_heap_realloc_aligned(mi_get_default_heap(), p, newsize, alignment);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static partial void* mi_reallocn_aligned_at(void* p, nuint count, nuint size, nuint alignment, nuint offset) => mi_heap_reallocn_aligned_at(mi_get_default_heap(), p, count, size, alignment, offset);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static partial void* mi_reallocn_aligned(void* p, nuint count, nuint size, nuint alignment) => mi_heap_reallocn_aligned(mi_get_default_heap(), p, count, size, alignment);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static partial void* mi_rezalloc_aligned_at(void* p, nuint newsize, nuint alignment, nuint offset) => mi_heap_rezalloc_aligned_at(mi_get_default_heap(), p, newsize, alignment, offset);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static partial void* mi_rezalloc_aligned(void* p, nuint newsize, nuint alignment) => mi_heap_rezalloc_aligned(mi_get_default_heap(), p, newsize, alignment);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static partial void* mi_recalloc_aligned_at(void* p, nuint newcount, nuint size, nuint alignment, nuint offset) => mi_heap_recalloc_aligned_at(mi_get_default_heap(), p, newcount, size, alignment, offset);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static partial void* mi_recalloc_aligned(void* p, nuint newcount, nuint size, nuint alignment) => mi_heap_recalloc_aligned(mi_get_default_heap(), p, newcount, size, alignment);
    }
}
