# Testes do projeto

## Testes automatizados atuais

Os testes EditMode cobrem o estado e a identificação de uma encomenda, além da capacidade de armazenamento, seleção do primeiro slot livre e rejeição de armazenamento quando o destino está cheio. Eles ficam em `Assets/_Project/Tests/EditMode`.

## Rodar no Unity Editor

1. Abra **Window > General > Test Runner**.
2. Selecione **EditMode**.
3. Clique em **Run All**.

## Esteira no GitHub

O workflow `.github/workflows/unity-tests.yml` roda em pull requests, em pushes para `main` e manualmente. Ele usa Unity `6000.6.7f1`, executa os testes EditMode e publica os resultados como artefato da execução.

Para habilitar a execução, cadastre `UNITY_LICENSE` nos secrets do repositório. Use o arquivo de licença Unity Personal ativado, em seu formato integral. Não adicione a licença ao Git. A documentação do GameCI descreve a ativação e o uso do secret.

## Limites atuais

- A cobertura inicial testa unidades de lógica; ainda não há testes PlayMode de navegação ou do ciclo completo de entrega.
- A execução no GitHub depende da disponibilidade da imagem GameCI para `6000.6.7f1` e da configuração válida do secret da licença.
- O workflow não constrói um executável; por enquanto, a barreira automatizada é a suíte EditMode.
