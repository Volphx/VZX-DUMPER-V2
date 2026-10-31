# v2.1.1

- Corrige o início do ESP V7A quando o dump contém offsets inválidos (0x6A6A6A6A).
- Recupera campos essenciais por leitura de FieldInfo, com conferência pelo global-metadata.dat v31 ao lado da biblioteca selecionada.
- Rejeita divergências entre campos recuperados e valores existentes no dump.
- Mantém a tabela como resultado estático; a recuperação é exclusiva da sessão de teste.

Validação do backend V7A em treinamento: quatro alvos com 200/200 HP, coordenadas e ossos válidos em três quadros consecutivos; encerramento da sessão aprovado. A sobreposição não foi verificada visualmente nesta execução.

Requer Windows x64 e .NET Desktop Runtime 10 x64.

# v2.1.0

- Interface reorganizada, compacta e consistente entre as abas.
- Correção do arraste da janela e dos ícones de minimizar e fechar.
- Busca integrada de offsets e candidatos a RVA para ARMv7/ARM64.
- Filtros, pesquisa, cópia e exportação de resultados.
- ESP de diagnóstico com leitura dos resultados atuais.
- Publicação compacta Windows x64 e scripts de compilação.

Requer .NET Desktop Runtime 10 x64. Candidatos estáticos e campos pendentes continuam identificados na interface. A validação ao vivo ARMv7 permanece pendente.
