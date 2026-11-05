# Handoff entre agentes

Use este modelo ao iniciar ou concluir uma tarefa. Acrescente uma entrada datada a cada passagem para manter o histórico.

## Modelo

### Tarefa
- **Data:** AAAA-MM-DD
- **Agente que preparou:**
- **Agente destinatário:**
- **Objetivo / escopo aprovado:**
- **Arquivos previstos:**

### Resultado
- **Arquivos criados ou alterados:**
- **O que foi feito:**
- **Verificações ou testes executados e resultados:**
- **Pendências, dúvidas ou bloqueios:**
- **Próximo passo proposto:**
- **Aprovação necessária antes de continuar:**

---

## Registro — documentação de colaboração

- **Data:** 2026-11-04
- **Agente que preparou:** ChatGPT
- **Agente destinatário:** Claude e ChatGPT
- **Objetivo / escopo aprovado:** estabelecer documentação compartilhada de colaboração sem alterar arquivos Unity, cenas, scripts, prefabs, assets ou configurações MCP.
- **Arquivos criados:** `AGENTS.md`, `documentation/project-status.md`, `documentation/decisions.md` e `documentation/handoff.md`.
- **O que foi feito:** registradas as instruções comuns, o estado confirmado do projeto, decisões e questões pendentes, e este modelo de handoff.
- **Verificações:** confirmada a existência dos quatro arquivos. Nenhum teste de Unity foi executado; nenhuma alteração técnica foi feita.
- **Pendências:** definir e aprovar o plano técnico da Etapa 2 e seus arquivos afetados antes de implementar.
- **Próximo passo proposto:** preparar o plano de navegação NavMesh, robô-cubo de 0,5 m e testes.
- **Aprovação necessária antes de continuar:** aprovação do plano e da lista de arquivos antes de qualquer implementação.

## Registro — retomada da Etapa 2

- **Data:** 2026-11-04
- **Agente que preparou:** Codex
- **Agente destinatário:** usuário / Codex
- **Objetivo / escopo aprovado:** continuar a implementação da Etapa 2 — navegação, na branch `etapa-2-navegacao`, usando o plano e a lista de arquivos aprovados pelo usuário.
- **Arquivos previstos:** `Packages/manifest.json`, `Packages/packages-lock.json`, `ProjectSettings/NavMeshAreas.asset`, `Assets/_Project/Scenes/SampleScene.unity`, `Assets/_Project/Prefabs/Robot_Cubo.prefab` e `.meta`, `Assets/_Project/Scripts/RobotNavigationController.cs` e `.meta`, `Assets/_Project/Materials/Mat_Robo.mat` e `.meta`, dados de bake NavMesh em `Assets/_Project/Scenes/SampleScene/`, `documentation/handoff.md` e `documentation/project-status.md` no encerramento.
- **Arquivos alterados até este ponto:** `Packages/manifest.json`, `Packages/packages-lock.json`, `Assets/_Project/Scripts/RobotNavigationController.cs` e `.meta`, `Assets/_Project/Materials/Mat_Robo.mat` e `.meta`.
- **O que foi feito:** confirmado que a branch local `etapa-2-navegacao` está ativa e começa no mesmo commit da `main`/Etapa 1. Adicionada a dependência `com.unity.ai.navigation` versão `2.0.16`; a Unity gerou a entrada correspondente no lockfile e importou o pacote. Criado o controlador de navegação com `MoveTo(Vector3)`, checagem de caminho completo, evento `Arrived`, logs e teclas 1–5 via Input System, com alternância dos atalhos. Criado `Mat_Robo` com cor azul ciano para distinguir o robô.
- **Verificações ou testes executados e resultados:** leitura do `AGENTS.md` e da documentação compartilhada; inspeção da branch, estado Git, manifesto, lockfile, cena e lista de destinos. Confirmado no Editor.log que houve recompilação bem-sucedida das assemblies depois da importação do pacote/script; não encontrei erros de compilação C# no log consultado. Confirmado que a cena contém os marcadores `Entrega_Sala`, `Entrega_Cozinha`, `Entrega_Quarto` e `Entrega_Escritorio`, além de `Ponto_Aproximacao`; nenhum objeto chamado `Ponto_Coleta` foi localizado. Nenhum teste de navegação ou Play Mode foi executado.
- **Pendências, dúvidas ou bloqueios:** esta sessão não expõe ferramentas Unity MCP. A janela Unity está aberta, mas a captura de UI retornou a janela do Codex, então não foi seguro operar o Editor por UI. Falta configurar o tipo de agente e o NavMesh Surface, criar prefab/instância na cena, decidir o destino de teste `Ponto_Coleta` (o marcador não existe com esse nome), fazer bake e executar validação em Play Mode. `documentation/project-status.md` será atualizado apenas ao final, como autorizado.
- **Próximo passo proposto:** continuar quando as ferramentas Unity MCP estiverem disponíveis nesta sessão, para configurar o tipo de agente, a superfície, o prefab, o bake e os testes no Editor.
- **Aprovação necessária antes de continuar:** nenhuma para os itens já aprovados; pedir autorização antes de alterar arquivos fora da lista ou redefinir o que representa `Ponto_Coleta`.

