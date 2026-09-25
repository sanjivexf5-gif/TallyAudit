export type DuplicateConfidenceTier = 'Exact Duplicate' | 'Likely Duplicate' | 'Possible Duplicate';

export interface TransactionSnapshotItem {
  voucherId: string;
  voucherNumber: string;
  referenceNumber?: string;
  voucherDate: string;
  voucherTypeName: 'Sales' | 'Purchase' | 'Receipt' | 'Payment' | 'Journal' | 'Credit Note' | 'Debit Note';
  partyLedgerName: string;
  partyGstin?: string;
  partyPan?: string;
  totalAmount: number;
  narration?: string;
  lineItemCount: number;
}

export interface FieldComparisonItem {
  fieldName: string;
  originalValue: string;
  duplicateValue: string;
  isMatched: boolean;
  differenceNote?: string;
}

export interface DuplicateMatchPairItem {
  matchId: string;
  tier: DuplicateConfidenceTier;
  confidenceScore: number;
  strategyUsed: string;
  voucherCategory: 'Sales' | 'Purchase' | 'Receipt' | 'Payment' | 'Journal' | 'Credit Note' | 'Debit Note';
  originalTransaction: TransactionSnapshotItem;
  potentialDuplicate: TransactionSnapshotItem;
  matchingFields: string[];
  differenceFields: string[];
  detailedComparisons: FieldComparisonItem[];
  explanation: string;
  evidenceJson: string;
  reviewStatus: 'Pending' | 'Confirmed Duplicate' | 'False Positive (Legitimate)' | 'Resolved';
  reviewer?: string;
  reviewerNote?: string;
}

export interface DuplicateConfigState {
  enableExactMatch: boolean;
  enableStrongMatch: boolean;
  enablePossibleMatch: boolean;
  strongDateWindowDays: number;
  possibleDateWindowDays: number;
  amountTolerancePercent: number;
  amountToleranceRupees: number;
  narrationSimilarityThreshold: number;
}

