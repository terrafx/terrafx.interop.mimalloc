// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the mi_page_kind_t enum from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

namespace TerraFX.Interop.Mimalloc
{
    internal enum mi_page_kind_t
    {
        // small blocks go into 64kb pages inside a segment
        MI_PAGE_SMALL,

        // medium blocks go into 512kb pages inside a segment
        MI_PAGE_MEDIUM,

        // larger blocks go into a single page spanning a whole segment
        MI_PAGE_LARGE,

        // huge blocks (>512kb) are put into a single page in a segment of the exact size (but still 2mb aligned)
        MI_PAGE_HUGE,
    }
}
