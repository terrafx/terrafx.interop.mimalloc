// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the mi_option_t enum from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

namespace TerraFX.Interop.Mimalloc
{
    public enum mi_option_t
    {
        // stable options

        mi_option_show_errors,

        mi_option_show_stats,

        mi_option_verbose,

        // the following options are experimental

        mi_option_eager_commit,

        mi_option_eager_region_commit,

        mi_option_reset_decommits,

        // implies eager commit
        mi_option_large_os_pages,

        mi_option_reserve_huge_os_pages,

        mi_option_segment_cache,

        mi_option_page_reset,

        mi_option_abandoned_page_reset,

        mi_option_segment_reset,

        mi_option_eager_commit_delay,

        mi_option_reset_delay,

        mi_option_use_numa_nodes,

        mi_option_os_tag,

        mi_option_max_errors,

        _mi_option_last
    }
}