## Registro — configuração e validação inicial da navegação

- **Data:** 2026-11-04
- **Agente que preparou:** Codex
- **Agente destinatário:** usuário / próximo agente
- **Objetivo / escopo aprovado:** continuar a Etapa 2 já aprovada, configurar NavMesh e o robô-cubo de 0,5 m, e validar os destinos existentes.
- **Arquivos previstos:** `ProjectSettings/NavMeshAreas.asset`, `Assets/_Project/Scenes/SampleScene.unity`, `Assets/_Project/Prefabs/Robot_Cubo.prefab` e `.meta`, dados de bake em `Assets/_Project/Scenes/SampleScene/`, `documentation/handoff.md` e `documentation/project-status.md`.
- **Arquivos alterados/criados nesta continuação:** `ProjectSettings/NavMeshAreas.asset`, `Assets/_Project/Scenes/SampleScene.unity`, `Assets/_Project/Scenes/SampleScene/NavMesh-Casa.asset` e `.meta`, `Assets/_Project/Prefabs/Robot_Cubo.prefab` e `.meta`, `documentation/handoff.md`, `documentation/project-status.md` e `documentation/decisions.md`. O Unity criou também `Assets/_Project/Scenes/SampleScene.meta`. Uma captura de inspeção foi gravada em `Assets/Screenshots/screenshot-20261104-214430.png`, com `Assets/Screenshots.meta`; ela foi mantida no workspace.
- **O que foi feito:** adicionado `NavMeshSurface` à raiz `Casa`, configurado para coletar os filhos usando colisores; configurado o único tipo de agente como `Robo_0_5m` (raio 0,25 m, altura 0,5 m, climb 0,1 m); criado e vinculado o prefab com corpo visual cúbico de 0,5 m, `NavMeshAgent` e `RobotNavigationController`; referenciados os cinco destinos que existem na cena. A busca confirmou que `Casa/Central_Recebimento/Ponto_Coleta` existe, corrigindo a observação do handoff anterior. Gerado `NavMesh-Casa.asset` pela janela/bake oficial do pacote. Reposicionada a instância na cena para `(0, 0, -2.5)`, em área livre do piso, pois a posição inicial anterior coincidia com uma divisória.
- **Verificações ou testes executados e resultados:** Editor Unity 6000.6.7f1 conectado; compilação terminou sem erros reportados no console; inspeção do prefab confirmou a referência a `Mat_Robo.mat`, escala visual de 0,5 m e parâmetros do agente; bake confirmado pelo asset e pela referência no `NavMeshSurface`. A partir do novo ponto inicial, `CalculatePath` obteve `PathComplete` para `Ponto_Coleta`, `Entrega_Sala`, `Entrega_Cozinha` e `Entrega_Quarto`; `Entrega_Escritorio` retorna `PathPartial`. Em Play Mode, o robô se deslocou até `Entrega_Sala`, chegou ao destino e emitiu o log correspondente. Play Mode foi encerrado.
- **Pendências, dúvidas ou bloqueios:** não alterei as paredes/divisórias. É necessário decidir se o layout deve ser modificado para dar acesso ao escritório ou se esse destino deve ser tratado de outra forma. A captura de inspeção permanece como artefato extra em `Assets/Screenshots/`.
- **Próximo passo proposto:** após a decisão sobre o escritório, ajustar apenas o escopo autorizado, refazer o bake e validar o movimento até os destinos acessíveis; então atualizar o status da Etapa 2.
- **Aprovação necessária antes de continuar:** autorização explícita antes de alterar a geometria existente da casa ou de remover a captura de inspeção.

## Registro — passagem para o escritório e validação final da Etapa 2

