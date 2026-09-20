using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Il2CppDumper;

namespace Il2CppDumper_ImGuiStyle
{
    public partial class MainWindow : Window
    {
        private Config config = new Config();

        public MainWindow()
        {
            InitializeComponent();
            TxtOutputDir.Text = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Dumps");
            Log("[INFO] VZX DUMPER V2 pronto.");
            Log("[INFO] Suporte completo 64-bit & 32-bit.");
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Log(string msg)
        {
            Dispatcher.Invoke(() =>
            {
                var formatted = $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
                TxtMiniLog.AppendText(formatted);
                TxtFullLog.AppendText(formatted);
                TxtMiniLog.ScrollToEnd();
                TxtFullLog.ScrollToEnd();
            });
        }

        private void BtnBrowseSo_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Selecione o binario ELF (libil2cpp.so / dump de memoria)",
                Filter = "Binario Il2Cpp (*.so;*.dll;*.exe)|*.so;*.dll;*.exe|Todos os arquivos|*.*"
            };
            if (ofd.ShowDialog() == true)
            {
                TxtSoPath.Text = ofd.FileName;
                Log($"[OK] Binario selecionado: {Path.GetFileName(ofd.FileName)}");

                // Auto detect base address from file name
                var match = Regex.Match(Path.GetFileName(ofd.FileName), @"(?i)^([0-9a-f]{6,16})");
                if (match.Success)
                {
                    TxtBaseAddr.Text = "0x" + match.Groups[1].Value;
                    Log($"[AUTO] Endereco base detectado no nome: 0x{match.Groups[1].Value}");
                }

                // Tenta achar metadata no mesmo diretorio
                var dir = Path.GetDirectoryName(ofd.FileName);
                var metaCandidates = new[]
                {
                    Path.Combine(dir, "global-metadata.dat"),
                    Path.Combine(dir, "..", "global-metadata.dat"),
                    Path.Combine(dir, "..", "assets", "bin", "Data", "Managed", "Metadata", "global-metadata.dat")
                };
                foreach (var c in metaCandidates)
                {
                    if (File.Exists(c))
                    {
                        TxtMetaPath.Text = Path.GetFullPath(c);
                        Log($"[AUTO] Metadados localizados automaticamente: {Path.GetFileName(c)}");
                        break;
                    }
                }
            }
        }

