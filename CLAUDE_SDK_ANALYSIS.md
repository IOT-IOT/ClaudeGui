# Analisi: Claude Code SDK vs Headless Mode

**Data:** 2025-10-29
**Progetto:** ClaudeCodeGUI
**Autore:** Claude (Anthropic)

---

## Executive Summary

Abbiamo investigato l'esistenza del Claude Agent SDK e valutato se rappresentasse un errore architetturale nella nostra implementazione basata su headless mode.

**Conclusione:** ✅ **L'approccio headless mode è corretto e appropriato per progetti .NET/WinForms.**

**Motivazione principale:** Non esiste un SDK .NET/C# ufficiale per Claude Agent SDK. Le alternative (TypeScript/Python) richiederebbero un bridge architetturale complesso senza benefici tangibili per una GUI desktop standalone.

---

## 1. Claude Agent SDK: Stato Attuale

### 1.1 Disponibilità

**SDK Ufficiali Esistenti:**
- ✅ **TypeScript/JavaScript**: `@anthropic-ai/claude-agent-sdk` (npm)
  - Versione corrente: 0.1.28
  - Pubblicato: 2 giorni fa (molto recente)
  - Repository: https://github.com/anthropics/claude-agent-sdk-typescript

- ✅ **Python**: `claude-agent-sdk` (pip)
  - Repository: https://github.com/anthropics/claude-agent-sdk-python

**SDK .NET/C#:**
- ❌ **NON ESISTE** un SDK ufficiale per .NET
- Esistono solo SDK per API Messages base (`Anthropic.SDK`, `Anthropic` su NuGet)
- Nessun supporto per feature agentic/multi-turn del Claude Agent SDK

### 1.2 Feature dell'Agent SDK

**Funzionalità Avanzate:**

1. **Custom Tools Inline**
   ```typescript
   const calculator = {
     name: 'calculator',
     description: 'Performs calculations',
     schema: { expression: 'string' },
     handler: async (params) => eval(params.expression)
   };
   client.addTool(calculator);
   ```

2. **Hooks per Modificare Comportamento**
   ```typescript
   client.addHook('beforeMessage', async (message) => {
     console.log('Sending:', message);
     return message; // Può modificare o bloccare
   });
   ```

3. **Session Forking**
   ```typescript
   const forkedSession = await client.forkSession(originalSessionId);
   ```

4. **MCP In-Process**
   - Supporto nativo per Model Context Protocol
   - Nessun processo MCP server separato necessario

5. **Subagents Programmatici**
   - Definizione di agenti specializzati via codice
   - Orchestrazione multi-agent

6. **Streaming Native**
   - Async iterators per streaming real-time
   - Controllo granulare dei messaggi parziali

---

## 2. Confronto: Headless Mode vs SDK

### 2.1 Implementazione Corrente (Headless Mode)

**Come Funziona:**
```csharp
// ClaudeProcessManager.cs
var process = Process.Start("claude", "-p --input-format stream-json --output-format stream-json");

// Scrittura stdin JSONL
var json = $@"{{""type"":""user"",""message"":{{""role"":""user"",""content"":""{prompt}""}}}}";
await stdin.WriteLineAsync(json);

// Lettura stdout JSONL
var line = await stdout.ReadLineAsync();
var response = JsonSerializer.Deserialize<ClaudeMessage>(line);
```

