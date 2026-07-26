ALTER TABLE disputes
    ADD COLUMN IF NOT EXISTS no_show_confirmed_at TIMESTAMP WITHOUT TIME ZONE,
    ADD COLUMN IF NOT EXISTS no_show_confirmed_by VARCHAR(50);

CREATE INDEX IF NOT EXISTS idx_disputes_no_show_confirmation
    ON disputes (status, no_show_confirmed_at)
    WHERE dispute_type = 'no_show';