export const initialDuplicateMatches: DuplicateMatchPairItem[] = [
  // 1. Sales Invoice - Exact Duplicate
  {
    matchId: 'DUP-PAIR-001',
    tier: 'Exact Duplicate',
    confidenceScore: 100,
    strategyUsed: 'Exact Match: Date + Party + Amount + Invoice Number',
    voucherCategory: 'Sales',
    originalTransaction: {
      voucherId: 'V-SLS-01',
      voucherNumber: 'INV/25-26/101',
      referenceNumber: 'PO-9912',
      voucherDate: '10-May-2025',
      voucherTypeName: 'Sales',
      partyLedgerName: 'Acme Steel Distribution',
      partyGstin: '27BBBCB3333E1Z8',
      partyPan: 'BBBCB3333E',
      totalAmount: 45000,
      narration: 'Supply of structural steel sections batch A dispatched via Truck MH12-8812',
      lineItemCount: 3
    },
    potentialDuplicate: {
      voucherId: 'V-SLS-02',
      voucherNumber: 'INV/25-26/101',
      referenceNumber: 'PO-9912',
      voucherDate: '10-May-2025',
      voucherTypeName: 'Sales',
      partyLedgerName: 'Acme Steel Distribution',
      partyGstin: '27BBBCB3333E1Z8',
      partyPan: 'BBBCB3333E',
      totalAmount: 45000,
      narration: 'Supply of structural steel sections batch A dispatched via Truck MH12-8812',
      lineItemCount: 3
    },
    matchingFields: ['Invoice Number', 'Party Ledger', 'Transaction Date', 'Total Amount', 'Reference Number', 'Narration', 'Voucher Type'],
    differenceFields: [],
    detailedComparisons: [
      { fieldName: 'Invoice Number', originalValue: 'INV/25-26/101', duplicateValue: 'INV/25-26/101', isMatched: true },
      { fieldName: 'Party Ledger', originalValue: 'Acme Steel Distribution', duplicateValue: 'Acme Steel Distribution', isMatched: true },
      { fieldName: 'Voucher Date', originalValue: '10-May-2025', duplicateValue: '10-May-2025', isMatched: true },
      { fieldName: 'Total Amount', originalValue: '₹45,000.00', duplicateValue: '₹45,000.00', isMatched: true },
      { fieldName: 'Reference No.', originalValue: 'PO-9912', duplicateValue: 'PO-9912', isMatched: true },
      { fieldName: 'Narration', originalValue: 'Supply of structural steel sections...', duplicateValue: 'Supply of structural steel sections...', isMatched: true }
    ],
    explanation: 'Potential exact duplicate match detected: Voucher INV/25-26/101 and its duplicate entry share 100% identical invoice number, party "Acme Steel Distribution", transaction date (10-May-2025), amount (₹45,000.00), and narration.',
    evidenceJson: '{"MatchTier": "Exact Duplicate", "Confidence": 100, "MatchedFields": ["VoucherNumber", "Party", "Date", "Amount", "Reference", "Narration"], "DateDeltaDays": 0}',
    reviewStatus: 'Pending'
  },

  // 2. Purchase Invoice - Likely Duplicate
  {
    matchId: 'DUP-PAIR-002',
    tier: 'Likely Duplicate',
    confidenceScore: 92.5,
    strategyUsed: 'Strong Match: Party + Exact Amount + Proximity Date (2 days) + Voucher Type',
    voucherCategory: 'Purchase',
    originalTransaction: {
      voucherId: 'V-PUR-01',
      voucherNumber: 'PUR/2025/088',
      referenceNumber: 'BILL-88',
      voucherDate: '01-Jun-2025',
      voucherTypeName: 'Purchase',
      partyLedgerName: 'Bharat Heavy Plates Ltd',
      partyGstin: '27AAACB2222D1Z9',
      partyPan: 'AAACB2222D',
      totalAmount: 75000,
      narration: 'Procurement of heavy boiler grade steel plates consignment #1',
      lineItemCount: 2
    },
    potentialDuplicate: {
      voucherId: 'V-PUR-02',
      voucherNumber: 'PUR/2025/091',
      referenceNumber: 'BILL-88-A',
      voucherDate: '03-Jun-2025',
      voucherTypeName: 'Purchase',
      partyLedgerName: 'Bharat Heavy Plates Ltd',
      partyGstin: '27AAACB2222D1Z9',
      partyPan: 'AAACB2222D',
      totalAmount: 75000,
      narration: 'Procurement of heavy boiler grade steel plates consignment #1 repeat entry',
      lineItemCount: 2
    },
    matchingFields: ['Party Ledger', 'Total Amount', 'Voucher Type (Purchase)', 'Party GSTIN'],
    differenceFields: ['Voucher Number (PUR/2025/088 vs PUR/2025/091)', 'Date (2 days delta: 01-Jun vs 03-Jun)', 'Reference No (BILL-88 vs BILL-88-A)'],
    detailedComparisons: [
      { fieldName: 'Party Ledger', originalValue: 'Bharat Heavy Plates Ltd', duplicateValue: 'Bharat Heavy Plates Ltd', isMatched: true },
      { fieldName: 'Total Amount', originalValue: '₹75,000.00', duplicateValue: '₹75,000.00', isMatched: true },
      { fieldName: 'Voucher Date', originalValue: '01-Jun-2025', duplicateValue: '03-Jun-2025', isMatched: false, differenceNote: '+2 days interval' },
      { fieldName: 'Voucher Number', originalValue: 'PUR/2025/088', duplicateValue: 'PUR/2025/091', isMatched: false, differenceNote: 'Different internal numbering' },
      { fieldName: 'Supplier Ref', originalValue: 'BILL-88', duplicateValue: 'BILL-88-A', isMatched: false, differenceNote: 'Similar reference suffix' },
      { fieldName: 'Narration', originalValue: 'Procurement of heavy boiler grade steel...', duplicateValue: 'Procurement of heavy boiler grade steel... repeat entry', isMatched: true, differenceNote: '92% text similarity' }
    ],
    explanation: 'Potential likely duplicate match: Vouchers PUR/2025/088 and PUR/2025/091 share identical vendor "Bharat Heavy Plates Ltd" and exact commercial value of ₹75,000.00 posted within a 2-day window under Purchase vouchers.',
    evidenceJson: '{"MatchTier": "Likely Duplicate", "Confidence": 92.5, "Party": "Bharat Heavy Plates Ltd", "Amount": 75000.00, "DaysBetween": 2, "OriginalRef": "BILL-88", "DuplicateRef": "BILL-88-A"}',
    reviewStatus: 'Pending'
  },

  // 3. Payment Voucher - Possible Duplicate
  {
    matchId: 'DUP-PAIR-003',
    tier: 'Possible Duplicate',
    confidenceScore: 74.0,
    strategyUsed: 'Possible Match: Party + Similar Amount (Paise rounding) + 3 days interval + Narration token match',
    voucherCategory: 'Payment',
    originalTransaction: {
      voucherId: 'V-PMT-01',
      voucherNumber: 'PMT/0044',
      referenceNumber: 'CHQ-1001',
      voucherDate: '15-Jun-2025',
      voucherTypeName: 'Payment',
      partyLedgerName: 'Precision Tooling Corp',
      partyPan: 'CCCCD4444F',
      totalAmount: 25000,
      narration: 'Advance payment for tooling dies work order #402',
      lineItemCount: 2
    },
    potentialDuplicate: {
      voucherId: 'V-PMT-02',
      voucherNumber: 'PMT/0047',
      referenceNumber: 'CHQ-1002',
      voucherDate: '18-Jun-2025',
      voucherTypeName: 'Payment',
      partyLedgerName: 'Precision Tooling Corp',
      partyPan: 'CCCCD4444F',
      totalAmount: 25000,
      narration: 'Advance for tooling dies work order #402 released via bank',
      lineItemCount: 2
    },
    matchingFields: ['Payee Party Ledger', 'Exact Amount', 'Voucher Type (Payment)', 'Work Order Reference #402'],
    differenceFields: ['Voucher Date (+3 days)', 'Cheque Number (CHQ-1001 vs CHQ-1002)', 'Voucher Number (PMT/0044 vs PMT/0047)'],
    detailedComparisons: [
      { fieldName: 'Payee Party', originalValue: 'Precision Tooling Corp', duplicateValue: 'Precision Tooling Corp', isMatched: true },
      { fieldName: 'Amount', originalValue: '₹25,000.00', duplicateValue: '₹25,000.00', isMatched: true },
      { fieldName: 'Voucher Date', originalValue: '15-Jun-2025', duplicateValue: '18-Jun-2025', isMatched: false, differenceNote: '+3 days interval' },
      { fieldName: 'Cheque / Ref', originalValue: 'CHQ-1001', duplicateValue: 'CHQ-1002', isMatched: false, differenceNote: 'Sequential cheque numbering' },
      { fieldName: 'Narration', originalValue: 'Advance payment for tooling dies work order #402', duplicateValue: 'Advance for tooling dies work order #402 released...', isMatched: true, differenceNote: '84% token overlap' }
    ],
    explanation: 'Potential duplicate match identified for auditor evaluation between payment PMT/0044 and PMT/0047: Both entries disburse ₹25,000.00 to "Precision Tooling Corp" referencing Work Order #402 within a 3-day interval.',
    evidenceJson: '{"MatchTier": "Possible Duplicate", "Confidence": 74.0, "Payee": "Precision Tooling Corp", "Amount": 25000.00, "DateDeltaDays": 3, "CommonToken": "work order #402"}',
    reviewStatus: 'Pending'
  },

  // 4. Receipt Voucher - Likely Duplicate
  {
    matchId: 'DUP-PAIR-004',
    tier: 'Likely Duplicate',
    confidenceScore: 89.0,
    strategyUsed: 'Strong Match: Same Customer + Exact Settlement Amount (₹1,00,000) + Next Day Entry',
    voucherCategory: 'Receipt',
    originalTransaction: {
      voucherId: 'V-RCPT-01',
      voucherNumber: 'RCPT/0012',
      referenceNumber: 'NEFT-881',
      voucherDate: '01-Jul-2025',
      voucherTypeName: 'Receipt',
      partyLedgerName: 'Acme Steel Distribution',
      partyGstin: '27BBBCB3333E1Z8',
      totalAmount: 100000,
      narration: 'Customer balance settlement received through HDFC Bank NEFT #881',
      lineItemCount: 2
    },
    potentialDuplicate: {
      voucherId: 'V-RCPT-02',
      voucherNumber: 'RCPT/0014',
      referenceNumber: 'NEFT-881',
      voucherDate: '02-Jul-2025',
      voucherTypeName: 'Receipt',
      partyLedgerName: 'Acme Steel Distribution',
      partyGstin: '27BBBCB3333E1Z8',
      totalAmount: 100000,
      narration: 'Customer balance settlement NEFT #881 re-entered in error',
      lineItemCount: 2
    },
    matchingFields: ['Customer Party', 'Total Amount', 'Bank Reference (NEFT-881)', 'Voucher Type (Receipt)'],
    differenceFields: ['Voucher Date (+1 day)', 'Voucher Number (RCPT/0012 vs RCPT/0014)'],
    detailedComparisons: [
      { fieldName: 'Customer', originalValue: 'Acme Steel Distribution', duplicateValue: 'Acme Steel Distribution', isMatched: true },
      { fieldName: 'Total Amount', originalValue: '₹1,00,000.00', duplicateValue: '₹1,00,000.00', isMatched: true },
      { fieldName: 'Bank Ref', originalValue: 'NEFT-881', duplicateValue: 'NEFT-881', isMatched: true },
      { fieldName: 'Voucher Date', originalValue: '01-Jul-2025', duplicateValue: '02-Jul-2025', isMatched: false, differenceNote: '+1 day gap' }
    ],
    explanation: 'Potential likely duplicate match: Receipt voucher RCPT/0014 entered on 02-Jul-2025 shares the identical customer "Acme Steel Distribution", exact sum of ₹1,00,000.00, and matching bank transaction identifier "NEFT-881" as RCPT/0012.',
    evidenceJson: '{"MatchTier": "Likely Duplicate", "Confidence": 89.0, "Party": "Acme Steel Distribution", "Amount": 100000.00, "BankRef": "NEFT-881", "DateDeltaDays": 1}',
    reviewStatus: 'Pending'
  },

  // 5. Journal Entry - Exact Duplicate
  {
    matchId: 'DUP-PAIR-005',
    tier: 'Exact Duplicate',
    confidenceScore: 100,
    strategyUsed: 'Exact Match: Same Date + Same Party + Exact Amount + Same Adjustment Heads',
    voucherCategory: 'Journal',
    originalTransaction: {
      voucherId: 'V-JRN-01',
      voucherNumber: 'JRN/0055',
      voucherDate: '15-Jul-2025',
      voucherTypeName: 'Journal',
      partyLedgerName: 'Bharat Heavy Plates Ltd',
      totalAmount: 12000,
      narration: 'Freight debit note adjustment and supplier ledger credit reconciliation',
      lineItemCount: 2
    },
    potentialDuplicate: {
      voucherId: 'V-JRN-02',
      voucherNumber: 'JRN/0056',
      voucherDate: '15-Jul-2025',
      voucherTypeName: 'Journal',
      partyLedgerName: 'Bharat Heavy Plates Ltd',
      totalAmount: 12000,
      narration: 'Freight debit note adjustment and supplier ledger credit reconciliation',
      lineItemCount: 2
    },
    matchingFields: ['Party Ledger', 'Transaction Date', 'Total Amount', 'Narration', 'Voucher Type (Journal)'],
    differenceFields: ['Voucher Number (JRN/0055 vs JRN/0056)'],
    detailedComparisons: [
      { fieldName: 'Party Ledger', originalValue: 'Bharat Heavy Plates Ltd', duplicateValue: 'Bharat Heavy Plates Ltd', isMatched: true },
      { fieldName: 'Date', originalValue: '15-Jul-2025', duplicateValue: '15-Jul-2025', isMatched: true },
      { fieldName: 'Adjustment Amount', originalValue: '₹12,000.00', duplicateValue: '₹12,000.00', isMatched: true },
      { fieldName: 'Narration', originalValue: 'Freight debit note adjustment...', duplicateValue: 'Freight debit note adjustment...', isMatched: true },
      { fieldName: 'Voucher Number', originalValue: 'JRN/0055', duplicateValue: 'JRN/0056', isMatched: false, differenceNote: 'Consecutive journal numbers' }
    ],
    explanation: 'Potential exact duplicate adjustment: Journal vouchers JRN/0055 and JRN/0056 posted on 15-Jul-2025 post identical adjustments of ₹12,000.00 against vendor "Bharat Heavy Plates Ltd" with matching narration.',
    evidenceJson: '{"MatchTier": "Exact Duplicate", "Confidence": 100, "Amount": 12000.00, "Date": "2025-07-15", "Party": "Bharat Heavy Plates Ltd"}',
    reviewStatus: 'Pending'
  },

  // 6. Credit Note - Likely Duplicate
  {
    matchId: 'DUP-PAIR-006',
    tier: 'Likely Duplicate',
    confidenceScore: 88.0,
    strategyUsed: 'Strong Match: Same Customer + Same Linked Invoice + Same Rebate Value (₹5,000)',
    voucherCategory: 'Credit Note',
    originalTransaction: {
      voucherId: 'V-CN-01',
      voucherNumber: 'CN/002',
      referenceNumber: 'INV/25-26/101',
      voucherDate: '01-Aug-2025',
      voucherTypeName: 'Credit Note',
      partyLedgerName: 'Acme Steel Distribution',
      partyGstin: '27BBBCB3333E1Z8',
      totalAmount: 5000,
      narration: 'Rate difference discount credit note issued on bill INV/25-26/101',
      lineItemCount: 2
    },
    potentialDuplicate: {
      voucherId: 'V-CN-02',
      voucherNumber: 'CN/003',
      referenceNumber: 'INV/25-26/101',
      voucherDate: '02-Aug-2025',
      voucherTypeName: 'Credit Note',
      partyLedgerName: 'Acme Steel Distribution',
      partyGstin: '27BBBCB3333E1Z8',
      totalAmount: 5000,
      narration: 'Rate difference credit note re-entry for bill INV/25-26/101',
      lineItemCount: 2
    },
    matchingFields: ['Customer Party', 'Credit Amount', 'Linked Original Invoice (INV/25-26/101)', 'Voucher Type'],
    differenceFields: ['Voucher Date (+1 day)', 'Voucher Number (CN/002 vs CN/003)'],
    detailedComparisons: [
      { fieldName: 'Customer', originalValue: 'Acme Steel Distribution', duplicateValue: 'Acme Steel Distribution', isMatched: true },
      { fieldName: 'Credit Note Value', originalValue: '₹5,000.00', duplicateValue: '₹5,000.00', isMatched: true },
      { fieldName: 'Linked Tax Invoice', originalValue: 'INV/25-26/101', duplicateValue: 'INV/25-26/101', isMatched: true },
      { fieldName: 'Date', originalValue: '01-Aug-2025', duplicateValue: '02-Aug-2025', isMatched: false, differenceNote: '+1 day gap' }
    ],
    explanation: 'Potential likely duplicate credit note: Credit note CN/003 (₹5,000.00) issued against invoice INV/25-26/101 mirrors CN/002 issued 1 day earlier for the same customer "Acme Steel Distribution".',
    evidenceJson: '{"MatchTier": "Likely Duplicate", "Confidence": 88.0, "Party": "Acme Steel Distribution", "CreditAmount": 5000.00, "LinkedInvoice": "INV/25-26/101", "DateDeltaDays": 1}',
    reviewStatus: 'Pending'
  },

  // 7. Debit Note - Possible Duplicate
  {
    matchId: 'DUP-PAIR-007',
    tier: 'Possible Duplicate',
    confidenceScore: 68.5,
    strategyUsed: 'Possible Match: Same Supplier + Similar Quantity Rejection Note + 5 days window',
    voucherCategory: 'Debit Note',
    originalTransaction: {
      voucherId: 'V-DN-01',
      voucherNumber: 'DN/001',
      referenceNumber: 'PUR/2025/088',
      voucherDate: '10-Aug-2025',
      voucherTypeName: 'Debit Note',
      partyLedgerName: 'Bharat Heavy Plates Ltd',
      partyGstin: '27AAACB2222D1Z9',
      totalAmount: 8500,
      narration: 'Material defect quality rejection debit note on consignment PUR/2025/088',
      lineItemCount: 2
    },
    potentialDuplicate: {
      voucherId: 'V-DN-02',
      voucherNumber: 'DN/002',
      referenceNumber: 'PUR/2025/088',
      voucherDate: '15-Aug-2025',
      voucherTypeName: 'Debit Note',
      partyLedgerName: 'Bharat Heavy Plates Ltd',
      partyGstin: '27AAACB2222D1Z9',
      totalAmount: 8500,
      narration: 'Quality rejection debit note on consignment PUR/2025/088 re-entered',
      lineItemCount: 2
    },
    matchingFields: ['Supplier Party', 'Debit Value', 'Linked Purchase Ref', 'Voucher Type (Debit Note)'],
    differenceFields: ['Voucher Date (+5 days)', 'Voucher Number (DN/001 vs DN/002)'],
    detailedComparisons: [
      { fieldName: 'Supplier', originalValue: 'Bharat Heavy Plates Ltd', duplicateValue: 'Bharat Heavy Plates Ltd', isMatched: true },
      { fieldName: 'Debit Value', originalValue: '₹8,500.00', duplicateValue: '₹8,500.00', isMatched: true },
      { fieldName: 'Linked Purchase Ref', originalValue: 'PUR/2025/088', duplicateValue: 'PUR/2025/088', isMatched: true },
      { fieldName: 'Date', originalValue: '10-Aug-2025', duplicateValue: '15-Aug-2025', isMatched: false, differenceNote: '+5 days gap' }
    ],
    explanation: 'Potential duplicate debit note identified for auditor review: Debit Note DN/002 (₹8,500.00) issued against purchase bill PUR/2025/088 shares identical value and rejection reason as DN/001.',
    evidenceJson: '{"MatchTier": "Possible Duplicate", "Confidence": 68.5, "Party": "Bharat Heavy Plates Ltd", "Amount": 8500.00, "DaysBetween": 5}',
    reviewStatus: 'Pending'
  }
];
