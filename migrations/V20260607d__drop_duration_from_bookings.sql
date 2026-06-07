BEGIN;

-- Drop the durationminutespersession column from bookings
ALTER TABLE public.bookings
    DROP COLUMN IF EXISTS durationminutespersession;

COMMIT;
