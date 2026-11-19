// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on mimalloc's internal statistic counter type.
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

namespace TerraFX.Interop.Mimalloc;

internal struct mi_stat_counter_internal_t
{
    [NativeTypeName("int64_t")]
    public long total;

    [NativeTypeName("int64_t")]
    public long count;
}
