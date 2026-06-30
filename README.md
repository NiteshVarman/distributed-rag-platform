<div align="center">

# 🧠 SynapseAI — Distributed RAG Platform

### AI-powered web knowledge ingestion & question-answering, with an in-page browser assistant

[![.NET](https://img.shields.io/badge/.NET_8-512BD4?logo=dotnet&logoColor=white)](#)
[![Azure Functions](https://img.shields.io/badge/Azure_Functions-0062AD?logo=azurefunctions&logoColor=white)](#)
[![Service Bus](https://img.shields.io/badge/Azure_Service_Bus-0089D6?logo=microsoftazure&logoColor=white)](#)
[![MongoDB Atlas](https://img.shields.io/badge/MongoDB_Atlas_Vector_Search-47A248?logo=mongodb&logoColor=white)](#)
[![React](https://img.shields.io/badge/React_18-20232A?logo=react&logoColor=61DAFB)](#)
[![TypeScript](https://img.shields.io/badge/TypeScript-3178C6?logo=typescript&logoColor=white)](#)
[![Gemini](https://img.shields.io/badge/Gemini_Embeddings-8E75B2?logo=googlegemini&logoColor=white)](#)

</div>

---

## ✨ Overview

**SynapseAI** turns any web page into something you can **question, summarize, and understand instantly**. Highlight text on any site to summon an AI orb with one-click tools, or open **Ask AI** to chat with answers grounded in the page's actual content via **Retrieval-Augmented Generation (RAG)**.

Behind the browser extension sits a **cloud-native, event-driven pipeline**: a page is scraped, structure-aware chunked, embedded, and indexed into a vector store through a fault-tolerant, idempotent background workflow — all decoupled by a message queue and orchestrated by Durable Functions.

## 🏗️ Architecture

```
┌──────────────────┐   POST /process-url   ┌─────────────┐   message   ┌──────────────────────┐
│  Browser          │ ────────────────────▶ │   REST API  │ ──────────▶ │  Azure Service Bus    │
│  Extension        │                       │  (.NET 8)   │             │  (process-url-queue)  │
│  (React + WXT)    │ ◀──── /query ──────── │             │             └──────────┬───────────┘
└──────────────────┘    /text-action        └──────┬──────┘                        │ trigger
        ▲                                          │                                ▼
        │ grounded answer + sources                │                  ┌────────────────────────────┐
        │                                          │                  │  Durable Functions          │
        │                            vector + text │                  │  Orchestrator (fan-out)     │
        │                              search (RRF) ▼                  │  Scrape → Chunk → Hash →    │
        │                          ┌───────────────────────┐          │  Embed (batched) → Finalize │
        └───── LLM answer ◀─────── │  MongoDB Atlas         │ ◀─────── │                             │
                                   │  Vector + Text Search  │  upsert  └────────────┬───────────────┘
                                   └───────────────────────┘                       │ embeddings
                                                                  Gemini text-embedding-004 (768-dim)
```

**Query path:** embed question → hybrid search (vector + keyword) → **Reciprocal Rank Fusion** → context → **Groq Llama-3.3-70B** → grounded answer with citations.

## 🚀 Features

- **In-page AI overlay** — select text → radial tool menu (Summarize · Explain · Translate · Rewrite · Expand · Grammar).
- **Ask AI chat, grounded in the page** — auto-indexes the current page, then answers from its content; a badge shows **page-grounded** vs **general**.
- **Event-driven ingestion** — Service Bus + Durable Functions: decoupled, **fault-tolerant**, **idempotent** (safe retries, content-hash dedup, blue/green re-index).
- **Hybrid retrieval** — MongoDB Atlas vector search + full-text search fused with **RRF**.
- **Batched embeddings** — chunks embedded in batches with bulk writes for **~5–10× faster** indexing.
- **Provider-abstracted embeddings** — swap embedding backends behind `IEmbeddingService` via config.

## 🧰 Tech Stack

| Layer | Technology |
|---|---|
| **Backend API** | ASP.NET Core 8 Minimal API (C#) |
| **Background pipeline** | Azure Durable Functions (isolated worker), Azure Service Bus |
| **Vector store** | MongoDB Atlas — Vector Search + Atlas Search |
| **Embeddings** | Google Gemini `text-embedding-004` (768-dim) |
| **LLM** | Groq `llama-3.3-70b-versatile` |
| **Extension** | WXT + React 18 + TypeScript + Framer Motion (Shadow-DOM UI) |
| **Cloud** | Azure App Service, Functions (Consumption), Service Bus |

## 📁 Repository structure

```
.
├── src/
│   ├── DistributedRag.Api/         # REST API (process-url, query, text-action, page-status)
│   ├── DistributedRag.Functions/   # Durable orchestrator + activities + Service Bus trigger
│   ├── DistributedRag.Shared/      # Models, config, services (Mongo, Gemini, Groq, RAG)
│   └── TestMongo/                  # Mongo inspection utility
├── extension/                      # SynapseAI browser extension (WXT + React)
│   ├── entrypoints/                # background worker, content script, popup
│   ├── components/                 # overlay, radial menu, result card, chat panel
│   └── lib/                        # API bridge, tools, types
└── docs/                           # setup guides
```

## ⚡ Getting started

### Prerequisites
.NET 8 SDK · Node 18+ & pnpm · MongoDB Atlas cluster (with `vector_index` & `text_index`) · Azure Service Bus · Gemini API key · Groq API key

### Backend
```bash
# Configure secrets via user-secrets or environment variables, then:
cd src/DistributedRag.Api    && dotnet run     # REST API
cd src/DistributedRag.Functions && func start  # background pipeline
```

### Extension
```bash
cd extension
pnpm install
pnpm dev          # hot-reload dev browser
# or: pnpm build  → load .output/chrome-mv3 (Chrome) / .output/edge-mv3 (Edge)
```

### MongoDB Atlas indexes (on `embeddings`)
```jsonc
// vector_index (Vector Search)
{ "fields": [
  { "type": "vector", "path": "embedding", "numDimensions": 768, "similarity": "cosine" },
  { "type": "filter", "path": "url" }
]}
// text_index (Search)
{ "mappings": { "dynamic": true } }
```

## ☁️ Deployment

Runs fully on Azure: **API** → App Service, **pipeline** → Functions (Consumption), **queue** → Service Bus, **vectors** → MongoDB Atlas, **embeddings/LLM** → Gemini + Groq (free tiers). Secrets are supplied via Azure **app settings** (never committed).

## 🔒 Security notes

- No secrets in the repo — all credentials are injected via environment / Azure app settings.
- Server-side **SSRF protection** on the scraper (blocks private/loopback/metadata targets, per-redirect re-validation, response-size cap).

---

<div align="center">
<sub>Built with .NET, Azure, MongoDB Atlas, and React.</sub>
</div>
