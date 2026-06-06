-- Migration: Add parentcode columns to users table
-- Date: 2026-03-04
-- Description: Add parentcode and parentcodeexpiresat to users table for parent invite code flow

ALTER TABLE users ADD COLUMN IF NOT EXISTS parentcode VARCHAR(10);
ALTER TABLE users ADD COLUMN IF NOT EXISTS parentcodeexpiresat TIMESTAMP WITHOUT TIME ZONE;

-- Unique index on parentcode (filtered, only non-null values)
CREATE UNIQUE INDEX IF NOT EXISTS users_parentcode_key ON users (parentcode) WHERE parentcode IS NOT NULL;
