-- =====================================================
-- Migration: Drop OTP columns from users
-- Date: 2026-06-11
-- Description:
--   - OTP (email verification) and password-reset tokens are now stored in Redis
--     (IDistributedCache) with a 10-minute TTL, not on the users row.
--   - Remove the now-unused otpcode / otpexpiresat / otpattempts columns.
--   - Idempotent (IF EXISTS) — safe to run even if otpcode was already dropped manually.
-- =====================================================

BEGIN;

ALTER TABLE users
  DROP COLUMN IF EXISTS otpcode,
  DROP COLUMN IF EXISTS otpexpiresat,
  DROP COLUMN IF EXISTS otpattempts;

COMMIT;

-- =====================================================
-- Verification (run after migration) — should return 0 rows
-- =====================================================
-- SELECT column_name
-- FROM information_schema.columns
-- WHERE table_name = 'users' AND column_name LIKE 'otp%';
