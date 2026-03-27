# Copilot General Instructions

This repository follows a strict architecture defined in the Technical Architecture Design document.

Copilot's role is to IMPLEMENT the system according to the architecture documents provided.

## Important Principles

1. Do NOT redesign the architecture.
2. Do NOT invent new modules unless explicitly requested.
3. Always follow the defined module boundaries.
4. Business logic must be implemented in Services or Use Cases.
5. ViewModels must remain thin and contain only UI logic.

## Architecture Layers

UI (WPF)
Application (Use Cases / Coordinators)
Domain (Business Model)
Infrastructure (DB / Filesystem / Integrations)

## System Flow

Email → Context → Process → Work → Files → Review → Delivery

Where:

Email = Trigger
Workflow = Process
Task = Unit of Work
ProjectWork = Main Workspace

Copilot should always implement features in a way that supports this flow.