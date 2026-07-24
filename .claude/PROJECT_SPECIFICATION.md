# Project Specification: AI-Powered System Optimizer

## Overview

NeuroTune is an intelligent Windows desktop application that analyzes, diagnoses, and safely tunes operating-system performance according to the actual hardware, installed software, and user goal, such as gaming performance, productivity, or network responsiveness.

Unlike generic cleanup tools based on fixed recipes, NeuroTune uses a user-selected language model through a BYOK provider. It collects the system profile automatically and asks the model for contextual recommendations. The model may only select actions from a local, compiled allowlist; it cannot generate executable changes.

## Project Goal

The goal is to make advanced Windows administration understandable to non-technical users while preserving transparency, validation, and recovery.

NeuroTune addresses:

- **Technical complexity:** users should not need to edit the Registry or run scripts manually.
- **Generic advice:** recommendations should consider the real CPU, GPU, memory, storage, Windows build, and configuration.
- **Safety:** every change must be inspectable, compatible, verified, and reversible.
- **Time:** profiling, diagnosis, review, execution, and recovery should form one guided workflow.

## Application Architecture

- Tauri 2 native desktop shell using a React and TypeScript web frontend.
- System-synchronized light/dark appearance with persistent manual overrides.
- Semantic design tokens and automated WCAG AA contrast checks.
- Local .NET agent for Windows-only profiling, DPAPI, backup, execution, and rollback.

## Core Capabilities

### 1. BYOK Provider Configuration

- Secure local storage for OpenRouter, OpenAI, Anthropic, DeepSeek, and custom-provider API keys.
- Official browser authorization where a provider exposes a supported third-party flow.
- Custom OpenAI-compatible and Anthropic-compatible HTTPS endpoints.
- Loopback-only HTTP support for Ollama, LM Studio, vLLM, and other local model servers.
- Provider connection testing and dynamic model selection.
- No proprietary subscription or NeuroTune-hosted backend.

### 2. Automatic System Inspection

Collect without manual data entry:

- **Hardware and firmware:** CPU/CPUID, GPU and driver, motherboard, BIOS, SMBIOS DIMMs, configured memory speed/voltage where exposed, storage reliability, displays, thermal zones, form factor, and security virtualization.
- **Windows:** version/build, power, boot configuration, device errors, relevant policies, gaming state, and a vetted inventory of 80+ performance-related Registry values.
- **Network:** adapters, advanced properties, components/filter drivers, proxy, global TCP state, latency sample, and per-interface overrides.
- **Software:** installed applications, relevant signed drivers, tuning/overlay/VPN/virtualization signals, startup entries, processes, and services.
- **Conflict graph:** deterministic rules identify exact evidence pairs, relationship type, objective impact, confidence, and why a combination may be counterproductive.

The user must see the sanitized evidence bundle before it is sent to a provider.

### 3. LLM Diagnosis

- Ask for the user's games or workloads and priority: balanced, frame rate, system latency, network latency, or efficiency.
- Produce a clear diagnosis whose findings cite exact observed fields and values.
- Synthesize the deterministic local conflict graph instead of inventing unsupported relationships.
- Return structured recommendations by action identifier and evidence-backed reason.
- Reject unknown actions, scripts, commands, and malformed responses.

### 4. Dynamic Plan

- Let the user switch between AI-recommended, conflict-related, and all supported reversible actions.
- Offer **Select all supported**, **Select safe only**, explicit per-action selection, and a printable report.
- Risk changes warnings and confirmation strength, not whether a supported action is visible.
- Keep execution behind an explicit consent question and local confirmation.

### 5. Compatibility and Transparency

- Inspect current state before an action can be selected.
- Disable unsupported or already-configured actions.
- Show description, current value, compatibility, reason, risk, and restart requirement.

### 6. Backup and Recovery

- Require and verify a new System Restore point before any system modification.
- Export affected Registry keys and persist original values.
- Journal each action before attempting it.
- Verify each applied or restored value.
- Roll back automatically on failure and expose manual recovery from operation history.
- Warn at startup when an interrupted operation needs attention.

### 7. Measurement

- Capture factual before/after observations such as CPU load, memory use, process count, latency, and active power plan.
- Never present an immediate observation or synthetic score as proof of a performance improvement.

## User Journey

1. Start NeuroTune and configure a BYOK provider.
2. Run a local scan and review the sanitized profile.
3. Enter relevant games or workloads, choose a priority, and request an AI diagnosis.
4. Review the dynamic evidence-backed plan, print it, or select all/safe/manual fixes.
5. Confirm the exact changes.
6. Let NeuroTune verify backups, apply changes, and record results.
7. Review observational telemetry and restart when required.
8. Restore the operation from history if necessary.

## Product Principles

- **Safety over action count:** a few tested actions are better than fifty undocumented tweaks.
- **Local enforcement:** the application, not the model, decides what can execute.
- **No hidden behavior:** profiles, recommendations, changes, and recovery state remain visible.
- **Evidence over marketing:** performance claims require reproducible measurements.
- **No destructive shortcuts:** no generated scripts, remote tweak catalogs, security-control disabling, or irreversible cleanup.
