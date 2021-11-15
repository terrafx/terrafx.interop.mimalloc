// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the mi_padding_t struct from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

namespace TerraFX.Interop.Mimalloc
{
    // In debug mode there is a padding stucture at the end of the blocks to check for buffer overflows
    internal struct mi_padding_t
    {
        // encoded block value to check validity of the padding (in case of overflow)
        [NativeTypeName("uint32_t")]
        public uint canary;

        // padding bytes before the block. (mi_usable_size(p) - delta == exact allocated bytes)
        [NativeTypeName("uint32_t")]
        public uint delta;
    }
}
