# Deployment Principles

- **Decision date / Updated date:** 26.05.2026
- **Status:** Active (skeleton) — source of truth for office installation and services.
- **Scope:** Office installation, services, prerequisites, shared folders, permissions, ACC service, Google connection.

## Purpose
Define how the application is installed and operated in a customer office.

## Source of truth
- `SiOffice.AccService\README.md` and `SiOffice.AccService\DEPLOYMENT.md` for ACC service deployment specifics.
- This document for cross-cutting deployment principles.

## Core principles
1. `SiOffice.AccService` is a privileged Windows Service. It requires Account Admin / Project Admin / Folder `CONTROL` rights on the ACC side and is reached over HTTPS.
2. Remote WPF clients call the service when `AccService:BaseUrl` is configured. In that mode, local `AccUserBootstrapService.ProvisionUsersAsync` is skipped on startup.
3. The Google connection (Gmail / Drive) is provided through `SiOffice.GoogleConnector` / `GoogleService`. Outbound Gmail API logic lives in the connector / service layer, not in configuration files.
4. Office Inbox ensure is exposed through the service endpoint and is the central remote provisioning path.
5. Default Office Management project ID is **136** (not 126), used for project-independent workflows.
6. Authentication / token paths (`TokenProvider`, `Bim360Service`, service architecture) are not changed without explicit approval.
7. Prerequisites, shared folders, and Windows group permissions are office-specific; they are recorded per deployment and must not be hard-coded in app code.

## What we do not do now
- Do not add startup-time browser authorization or unrelated ACC bootstrap on remote clients in service mode.
- Do not change service architecture, `TokenProvider`, `Bim360Service`, or authentication flows as part of deployment documentation.
- Do not store secrets in source documents.

## Dropped / cancelled / postponed
- Running privileged ACC orchestration locally on remote clients when service mode is configured — dropped.
- Full step-by-step office install runbook in this document — postponed (lives in service-specific DEPLOYMENT docs).

## Relevant terms / search terms
SiOffice.AccService, SiOffice.AutodeskConnector, SiOffice.GoogleConnector, AccService:BaseUrl, AccUserBootstrapService, TokenProvider, Bim360Service, two-legged, three-legged, Office Inbox ensure, default project 136.
