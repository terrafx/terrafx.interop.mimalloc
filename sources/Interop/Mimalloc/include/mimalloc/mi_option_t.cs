// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the mi_option_t enum from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

namespace TerraFX.Interop.Mimalloc;

public enum mi_option_t
{
    // stable options

    mi_option_show_errors,

    mi_option_show_stats,

    mi_option_verbose,

    // the following options are experimental

    mi_option_eager_commit,

    mi_option_arena_eager_commit,

    mi_option_purge_decommits,

    // implies eager commit
    mi_option_allow_large_os_pages,

    mi_option_reserve_huge_os_pages,

    mi_option_reserve_huge_os_pages_at,

    mi_option_reserve_os_memory,

    mi_option_deprecated_segment_cache,

    mi_option_deprecated_page_reset,

    mi_option_abandoned_page_purge,

    mi_option_deprecated_segment_reset,

    mi_option_eager_commit_delay,

    mi_option_purge_delay,

    mi_option_use_numa_nodes,

    mi_option_disallow_os_alloc,

    mi_option_os_tag,

    mi_option_max_errors,

    mi_option_max_warnings,

    mi_option_max_segment_reclaim,

    mi_option_destroy_on_exit,

    mi_option_arena_reserve,

    mi_option_arena_purge_mult,

    mi_option_purge_extend_delay,

    mi_option_abandoned_reclaim_on_free,

    mi_option_disallow_arena_alloc,

    mi_option_retry_on_oom,

    // The following native-only subprocess and guarded-allocation features are not implemented by this managed port.
    mi_option_visit_abandoned,

    mi_option_guarded_min,

    mi_option_guarded_max,

    mi_option_guarded_precise,

    mi_option_guarded_sample_rate,

    mi_option_guarded_sample_seed,

    mi_option_target_segments_per_thread,

    mi_option_generic_collect,

    mi_option_allow_thp,

    _mi_option_last,

    mi_option_segment_eager_commit = mi_option_eager_commit,

    mi_option_eager_region_commit = mi_option_arena_eager_commit,

    mi_option_large_os_pages = mi_option_allow_large_os_pages,

    mi_option_page_reset = mi_option_deprecated_page_reset,

    mi_option_abandoned_page_reset = mi_option_abandoned_page_purge,

    mi_option_segment_reset = mi_option_deprecated_segment_reset,

    mi_option_arena_purge_delay = mi_option_arena_purge_mult,

    mi_option_reset_decommits = mi_option_purge_decommits,

    mi_option_reset_delay = mi_option_purge_delay,

    mi_option_limit_os_alloc = mi_option_disallow_os_alloc
}
