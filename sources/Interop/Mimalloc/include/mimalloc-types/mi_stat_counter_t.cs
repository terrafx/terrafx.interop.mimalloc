// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the mi_stat_counter_t struct from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

namespace TerraFX.Interop.Mimalloc
{
    internal struct mi_stat_counter_t
    {
        [NativeTypeName("int64_t")]
        public long total;

        [NativeTypeName("int64_t")]
        public long count;
    }
}
