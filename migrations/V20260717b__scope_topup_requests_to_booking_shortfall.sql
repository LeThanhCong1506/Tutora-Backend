BEGIN;

ALTER TABLE public.topup_requests
    ADD COLUMN IF NOT EXISTS booking_id integer NULL,
    ADD COLUMN IF NOT EXISTS payment_phase varchar(20) NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'topup_requests_booking_id_fkey'
          AND conrelid = 'public.topup_requests'::regclass
    ) THEN
        ALTER TABLE public.topup_requests
            ADD CONSTRAINT topup_requests_booking_id_fkey
            FOREIGN KEY (booking_id)
            REFERENCES public.bookings(booking_id)
            ON DELETE RESTRICT;
    END IF;
END
$$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_topup_requests_booking_shortfall_scope'
          AND conrelid = 'public.topup_requests'::regclass
    ) THEN
        ALTER TABLE public.topup_requests
            ADD CONSTRAINT ck_topup_requests_booking_shortfall_scope
            CHECK (
                booking_id IS NOT NULL
                AND payment_phase IS NOT NULL
                AND payment_phase IN ('deposit', 'remaining')
                AND user_id IS NOT NULL
                AND amount IS NOT NULL
                AND amount > 0
            ) NOT VALID;
    END IF;
END
$$;

CREATE INDEX IF NOT EXISTS idx_topup_requests_booking_phase_user_status
    ON public.topup_requests (booking_id, payment_phase, user_id, status);

COMMENT ON COLUMN public.topup_requests.booking_id IS
    'Required for every new top-up. Identifies the booking whose wallet shortfall authorized this credit.';
COMMENT ON COLUMN public.topup_requests.payment_phase IS
    'Required for every new top-up. Must be deposit or remaining.';

COMMENT ON CONSTRAINT ck_topup_requests_booking_shortfall_scope
    ON public.topup_requests IS
    'Preserves legacy rows without validation while requiring every new top-up to be a positive booking payment shortfall.';

COMMIT;
