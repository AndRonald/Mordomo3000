# Testes do projeto

## Testes automatizados atuais

Os testes EditMode cobrem o estado e a identificação de uma encomenda, além da capacidade de armazenamento, seleção do primeiro slot livre e rejeição de armazenamento quando o destino está cheio. Eles ficam em `Assets/_Project/Tests/EditMode`.

## Rodar no Unity Editor

1. Abra **Window > General > Test Runner**.
2. Selecione **EditMode**.
3. Clique em **Run All**.

## Esteira no GitHub

O workflow `.github/workflows/unity-tests.yml` roda em pull requests, em pushes para `main` e manualmente. Ele usa Unity `6000.6.7f1`, executa os testes EditMode e publica os resultados como artefato da execução.

O workflow não envia um check de resultados separado pela Checks API do GitHub. O status do próprio job do Actions indica sucesso/falha; o relatório detalhado permanece nos artefatos. Isso evita conceder permissões de escrita adicionais ao `GITHUB_TOKEN`.

O runner precisa de uma credencial de ativação nos secrets do repositório GitHub. Configure somente o conjunto correspondente à sua licença:

- Unity Personal gratuita: `UNITY_LICENSE` com o conteúdo integral do arquivo `.ulf`, `UNITY_EMAIL` e `UNITY_PASSWORD` (o GameCI recomenda o arquivo e as credenciais da conta).
- Unity Plus/Pro: `UNITY_EMAIL`, `UNITY_PASSWORD` e `UNITY_SERIAL`.
- Unity Enterprise/Industry: `UNITY_LICENSE` com o conteúdo integral do arquivo `.ulf`.

O workflow encaminha essas quatro variáveis ao runner; os secrets não configurados ficam vazios. Não coloque credenciais ou arquivos de licença no Git. O log da primeira execução falhou antes de rodar os testes porque nenhuma estratégia de ativação estava configurada. Veja o passo a passo oficial do [GameCI para ativação](https://game.ci/docs/github/activation/).

## Limites atuais

- A cobertura inicial testa unidades de lógica; ainda não há testes PlayMode de navegação ou do ciclo completo de entrega.
- A execução no GitHub depende da disponibilidade da imagem GameCI para `6000.6.7f1` e da configuração dos secrets de ativação adequados à licença da conta.
- A suíte remota ainda precisa de uma execução bem-sucedida após remover o envio do check adicional.
- O workflow não constrói um executável; por enquanto, a barreira automatizada é a suíte EditMode.
