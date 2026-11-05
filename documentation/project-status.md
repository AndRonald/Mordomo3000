# Estado do projeto

## Situação atual

- **Etapa 1 — organização e ambiente:** concluída e commitada, conforme contexto confirmado pelo usuário.
- **Etapa 2 — navegação:** concluída na branch `etapa-2-navegacao`. O pacote AI Navigation, a superfície NavMesh, o agente `Robo_0_5m`, o prefab do robô-cubo de 0,5 m, os destinos e o bake estão configurados.
- **Resultado de navegação:** os cinco destinos (`Ponto_Coleta`, sala, cozinha, quarto e escritório) aceitam caminhos completos. O robô percorreu e chegou à sala e ao escritório em Play Mode.
- **Etapa 3 — sistema de encomendas:** concluída na branch `etapa-3-encomendas`. A tecla X foi validada pelo Input System; o robô cria, coleta, entrega conforme a prioridade e retorna. A fila, as capacidades por cômodo, a mudança para o próximo destino e a espera quando todos estão cheios também foram verificadas em Play Mode.

## Limites para o próximo trabalho

A abertura de 2 m na divisória central foi autorizada durante a Etapa 2 e conectou o escritório à NavMesh. A Etapa 3 foi iniciada com orientação explícita do usuário; não avance para outra etapa automaticamente.

## Atualização

Em 2026-11-05, foram concluídos os scripts e a integração dos prefabs/cena. A compilação terminou sem erros. Em Play Mode, foram validados o evento X via Input System, o ciclo completo de uma encomenda, uma fila de quatro encomendas, sala cheia com desvio para a cozinha e fila aguardando quando todos os destinos estavam cheios. O Play Mode foi encerrado. Próxima etapa: aguardar orientação do usuário.

## Infraestrutura de testes

Em 2026-11-05, foi criada uma suíte inicial de testes EditMode para `PackageItem` e `DeliveryDestination`, com workflow GitHub Actions para Unity `6000.6.7f1`. Os cinco testes passaram no Unity Editor. A execução remota ainda exige configurar o secret `UNITY_LICENSE` e confirmar a compatibilidade da imagem GameCI com essa versão do Editor.
