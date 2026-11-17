// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the alloc-aligned.c file from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

using System;
using System.Runtime.CompilerServices;

namespace TerraFX.Interop.Mimalloc;

public static unsafe partial class Mimalloc
{
    // ------------------------------------------------------
    // Aligned Allocation
    // ------------------------------------------------------

    private static bool mi_malloc_is_naturally_aligned(nuint size, nuint alignment)
    {
        mi_assert_internal((MI_DEBUG > 1) && (alignment != 0) && _mi_is_power_of_two(alignment));

        if (alignment > size)
        {
            return false;
        }

        if (alignment <= MI_MAX_ALIGN_SIZE)
        {
            return true;
        }

        nuint block_size = mi_good_size(size);
        return (block_size <= MI_MAX_ALIGN_GUARANTEE) && ((block_size & (alignment - 1)) == 0);
    }

    private static void* mi_heap_malloc_zero_no_guarded(mi_heap_t* heap, nuint size, bool zero)
    {
        nuint sample_rate = heap->guarded_sample_rate;
        if (sample_rate != 0)
        {
            heap->guarded_sample_rate = 0;
        }

        void* p = _mi_heap_malloc_zero(heap, size, zero);
        if (sample_rate != 0)
        {
            heap->guarded_sample_rate = sample_rate;
        }
        return p;
    }

