using System.IO;

namespace Il2CppDumper_ImGuiStyle;

public partial class MainWindow
{
    public void UseCurrentDumpForOffsets()
    {
        var directory = string.IsNullOrWhiteSpace(TxtOutputDir.Text)
            ? Path.Combine(AppContext.BaseDirectory, "Dumps") : TxtOutputDir.Text.Trim();
        var library = TxtSoPath.Text.Trim();
        if (library.Length == 0 && Directory.Exists(directory))
        {
            var candidates = Directory.GetFiles(directory, "*libil2cpp.so");
            if (candidates.Length == 1) library = candidates[0];
        }
        OffsetsView.SetFiles(Path.Combine(directory, "dump.cs"), library);
        MainTabs.SelectedItem = OffsetsTab;
    }
}
