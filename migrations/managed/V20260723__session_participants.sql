-- Who was admitted into a live session, and when.
--
-- Agora NCS reports a channel `uid` that we cannot map back to an application user on its own:
-- the media channel is joined with a string user account, and the notification payload carries
-- Agora's internal numeric id. This table records the admission moment per user so the session
-- log can bind each Agora uid to a real participant.
--
-- Written best-effort during room admission — a failure here must never block joining a lesson.
CREATE TABLE IF NOT EXISTS session_participants (
    class_session_id  INTEGER NOT NULL,
    app_user_id       VARCHAR(50) NOT NULL,
    role              VARCHAR(20) NOT NULL,
    first_admitted_at TIMESTAMP WITHOUT TIME ZONE NOT NULL,
    last_admitted_at  TIMESTAMP WITHOUT TIME ZONE NOT NULL,
    admission_count   INTEGER NOT NULL DEFAULT 1,
    CONSTRAINT session_participants_pkey PRIMARY KEY (class_session_id, app_user_id)
);

CREATE INDEX IF NOT EXISTS idx_session_participants_session
    ON session_participants (class_session_id, first_admitted_at);
