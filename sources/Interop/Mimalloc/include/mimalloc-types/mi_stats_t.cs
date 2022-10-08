// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the mi_stats_t struct from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

namespace TerraFX.Interop.Mimalloc;

internal struct mi_stats_t
{
    public mi_stat_count_t segments;

    public mi_stat_count_t pages;

    public mi_stat_count_t reserved;

    public mi_stat_count_t committed;

    public mi_stat_count_t reset;

    public mi_stat_count_t page_committed;

    public mi_stat_count_t segments_abandoned;

    public mi_stat_count_t pages_abandoned;

    public mi_stat_count_t threads;

    public mi_stat_count_t huge;

    public mi_stat_count_t giant;

    public mi_stat_count_t malloc;

    public mi_stat_count_t segments_cache;

    public mi_stat_counter_t pages_extended;

    public mi_stat_counter_t mmap_calls;

    public mi_stat_counter_t commit_calls;

    public mi_stat_counter_t page_no_retire;

    public mi_stat_counter_t searches;

    public mi_stat_counter_t huge_count;

    public mi_stat_counter_t giant_count;

    [NativeTypeName("mi_stat_count_t [MI_BIN_HUGE + 1]")]
    public _normal_e__FixedBuffer normal;

    public partial struct _normal_e__FixedBuffer
    {
        public mi_stat_count_t e0;
        public mi_stat_count_t e1;
        public mi_stat_count_t e2;
        public mi_stat_count_t e3;
        public mi_stat_count_t e4;
        public mi_stat_count_t e5;
        public mi_stat_count_t e6;
        public mi_stat_count_t e7;
        public mi_stat_count_t e8;
        public mi_stat_count_t e9;
        public mi_stat_count_t e10;
        public mi_stat_count_t e11;
        public mi_stat_count_t e12;
        public mi_stat_count_t e13;
        public mi_stat_count_t e14;
        public mi_stat_count_t e15;
        public mi_stat_count_t e16;
        public mi_stat_count_t e17;
        public mi_stat_count_t e18;
        public mi_stat_count_t e19;
        public mi_stat_count_t e20;
        public mi_stat_count_t e21;
        public mi_stat_count_t e22;
        public mi_stat_count_t e23;
        public mi_stat_count_t e24;
        public mi_stat_count_t e25;
        public mi_stat_count_t e26;
        public mi_stat_count_t e27;
        public mi_stat_count_t e28;
        public mi_stat_count_t e29;
        public mi_stat_count_t e30;
        public mi_stat_count_t e31;
        public mi_stat_count_t e32;
        public mi_stat_count_t e33;
        public mi_stat_count_t e34;
        public mi_stat_count_t e35;
        public mi_stat_count_t e36;
        public mi_stat_count_t e37;
        public mi_stat_count_t e38;
        public mi_stat_count_t e39;
        public mi_stat_count_t e40;
        public mi_stat_count_t e41;
        public mi_stat_count_t e42;
        public mi_stat_count_t e43;
        public mi_stat_count_t e44;
        public mi_stat_count_t e45;
        public mi_stat_count_t e46;
        public mi_stat_count_t e47;
        public mi_stat_count_t e48;
        public mi_stat_count_t e49;
        public mi_stat_count_t e50;
        public mi_stat_count_t e51;
        public mi_stat_count_t e52;
        public mi_stat_count_t e53;
        public mi_stat_count_t e54;
        public mi_stat_count_t e55;
        public mi_stat_count_t e56;
        public mi_stat_count_t e57;
        public mi_stat_count_t e58;
        public mi_stat_count_t e59;
        public mi_stat_count_t e60;
        public mi_stat_count_t e61;
        public mi_stat_count_t e62;
        public mi_stat_count_t e63;
        public mi_stat_count_t e64;
        public mi_stat_count_t e65;
        public mi_stat_count_t e66;
        public mi_stat_count_t e67;
        public mi_stat_count_t e68;
        public mi_stat_count_t e69;
        public mi_stat_count_t e70;
        public mi_stat_count_t e71;
        public mi_stat_count_t e72;
        public mi_stat_count_t e73;
    }
}
