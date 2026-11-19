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

    Basic operations are limited to a single field; arena operations can span fields.
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

        if (count >= MI_BITMAP_FIELD_BITS)
        {
            return MI_BITMAP_FIELD_FULL;
        }

        if (count == 0)
        {
            return 0;
        }

        return (((nuint)1 << (int)count) - 1) << (int)bitidx;
    }

    /* -----------------------------------------------------------
      Count leading or trailing zeros
    ----------------------------------------------------------- */

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return: NativeTypeName("size_t")]
    private static nuint mi_clz([NativeTypeName("uintptr_t")] nuint x)
    {
        if (Environment.Is64BitProcess)
        {
            return (nuint)BitOperations.LeadingZeroCount(x);
        }
        else
        {
            return (nuint)BitOperations.LeadingZeroCount((uint)x);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return: NativeTypeName("size_t")]
    private static nuint mi_ctz([NativeTypeName("uintptr_t")] nuint x)
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
        => (x == 0) ? MI_BITMAP_FIELD_BITS : (MI_BITMAP_FIELD_BITS - 1 - mi_clz(x));

    /* -----------------------------------------------------------
      Claim a bit sequence atomically
    ----------------------------------------------------------- */

    // Try to atomically claim a sequence of `count` bits in a single
    // field at `idx` in `bitmap`. Returns `true` on success.
    private static bool mi_bitmap_try_find_claim_field([NativeTypeName("mi_bitmap_t")] nuint* bitmap, [NativeTypeName("size_t")] nuint idx, [NativeTypeName("const size_t")] nuint count, [NativeTypeName("mi_bitmap_index_t*")] out nuint bitmap_idx)
    {
        mi_assert_internal((MI_DEBUG > 1) && (count <= MI_BITMAP_FIELD_BITS));

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
        nuint bitidx = mi_ctz(~map);

        // invariant: m == mask shifted by bitidx
        nuint m = mask << (int)bitidx;

        // scan linearly for a free range of zero bits
        while (bitidx <= bitidx_max)
        {
            nuint mapm = map & m;

            if (mapm == 0)
            {
                // are the mask bits free at bitidx?

                // no overflow?
                mi_assert_internal((MI_DEBUG > 1) && ((m >> (int)bitidx) == mask));

                nuint newmap = map | m;
                mi_assert_internal((MI_DEBUG > 1) && (((newmap ^ map) >> (int)bitidx) == mask));

                if (!mi_atomic_cas_strong_acq_rel(ref *field, ref map, newmap))
                {
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

                mi_assert_internal((MI_DEBUG > 1) && (mapm != 0));
                nuint shift = (count == 1) ? 1 : (MI_BITMAP_FIELD_BITS - mi_clz(mapm) - bitidx);
                mi_assert_internal((MI_DEBUG > 1) && (shift > 0) && (shift <= count));

                bitidx += shift;
                m <<= (int)shift;
            }
        }

        // no bits found
        bitmap_idx = 0;
        return false;
    }

    // Starts at idx, and wraps around to search in all `bitmap_fields` fields.
    private static bool mi_bitmap_try_find_from_claim([NativeTypeName("mi_bitmap_t")] nuint* bitmap, [NativeTypeName("size_t")] nuint bitmap_fields, [NativeTypeName("size_t")] nuint start_field_idx, [NativeTypeName("size_t")] nuint count, [NativeTypeName("mi_bitmap_index_t*")] out nuint bitmap_idx)
    {
        nuint idx = start_field_idx;

        for (nuint visited = 0; visited < bitmap_fields; visited++, idx++)
        {
            if (idx >= bitmap_fields)
            {
                idx = 0;
            }

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

    // Try to claim a free sequence that starts in this field and crosses subsequent fields.
    private static bool mi_bitmap_try_find_claim_field_across([NativeTypeName("mi_bitmap_t")] nuint* bitmap, [NativeTypeName("size_t")] nuint bitmap_fields, [NativeTypeName("size_t")] nuint idx, [NativeTypeName("size_t")] nuint count, [NativeTypeName("size_t")] nuint retries, [NativeTypeName("mi_bitmap_index_t*")] out nuint bitmap_idx)
    {
        nuint* field = &bitmap[idx];
        nuint map = mi_atomic_load_relaxed(ref *field);
        nuint initial = mi_clz(map);
        nuint initial_idx = MI_BITMAP_FIELD_BITS - initial;

        if (initial == 0)
        {
            bitmap_idx = 0;
            return false;
        }

        if (initial >= count)
        {
            return mi_bitmap_try_find_claim_field(bitmap, idx, count, out bitmap_idx);
        }

        if (_mi_divide_up(count - initial, MI_BITMAP_FIELD_BITS) >= (bitmap_fields - idx))
        {
            bitmap_idx = 0;
            return false;
        }

        // Check that every field in the proposed range is still free.
        nuint found = initial;
        nuint mask = 0;

        while (found < count)
        {
            field++;
            map = mi_atomic_load_relaxed(ref *field);

            nuint mask_bits = (count - found >= MI_BITMAP_FIELD_BITS) ? MI_BITMAP_FIELD_BITS : (count - found);
            mi_assert_internal((MI_DEBUG > 1) && (mask_bits > 0) && (mask_bits <= MI_BITMAP_FIELD_BITS));
            mask = mi_bitmap_mask_(mask_bits, 0);

            if ((map & mask) != 0)
            {
                bitmap_idx = 0;
                return false;
            }

            found += mask_bits;
        }

        nuint* final_field = field;
        nuint final_mask = mask;
        nuint* initial_field = &bitmap[idx];
        nuint initial_mask = mi_bitmap_mask_(initial, initial_idx);
        nuint newmap;
        nuint middle_claimed = 0;
        bool initial_claimed = false;
        bool failed = false;

        // Claim the trailing zeros in the initial field.
        field = initial_field;
        map = mi_atomic_load_relaxed(ref *field);

        while (true)
        {
            if ((map & initial_mask) != 0)
            {
                failed = true;
                break;
            }

            newmap = map | initial_mask;

            if (mi_atomic_cas_strong_acq_rel(ref *field, ref map, newmap))
            {
                initial_claimed = true;
                break;
            }
        }

        // Claim any full fields between the initial and final fields.
        if (!failed)
        {
            for (field = initial_field + 1; field < final_field; field++)
            {
                map = 0;
                newmap = MI_BITMAP_FIELD_FULL;

                if (!mi_atomic_cas_strong_acq_rel(ref *field, ref map, newmap))
                {
                    failed = true;
                    break;
                }

                middle_claimed++;
            }
        }

        // Claim the remaining bits in the final field.
        if (!failed)
        {
            field = final_field;
            map = mi_atomic_load_relaxed(ref *field);

            while (true)
            {
                if ((map & final_mask) != 0)
                {
                    failed = true;
                    break;
                }

                newmap = map | final_mask;

                if (mi_atomic_cas_strong_acq_rel(ref *field, ref map, newmap))
                {
                    bitmap_idx = mi_bitmap_index_create(idx, initial_idx);
                    return true;
                }
            }
        }

        // Roll back fields claimed by this attempt before retrying.
        for (nuint i = 0; i < middle_claimed; i++)
        {
            nuint* rollback_field = initial_field + 1 + i;
            mi_assert_internal((MI_DEBUG > 1) && (mi_atomic_load_relaxed(ref *rollback_field) == MI_BITMAP_FIELD_FULL));
            mi_atomic_store_release(ref *rollback_field, 0);
        }

        if (initial_claimed)
        {
            map = mi_atomic_load_relaxed(ref *initial_field);

            while (true)
            {
                mi_assert_internal((MI_DEBUG > 1) && ((map & initial_mask) == initial_mask));
                newmap = map & ~initial_mask;

                if (mi_atomic_cas_strong_acq_rel(ref *initial_field, ref map, newmap))
                {
                    break;
                }
            }
        }

        mi_stat_counter_increase(ref _mi_stats_main.arena_rollback_count, 1);

        if (retries <= 2)
        {
            return mi_bitmap_try_find_claim_field_across(bitmap, bitmap_fields, idx, count, retries + 1, out bitmap_idx);
        }

        bitmap_idx = 0;
        return false;
    }

    private static bool mi_bitmap_try_find_from_claim_across([NativeTypeName("mi_bitmap_t")] nuint* bitmap, [NativeTypeName("size_t")] nuint bitmap_fields, [NativeTypeName("size_t")] nuint start_field_idx, [NativeTypeName("size_t")] nuint count, [NativeTypeName("mi_bitmap_index_t*")] out nuint bitmap_idx)
    {
        mi_assert_internal((MI_DEBUG > 1) && (count > 0));

        if (count <= 2)
        {
            return mi_bitmap_try_find_from_claim(bitmap, bitmap_fields, start_field_idx, count, out bitmap_idx);
        }

        nuint idx = start_field_idx;

        for (nuint visited = 0; visited < bitmap_fields; visited++, idx++)
        {
            if (idx >= bitmap_fields)
            {
                idx = 0;
            }

            if ((count <= MI_BITMAP_FIELD_BITS) && mi_bitmap_try_find_claim_field(bitmap, idx, count, out bitmap_idx))
            {
                return true;
            }

            if (mi_bitmap_try_find_claim_field_across(bitmap, bitmap_fields, idx, count, 0, out bitmap_idx))
            {
                return true;
            }
        }

        bitmap_idx = 0;
        return false;
    }

    [return: NativeTypeName("size_t")]
    private static nuint mi_bitmap_mask_across([NativeTypeName("mi_bitmap_index_t")] nuint bitmap_idx, [NativeTypeName("size_t")] nuint bitmap_fields, [NativeTypeName("size_t")] nuint count, [NativeTypeName("uintptr_t*")] out nuint pre_mask, [NativeTypeName("uintptr_t*")] out nuint mid_mask, [NativeTypeName("uintptr_t*")] out nuint post_mask)
    {
        nuint bitidx = mi_bitmap_index_bit_in_field(bitmap_idx);

        if (count <= (MI_BITMAP_FIELD_BITS - bitidx))
        {
            pre_mask = mi_bitmap_mask_(count, bitidx);
            mid_mask = 0;
            post_mask = 0;
            mi_assert_internal((MI_DEBUG > 1) && (mi_bitmap_index_field(bitmap_idx) < bitmap_fields));
            return 0;
        }

        nuint pre_bits = MI_BITMAP_FIELD_BITS - bitidx;
        mi_assert_internal((MI_DEBUG > 1) && (pre_bits < count));

        pre_mask = mi_bitmap_mask_(pre_bits, bitidx);
        count -= pre_bits;

        nuint mid_count = count / MI_BITMAP_FIELD_BITS;
        mid_mask = MI_BITMAP_FIELD_FULL;
        count %= MI_BITMAP_FIELD_BITS;
        post_mask = (count == 0) ? 0 : mi_bitmap_mask_(count, 0);

        mi_assert_internal((MI_DEBUG > 1) && ((mi_bitmap_index_field(bitmap_idx) + mid_count + ((count == 0) ? 0u : 1u)) < bitmap_fields));
        return mid_count;
    }

    private static bool mi_bitmap_unclaim_across([NativeTypeName("mi_bitmap_t")] nuint* bitmap, [NativeTypeName("size_t")] nuint bitmap_fields, [NativeTypeName("size_t")] nuint count, [NativeTypeName("mi_bitmap_index_t")] nuint bitmap_idx)
    {
        nuint idx = mi_bitmap_index_field(bitmap_idx);
        nuint mid_count = mi_bitmap_mask_across(bitmap_idx, bitmap_fields, count, out nuint pre_mask, out nuint mid_mask, out nuint post_mask);
        bool all_one = true;
        nuint* field = &bitmap[idx];

        nuint prev = mi_atomic_and_acq_rel(ref *field++, ~pre_mask);

        if ((prev & pre_mask) != pre_mask)
        {
            all_one = false;
        }

        while (mid_count > 0)
        {
            prev = mi_atomic_and_acq_rel(ref *field++, ~mid_mask);

            if ((prev & mid_mask) != mid_mask)
            {
                all_one = false;
            }

            mid_count--;
        }

        if (post_mask != 0)
        {
            prev = mi_atomic_and_acq_rel(ref *field, ~post_mask);

            if ((prev & post_mask) != post_mask)
            {
                all_one = false;
            }
        }

        return all_one;
    }

    private static bool mi_bitmap_claim_across([NativeTypeName("mi_bitmap_t")] nuint* bitmap, [NativeTypeName("size_t")] nuint bitmap_fields, [NativeTypeName("size_t")] nuint count, [NativeTypeName("mi_bitmap_index_t")] nuint bitmap_idx, [NativeTypeName("bool*")] out bool any_zero)
    {
        nuint idx = mi_bitmap_index_field(bitmap_idx);
        nuint mid_count = mi_bitmap_mask_across(bitmap_idx, bitmap_fields, count, out nuint pre_mask, out nuint mid_mask, out nuint post_mask);
        bool all_zero = true;
        any_zero = false;
        nuint* field = &bitmap[idx];

        nuint prev = mi_atomic_or_acq_rel(ref *field++, pre_mask);

        if ((prev & pre_mask) != 0)
        {
            all_zero = false;
        }

        if ((prev & pre_mask) != pre_mask)
        {
            any_zero = true;
        }

        while (mid_count > 0)
        {
            prev = mi_atomic_or_acq_rel(ref *field++, mid_mask);

            if ((prev & mid_mask) != 0)
            {
                all_zero = false;
            }

            if ((prev & mid_mask) != mid_mask)
            {
                any_zero = true;
            }

            mid_count--;
        }

        if (post_mask != 0)
        {
            prev = mi_atomic_or_acq_rel(ref *field, post_mask);

            if ((prev & post_mask) != 0)
            {
                all_zero = false;
            }

            if ((prev & post_mask) != post_mask)
            {
                any_zero = true;
            }
        }

        return all_zero;
    }

    private static bool mi_bitmap_is_claimedx_across([NativeTypeName("mi_bitmap_t")] nuint* bitmap, [NativeTypeName("size_t")] nuint bitmap_fields, [NativeTypeName("size_t")] nuint count, [NativeTypeName("mi_bitmap_index_t")] nuint bitmap_idx, [NativeTypeName("bool*")] out bool any_ones)
    {
        nuint idx = mi_bitmap_index_field(bitmap_idx);
        nuint mid_count = mi_bitmap_mask_across(bitmap_idx, bitmap_fields, count, out nuint pre_mask, out nuint mid_mask, out nuint post_mask);
        bool all_ones = true;
        any_ones = false;
        nuint* field = &bitmap[idx];

        nuint prev = mi_atomic_load_relaxed(ref *field++);

        if ((prev & pre_mask) != pre_mask)
        {
            all_ones = false;
        }

        if ((prev & pre_mask) != 0)
        {
            any_ones = true;
        }

        while (mid_count > 0)
        {
            prev = mi_atomic_load_relaxed(ref *field++);

            if ((prev & mid_mask) != mid_mask)
            {
                all_ones = false;
            }

            if ((prev & mid_mask) != 0)
            {
                any_ones = true;
            }

            mid_count--;
        }

        if (post_mask != 0)
        {
            prev = mi_atomic_load_relaxed(ref *field);

            if ((prev & post_mask) != post_mask)
            {
                all_ones = false;
            }

            if ((prev & post_mask) != 0)
            {
                any_ones = true;
            }
        }

        return all_ones;
    }

    private static bool mi_bitmap_is_claimed_across([NativeTypeName("mi_bitmap_t")] nuint* bitmap, [NativeTypeName("size_t")] nuint bitmap_fields, [NativeTypeName("size_t")] nuint count, [NativeTypeName("mi_bitmap_index_t")] nuint bitmap_idx)
        => mi_bitmap_is_claimedx_across(bitmap, bitmap_fields, count, bitmap_idx, out _);

    private static bool mi_bitmap_is_any_claimed_across([NativeTypeName("mi_bitmap_t")] nuint* bitmap, [NativeTypeName("size_t")] nuint bitmap_fields, [NativeTypeName("size_t")] nuint count, [NativeTypeName("mi_bitmap_index_t")] nuint bitmap_idx)
    {
        _ = mi_bitmap_is_claimedx_across(bitmap, bitmap_fields, count, bitmap_idx, out bool any_ones);
        return any_ones;
    }
}