- **Data:** 2026-11-04
- **Agente que preparou:** Codex
- **Agente destinatário:** usuário / próximo agente
- **Objetivo / escopo aprovado:** abrir uma passagem para `Entrega_Escritorio`, refazer o bake e validar a navegação, após autorização explícita do usuário.
- **Arquivos previstos:** `Assets/_Project/Scenes/SampleScene.unity`, `Assets/_Project/Scenes/SampleScene/NavMesh-Casa.asset` e documentação de status/handoff.
- **Arquivos alterados:** `Assets/_Project/Scenes/SampleScene.unity`, `Assets/_Project/Scenes/SampleScene/NavMesh-Casa.asset`, `Assets/_Project/Scenes/SampleScene/NavMesh-Casa.asset.meta`, `documentation/handoff.md`, `documentation/project-status.md` e `documentation/decisions.md`. O GUID do bake permaneceu estável após a regeneração.
- **O que foi feito:** ampliada a abertura central entre `Divisoria_Meio` e `Divisoria_Meio_Dir` de 1 m para 2 m, preservando o restante da geometria; refeito o bake do `NavMeshSurface`; mantida a posição inicial do robô em `(0, 0, -2.5)`.
- **Verificações ou testes executados e resultados:** `RobotNavigationController.MoveTo` aceitou `Entrega_Escritorio` com `PathComplete`; em Play Mode o robô se deslocou até `(0, 0.03, 4.4)`, chegou ao destino e emitiu o log correspondente. A partir do escritório, `CalculatePath` retornou `PathComplete` para `Ponto_Coleta`, sala, cozinha, quarto e escritório. O bake está presente em `NavMesh-Casa.asset`. Play Mode foi encerrado.
- **Pendências, dúvidas ou bloqueios:** nenhuma pendência funcional da Etapa 2. A captura de inspeção `Assets/Screenshots/screenshot-20261104-214430.png` continua no workspace como artefato gerado durante a verificação.
- **Próximo passo proposto:** aguardar orientação do usuário para iniciar a etapa seguinte.
- **Aprovação necessária antes de continuar:** nenhuma para a Etapa 2 concluída; não iniciar uma etapa nova sem orientação.

## Diagnóstico — conexão Unity MCP no Codex

- **Data:** 2026-11-04
- **O que foi verificado:** `codex mcp list` lista `unityMCP` como habilitado, com `uvx --offline --from mcpforunityserver==10.3.0 mcp-for-unity --transport stdio`. O pacote MCP for Unity versão `10.3.0` está instalado no projeto.
- **Resultado:** a configuração existe no Codex, mas o inventário de ferramentas desta conversa não contém `mcp__unityMCP__*` e os recursos `mcpforunity://`. A configuração atual não injeta novas ferramentas numa conversa já em execução.
- **Alterações nesta verificação:** nenhuma configuração de MCP foi alterada; foi somente atualizado este registro.
- **Próximo passo proposto:** abrir uma nova conversa Codex local no projeto RoboMordomo para que ela carregue a configuração `unityMCP` no início; verificar nessa conversa se os recursos e ferramentas Unity MCP aparecem antes de continuar a Etapa 2.

### Tentativa em nova conversa Codex

- **Data:** 2026-11-04
- **Resultado da verificação:** esta conversa também não recebeu ferramentas `mcp__unityMCP__*` nem recursos `mcpforunity://`; os recursos MCP listados só incluem os servidores internos do Codex.
- **Ações:** nenhuma operação no Editor Unity foi feita; não houve alteração na cena, no prefab, nas configurações NavMesh ou nos testes.
- **Próximo passo proposto:** reiniciar/atualizar o Codex desktop para que recarregue a configuração MCP já habilitada, e só então conferir ferramentas/recursos novamente e retomar a implementação.

## Registro — início da Etapa 3: sistema de encomendas

