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

    [LibraryImport(LibraryName, EntryPoint = "collect_network_infos")]
    internal static partial int CollectNetworkInfos(IntPtr infosBuffer, nuint maxLength);

    [LibraryImport(LibraryName, EntryPoint = "get_error_msg")]
    internal static partial void GetErrorMessage(out IntPtr message);

    [LibraryImport(LibraryName, EntryPoint = "free_string")]
    internal static partial void FreeString(IntPtr value);
}

/// <summary>
/// 与 FFI KeyValuePair 结构对应的托管结构；两个字段均为 FFI 分配的 UTF-8 字符串指针。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeKeyValuePair
{
    /// <summary>键字符串指针；实例名称。</summary>
    public IntPtr Key;

    /// <summary>值字符串指针；网络运行信息 JSON。</summary>
    public IntPtr Value;
}
