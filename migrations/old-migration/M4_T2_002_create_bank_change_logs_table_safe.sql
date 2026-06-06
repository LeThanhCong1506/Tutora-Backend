-- =============================================
-- M4-T2: Bank Verification System - Phase 2 (Idempotent)
-- Migration: Create bank_change_logs table
-- Author: Backend Dev 1
-- Date: 2025-02-11 (Updated for safety)
-- =============================================

-- Create bank_change_logs table for audit trail
CREATE TABLE IF NOT EXISTS bank_change_logs (
    logid SERIAL PRIMARY KEY,
    tutorid VARCHAR(50) NOT NULL,
    oldbankname VARCHAR(100),
    oldbankaccountnumber VARCHAR(50),
    oldbankaccountname VARCHAR(200),
    newbankname VARCHAR(100),
    newbankaccountnumber VARCHAR(50),
    newbankaccountname VARCHAR(200),
    changedat TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ipaddress VARCHAR(45),
    useragent TEXT,
    reason VARCHAR(500),
    CONSTRAINT fk_bank_change_logs_tutor FOREIGN KEY (tutorid) REFERENCES users(userid) ON DELETE CASCADE
);

-- Add comments (safe to run multiple times)
COMMENT ON TABLE bank_change_logs IS 'Audit trail for bank account changes by tutors';
COMMENT ON COLUMN bank_change_logs.logid IS 'Primary key, auto-increment';
COMMENT ON COLUMN bank_change_logs.tutorid IS 'Foreign key to users table (VARCHAR to match users.userid)';
COMMENT ON COLUMN bank_change_logs.changedat IS 'Timestamp when bank info was changed';
COMMENT ON COLUMN bank_change_logs.ipaddress IS 'IP address of user making the change';
COMMENT ON COLUMN bank_change_logs.useragent IS 'User agent string from the request';
COMMENT ON COLUMN bank_change_logs.reason IS 'Optional reason for the change';

-- Create indexes for efficient queries
CREATE INDEX IF NOT EXISTS idx_bank_change_logs_tutorid ON bank_change_logs(tutorid);
CREATE INDEX IF NOT EXISTS idx_bank_change_logs_changedat ON bank_change_logs(changedat DESC);
CREATE INDEX IF NOT EXISTS idx_bank_change_logs_tutor_date ON bank_change_logs(tutorid, changedat DESC);

-- =============================================
-- Rollback script (if needed)
-- =============================================
-- DROP TABLE IF EXISTS bank_change_logs CASCADE;
