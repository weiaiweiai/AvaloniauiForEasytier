using System;
using System.Runtime.InteropServices;

namespace AvaloniauiForEasytier.Services;

/// <summary>
/// EasyTier FFI 的 AOT 兼容原生函数声明。
/// </summary>
internal static partial class EasyTierNative
{
    private const string LibraryName = "easytier_ffi";

    [LibraryImport(LibraryName, EntryPoint = "parse_config", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int ParseConfig(string config);

    [LibraryImport(LibraryName, EntryPoint = "run_network_instance", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int RunNetworkInstance(string config);

    [LibraryImport(LibraryName, EntryPoint = "delete_network_instance")]
    internal static partial int DeleteNetworkInstance(IntPtr instanceNames, nuint length);

    [LibraryImport(LibraryName, EntryPoint = "get_error_msg")]
    internal static partial void GetErrorMessage(out IntPtr message);

    [LibraryImport(LibraryName, EntryPoint = "free_string")]
    internal static partial void FreeString(IntPtr value);
}
