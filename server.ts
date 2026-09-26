import express from 'express';
import path from 'path';
import { fileURLToPath } from 'url';
import { createServer as createViteServer } from 'vite';
import { GoogleGenAI } from "@google/genai";
import dotenv from 'dotenv';

dotenv.config();

const __dirname = path.dirname(fileURLToPath(import.meta.url));

async function createServer() {
  const app = express();
  app.use(express.json({ limit: '1mb' }));

  const vite = await createViteServer({
    server: { middlewareMode: true },
    appType: 'spa',
  });

  app.use(vite.middlewares);

  // Gemini AI Client
  const ai = new GoogleGenAI({
    apiKey: process.env.GEMINI_API_KEY || '',
    httpOptions: {
      headers: {
        'User-Agent': 'aistudio-build',
      }
    }
  });

  // AI Endpoints
  app.post('/api/ai/explain-finding', async (req, res) => {
    try {
      const { exception } = req.body;
      if (!process.env.GEMINI_API_KEY) {
        return res.status(400).json({ error: "AI assistance is not configured." });
      }

      const systemPrompt = `You are an expert, professional audit assistant. Your job is to explain a specific audit rule finding clearly and neutrally.
You must adhere to the following rules:
1. Do NOT independently determine whether a transaction is fraudulent, illegal, or tax-evasive. 
2. Use neutral audit-analysis terminology, such as 'potential exception', 'review required', or 'rule triggered'.
3. Follow the STRICT RESPONSE FORMAT, separating:
   - FACT: What are the concrete recorded values?
   - RULE RESULT: What rule triggered and why?
   - POSSIBLE INTERPRETATION: Explain that these are possibilities, not absolute truths (e.g., could be a formatting error, rounding difference, or missing voucher link).
   - REVIEW QUESTION: Suggest what the human auditor should check.`;

      const userPrompt = `
Company Name: [Sanitized]
Rule Code: ${exception.ruleId}
Rule Name: ${exception.ruleName}
Category: ${exception.module}
Severity: ${exception.severity}
Voucher Number: ${exception.voucherNumber || "—"}
Voucher Date: ${exception.voucherDate || "—"}
Ledger/Party: ${exception.partyLedgerName || "—"}
Flagged Amount: ${exception.amount || "—"}
Evidence: ${exception.evidenceJson}
Rule Trigger Explanation: ${exception.whyFlagged || "—"}`;

      const response = await ai.models.generateContent({
        model: "gemini-3.8-flash",
        contents: [{ role: "user", parts: [{ text: `${systemPrompt}\n\n${userPrompt}` }] }],
      });

      res.json({ text: response.text });
    } catch (error: any) {
      console.error("AI Error:", error);
      res.status(500).json({ error: error.message || "An error occurred during AI processing." });
    }
  });

  app.post('/api/ai/suggest-questions', async (req, res) => {
    try {
      const { exception } = req.body;
      if (!process.env.GEMINI_API_KEY) {
        return res.status(400).json({ error: "AI assistance is not configured." });
      }

      const systemPrompt = `You are a professional auditor copilot. Suggest 4-5 constructive, neutral, and precise review questions for the human auditor to investigate regarding the triggered rule. 
Do not imply any guilt, tax evasion, or fraud. Keep questions strictly professional, focusing on voucher classification, supporting documentation, party mapping, and ledger consistency.`;

      const userPrompt = `
Rule Code: ${exception.ruleId}
Rule Name: ${exception.ruleName}
Category: ${exception.module}
Ledger: ${exception.partyLedgerName || "—"}
Evidence: ${exception.evidenceJson}
Trigger Reason: ${exception.whyFlagged || "—"}`;

      const response = await ai.models.generateContent({
        model: "gemini-3.8-flash",
        contents: [{ role: "user", parts: [{ text: `${systemPrompt}\n\n${userPrompt}` }] }],
      });

      res.json({ text: response.text });
    } catch (error: any) {
      res.status(500).json({ error: error.message });
    }
  });

  app.post('/api/ai/draft-remark', async (req, res) => {
    try {
      const { exception } = req.body;
      if (!process.env.GEMINI_API_KEY) {
        return res.status(400).json({ error: "AI assistance is not configured." });
      }

      const systemPrompt = `You are an assistant to a corporate auditor. Draft a concise, highly professional working paper note or remark for the selected exception.
Your draft must follow this structure:
Observation: [Descriptive summary of what was detected in neutral audit-analysis terminology, e.g. 'Difference identified during reconciliation']
Verification: [What supporting documentation or clarification the auditor should verify]
Conclusion: [State that the final conclusion is left for the auditor to determine after completing verification]

Strictly avoid conclusions such as 'Fraud detected' or 'Tax evasion'. Only use phrases like 'Transaction requires verification' or 'Supporting documentation should be reviewed'.`;

      const userPrompt = `
Rule Name: ${exception.ruleName}
Category: ${exception.module}
Voucher Number: ${exception.voucherNumber || "—"}
Amount: ${exception.amount || "—"}
Details: ${exception.whyFlagged || "—"}`;

      const response = await ai.models.generateContent({
        model: "gemini-3.8-flash",
        contents: [{ role: "user", parts: [{ text: `${systemPrompt}\n\n${userPrompt}` }] }],
      });

      res.json({ text: response.text });
    } catch (error: any) {
      res.status(500).json({ error: error.message });
    }
  });

  app.post('/api/ai/summarize-audit', async (req, res) => {
    try {
      const { stats } = req.body;
      if (!process.env.GEMINI_API_KEY) {
        return res.status(400).json({ error: "AI assistance is not configured." });
      }

      const systemPrompt = `You are an executive audit reporter. Summarize the completed automated audit run based on the provided aggregate statistics.
Provide a professional, high-level summary that contains:
1. Overview of the audit execution scope.
2. Highlight key focus categories requiring review (e.g., GST or TDS) based on the findings counts.
3. Highlight general patterns (e.g., recurring reconciliation differences or missing narrations) indicated by the counts.
4. Suggested review priorities for the auditor.

Rules:
- Do NOT draw legal, political, or business compliance conclusions.
- State findings clearly and neutrally.
- The summary must be generated solely from the provided statistics. Do not invent any names or transactions.`;

      const userPrompt = `
Transactions Audited: ${stats.transactionsAudited}
Rules Executed: ${stats.rulesExecuted}
Total Findings Discovered: ${stats.findings}
High Severity Findings: ${stats.highPriority}
Review Status Pending: ${stats.reviewRequired}
Detailed Exception Counts by Category: ${JSON.stringify(stats.additionalStats)}`;

      const response = await ai.models.generateContent({
        model: "gemini-3.8-flash",
        contents: [{ role: "user", parts: [{ text: `${systemPrompt}\n\n${userPrompt}` }] }],
      });

      res.json({ text: response.text });
    } catch (error: any) {
      res.status(500).json({ error: error.message });
    }
  });

  app.post('/api/ai/suggest-procedures', async (req, res) => {
    try {
      const { plan, risks } = req.body;
      if (!process.env.GEMINI_API_KEY) {
        return res.status(400).json({ error: "AI assistance is not configured." });
      }

      const systemPrompt = `You are a senior audit manager. Based on the provided audit plan and risk assessment, suggest 3-5 specific audit procedures for the identified risks.
Keep suggestions professional, objective-oriented, and neutrally worded. Do not conclude fraud or illegality.`;

      const userPrompt = `
Company: ${plan.companyName}
Period: ${plan.financialPeriod}
Risks Identified: ${risks.map((r: any) => `${r.auditArea}: ${r.description} (Risk Level: ${r.riskLevel})`).join('\n')}
Audit Areas: ${plan.selectedAreas.filter((a: any) => a.isEnabled).map((a: any) => a.name).join(', ')}`;

      const response = await ai.models.generateContent({
        model: "gemini-3.8-flash",
        contents: [{ role: "user", parts: [{ text: `${systemPrompt}\n\n${userPrompt}` }] }],
      });

      res.json({ text: response.text });
    } catch (error: any) {
      res.status(500).json({ error: error.message });
    }
  });

  app.post('/api/ai/draft-plan-notes', async (req, res) => {
    try {
      const { plan } = req.body;
      if (!process.env.GEMINI_API_KEY) {
        return res.status(400).json({ error: "AI assistance is not configured." });
      }

      const systemPrompt = `You are an audit planning assistant. Draft professional, high-level audit plan notes/objectives for the current engagement.
Focus on scope, materiality basis, and key focus areas.`;

      const userPrompt = `
Company: ${plan.companyName}
Period: ${plan.financialPeriod}
Materiality: ${plan.materialityAmount}
Focus Areas: ${plan.selectedAreas.filter((a: any) => a.isEnabled).map((a: any) => a.name).join(', ')}`;

      const response = await ai.models.generateContent({
        model: "gemini-3.8-flash",
        contents: [{ role: "user", parts: [{ text: `${systemPrompt}\n\n${userPrompt}` }] }],
      });

      res.json({ text: response.text });
    } catch (error: any) {
      res.status(500).json({ error: error.message });
    }
  });

  app.use('*', async (req, res) => {
    try {
      const url = req.originalUrl;
      const html = await vite.transformIndexHtml(url, '');
      res.status(200).set({ 'Content-Type': 'text/html' }).end(html);
    } catch (e: any) {
      vite.ssrFixStacktrace(e);
      res.status(500).end(e.message);
    }
  });

  const port = 3000;
  app.listen(port, '0.0.0.0', () => {
    console.log(`Server started on http://localhost:${port}`);
  });
}

createServer();
