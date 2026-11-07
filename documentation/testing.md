# Testes do projeto

## Testes automatizados atuais

Os testes EditMode cobrem o estado e a identificação de uma encomenda, além da capacidade de armazenamento, seleção do primeiro slot livre e rejeição de armazenamento quando o destino está cheio. Os testes PlayMode verificam a detecção de um obstáculo pela suíte de sensores do robô e a limpeza do caminho quando o obstáculo sai. Eles ficam em `Assets/_Project/Tests/EditMode` e `Assets/_Project/Tests/PlayMode`.

## Rodar no Unity Editor

1. Abra **Window > General > Test Runner**.
2. Selecione **EditMode** e clique em **Run All**.
3. Repita em **PlayMode** para executar os testes dos sensores em tempo de execução.

## Esteira no GitHub

O workflow `.github/workflows/unity-tests.yml` roda em pull requests, em pushes para `main` e manualmente. Ele usa Unity `6000.6.7f1` e executa EditMode e PlayMode em jobs separados. Cada job publica seus resultados como artefato da execução.

O workflow desativa explicitamente o check de resultados separado pela Checks API usando `githubToken: ''`. O GameCI v4 preenche `githubToken` com `github.token` por padrão quando o campo é omitido. O status do próprio job do Actions indica sucesso/falha; o relatório detalhado permanece nos artefatos. Isso evita conceder permissões de escrita adicionais ao `GITHUB_TOKEN`.

O runner precisa de uma credencial de ativação nos secrets do repositório GitHub. Configure somente o conjunto correspondente à sua licença:

- Unity Personal gratuita: `UNITY_LICENSE` com o conteúdo integral do arquivo `.ulf`, `UNITY_EMAIL` e `UNITY_PASSWORD` (o GameCI recomenda o arquivo e as credenciais da conta).
- Unity Plus/Pro: `UNITY_EMAIL`, `UNITY_PASSWORD` e `UNITY_SERIAL`.
- Unity Enterprise/Industry: `UNITY_LICENSE` com o conteúdo integral do arquivo `.ulf`.

O workflow encaminha essas quatro variáveis ao runner; os secrets não configurados ficam vazios. Não coloque credenciais ou arquivos de licença no Git. O log da primeira execução falhou antes de rodar os testes porque nenhuma estratégia de ativação estava configurada. Veja o passo a passo oficial do [GameCI para ativação](https://game.ci/docs/github/activation/).

## Limites atuais

- A cobertura PlayMode atual testa a percepção de obstáculos; ainda não há teste automatizado de navegação sobre NavMesh nem do ciclo completo de entrega.
- A execução no GitHub depende da disponibilidade da imagem GameCI para `6000.6.7f1` e da configuração dos secrets de ativação adequados à licença da conta.
- O workflow não constrói um executável; por enquanto, a barreira automatizada são as suítes EditMode e PlayMode.
