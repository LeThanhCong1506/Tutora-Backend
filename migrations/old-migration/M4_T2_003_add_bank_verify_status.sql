-- M4_T2_003: Add bankverifystatus field for micro-deposit flow
-- Migration date: 2025-01-XX
-- Description: Add status tracking field for bank verification process

-- Add bankverifystatus column
ALTER TABLE tutorprofiles
ADD COLUMN IF NOT EXISTS bankverifystatus VARCHAR(20) DEFAULT 'none' NOT NULL;

-- Sync existing data: if already verified, set status to 'verified'
UPDATE tutorprofiles
SET bankverifystatus = 'verified'
WHERE isbankverified = true;

-- Add comment for documentation
COMMENT ON COLUMN tutorprofiles.bankverifystatus IS 'Bank verification status: none, pending_deposit, ready_to_confirm, verified, failed';
