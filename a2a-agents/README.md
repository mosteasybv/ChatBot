# A2A-agents voor WorkEasy (LlmTornado)

Met deze service maak je **agents die onderling met elkaar praten** via het
[A2A-protocol (Agent2Agent)](https://a2a-protocol.org). Elke agent is gebaseerd op een GPT uit de
WorkEasy-werkruimte en heeft daardoor **dezelfde functionaliteit**: systeemprompt, kennisbanken,
filters, tools, webzoeken, afbeeldingen genereren, code-interpreter en modelparameters. Nieuw is dat
een agent collega-agents kan inschakelen.

De service is geschreven in .NET 8 met [LlmTornado](https://github.com/lofcz/LlmTornado) en
`LlmTornado.A2A` (A2A-protocol, JSON-RPC).

## Hoe werkt het?

```
Gebruiker ──► WorkEasy (Werkruimte → Agents) ──► /api/v1/a2a/*  (rechtencontrole)
                                                     │
                                                     ▼
                                    A2A-agentservice (deze map, LlmTornado)
                                    ├─ agent "intake"   ──A2A──►  agent "jurist"
                                    │        │                         │
                                    │        ▼                         ▼
                                    └─ LlmTornado ──► WorkEasy /api/chat/completions (GPT)
                                                       kennis · tools · webzoeken · filters
```

1. Een agent verwijst naar een **GPT/model uit WorkEasy**. LlmTornado roept daarvoor
   `WorkEasy /api/chat/completions` aan, zodat alles wat je in de werkruimte bij die GPT hebt ingesteld
   automatisch meedoet. Per agent kun je extra instructies, kennis, tools en functies toevoegen.
   Optioneel kan een agent ook rechtstreeks een provider gebruiken (OpenAI, Anthropic, Google, Mistral,
   Groq, DeepSeek, OpenRouter, een eigen OpenAI-compatibele server, …).
2. Bij **"Mag praten met"** kies je welke andere agents een agent mag raadplegen (ook agents op
   externe A2A-servers). Elke collega verschijnt voor het model als tool `ask_<agent>`.
3. Roept het model zo'n tool aan, dan stuurt de service een A2A-bericht (`message/send`) naar de
   collega-agent. Diens antwoord komt terug als toolresultaat en wordt verwerkt in het eindantwoord.
4. Een **lusbeveiliging** voorkomt dat agents eindeloos naar elkaar blijven doorverwijzen: agents die
   al in de keten zitten worden niet meer aangeboden en er is een maximale doorverwijsdiepte
   (standaard 3, per agent instelbaar).
5. In WorkEasy zie je bij het testen per antwoord **welke agent wat aan wie heeft gevraagd**.

## Starten

### Met Docker Compose (samen met WorkEasy)

```bash
export A2A_AGENTS_API_KEY="$(openssl rand -hex 32)"     # gedeelde sleutel WorkEasy ↔ agentservice
export A2A_OPENWEBUI_API_KEY="sk-..."                   # API-sleutel van een WorkEasy-gebruiker
docker compose -f docker-compose.yaml -f a2a-agents/docker-compose.yaml up -d --build
```

De API-sleutel voor `A2A_OPENWEBUI_API_KEY` maak je in WorkEasy via *Instellingen → Account →
API-sleutels* (zet API-sleutels eerst aan via *Beheer → Instellingen → Algemeen*). Gebruik bij voorkeur
een apart serviceaccount: de agents gebruiken de modellen, kennis en tools waar dit account bij kan.

### Lokaal (ontwikkeling)

```bash
cd a2a-agents/src/WorkEasy.A2A
A2A__OpenWebUiBaseUrl=http://localhost:8080 \
A2A__OpenWebUiApiKey=sk-... \
A2A__ApiKey=geheim \
ASPNETCORE_URLS=http://localhost:8090 \
dotnet run
```

En start de WorkEasy-backend met:

```bash
A2A_AGENTS_BASE_URL=http://localhost:8090 A2A_AGENTS_API_KEY=geheim ./backend/dev.sh
```

## Configuratie

| Variabele (service)          | Betekenis                                                                     | Standaard               |
| ---------------------------- | ----------------------------------------------------------------------------- | ----------------------- |
| `A2A__OpenWebUiBaseUrl`      | URL van WorkEasy (zonder `/api`)                                              | `http://localhost:8080` |
| `A2A__OpenWebUiApiKey`       | API-sleutel waarmee agents WorkEasy-GPT's aanroepen                           | –                       |
| `A2A__ApiKey`                | Sleutel die clients (WorkEasy, agents, externe A2A-clients) moeten meesturen  | – (geen controle)       |
| `A2A__PublicBaseUrl`         | Publieke URL, gebruikt in agent cards                                         | afgeleid van verzoek    |
| `A2A__InternalBaseUrl`       | URL waarmee agents elkaar aanroepen                                           | eigen luisteradres      |
| `A2A__DataPath`              | JSON-bestand met agentconfiguraties                                           | `data/agents.json`      |
| `A2A__MaxDelegationDepth`    | Maximale diepte van doorverwijzingen                                          | `3`                     |
| `A2A__MaxToolIterations`     | Maximaal aantal modelrondes per agentbeurt                                    | `8`                     |
| `A2A__RequestTimeoutSeconds` | Time-out per A2A-verzoek                                                      | `300`                   |

| Variabele (WorkEasy-backend) | Betekenis                                      |
| ---------------------------- | ---------------------------------------------- |
| `A2A_AGENTS_BASE_URL`        | URL van deze service, bijv. `http://a2a-agents:8080` |
| `A2A_AGENTS_API_KEY`         | Gelijk aan `A2A__ApiKey` van de service        |
| `A2A_AGENTS_TIMEOUT`         | Time-out in seconden (standaard 300)           |

API-sleutels voor directe providers worden nooit in de configuratie opgeslagen: je geeft per agent de
**naam** van een omgevingsvariabele op (bijv. `OPENAI_API_KEY`) en zet die variabele op de service.

## Agents beheren in WorkEasy

*Werkruimte → Agents* (zichtbaar voor beheerders en gebruikers die werkruimte-modellen mogen beheren):

- **Model** – kies een GPT/model uit WorkEasy of een directe provider.
- **Extra instructies, parameters, kennis, tools en functies** – zoals bij een GPT.
- **Samenwerken (A2A)** – vink aan met welke agents deze agent mag praten, voeg externe A2A-agents toe,
  stel de doorverwijsdiepte in en publiceer vaardigheden in de agent card.
- **Testen** – op de bewerkpagina kun je direct met de agent chatten en zie je het overleg tussen agents.

WorkEasy controleert dat een gebruiker alleen GPT's, kennisbanken en tools koppelt waar hij zelf
toegang toe heeft, en dat alleen de maker of een beheerder een agent mag wijzigen of verwijderen.

### Agents gebruiken in de gewone chat

Voeg de service toe als OpenAI-verbinding (*Beheer → Instellingen → Verbindingen*):
URL `http://a2a-agents:8080/v1`, sleutel = `A2A_AGENTS_API_KEY`. De agents verschijnen dan als
modellen `a2a.<id>` in de modelkiezer.

## Endpoints

| Endpoint                                         | Doel                                              |
| ------------------------------------------------ | ------------------------------------------------- |
| `POST /a2a/{agentId}`                            | A2A JSON-RPC (`message/send`, `tasks/get`, …)     |
| `GET /a2a/{agentId}/.well-known/agent-card.json` | Agent card (publiek, voor ontdekking)             |
| `GET/POST/PUT/DELETE /api/agents[/{id}]`         | Beheer van agents                                 |
| `POST /api/agents/{id}/chat`                     | Agent testen, inclusief verloop tussen agents     |
| `GET /api/traces/{traceId}`                      | Verloop van een gesprek                           |
| `GET /api/network`                               | Wie mag met wie praten                            |
| `GET /v1/models`, `POST /v1/chat/completions`    | OpenAI-compatibel (voor de WorkEasy-chat)         |

Voorbeeld van een A2A-aanroep:

```bash
curl -s http://localhost:8090/a2a/intake \
  -H "Authorization: Bearer $A2A_AGENTS_API_KEY" -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"message/send","params":{"message":{"kind":"message","role":"user","messageId":"1","parts":[{"kind":"text","text":"Mag ik een dakkapel plaatsen?"}]}}}'
```

## Voorbeeldconfiguratie

`examples/agents.example.json` bevat drie agents (intake, jurist, communicatie) die met elkaar
samenwerken. Kopieer het naar `data/agents.json` en pas de model-id's aan naar GPT's uit jouw werkruimte.

## Aandachtspunten

- Gebruikt een GPT *native* function calling met eigen WorkEasy-tools, dan vervangt WorkEasy de
  tool-lijst in het verzoek. Laat voor agents die collega's moeten kunnen raadplegen function calling op
  *standaard* staan (WorkEasy-tools werken dan nog steeds).
- Het overlegverloop wordt alleen in het geheugen bewaard (laatste 200 gesprekken) en verdwijnt bij een
  herstart van de service.
- Antwoorden via `/v1/chat/completions` worden in één keer verstuurd (geen token-streaming), omdat het
  overleg tussen agents eerst afgerond moet zijn.
