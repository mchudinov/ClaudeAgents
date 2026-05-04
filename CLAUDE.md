# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repo is

This repo does **not** contain an application — it contains the **infrastructure for doing .NET work with Claude-style agents**. Three artifacts that share one convention (the .NET app being assisted listens on **port 8081**):

1. **`Docker/`** — Dockerfile + compose that build a containerized Claude Code dev environment on top of `mcr.microsoft.com/dotnet/sdk:10.0`. The image bundles Node.js 22, the `@anthropic-ai/claude-code` CLI, `git`, and `ripgrep`, runs as user `dev`, mounts the host repo at `/workspace`, and persists `~/.claude` in the `claude-home` named volume. Port `8081:8081` is published so a .NET app the user is working on inside the container is reachable from the host.
2. **`.claude/agents/dotnet-docker-expert.md`** — a Claude Code subagent definition for the same persona, tools `Read, Write, Edit, Bash, Glob, Grep`. This is the local-machine flavor.
3. **`kagent/kagent-dotnet-docker-expert.yaml`** — a [kagent](https://kagent.dev) Kubernetes deployment of the *same* persona (`Namespace`, `Secret`, `ModelConfig`, `Agent` CRDs, `apiVersion: kagent.dev/v1alpha2`, model `claude-sonnet-4-7`, provider `Anthropic`). This is the in-cluster flavor.

Treat the three as **mirrors of one persona** — when editing the system prompt in one, consider whether the others need the same edit (port number, focus areas, response format). They are intentionally kept in sync.

`MSAgentFramework/` is currently an empty placeholder.

## The port 8081 convention

Every artifact assumes the assisted .NET app binds `http://0.0.0.0:8081` (`ASPNETCORE_URLS=http://+:8081`). Never bind to `localhost` inside a container — it won't be reachable from the host. The Dockerfile `EXPOSE`s 8081, compose publishes `8081:8081`, and the agent prompts instruct sample `curl` commands to target `http://localhost:8081`. If you change the port, change it in **all** of: `Docker/Dockerfile`, `Docker/docker-compose.yml`, `.claude/agents/dotnet-docker-expert.md`, and `kagent/kagent-dotnet-docker-expert.yaml`.

## Common commands

Set `ANTHROPIC_API_KEY` first (see `.env.example`).

```bash
# Build + start the containerized Claude Code dev shell (from Docker/).
cd Docker && docker compose up --build       # interactive: launches `claude` inside the container

# One-off run without compose:
docker build -t claude-dotnet-dev Docker/
docker run -it --rm -p 8081:8081 -v "$PWD:/workspace" \
  -e ANTHROPIC_API_KEY claude-dotnet-dev

# Deploy the kagent Agent CRD (cluster must already have kagent installed).
# The YAML hard-codes namespace `agent` (note: not `kagent`) and a placeholder
# secret value — replace it or apply via:
kubectl create secret generic kagent-anthropic -n agent \
  --from-literal ANTHROPIC_API_KEY=$ANTHROPIC_API_KEY
kubectl apply -f kagent/kagent-dotnet-docker-expert.yaml
```

There are no build/test/lint commands — the repo contains no source code, only configuration. Test changes by actually launching the container or applying the kagent YAML and exercising the agent.

## Known gotchas

- The kagent YAML's `Namespace` is `agent` but the comment block says `kagent` — the manifest itself is authoritative; if you create the secret manually, target `-n agent`.
- `kagent` agent definitions can advise but **cannot edit files** unless an MCP filesystem/shell server is wired up via `tools:` (commented example at the bottom of `kagent-dotnet-docker-expert.yaml`). The local Claude Code subagent flavor *can* edit files because it inherits the host CLI's tools.
- The `.gitignore` is the standard Visual Studio / .NET template — don't be misled into thinking this repo *is* a .NET solution.