- **Data:** 2026-11-05
- **Agente que preparou:** Codex
- **Agente destinatário:** usuário / próximo agente
- **Objetivo / escopo aprovado:** reaproveitar o contexto enviado pelo usuário e implementar o sistema de encomendas: criação por X, fila na central, coleta pelo robô, escolha de destino por prioridade e capacidade, entrega, retorno à espera e retenção da fila quando todos os destinos estiverem cheios.
- **Arquivos previstos:** `Assets/_Project/Scripts/PackageItem.cs`, `Assets/_Project/Scripts/DeliveryDestination.cs`, `Assets/_Project/Scripts/PackageDeliveryManager.cs`, prefab de encomenda, pontos de spawn/carga, cena `SampleScene.unity`, prefabs da central e do robô, além deste handoff e do status do projeto.
- **Arquivos criados ou alterados:** os três scripts listados foram criados; `PackageDeliveryManager.cs` foi ajustado para usar as APIs atuais de busca de objetos da Unity. `documentation/project-status.md`, `documentation/decisions.md` e este arquivo foram atualizados.
- **O que foi feito:** criada a lógica base da encomenda com estados aguardando/coletada/entregue, a capacidade por destino baseada nos slots configurados, e um gerenciador com fila, tecla X, coleta, seleção por prioridade, entrega e retorno. A prioridade automática usa os nomes `Entrega_Sala`, `Entrega_Cozinha`, `Entrega_Quarto` e `Entrega_Escritorio`.
- **Verificações ou testes executados e resultados:** a compilação observada no Editor não apresentou erros; o Console mostrou inicialmente dois avisos de APIs obsoletas, que foram corrigidos. Depois disso, a conexão Unity MCP caiu antes de confirmar a nova compilação. Nenhum teste de Play Mode foi executado.
- **Pendências, dúvidas ou bloqueios:** falta concluir a integração de GameObjects, âncoras, slots, prefab visual e referências na cena. A instância `RoboMordomo` ficou indisponível no Unity MCP durante a edição; não foi possível salvar e validar a integração. Os três scripts novos ainda precisam dos `.meta` gerados pelo Unity.
- **Próximo passo proposto:** restabelecer a conexão Unity MCP e continuar pela integração dos componentes nos prefabs e na cena; então conferir Console e testar criação, coleta, prioridades, capacidade, fila e retorno em Play Mode.
- **Aprovação necessária antes de continuar:** nenhuma para o escopo já autorizado da Etapa 3. Não iniciar outra etapa ao concluir esta.

### Continuação — integração inicial na Unity

- **Data:** 2026-11-05
- **Agente que preparou:** Codex
- **Agente destinatário:** usuário / próximo agente
- **Objetivo / escopo aprovado:** continuar a Etapa 3 na branch `etapa-3-encomendas`.
- **Arquivos criados ou alterados:** `Assets/_Project/Prefabs/Package_Cubo.prefab` e `.meta`; `Assets/_Project/Prefabs/Central_Recebimento.prefab`; `Assets/_Project/Prefabs/Robot_Cubo.prefab`; `Assets/_Project/Scenes/SampleScene.unity`; `Assets/_Project/Scripts/PackageDeliveryManager.cs`; `Assets/_Project/Scripts/DeliveryDestination.cs`; `documentation/project-status.md`; `documentation/handoff.md`.
- **O que foi feito:** criado o prefab visual `Package_Cubo` com `PackageItem` e sem collider; adicionados `Ancora_Spawn` e `Ponto_Carga` aos prefabs da central e do robô; adicionados `DeliveryDestination` aos quatro destinos, configurados com `Ponto_Aproximacao` e quatro slots cada; adicionado `PackageDeliveryManager` à raiz `Casa` e referenciado o prefab de encomenda. A cena foi salva. Corrigida a escala local da encomenda para que permaneça no tamanho do prefab ao ser armazenada, e o espaçamento inicial da fila foi ajustado para evitar sobreposição.
- **Verificações ou testes executados e resultados:** a compilação terminou sem erros. Confirmados quatro slots para cada destino e referências runtime válidas no gerenciador. Em Play Mode, a encomenda 1 foi coletada, entregue à sala (`1/4`) e o robô retornou. Com encomendas em fila, as encomendas 2–4 completaram a sala (`4/4`), e a encomenda 5 foi desviada à cozinha (`1/4`), confirmando prioridade e capacidade. Ao preencher temporariamente todos os destinos, a encomenda 6 permaneceu na fila e o Console registrou o aviso de destinos cheios. Por fim, foi enfileirado um evento de teclado X pelo Input System e executado o método de atualização do gerenciador; a criação, entrega à sala e retorno foram confirmados nos logs. Play Mode foi encerrado; objetos de teste não foram salvos na cena.
- **Pendências, dúvidas ou bloqueios:** nenhuma pendência funcional conhecida para a Etapa 3. O espaçamento serializado da fila foi ajustado para `0.35` e a cena salva. Não foi criado `Mat_Encomenda`; a encomenda usa o material padrão do cubo.
- **Próximo passo proposto:** aguardar orientação do usuário para iniciar a próxima etapa.
- **Aprovação necessária antes de continuar:** pedir orientação antes de iniciar outra etapa.
- **Aprovação necessária antes de continuar:** nenhuma para continuar a validação da Etapa 3 já autorizada.

