// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on mimalloc's internal statistic count type.
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

namespace TerraFX.Interop.Mimalloc;

internal struct mi_stat_count_internal_t
{
    [NativeTypeName("int64_t")]
    public long allocated;

    [NativeTypeName("int64_t")]
    public long freed;

    [NativeTypeName("int64_t")]
    public long peak;

    [NativeTypeName("int64_t")]
    public long current;
}
