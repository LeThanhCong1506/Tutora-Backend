# PayOS booking-cache contract migration

Run `V20260716b__drop_booking_payos_cache.sql` only after the application
reconciliation job has resolved all payment alerts created by
`V20260716__payment_requests_and_transaction_reconciliation.sql`.

The SQL is guarded and rolls back if a booking cache row has not been copied to
`payment_requests`, or if a PayOS booking transaction still needs review.
