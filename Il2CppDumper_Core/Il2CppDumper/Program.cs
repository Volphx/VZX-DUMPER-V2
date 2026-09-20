using System.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Diagnostics;
using Mono.Cecil;

namespace Il2CppDumper
{
    public class Program
    {
        private static Config config;
        private static readonly Stopwatch timer = new();

        private static void PrintBanner()
        {
            try { Console.Clear(); } catch { }
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(@"
 ┌─────────────────────────────────────────────────────────────┐
 │                IL2CPPDUMPER - PRO EDITION                   │
 │           Descompilador de Binários Unity [PT-BR]           │
 └─────────────────────────────────────────────────────────────┘");
            Console.ResetColor();
            Console.WriteLine();
        }

        private static void LogInfo(string msg)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("[INFO] ");
            Console.ResetColor();
            Console.WriteLine(msg);
        }

        private static void LogSucesso(string msg)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("[OK] ");
            Console.ResetColor();
            Console.WriteLine(msg);
        }

        private static void LogAviso(string msg)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("[AVISO] ");
            Console.ResetColor();
            Console.WriteLine(msg);
        }

        private static void LogErro(string msg)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("[ERRO] ");
            Console.ResetColor();
            Console.WriteLine(msg);
        }

        private static void LogDestaque(string titulo, string valor)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("  -> ");
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(titulo.PadRight(22));
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(valor);
            Console.ResetColor();
        }

        [STAThread]
        static void Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            try { Console.Title = "Il2CppDumper Console [PT-BR] - Metadata v31"; } catch { }
            PrintBanner();

            bool firstRun = true;
            while (true)
            {
                // --- Recarrega config a cada iteração ---
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
                if (File.Exists(configPath))
                {
                    try { config = JsonSerializer.Deserialize<Config>(File.ReadAllText(configPath)); }
                    catch { config = new Config(); }
                }
                else
                {
                    config = new Config();
                    try
                    {
                        var opts = new JsonSerializerOptions { WriteIndented = true };
                        File.WriteAllText(configPath, JsonSerializer.Serialize(config, opts));
                    }
                    catch { }
                }

                string il2cppPath = null;
                string metadataPath = null;
                string outputDir = null;

                // --- Argumento de linha de comando / drag-and-drop (só no primeiro run) ---
                if (firstRun && args.Length == 1 && (args[0] == "-h" || args[0] == "--help" || args[0] == "/?" || args[0] == "/h"))
                {
                    ShowHelp();
                    return;
                }
                if (firstRun && args.Length > 0)
                {
                    foreach (var arg in args)
                    {
                        if (File.Exists(arg))
                        {
                            var file = File.ReadAllBytes(arg);
                            il2cppPath ??= (file.Length >= 4 && BitConverter.ToUInt32(file, 0) == 0xFAB11BAF) ? null : arg;
                            metadataPath ??= (file.Length >= 4 && BitConverter.ToUInt32(file, 0) == 0xFAB11BAF) ? arg : null;
                        }
                        else if (Directory.Exists(arg))
                        {
                            var soFiles = Directory.GetFiles(arg, "*libil2cpp*.so", SearchOption.AllDirectories);
                            if (soFiles.Length == 0) soFiles = Directory.GetFiles(arg, "*.so", SearchOption.AllDirectories);
                            if (soFiles.Length > 0) il2cppPath ??= soFiles[0];
                            var datFiles = Directory.GetFiles(arg, "*global-metadata.dat", SearchOption.AllDirectories);
                            if (datFiles.Length > 0) metadataPath ??= datFiles[0];
                        }
                    }
                }
                firstRun = false;

                // ================================================================
                //  MENU PRINCIPAL — aparece toda vez que inicia um novo dump
                // ================================================================
                if (il2cppPath == null && metadataPath == null)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine("  Como deseja carregar os arquivos?\n");
                    Console.ResetColor();

                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.Write("  [1]");
                    Console.ResetColor();
                    Console.Write(" Extrair da memoria");
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine("  (BlueStacks / Free Fire / emulador)");
                    Console.ResetColor();

                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.Write("  [2]");
                    Console.ResetColor();
                    Console.Write(" Selecionar manualmente");
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine("  (libil2cpp.so + global-metadata.dat)");
                    Console.ResetColor();

                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine("\n  [ESC] Sair");
                    Console.ResetColor();
                    Console.WriteLine();

                    ConsoleKey menuKey;
                    while (true)
                    {
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.Write("  > ");
                        Console.ResetColor();
                        var mk = Console.ReadKey(true);
                        Console.WriteLine();
                        if (mk.Key == ConsoleKey.Escape) { goto Sair; }
                        if (mk.KeyChar == '1') { menuKey = ConsoleKey.D1; break; }
                        if (mk.KeyChar == '2') { menuKey = ConsoleKey.D2; break; }
                    }

                    Console.WriteLine();

                    // ============================================================
                    //  OPCAO 1 — Memoria via ADB / Root / BlueStacks
                    // ============================================================
                    if (menuKey == ConsoleKey.D1)
                    {
                        LogInfo("Iniciando extracao direta via ADB / Root...");
                        Console.WriteLine();

                        Console.ForegroundColor = ConsoleColor.White;
                        Console.Write("  Porta ADB (ou ENTER para auto): ");
                        Console.ResetColor();
                        var portInput = Console.ReadLine()?.Trim();

                        Console.ForegroundColor = ConsoleColor.White;
                        Console.Write("  Pacote alvo (ou ENTER para com.dts.freefireth): ");
                        Console.ResetColor();
                        var pkgInput = Console.ReadLine()?.Trim();
                        if (string.IsNullOrEmpty(pkgInput)) pkgInput = "com.dts.freefireth";

                        Console.WriteLine();
                        if (AdbMemoryDumper.TryDumpFromAdb(out var adbSo, out var adbMeta, portInput, pkgInput))
                        {
                            il2cppPath = adbSo;
                            metadataPath = adbMeta;
                        }
                        else
                        {
                            LogAviso("Extracao direta via ADB falhou ou foi cancelada. Tentando arquivos do emulador no disco...");
                            Console.WriteLine();

                            // Detecta processos BlueStacks / LDPlayer / NoxPlayer / Memu
                            var emulatorKeywords = new[] { "HD-Player", "BlueStacks", "Bluestacks", "dnplayer", "NoxVMHandle", "MEmu", "LD", "Nox" };
                            var allProcs = Process.GetProcesses();
                            var emulators = allProcs
                                .Where(p => emulatorKeywords.Any(k => p.ProcessName.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0))
                                .ToList();

                            // Busca diretórios do BlueStacks onde os APKs são extraídos
                            var bsPaths = new List<string>();
                            var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

                            var bsRoots = new[]
                            {
                                Path.Combine(programData, "BlueStacks_nxt"),
                                Path.Combine(programData, "BlueStacks"),
                                Path.Combine(localApp, "BlueStacks"),
                                Path.Combine(localApp, "BlueStacks_nxt"),
                                @"C:\BlueStacks_nxt",
                                @"C:\ProgramData\BlueStacks_nxt",
                            };

                            foreach (var root in bsRoots)
                            {
                                if (!Directory.Exists(root)) continue;
                                try
                                {
                                    foreach (var f in Directory.EnumerateFiles(root, "libil2cpp.so", SearchOption.AllDirectories))
                                        bsPaths.Add(f);
                                }
                                catch { }
                            }

                            if (emulators.Count > 0)
                            {
                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.Write("  [OK]");
                                Console.ResetColor();
                                Console.WriteLine($" {emulators.Count} processo(s) de emulador detectado(s):");
                                foreach (var ep in emulators)
                                {
                                    Console.ForegroundColor = ConsoleColor.DarkGray;
                                    Console.WriteLine($"       - {ep.ProcessName} (PID {ep.Id})");
                                    Console.ResetColor();
                                }
                                Console.WriteLine();
                            }

                            if (bsPaths.Count > 0)
                            {
                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.Write("  [OK]");
                                Console.ResetColor();
                                Console.WriteLine($" {bsPaths.Count} libil2cpp.so encontrado(s) no BlueStacks:");
                                Console.WriteLine();

                                for (int i = 0; i < Math.Min(bsPaths.Count, 9); i++)
                                {
                                    Console.ForegroundColor = ConsoleColor.Cyan;
                                    Console.Write($"  [{i + 1}]");
                                    Console.ResetColor();
                                    Console.ForegroundColor = ConsoleColor.DarkGray;
                                    var parts = bsPaths[i].Split(Path.DirectorySeparatorChar);
                                    var shortPath = parts.Length > 3
                                        ? "..." + Path.DirectorySeparatorChar + string.Join(Path.DirectorySeparatorChar.ToString(), parts[^3..])
                                        : bsPaths[i];
                                    Console.Write($" {shortPath}");
                                    Console.ResetColor();
                                    Console.WriteLine();
                                }
                                Console.WriteLine();

                                int escolha = -1;
                                while (escolha < 0)
                                {
                                    Console.ForegroundColor = ConsoleColor.White;
                                    Console.Write("  Selecione [1");
                                    if (Math.Min(bsPaths.Count, 9) > 1) Console.Write($"-{Math.Min(bsPaths.Count, 9)}");
                                    Console.Write("] ou [M] para selecionar manualmente: ");
                                    Console.ResetColor();
                                    var escolhaKey = Console.ReadKey(true);
                                    Console.WriteLine();

                                    if (escolhaKey.KeyChar >= '1' && escolhaKey.KeyChar <= '9')
                                    {
                                        int idx = escolhaKey.KeyChar - '1';
                                        if (idx < bsPaths.Count)
                                        {
                                            il2cppPath = bsPaths[idx];
                                            escolha = idx;
                                        }
                                    }
                                    else if (escolhaKey.KeyChar == 'm' || escolhaKey.KeyChar == 'M')
                                    {
                                        escolha = -2;
                                    }
                                }

                                if (escolha >= 0)
                                {
                                    LogSucesso($"Selecionado: {Path.GetFileName(il2cppPath)}");
                                    Console.WriteLine();

                                    var soDir = Path.GetDirectoryName(il2cppPath);
                                    var candidateMeta = new[]
                                    {
                                        Path.Combine(soDir, "global-metadata.dat"),
                                        Path.Combine(soDir, "..", "global-metadata.dat"),
                                        Path.Combine(soDir, "..", "assets", "bin", "Data", "Managed", "Metadata", "global-metadata.dat"),
                                    };
                                    foreach (var cm in candidateMeta)
                                    {
                                        if (File.Exists(cm)) { metadataPath = Path.GetFullPath(cm); break; }
                                    }

                                    if (metadataPath != null)
                                    {
                                        LogSucesso($"Metadata encontrado automaticamente: {Path.GetFileName(metadataPath)}");
                                    }
                                    else
                                    {
                                        LogAviso("global-metadata.dat nao encontrado automaticamente.");
                                        LogInfo("Abrindo seletor de metadata...");
                                        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                                        {
                                            var ofd = new OpenFileDialog
                                            {
                                                Title = "Selecione o global-metadata.dat",
                                                Filter = "Metadata Il2Cpp (global-metadata.dat)|global-metadata.dat|Todos os arquivos|*.*"
                                            };
                                            if (ofd.ShowDialog()) metadataPath = ofd.FileName;
                                            else { LogAviso("Cancelado."); goto AskNext; }
                                        }
                                    }
                                }
                            }
                            if (il2cppPath == null && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                            {
                                var ofd1 = new OpenFileDialog
                                {
                                    Title = "Selecione o libil2cpp.so (dump de memoria)",
                                    Filter = "Binario Il2Cpp (*.so;*.*)|*.so;*.*"
                                };
                                if (ofd1.ShowDialog()) il2cppPath = ofd1.FileName;
                                else { LogAviso("Cancelado."); goto AskNext; }
                            }
                            if (metadataPath == null && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                            {
                                var ofd2 = new OpenFileDialog
                                {
                                    Title = "Selecione o global-metadata.dat",
                                    Filter = "Metadata Il2Cpp (global-metadata.dat)|global-metadata.dat|Todos os arquivos|*.*"
                                };
                                if (ofd2.ShowDialog()) metadataPath = ofd2.FileName;
                                else { LogAviso("Cancelado."); goto AskNext; }
                            }
                        }
                    }
                    // ============================================================
                    //  OPCAO 2 — Manual
                    // ============================================================
                    else
                    {
                        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                        {
                            LogInfo("Abrindo seletor para o arquivo binario ELF (.so)...");
                            var ofd = new OpenFileDialog
                            {
                                Title = "Selecione o arquivo binario ELF (libil2cpp.so)",
                                Filter = "Binario Il2Cpp (*.so;*.*)|*.so;*.*"
                            };
                            if (ofd.ShowDialog()) il2cppPath = ofd.FileName;
                            else { LogAviso("Operacao cancelada."); goto AskNext; }

                            LogInfo("Abrindo seletor para o global-metadata.dat...");
                            var ofd2 = new OpenFileDialog
                            {
                                Title = "Selecione o arquivo de metadados (global-metadata.dat)",
                                Filter = "Metadata Il2Cpp (global-metadata.dat)|global-metadata.dat|Todos os arquivos|*.*"
                            };
                            if (ofd2.ShowDialog()) metadataPath = ofd2.FileName;
                            else { LogAviso("Operacao cancelada."); goto AskNext; }
                        }
                    }
                }

                if (il2cppPath == null || metadataPath == null)
                {
                    ShowHelp();
                    goto AskNext;
                }

                // --- Output sempre em Dumps\ ao lado do exe ---
                outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Dumps") + Path.DirectorySeparatorChar;
                Directory.CreateDirectory(outputDir);

                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"  SO:       {Path.GetFileName(il2cppPath)}");
                Console.WriteLine($"  Metadata: {Path.GetFileName(metadataPath)}");
                Console.WriteLine($"  Saida:    {outputDir}");
                Console.ResetColor();
                Console.WriteLine();

                timer.Restart();
                try
                {
                    if (Init(il2cppPath, metadataPath, out var metadata, out var il2Cpp))
                    {
                        Dump(metadata, il2Cpp, outputDir);
                    }
                    else
                    {
                        LogAviso("Geracao direta falhou. Gerando dump via metadados puros...");
                        GenerateStandaloneMetadataDump(metadataPath, outputDir);
                    }
                }
                catch (Exception e)
                {
                    LogErro("Erro inesperado: " + e.Message);
                    if (!File.Exists(Path.Combine(outputDir, "dump.cs")))
                    {
                        LogInfo("Modo de recuperacao por metadados...");
                        GenerateStandaloneMetadataDump(metadataPath, outputDir);
                    }
                }
                timer.Stop();

                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  [CONCLUIDO] {timer.Elapsed.TotalSeconds:F2}s");
                Console.ResetColor();

            AskNext:
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.Write("  [ENTER]");
                Console.ResetColor();
                Console.Write(" Novo dump  ");
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.Write("[ESC]");
                Console.ResetColor();
                Console.WriteLine(" Sair");
                Console.WriteLine();

                while (true)
                {
                    var k = Console.ReadKey(true);
                    if (k.Key == ConsoleKey.Enter) break;
                    if (k.Key == ConsoleKey.Escape) goto Sair;
                }

                Console.Clear();
                PrintBanner();
            }

            Sair:
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("\n  Ate mais!\n");
            Console.ResetColor();
        }

        static void ShowHelp()
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Modo de uso:");
            Console.ResetColor();
            Console.WriteLine($"  {AppDomain.CurrentDomain.FriendlyName} <arquivo-so> <global-metadata.dat> [pasta-de-saida]");
            Console.WriteLine($"  Ou simplesmente arraste a pasta do jogo para cima do executável!");
        }

        private static bool Init(string il2cppPath, string metadataPath, out Metadata metadata, out Il2Cpp il2Cpp)
        {
            LogInfo("Inicializando metadados...");
            var metadataBytes = File.ReadAllBytes(metadataPath);
            metadata = new Metadata(new MemoryStream(metadataBytes));
            LogDestaque("Versão dos Metadados:", metadata.Version.ToString());

            LogInfo("Inicializando binário il2cpp...");
            var il2cppBytes = File.ReadAllBytes(il2cppPath);
            var il2cppMagic = BitConverter.ToUInt32(il2cppBytes, 0);
            var il2CppMemory = new MemoryStream(il2cppBytes);

            switch (il2cppMagic)
            {
                default:
                    throw new NotSupportedException("Formato de arquivo il2cpp não suportado.");
                case 0x304F534E:
                    var nso = new NSO(il2CppMemory);
                    il2Cpp = nso.UnCompress();
                    break;
                case 0x905A4D: //PE
                    il2Cpp = new PE(il2CppMemory);
                    break;
                case 0x464c457f: //ELF
                    if (il2cppBytes[4] == 2) //ELF64
                    {
                        il2Cpp = new Elf64(il2CppMemory);
                        LogDestaque("Arquitetura:", "ELF 64-bit (ARM64 / x86_64)");
                    }
                    else
                    {
                        il2Cpp = new Elf(il2CppMemory);
                        LogDestaque("Arquitetura:", "ELF 32-bit (ARMv7 / x86)");
                    }
                    break;
                case 0xCAFEBABE:
                case 0xBEBAFECA:
                    var machofat = new MachoFat(new MemoryStream(il2cppBytes));
                    il2cppBytes = machofat.GetMacho(0);
                    il2Cpp = new Macho64(new MemoryStream(il2cppBytes));
                    break;
                case 0xFEEDFACF:
                    il2Cpp = new Macho64(il2CppMemory);
                    break;
                case 0xFEEDFACE:
                    il2Cpp = new Macho(il2CppMemory);
                    break;
            }

            var version = config.ForceIl2CppVersion ? config.ForceVersion : metadata.Version;
            il2Cpp.SetProperties(version, metadata.metadataUsagesCount);

            if (config.ForceDump || il2Cpp.CheckDump())
            {
                if (il2Cpp is ElfBase elf)
                {
                    LogAviso("Detectado que o arquivo é um dump de memória ou protegido.");
                    ulong DumpAddr = 0;

                    // Auto-detecção pelo nome do arquivo
                    var match = System.Text.RegularExpressions.Regex.Match(Path.GetFileName(il2cppPath), @"(?i)^([0-9a-f]{6,16})");
                    if (match.Success && ulong.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, null, out var autoAddr))
                    {
                        DumpAddr = autoAddr;
                        LogSucesso($"Endereço base detectado automaticamente pelo nome: 0x{DumpAddr:X}");
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.Write("Digite o endereço de memória base (ou 0 para ignorar): ");
                        Console.ResetColor();
                        var line = Console.ReadLine();
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            ulong.TryParse(line.Trim().Replace("0x", "").Replace("0X", ""), System.Globalization.NumberStyles.HexNumber, null, out DumpAddr);
                        }
                    }

                    if (DumpAddr != 0)
                    {
                        il2Cpp.ImageBase = DumpAddr;
                        il2Cpp.IsDumped = true;
                        if (!config.NoRedirectedPointer)
                        {
                            elf.Reload();
                        }
                    }
                }
                else
                {
                    il2Cpp.IsDumped = true;
                }
            }

            LogInfo("Pesquisando registros CodeRegistration e MetadataRegistration...");
            try
            {
                var flag = il2Cpp.PlusSearch(metadata.methodDefs.Count(x => x.methodIndex >= 0), metadata.typeDefs.Length, metadata.imageDefs.Length);
                if (!flag)
                {
                    flag = il2Cpp.Search();
                }
                if (!flag)
                {
                    flag = il2Cpp.SymbolSearch();
                }
                if (!flag)
                {
                    if (il2Cpp is Elf)
                    {
                        LogAviso("Pesquisa heurística falhou. Aplicando registros conhecidos do Free Fire 32-bit...");
                        try
                        {
                            il2Cpp.Init(0xb0939ff4, 0xb0a0fc80);
                            flag = true;
                            LogSucesso("Registros 32-bit aplicados com sucesso! (Code: 0xb0939ff4 | Meta: 0xb0a0fc80)");
                        }
                        catch (Exception ex)
                        {
                            LogErro("Falha ao registrar endereços fixos 32-bit: " + ex.Message);
                        }
                    }
                }

                if (!flag)
                {
                    LogAviso("Não foi possível resolver ponteiros nativos automaticamente.");
                    return false;
                }

                if (il2Cpp.Version >= 27 && il2Cpp.IsDumped)
                {
                    var typeDef = metadata.typeDefs[0];
                    var il2CppType = il2Cpp.types[typeDef.byvalTypeIndex];
                    metadata.ImageBase = il2CppType.data.typeHandle - metadata.header.typeDefinitionsOffset;
                }
            }
            catch (Exception e)
            {
                LogErro("Erro ao processar registros de execução: " + e.Message);
                return false;
            }

            return true;
        }

        public static void GenerateStandaloneMetadataDump(string metadataPath, string outputDir)
        {
            try
            {
                using var stream = new MemoryStream(File.ReadAllBytes(metadataPath));
                var meta = new Metadata(stream);
                Directory.CreateDirectory(outputDir);
                var dumpPath = Path.Combine(outputDir, "dump.cs");
                using var writer = new StreamWriter(new FileStream(dumpPath, FileMode.Create), new UTF8Encoding(false));

                for (var i = 0; i < meta.imageDefs.Length; i++)
                {
                    var img = meta.imageDefs[i];
                    writer.WriteLine($"// Image {i}: {meta.GetStringFromIndex(img.nameIndex)} - {img.typeStart}");
                }

                foreach (var img in meta.imageDefs)
                {
                    var imageName = meta.GetStringFromIndex(img.nameIndex);
                    if (config != null && config.DumpOnlyAssemblyCSharp && !imageName.Equals("Assembly-CSharp.dll", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var typeEnd = Math.Min(img.typeStart + img.typeCount, meta.typeDefs.Length);
                    for (int t = img.typeStart; t < typeEnd; t++)
                    {
                        var typeDef = meta.typeDefs[t];
                        var typeName = meta.GetStringFromIndex(typeDef.nameIndex);
                        var nsName = meta.GetStringFromIndex(typeDef.namespaceIndex);

                        writer.WriteLine($"\n// Namespace: {nsName}");
                        writer.WriteLine($"public class {typeName} // TypeDefIndex: {t}\n{{");

                        var fieldEnd = Math.Min(typeDef.fieldStart + typeDef.field_count, meta.fieldDefs.Length);
                        if (typeDef.field_count > 0)
                        {
                            writer.WriteLine("\t// Fields");
                            for (int f = typeDef.fieldStart; f < fieldEnd; f++)
                            {
                                var fieldDef = meta.fieldDefs[f];
                                writer.WriteLine($"\tpublic object {meta.GetStringFromIndex(fieldDef.nameIndex)};");
                            }
                        }

                        var methodEnd = Math.Min(typeDef.methodStart + typeDef.method_count, meta.methodDefs.Length);
                        if (typeDef.method_count > 0)
                        {
                            writer.WriteLine("\n\t// Methods");
                            for (int m = typeDef.methodStart; m < methodEnd; m++)
                            {
                                var methodDef = meta.methodDefs[m];
                                writer.WriteLine($"\tpublic void {meta.GetStringFromIndex(methodDef.nameIndex)}() {{ }}");
                            }
                        }
                        writer.WriteLine("}");
                    }
                }

                LogSucesso($"dump.cs gerado com sucesso em: {dumpPath}");
            }
            catch (Exception ex)
            {
                LogErro("Falha no Standalone Dump: " + ex.Message);
            }
        }

        private static void Dump(Metadata metadata, Il2Cpp il2Cpp, string outputDir)
        {
            var executor = new Il2CppExecutor(metadata, il2Cpp);
            var decompiler = new Il2CppDecompiler(executor);
            decompiler.Decompile(config, outputDir);
            LogSucesso("dump.cs gerado com sucesso!");

            if (config.GenerateStruct)
            {
                try
                {
                    LogInfo("Gerando arquivos de estruturas (script.json)...");
                    var scriptGenerator = new StructGenerator(executor);
                    scriptGenerator.WriteScript(outputDir);
                    LogSucesso("Estruturas geradas com sucesso!");
                }
                catch (Exception ex)
                {
                    LogAviso("Aviso na geração de structs: " + ex.Message);
                }
            }

            if (config.GenerateDummyDll)
            {
                try
                {
                    LogInfo("Gerando Dummy DLLs para análise no dnSpy / ILSpy...");
                    DummyAssemblyExporter.Export(executor, outputDir, config.DummyDllAddToken);
                    LogSucesso("Dummy DLLs geradas com sucesso na pasta DummyDll!");
                }
                catch (Exception ex)
                {
                    LogAviso("Aviso na exportação de Dummy DLLs: " + ex.Message);
                }
            }
        }
    }
}
