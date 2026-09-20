using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Il2CppDumper
{
    public static class AdbMemoryDumper
    {
        private static string cachedAdbPath = null;

        public static string FindAdb()
        {
            if (cachedAdbPath != null && File.Exists(cachedAdbPath))
                return cachedAdbPath;

            var candidates = new[]
            {
                @"C:\Program Files\BlueStacks_nxt_cn\HD-Adb.exe",
                @"C:\Program Files\BlueStacks_nxt\HD-Adb.exe",
                @"C:\Program Files (x86)\BlueStacks_nxt\HD-Adb.exe",
                @"C:\Program Files\BlueStacks\HD-Adb.exe",
                @"C:\Program Files (x86)\BlueStacks\HD-Adb.exe",
                @"C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Android\Sdk\platform-tools\adb.exe")
            };

            foreach (var p in candidates)
            {
                if (File.Exists(p))
                {
                    cachedAdbPath = p;
                    return p;
                }
            }

            try
            {
                var psi = new ProcessStartInfo("where.exe", "adb")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                var line = proc?.StandardOutput.ReadLine();
                proc?.WaitForExit();
                if (!string.IsNullOrWhiteSpace(line) && File.Exists(line.Trim()))
                {
                    cachedAdbPath = line.Trim();
                    return cachedAdbPath;
                }
            }
            catch { }

            return null;
        }

        public static string ExecAdb(string adbPath, string args, int timeoutMs = 25000)
        {
            try
            {
                var psi = new ProcessStartInfo(adbPath, args)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc == null) return string.Empty;
                var output = proc.StandardOutput.ReadToEnd();
                var error = proc.StandardError.ReadToEnd();
                proc.WaitForExit(timeoutMs);
                return string.IsNullOrWhiteSpace(output) ? error : output;
            }
            catch (Exception ex)
            {
                return "ERR: " + ex.Message;
            }
        }

        public static bool TryDumpFromAdb(out string dumpedSoPath, out string metadataPath, string customPort = null, string customPackage = null, Action<string> log = null)
        {
            return TryDumpFromAdb(out dumpedSoPath, out metadataPath, out _, customPort, customPackage, log);
        }

        public static bool TryDumpFromAdb(out string dumpedSoPath, out string metadataPath, out ulong detectedBaseAddr, string customPort = null, string customPackage = null, Action<string> log = null)
        {
            dumpedSoPath = null;
            metadataPath = null;
            detectedBaseAddr = 0;

            void LogMsg(string msg, ConsoleColor color = ConsoleColor.White)
            {
                Console.ForegroundColor = color;
                Console.WriteLine(msg);
                Console.ResetColor();
                log?.Invoke(msg);
            }

            var adb = FindAdb();
            if (adb == null)
            {
                LogMsg("  [AVISO] ADB (HD-Adb.exe ou adb.exe) nao foi localizado no sistema.", ConsoleColor.Yellow);
                return false;
            }

            LogMsg($"  [INFO] Usando ADB: {Path.GetFileName(adb)} ({adb})", ConsoleColor.Cyan);

            // Se o usuário especificou porta customizada, tenta conectar
            string targetDevicePrefix = "";
            if (!string.IsNullOrWhiteSpace(customPort))
            {
                var port = customPort.Trim();
                LogMsg($"  [INFO] Conectando a porta {port}...", ConsoleColor.Cyan);
                var connOut = ExecAdb(adb, $"connect 127.0.0.1:{port}");
                LogMsg($"  [ADB] {connOut.Trim()}", ConsoleColor.DarkGray);
                targetDevicePrefix = $"-s 127.0.0.1:{port} ";
            }

            var devicesOut = ExecAdb(adb, "devices");
            if (!devicesOut.Contains("\tdevice"))
            {
                LogMsg("  [AVISO] Nenhum dispositivo/emulador conectado ao ADB em modo 'device'.", ConsoleColor.Yellow);
                return false;
            }

            // Se não especificou prefixo de porta, mas tem só um ou emulator-5554, detecta
            if (string.IsNullOrEmpty(targetDevicePrefix))
            {
                var linesDev = devicesOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var activeDev = linesDev.FirstOrDefault(l => l.Contains("\tdevice"));
                if (activeDev != null)
                {
                    var devId = activeDev.Split('\t')[0].Trim();
                    targetDevicePrefix = $"-s {devId} ";
                }
            }

            LogMsg($"  [OK] Conectado ao dispositivo: {targetDevicePrefix.Trim()}", ConsoleColor.Green);

            // 1. Localizar processo
            string targetPkg = null;
            int targetPid = 0;

            var candidatePkgs = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(customPackage))
            {
                candidatePkgs.Add(customPackage.Trim());
            }
            candidatePkgs.Add("com.dts.freefireth");
            candidatePkgs.Add("com.dts.freefiremax");

            foreach (var pkg in candidatePkgs)
            {
                var pidStr = ExecAdb(adb, $"{targetDevicePrefix}shell \"su -c 'pidof {pkg}' || pidof {pkg}\"").Trim();
                var match = Regex.Match(pidStr, @"\b(\d+)\b");
                if (match.Success && int.TryParse(match.Groups[1].Value, out var pid) && pid > 0)
                {
                    targetPkg = pkg;
                    targetPid = pid;
                    break;
                }
            }

            if (targetPid == 0)
            {
                LogMsg($"  [AVISO] Pacote '{customPackage ?? "Free Fire"}' nao esta aberto no emulador.", ConsoleColor.Yellow);
                return false;
            }

            LogMsg($"  [OK] Processo detectado: PID {targetPid} ({targetPkg})", ConsoleColor.Green);

            // 2. Extrair mapas de memoria do libil2cpp.so
            LogMsg("  [INFO] Analisando mapas de memoria (/proc/[pid]/maps)...", ConsoleColor.Cyan);

            var mapsOut = ExecAdb(adb, $"{targetDevicePrefix}shell \"su -c 'cat /proc/{targetPid}/maps | grep libil2cpp.so'\"");
            var lines = mapsOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                               .Where(l => l.Contains("libil2cpp.so"))
                               .ToList();

            string remoteSoPath = null;
            ulong startAddr = 0;
            ulong endAddr = 0;

            if (lines.Count > 0)
            {
                // Procurar caminho remoto do .so nos mapas
                foreach (var l in lines)
                {
                    var mPath = Regex.Match(l, @"(/data/[^\r\n]+libil2cpp\.so|/storage/[^\r\n]+libil2cpp\.so)");
                    if (mPath.Success)
                    {
                        remoteSoPath = mPath.Groups[1].Value.Trim();
                        break;
                    }
                }

                // Procurar segmento com offset 00000000 (base da biblioteca carregada)
                foreach (var l in lines)
                {
                    var m = Regex.Match(l, @"^([0-9a-fA-F]+)-([0-9a-fA-F]+)\s+[rwxp-]+\s+00000000");
                    if (m.Success)
                    {
                        var s = Convert.ToUInt64(m.Groups[1].Value, 16);
                        if (startAddr == 0 || s > startAddr) // pegar a mapping principal de código
                            startAddr = s;
                    }
                }

                // Se não achou com offset 00000000, pega primeiro endereço
                if (startAddr == 0)
                {
                    var mFirst = Regex.Match(lines[0], @"^([0-9a-fA-F]+)-([0-9a-fA-F]+)");
                    if (mFirst.Success)
                        startAddr = Convert.ToUInt64(mFirst.Groups[1].Value, 16);
                }

                detectedBaseAddr = startAddr;
                LogMsg($"  [OK] Endereco base detectado na memoria: 0x{startAddr:X}", ConsoleColor.Green);
            }
            else
            {
                LogMsg("  [INFO] libil2cpp.so nao listada em maps ativos, buscando no pacote...", ConsoleColor.Cyan);
            }

            // Se não encontrou remoteSoPath via maps, procura no /data/app
            if (string.IsNullOrEmpty(remoteSoPath))
            {
                var findOut = ExecAdb(adb, $"{targetDevicePrefix}shell \"su -c 'ls -1 /data/app/{targetPkg}*/lib/*/libil2cpp.so'\"").Trim();
                var firstLine = findOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (!string.IsNullOrEmpty(firstLine) && firstLine.EndsWith("libil2cpp.so"))
                {
                    remoteSoPath = firstLine.Trim();
                }
            }

            if (!string.IsNullOrEmpty(remoteSoPath))
            {
                if (remoteSoPath.Contains("arm64") || remoteSoPath.Contains("x86_64") || startAddr > 0xFFFFFFFF)
                {
                    LogMsg($"  [OK] Arquitetura detectada no emulador: 64-bit (ARM64 / x86_64)", ConsoleColor.Green);
                }
                else
                {
                    LogMsg($"  [OK] Arquitetura detectada no emulador: 32-bit (ARMv7 / x86)", ConsoleColor.Green);
                }
            }

            // 3. Fazer dump ou cópia direta do libil2cpp.so
            var dumpDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Dumps");
            Directory.CreateDirectory(dumpDir);

            var localDumpName = startAddr != 0 ? $"{startAddr:x}-libil2cpp.so" : "libil2cpp.so";
            var localDumpFile = Path.Combine(dumpDir, localDumpName);
            var remoteTmpDump = $"/data/local/tmp/{localDumpName}";

            bool dumpSuccess = false;

            // Tentativa A: Dump direto via dd de /proc/mem (se startAddr e endAddr válidos e processo acessível)
            if (startAddr != 0 && endAddr > startAddr)
            {
                ulong totalSize = endAddr - startAddr;
                long skipBlocks = (long)(startAddr / 4096);
                long countBlocks = (long)(totalSize / 4096);

                if (countBlocks > 0 && countBlocks < 100000)
                {
                    LogMsg($"  [INFO] Tentando extrair de /proc/{targetPid}/mem via dd...", ConsoleColor.Cyan);
                    var ddCmd = $"{targetDevicePrefix}shell \"su -c 'dd if=/proc/{targetPid}/mem of={remoteTmpDump} bs=4096 skip={skipBlocks} count={countBlocks} && chmod 777 {remoteTmpDump}'\"";
                    ExecAdb(adb, ddCmd, 30000);

                    ExecAdb(adb, $"{targetDevicePrefix}pull \"{remoteTmpDump}\" \"{localDumpFile}\"", 40000);
                    ExecAdb(adb, $"{targetDevicePrefix}shell \"su -c 'rm -f {remoteTmpDump}'\"");

                    if (File.Exists(localDumpFile) && new FileInfo(localDumpFile).Length > 1024 * 1024)
                    {
                        dumpSuccess = true;
                        LogMsg($"  [OK] Memoria extraida via /proc/mem ({new FileInfo(localDumpFile).Length / 1024 / 1024} MB)", ConsoleColor.Green);
                    }
                }
            }

            // Tentativa B: Cópia direta do libil2cpp.so do pacote no emulador (sempre 100% íntegro)
            if (!dumpSuccess && !string.IsNullOrEmpty(remoteSoPath))
            {
                LogMsg($"  [INFO] Extraindo libil2cpp.so de {remoteSoPath}...", ConsoleColor.Cyan);
                var tmpSo = "/data/local/tmp/libil2cpp_extract.so";
                ExecAdb(adb, $"{targetDevicePrefix}shell \"su -c 'cp \"{remoteSoPath}\" {tmpSo} && chmod 777 {tmpSo}'\"");
                ExecAdb(adb, $"{targetDevicePrefix}pull {tmpSo} \"{localDumpFile}\"", 60000);
                ExecAdb(adb, $"{targetDevicePrefix}shell \"su -c 'rm -f {tmpSo}'\"");

                if (File.Exists(localDumpFile) && new FileInfo(localDumpFile).Length > 1024 * 1024)
                {
                    dumpSuccess = true;
                    LogMsg($"  [OK] libil2cpp.so extraido com sucesso ({new FileInfo(localDumpFile).Length / 1024 / 1024} MB)!", ConsoleColor.Green);
                }
            }

            if (dumpSuccess)
            {
                dumpedSoPath = localDumpFile;
            }
            else
            {
                LogMsg("  [AVISO] Nao foi possivel extrair libil2cpp.so automaticamente.", ConsoleColor.Yellow);
            }

            // 4. Localizar e puxar global-metadata.dat via adb/root
            LogMsg("  [INFO] Localizando global-metadata.dat via ADB / Root...", ConsoleColor.Cyan);

            string remoteMetaPath = null;

            var metaMaps = ExecAdb(adb, $"{targetDevicePrefix}shell \"su -c 'cat /proc/{targetPid}/maps | grep global-metadata.dat'\"");
            var metaMatch = Regex.Match(metaMaps, @"(/storage[^\r\n]+|/data[^\r\n]+)");
            if (metaMatch.Success && metaMatch.Groups[1].Value.EndsWith("global-metadata.dat"))
            {
                remoteMetaPath = metaMatch.Groups[1].Value.Trim();
            }

            if (string.IsNullOrEmpty(remoteMetaPath))
            {
                var testPaths = new[]
                {
                    $"/storage/emulated/0/Android/data/{targetPkg}/files/il2cpp/Metadata/global-metadata.dat",
                    $"/data/data/{targetPkg}/files/il2cpp/Metadata/global-metadata.dat",
                    $"/sdcard/Android/data/{targetPkg}/files/il2cpp/Metadata/global-metadata.dat"
                };

                foreach (var tp in testPaths)
                {
                    var chk = ExecAdb(adb, $"{targetDevicePrefix}shell \"su -c 'ls {tp}'\"").Trim();
                    if (chk.Contains("global-metadata.dat"))
                    {
                        remoteMetaPath = tp;
                        break;
                    }
                }
            }

            if (!string.IsNullOrEmpty(remoteMetaPath))
            {
                LogMsg($"  [OK] global-metadata.dat localizado: {remoteMetaPath}", ConsoleColor.Green);

                var localMetaFile = Path.Combine(dumpDir, "global-metadata.dat");
                LogMsg("  [INFO] Baixando global-metadata.dat via ADB...", ConsoleColor.Cyan);

                var tmpMeta = "/data/local/tmp/meta_extract.dat";
                ExecAdb(adb, $"{targetDevicePrefix}shell \"su -c 'cp \"{remoteMetaPath}\" {tmpMeta} && chmod 777 {tmpMeta}'\"");
                ExecAdb(adb, $"{targetDevicePrefix}pull {tmpMeta} \"{localMetaFile}\"", 40000);
                ExecAdb(adb, $"{targetDevicePrefix}shell \"su -c 'rm -f {tmpMeta}'\"");

                if (File.Exists(localMetaFile) && new FileInfo(localMetaFile).Length > 1024)
                {
                    metadataPath = localMetaFile;
                    LogMsg($"  [OK] global-metadata.dat salvo com sucesso ({new FileInfo(localMetaFile).Length / 1024 / 1024} MB)", ConsoleColor.Green);
                }
            }
            else
            {
                LogMsg("  [AVISO] global-metadata.dat nao localizado automaticamente no emulador.", ConsoleColor.Yellow);
            }

            return dumpedSoPath != null && metadataPath != null;
        }
    }
}
