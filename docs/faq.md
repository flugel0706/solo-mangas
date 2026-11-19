# FAQ

**O Solo Mangás é gratuito?**
Sim. O app é gratuito, sem anúncios dentro da interface e sem assinatura. O
código é aberto sob licença MIT.

**O app hospeda mangás?**
Não. Ele apenas exibe o que as APIs e os sites públicos das fontes servem. As
obras pertencem aos autores e às fontes originais. Paywall e capítulos pagos
continuam pagos.

**Preciso instalar .NET ou WebView2?**
Não. O executável é autocontido e usa WPF nativo — nada de navegador embutido.

**Onde ficam meus favoritos e meu histórico?**
Em `%APPDATA%\SoloMangas\store.json`. A sessão da conta Mangeek fica em
`%APPDATA%\MangeekPC\session.json`. O cache de imagens fica em
`%LOCALAPPDATA%\SoloMangas\cache\img`.

**A conta Mangeek sincroniza com o celular?**
Sim. Moedas, favoritos, histórico e progresso da conta Mangeek são os mesmos do
app Android. As outras fontes guardam favoritos e histórico apenas no PC.

**Como ligo o conteúdo +18?**
No botão **＋18** da barra lateral ou em **Configurações → Conteúdo +18**. Isso
libera obras adultas nas fontes normais e as quatro fontes exclusivamente +18
(Hanami Heaven, HQ Brasa, Melhor Hentai e MangaDex +18).

**A busca é global?**
Por padrão sim: consulta todas as fontes visíveis e agrupa títulos repetidos.
Cada fonte aparece assim que responde. Dá para desligar em Configurações.

**Como continuo de onde parei?**
Abra a obra em **Continuar lendo** na tela inicial ou no **Histórico**. A posição
é salva por capítulo e restaurada ao reabrir.

**Como atualizo o app?**
O próprio app verifica, baixa, confere o SHA-256 e troca o executável. Use
**Configurações → Verificar atualizações**. Também é possível baixar o novo
`.exe` manualmente na página de Releases e substituir o antigo.

**Por que o antivírus reclamou?**
O app baixa imagens da internet e substitui o próprio executável ao atualizar —
comportamentos que podem disparar heurística. O código é aberto; compile você
mesmo e confira o hash publicado.

**Funciona offline?**
Favoritos, histórico e páginas já carregadas funcionam offline. Catálogo, busca,
capítulos novos e imagens novas precisam de internet.

**Funciona em Linux, macOS ou Windows ARM?**
Não. O app é WPF, Windows x64.

**Como faço backup?**
**Configurações → Backup dos dados** exporta o `store.json`. Para restaurar, use
**Configurações → Restaurar backup**: o app importa favoritos, histórico,
capítulos lidos, posições e pesquisas e mescla com o que já existe, sem apagar
nem duplicar. Também dá para fechar o app e copiar o JSON de volta para
`%APPDATA%\SoloMangas\store.json`.

**Como troco o tema?**
Em **Configurações → Aparência**, o botão do tema alterna entre **Call of Duty**
e **Sangue & aço**. O app pergunta se pode reiniciar; confirme para aplicar na
hora. A escolha fica salva para as próximas aberturas.

**Uma fonte parou de funcionar. O que fazer?**
A fonte pode estar fora do ar ou ter mudado o layout. Teste outra fonte e ative
o log com `SOLO_DEBUG=1` antes de abrir o app; o arquivo fica em
`%TEMP%\solo2.log`.

**Como compilo o projeto?**
Veja [docs/compilacao.md](compilacao.md). Resumo:

```powershell
dotnet publish src/SoloMangas/SoloMangas.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true -o dist
```

**Posso usar comercialmente?**
A licença MIT permite uso, modificação e distribuição, mantendo o aviso de
copyright. Respeite os termos das fontes e os direitos das obras.
