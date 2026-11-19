// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the options.c file from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using static TerraFX.Interop.Mimalloc.mi_init_t;
using static TerraFX.Interop.Mimalloc.mi_option_t;

namespace TerraFX.Interop.Mimalloc;

public static unsafe partial class Mimalloc
{
    // The following members have not been ported as they aren't needed for .NET:
    //  * void mi_add_stderr_output()

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial int mi_version() => MI_MALLOC_VERSION;

    private static partial void mi_option_init([NativeTypeName("mi_option_desc_t*")] ref mi_option_desc_t desc);

    private static partial void _mi_options_init()
    {
        // called on process load; should not be called before the CRT is initialized!
        // (e.g. do not call this from process_init as that may run before CRT initialization)

        for (int i = 0; i < (int)_mi_option_last; i++)
        {
            mi_option_t option = (mi_option_t)i;
            _ = mi_option_get(option);
        }

        if (mi_option_is_enabled(mi_option_guarded_sample_rate) && mi_option_is_enabled(mi_option_allow_large_os_pages))
        {
            mi_option_disable(mi_option_allow_large_os_pages);
            _mi_warning_message("option 'allow_large_os_pages' is disabled to allow for guarded objects\n");
        }

        if (mi_option_is_enabled(mi_option_verbose))
        {
            mi_options_print();
        }
    }

