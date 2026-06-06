-- =============================================
-- M4-T2: Bank Verification System - Phase 0
-- Migration: Add bank verification fields to tutorprofiles
-- Author: Backend Dev 1
-- Date: 2025-02-06
-- =============================================

-- Add bank verification fields to tutorprofiles table
ALTER TABLE tutorprofiles
ADD COLUMN bankverifycode VARCHAR(50) NULL,
ADD COLUMN bankverifyrequested TIMESTAMP NULL,
ADD COLUMN bankverifyattempts INT DEFAULT 0 NOT NULL,
ADD COLUMN bankverifiedat TIMESTAMP NULL;

-- Add comments for documentation
COMMENT ON COLUMN tutorprofiles.bankverifycode IS 'Verification code format: AGORA-XXXX for micro-deposit verification';
COMMENT ON COLUMN tutorprofiles.bankverifyrequested IS 'Timestamp when bank verification was last requested';
COMMENT ON COLUMN tutorprofiles.bankverifyattempts IS 'Number of failed verification attempts (max 5 before lockout)';
COMMENT ON COLUMN tutorprofiles.bankverifiedat IS 'Timestamp when bank account was successfully verified';

-- Create index for verification lookups
CREATE INDEX idx_tutorprofiles_bankverified ON tutorprofiles(isbankverified, bankverifiedat);
CREATE INDEX idx_tutorprofiles_bankverifycode ON tutorprofiles(bankverifycode) WHERE bankverifycode IS NOT NULL;

-- =============================================
-- Rollback script (if needed)
-- =============================================
-- ALTER TABLE tutorprofiles
-- DROP COLUMN IF EXISTS bankverifycode,
-- DROP COLUMN IF EXISTS bankverifyrequested,
-- DROP COLUMN IF EXISTS bankverifyattempts,
-- DROP COLUMN IF EXISTS bankverifiedat;
-- 
-- DROP INDEX IF EXISTS idx_tutorprofiles_bankverified;
-- DROP INDEX IF EXISTS idx_tutorprofiles_bankverifycode;
