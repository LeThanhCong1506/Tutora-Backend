-- Migration: Add bank info fields to tutorprofiles table
-- Date: 2026-02-09
-- Description: Add bank account fields for tutor withdrawal management

ALTER TABLE tutorprofiles
ADD COLUMN IF NOT EXISTS bankname VARCHAR(100),
ADD COLUMN IF NOT EXISTS bankaccountnumber VARCHAR(50),
ADD COLUMN IF NOT EXISTS bankaccountname VARCHAR(100),
ADD COLUMN IF NOT EXISTS isbankverified BOOLEAN DEFAULT FALSE,
ADD COLUMN IF NOT EXISTS bankchangedat TIMESTAMP WITHOUT TIME ZONE;

-- Create index for bank verification status
CREATE INDEX IF NOT EXISTS idx_tutorprofiles_bankverified ON tutorprofiles(isbankverified) WHERE isbankverified = TRUE;

COMMENT ON COLUMN tutorprofiles.bankname IS 'Name of the bank for tutor withdrawals';
COMMENT ON COLUMN tutorprofiles.bankaccountnumber IS 'Bank account number (8-19 digits)';
COMMENT ON COLUMN tutorprofiles.bankaccountname IS 'Account holder name (uppercase, no accents)';
COMMENT ON COLUMN tutorprofiles.isbankverified IS 'Whether the bank account has been verified by admin';
COMMENT ON COLUMN tutorprofiles.bankchangedat IS 'Timestamp when bank info was last updated';
