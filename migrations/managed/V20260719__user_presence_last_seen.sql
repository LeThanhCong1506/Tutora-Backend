ALTER TABLE users
    ADD COLUMN IF NOT EXISTS last_seen_at TIMESTAMP WITHOUT TIME ZONE;

-- Preserve a useful initial value for existing accounts without changing the
-- meaning of last_login_at going forward.
UPDATE users
SET last_seen_at = last_login_at
WHERE last_seen_at IS NULL
  AND last_login_at IS NOT NULL;
