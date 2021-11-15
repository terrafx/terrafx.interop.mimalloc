// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the mi_stat_count_t struct from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

namespace TerraFX.Interop.Mimalloc
{
    internal struct mi_stat_count_t
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
}
