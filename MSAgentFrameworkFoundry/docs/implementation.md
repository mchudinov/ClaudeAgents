# Foundry Agents — Implementation Roadmap

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement each plan task-by-task. Plans use checkbox (`- [ ]`) syntax for tracking.

**Source spec:** [`docs/superpowers/specs/2026-05-13-foundry-agents-design.md`](superpowers/specs/2026-05-13-foundry-agents-design.md)

**Date:** 2026-05-13

This roadmap splits the spec into three sequential plans. Each plan ships working, testable software on its own.

---

## Execution order

| # | Plan | What ships | File |
|---|------|------------|------|
| 1 | **Libraries** | `Foundry.Agents.Contracts`, `Foundry.Agents.Memory`, `Foundry.Agents.ServiceDefaults`, `Foundry.Agents.TestUtils` and their unit tests. Cosmos thread store passes round-trip, ETag, TTL, and compact tests against a Testcontainers Cosmos linux emulator. No services yet. | [`implementation-plan-1-libraries.md`](implementation-plan-1-libraries.md) |
| 2 | **Services** | `Foundry.Agents.Developer`, `Foundry.Agents.Reviewer`, `Foundry.Agents.AppHost` (Aspire dev-only) and their tests. End-to-end Developer → Reviewer → Developer happy path runs locally via `dotnet run --project src/Foundry.Agents.AppHost` against the Cosmos emulator and the Foundry-hosted Claude Opus 4.7 endpoint. | [`implementation-plan-2-services.md`](implementation-plan-2-services.md) |
| 3 | **Infrastructure & CI/CD** | `Dockerfile`s for both services, `infra/main.bicep` and modules (Log Analytics, ACR, Cosmos, Key Vault, Foundry, Container Apps), `.github/workflows/pr.yml` and `deploy.yml`. `azd up` (or `az deployment sub create`) provisions everything; `git push origin main` deploys. | [`implementation-plan-3-infra.md`](implementation-plan-3-infra.md) |

Plan 2 depends on Plan 1's libraries (project references). Plan 3 depends on Plan 2's projects (Dockerfiles target the published binaries). Do not start Plan 2 until Plan 1's tests are green; do not start Plan 3 until Plan 2 boots locally.

## Locked-in package pins (from brainstorming)

Per the matched-versions decision in the spec, the following packages are pinned across every project that consumes them. These are set centrally via `Directory.Packages.props` (Central Package Management) in Plan 1 T1.

| Package | Version | Rationale |
|---|---|---|
| `Microsoft.Agents.AI` | `1.0.0-rc1` | Matches the sibling Dapr project; avoids the MEAI 10.4+ `HostedMcpServerTool.AuthorizationToken` regression that broke Anthropic.SDK 5.10.0. |
| `Microsoft.Extensions.AI` | `10.3.0` | Same constraint. |
| `Microsoft.Extensions.AI.Abstractions` | `10.3.0` | Same constraint. |
| `Microsoft.Extensions.AI.AzureAIInference` | `10.3.0-preview` | The `IChatClient` surface for Foundry's Azure AI Inference endpoint per the user's decision. |
| `Azure.AI.Inference` | `1.0.0-beta.5` | Underlying transport for `AzureAIInference`. |
| `ModelContextProtocol` | `1.1.0` | Matched pair with MEAI 10.3.0. |
| `ModelContextProtocol.AspNetCore` | `1.1.0` | Same. |

All other packages float to latest stable at task time unless a task says otherwise.

## Personas

Both persona files (`personas/developer.md`, `personas/reviewer.md`) are checked in at the **repo root** (i.e. `MSAgentFrameworkFoundry/personas/`), not inside a project. They are embedded into each agent project via `<EmbeddedResource Include="..\..\personas\developer.md" LogicalName="persona.md" />` so the loader is identical on both sides. Their content is drafted in Plan 2 T1 and T14; the spec's two non-negotiable persona constraints (§5.2) are unit-tested as invariants.

## What's deferred (out of scope)

- PR merging, semantic memory, multi-region HA, webhooks, async completion, custom Foundry model deployment work — all listed as non-goals in §9 of the spec.
- Dev/staging ACA split — single prod env per D-13.
- Alerting/dashboards — observability ships logs + traces + metrics only.
