# KDK Feature Parity and Beyond — Delivery Roadmap

**Status:** Program baseline; features are not considered implemented until code, tests, UI, persistence, documentation, and (where applicable) a verified external integration are complete.

## Product principles

- Preserve the existing offline-first, local SQLite operating model for accounting and audit data wherever practical.
- Government portal filing, GSTN/Income Tax/TRACES access, GSP services, DSC signing, email, WhatsApp, and ERP/cloud connectivity must use authorised providers and explicit tenant/user consent. Never simulate a successful submission.
- Keep preparation, validation, approval, submission, acknowledgement, and post-filing reconciliation as separate auditable states.
- Tax computations and statutory rules require effective dates, financial-year/assessment-year versioning, source references, regression tests, and professional review.
- Enforce tenant/client/company isolation, role-based permissions, encryption, backup/restore, retention controls, and immutable activity history.
- Bulk operations must support preview, validation summaries, idempotency, retry/resume, and per-client failure isolation.
- Large datasets must be paginated/streamed and cancellation-aware; do not trade audit completeness for performance.

## Existing capabilities evidenced in the repository

- TallyPrime XML integration and local accounting synchronization.
- Accounting anomaly rules, GST and TDS audit rule engines.
- GSTR-2B purchase reconciliation, ITC risk screening, vendor GSTIN/PAN intelligence.
- TDS threshold checks and challan/return reconciliation.
- Compliance deadline calendar/alerts, multi-company compliance overview.
- Audit findings, reviewer assignment, due dates, evidence, working papers, audit trail, quality control and finalization.
- Offline-first SQLite persistence and optional consent-gated Gemini assistance.

These are not equivalent to complete tax-return preparation or government filing.

## Workstream A — GST compliance

1. GSTIN/client master, registrations, return periods, filing credentials metadata (never store portal passwords in plaintext).
2. Imports: Tally/books, Excel/CSV, GSTR-1, GSTR-2A, GSTR-2B, GSTR-3B, IMS, e-invoice and e-way-bill data through supported authorised sources.
3. Reconciliation: books vs 2A/2B, 1 vs 3B, credit/debit notes, amendments, advances, reverse charge, ISD, import/SEZ, supplier risk and period roll-forward.
4. Return workpapers: GSTR-1, GSTR-3B, GSTR-9, GSTR-9C and ISD preparation with traceable source-to-summary mapping.
5. Validation: schema, arithmetic, tax-rate, place-of-supply, ITC eligibility, duplicate, period, registration and statutory rule checks.
6. Workflow: draft -> validated -> reviewer-approved -> authorised submission -> acknowledgement -> post-filing reconciliation.
7. GST notices/orders: inbox/import, deadline, owner, evidence, response draft, approval, submission tracking and closure.
8. Bulk GSTIN dashboard, exception queue, export packs and filing MIS.

## Workstream B — TDS/TCS compliance

1. Deductor/deductee master, PAN validation through authorised services, section/rate/threshold rules with effective dates.
2. Challan import and matching, challan consumption, short/excess deduction, interest/late fee and unconsumed challan reports.
3. Prepare/validate 24Q, 26Q, 27Q, 27EQ and applicable correction statements; support 24G and other forms where relevant.
4. Form 16/16A/27D generation and controlled distribution; TRACES requests/download tracking where authorised.
5. Bulk quarter processing, FVU validation/outputs and filing status/acknowledgement workflow.
6. Reconciliation against Tally, challans, 26AS and filed statements.

## Workstream C — Income tax and tax audit

1. Client master and assessment-year-specific profile, tax regime, residential status and source documents.
2. Import AIS/TIS/26AS and broker/capital-gain statements using authorised downloads/integrations.
3. Computation engine for salary, house property, business/profession, capital gains, other sources, deductions, exemptions, set-off/carry-forward and tax/interest.
4. ITR preparation for applicable ITR forms, validation, JSON generation/import and controlled e-filing integration.
5. Advance tax, self-assessment tax, refunds, demands, rectification, defective-return and notice workflows.
6. Tax audit: trial balance mapping, 3CA/3CB/3CD workpapers, clause-level evidence, UDIN workflow and reviewer sign-off.
7. Financial statements, depreciation schedules, comparative statements, MIS and client computation reports.

## Workstream D — CA practice and revenue operations

1. Client/engagement master: individuals, entities, GSTINs, PANs, contacts, authorised representatives, engagement letters and KYC status.
2. Recurring task templates for GST/TDS/ITR/audit/ROC and custom services; calendar, Kanban/list, SLA, escalation and reminders.
3. Team directory, role/permission matrix, workload/capacity, assignment, maker-checker-reviewer-CA sign-off.
4. Time/effort capture, fee schedules, estimates, invoices, credit notes, receipts, collections, outstanding and unbilled work.
5. Client document requests, secure upload/download, versioning, tags, retention and request reminders.
6. Email-to-task and client communication log; WhatsApp/email notifications only through approved providers and consent.
7. Client portal with scoped access to tasks, document requests, reports, invoices, notices and filing acknowledgements.

## Workstream E — Intelligence, automation and enterprise

1. AI-assisted extraction/classification of PDFs, spreadsheets and notices with confidence scores and mandatory human verification.
2. Anomaly/mismatch prioritisation with explainable evidence and source links; never autonomous statutory sign-off.
3. Regulatory update pipeline: source, effective date, impact assessment, test fixtures, reviewer approval and signed release.
4. PDF-to-Excel, document manager, reusable forms, project/CMA reports, cash flow/fund flow and EMI schedules.
5. ERP integrations (Tally first; SAP/Oracle/Busy/Zoho only through documented, authorised interfaces).
6. Multi-branch/entity portfolio dashboards, consolidated controls, audit logs, data export and enterprise identity integration.
7. Optional secure cloud sync and mobile companion with tenant isolation, encryption, revocation and offline-safe conflict handling.

## Release gates

Every feature must have:
- Domain model and schema migration with upgrade/rollback or recovery plan.
- Repository/service/view-model/UI implementation with permission checks.
- Unit tests and integration tests, including boundary cases and financial-year isolation.
- Reconciliation fixtures with known expected totals; no hard-coded success responses.
- Cancellation, pagination, retry/idempotency and resource-disposal checks for long-running work.
- Audit events for material changes and external submissions.
- Updated user manual and release notes.
- CI build and all automated tests passing before merge.

## Initial delivery sequence

1. Close current Tally XML request-safety fix and keep all external request construction validated.
2. Build a reusable client/engagement and compliance-period foundation.
3. Deliver GST return workpapers and validation before any portal submission integration.
4. Add authorised GSTN/GSP submission only after provider credentials, API contracts, certification and user approval are available.
5. Continue TDS, ITR, practice/revenue, client portal and enterprise workstreams in independently testable releases.

## Important scope boundary

A repository-only implementation cannot obtain government/GSP credentials, portal approvals, DSC certificates, WhatsApp business approval, or legal authority on behalf of the user. Those integrations must be connected and tested with the user's authorised provider accounts. Until then, the app may prepare, validate, export and track work, but must clearly label it as not filed.
