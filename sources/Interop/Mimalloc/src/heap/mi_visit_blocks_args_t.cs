// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the mi_visit_blocks_args_t struct from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

namespace TerraFX.Interop.Mimalloc;

// Just to pass arguments
internal unsafe struct mi_visit_blocks_args_t
{
    public bool visit_blocks;

    [NativeTypeName("mi_block_visit_fun*")]
    public void* visitor;

    public void* arg;
}
