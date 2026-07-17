-- Contract phase for the payment_requests split.
-- Run only after V20260716 has completed and the reconciliation job has
-- resolved every legacy PayOS request/transaction. The guard aborts the whole
-- transaction rather than dropping the last copy of unresolved provider data.

BEGIN;

DO $$
DECLARE
    unresolved_count INTEGER;
BEGIN
    IF to_regclass('public.payment_requests') IS NULL THEN
        RAISE EXCEPTION
            'payment_requests does not exist; run V20260716 before the contract migration';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'bookings'
          AND column_name = 'payment_code'
    ) THEN
        RETURN;
    END IF;

    SELECT COUNT(*)
    INTO unresolved_count
    FROM public.bookings b
    WHERE (
        b.payment_code IS NOT NULL
        OR b.payos_bin IS NOT NULL
        OR b.payos_account_number IS NOT NULL
        OR b.payos_account_name IS NOT NULL
        OR b.payos_description IS NOT NULL
        OR b.payos_checkout_url IS NOT NULL
        OR b.payos_qr_code IS NOT NULL
    )
      AND NOT EXISTS (
          SELECT 1
          FROM public.payment_requests pr
          WHERE pr.booking_id = b.booking_id
            AND pr.provider = 'PayOS'
            AND pr.phase IN ('deposit', 'remaining')
            AND pr.status NOT IN ('UNKNOWN', 'REQUIRES_REVIEW')
            AND (b.payment_code IS NULL
                 OR pr.payment_link_id = b.payment_code)
            AND (b.payos_bin IS NULL
                 OR pr.destination_bank_bin = b.payos_bin)
            AND (b.payos_account_number IS NULL
                 OR pr.display_account_number = b.payos_account_number)
            AND (b.payos_account_name IS NULL
                 OR pr.display_account_name = b.payos_account_name)
            AND (b.payos_description IS NULL
                 OR pr.description = b.payos_description)
            AND (b.payos_checkout_url IS NULL
                 OR pr.checkout_url = b.payos_checkout_url)
            AND (b.payos_qr_code IS NULL
                 OR pr.qr_code = b.payos_qr_code)
      );

    IF unresolved_count > 0 THEN
        RAISE EXCEPTION
            'Refusing to drop booking PayOS cache: % booking row(s) are not fully reconciled',
            unresolved_count;
    END IF;

    SELECT COUNT(*)
    INTO unresolved_count
    FROM public.payment_transactions pt
    WHERE COALESCE(
        to_jsonb(pt) ->> 'payment_method',
        to_jsonb(pt) ->> 'channel'
    ) = 'PayOS'
      AND pt.booking_id IS NOT NULL
      AND pt.reconciliation_status IN (
          'Orphan', 'AmountMismatch', 'Unexpected'
      );

    IF unresolved_count > 0 THEN
        RAISE EXCEPTION
            'Refusing to drop booking PayOS cache: % PayOS transaction(s) still require review',
            unresolved_count;
    END IF;
END $$;

ALTER TABLE public.bookings
    DROP COLUMN IF EXISTS payment_code,
    DROP COLUMN IF EXISTS payos_bin,
    DROP COLUMN IF EXISTS payos_account_number,
    DROP COLUMN IF EXISTS payos_account_name,
    DROP COLUMN IF EXISTS payos_description,
    DROP COLUMN IF EXISTS payos_checkout_url,
    DROP COLUMN IF EXISTS payos_qr_code,
    DROP COLUMN IF EXISTS payos_legacy_reconciliation_version;

COMMIT;
