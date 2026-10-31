using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;

namespace Il2CppDumper_ImGuiStyle;

public partial class OffsetSearchView : UserControl
{
    private OffsetAnalysis? result;
    private ICollectionView? rows;
    private readonly SemaphoreSlim analysisLock = new(1, 1);
    private EspDiagnostic? esp;
    private bool connectingEsp;
    private string analyzedDump = "", analyzedLibrary = "";
    public bool AnalyzeAfterDump => AutoAnalyze.IsChecked == true;

    public OffsetSearchView() { InitializeComponent(); Loaded += (_, _) => LoadDefaults(); }
    private void LoadDefaults()
    {
        if (!string.IsNullOrWhiteSpace(DumpPath.Text)) return;
        var directory = Path.Combine(AppContext.BaseDirectory, "Dumps");
        var dump = Path.Combine(directory, "dump.cs");
        if (File.Exists(dump)) DumpPath.Text = dump;
        if (Directory.Exists(directory))
        {
            var libraries = Directory.GetFiles(directory, "*libil2cpp.so");
            if (libraries.Length == 1) LibraryPath.Text = libraries[0];
        }
    }
    private void BrowseDump_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Selecionar dump.cs", Filter = "Dump C# (*.cs)|*.cs|Todos os arquivos|*.*" };
        if (dialog.ShowDialog() == true) DumpPath.Text = dialog.FileName;
    }
    private void BrowseLibrary_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Selecionar libil2cpp.so", Filter = "Biblioteca ELF (*.so)|*.so|Todos os arquivos|*.*" };
        if (dialog.ShowDialog() == true) LibraryPath.Text = dialog.FileName;
    }
    private async void UseDump_Click(object sender, RoutedEventArgs e)
    {
        await StopEspAsync();
        if (Window.GetWindow(this) is MainWindow window) window.UseCurrentDumpForOffsets();
    }
    public void SetFiles(string dump, string library) { DumpPath.Text = dump; LibraryPath.Text = library; }
    private async void Analyze_Click(object sender, RoutedEventArgs e) => await AnalyzeAsync(DumpPath.Text.Trim(), LibraryPath.Text.Trim());

    public async Task<bool> AnalyzeAsync(string dump, string library)
    {
        await analysisLock.WaitAsync();
        try
        {
            await StopEspAsync();
            SetFiles(dump, library); AnalyzeButton.IsEnabled = false; FileInputs.IsEnabled = false;
            EspTestButton.IsEnabled = false;
            CopyButton.IsEnabled = ExportButton.IsEnabled = CopyFieldButton.IsEnabled = false;
            AnalysisProgress.Visibility = Visibility.Visible; EmptyMessage.Visibility = Visibility.Visible;
            EmptyMessage.Text = "Analisando o dump e as referências ARM da biblioteca...";
            AnalysisStatus.Text = "Análise em andamento. O restante da interface continua disponível.";
            result = null; rows = null; ResultsGrid.ItemsSource = null;
            ArchitectureValue.Text = FoundValue.Text = PendingValue.Text = RvaValue.Text = "—";
            var found = await Task.Run(() => OffsetRvaNative.Analyze(dump, library));
            analyzedDump = Path.GetFullPath(dump); analyzedLibrary = Path.GetFullPath(library);
            result = found; rows = CollectionViewSource.GetDefaultView(found.Offsets); rows.Filter = PassFilter;
            ResultsGrid.ItemsSource = rows;
            ArchitectureValue.Text = found.Architecture;
            FoundValue.Text = $"{found.Found} / {found.Offsets.Count}";
            PendingValue.Text = found.Offsets.Count(row => !row.HasValue).ToString();
            RvaValue.Text = string.IsNullOrEmpty(found.Rva) ? "Não identificado" : found.Rva;
            AnalysisStatus.Text = found.Summary + (found.Support > 0 ? $" Suporte: {found.Support} métodos." : "");
            ResultsSummary.Text = $"{found.Offsets.Count} campos · {found.Offsets.Count(row => row.Name.StartsWith("bone_", StringComparison.Ordinal))} bones · análise estática";
            SelectedDetail.Text = "RVA candidato não confirmado em memória. Campos ausentes permanecem pendentes.";
            CopyButton.IsEnabled = ExportButton.IsEnabled = true; RefreshFilter(); return true;
        }
        catch (Exception ex)
        {
            AnalysisStatus.Text = "Não foi possível analisar: " + ex.Message;
            EmptyMessage.Text = "Confira os dois arquivos selecionados e tente novamente.";
            EmptyMessage.Visibility = Visibility.Visible; ResultsSummary.Text = "Nenhum resultado desta análise."; return false;
        }
        finally
        {
            AnalyzeButton.IsEnabled = true; FileInputs.IsEnabled = true; AnalysisProgress.Visibility = Visibility.Collapsed;
            EspTestButton.IsEnabled = result != null && !result.Ambiguous && !string.IsNullOrEmpty(result.Rva);
            analysisLock.Release();
        }
    }
    private bool PassFilter(object item)
    {
        if (item is not OffsetRow row) return false;
        if (BonesOnly.IsChecked == true && !row.Name.StartsWith("bone_", StringComparison.Ordinal)) return false;
        if (PendingOnly.IsChecked == true && row.HasValue) return false;
        var query = SearchText.Text.Trim();
        return query.Length == 0 || (row.Name + " " + row.Field + " " + row.Value).Contains(query, StringComparison.OrdinalIgnoreCase);
    }
    private void RefreshFilter()
    {
        rows?.Refresh();
        if (rows != null) { EmptyMessage.Text = "Nenhum campo corresponde ao filtro."; EmptyMessage.Visibility = rows.IsEmpty ? Visibility.Visible : Visibility.Collapsed; }
    }
    private void Filter_Changed(object sender, RoutedEventArgs e) { if (IsInitialized) RefreshFilter(); }
    private void Search_Changed(object sender, TextChangedEventArgs e) { if (IsInitialized) RefreshFilter(); }
    private void Result_Selected(object sender, SelectionChangedEventArgs e)
    {
        CopyFieldButton.IsEnabled = ResultsGrid.SelectedItem is OffsetRow;
        if (ResultsGrid.SelectedItem is OffsetRow row) SelectedDetail.Text = $"{row.Key} · {row.Field} · {row.State}";
    }
    private void CopyAll_Click(object sender, RoutedEventArgs e)
    {
        try { if (result != null) { Clipboard.SetText(result.Text); AnalysisStatus.Text = "Resultado completo copiado."; } }
        catch (Exception ex) { AnalysisStatus.Text = "Não foi possível copiar: " + ex.Message; }
    }
    private void CopyField_Click(object sender, RoutedEventArgs e)
    {
        try { if (ResultsGrid.SelectedItem is OffsetRow row) Clipboard.SetText($"{row.Name} = {(row.HasValue ? row.Value : row.State)}"); }
        catch (Exception ex) { AnalysisStatus.Text = "Não foi possível copiar: " + ex.Message; }
    }
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (result == null) return;
        var dialog = new SaveFileDialog { FileName = "offsets_e_rva.hpp", Filter = "Header C++ (*.hpp)|*.hpp|Texto (*.txt)|*.txt", AddExtension = true };
        if (dialog.ShowDialog() != true) return;
        try { File.WriteAllText(dialog.FileName, result.Text, new System.Text.UTF8Encoding(false)); AnalysisStatus.Text = "Resultado exportado: " + dialog.FileName; }
        catch (Exception ex) { AnalysisStatus.Text = "Não foi possível exportar: " + ex.Message; }
    }
    private async void EspTest_Click(object sender, RoutedEventArgs e)
    {
        if (connectingEsp) return;
        if (esp != null) { await StopEspAsync(); AnalysisStatus.Text = "Teste ESP parado."; return; }
        if (result == null) return;
        connectingEsp = true; EspTestButton.IsEnabled = false; AnalyzeButton.IsEnabled = false; FileInputs.IsEnabled = false;
        var current = new EspDiagnostic(Window.GetWindow(this), text => AnalysisStatus.Text = text);
        esp = current;
        AnalysisStatus.Text = "Conectando ao BlueStacks e validando o perfil encontrado...";
        try
        {
            if (!string.Equals(Path.GetFullPath(DumpPath.Text.Trim()), analyzedDump, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetFullPath(LibraryPath.Text.Trim()), analyzedLibrary, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Os arquivos foram alterados. Execute a análise novamente antes do teste.");
            await current.StartAsync(result, analyzedLibrary);
            if (ReferenceEquals(esp, current)) EspTestButton.Content = "Parar ESP";
        }
        catch (Exception ex)
        {
            await current.StopAsync(); esp = null; AnalysisStatus.Text = "ESP não iniciado: " + ex.Message;
        }
        finally
        {
            connectingEsp = false; EspTestButton.IsEnabled = true; AnalyzeButton.IsEnabled = true; FileInputs.IsEnabled = esp == null;
        }
    }
    private async Task StopEspAsync()
    {
        var current = esp; esp = null;
        if (current != null) await current.StopAsync();
        EspTestButton.Content = "Testar ESP";
        FileInputs.IsEnabled = true;
    }
}
