export interface SecurityAuditLogItem {
  id: string;
  timestamp: string;
  category: 'AUTHENTICATION' | 'DATA_ACCESS' | 'WRITE_BACK' | 'BACKUP_RESTORE' | 'SECURITY_POLICY' | 'SYSTEM';
  action: string;
  user: string;
  details: string;
  ipAddress: string;
  integrityHash: string;
  status: 'SUCCESS' | 'BLOCKED' | 'WARNING' | 'FAILED';
}

export interface DatabaseBackupItem {
  id: string;
  timestamp: string;
  filename: string;
  sizeBytes: number;
  recordCount: number;
  triggerReason: 'Manual Backup' | 'Pre-Writeback Snapshot' | 'Pre-Restore Point' | 'Scheduled System Backup';
  sha256Hash: string;
}

// Utility: Prevent Path Traversal by checking canonical path
export function validateAndSanitizePath(inputPath: string, allowedDir: string = 'C:\\Exports'): { isValid: boolean; sanitizedPath: string; error?: string } {
  if (!inputPath || typeof inputPath !== 'string') {
    return { isValid: false, sanitizedPath: '', error: 'Invalid input path provided.' };
  }
  const trimmed = inputPath.trim();
  if (trimmed.includes('..') || /cmd\.exe|powershell|sh|bash/i.test(trimmed)) {
    return { isValid: false, sanitizedPath: '', error: 'Path traversal or command injection attempt blocked. Relative path navigators (..) are disallowed.' };
  }
  // Extract base filename to avoid drive letter duplication
  const cleanFileName = trimmed.split(/[/\\]/).pop() || 'export_data.dat';
  const sanitized = cleanFileName.replace(/[^a-zA-Z0-9_\-\.]/g, '_');
  return { isValid: true, sanitizedPath: `${allowedDir}\\${sanitized}` };
}

// Utility: Simulate SHA-256 Hash for Audit Trail Integrity
export function generateIntegrityHash(data: string): string {
  let hash = 0;
  for (let i = 0; i < data.length; i++) {
    const char = data.charCodeAt(i);
    hash = ((hash << 5) - hash) + char;
    hash |= 0;
  }
  const hex = Math.abs(hash).toString(16).padStart(8, '0');
  return `SHA256-${hex.toUpperCase()}-SEC2026`;
}

export const initialSecurityLogs: SecurityAuditLogItem[] = [
  {
    id: 'LOG-8801',
    timestamp: '25-Sep-2026 09:14:02 AM',
    category: 'SYSTEM',
    action: 'Local SQLite Engine Initialized',
    user: 'Senior Statutory Auditor',
    details: 'Database file: audit_assistant_data.db (WAL Mode active, 8MB RAM cap enforced)',
    ipAddress: '127.0.0.1 (Local Loopback)',
    integrityHash: 'SHA256-8A91F2C4-SEC2026',
    status: 'SUCCESS'
  },
  {
    id: 'LOG-8802',
    timestamp: '25-Sep-2026 09:14:05 AM',
    category: 'SECURITY_POLICY',
    action: 'Tally Mode Set to READ-ONLY',
    user: 'System Guard',
    details: 'Default TallyPrime communication initialized in READ-ONLY mode. Write-back disabled.',
    ipAddress: '127.0.0.1 (Local Loopback)',
    integrityHash: 'SHA256-4B12E9F0-SEC2026',
    status: 'SUCCESS'
  },
  {
    id: 'LOG-8803',
    timestamp: '25-Sep-2026 09:15:30 AM',
    category: 'AUTHENTICATION',
    action: 'Auditor PIN Set & App Lock Enabled',
    user: 'Senior Statutory Auditor',
    details: '4-digit auditor PIN configured. Auto-lock timeout set to 15 minutes.',
    ipAddress: '127.0.0.1 (Local Loopback)',
    integrityHash: 'SHA256-9C33D1A5-SEC2026',
    status: 'SUCCESS'
  },
  {
    id: 'LOG-8804',
    timestamp: '25-Sep-2026 09:30:00 AM',
    category: 'BACKUP_RESTORE',
    action: 'Automatic Pre-Sync Snapshot Created',
    user: 'System Guard',
    details: 'Backup created: SQLite_PreSync_20260925_093000.db.bak (14.2k Vouchers)',
    ipAddress: '127.0.0.1 (Local Loopback)',
    integrityHash: 'SHA256-1F88B4C2-SEC2026',
    status: 'SUCCESS'
  },
  {
    id: 'LOG-8805',
    timestamp: '25-Sep-2026 10:05:12 AM',
    category: 'WRITE_BACK',
    action: 'Tally Modification Attempt Intercepted',
    user: 'Senior Statutory Auditor',
    details: 'Write-back action "Push GST Tax Ledger Adjustment" blocked by Read-Only policy. Required explicit auditor PIN confirmation & backup.',
    ipAddress: '127.0.0.1 (Local Loopback)',
    integrityHash: 'SHA256-7E42A0D8-SEC2026',
    status: 'BLOCKED'
  }
];

export const initialBackups: DatabaseBackupItem[] = [
  {
    id: 'BAK-1001',
    timestamp: '25-Sep-2026 09:30:00 AM',
    filename: 'SQLite_PreSync_20260925_093000.db.bak',
    sizeBytes: 18450000,
    recordCount: 14280,
    triggerReason: 'Manual Backup',
    sha256Hash: 'SHA256-3A11C90B22F1E4A'
  },
  {
    id: 'BAK-1000',
    timestamp: '24-Sep-2026 06:00:00 PM',
    filename: 'SQLite_DailyAuto_20260924_180000.db.bak',
    sizeBytes: 18210000,
    recordCount: 14120,
    triggerReason: 'Scheduled System Backup',
    sha256Hash: 'SHA256-8F22A0091CD32B1'
  }
];
