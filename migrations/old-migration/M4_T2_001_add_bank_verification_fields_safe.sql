-- =============================================
-- M4-T2: Bank Verification System - Phase 1 (Idempotent)
-- Migration: Add bank verification fields to tutorprofiles
-- Author: Backend Dev 1
-- Date: 2025-02-11 (Updated for safety)
-- =============================================

-- Add bankverifycode column if not exists
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'tutorprofiles'
        AND column_name = 'bankverifycode'
    ) THEN
        ALTER TABLE tutorprofiles ADD COLUMN bankverifycode VARCHAR(50) NULL;
    END IF;
END $$;

-- Add bankverifyrequested column if not exists
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'tutorprofiles'
        AND column_name = 'bankverifyrequested'
    ) THEN
        ALTER TABLE tutorprofiles ADD COLUMN bankverifyrequested TIMESTAMP NULL;
    END IF;
END $$;

-- Add bankverifyattempts column if not exists
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'tutorprofiles'
        AND column_name = 'bankverifyattempts'
    ) THEN
        ALTER TABLE tutorprofiles ADD COLUMN bankverifyattempts INT DEFAULT 0 NOT NULL;
    END IF;
END $$;

-- Add bankverifiedat column if not exists
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'tutorprofiles'
        AND column_name = 'bankverifiedat'
    ) THEN
        ALTER TABLE tutorprofiles ADD COLUMN bankverifiedat TIMESTAMP NULL;
    END IF;
END $$;

-- Add comments (safe to run multiple times)
COMMENT ON COLUMN tutorprofiles.bankverifycode IS 'Verification code format: AGORA-XXXX for micro-deposit verification';
COMMENT ON COLUMN tutorprofiles.bankverifyrequested IS 'Timestamp when bank verification was last requested';
COMMENT ON COLUMN tutorprofiles.bankverifyattempts IS 'Number of failed verification attempts (max 5 before lockout)';
COMMENT ON COLUMN tutorprofiles.bankverifiedat IS 'Timestamp when bank account was successfully verified';

-- Create index if not exists
CREATE INDEX IF NOT EXISTS idx_tutorprofiles_bankverified ON tutorprofiles(isbankverified, bankverifiedat);

-- =============================================
-- Rollback script (if needed)
-- =============================================
-- ALTER TABLE tutorprofiles DROP COLUMN IF EXISTS bankverifycode;
-- ALTER TABLE tutorprofiles DROP COLUMN IF EXISTS bankverifyrequested;
-- ALTER TABLE tutorprofiles DROP COLUMN IF EXISTS bankverifyattempts;
-- ALTER TABLE tutorprofiles DROP COLUMN IF EXISTS bankverifiedat;
-- DROP INDEX IF EXISTS idx_tutorprofiles_bankverified;
