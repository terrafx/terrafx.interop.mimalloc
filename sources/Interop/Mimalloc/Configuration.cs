// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;

namespace TerraFX.Interop.Mimalloc;

internal static class Configuration
{
    private static readonly bool s_disableResolveLibraryHook = GetAppContextData("TerraFX.Interop.Mimalloc.DisableResolveLibraryHook", defaultValue: false);

    public static bool DisableResolveLibraryHook => s_disableResolveLibraryHook;

    private static bool GetAppContextData(string name, bool defaultValue)
    {
        object? data = AppContext.GetData(name);

        if (data is bool value)
        {
            return value;
        }
        else if ((data is string s) && bool.TryParse(s, out bool result))
        {
            return result;
        }
        else
        {
            return defaultValue;
        }
    }
}