**Vantaggi:**
- ✅ Nessuna dipendenza esterna (solo `claude.exe`)
- ✅ Integrazione nativa con C#/WinForms
- ✅ Controllo totale del processo e lifetime
- ✅ Supporto ufficiale e stabile (CLI interface)
- ✅ Funziona su Windows senza runtime aggiuntivi
- ✅ Competenze team già presenti (C#)
- ✅ Deployment semplice (un eseguibile)

**Svantaggi:**
- ⚠️ Parsing JSON manuale richiesto
- ⚠️ Custom tools richiedono MCP server separati
- ⚠️ Nessun hook nativo per modificare comportamento
- ⚠️ Session forking non documentato (forse possibile con flag hidden)
- ⚠️ Error handling più verboso

### 2.2 Ipotetico SDK Approach (TypeScript/Python)

**Come Funzionerebbe:**
```typescript
// Esempio TypeScript
import { ClaudeSDKClient } from '@anthropic-ai/claude-agent-sdk';

const client = new ClaudeSDKClient({
  systemPrompt: "Custom system prompt",
  permissionMode: 'plan',
  sessionId: existingSessionId
});

for await (const message of client.run({ prompt: userInput })) {
  handleMessage(message);
}
```

**Vantaggi:**
- ✅ API pulita e type-safe
- ✅ Custom tools inline (no MCP server)
- ✅ Hooks nativi
- ✅ Session forking built-in
- ✅ Error handling robusto
- ✅ Documentazione ufficiale completa

**Svantaggi:**
- ❌ Nessun SDK .NET disponibile
- ❌ Richiede Node.js runtime (per TypeScript)
- ❌ Bridge necessario per comunicare con C#
- ❌ Deployment più complesso (due runtime)
- ❌ Competenze aggiuntive necessarie (TypeScript/Node.js)
- ❌ SDK in early stage (v0.1.x, potenziali breaking changes)

---

## 3. Tabella Comparativa Feature

| Feature | Headless Mode (C#) | Agent SDK (TS/Py) | SDK .NET Disponibile |
|---------|-------------------|-------------------|---------------------|
| **Multi-turn conversations** | ✅ Implementato manualmente | ✅ Nativo | ❌ |
| **Session persistence** | ✅ Con MariaDB custom | ✅ Nativo o custom | ❌ |
| **Session forking** | ⚠️ Non documentato | ✅ Nativo | ❌ |
| **Streaming responses** | ✅ JSONL line-by-line | ✅ Native async | ❌ |
| **Tool calls** | ✅ Parse manuale | ✅ Automatico | ⚠️ Solo API Messages |
| **Custom tools inline** | ❌ Serve MCP server | ✅ Inline functions | ❌ |
| **Hooks** | ❌ | ✅ Before/after message | ❌ |
| **MCP support** | ⚠️ Via --mcp-config | ✅ In-process | ❌ |
| **Permission control** | ✅ Via CLI flags | ✅ Programmatico | ❌ |
| **Subagents** | ❌ | ✅ Programmatico | ❌ |
| **Type safety** | ⚠️ Manuale | ✅ Full TypeScript | ✅ C# |
| **.NET integration** | ✅ Nativo | ❌ Richiede bridge | ⚠️ Limitato |
| **Deployment** | ✅ Semplice | ⚠️ Node.js + .NET | ✅ Semplice |
| **Maintenance** | ⚠️ Più boilerplate | ✅ Meno codice | N/A |

---

## 4. Decisione Architetturale: Perché Headless Mode è Corretto

### 4.1 Per il Nostro Use Case (GUI Desktop)

**ClaudeCodeGUI è:**
- Un'applicazione WinForms desktop standalone
- Destinata a singolo utente su Windows
- Con focus su conversazioni persistenti e recovery
- Integrata con database MariaDB locale

**Headless mode è perfetto perché:**
1. Nessuna dipendenza da runtime aggiuntivi
2. Integrazione nativa con Windows e .NET
3. Controllo totale del processo Claude
4. Le feature avanzate dell'SDK non sono necessarie per GUI desktop
5. Deployment semplificato

### 4.2 Quando SDK Sarebbe Preferibile

**Use case dove SDK è superiore:**

1. **Applicazioni Web Multi-Tenant**
   - Backend Node.js/Python condiviso
   - Gestione di centinaia di sessioni simultanee
   - Custom tools complessi per ogni tenant

2. **Sistemi di Orchestrazione Complessi**
   - Workflow multi-agent con subagents
   - Hooks per logging centralizzato, rate limiting, content filtering
   - Session forking per scenari "what-if"

3. **Integrazione con Ecosystem Esistente**
   - Applicazione già in TypeScript/Python
   - Team con competenze JS/Python
   - Infrastructure cloud-native (Lambda, Cloud Functions)

**Il nostro caso NON rientra in questi scenari.**

---

## 5. Scenari d'Uso Avanzati: Custom Tools, Hooks, Session Forking

### 5.1 Custom Tools Inline

**Scenario: Calcolatrice Finanziaria Integrata**

Con l'SDK, si potrebbero creare custom tools complessi inline:

```typescript
// Tool per calcoli finanziari complessi
const financialCalculator = {
  name: 'calculate_portfolio_returns',
  description: 'Calculates portfolio returns with compound interest',
  schema: {
    principal: { type: 'number', description: 'Initial investment' },
    rate: { type: 'number', description: 'Annual interest rate (decimal)' },
    years: { type: 'number', description: 'Investment period in years' },
    contributions: { type: 'number', description: 'Monthly contributions' }
  },
  handler: async (params) => {
    const { principal, rate, years, contributions } = params;

    // Calcolo complesso con libreria finanziaria
    const monthlyRate = rate / 12;
    const months = years * 12;

    let total = principal;
    for (let i = 0; i < months; i++) {
      total = total * (1 + monthlyRate) + contributions;
    }

    return {
      finalValue: total,
      totalContributions: contributions * months,
      interestEarned: total - principal - (contributions * months),
      chart: generateChart(total, months) // Genera grafico
    };
  }
};

client.addTool(financialCalculator);
```

**Utente chiede:** "Se investo 10000€ al 5% annuo per 20 anni con 200€ mensili, quanto avrò?"

Claude chiamerebbe automaticamente il tool e mostrerebbe risultati formattati con grafico.

**Con Headless Mode (alternativa):**
```csharp
// Serve creare MCP server separato
// financial-calculator-mcp/
//   - Program.cs
//   - manifest.json
//   - Deploy come servizio separato
//   - Configurare --mcp-config

// Più complesso ma comunque fattibile
```

**Vantaggio SDK:** Tool inline sono più veloci da sviluppare per casi complessi.

---

### 5.2 Hooks per Modificare Comportamento

**Scenario: Content Filtering e Compliance**

Con hooks, si può intercettare e modificare ogni messaggio:

```typescript
// Hook per filtrare contenuti sensibili
client.addHook('beforeMessage', async (message) => {
  if (message.role === 'user') {
    // Rimuovi dati sensibili prima di inviare
    message.content = redactSensitiveInfo(message.content);
  }
  return message;
});

// Hook per logging centralizzato
client.addHook('afterMessage', async (message) => {
  await logToDatabase({
    sessionId: client.sessionId,
    role: message.role,
    content: message.content,
    timestamp: new Date(),
    userId: currentUser.id
  });
  return message;
});

// Hook per rate limiting
client.addHook('beforeMessage', async (message) => {
  const usage = await getRateLimitUsage(currentUser.id);
  if (usage.exceeded) {
    throw new Error('Rate limit exceeded. Please upgrade your plan.');
  }
  return message;
});
```

**Use Case Concreti:**
1. **Enterprise compliance:** Mascherare automaticamente numeri di carte di credito, SSN, etc.
2. **Audit trail:** Log completo di tutte le interazioni per compliance GDPR
3. **Cost control:** Bloccare utenti che superano quota mensile
4. **Content moderation:** Filtrare contenuti inappropriati prima/dopo Claude
5. **A/B testing:** Modificare system prompt per diversi gruppi utenti

**Con Headless Mode (alternativa):**
```csharp
// Implementabile ma più manuale
var filteredPrompt = RedactSensitiveInfo(userPrompt);
var json = BuildJsonMessage(filteredPrompt);

await logService.LogAsync(sessionId, userPrompt);

if (await rateLimitService.IsExceededAsync(userId))
{
    throw new RateLimitException();
}

await processManager.SendMessageAsync(json);
```

**Vantaggio SDK:** Hooks sono più eleganti e riutilizzabili. Con headless serve boilerplate wrapper.

---

### 5.3 Session Forking

**Scenario: "What-If" Analysis e Branching Conversations**

Session forking permette di creare branch da una conversazione esistente:

```typescript
// Conversazione principale
const mainSession = new ClaudeSDKClient({ sessionId: 'main-123' });

// Utente: "Sto sviluppando una app mobile per e-commerce"
// Claude risponde con architettura consigliata

// Utente vuole esplorare due alternative
const reactNativeFork = await mainSession.forkSession();
const flutterFork = await mainSession.forkSession();

// Fork 1: Esplora React Native
await reactNativeFork.run({
  prompt: "Continua con architettura React Native. Pro e contro?"
});

// Fork 2: Esplora Flutter
await flutterFork.run({
  prompt: "Continua con architettura Flutter. Pro e contro?"
});

// Utente confronta risultati di entrambi i fork
// Poi decide quale fork continuare come main session
```

**Use Case Concreti:**

1. **Decision Making Support:**
   - Utente discute problema con Claude
   - Arriva a bivio decisionale
   - Forka sessione per esplorare opzione A e opzione B in parallelo
   - Confronta risultati
   - Sceglie fork vincente come continuazione

2. **Educational Scenarios:**
   - Studente chiede "Come risolvo questo problema di programmazione?"
   - Teacher fork: Claude guida passo-passo
   - Independent fork: Claude dà hint minimali
   - Confronto per capire meglio il processo

3. **Code Review con Alternative:**
   - Developer mostra codice
   - Fork 1: "Refactor per performance"
   - Fork 2: "Refactor per readability"
   - Fork 3: "Refactor per testability"
   - Confronta tutti gli approcci

4. **Creative Writing:**
   - Scrittore sviluppa trama con Claude
   - Arriva a plot twist
   - Forka 3 sessioni per esplorare twist differenti
   - Valuta quale funziona meglio

5. **Troubleshooting Paths:**
   - "Il server non risponde"
   - Fork 1: Esplora problemi di rete
   - Fork 2: Esplora problemi di configurazione
   - Fork 3: Esplora problemi di database
   - Debug parallelo per trovare root cause

**Implementazione GUI con Session Forking:**

```csharp
// UI Design
┌─────────────────────────────────────┐
│ Main Conversation                   │
│ ├─ Message 1                        │
│ ├─ Message 2                        │
│ └─ Message 3 ◄── Fork Point         │
│     ├─[Fork A: Option 1] 🔀         │
│     │   └─ Explores path A...       │
│     └─[Fork B: Option 2] 🔀         │
│         └─ Explores path B...       │
└─────────────────────────────────────┘
```

**Con Headless Mode (alternativa):**
```csharp
// Forking potrebbe essere supportato ma non documentato
// Possibile workaround:
// 1. Salva tutti i messaggi fino al fork point
// 2. Crea nuove sessioni
// 3. Replay messaggi iniziali in entrambe
// 4. Continua con branch diversi

// Più complesso ma teoricamente fattibile
```

**Vantaggio SDK:** Session forking è nativo e ottimizzato. Con headless richiede replay manuale.

---

## 6. Scenario Completo: Quando SDK Sarebbe Giustificato

### 6.1 Ipotetico Prodotto Enterprise

**Nome:** "Claude Enterprise Copilot" - SaaS multi-tenant

**Caratteristiche Richieste:**

1. **Multi-Tenant Architecture**
   - Centinaia di aziende clienti
   - Ogni azienda ha custom tools specifici (CRM, ERP, etc.)
   - Hooks per compliance e audit per ogni tenant
   - Session forking per decision support workflows

2. **Custom Tools Per Tenant**
   ```typescript
   // Tenant A: E-commerce
   tenantA.addTool(inventoryChecker);
   tenantA.addTool(orderProcessor);
   tenantA.addTool(customerDataRetriever);

   // Tenant B: Healthcare
   tenantB.addTool(patientRecordSearch);
   tenantB.addTool(medicationDatabase);
   tenantB.addTool(hipaaComplianceChecker);
   ```

3. **Hooks Per Compliance**
   ```typescript
   // Ogni tenant ha hook personalizzati
   tenantA.addHook('afterMessage', auditLogHook);
   tenantB.addHook('beforeMessage', hipaaFilterHook);
   ```

4. **Session Forking Workflows**
   ```typescript
   // Workflow: Strategic Planning Assistant
   // 1. Discute problema aziendale
   // 2. Identifica 3 possibili strategie
   // 3. Forka sessione per ogni strategia
   // 4. Esplora pro/contro in parallelo
   // 5. Presenta comparazione
   ```

**In questo caso:** ✅ **SDK sarebbe giustificato**
- Backend TypeScript/Node.js
- Architettura multi-tenant
- Custom tools critici per business logic
- Hooks essenziali per compliance
- Session forking per decision support workflows

**Vs il nostro caso (GUI Desktop):**
- ❌ Single-user app
- ❌ No multi-tenancy
- ❌ Custom tools non critici (user digita prompt normale)
- ❌ No compliance hooks necessari
- ❌ Session forking non prioritario

---

### 6.2 Esempio Concreto: Code Review Assistant

**Scenario:** Team sviluppo con processo code review complesso

```typescript
// Code Review Assistant con SDK
class CodeReviewAssistant {
  private client: ClaudeSDKClient;

  constructor() {
    this.client = new ClaudeSDKClient();

    // Custom tools per code review
    this.client.addTool({
      name: 'analyze_code_diff',
      handler: async (params) => {
        const { gitDiff } = params;
        return await runStaticAnalysis(gitDiff);
      }
    });

    this.client.addTool({
      name: 'check_test_coverage',
      handler: async (params) => {
        return await getCoverageReport(params.files);
      }
    });

    this.client.addTool({
      name: 'search_similar_patterns',
      handler: async (params) => {
        return await searchCodebase(params.pattern);
      }
    });

    // Hooks per tracking
    this.client.addHook('afterMessage', async (msg) => {
      await trackReviewMetrics(msg);
    });
  }

  async reviewPR(prNumber: number) {
    const mainSession = await this.startReview(prNumber);

    // Parallel analysis con session forking
    const securityFork = await mainSession.forkSession();
    const performanceFork = await mainSession.forkSession();
    const maintainabilityFork = await mainSession.forkSession();

    const [securityReport, perfReport, maintainReport] = await Promise.all([
      securityFork.run({ prompt: "Analizza security vulnerabilities" }),
      performanceFork.run({ prompt: "Analizza performance issues" }),
      maintainabilityFork.run({ prompt: "Analizza maintainability" })
    ]);

    return combineReports([securityReport, perfReport, maintainReport]);
  }
}
```

**Benefici:**
- ✅ Custom tools per static analysis, coverage, codebase search
- ✅ Hooks per tracciare metriche team
- ✅ Session forking per analisi parallele (security/perf/maintainability)
- ✅ Workflow automatizzato

**Con Headless Mode:** Molto più complesso. Servirebbe orchestrazione manuale.

---

## 7. Analisi Costi/Benefici: Migrazione a SDK

### 7.1 Effort Richiesto

**Riscrittura Backend (TypeScript/Node.js):**
- Setup progetto Node.js/Express: 1 giorno
- Riscrittura DbService con TypeORM/Prisma: 2 giorni
- Implementazione API REST: 2-3 giorni
- Custom tools development: 2-3 giorni
- Testing e debugging: 3-5 giorni
- **SUBTOTALE: 10-14 giorni** ⏱️

**Frontend WinForms Adaptation:**
- Riscrittura HTTP client layer: 1-2 giorni
- Adapter per API REST: 1 giorno
- Testing integrazione: 2-3 giorni
- **SUBTOTALE: 4-6 giorni** ⏱️

**Deployment e Infrastructure:**
- Setup Node.js runtime su target machines: 1 giorno
- Packaging (electron o simile): 2-3 giorni
- CI/CD pipeline: 1-2 giorni
- **SUBTOTALE: 4-6 giorni** ⏱️

**TOTALE: 18-26 giorni** 📅 (~4-5 settimane)

### 7.2 Benefici Attesi

**Per il nostro use case (GUI Desktop):**
- ⚠️ **Limitati** - Le feature avanzate SDK non sono essenziali
- Custom tools: Uso limitato (user digita prompt liberamente)
- Hooks: Non necessari per single-user desktop app
- Session forking: Nice-to-have ma non critico

**ROI Analysis:**
```
Effort: 4-5 settimane sviluppo + complessità deployment
Benefici: Feature avanzate non critiche per desktop GUI
Conclusione: ROI NEGATIVO ❌
```

### 7.3 Rischi Migrazione

1. **Technical Debt:**
   - Due runtime da mantenere (.NET + Node.js)
   - Bridge layer aggiuntivo (point of failure)
   - Deployment più complesso

2. **Breaking Changes:**
   - SDK in v0.1.x (early stage)
   - Possibili breaking changes in future
   - Headless mode più stabile

3. **Team Skills:**
   - Team deve imparare TypeScript/Node.js
   - Debugging più complesso (cross-language)
   - Maintenance più costoso

---

## 8. Raccomandazioni Finali

### 8.1 Per ClaudeCodeGUI (Corrente)

**✅ MANTENERE HEADLESS MODE**

**Motivazioni:**
1. Appropriato per GUI desktop single-user
2. Nessun SDK .NET disponibile
3. Feature avanzate SDK non critiche per use case
4. Deployment semplice
5. Team competenze già presenti
6. Architettura già funzionante

**Miglioramenti Opzionali (Senza Migrazione):**
```csharp
// 1. JSON Helper Library
public static class ClaudeJsonHelper
{
    public static string BuildUserMessage(string prompt) { }
    public static ClaudeMessage ParseResponse(string json) { }
    public static bool TryParseToolCall(ClaudeMessage msg, out ToolCall call) { }
}

// 2. Implementare Session Forking (Potenzialmente Supportato)
// Investigare flag CLI hidden per forking
// O implementare fork via replay messaggi

// 3. MCP Server .NET (Se Custom Tools Necessari)
// Creare MCP server separato per tool specifici
// Usare --mcp-config per integrazione
```

### 8.2 Quando Considerare SDK (Future)

**Considerare migrazione SOLO SE:**
1. ✅ Il prodotto diventa SaaS multi-tenant
2. ✅ Custom tools complessi diventano critici per business
3. ✅ Serve orchestrazione multi-agent complessa
4. ✅ Team acquisisce competenze TypeScript/Node.js
5. ✅ Anthropic rilascia SDK .NET ufficiale (monitorare!)

**Segnali che SDK diventa necessario:**
- User chiedono custom integrations (CRM, ERP, etc.)
- Serve content filtering/moderation complessa
- Workflow decision support con branching diventa feature chiave
- Volume utenti cresce significativamente (>1000 users)

### 8.3 Monitoring e Future-Proofing

**Da monitorare:**
1. **Rilascio SDK .NET:** Controllare periodicamente repository Anthropic
2. **SDK Maturity:** Quando SDK raggiunge v1.0, rivalutare
3. **Community .NET:** Eventuali wrapper community-driven
4. **Feature Requests:** Se utenti richiedono custom tools, rivalutare

**Update cadence suggerito:**
- Controllare ogni 3 mesi roadmap Anthropic
- Seguire Discord community per annunci SDK .NET
- Valutare se feature requests utenti matchano con SDK features

---

## 9. Conclusioni

### 9.1 Risposta alla Domanda Originale

**"Abbiamo fatto un errore architetturale?"**

**NO.** ✅ L'approccio headless mode è:
- ✅ Corretto per progetti .NET/WinForms
- ✅ Appropriato per GUI desktop single-user
- ✅ Raccomandato ufficialmente per integrazioni programmatiche
- ✅ Stabile e supportato a lungo termine
- ✅ Privo di dipendenze complesse

### 9.2 L'SDK è Fantastico, Ma...

**Agent SDK offre feature potenti:**
- Custom tools inline
- Hooks eleganti
- Session forking nativo
- API pulita e type-safe

**MA per il nostro caso:**
- ❌ Non disponibile per .NET
- ❌ Feature avanzate non critiche
- ❌ Costi migrazione > Benefici
- ❌ Complessità deployment aumenta

### 9.3 Path Forward

**Short Term (Ora - 6 mesi):**
- Continuare con headless mode
- Migliorare con helper libraries
- Monitorare feedback utenti
- Valutare se feature avanzate diventano necessarie

**Medium Term (6-12 mesi):**
- Monitorare rilascio SDK .NET
- Considerare pilot per custom tools via MCP
- Rivalutare se use case evolve verso enterprise

**Long Term (12+ mesi):**
- Se SDK .NET rilasciato: valutare migrazione
- Se prodotto diventa SaaS: considerare backend Node.js
- Se rimane desktop app: mantenere headless mode

---

## 10. Appendice: Risorse e Riferimenti

### 10.1 Link Utili

**Documentazione Ufficiale:**
- Claude Agent SDK Overview: https://docs.claude.com/en/api/agent-sdk/overview
- Claude Code Headless Mode: https://docs.claude.com/en/docs/claude-code/headless
- SDK Migration Guide: https://docs.claude.com/en/docs/claude-code/sdk/migration-guide

**Repository GitHub:**
- TypeScript SDK: https://github.com/anthropics/claude-agent-sdk-typescript
- Python SDK: https://github.com/anthropics/claude-agent-sdk-python

**Community:**
- Discord: https://anthropic.com/discord
- GitHub Discussions: Controllare issues/discussions nei repository

### 10.2 Glossario

- **Headless Mode:** Modalità CLI di Claude Code con stream-json I/O
- **Agent SDK:** Libreria programmatica per integrare Claude con feature agentic
- **Custom Tools:** Funzioni custom che Claude può chiamare durante conversazioni
- **Hooks:** Callback per intercettare e modificare messaggi prima/dopo invio
- **Session Forking:** Creazione di branch da conversazione esistente per esplorare alternative
- **MCP:** Model Context Protocol - Standard per tool e context providers

### 10.3 Decision Matrix

**Usa Headless Mode se:**
- ✅ Progetti .NET/C#/Java/Go
- ✅ GUI desktop standalone
- ✅ Single-user o small team
- ✅ Feature base sufficienti
- ✅ Deployment semplice prioritario

**Usa Agent SDK se:**
- ✅ Progetti TypeScript/Python
- ✅ Backend web/API
- ✅ Multi-tenant SaaS
- ✅ Custom tools critici
- ✅ Workflow orchestration complessi

---

**Fine del documento**

**Ultima modifica:** 2025-10-29
**Prossima revisione:** 2026-01-29 (3 mesi)
