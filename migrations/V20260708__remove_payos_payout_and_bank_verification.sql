-- =====================================================
-- Migration: Remove PayOS automated payout + micro-deposit bank
--            verification, add manual staff-transfer completion note
-- Date: 2026-07-08
--
-- Background:
--   The automated PayOS tutor-payout mechanism (PayoutService,
--   PayoutJobHandler, ReconciliationJob) and the micro-deposit bank
--   account verification mechanism (BankVerificationService) are being
--   replaced with a manual flow: Staff/Admin performs the bank transfer
--   themselves outside the app, then marks the withdrawal request
--   completed in one step with a mandatory note describing what was done.
--
-- Contents:
--   Part A (additive, safe to run any time -- no backend coordination
--           needed until the app starts requiring the note on the
--           "mark complete" action):
--     A1. withdrawal_requests.completion_note -- staff note recorded
--         when manually marking a withdrawal request as completed.
--         Nullable at the DB level (same pattern as
--         disputes.resolution_note / tutor_profiles.rejection_note in
--         this schema) -- "required" is enforced by app-layer
--         validation on the "mark complete" action, not a DB constraint.
--   Part B (BREAKING -- run only together with the new backend code
--           deploy that removes every C# read/write of these columns):
--     B1. DROP TABLE bank_change_logs (bank-info change audit trail,
--         only ever written by BankVerificationService.RequestVerificationAsync)
--     B2. DROP tutor_profiles bank-verification columns:
--         bank_verify_code, bank_verify_status, bank_verify_requested,
--         bank_verified_at, bank_verify_attempts, is_bank_verified
--         (is_bank_verified dropped entirely -- the whole verification
--         concept is being removed, not just the app-layer gate check)
--     B3. DROP withdrawal_requests PayOS-tracking columns:
--         payos_transaction_id, payos_status, payos_response_code,
--         payos_error, retry_count, last_retry_at
--
-- Notes:
--   - Idempotent throughout (IF EXISTS) -- safe to re-run.
--   - Dropping a column automatically drops any index/constraint that
--     depends only on that column (Postgres behavior, no CASCADE
--     needed) -- this takes idx_tutorprofiles_bankverified,
--     idx_tutorprofiles_bankverifycode, idx_withdrawalrequests_payostransactionid
--     and idx_withdrawalrequests_status_retrycount with it.
--   - Part B is a hard coordination point with the C# side: the
--     BankChangeLog entity/DbSet, the six Bankverify*/Isbankverified
--     properties on Tutorprofile.cs, and the six
--     Payos*/Retrycount/Lastretryat properties on Withdrawalrequest.cs
--     (plus their fluent-API mappings in AGORADbContext.cs) must be
--     removed from the backend in the same deploy. EF Core issues
--     explicit column lists for every mapped property, so leaving even
--     one stale property on either entity breaks EVERY query against
--     tutor_profiles / withdrawal_requests (both core, heavily-used
--     tables) with a "column ... does not exist" error at runtime --
--     not just the payout/verification code paths.
--   - Processedby/Processedat on withdrawal_requests are reused as-is
--     for "who marked it complete and when" -- no new actor/timestamp
--     columns needed.
--   - Before running Part B against a real production database, verify
--     both tables are still safe to drop from (they were empty in the
--     last reviewed dump):
--       SELECT COUNT(*) FROM public.withdrawal_requests WHERE payos_transaction_id IS NOT NULL;
--       SELECT COUNT(*) FROM public.bank_change_logs;
--     If either returns > 0, export those rows (COPY ... TO) before
--     running Part B.
-- =====================================================


-- =====================================================
-- PART A1: withdrawal_requests.completion_note
-- =====================================================
BEGIN;

ALTER TABLE public.withdrawal_requests
    ADD COLUMN IF NOT EXISTS completion_note text NULL;

COMMENT ON COLUMN public.withdrawal_requests.completion_note IS
    'Ghi chu bat buoc (enforced o app layer) Staff/Admin nhap khi danh dau da chuyen khoan thu cong (vd: ma giao dich ngan hang, so tien, thoi gian chuyen).';

COMMIT;


-- =====================================================
-- PART B1: bank_change_logs (BREAKING)
-- Run this block ONLY when the new backend code (no BankChangeLog
-- entity/DbSet, no BankVerificationService) is deployed at the same time.
-- =====================================================
BEGIN;

DROP TABLE IF EXISTS public.bank_change_logs;

COMMIT;


-- =====================================================
-- PART B2: tutor_profiles bank-verification columns (BREAKING)
-- =====================================================
BEGIN;

ALTER TABLE public.tutor_profiles
    DROP COLUMN IF EXISTS bank_verify_code,
    DROP COLUMN IF EXISTS bank_verify_status,
    DROP COLUMN IF EXISTS bank_verify_requested,
    DROP COLUMN IF EXISTS bank_verified_at,
    DROP COLUMN IF EXISTS bank_verify_attempts,
    DROP COLUMN IF EXISTS is_bank_verified;

-- bank_name / bank_account_number / bank_account_name / bank_changed_at
-- are KEPT -- still used to store the tutor's payout bank details and
-- last-edited-at timestamp, independent of verification.

COMMIT;


-- =====================================================
-- PART B3: withdrawal_requests PayOS-tracking columns (BREAKING)
-- =====================================================
BEGIN;

ALTER TABLE public.withdrawal_requests
    DROP COLUMN IF EXISTS payos_transaction_id,
    DROP COLUMN IF EXISTS payos_status,
    DROP COLUMN IF EXISTS payos_response_code,
    DROP COLUMN IF EXISTS payos_error,
    DROP COLUMN IF EXISTS retry_count,
    DROP COLUMN IF EXISTS last_retry_at;

COMMIT;


-- =====================================================
-- Verification (run after migration) -- should return 0 rows
-- =====================================================
-- SELECT table_name FROM information_schema.tables
-- WHERE table_schema = 'public' AND table_name = 'bank_change_logs';
--
-- SELECT column_name FROM information_schema.columns
-- WHERE table_schema = 'public' AND table_name = 'tutor_profiles'
--   AND (column_name LIKE 'bank_verify%' OR column_name = 'is_bank_verified');
--
-- SELECT column_name FROM information_schema.columns
-- WHERE table_schema = 'public' AND table_name = 'withdrawal_requests'
--   AND (column_name LIKE 'payos_%' OR column_name IN ('retry_count', 'last_retry_at'));
