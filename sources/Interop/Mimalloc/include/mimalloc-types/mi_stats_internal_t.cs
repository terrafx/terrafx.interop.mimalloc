// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on mimalloc's internal statistics type.
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

namespace TerraFX.Interop.Mimalloc;

internal struct mi_stats_internal_t
{
    public mi_stat_count_internal_t segments;

    public mi_stat_count_internal_t pages;

    public mi_stat_count_internal_t reserved;

    public mi_stat_count_internal_t committed;

    public mi_stat_count_internal_t reset;

    public mi_stat_count_internal_t purged;

    public mi_stat_count_internal_t page_committed;

    public mi_stat_count_internal_t segments_abandoned;

    public mi_stat_count_internal_t pages_abandoned;

    public mi_stat_count_internal_t threads;

    public mi_stat_count_internal_t normal;

    public mi_stat_count_internal_t huge;

    public mi_stat_count_internal_t malloc;

    public mi_stat_count_internal_t malloc_requested;

    public mi_stat_count_internal_t segments_cache;

    public mi_stat_counter_internal_t pages_extended;

    public mi_stat_counter_internal_t arena_count;

    public mi_stat_counter_internal_t arena_rollback_count;

    public mi_stat_counter_internal_t arena_purges;

    public mi_stat_counter_internal_t pages_retire;

    public mi_stat_counter_internal_t mmap_calls;

    public mi_stat_counter_internal_t commit_calls;

    public mi_stat_counter_internal_t reset_calls;

    public mi_stat_counter_internal_t purge_calls;

    public mi_stat_counter_internal_t page_no_retire;

    public mi_stat_counter_internal_t searches;

    public mi_stat_counter_internal_t normal_count;

    public mi_stat_counter_internal_t huge_count;

    public mi_stat_counter_internal_t malloc_guarded_count;

    [NativeTypeName("mi_stat_count_t [MI_BIN_HUGE + 1]")]
    public _normal_bins_e__FixedBuffer normal_bins;

    [NativeTypeName("mi_stat_count_t [MI_BIN_HUGE + 1]")]
    public _normal_bins_e__FixedBuffer page_bins;

    public partial struct _normal_bins_e__FixedBuffer
    {
        public mi_stat_count_internal_t e0;
        public mi_stat_count_internal_t e1;
        public mi_stat_count_internal_t e2;
        public mi_stat_count_internal_t e3;
        public mi_stat_count_internal_t e4;
        public mi_stat_count_internal_t e5;
        public mi_stat_count_internal_t e6;
        public mi_stat_count_internal_t e7;
        public mi_stat_count_internal_t e8;
        public mi_stat_count_internal_t e9;
        public mi_stat_count_internal_t e10;
        public mi_stat_count_internal_t e11;
        public mi_stat_count_internal_t e12;
        public mi_stat_count_internal_t e13;
        public mi_stat_count_internal_t e14;
        public mi_stat_count_internal_t e15;
        public mi_stat_count_internal_t e16;
        public mi_stat_count_internal_t e17;
        public mi_stat_count_internal_t e18;
        public mi_stat_count_internal_t e19;
        public mi_stat_count_internal_t e20;
        public mi_stat_count_internal_t e21;
        public mi_stat_count_internal_t e22;
        public mi_stat_count_internal_t e23;
        public mi_stat_count_internal_t e24;
        public mi_stat_count_internal_t e25;
        public mi_stat_count_internal_t e26;
        public mi_stat_count_internal_t e27;
        public mi_stat_count_internal_t e28;
        public mi_stat_count_internal_t e29;
        public mi_stat_count_internal_t e30;
        public mi_stat_count_internal_t e31;
        public mi_stat_count_internal_t e32;
        public mi_stat_count_internal_t e33;
        public mi_stat_count_internal_t e34;
        public mi_stat_count_internal_t e35;
        public mi_stat_count_internal_t e36;
        public mi_stat_count_internal_t e37;
        public mi_stat_count_internal_t e38;
        public mi_stat_count_internal_t e39;
        public mi_stat_count_internal_t e40;
        public mi_stat_count_internal_t e41;
        public mi_stat_count_internal_t e42;
        public mi_stat_count_internal_t e43;
        public mi_stat_count_internal_t e44;
        public mi_stat_count_internal_t e45;
        public mi_stat_count_internal_t e46;
        public mi_stat_count_internal_t e47;
        public mi_stat_count_internal_t e48;
        public mi_stat_count_internal_t e49;
        public mi_stat_count_internal_t e50;
        public mi_stat_count_internal_t e51;
        public mi_stat_count_internal_t e52;
        public mi_stat_count_internal_t e53;
        public mi_stat_count_internal_t e54;
        public mi_stat_count_internal_t e55;
        public mi_stat_count_internal_t e56;
        public mi_stat_count_internal_t e57;
        public mi_stat_count_internal_t e58;
        public mi_stat_count_internal_t e59;
        public mi_stat_count_internal_t e60;
        public mi_stat_count_internal_t e61;
        public mi_stat_count_internal_t e62;
        public mi_stat_count_internal_t e63;
        public mi_stat_count_internal_t e64;
        public mi_stat_count_internal_t e65;
        public mi_stat_count_internal_t e66;
        public mi_stat_count_internal_t e67;
        public mi_stat_count_internal_t e68;
        public mi_stat_count_internal_t e69;
        public mi_stat_count_internal_t e70;
        public mi_stat_count_internal_t e71;
        public mi_stat_count_internal_t e72;
        public mi_stat_count_internal_t e73;
    }
}
