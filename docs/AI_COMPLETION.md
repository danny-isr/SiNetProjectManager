# Generic AI completion + email project recommendation

> **Status:** Active  
> **Date:** 16.09.2026  
> **Scope:** Standalone New System host (`SiNet.App.Wpf`)  
> **Related:** [`SETTINGS.md`](./SETTINGS.md), [`PROJECTS.md`](./PROJECTS.md)

## Authority

`ISystemSettingsQueryService` / `AiSystemSettingsDto` is the only routing source.
The only cloud secret is the existing vault key `SiNet/GeminiApiKey`.
No feature-specific API keys, no second settings stack, no HTTP from WPF.

## Generic port

`IAiCompletionService.CompleteAsync(AiCompletionRequest)`:

- prompt + optional system instructions
- `AiCompletionLevel` (Simple / QualityCheck / Writing / DeepAnalysis)
- `ExpectJson` (provider hint only)
- `CancellationToken` + optional timeout
- text result or a clean `AiCompletionFailureKind`

The port has no Email or Inspection types.

### Provider routing

The requested **level** selects `Ai.Simple` / `QualityCheck` / `Writing` / `DeepAnalysis`.
`Provider` + `Model` come from that row (model falls back to `OllamaModel`).

| Provider | Runtime |
| --- | --- |
| `Ollama` | `POST {OllamaBaseUrl}/api/generate` (same contract as the former inspection client) |
| `Gemini` | `generativelanguage.googleapis.com` + `SiNet/GeminiApiKey` + configured model |
| `OpenAICompatible` | **Not implemented** — returns `ProviderNotConfigured` (no base URL / auth settings exist) |

## Inspection notes

`OllamaInspectionNoteAiReviewer` calls `IAiCompletionService`:
Simple = grammar, QualityCheck = rephrase. No private `HttpClient`.

## Email project recommendation

**Active picker path:** `IEmailProjectSuggestionService` — Facts-only local retrieval. See [`PROJECT_INTELLIGENCE.md`](./PROJECT_INTELLIGENCE.md). No `IAiCompletionService` call when opening «שייך לפרויקט».

`IEmailProjectRecommendationService` (Subject → up to 400 candidates → AI Simple) remains in Application / DI as a **future** port. The filing picker must not call `RecommendAsync`. The generic `IAiCompletionService` port stays for Inspection notes and a later evidence-based enrichment phase.
