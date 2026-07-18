BEGIN;

ALTER TABLE public.withdrawal_requests
    ADD COLUMN IF NOT EXISTS claimed_by varchar(50) NULL,
    ADD COLUMN IF NOT EXISTS claimed_at timestamp without time zone NULL,
    ADD COLUMN IF NOT EXISTS rejection_reason text NULL;

ALTER TABLE public.payment_transactions
    ADD COLUMN IF NOT EXISTS proof_image_path text NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'withdrawal_requests_claimed_by_fkey'
          AND conrelid = 'public.withdrawal_requests'::regclass
    ) THEN
        ALTER TABLE public.withdrawal_requests
            ADD CONSTRAINT withdrawal_requests_claimed_by_fkey
            FOREIGN KEY (claimed_by)
            REFERENCES public.users(user_id)
            ON DELETE SET NULL;
    END IF;
END
$$;

CREATE INDEX IF NOT EXISTS idx_withdrawal_requests_status_claimed_by
    ON public.withdrawal_requests (status, claimed_by, claimed_at);

-- Close legacy rows that already have an immutable successful payout ledger
-- entry; they were paid even if the old workflow did not advance the status.
UPDATE public.withdrawal_requests w
SET status = 'completed',
    processed_at = COALESCE(
        w.processed_at,
        (
            SELECT MAX(pt.paid_at)
            FROM public.payment_transactions pt
            WHERE pt.withdrawal_id = w.withdrawal_id
              AND pt.purpose = 'Withdrawal'
              AND pt.status = 'Succeeded'
        )
    ),
    completion_note = COALESCE(
        w.completion_note,
        'Migrated from legacy successful payout transaction.'
    ),
    claimed_by = NULL,
    claimed_at = NULL
WHERE w.status = 'approved'
  AND EXISTS (
      SELECT 1
      FROM public.payment_transactions pt
      WHERE pt.withdrawal_id = w.withdrawal_id
        AND pt.purpose = 'Withdrawal'
        AND pt.status = 'Succeeded'
  );

-- Requeue unpaid legacy rows so a staff/admin must explicitly claim them under
-- the manual workflow.
UPDATE public.withdrawal_requests w
SET status = 'pending_review',
    claimed_by = NULL,
    claimed_at = NULL
WHERE w.status = 'approved'
  AND NOT EXISTS (
      SELECT 1
      FROM public.payment_transactions pt
      WHERE pt.withdrawal_id = w.withdrawal_id
        AND pt.purpose = 'Withdrawal'
        AND pt.status = 'Succeeded'
  );

COMMENT ON COLUMN public.withdrawal_requests.claimed_by IS
    'Staff/admin who exclusively claimed this payout for manual bank transfer.';
COMMENT ON COLUMN public.withdrawal_requests.claimed_at IS
    'UTC time at which the payout was claimed for manual processing.';
COMMENT ON COLUMN public.withdrawal_requests.rejection_reason IS
    'Persisted reason shown in payout history when a withdrawal is rejected.';
COMMENT ON COLUMN public.payment_transactions.proof_image_path IS
    'Private storage public ID for the manual bank-transfer receipt image.';

COMMIT;
