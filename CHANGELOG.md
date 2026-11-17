# Changelog

Todas as mudanças relevantes do Solo Mangás são registradas aqui.
O formato segue [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/)
e o projeto adere ao [Versionamento Semântico](https://semver.org/lang/pt-BR/).

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
