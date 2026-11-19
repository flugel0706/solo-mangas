# Changelog

Todas as mudanças relevantes do Solo Mangás são registradas aqui.
O formato segue [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/)
e o projeto adere ao [Versionamento Semântico](https://semver.org/lang/pt-BR/).

## [2.8.0] — 2026-11-19

### Adicionado
- **Tema Call of Duty** (novo padrão): foto real de fundo — *Soldier
  Silhouetted in Afghanistan*, do UK Ministry of Defence (OGL v1.0) — com
  película escura, paleta carvão/oliva, dourado, tipografia Bahnschrift,
  painéis translúcidos e linhas de visor discretas.
- Seletor de tema em **Configurações → Aparência**, alternando entre
  **Call of Duty** e **Sangue & aço**; o app reinicia sozinho para aplicar.
- **Importar backup** em Configurações: restaura favoritos, histórico,
  capítulos lidos, posições e pesquisas de um `store.json` exportado, em modo
  mescla (nada é apagado e entradas repetidas não duplicam).
- **Ordem inicial dos capítulos** configurável: último → 1º ou 1º → último,
  respeitada ao abrir qualquer obra.
- Atribuição da foto no app (**Configurações → Sobre**) e em
  `Assets/ATTRIBUTION.md`.

### Alterado
- Versão exibida no título, no rodapé da barra lateral e na janela de
  atualização vem da assembly (2.8.0), sem texto fixo.
- Cabeçalhos, barra do leitor, barras de rolagem, letras de capa e películas de
  hover passam a usar a cor de destaque do tema, sem tons vermelhos fixos.
- Botão principal do tema Call of Duty usa texto escuro sobre o dourado para
  manter o contraste.
- Textos de Ajuda/Sobre atualizados para a 2.8.

### Corrigido
- O título da janela não é mais fixo em "2.7": usa a versão real da assembly
  (2.8.0), assim como o rodapé da barra lateral.
- A preferência de ordem dos capítulos vale já na primeira abertura da obra;
  antes o leitor sempre começava em "último → 1º".
- O caractere `&` do nome "Sangue & aço" aparece corretamente no diálogo de
  confirmação da troca de tema.
- A troca de tema reinicia o app pelo próprio executável também na versão
  single-file (`Environment.ProcessPath`).
- Compatibilidade total com o `store.json` da 2.7: campos novos recebem padrão
  quando ausentes e nada existente é descartado.

## [2.7.0] — 2026-11-17

### Adicionado
- Cache de capas por URL: cada imagem é baixada uma única vez e reaproveitada
  no destaque, nos cards, em favoritos e no "Continuar lendo".
- Carregamento sob demanda: imagens só são baixadas quando entram na tela.
- Cache de disco de até 1,2 GB, com limpeza automática dos arquivos mais antigos.
- Cache de resultados da busca global por 2,5 minutos.
- Cabeçalho fixo na tela inicial, com busca, botão **Atualizar**, contador de
  obras e esqueletos de carregamento.
- Cantos arredondados reais nas capas e letra de reserva quando a fonte não
  devolve a imagem.
- Busca global progressiva: cada fonte aparece assim que responde.

### Mantido
- Favoritos, histórico e progresso de leitura intactos entre versões.
- Atualização integrada com manifesto `update.json`, SHA-256 e troca do
  executável sem apagar os dados do usuário.