    private static void* mi_heap_malloc_guarded_aligned(mi_heap_t* heap, nuint size, nuint alignment, bool zero)
    {
        if ((alignment >= MI_BLOCK_ALIGNMENT_MAX) || (size > (MI_MAX_ALLOC_SIZE - MI_PADDING_SIZE - alignment)))
        {
            _mi_error_message(EOVERFLOW, "(guarded) aligned allocation request is too large (size {0}, alignment {1})\n", size, alignment);
            return null;
        }

        nuint oversize = size + alignment - 1;
        void* base_pointer = mi_heap_malloc_guarded(heap, oversize, zero);
        if (base_pointer == null)
        {
            return null;
        }

        void* p = (void*)_mi_align_up((nuint)base_pointer, alignment);
        mi_assert_internal((MI_DEBUG > 1) && (mi_usable_size(p) >= size));
        mi_assert_internal((MI_DEBUG > 1) && (((nuint)p & (alignment - 1)) == 0));
        return p;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void* mi_heap_malloc_zero_aligned_at_overalloc(mi_heap_t* heap, nuint size, nuint alignment, nuint offset, bool zero)
    {
        mi_assert_internal((MI_DEBUG > 1) && (size <= (MI_MAX_ALLOC_SIZE - MI_PADDING_SIZE)));
        mi_assert_internal((MI_DEBUG > 1) && (alignment != 0) && _mi_is_power_of_two(alignment));

        nuint oversize;
        void* p;

        if (mi_unlikely(alignment > MI_BLOCK_ALIGNMENT_MAX))
        {
            if (mi_unlikely(offset != 0))
            {
                if (MI_DEBUG > 0)
                {
                    _mi_error_message(EOVERFLOW, "aligned allocation with a very large alignment cannot be used with an alignment offset (size {0}, alignment {1}, offset {2})\n", size, alignment, offset);
                }
                return null;
            }

            oversize = (size <= MI_SMALL_SIZE_MAX) ? (MI_SMALL_SIZE_MAX + 1) : size;
            p = _mi_heap_malloc_zero_ex(heap, oversize, false, alignment);
        }
        else
        {
            mi_assert_internal((MI_DEBUG > 1) && (size < (SIZE_MAX - alignment)));
            oversize = ((size < MI_MAX_ALIGN_SIZE) ? MI_MAX_ALIGN_SIZE : size) + alignment - 1;
            p = mi_heap_malloc_zero_no_guarded(heap, oversize, zero);
        }

        if (p == null)
        {
            return null;
        }

        // .. and align within the allocation
        nuint align_mask = alignment - 1;
        nuint poffset = ((nuint)p + offset) & align_mask;
        nuint adjust = (poffset == 0) ? 0 : (alignment - poffset);

        mi_assert_internal((MI_DEBUG > 1) && (adjust < alignment));

        void* aligned_p = (void*)((nuint)p + adjust);

        if (aligned_p != p)
        {
            mi_page_set_has_aligned(_mi_ptr_page(p), true);
        }

        mi_assert_internal((MI_DEBUG > 1) && (((nuint)aligned_p + offset) % alignment == 0));
        mi_assert_internal((MI_DEBUG > 1) && (p == _mi_page_ptr_unalign(_mi_ptr_page(aligned_p), aligned_p)));
        mi_assert_internal((MI_DEBUG > 1) && (mi_page_usable_block_size(_mi_ptr_page(p)) >= (adjust + size)));

        if (zero && (alignment > MI_BLOCK_ALIGNMENT_MAX))
        {
            _ = memset(aligned_p, 0, mi_usable_size(aligned_p));
        }

        return aligned_p;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void* mi_heap_malloc_zero_aligned_at_generic(mi_heap_t* heap, nuint size, nuint alignment, nuint offset, bool zero)
    {
        mi_assert_internal((MI_DEBUG > 1) && (alignment != 0) && _mi_is_power_of_two(alignment));

        if (mi_unlikely(size > (MI_MAX_ALLOC_SIZE - MI_PADDING_SIZE)))
        {
            _mi_error_message(EINVAL, "aligned allocation request is too large (size {0}, alignment {1})\n", size, alignment);
            return null;
        }

        if ((offset == 0) && mi_malloc_is_naturally_aligned(size, alignment))
        {
            void* p = mi_heap_malloc_zero_no_guarded(heap, size, zero);
            mi_assert_internal((MI_DEBUG > 1) && ((p == null) || (((nuint)p % alignment) == 0)));

            if ((p == null) || (((nuint)p & (alignment - 1)) == 0))
            {
                return p;
            }

            mi_assert((MI_DEBUG != 0) && false);
            mi_free(p);
        }

        return mi_heap_malloc_zero_aligned_at_overalloc(heap, size, alignment, offset, zero);
    }

    private static void* mi_error_bad_alignment(nuint size, nuint alignment, nuint offset)
    {
        _mi_error_message(EINVAL, "aligned allocation requires the alignment to be a power-of-two (size {0}, alignment {1}, offset {2})\n", size, alignment, offset);
        return null;
    }

    private static void* mi_heap_malloc_zero_aligned_at([NativeTypeName("mi_heap_t* const")] mi_heap_t* heap, [NativeTypeName("const size_t")] nuint size, [NativeTypeName("const size_t")] nuint alignment, [NativeTypeName("const size_t")] nuint offset, [NativeTypeName("const bool")] bool zero)
    {
        // note: we don't require `size > offset`, we just guarantee that the address at offset is aligned regardless of the allocated size.
        if (mi_unlikely((alignment == 0) || !_mi_is_power_of_two(alignment)))
        {
            return mi_error_bad_alignment(size, alignment, offset);
        }

        if ((offset == 0) && (alignment < MI_BLOCK_ALIGNMENT_MAX) && mi_heap_malloc_use_guarded(heap, size))
        {
            return mi_heap_malloc_guarded_aligned(heap, size, alignment, zero);
        }

        if (mi_likely((size <= MI_SMALL_SIZE_MAX) && (alignment <= size)))
        {
            nuint align_mask = alignment - 1;
            nuint padsize = size + MI_PADDING_SIZE;
            mi_page_t* page = _mi_heap_get_free_small_page(heap, padsize);

            if (mi_likely(page->free != null))
            {
                bool is_aligned = (((nuint)page->free + offset) & align_mask) == 0;

                if (mi_likely(is_aligned))
                {
                    if (MI_STAT > 1)
                    {
                        mi_stat_increase(ref heap->tld->stats.malloc, size);
                    }

                    void* p = zero ? _mi_page_malloc_zeroed(heap, page, padsize) : _mi_page_malloc(heap, page, padsize);

                    mi_assert_internal((MI_DEBUG > 1) && (p != null));
                    mi_assert_internal((MI_DEBUG > 1) && (((nuint)p + offset) % alignment == 0));

                    return p;
                }
            }
        }

        return mi_heap_malloc_zero_aligned_at_generic(heap, size, alignment, offset, zero);
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void* mi_heap_malloc_aligned_at(mi_heap_t* heap, [NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment, [NativeTypeName("size_t")] nuint offset)
        => mi_heap_malloc_zero_aligned_at(heap, size, alignment, offset, false);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void* mi_heap_malloc_aligned_at(IntPtr heap, nuint size, nuint alignment, nuint offset) => mi_heap_malloc_aligned_at((mi_heap_t*)heap, size, alignment, offset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void* mi_heap_malloc_aligned(mi_heap_t* heap, [NativeTypeName("size_t")] nuint size, [NativeTypeName("size_t")] nuint alignment)
        => mi_heap_malloc_aligned_at(heap, size, alignment, 0);

    public static partial void* mi_heap_malloc_aligned(IntPtr heap, nuint size, nuint alignment) => mi_heap_malloc_aligned((mi_heap_t*)heap, size, alignment);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void* mi_theap_malloc_aligned(IntPtr heap, nuint size, nuint alignment) => mi_heap_malloc_aligned(heap, size, alignment);

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
        if ((alignment == 0) || !_mi_is_power_of_two(alignment))
        {
            return mi_error_bad_alignment(newsize, alignment, offset);
        }

        mi_assert((MI_DEBUG != 0) && (alignment > 0));

        if ((alignment <= SizeOf<nuint>()) && (offset == 0))
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
                nuint copy_size = (newsize > size) ? size : newsize;
                if (zero)
                {
                    nuint zero_start = (copy_size >= SizeOf<nuint>()) ? copy_size - SizeOf<nuint>() : 0;
                    zero_start = _mi_align_down(zero_start, SizeOf<nuint>());
                    nuint usable = mi_usable_size(newp);
                    mi_assert_internal((MI_DEBUG > 1) && (usable >= newsize));

                    if (usable > zero_start)
                    {
                        // also set last word in the previous allocation to zero to ensure any padding is zero-initialized
                        _ = memset((byte*)newp + zero_start, 0, usable - zero_start);
                    }
                }

                _ = memcpy(newp, p, copy_size);

                // only free if successful
                mi_free(p);
            }

            return newp;
        }
    }

    private static void* mi_heap_realloc_zero_aligned(mi_heap_t* heap, void* p, [NativeTypeName("size_t")] nuint newsize, [NativeTypeName("size_t")] nuint alignment, bool zero)
    {
        if ((alignment == 0) || !_mi_is_power_of_two(alignment))
        {
            return mi_error_bad_alignment(newsize, alignment, 0);
        }

        mi_assert((MI_DEBUG != 0) && (alignment > 0));

        if (alignment <= SizeOf<nuint>())
        {
            return _mi_heap_realloc_zero(heap, p, newsize, zero);
        }

        return mi_heap_realloc_zero_aligned_at(heap, p, newsize, alignment, 0, zero);
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
