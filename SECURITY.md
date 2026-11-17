# Política de segurança

## Versões suportadas

| Versão | Suporte |
|---|---|
| 2.7.x | sim |
| < 2.7 | não |

## Reportando uma vulnerabilidade

Não abra issue pública para problemas de segurança. Use
**Security → Report a vulnerability** no GitHub (private vulnerability reporting)
ou entre em contato com o mantenedor pelo perfil do repositório.

Inclua:

- descrição do problema e impacto;
- versão do app e do Windows;
- passos para reproduzir ou PoC;
- arquivos/logs relevantes (sem dados pessoais).

Resposta esperada em até 7 dias. Correções de segurança são priorizadas e
divulgadas após a publicação da versão corrigida.

## Escopo

O app executa código local com privilégios do usuário e faz requisições para
sites públicos. Fazem parte do escopo:

- execução de código a partir de respostas das fontes (HTML/JSON malicioso);
- falhas de parsing que causem crash, corrupção do `store.json` ou execução;
- problemas no manifesto de atualização (path traversal, hash ignorado, troca de
  executável indevida);
- vazamento da sessão Mangeek (`session.json`) ou de dados locais;
- elevação de privilégio, persistência ou acesso não autorizado.

## Fora de escopo

- Conteúdo, disponibilidade ou mudanças de layout das fontes.
- Bloqueios de Cloudflare ou regionais.
- Alertas de antivírus por heurística (download de imagens + auto-atualização).
- Uso do app para acessar conteúdo sem autorização.

## Boas práticas para usuários

- Baixe apenas das Releases oficiais e confira o `SHA256SUMS.txt`.
- Use a origem de atualização oficial; manifestos de terceiros podem apontar
  para executáveis arbitrários.
- Mantenha o Windows atualizado.
