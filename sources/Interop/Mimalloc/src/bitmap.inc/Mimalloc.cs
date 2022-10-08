// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the bitmap.inc.c file from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace TerraFX.Interop.Mimalloc;

public static unsafe partial class Mimalloc
{
    /* ----------------------------------------------------------------------------
    This file is meant to be included in other files for efficiency.
    It implements a bitmap that can set/reset sequences of bits atomically
    and is used to concurrently claim memory ranges.

    A bitmap is an array of fields where each field is a machine word (`uintptr_t`)

    A current limitation is that the bit sequences cannot cross fields
    and that the sequence must be smaller or equal to the bits in a field.
    ---------------------------------------------------------------------------- */

    /* -----------------------------------------------------------
      Bitmap definition
    ----------------------------------------------------------- */

    // Create a bit index.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return: NativeTypeName("mi_bitmap_index_t")]
    private static nuint mi_bitmap_index_create([NativeTypeName("size_t")] nuint idx, [NativeTypeName("size_t")] nuint bitidx)
    {
        mi_assert_internal((MI_DEBUG > 1) && (bitidx < MI_BITMAP_FIELD_BITS));
        return (idx * MI_BITMAP_FIELD_BITS) + bitidx;
    }

    // Get the field index from a bit index.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return: NativeTypeName("size_t")]
    private static nuint mi_bitmap_index_field([NativeTypeName("mi_bitmap_index_t")] nuint bitmap_idx) => bitmap_idx / MI_BITMAP_FIELD_BITS;

