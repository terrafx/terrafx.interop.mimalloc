// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the segment-map.c file from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

namespace TerraFX.Interop.Mimalloc;

public static unsafe partial class Mimalloc
{
    private static nuint mi_segment_map_index_of(mi_segment_t* segment, out nuint bitidx)
    {
        mi_assert_internal((MI_DEBUG > 1) && (_mi_ptr_segment(segment + 1) == segment));

        nuint address = (nuint)segment;

        if (address >= MI_SEGMENT_MAP_MAX_ADDRESS)
        {
            bitidx = 0;
            return MI_SEGMENT_MAP_WSIZE;
        }

        nuint segment_index = address / MI_SEGMENT_SIZE;
        bitidx = segment_index % MI_BITMAP_FIELD_BITS;
        return segment_index / MI_BITMAP_FIELD_BITS;
    }

    private static void _mi_segment_map_allocated_at(mi_segment_t* segment)
    {
        nuint index = mi_segment_map_index_of(segment, out nuint bitidx);
        mi_assert_internal((MI_DEBUG > 1) && (index <= MI_SEGMENT_MAP_WSIZE));

        if (index == MI_SEGMENT_MAP_WSIZE)
        {
            return;
        }

        nuint* field = &mi_segment_map[index];
        nuint mask = mi_atomic_load_relaxed(ref *field);
        nuint set = (nuint)1 << (int)bitidx;
        nuint newmask;

        do
        {
            newmask = mask | set;
        }
        while (!mi_atomic_cas_weak_release(ref *field, ref mask, newmask));
    }

    private static void _mi_segment_map_freed_at(mi_segment_t* segment)
    {
        nuint index = mi_segment_map_index_of(segment, out nuint bitidx);
        mi_assert_internal((MI_DEBUG > 1) && (index <= MI_SEGMENT_MAP_WSIZE));

        if (index == MI_SEGMENT_MAP_WSIZE)
        {
            return;
        }

        nuint* field = &mi_segment_map[index];
        nuint mask = mi_atomic_load_relaxed(ref *field);
        nuint clear = ~((nuint)1 << (int)bitidx);
        nuint newmask;

        do
        {
            newmask = mask & clear;
        }
        while (!mi_atomic_cas_weak_release(ref *field, ref mask, newmask));
    }

    private static mi_segment_t* _mi_segment_of(void* p)
    {
        if (p == null)
        {
            return null;
        }

        mi_segment_t* segment = _mi_ptr_segment(p);
        nuint index = mi_segment_map_index_of(segment, out nuint bitidx);
        nuint mask = mi_atomic_load_relaxed(ref mi_segment_map[index]);
        nuint bit = (nuint)1 << (int)bitidx;

        if ((mask & bit) != 0)
        {
            return segment;
        }

        if (index == MI_SEGMENT_MAP_WSIZE)
        {
            return null;
        }

        nuint lower_bits = mask & (((nuint)1 << (int)bitidx) - 1);
        nuint lower_index;
        nuint lower_bitidx;

        if (lower_bits != 0)
        {
            lower_index = index;
            lower_bitidx = mi_bsr(lower_bits);
        }
        else if (index == 0)
        {
            return null;
        }
        else
        {
            nuint lower_mask = mask;
            lower_index = index;

            do
            {
                lower_index--;
                lower_mask = mi_atomic_load_relaxed(ref mi_segment_map[lower_index]);
            }
            while ((lower_mask != 0) && (lower_index > 0));

            if (lower_mask == 0)
            {
                return null;
            }

            lower_bitidx = mi_bsr(lower_mask);
        }

        nuint diff = (((index - lower_index) * MI_BITMAP_FIELD_BITS) + bitidx - lower_bitidx) * MI_SEGMENT_SIZE;
        segment = (mi_segment_t*)((byte*)segment - diff);

        if ((byte*)segment >= (byte*)p)
        {
            return null;
        }

        bool cookie_ok = _mi_ptr_cookie(segment) == segment->cookie;
        mi_assert_internal((MI_DEBUG > 1) && cookie_ok);

        if (!cookie_ok || (((byte*)segment + segment->segment_size) <= (byte*)p))
        {
            return null;
        }

        return segment;
    }
}
