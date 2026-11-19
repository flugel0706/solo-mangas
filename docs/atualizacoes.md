# Atualizações

A partir da **2.5.0** o próprio app verifica, baixa e instala versões novas:
ele fecha, substitui o executável e reabre já atualizado. Nada é apagado do
perfil — favoritos, histórico, lidos e configurações ficam em
`%APPDATA%\SoloMangas\store.json` e continuam intactos entre versões.

## Como funciona

1. O app lê um manifesto `update.json` (ao lado do exe, numa pasta de rede ou
   numa URL) e compara `version` com a versão instalada.
2. Se houver versão maior, a janela **Atualizações — Solo Mangás** mostra as
   notas e o botão **Baixar e instalar**.
3. O download vai para `%TEMP%\SoloMangas-update` e o SHA-256 é conferido
   contra o manifesto. Hash diferente = atualização cancelada.
4. **Reiniciar e atualizar** abre uma segunda instância do novo exe com
   `--apply-update <alvo> <pid>`; ela espera o app antigo fechar, copia a si
   mesma sobre o executável antigo e reabre o programa.

A verificação ao iniciar roda 1,8 s depois de abrir, é silenciosa quando não
há internet/manifesto e pode ser desligada em **Configurações → Atualizações**.

## O manifesto `update.json`

```json
{
  "version": "2.8.0",
  "url": "Solo-Mangas-2.8.0.exe",
  "sha256": "73989E8557892A8834F2F410B92E359FB386745052B8B46DCC0B6B4C90D03647",
  "size": 83050450,
  "notes": "2.8 — tema Call of Duty, backup restaurável e ordem de capítulos\n\n• ..."
}
```

| Campo | Obrigatório | Conteúdo |
|---|---|---|
| `version` | sim | Versão nova, ex.: `2.8.0` (aceita `v2.8.0`). Comparada por número. |
| `url` | sim | Nome do exe ao lado do manifesto, caminho absoluto ou URL http(s). |
| `sha256` | recomendado | Hash do exe. Se presente, o download só é aceito se bater. |
| `size` | opcional | Tamanho em bytes; usado como referência de progresso. |
| `notes` | opcional | Texto exibido na janela de atualização (aceita `\n`). |

O parser ignora maiúsculas/minúsculas nos nomes dos campos, aceita vírgula
final e comentários, e resolve `url` relativa a partir da pasta/URL do manifesto.

## Onde o app procura o manifesto

**Configurações → Atualizações → Origem das atualizações** aceita três modos:

| Origem configurada | O que o app faz |
|---|---|
| *(vazio)* | usa `update.json` na mesma pasta do executável |
| Pasta local ou de rede (`D:\SoloMangas`, `\\nas\solo`) | usa `<pasta>\update.json` |
| URL (`https://exemplo.com/solo`) | usa `https://exemplo.com/solo/update.json` |
| URL completa (`https://exemplo.com/solo/update.json`) | usa exatamente essa URL |

No modo HTTP o app acrescenta um parâmetro anti-cache (`?t=...`) na leitura do
manifesto, então atualizar o arquivo no servidor já basta.

Nesta release, o manifesto publicado é:

```text
https://github.com/flugel0706/solo-mangas/releases/latest/download/update.json
```

## Publicando uma versão nova

Exemplo para a próxima versão. **Nunca sobrescreva um exe antigo**: cada versão
entra no `dist` com nome próprio.

1. **Subir a versão** em `src/SoloMangas/SoloMangas.csproj`:

   ```xml
   <Version>2.8.0</Version>
   <AssemblyVersion>2.8.0.0</AssemblyVersion>
   <FileVersion>2.8.0.0</FileVersion>
   ```

2. **Publicar single-file**:

   ```powershell
   dotnet publish src/SoloMangas/SoloMangas.csproj -c Release -r win-x64 `
     --self-contained true -p:PublishSingleFile=true -o dist
   ```

3. **Testar antes de distribuir**: copie o exe novo e o `update.json` para uma
   pasta de teste, rode a versão anterior e use **Configurações → Verificar
   atualizações**. O ciclo esperado é: baixar → conferir hash → reiniciar →
   exe trocado → app reaberto.

4. **Publicar a release**:

   ```powershell
   git tag v2.8.0
   git push origin v2.8.0
   ```

   O workflow `.github/workflows/release.yml` publica o executável, o
   `SHA256SUMS.txt` e o `update.json` com a URL absoluta do asset.

## Compatibilidade

- **2.4.0 e anteriores não têm o atualizador.** Quem está nelas precisa baixar
  a **2.5.0 ou mais nova manualmente uma única vez**; da 2.5.0 em diante tudo
  flui pelo app.
- A troca só substitui o `.exe`. O `store.json` do usuário nunca é tocado;
  campos novos de configuração ganham o valor padrão ao abrir.
- A pasta do executável precisa permitir escrita pelo usuário. Em `Arquivos de
  Programas` o app avisa antes de tentar e pede para mover a pasta (ou rode como
  administrador).

## Diagnóstico

- Ligue o log com a variável de ambiente `SOLO_DEBUG=1` antes de abrir o app.
  O arquivo fica em `%TEMP%\solo2.log` e registra as linhas `update: ...`
  (manifesto lido, versão encontrada, falhas).
- Se a cópia do exe falhar, o aplicador grava o motivo em
  `%TEMP%\SoloMangas-update\falha-ao-aplicar.txt`.
- Downloads antigos em `%TEMP%\SoloMangas-update` são limpos automaticamente
  após 7 dias.
- Antivírus pode levar alguns segundos para liberar o exe de ~83 MB recém
  copiado; o aplicador insiste na cópia por até ~48 s e o app reabre sozinho.
