using System.Runtime.InteropServices;
using System.IO;
using System.Text.Json;

namespace Il2CppDumper_ImGuiStyle;
public sealed class EspBox
{
    public double X { get; set; }
    public double Top { get; set; }
    public double Bottom { get; set; }
    public int Hp { get; set; }
    public int MaxHp { get; set; }
    public double Distance { get; set; }
    public string Name { get; set; } = "";
    public List<double[]> Bones { get; set; } = [];
}
public sealed class EspFrame
{
    public bool Valid { get; set; }
    public int Pid { get; set; }
    public int Entities { get; set; }
    public int Visible { get; set; }
    public int Accepted { get; set; }
    public int Named { get; set; }
    public string Status { get; set; } = "";
    public string? Error { get; set; }
    public List<EspBox> Boxes { get; set; } = [];
}
public static partial class OffsetRvaNative
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr EspStartDelegate([MarshalAs(UnmanagedType.LPWStr)] string library, ulong rva, [MarshalAs(UnmanagedType.LPUTF8Str)] string offsets, uint pid);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr EspFrameDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void EspStopDelegate();
    public static EspFrame StartEsp(OffsetAnalysis profile, string library, int pid)
    {
        if (string.IsNullOrEmpty(profile.Rva) || profile.Ambiguous) throw new InvalidOperationException("A análise precisa ter um único RVA candidato antes do teste.");
        if (pid <= 0) throw new InvalidOperationException("Selecione uma instância do BlueStacks.");
        var offsets = string.Join('\n', profile.Offsets.Where(row => row.HasValue).Select(row => row.Key + "=" + row.Value));
        var rva = Convert.ToUInt64(profile.Rva.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? profile.Rva[2..] : profile.Rva, 16);
        lock (Sync)
        {
            Load();
            var start = Marshal.GetDelegateForFunctionPointer<EspStartDelegate>(NativeLibrary.GetExport(module, "vzx_esp_start"));
            return EspResponse(start(library, rva, offsets, (uint)pid));
        }
    }
    public static EspFrame ReadEspFrame()
    {
        lock (Sync)
        {
            Load(); var read = Marshal.GetDelegateForFunctionPointer<EspFrameDelegate>(NativeLibrary.GetExport(module, "vzx_esp_frame"));
            return EspResponse(read());
        }
    }
    public static void StopEsp()
    {
        lock (Sync)
        {
            if (module == IntPtr.Zero) return;
            Marshal.GetDelegateForFunctionPointer<EspStopDelegate>(NativeLibrary.GetExport(module, "vzx_esp_stop"))();
        }
    }
    private static EspFrame EspResponse(IntPtr pointer)
    {
        if (pointer == IntPtr.Zero) throw new OutOfMemoryException();
        try
        {
            var value = JsonSerializer.Deserialize<EspFrame>(Marshal.PtrToStringUTF8(pointer) ?? "{}", new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("Resposta de diagnóstico inválida.");
            if (!string.IsNullOrEmpty(value.Error)) throw new InvalidOperationException(value.Error);
            return value;
        }
        finally { free!(pointer); }
    }
}
