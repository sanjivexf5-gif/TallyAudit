import { WorkspaceExceptionItem } from './workspaceData';

export interface AiAuditRunStats {
  transactionsAudited: number;
  rulesExecuted: number;
  findings: number;
  highPriority: number;
  reviewRequired: number;
  additionalStats: Record<string, number>;
}

export const aiAssistantService = {
  async explainFinding(exception: WorkspaceExceptionItem): Promise<string> {
    const response = await fetch('/api/ai/explain-finding', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ exception }),
    });
    const data = await response.json();
    if (data.error) throw new Error(data.error);
    return data.text;
  },

  async suggestQuestions(exception: WorkspaceExceptionItem): Promise<string> {
    const response = await fetch('/api/ai/suggest-questions', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ exception }),
    });
    const data = await response.json();
    if (data.error) throw new Error(data.error);
    return data.text;
  },

  async draftRemark(exception: WorkspaceExceptionItem): Promise<string> {
    const response = await fetch('/api/ai/draft-remark', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ exception }),
    });
    const data = await response.json();
    if (data.error) throw new Error(data.error);
    return data.text;
  },

  async summarizeAudit(stats: AiAuditRunStats): Promise<string> {
    const response = await fetch('/api/ai/summarize-audit', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ stats }),
    });
    const data = await response.json();
    if (data.error) throw new Error(data.error);
    return data.text;
  },

  async suggestProcedures(plan: any, risks: any[]): Promise<string> {
    const response = await fetch('/api/ai/suggest-procedures', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ plan, risks }),
    });
    const data = await response.json();
    if (data.error) throw new Error(data.error);
    return data.text;
  },

  async draftPlanNotes(plan: any): Promise<string> {
    const response = await fetch('/api/ai/draft-plan-notes', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ plan }),
    });
    const data = await response.json();
    if (data.error) throw new Error(data.error);
    return data.text;
  }
};