    public static partial void mi_options_print()
    {
        int version = MI_MALLOC_VERSION;
        int major = (version >= 10000) ? (version / 10000) : (version / 100);
        int minor = (version >= 10000) ? ((version % 10000) / 100) : ((version % 100) / 10);
        int patch = (version >= 10000) ? (version % 100) : (version % 10);

        _mi_fprintf(null, null, "v{0}.{1}.{2}\n", major, minor, patch);

        for (int i = 0; i < (int)_mi_option_last; i++)
        {
            mi_option_t option = (mi_option_t)i;
            _ = mi_option_get(option);
            ref readonly mi_option_desc_t desc = ref options[i];
            _mi_fprintf(null, null, "option '{0}': {1}{2}\n", desc.name, desc.value, mi_option_has_size_in_kib(option) ? " KiB" : "");
        }

        _mi_fprintf(null, null, "debug level : {0}\n", MI_DEBUG);
        _mi_fprintf(null, null, "secure level: {0}\n", MI_SECURE);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool mi_option_has_size_in_kib(mi_option_t option)
        => (option == mi_option_reserve_os_memory) || (option == mi_option_arena_reserve);

    public static partial int mi_option_get(mi_option_t option)
    {
        mi_assert((MI_DEBUG != 0) && (option >= 0) && (option < _mi_option_last));
        ref mi_option_desc_t desc = ref options[(int)option];

        // index should match the option
        mi_assert((MI_DEBUG != 0) && (desc.option == option));

        if (mi_unlikely(desc.init == UNINIT))
        {
            mi_option_init(ref desc);
        }

        return desc.value;
    }

    public static partial int mi_option_get_clamp(mi_option_t option, int min, int max)
    {
        int value = mi_option_get(option);
        return (value < min) ? min : ((value > max) ? max : value);
    }

    public static partial nuint mi_option_get_size(mi_option_t option)
    {
        mi_assert_internal((MI_DEBUG > 1) && mi_option_has_size_in_kib(option));
        int value = mi_option_get(option);

        if (value <= 0)
        {
            return 0;
        }

        nuint size = (nuint)value;
        return mi_mul_overflow(size, KiB, out nuint bytes) ? MI_MAX_ALLOC_SIZE : bytes;
    }

    public static partial void mi_option_set(mi_option_t option, int value)
    {
        mi_assert((MI_DEBUG != 0) && (option >= 0) && (option < _mi_option_last));
        ref mi_option_desc_t desc = ref options[(int)option];

        // index should match the option
        mi_assert((MI_DEBUG != 0) && (desc.option == option));

        desc.value = value;
        desc.init = INITIALIZED;
    }

    public static partial void mi_option_set_default(mi_option_t option, int value)
    {
        mi_assert((MI_DEBUG != 0) && (option >= 0) && (option < _mi_option_last));
        ref mi_option_desc_t desc = ref options[(int)option];

        if (desc.init != INITIALIZED)
        {
            desc.value = value;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial bool mi_option_is_enabled(mi_option_t option) => mi_option_get(option) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void mi_option_set_enabled(mi_option_t option, bool enable) => mi_option_set(option, enable ? 1 : 0);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void mi_option_set_enabled_default(mi_option_t option, bool enable) => mi_option_set_default(option, enable ? 1 : 0);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void mi_option_enable(mi_option_t option) => mi_option_set_enabled(option, true);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void mi_option_disable(mi_option_t option) => mi_option_set_enabled(option, false);

    private static void mi_out_stderr([NativeTypeName("const char*")] string msg, void* arg)
    {
        if (Debugger.IsAttached)
        {
            Debug.Write(msg);
        }
        Console.Error.Write(msg);
    }

    // The following members have not been ported as they aren't needed for .NET:
    //  * nuint MI_MAX_DELAY_OUTPUT
    //  * char out_buf[MI_MAX_DELAY_OUTPUT + 1]
    //  * std::atomic<uintptr_t> out_len
    //  * void mi_out_buf(const char*, void*)
    //  * void mi_out_buf_flush(mi_output_fun*, bool, void*)
    //  * void mi_out_buf_stderr(const char* void*)

    // --------------------------------------------------------
    // Default output handler
    // --------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return: NativeTypeName("mi_output_fun*")]
    private static mi_output_fun mi_out_get_default() => mi_out_default ?? mi_out_stderr;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return: NativeTypeName("mi_output_fun*")]
    private static mi_output_fun mi_out_get_default([NativeTypeName("void**")] out void* parg)
    {
#pragma warning disable CS0420
        parg = mi_atomic_load_ptr_acquire(ref mi_out_arg);
        return mi_out_get_default();
#pragma warning restore CS0420
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static partial void mi_register_output(mi_output_fun? @out, void* arg)
    {
#pragma warning disable CS0420
        // stop using the delayed output buffer
        mi_out_default = @out ?? mi_out_stderr;
        mi_atomic_store_ptr_release(ref mi_out_arg, arg);
#pragma warning restore CS0420
    }

    // The following members have not been ported as they aren't needed for .NET:
    //  * void mi_add_stderr_output()

    // --------------------------------------------------------
    // Messages, all end up calling `_mi_fputs`.
    // --------------------------------------------------------

    // The following members have not been ported as they aren't needed for .NET:
    //  * bool t_recurse
    //  * bool mi_recurse_enter()
    //  * void mi_recurse_exit()

    private static partial void _mi_fputs(mi_output_fun? @out, void* arg, string prefix, string message)
    {
        @out ??= mi_out_get_default(out arg);

        if (!string.IsNullOrEmpty(prefix))
        {
            @out(prefix, arg);
        }

        @out(message, arg);
    }

    private static void mi_vfprintf([NativeTypeName("mi_output_fun*")] mi_output_fun? @out, void* arg, [NativeTypeName("const char*")] string prefix, [NativeTypeName("const char*")] string fmt, params object[] args)
    {
        if (string.IsNullOrEmpty(fmt))
        {
            return;
        }

        var buf = string.Format(fmt, args);
        _mi_fputs(@out, arg, prefix, buf);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static partial void _mi_fprintf(mi_output_fun? @out, void* arg, string fmt, params object[] args) => mi_vfprintf(@out, arg, string.Empty, fmt, args);

    private static partial void _mi_trace_message(string fmt, params object[] args)
    {
        if (mi_option_get(mi_option_verbose) <= 1)
        {
            // only with verbose level 2 or higher
            return;
        }

        mi_vfprintf(null, null, "mimalloc: ", fmt, args);
    }

    private static partial void _mi_verbose_message(string fmt, params object[] args)
    {
        if (!mi_option_is_enabled(mi_option_verbose))
        {
            return;
        }

        mi_vfprintf(null, null, "mimalloc: ", fmt, args);
    }

    private static void mi_show_error_message([NativeTypeName("const char*")] string fmt, params object[] args)
    {
#pragma warning disable CS0420
        if (!mi_option_is_enabled(mi_option_verbose))
        {
            if (!mi_option_is_enabled(mi_option_show_errors))
            {
                return;
            }

            if ((mi_max_error_count >= 0) && ((long)mi_atomic_increment_acq_rel(ref error_count) > mi_max_error_count))
            {
                return;
            }
        }

        mi_vfprintf(null, null, "mimalloc: error: ", fmt, args);
#pragma warning restore CS0420
    }

    private static partial void _mi_warning_message(string fmt, params object[] args)
    {
#pragma warning disable CS0420
        if (!mi_option_is_enabled(mi_option_verbose))
        {
            if (!mi_option_is_enabled(mi_option_show_errors))
            {
                return;
            }

            if ((mi_max_warning_count >= 0) && ((long)mi_atomic_increment_acq_rel(ref warning_count) > mi_max_warning_count))
            {
                return;
            }
        }

        mi_vfprintf(null, null, "mimalloc: warning: ", fmt, args);
#pragma warning restore CS0420
    }

    private static partial void _mi_assert_fail(string assertion, string fname, uint line, string func)
    {
        _mi_fprintf(null, null, "mimalloc: assertion failed: at \"{0}\":{1}, {2}\n  assertion: \"{3}\"\n", fname, line, (func is null) ? "" : func, assertion);
        abort();
    }

    // --------------------------------------------------------
    // Errors
    // --------------------------------------------------------

    private static void mi_error_default(int err, void* arg)
    {
        if ((MI_DEBUG > 0) && (err == EFAULT))
        {
            if (Debugger.IsAttached)
            {
                Debugger.Break();
            }

            abort();
        }

        if ((MI_SECURE > 0) && (err == EFAULT))
        {
            // abort on serious errors in secure mode (corrupted meta-data)
            abort();
        }

        if ((MI_XMALLOC != 0) && ((err == ENOMEM) || (err == EOVERFLOW) || (err == EINVAL)))
        {
            // abort on memory allocation fails in xmalloc mode
            abort();
        }

        if (last_errno == 0)
        {
            last_errno = (err == EINVAL) ? EINVAL : ENOMEM;
        }
    }

    public static partial void mi_register_error(mi_error_fun? fun, void* arg)
    {
#pragma warning disable CS0420
        mi_error_handler = fun ?? mi_error_default;
        mi_atomic_store_ptr_release(ref mi_error_arg, arg);
#pragma warning restore CS0420
    }

    private static partial void _mi_error_message(int err, string fmt, params object[] args)
    {
#pragma warning disable CS0420
        // show detailed error message
        mi_show_error_message(fmt, args);

        // and call the error handler which may abort (or return normally)
        mi_error_handler(err, mi_atomic_load_ptr_acquire(ref mi_error_arg));
#pragma warning restore CS0420
    }

    // --------------------------------------------------------
    // Initialize options by checking the environment
    // --------------------------------------------------------

    // The following members have not been ported as they aren't needed for .NET:
    //  * void mi_strlcpy(char*, const char*, size_t)
    //  * void mi_strlcat(char*, const char*, size_t)
    //  * int mi_strnicmp(const char*, const char*, size_t)
    //  * bool mi_getenv(const char*, char*, size_t)

    private static bool mi_option_parse_size_in_kib(string value, out int size_kib)
    {
        size_kib = 0;
        string number = value;

        if (number.EndsWith("IB", StringComparison.OrdinalIgnoreCase))
        {
            number = number[..^2];
        }
        else if (number.EndsWith("B", StringComparison.OrdinalIgnoreCase))
        {
            number = number[..^1];
        }

        char unit = '\0';

        if (number.Length > 0)
        {
            char suffix = char.ToUpperInvariant(number[^1]);

            if ((suffix == 'K') || (suffix == 'M') || (suffix == 'G') || (suffix == 'T'))
            {
                unit = suffix;
                number = number[..^1];
            }
        }

        if (!long.TryParse(number, NumberStyles.AllowLeadingWhite | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long parsed))
        {
            return false;
        }

        if (parsed < 0)
        {
            size_kib = 0;
            return true;
        }

        long multiplier = unit switch
        {
            'M' => 1024,
            'G' => 1024 * 1024,
            'T' => 1024L * 1024 * 1024,
            _ => 1,
        };

        if (unit == '\0')
        {
            parsed = (parsed / 1024) + ((parsed % 1024) != 0 ? 1 : 0);
        }
        else if (parsed > (long.MaxValue / multiplier))
        {
            parsed = long.MaxValue;
        }
        else
        {
            parsed *= multiplier;
        }

        nuint maxKib = MI_MAX_ALLOC_SIZE / KiB;
        long maxStoredKib = (maxKib > int.MaxValue) ? int.MaxValue : (long)maxKib;
        if (parsed > maxStoredKib)
        {
            parsed = maxStoredKib;
        }

        size_kib = (int)parsed;
        return true;
    }

    private static partial void mi_option_init(ref mi_option_desc_t desc)
    {
        // Read option value from the environment
        string? s = Environment.GetEnvironmentVariable($"mimalloc_{desc.name}");

        if (s is null)
        {
            string? legacy_name = desc.option switch
            {
                mi_option_eager_commit => "segment_eager_commit",
                mi_option_arena_eager_commit => "eager_region_commit",
                mi_option_purge_decommits => "reset_decommits",
                mi_option_purge_delay => "reset_delay",
                mi_option_allow_large_os_pages => "large_os_pages",
                mi_option_deprecated_page_reset => "page_reset",
                mi_option_abandoned_page_purge => "abandoned_page_reset",
                mi_option_deprecated_segment_reset => "segment_reset",
                mi_option_disallow_os_alloc => "limit_os_alloc",
                mi_option_arena_purge_mult => "arena_purge_delay",
                mi_option_purge_extend_delay => "decommit_extend_delay",
                _ => null,
            };

            if (legacy_name is not null)
            {
                s = Environment.GetEnvironmentVariable($"mimalloc_{legacy_name}");

                if (s is not null)
                {
                    _mi_warning_message("environment option \"mimalloc_{0}\" is deprecated -- use \"mimalloc_{1}\" instead.\n", legacy_name, desc.name);
                }
            }
        }

        if (s is not null)
        {
            if (s.Length > 64)
            {
                _mi_warning_message("environment option mimalloc_{0} has an invalid value.\n", desc.name);
                desc.init = DEFAULTED;
            }
            else
            {
                switch (s.ToUpperInvariant())
                {
                    case "":
                    case "1":
                    case "TRUE":
                    case "YES":
                    case "ON":
                    {
                        desc.value = 1;
                        desc.init = INITIALIZED;
                        break;
                    }

                    case "0":
                    case "FALSE":
                    case "NO":
                    case "OFF":
                    {
                        desc.value = 0;
                        desc.init = INITIALIZED;
                        break;
                    }

                    default:
                    {
                        bool parsed;
                        int value;

                        if ((desc.option == mi_option_reserve_os_memory) || (desc.option == mi_option_arena_reserve))
                        {
                            parsed = mi_option_parse_size_in_kib(s, out value);
                        }
                        else
                        {
                            parsed = int.TryParse(s, NumberStyles.AllowLeadingWhite | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
                        }

                        if (parsed)
                        {
                            desc.value = value;
                            desc.init = INITIALIZED;
                        }
                        else
                        {
                            _mi_warning_message("environment option mimalloc_{0} has an invalid value.\n", desc.name);
                            desc.init = DEFAULTED;
                        }
                        break;
                    }
                }
            }

            // mi_assert_internal((MI_DEBUG > 1) && (desc.init != UNINIT));
        }
        else
        {
            desc.init = DEFAULTED;
        }
    }
}
