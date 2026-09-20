using System;
using System.IO;

namespace Il2CppDumper
{
    public static class DummyAssemblyExporter
    {
        public static void Export(Il2CppExecutor il2CppExecutor, string outputDir, bool addToken)
        {
            Directory.SetCurrentDirectory(outputDir);
            if (Directory.Exists("DummyDll"))
                Directory.Delete("DummyDll", true);
            Directory.CreateDirectory("DummyDll");
            Directory.SetCurrentDirectory("DummyDll");
            var dummy = new DummyAssemblyGenerator(il2CppExecutor, addToken);
            int total = dummy.Assemblies.Count;
            int count = 0;
            foreach (var assembly in dummy.Assemblies)
            {
                using var stream = new MemoryStream();
                assembly.Write(stream);
                File.WriteAllBytes(assembly.MainModule.Name, stream.ToArray());
                count++;
                if (total > 0)
                {
                    int percent = (int)((long)count * 100 / total);
                    DrawProgressBar("Exportando Dummy DLLs", percent);
                }
            }
            Console.WriteLine();
        }

        private static void DrawProgressBar(string taskName, int percent)
        {
            try
            {
                if (percent < 0) percent = 0;
                if (percent > 100) percent = 100;
                const int totalBlocks = 25;
                int filled = (int)Math.Round((double)percent * totalBlocks / 100);
                string bar = new string('#', filled) + new string('-', totalBlocks - filled);
                Console.Write($"\r [INFO] {taskName}: [{bar}] {percent,3}%   ");
            }
            catch { }
        }
    }
}
