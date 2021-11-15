// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the mi_option_desc_t struct from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

namespace TerraFX.Interop.Mimalloc
{
    internal struct mi_option_desc_t
    {
        // the value
        [NativeTypeName("long")]
        public int value;

        // is it initialized yet? (from the environment)
        public mi_init_t init;

        // for debugging: the option index should match the option
        public mi_option_t option;

        // option name without `mimalloc_` prefix
        [NativeTypeName("const char*")]
        public string name;
    }
}
