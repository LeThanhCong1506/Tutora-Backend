-- M4_T5_002: Create systemalerts table for admin notifications
-- Purpose: Track system-level alerts for low balance, failed payouts, etc.

CREATE TABLE IF NOT EXISTS systemalerts (
    alertid SERIAL PRIMARY KEY,
    type VARCHAR(50) NOT NULL,
    severity VARCHAR(20) NOT NULL,
    message TEXT NOT NULL,
    metadata JSONB,
    resolved BOOLEAN DEFAULT FALSE,
    resolvedat TIMESTAMP,
    createdat TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Add indexes for efficient querying
CREATE INDEX IF NOT EXISTS idx_systemalerts_type_resolved
ON systemalerts(type, resolved);

CREATE INDEX IF NOT EXISTS idx_systemalerts_severity_createdat
ON systemalerts(severity, createdat DESC);

CREATE INDEX IF NOT EXISTS idx_systemalerts_createdat
ON systemalerts(createdat DESC);

-- Add comments for documentation
COMMENT ON TABLE systemalerts IS 'System-level alerts for admin monitoring';
COMMENT ON COLUMN systemalerts.type IS 'Alert type: LOW_BALANCE, CRITICAL_BALANCE, PAYOUT_FAILED, etc.';
COMMENT ON COLUMN systemalerts.severity IS 'Alert severity: info, warning, critical';
COMMENT ON COLUMN systemalerts.message IS 'Human-readable alert message in English';
COMMENT ON COLUMN systemalerts.metadata IS 'Additional context data in JSON format';
COMMENT ON COLUMN systemalerts.resolved IS 'Whether the alert has been resolved';
COMMENT ON COLUMN systemalerts.resolvedat IS 'Timestamp when alert was resolved';
