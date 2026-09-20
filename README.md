# VZX-DUMPER-V2

Ferramenta avançada para análise, dumping e engenharia reversa de binários e metadados Unity Il2Cpp com suporte a interface gráfica moderna e automações dedicadas.

## 🚀 Componentes

- **Il2CppDumper_Core**: Motor aprimorado para parseamento e extração de estruturas, offsets e metadados de executáveis Il2Cpp (ELF, PE, Mach-O, NSO, WebAssembly). Inclui rotinas dedicadas para geração de scripts IDA, Ghidra e dumps estruturados.
- **VZX_Dumper_UI**: Interface de usuário moderna (WPF/ImGui-inspired) para gerenciamento de dumps, monitoramento de processos e exportação de offsets em tempo real.
- **COMPILAR_PROJETO.bat**: Script automatizado para compilação rápida da solução em ambiente Windows.

## 🛠️ Tecnologias e Requisitos

- .NET SDK (6.0+ / compatível com WPF no Windows)
- Visual Studio 2022 ou posterior
- Plataforma: Windows x64

## 📦 Como Compilar

Execute o arquivo `COMPILAR_PROJETO.bat` ou abra `VZX_DUMPER_V2.sln` diretamente no Visual Studio e compile em modo `Release` ou `Debug`.
