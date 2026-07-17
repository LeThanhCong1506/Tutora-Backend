-- Rename the payment transaction's actual settlement method from the
-- ambiguous "channel" name to "payment_method".
--
-- payment_requests.provider remains unchanged: it identifies the provider
-- that created a checkout request, not the method that completed payment.

BEGIN;

DO $$
DECLARE
    has_channel BOOLEAN;
    has_payment_method BOOLEAN;
BEGIN
    SELECT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'payment_transactions'
          AND column_name = 'channel'
    ) INTO has_channel;

    SELECT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'payment_transactions'
          AND column_name = 'payment_method'
    ) INTO has_payment_method;

    IF has_channel AND NOT has_payment_method THEN
        ALTER TABLE public.payment_transactions
            RENAME COLUMN channel TO payment_method;
    ELSIF has_channel AND has_payment_method THEN
        RAISE EXCEPTION
            'payment_transactions contains both channel and payment_method; resolve the partial migration manually';
    ELSIF NOT has_payment_method THEN
        RAISE EXCEPTION
            'payment_transactions has neither channel nor payment_method';
    END IF;
END $$;

DO $$
BEGIN
    IF to_regclass(
        'public.uq_payment_transactions_channel_provider_transaction_id'
    ) IS NOT NULL
       AND to_regclass(
        'public.uq_payment_transactions_payment_method_provider_transaction_id'
    ) IS NULL THEN
        ALTER INDEX
            public.uq_payment_transactions_channel_provider_transaction_id
            RENAME TO
            uq_payment_transactions_payment_method_provider_transaction_id;
    END IF;

    IF to_regclass(
        'public.uq_payment_transactions_channel_capture_fingerprint'
    ) IS NOT NULL
       AND to_regclass(
        'public.uq_payment_transactions_payment_method_capture_fingerprint'
    ) IS NULL THEN
        ALTER INDEX
            public.uq_payment_transactions_channel_capture_fingerprint
            RENAME TO
            uq_payment_transactions_payment_method_capture_fingerprint;
    END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS
    uq_payment_transactions_payment_method_provider_transaction_id
    ON public.payment_transactions(payment_method, provider_transaction_id)
    WHERE provider_transaction_id IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS
    uq_payment_transactions_payment_method_capture_fingerprint
    ON public.payment_transactions(payment_method, capture_fingerprint)
    WHERE provider_transaction_id IS NULL
      AND capture_fingerprint IS NOT NULL;

DROP INDEX IF EXISTS
    public.uq_payment_transactions_channel_provider_transaction_id;
DROP INDEX IF EXISTS
    public.uq_payment_transactions_channel_capture_fingerprint;

COMMENT ON COLUMN public.payment_transactions.payment_method IS
    'Actual method that recorded the payment, for example PayOS, Wallet, or Manual.';

COMMIT;
