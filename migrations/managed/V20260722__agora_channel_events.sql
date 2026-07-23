-- Append-only capture of Agora Notification Center Service (NCS) RTC events.
-- There is intentionally no foreign key to class_sessions: evidence must survive session deletion.
CREATE TABLE IF NOT EXISTS agora_channel_events (
    event_id         BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    notice_id        VARCHAR(64) NOT NULL,
    class_session_id INTEGER,
    event_type       SMALLINT NOT NULL,
    event_at         TIMESTAMP WITHOUT TIME ZONE NOT NULL,
    received_at      TIMESTAMP WITHOUT TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    payload          JSONB NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_agora_events_notice
    ON agora_channel_events (notice_id);

CREATE INDEX IF NOT EXISTS idx_agora_events_session
    ON agora_channel_events (class_session_id, event_at);
