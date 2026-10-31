# VZX Dumper

Aplicativo Windows com interface WPF para descompilar IL2CPP, buscar offsets e analisar candidatos a RVA em bibliotecas ARMv7 e ARM64.

## Instalação

Baixe `VZX-Dumper-v2.1.0-win-x64.zip` na página Releases, extraia e execute `VZX-Dumper.exe`.

Requer Windows x64 e **.NET Desktop Runtime 10 x64**. A versão compacta não inclui o runtime. O executável Windows é de 64 bits; a análise aceita arquivos de jogos de 32 e 64 bits.

## Uso

1. Em **Descompilação**, selecione a biblioteca ELF e os metadados correspondentes ou utilize a extração ADB.
2. Em **Offsets + RVA**, selecione `dump.cs` e `libil2cpp.so` da mesma versão e clique em **Analisar arquivos**.
3. Consulte resultados, pesquise campos, filtre bones ou pendências e copie ou exporte o header C++.

A análise automática pode ser ativada ao concluir o dump. Campos ausentes ou ambíguos permanecem pendentes. O RVA exibido é um candidato estático e não constitui confirmação em memória. Bones sem identificação anatômica confirmada conservam nomes `bone_unidentified_XX`.

### ESP de teste

O botão **Testar ESP** usa uma cópia dos resultados da análise atual em uma sessão de leitura do BlueStacks. O jogo deve estar em primeiro plano. Há seleção de PID quando existem várias instâncias. Nova análise ou fechamento do Dumper encerra a sessão.

O leitor usa consulta/leitura, sem injeção ou escrita no processo. A DLL nativa é carregada no próprio Dumper. Os perfis auxiliares Unity/IL2CPP e BstkVMM precisam ser compatíveis. O teste verifica os caminhos necessários ao desenho e não certifica todos os offsets da tabela. O modo ao vivo ARMv7 não foi validado nesta entrega.

## Compilação

Instale:

- .NET SDK 10 no Windows;
- Visual Studio 2022 ou Build Tools com **Desktop development with C++**, MSVC x64 e Windows SDK.

Execute `PUBLICAR_RELEASE.cmd`. O executável fica em `artifacts/win-x64/VZX-Dumper.exe`. Para compilar a solução sem publicar, execute `COMPILAR_PROJETO.bat`.

O motor C++ é compilado automaticamente e incorporado ao aplicativo. Não há dependência de Python. Na execução, o módulo é extraído para `%LOCALAPPDATA%\VZX-Dumper\Native`; o cache de leitura fica em `%LOCALAPPDATA%\VZX-Dumper\LiveTest`.

## Estrutura

- `VZX_Dumper_UI`: interface, integração nativa e diagnóstico;
- `VZX_Dumper_UI/Native`: motor C++ de offsets/RVA e leitura de teste;
- `Il2CppDumper_Core`: núcleo de descompilação derivado de Il2CppDumper.

## Créditos e licença

O núcleo Il2CppDumper é de Perfare e contribuidores, sob licença MIT, preservada em `Il2CppDumper_Core/LICENSE`. Consulte os avisos nos componentes. Nenhuma licença adicional foi atribuída nesta atualização.

Dumps, bibliotecas de jogos, metadados, caches e saídas locais não são distribuídos no repositório nem na release.
