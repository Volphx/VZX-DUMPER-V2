using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Il2CppDumper_ImGuiStyle;

public sealed class OffsetRow
{
    public string Name { get; set; } = "";
    public string Key { get; set; } = "";
    public string Field { get; set; } = "";
    public string Value { get; set; } = "";
    public string State { get; set; } = "";
    public bool HasValue => !string.IsNullOrEmpty(Value);
    public string DisplayValue => HasValue ? Value : "—";
}

public sealed class OffsetAnalysis
{
    public string Architecture { get; set; } = "";
    public string Rva { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Text { get; set; } = "";
    public string? Error { get; set; }
    public uint Found { get; set; }
    public uint Support { get; set; }
    public bool Ambiguous { get; set; }
    public List<OffsetRow> Offsets { get; set; } = [];
}

public static partial class OffsetRvaNative
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr AnalyzeDelegate([MarshalAs(UnmanagedType.LPWStr)] string dump, [MarshalAs(UnmanagedType.LPWStr)] string library);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void FreeDelegate(IntPtr pointer);
    private static readonly object Sync = new();
    private static AnalyzeDelegate? analyze;
    private static FreeDelegate? free;
    private static IntPtr module;

    private static void Load()
    {
        if (analyze != null) return;
        if (!Environment.Is64BitProcess) throw new PlatformNotSupportedException("Execute a versão Windows x64 do Dumper. O jogo pode ser ARMv7 ou ARM64.");
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("VZX.OffsetRvaEngine.dll")
            ?? throw new InvalidOperationException("Motor de offsets ausente. Recompile o projeto com o módulo Native.");
        using var buffer = new MemoryStream(); resource.CopyTo(buffer);
        var bytes = buffer.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VZX-Dumper", "Native", hash);
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "OffsetRvaEngine.dll");
        if (!File.Exists(file) || !SHA256.HashData(File.ReadAllBytes(file)).SequenceEqual(SHA256.HashData(bytes)))
        {
            var temporary = file + "." + Guid.NewGuid().ToString("N");
            File.WriteAllBytes(temporary, bytes); File.Move(temporary, file, true);
        }
        module = NativeLibrary.Load(file);
        free = Marshal.GetDelegateForFunctionPointer<FreeDelegate>(NativeLibrary.GetExport(module, "vzx_free"));
        analyze = Marshal.GetDelegateForFunctionPointer<AnalyzeDelegate>(NativeLibrary.GetExport(module, "vzx_analyze"));
    }

    public static OffsetAnalysis Analyze(string dump, string library)
    {
        if (!File.Exists(dump)) throw new FileNotFoundException("Selecione um dump.cs válido.", dump);
        if (!File.Exists(library)) throw new FileNotFoundException("Selecione a libil2cpp.so da mesma versão.", library);
        lock (Sync)
        {
            Load(); var pointer = analyze!(Path.GetFullPath(dump), Path.GetFullPath(library));
            if (pointer == IntPtr.Zero) throw new OutOfMemoryException("Não foi possível alocar o resultado da análise.");
            try
            {
                var json = Marshal.PtrToStringUTF8(pointer) ?? throw new InvalidDataException("Resultado vazio.");
                var result = JsonSerializer.Deserialize<OffsetAnalysis>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new InvalidDataException("Resultado inválido.");
                if (!string.IsNullOrEmpty(result.Error)) throw new InvalidDataException(result.Error);
                return result;
            }
            finally { free!(pointer); }
        }
    }
}
