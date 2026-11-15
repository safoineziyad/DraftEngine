# Outbound network calls

This inventory covers application code in the current solution. The app has an existing
multi-profile AI configuration (`AiProfile`): the active profile is used for drafting and
analysis, and the profile being edited is used for Settings connection/model tests. Presets
include OpenAI-compatible endpoints, Azure OpenAI, Ollama, LM Studio, and custom endpoints.
The inventory below describes the current implementation; Phase 2 must apply the AI endpoint
policy to every one of these paths.

## AI provider calls

Application AI traffic uses the named `openai` client registered by
`SettingsRegistration.AddAppSettings` through `IHttpClientFactory` (120-second timeout).
`AppSettingsService` supplies a fresh client to `OpenAiContractService`; Settings supplies one
to `AiProviderClient`. The configured profile determines the destination. Requests include
the selected model and the profile's API key, when configured, as a Bearer authorization
header or Azure `api-key` header. Local Ollama/LM Studio profiles may have no key.

| Trigger / call site | Request and data sent | HTTP client |
| --- | --- | --- |
| Clause-generation API (`OpenAiContractService.GenerateClauseBodyAsync`) | If called, sends `POST {profile base URL}/chat/completions`. The prompt contains the clause template and serialized intake field values, plus the system prompt and model settings. No production caller of this method was found in the current app. | `openai` (`IHttpClientFactory`) |
| Optional AI polish (`ContractService.GenerateContractAsync` -> `OpenAiContractService.AssembleViaAiAsync`) | `POST {profile base URL}/chat/completions`. The prompt contains the contract title, serialized field values, and selected clause text after template filling (including custom body overrides), plus the system prompt and model settings. This path does not use the optional party-detail redaction in `ContractService`. | `openai` (`IHttpClientFactory`) |
| AI chat edit (`ContractService.PreviewChatEditAsync` -> `OpenAiContractService.ApplyChatEditAsync`) | `POST {profile base URL}/chat/completions`. The prompt contains the current contract and the user's edit instruction. When "hide party details from AI" is enabled, the document and instruction are redacted before sending and the response is restored locally; otherwise the original text is sent. | `openai` (`IHttpClientFactory`) |
| AI analysis review (`AppAiReviewer.ReviewAsync` -> `OpenAiContractService.CompletePromptAsync`) | `POST {profile base URL}/chat/completions`. The system prompt and user prompt include the document text, contract type, playbook name, and required categories. The reviewer detects and redacts party names before sending, then restores detected tokens in the response locally. | `openai` (`IHttpClientFactory`) |
| Settings: fetch models (`SettingsViewModel.FetchModelsAsync` -> `AiProviderClient`) | `GET {profile base URL}/models`, with the profile's authentication header. No contract or intake text is sent; the response is used to populate model IDs. | `openai` (`IHttpClientFactory`) |
| Settings: test connection (`SettingsViewModel.TestConnectionAsync` -> `AiProviderClient`) | First sends the same authenticated `GET /models`. If the endpoint lacks a model-list endpoint and a model is selected, it may fall back to `POST /chat/completions` with the selected model and a fixed one-token "ping" message. The legacy `OpenAiContractService.TestConnectionDetailedAsync` path also posts a fixed connection-test prompt. No contract or intake text is sent. | `openai` (`IHttpClientFactory`) |

No separate AI translation-suggestion call was found. Localization and translated UI strings
come from bundled resources; they do not call a translation service.

## Updates

The update flow uses the named `updates` `HttpClient`, registered through
`IHttpClientFactory` with a 30-second timeout. It is separate from AI traffic.

| Trigger / call site | Request and data sent | HTTP client |
| --- | --- | --- |
| Update check (`UpdateService.CheckAsync`) | One HTTPS `GET` to the configured stable or beta manifest URL. Its only application-identifying header is `User-Agent: DraftEngine/<version>`; `Accept: application/json` is content negotiation. No query string, contract, party, settings, or other user data is sent. Checks are enabled by default and can be disabled in Settings. | `updates` (`IHttpClientFactory`) |
| Confirmed update download (`UpdateService.DownloadAsync`) | `GET` to the installer URL from the verified manifest, restricted to configured installer hosts. The request asks only for the installer; no contract or profile data is sent. The downloaded bytes are verified before launch. | `updates` (`IHttpClientFactory`) |

## Other network activity

No other application HTTP clients, web requests, sockets, telemetry, or translation-provider
calls were found in the source scan. Installer launch and “open folder” actions start local
processes and are not application network requests.
