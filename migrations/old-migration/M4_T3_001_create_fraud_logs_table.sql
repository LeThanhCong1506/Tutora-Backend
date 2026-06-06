-- M4-T3: Fraud Detection Rules Engine
-- Migration 001: Create fraud_logs table
-- Purpose: Audit trail for all fraud rule executions

CREATE TABLE IF NOT EXISTS fraud_logs (
    logid SERIAL PRIMARY KEY,
    tutorid VARCHAR(50) NOT NULL,
    withdrawalrequestid INTEGER,
    rulename VARCHAR(100) NOT NULL,
    passed BOOLEAN NOT NULL DEFAULT false,
    isflagged BOOLEAN NOT NULL DEFAULT false,
    message TEXT,
    metadata JSONB,
    checkedat TIMESTAMP WITHOUT TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT fk_fraud_logs_tutor FOREIGN KEY (tutorid)
        REFERENCES tutorprofiles(tutorid) ON DELETE CASCADE,
    CONSTRAINT fk_fraud_logs_withdrawal FOREIGN KEY (withdrawalrequestid)
        REFERENCES withdrawalrequests(withdrawalid) ON DELETE SET NULL
);

-- Indexes for performance optimization
CREATE INDEX idx_fraud_logs_tutorid ON fraud_logs(tutorid);
CREATE INDEX idx_fraud_logs_tutorid_checkedat ON fraud_logs(tutorid, checkedat DESC);
CREATE INDEX idx_fraud_logs_withdrawalid ON fraud_logs(withdrawalrequestid);
CREATE INDEX idx_fraud_logs_rulename ON fraud_logs(rulename);
CREATE INDEX idx_fraud_logs_checkedat ON fraud_logs(checkedat DESC);

-- Partial indexes for failed/flagged cases (faster queries for fraud analysis)
CREATE INDEX idx_fraud_logs_failed ON fraud_logs(tutorid, checkedat DESC)
    WHERE passed = false;
CREATE INDEX idx_fraud_logs_flagged ON fraud_logs(tutorid, checkedat DESC)
    WHERE isflagged = true;

-- GIN index for JSONB metadata queries
CREATE INDEX idx_fraud_logs_metadata ON fraud_logs USING GIN(metadata);

COMMENT ON TABLE fraud_logs IS 'Audit trail for fraud detection rule executions';
COMMENT ON COLUMN fraud_logs.rulename IS 'Name of the fraud rule (e.g., VELOCITY_CHECK, AMOUNT_LIMITS)';
COMMENT ON COLUMN fraud_logs.passed IS 'Whether the rule passed (true) or failed/blocked (false)';
COMMENT ON COLUMN fraud_logs.isflagged IS 'Whether the rule flagged suspicious activity (warning, not blocking)';
COMMENT ON COLUMN fraud_logs.metadata IS 'Additional context (e.g., current counts, thresholds, IP addresses)';
