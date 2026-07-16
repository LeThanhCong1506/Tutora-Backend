-- Replace legacy Zalo-derived internal identifiers without touching real emails/usernames.
-- The Zalo UID remains only in users.zalo_user_id, which is required for authentication.
BEGIN;

WITH legacy_zalo_users AS MATERIALIZED (
    SELECT
        user_id,
        'social_' || md5(user_id || random()::text || clock_timestamp()::text) AS opaque_alias
    FROM users
    WHERE zalo_user_id IS NOT NULL
      AND (
          email = 'zalo_' || zalo_user_id || '@tutora.vn'
          OR username = 'zalo_' || zalo_user_id
      )
)
UPDATE users AS u
SET
    email = CASE
        WHEN u.email = 'zalo_' || u.zalo_user_id || '@tutora.vn'
            THEN legacy.opaque_alias || '@tutora.invalid'
        ELSE u.email
    END,
    username = CASE
        WHEN u.username = 'zalo_' || u.zalo_user_id
            THEN legacy.opaque_alias
        ELSE u.username
    END
FROM legacy_zalo_users AS legacy
WHERE u.user_id = legacy.user_id;

COMMIT;

-- Verification: this query must return zero rows.
-- SELECT user_id, email, username
-- FROM users
-- WHERE zalo_user_id IS NOT NULL
--   AND (
--       email = 'zalo_' || zalo_user_id || '@tutora.vn'
--       OR username = 'zalo_' || zalo_user_id
--   );
