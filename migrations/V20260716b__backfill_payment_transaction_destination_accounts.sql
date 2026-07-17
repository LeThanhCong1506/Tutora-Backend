-- Backfill immutable destination account metadata captured when the PayOS
-- payment link was created. Counter-account and virtual-account fields are
-- intentionally excluded because payment_requests is not an authoritative
-- source for those values.

BEGIN;

UPDATE public.payment_transactions AS pt
SET destination_account_bank_bin = COALESCE(
        NULLIF(BTRIM(pt.destination_account_bank_bin), ''),
        NULLIF(BTRIM(pr.destination_bank_bin), '')
    ),
    destination_account_bank_name = COALESCE(
        NULLIF(BTRIM(pt.destination_account_bank_name), ''),
        NULLIF(BTRIM(pr.destination_bank_name), '')
    ),
    destination_account_number = COALESCE(
        NULLIF(BTRIM(pt.destination_account_number), ''),
        NULLIF(BTRIM(pr.display_account_number), '')
    ),
    destination_account_name = COALESCE(
        NULLIF(BTRIM(pt.destination_account_name), ''),
        NULLIF(BTRIM(pr.display_account_name), '')
    )
FROM public.payment_requests AS pr
WHERE pt.payment_request_id = pr.payment_request_id
  AND pt.channel = 'PayOS'
  AND (
      (NULLIF(BTRIM(pt.destination_account_bank_bin), '') IS NULL
          AND NULLIF(BTRIM(pr.destination_bank_bin), '') IS NOT NULL)
      OR (NULLIF(BTRIM(pt.destination_account_bank_name), '') IS NULL
          AND NULLIF(BTRIM(pr.destination_bank_name), '') IS NOT NULL)
      OR (NULLIF(BTRIM(pt.destination_account_number), '') IS NULL
          AND NULLIF(BTRIM(pr.display_account_number), '') IS NOT NULL)
      OR (NULLIF(BTRIM(pt.destination_account_name), '') IS NULL
          AND NULLIF(BTRIM(pr.display_account_name), '') IS NOT NULL)
  );

COMMIT;
