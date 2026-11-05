# Decisões e dúvidas

Registre aqui decisões confirmadas. Não transforme sugestões ou hipóteses em decisões aprovadas.

## Decisões confirmadas

- A Etapa 1 foi concluída e commitada.
- A Etapa 2 foi concluída na branch `etapa-2-navegacao`, com NavMesh, agente `Robo_0_5m`, prefab do robô-cubo de 0,5 m e testes de navegação concluídos.
- O usuário autorizou abrir uma passagem na divisória central para conectar `Entrega_Escritorio`; a abertura de 2 m foi aplicada e validada em Play Mode.
- O fluxo de colaboração pretendido é: o usuário aprova, Claude implementa e ChatGPT revisa. A documentação compartilhada serve para preservar contexto entre as ferramentas.
- O plano de execução deve ser apresentado e os arquivos a alterar devem ser listados antes da execução; etapas não devem avançar automaticamente.
- A Etapa 3 seguirá o fluxo de encomendas descrito no contexto fornecido pelo usuário: criação por X, coleta no ponto central, entrega por prioridade respeitando capacidade, fila para encomendas adicionais e espera quando todos os destinos estiverem cheios.

## Pendências / dúvidas

- Todos os cinco destinos têm caminho completo. O robô percorreu e chegou à sala e ao escritório em Play Mode.
- Não há dúvida pendente registrada para a Etapa 2.
- **Pendente de confirmação após a primeira execução remota:** a implementação inicial usa EditMode no GitHub Actions/GameCI; decidir depois se é suficiente ampliar para integração PlayMode ou build automatizado.
