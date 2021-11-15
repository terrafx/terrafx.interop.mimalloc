// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the mi_init_t enum from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

namespace TerraFX.Interop.Mimalloc
{
    internal enum mi_init_t
    {
        // not yet initialized
        UNINIT,

        // not found in the environment, use default value
        DEFAULTED,

        // found in environment or set explicitly
        INITIALIZED,
    }
}
