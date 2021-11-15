// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the mi_random_ctx_t struct from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

namespace TerraFX.Interop.Mimalloc
{
    // Random context
    internal unsafe struct mi_random_ctx_t
    {
        [NativeTypeName("uint32_t [16]")]
        public fixed uint input[16];

        [NativeTypeName("uint32_t [16]")]
        public fixed uint output[16];

        public int output_available;
    }
}