## Registro — esteira inicial de testes

- **Data:** 2026-11-05
- **Agente que preparou:** Codex
- **Agente destinatário:** usuário / próximo agente
- **Objetivo / escopo aprovado:** criar uma base de testes automatizados e uma esteira para reduzir regressões nas próximas etapas.
- **Arquivos criados ou alterados:** `Packages/manifest.json`; `Assets/_Project/Scripts/RoboMordomo.Runtime.asmdef`; `Assets/_Project/Tests/EditMode/RoboMordomo.EditModeTests.asmdef`; `Assets/_Project/Tests/EditMode/DeliveryDestinationTests.cs`; `Assets/_Project/Tests/EditMode/PackageItemTests.cs`; `.github/workflows/unity-tests.yml`; `documentation/testing.md`; `documentation/project-status.md`; `documentation/decisions.md`; `documentation/handoff.md`.
- **O que foi feito:** declarada a dependência Unity Test Framework; criada uma assembly de runtime para permitir referência pelos testes; adicionados cinco casos EditMode para estado/ID de encomenda e capacidade/armazenamento por slot; criado workflow GameCI que executa EditMode no Unity `6000.6.7f1` em PR, push na `main` ou disparo manual; documentado o secret `UNITY_LICENSE` e o procedimento local.
- **Verificações ou testes executados e resultados:** Unity Editor `6000.6.7f1` compilou a assembly e executou a suíte EditMode: 5 passaram, 0 falharam, 0 ignorados. O Package Manager resolveu `com.unity.test-framework` `1.8.0` e atualizou `Packages/packages-lock.json`. Consultadas as instruções oficiais GameCI sobre execução do runner e uso da licença.
- **Pendências, dúvidas ou bloqueios:** configurar `UNITY_LICENSE` nos secrets do GitHub e observar uma execução real para confirmar disponibilidade da imagem GameCI para a versão `6000.6.7f1`. Não há testes PlayMode nesta primeira cobertura. Uma tentativa pela linha de comando local não gerou relatório; os resultados reportados são exclusivamente da execução pelo Unity MCP/Editor.
- **Próximo passo proposto:** abrir o Test Runner no Unity e executar EditMode; depois enviar as alterações ao GitHub quando o usuário decidir, configurar o secret e confirmar o status do workflow.
- **Aprovação necessária antes de continuar:** nenhuma para validar os testes locais; não fazer commit ou push sem solicitação explícita.

## Registro — configuração de ativação do GameCI

- **Data:** 2026-11-05
- **Agente que preparou:** Codex
- **Agente destinatário:** usuário / próximo agente
- **Objetivo / escopo aprovado:** diagnosticar a primeira execução do workflow e preparar a esteira para receber a autenticação Unity necessária.
- **Arquivos alterados:** `.github/workflows/unity-tests.yml`; `documentation/testing.md`; `documentation/project-status.md`; `documentation/handoff.md`.
- **O que foi feito:** o log mostrou que a imagem `unityci/editor:ubuntu-6000.6.7f1-linux-il2cpp-3` foi baixada, mas o GameCI não encontrou estratégia de ativação. O workflow agora encaminha `UNITY_EMAIL`, `UNITY_PASSWORD`, `UNITY_SERIAL` e `UNITY_LICENSE`. A documentação explica quais secrets configurar para Personal, Plus/Pro ou Enterprise/Industry.
- **Verificações ou testes executados e resultados:** analisado o log fornecido pelo usuário. Ele confirma que a execução terminou com erro de estratégia de licença antes da etapa de testes; portanto, nenhum teste remoto rodou. Os cinco testes continuam validados localmente no Unity Editor, conforme registro anterior. A correção do workflow ainda não foi executada no GitHub.
- **Pendências, dúvidas ou bloqueios:** o usuário precisa cadastrar no repositório os secrets correspondentes ao tipo de licença Unity usado. Nenhuma credencial foi solicitada ou armazenada nesta sessão.
- **Próximo passo proposto:** conferir o tipo de licença Unity, criar os secrets necessários nas configurações do repositório, commitar e enviar esta correção, e observar a próxima execução do workflow.
- **Aprovação necessária antes de continuar:** não fazer commit ou push sem solicitação explícita.
