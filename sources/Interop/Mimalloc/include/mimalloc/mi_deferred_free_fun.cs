// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the mi_deferred_free_fun fnptr from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

namespace TerraFX.Interop.Mimalloc;

public unsafe delegate void mi_deferred_free_fun(bool force, [NativeTypeName("unsigned long long")] ulong heartbeat, void* arg);