    // Get the bit index in a bitmap field
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return: NativeTypeName("size_t")]
    private static nuint mi_bitmap_index_bit_in_field([NativeTypeName("mi_bitmap_index_t")] nuint bitmap_idx) => bitmap_idx % MI_BITMAP_FIELD_BITS;

    // Get the full bit index
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return: NativeTypeName("size_t")]
    private static nuint mi_bitmap_index_bit([NativeTypeName("mi_bitmap_index_t")] nuint bitmap_idx) => bitmap_idx;

    // The bit mask for a given number of blocks at a specified bit index.
    [return: NativeTypeName("uintptr_t")]
    private static nuint mi_bitmap_mask_([NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint bitidx)
    {
        mi_assert_internal((MI_DEBUG > 1) && ((count + bitidx) <= MI_BITMAP_FIELD_BITS));

        if (count == MI_BITMAP_FIELD_BITS)
        {
            return MI_BITMAP_FIELD_FULL;
        }

        return (((nuint)1 << (int)count) - 1) << (int)bitidx;
    }

    /* -----------------------------------------------------------
      Use bit scan forward/reverse to quickly find the first zero bit if it is available
    ----------------------------------------------------------- */

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return: NativeTypeName("size_t")]
    private static nuint mi_bsf([NativeTypeName("uintptr_t")] nuint x)
    {
        if (Environment.Is64BitProcess)
        {
            return (nuint)BitOperations.TrailingZeroCount(x);
        }
        else
        {
            return (nuint)BitOperations.TrailingZeroCount((uint)x);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return: NativeTypeName("size_t")]
    private static nuint mi_bsr([NativeTypeName("uintptr_t")] nuint x)
    {
        if (Environment.Is64BitProcess)
        {
            return 63 - (nuint)BitOperations.LeadingZeroCount(x);
        }
        else
        {
            return 31 - (nuint)BitOperations.LeadingZeroCount((uint)x);
        }
    }

    /* -----------------------------------------------------------
      Claim a bit sequence atomically
    ----------------------------------------------------------- */

    // Try to atomically claim a sequence of `count` bits at in `idx`
    // in the bitmap field. Returns `true` on success.
    private static bool mi_bitmap_try_claim_field([NativeTypeName("mi_bitmap_t")] nuint* bitmap, [NativeTypeName("size_t")] nuint bitmap_fields, [NativeTypeName("const size_t")] nuint count, [NativeTypeName("mi_bitmap_index_t")] nuint bitmap_idx)
    {
        nuint idx = mi_bitmap_index_field(bitmap_idx);
        nuint bitidx = mi_bitmap_index_bit_in_field(bitmap_idx);
        nuint mask = mi_bitmap_mask_(count, bitidx);

        mi_assert_internal((MI_DEBUG > 1) && (bitmap_fields > idx));
        mi_assert_internal((MI_DEBUG > 1) && ((bitidx + count) <= MI_BITMAP_FIELD_BITS));

        nuint field = mi_atomic_load_relaxed(ref bitmap[idx]);

        if ((field & mask) == 0)
        {
            // free?
            if (mi_atomic_cas_strong_acq_rel(ref bitmap[idx], ref field, field | mask))
            {
                // claimed!
                return true;
            }
        }

        return false;
    }

    // Try to atomically claim a sequence of `count` bits in a single
    // field at `idx` in `bitmap`. Returns `true` on success.
    private static bool mi_bitmap_try_find_claim_field([NativeTypeName("mi_bitmap_t")] nuint* bitmap, [NativeTypeName("size_t")] nuint idx, [NativeTypeName("const size_t")] nuint count, [NativeTypeName("mi_bitmap_index_t*")] out nuint bitmap_idx)
    {
        nuint* field = &bitmap[idx];
        nuint map = mi_atomic_load_relaxed(ref *field);

        if (map == MI_BITMAP_FIELD_FULL)
        {
            // short cut
            bitmap_idx = 0;
            return false;
        }

        // search for 0-bit sequence of length count

        nuint mask = mi_bitmap_mask_(count, 0);
        nuint bitidx_max = MI_BITMAP_FIELD_BITS - count;

        // quickly find the first zero bit if possible
        nuint bitidx = mi_bsf(~map);

        // invariant: m == mask shifted by bitidx
        nuint m = mask << (int)bitidx;

        // scan linearly for a free range of zero bits
        while (bitidx <= bitidx_max)
        {
            if ((map & m) == 0)
            {
                // are the mask bits free at bitidx?

                // no overflow?
                mi_assert_internal((MI_DEBUG > 1) && ((m >> (int)bitidx) == mask));

                nuint newmap = map | m;
                mi_assert_internal((MI_DEBUG > 1) && (((newmap ^ map) >> (int)bitidx) == mask));

                if (!mi_atomic_cas_weak_acq_rel(ref *field, ref map, newmap))
                {
                    // TODO: use strong cas here?
                    // no success, another thread claimed concurrently.. keep going (with updated `map`)
                    continue;
                }
                else
                {
                    // success, we claimed the bits!
                    bitmap_idx = mi_bitmap_index_create(idx, bitidx);
                    return true;
                }
            }
            else
            {
                // on to the next bit range

                nuint shift = (count == 1) ? 1 : (mi_bsr(map & m) - bitidx + 1);
                mi_assert_internal((MI_DEBUG > 1) && (shift > 0) && (shift <= count));

                bitidx += shift;
                m <<= (int)shift;
            }
        }

        // no bits found
        bitmap_idx = 0;
        return false;
    }

    // Find `count` bits of 0 and set them to 1 atomically; returns `true` on success.
    // For now, `count` can be at most MI_BITMAP_FIELD_BITS and will never span fields.
    private static bool mi_bitmap_try_find_claim([NativeTypeName("mi_bitmap_t")] nuint* bitmap, [NativeTypeName("size_t")] nuint bitmap_fields, [NativeTypeName("size_t")] nuint count, [NativeTypeName("mi_bitmap_index_t*")] out nuint bitmap_idx)
    {
        for (nuint idx = 0; idx < bitmap_fields; idx++)
        {
            if (mi_bitmap_try_find_claim_field(bitmap, idx, count, out bitmap_idx))
            {
                return true;
            }
        }

        bitmap_idx = 0;
        return false;
    }

    // Set `count` bits at `bitmap_idx` to 0 atomically
    // Returns `true` if all `count` bits were 1 previously.
    private static bool mi_bitmap_unclaim([NativeTypeName("mi_bitmap_t")] nuint* bitmap, [NativeTypeName("size_t")] nuint bitmap_fields, [NativeTypeName("size_t")] nuint count, [NativeTypeName("mi_bitmap_index_t")] nuint bitmap_idx)
    {
        nuint idx = mi_bitmap_index_field(bitmap_idx);
        nuint bitidx = mi_bitmap_index_bit_in_field(bitmap_idx);
        nuint mask = mi_bitmap_mask_(count, bitidx);

        mi_assert_internal((MI_DEBUG > 1) && (bitmap_fields > idx));

        nuint prev = mi_atomic_and_acq_rel(ref bitmap[idx], ~mask);
        return (prev & mask) == mask;
    }


    // Set `count` bits at `bitmap_idx` to 1 atomically
    // Returns `true` if all `count` bits were 0 previously. `any_zero` is `true` if there was at least one zero bit.
    private static bool mi_bitmap_claim([NativeTypeName("mi_bitmap_t")] nuint* bitmap, [NativeTypeName("size_t")] nuint bitmap_fields, [NativeTypeName("size_t")] nuint count, [NativeTypeName("mi_bitmap_index_t")] nuint bitmap_idx, [NativeTypeName("bool*")] out bool any_zero)
    {
        nuint idx = mi_bitmap_index_field(bitmap_idx);
        nuint bitidx = mi_bitmap_index_bit_in_field(bitmap_idx);
        nuint mask = mi_bitmap_mask_(count, bitidx);

        mi_assert_internal((MI_DEBUG > 1) && (bitmap_fields > idx));
        nuint prev = mi_atomic_or_acq_rel(ref bitmap[idx], mask);

        any_zero = (prev & mask) != mask;
        return (prev & mask) == 0;
    }

    // Returns `true` if all `count` bits were 1. `any_ones` is `true` if there was at least one bit set to one.
    private static bool mi_bitmap_is_claimedx([NativeTypeName("mi_bitmap_t")] nuint* bitmap, [NativeTypeName("size_t")] nuint bitmap_fields, [NativeTypeName("size_t")] nuint count, [NativeTypeName("mi_bitmap_index_t")] nuint bitmap_idx, [NativeTypeName("bool*")] out bool any_ones)
    {
        nuint idx = mi_bitmap_index_field(bitmap_idx);
        nuint bitidx = mi_bitmap_index_bit_in_field(bitmap_idx);
        nuint mask = mi_bitmap_mask_(count, bitidx);

        mi_assert_internal((MI_DEBUG > 1) && (bitmap_fields > idx));
        nuint field = mi_atomic_load_relaxed(ref bitmap[idx]);

        any_ones = (field & mask) != 0;
        return (field & mask) == mask;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool mi_bitmap_is_claimed([NativeTypeName("mi_bitmap_t")] nuint* bitmap, [NativeTypeName("size_t")] nuint bitmap_fields, [NativeTypeName("size_t")] nuint count, [NativeTypeName("mi_bitmap_index_t")] nuint bitmap_idx)
        => mi_bitmap_is_claimedx(bitmap, bitmap_fields, count, bitmap_idx, out _);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool mi_bitmap_is_any_claimed([NativeTypeName("mi_bitmap_t")] nuint* bitmap, [NativeTypeName("size_t")] nuint bitmap_fields, [NativeTypeName("size_t")] nuint count, [NativeTypeName("mi_bitmap_index_t")] nuint bitmap_idx)
    {
        _ = mi_bitmap_is_claimedx(bitmap, bitmap_fields, count, bitmap_idx, out bool any_ones);
        return any_ones;
    }
}
