BEGIN;

DROP INDEX IF EXISTS public.idx_payment_transactions_topup_request_id;

ALTER TABLE public.payment_transactions
    DROP CONSTRAINT IF EXISTS payment_transactions_topup_request_id_fkey,
    DROP COLUMN IF EXISTS topup_request_id;

COMMIT;
