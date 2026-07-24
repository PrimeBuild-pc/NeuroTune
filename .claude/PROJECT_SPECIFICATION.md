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

## Core Capabilities

### 1. BYOK Provider Configuration

- Secure local storage for OpenRouter, OpenAI, and Anthropic API keys.
- Provider connection testing and model selection.
- No proprietary subscription or NeuroTune-hosted backend.

### 2. Automatic System Inspection

Collect without manual data entry:

- **Hardware:** CPU, GPU and driver, memory capacity and speed, and physical storage type.
- **Windows:** version/build, active power plan, relevant policy, and gaming settings.
- **Network:** adapters, DNS configuration count, global TCP settings, latency sample, and Nagle overrides.
- **Gaming:** Game Mode, HAGS, Game DVR, and VRR state where detectable.
- **Runtime:** high-memory processes, startup entries, and automatic services.

The user must see the sanitized profile before it is sent to a provider.

### 3. LLM Diagnosis

- Produce a clear diagnosis of the current system.
- Return structured recommendations by action identifier, category, and reason.
- Reject unknown actions, scripts, commands, and malformed responses.

### 4. Optimization Presets

- **Safe / Balanced:** only recommended low-risk actions.
- **Extreme Gaming:** compatible gaming actions plus recommended low-risk actions.
- **Custom:** explicit per-action selection by the user.

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
3. Request an AI diagnosis.
4. Review compatible actions and select a preset or customize the selection.
5. Confirm the exact changes.
6. Let NeuroTune verify backups, apply changes, and record results.
7. Review observational telemetry and restart when required.
8. Restore the operation from history if necessary.

## Product Principles

- **Safety over action count:** five tested actions are better than fifty undocumented tweaks.
- **Local enforcement:** the application, not the model, decides what can execute.
- **No hidden behavior:** profiles, recommendations, changes, and recovery state remain visible.
- **Evidence over marketing:** performance claims require reproducible measurements.
- **No destructive shortcuts:** no generated scripts, remote tweak catalogs, security-control disabling, or irreversible cleanup.
