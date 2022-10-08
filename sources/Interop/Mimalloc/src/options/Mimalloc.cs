// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the options.c file from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

using System;
using System.Diagnostics;
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

            if (option != mi_option_verbose)
            {
                ref readonly mi_option_desc_t desc = ref options[(int)option];
                _mi_verbose_message("option '{0}': {1}\n", desc.name, desc.value);
            }
        }
    }

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
    private static mi_output_fun mi_out_get_default() => mi_out_default;

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
        if (!mi_option_is_enabled(mi_option_show_errors) && !mi_option_is_enabled(mi_option_verbose))
        {
            return;
        }

        if (mi_atomic_increment_acq_rel(ref error_count) > mi_max_error_count)
        {
            return;
        }

        mi_vfprintf(null, null, "mimalloc: error: ", fmt, args);
#pragma warning restore CS0420
    }

    private static partial void _mi_warning_message(string fmt, params object[] args)
    {
#pragma warning disable CS0420
        if (!mi_option_is_enabled(mi_option_show_errors) && !mi_option_is_enabled(mi_option_verbose))
        {
            return;
        }

        if (mi_atomic_increment_acq_rel(ref error_count) > mi_max_error_count)
        {
            return;
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

        if ((MI_XMALLOC != 0) && ((err == ENOMEM) || (err == EOVERFLOW)))
        {
            // abort on memory allocation fails in xmalloc mode
            abort();
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

    private static partial void mi_option_init(ref mi_option_desc_t desc)
    {
        // Read option value from the environment
        string? s = Environment.GetEnvironmentVariable($"mimalloc_{desc.name}");

        if (s is not null)
        {
            switch (s.ToUpper())
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
                    if (int.TryParse(s, out int value))
                    {
                        desc.value = value;
                        desc.init = INITIALIZED;
                    }
                    else
                    {
                        _mi_warning_message("environment option mimalloc_{0} has an invalid value: {1}\n", desc.name, s);
                        desc.init = DEFAULTED;
                    }
                    break;
                }
            }

            mi_assert_internal((MI_DEBUG > 1) && (desc.init != UNINIT));
        }
        else
        {
            desc.init = DEFAULTED;
        }
    }
}
