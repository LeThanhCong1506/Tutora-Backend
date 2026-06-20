-- =====================================================
-- Migration: Add PayOS display fields to bookings
-- Date: 2026-06-11
-- Description:
--   - Cache PayOS transfer info (bin / account / description / qr / checkout url)
--     on the booking row when a payment link is created.
--   - Lets us reuse the SAME transfer info on every FE reload instead of
--     creating a brand-new PayOS link each time (which changed the QR/account).
-- =====================================================

BEGIN;

ALTER TABLE bookings
  ADD COLUMN IF NOT EXISTS payosbin            varchar(20),
  ADD COLUMN IF NOT EXISTS payosaccountnumber  varchar(50),
  ADD COLUMN IF NOT EXISTS payosaccountname    varchar(200),
  ADD COLUMN IF NOT EXISTS payosdescription    varchar(100),
  ADD COLUMN IF NOT EXISTS payoscheckouturl    text,
  ADD COLUMN IF NOT EXISTS payosqrcode         text;

COMMENT ON COLUMN bookings.payosqrcode IS
'Cached PayOS QR data string — reused so the transfer code stays stable across FE reloads.';

COMMIT;

-- =====================================================
-- Verification (run after migration)
-- =====================================================
-- SELECT column_name, data_type, character_maximum_length
-- FROM information_schema.columns
-- WHERE table_name = 'bookings' AND column_name LIKE 'payos%'
-- ORDER BY ordinal_position;