        private void BtnBrowseMeta_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Selecione o arquivo de metadados (global-metadata.dat)",
                Filter = "Metadados Il2Cpp (global-metadata.dat)|global-metadata.dat|Todos os arquivos|*.*"
            };
            if (ofd.ShowDialog() == true)
            {
                TxtMetaPath.Text = ofd.FileName;
                Log($"[OK] Metadados selecionados: {Path.GetFileName(ofd.FileName)}");
            }
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dir = string.IsNullOrWhiteSpace(TxtOutputDir.Text) ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Dumps") : TxtOutputDir.Text;
                Directory.CreateDirectory(dir);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log($"[ERRO] Não foi possível abrir a pasta: {ex.Message}");
            }
        }

        private void BtnAdbExtract_Click(object sender, RoutedEventArgs e)
        {
            var customPort = TxtAdbPort.Text.Trim();
            var customPackage = TxtAdbPackage.Text.Trim();

            BtnAdbExtract.IsEnabled = false;
            BtnStartDump.IsEnabled = false;
            PrgBar.IsIndeterminate = true;

            Task.Run(() =>
            {
                try
                {
                    Log($"[INFO] Iniciando rotina de extracao via ADB / Root... (Pacote: '{customPackage}', Porta: '{(string.IsNullOrEmpty(customPort) ? "Auto" : customPort)}')");
                    if (AdbMemoryDumper.TryDumpFromAdb(out var so, out var meta, out var baseAddr, customPort, customPackage, Log))
                    {
                        Dispatcher.Invoke(() =>
                        {
                            TxtSoPath.Text = so;
                            TxtMetaPath.Text = meta;
                            if (baseAddr != 0)
                            {
                                TxtBaseAddr.Text = "0x" + baseAddr.ToString("X");
                            }
                            else
                            {
                                var match = Regex.Match(Path.GetFileName(so), @"(?i)^([0-9a-f]{6,16})");
                                if (match.Success) TxtBaseAddr.Text = "0x" + match.Groups[1].Value;
                            }
                        });
                        Log("[SUCESSO] libil2cpp.so e global-metadata.dat extraidos com sucesso via ADB!");
                    }
                    else
                    {
                        Log("[AVISO] Extracao via ADB nao concluiu. Verifique as mensagens acima ou selecione manualmente.");
                    }
                }
                catch (Exception ex)
                {
                    Log($"[ERRO] Falha no ADB: {ex.Message}");
                }
                finally
                {
                    Dispatcher.Invoke(() =>
                    {
                        BtnAdbExtract.IsEnabled = true;
                        BtnStartDump.IsEnabled = true;
                        PrgBar.IsIndeterminate = false;
                    });
                }
            });
        }

        private void BtnStartDump_Click(object sender, RoutedEventArgs e)
        {
            var soPath = TxtSoPath.Text.Trim();
            var metaPath = TxtMetaPath.Text.Trim();
            var outDir = string.IsNullOrWhiteSpace(TxtOutputDir.Text) ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Dumps") : TxtOutputDir.Text.Trim();
            var baseAddrStr = TxtBaseAddr.Text.Trim();

            if (!File.Exists(soPath))
            {
                MessageBox.Show("Por favor, selecione um arquivo binário ELF (.so) válido.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!File.Exists(metaPath))
            {
                MessageBox.Show("Por favor, selecione o arquivo global-metadata.dat.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Sync Config
            config.DumpMethod = ChkDumpMethod.IsChecked == true;
            config.DumpField = ChkDumpField.IsChecked == true;
            config.DumpProperty = ChkDumpProperty.IsChecked == true;
            config.DumpAttribute = ChkDumpAttribute.IsChecked == true;
            config.DumpFieldOffset = ChkDumpFieldOffset.IsChecked == true;
            config.DumpMethodOffset = ChkDumpMethodOffset.IsChecked == true;
            config.DumpTypeDefIndex = ChkDumpTypeDefIndex.IsChecked == true;
            config.GenerateDummyDll = ChkGenerateDummyDll.IsChecked == true;
            config.DummyDllAddToken = ChkDummyDllAddToken.IsChecked == true;
            config.GenerateStruct = ChkGenerateStruct.IsChecked == true;
            config.ForceDump = ChkForceDump.IsChecked == true;
            config.DumpOnlyAssemblyCSharp = ChkDumpOnlyAssemblyCSharp.IsChecked == true;

            BtnStartDump.IsEnabled = false;
            BtnStartDump.Content = "Descompilando... Aguarde";
            BtnAdbExtract.IsEnabled = false;
            PrgBar.IsIndeterminate = true;

            Task.Run(() =>
            {
                try
                {
                    Log("[INFO] Inicializando metadados...");
                    var metaBytes = File.ReadAllBytes(metaPath);
                    var metadata = new Metadata(new MemoryStream(metaBytes));
                    Log($"[OK] Metadados versão: {metadata.Version}");

                    Log("[INFO] Inicializando binário il2cpp...");
                    var soBytes = File.ReadAllBytes(soPath);
                    var il2cppMagic = BitConverter.ToUInt32(soBytes, 0);
                    var il2CppMemory = new MemoryStream(soBytes);
                    Il2Cpp il2Cpp;

                    switch (il2cppMagic)
                    {
                        default:
                            throw new NotSupportedException("Formato de binário não suportado.");
                        case 0x304F534E: // NSO
                            var nso = new NSO(il2CppMemory);
                            il2Cpp = nso.UnCompress();
                            Log("[OK] Arquitetura detectada: Nintendo Switch NSO");
                            break;
                        case 0x905A4D: // PE
                            il2Cpp = new PE(il2CppMemory);
                            Log("[OK] Arquitetura detectada: Windows PE Executable");
                            break;
                        case 0x464c457f: // ELF
                            if (soBytes.Length > 4 && soBytes[4] == 2) // ELF64
                            {
                                il2Cpp = new Elf64(il2CppMemory);
                                Log("[OK] Arquitetura detectada: ELF 64-bit (ARM64 / x86_64) - Free Fire 64 bits");
                            }
                            else
                            {
                                il2Cpp = new Elf(il2CppMemory);
                                Log("[OK] Arquitetura detectada: ELF 32-bit (ARMv7 / x86) - Free Fire 32 bits");
                            }
                            break;
                        case 0xCAFEBABE:
                        case 0xBEBAFECA:
                            var machofat = new MachoFat(new MemoryStream(soBytes));
                            var mBytes = machofat.GetMacho(0);
                            il2Cpp = new Macho64(new MemoryStream(mBytes));
                            Log("[OK] Arquitetura detectada: Mach-O 64-bit Fat");
                            break;
                        case 0xFEEDFACF:
                            il2Cpp = new Macho64(il2CppMemory);
                            Log("[OK] Arquitetura detectada: Mach-O 64-bit");
                            break;
                        case 0xFEEDFACE:
                            il2Cpp = new Macho(il2CppMemory);
                            Log("[OK] Arquitetura detectada: Mach-O 32-bit");
                            break;
                    }

                    // Base address
                    ulong baseAddr = 0;
                    if (!string.IsNullOrWhiteSpace(baseAddrStr))
                    {
                        ulong.TryParse(baseAddrStr.Replace("0x", "").Replace("0X", ""), System.Globalization.NumberStyles.HexNumber, null, out baseAddr);
                    }
                    else
                    {
                        var match = Regex.Match(Path.GetFileName(soPath), @"(?i)^([0-9a-f]{6,16})");
                        if (match.Success) ulong.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, null, out baseAddr);
                    }

                    var version = config.ForceIl2CppVersion ? config.ForceVersion : metadata.Version;
                    il2Cpp.SetProperties(version, metadata.metadataUsagesCount);

                    Log("[INFO] Resolvendo ponteiros nativos (CodeRegistration e MetadataRegistration)...");
                    // 1. Tentar primeiro direto no binário (como no console, que encontra CodeRegistration e MetadataRegistration com 100% dos RVAs e offsets)
                    var flag = il2Cpp.PlusSearch(metadata.methodDefs.Count(x => x.methodIndex >= 0), metadata.typeDefs.Length, metadata.imageDefs.Length);
                    if (!flag) flag = il2Cpp.Search();
                    if (!flag) flag = il2Cpp.SymbolSearch();

                    // 2. Se falhar e tiver baseAddr (ou se o arquivo for dump bruto de memória /proc/pid/mem), aplica ImageBase e tenta novamente
                    if (!flag && baseAddr != 0)
                    {
                        Log($"[INFO] Tentando com endereco base de memoria: 0x{baseAddr:X}...");
                        il2Cpp.ImageBase = baseAddr;
                        il2Cpp.IsDumped = true;
                        if (il2Cpp is ElfBase elf)
                        {
                            elf.Reload();
                        }
                        flag = il2Cpp.PlusSearch(metadata.methodDefs.Count(x => x.methodIndex >= 0), metadata.typeDefs.Length, metadata.imageDefs.Length);
                        if (!flag) flag = il2Cpp.Search();
                        if (!flag) flag = il2Cpp.SymbolSearch();
                    }
                    else if (baseAddr != 0)
                    {
                        // Registrou ponteiros no arquivo base com sucesso! Guarda ImageBase para cálculo de VA/RVA se desejado
                        Log($"[OK] Ponteiros nativos resolvidos com sucesso!");
                    }

                    if (!flag)
                    {
                        if (il2Cpp is Elf)
                        {
                            Log("[AVISO] Heurística padrão falhou. Aplicando registros conhecidos do Free Fire 32-bit...");
                            try
                            {
                                il2Cpp.Init(0xb0939ff4, 0xb0a0fc80);
                                flag = true;
                                Log("[OK] Registros 32-bit aplicados com sucesso! (Code: 0xb0939ff4 | Meta: 0xb0a0fc80)");
                            }
                            catch (Exception ex)
                            {
                                Log("[ERRO] Falha nos registros fixos 32-bit: " + ex.Message);
                            }
                        }
                        else
                        {
                            Log("[AVISO] Heurística automática não localizou ponteiros 64-bit.");
                        }
                    }

                    if (flag && il2Cpp.Version >= 27 && il2Cpp.IsDumped)
                    {
                        var typeDef = metadata.typeDefs[0];
                        var il2CppType = il2Cpp.types[typeDef.byvalTypeIndex];
                        metadata.ImageBase = il2CppType.data.typeHandle - metadata.header.typeDefinitionsOffset;
                    }

                    if (!outDir.EndsWith(Path.DirectorySeparatorChar.ToString()) && !outDir.EndsWith("/"))
                        outDir += Path.DirectorySeparatorChar;
                    Directory.CreateDirectory(outDir);

                    if (flag)
                    {
                        Log("[INFO] Gerando dump.cs e stringliteral.json completos...");
                        var executor = new Il2CppExecutor(metadata, il2Cpp);
                        var decompiler = new Il2CppDecompiler(executor);
                        decompiler.Decompile(config, outDir);
                        Log("[OK] dump.cs e stringliteral.json gerados com sucesso!");

                        if (config.GenerateStruct)
                        {
                            try
                            {
                                Log("[INFO] Gerando script.json e il2cpp.h para IDA / Ghidra...");
                                var structGen = new StructGenerator(executor);
                                structGen.WriteScript(outDir);
                                Log("[OK] script.json e il2cpp.h gerados com sucesso!");
                            }
                            catch (Exception ex)
                            {
                                Log("[AVISO] Aviso na geração de structs: " + ex.Message);
                            }
                        }

                        if (config.GenerateDummyDll)
                        {
                            try
                            {
                                Log("[INFO] Gerando Dummy DLLs completas (Assembly-CSharp.dll, etc) para dnSpy / ILSpy...");
                                DummyAssemblyExporter.Export(executor, outDir, config.DummyDllAddToken);
                                Log("[OK] Dummy DLLs geradas com sucesso na pasta DummyDll!");
                            }
                            catch (Exception ex)
                            {
                                Log("[AVISO] Aviso na geração de Dummy DLLs: " + ex.Message);
                            }
                        }
                    }
                    else
                    {
                        Log("[AVISO] Gerando dump completo via metadados puros (Standalone Mode)...");
                        Program.GenerateStandaloneMetadataDump(metaPath, outDir);
                        Log("[OK] dump.cs de metadados gerado com sucesso!");
                    }

                    Log($"[✓] CONCLUÍDO COM SUCESSO! Dumps salvos em: {outDir}");
                    Dispatcher.Invoke(() =>
                    {
                        ShowSuccessModal("Descompilação Concluída com Sucesso!", "Os arquivos de dump (dump.cs, script.json, il2cpp.h) e as Dummy DLLs completas foram gerados com sucesso!", outDir);
                    });
                }
                catch (Exception ex)
                {
                    Log($"[ERRO FATAL] {ex.Message}");
                    Dispatcher.Invoke(() =>
                    {
                        ShowErrorModal("Erro na Descompilação", ex.Message);
                    });
                }
                finally
                {
                    Dispatcher.Invoke(() =>
                    {
                        BtnStartDump.IsEnabled = true;
                        BtnStartDump.Content = "Descompilar Arquivos";
                        BtnAdbExtract.IsEnabled = true;
                        PrgBar.IsIndeterminate = false;
                    });
                }
            });
        }

        private string lastSuccessOutDir = "";

        private void ShowSuccessModal(string title, string message, string path)
        {
            lastSuccessOutDir = path;
            ModalTitleTxt.Text = title;
            ModalMsgTxt.Text = message;
            ModalPathTxt.Text = path;
            ModalPathBd.Visibility = Visibility.Visible;
            BtnModalOpenFolder.Visibility = Visibility.Visible;

            ModalIconBd.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(22, 101, 52));
            ModalIconTxt.Text = "✓";
            ModalIconTxt.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(74, 222, 128));

            ModalOverlay.Visibility = Visibility.Visible;
        }

        private void ShowErrorModal(string title, string message)
        {
            ModalTitleTxt.Text = title;
            ModalMsgTxt.Text = message;
            ModalPathBd.Visibility = Visibility.Collapsed;
            BtnModalOpenFolder.Visibility = Visibility.Collapsed;

            ModalIconBd.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(153, 27, 27));
            ModalIconTxt.Text = "✕";
            ModalIconTxt.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(248, 113, 113));

            ModalOverlay.Visibility = Visibility.Visible;
        }

        private void BtnModalClose_Click(object sender, RoutedEventArgs e)
        {
            ModalOverlay.Visibility = Visibility.Collapsed;
        }

        private void BtnModalOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(lastSuccessOutDir) && Directory.Exists(lastSuccessOutDir))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = lastSuccessOutDir,
                        UseShellExecute = true
                    });
                }
            }
            catch { }
            ModalOverlay.Visibility = Visibility.Collapsed;
        }

        private void BtnClearLogs_Click(object sender, RoutedEventArgs e)
        {
            TxtMiniLog.Text = "";
            TxtFullLog.Text = "";
        }
    }
}
