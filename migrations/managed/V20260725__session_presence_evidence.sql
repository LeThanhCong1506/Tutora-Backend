-- Two evidence sources that Agora's channel notifications cannot provide on their own.
--
-- 1. session_presence_intervals — the heartbeat chain.
--    Agora tells us a user is inside the channel; it does not tell us whether they are still at
--    the desk. The classroom client beats every ~20s and reports whether its microphone/camera
--    are publishing and whether the tab is idle, which is what distinguishes "taught the lesson"
--    from "opened the room and walked away".
--
--    Beats are collapsed into intervals instead of stored one row per beat: a 90-minute lesson
--    would otherwise cost ~270 rows per participant, and the useful shape is the interval anyway.
--    beat_count keeps the chain auditable, and a gap between two intervals is exactly the
--    evidence that the client stopped reporting.
--
--    reported_beats counts beats that actually carried the activity fields. An older client that
--    sends none must read as "we do not know", never as "was idle" — the same rule the rest of
--    the session log follows.
--
-- 2. session_participant_devices — one row per (session, user, network, device).
--    Account sharing and device juggling show up here: two rows for the same user in one lesson
--    means the account was admitted from two places. Kept as evidence, never as an automatic
--    verdict, because carriers rotate mobile IPs mid-lesson.
--
-- Both tables are written best-effort during a lesson and carry no foreign key to class_sessions,
-- for the same reason agora_channel_events does not: evidence must outlive the row it describes.

CREATE TABLE IF NOT EXISTS session_presence_intervals (
    interval_id      BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    class_session_id INTEGER NOT NULL,
    app_user_id      VARCHAR(50) NOT NULL,
    role             VARCHAR(20) NOT NULL,
    started_at       TIMESTAMP WITHOUT TIME ZONE NOT NULL,
    last_beat_at     TIMESTAMP WITHOUT TIME ZONE NOT NULL,
    beat_count       INTEGER NOT NULL DEFAULT 1,
    -- Client-reported activity, counted over the beats that carried it.
    reported_beats   INTEGER NOT NULL DEFAULT 0,
    mic_on_beats     INTEGER NOT NULL DEFAULT 0,
    camera_on_beats  INTEGER NOT NULL DEFAULT 0,
    idle_beats       INTEGER NOT NULL DEFAULT 0,
    -- NULL while the interval is still receiving beats; 'leave' when the client said goodbye,
    -- 'gap' when beats stopped for longer than the presence window and a new interval opened.
    closed_reason    VARCHAR(20)
);

CREATE INDEX IF NOT EXISTS idx_presence_intervals_session
    ON session_presence_intervals (class_session_id, app_user_id, started_at);

CREATE TABLE IF NOT EXISTS session_participant_devices (
    device_row_id    BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    class_session_id INTEGER NOT NULL,
    app_user_id      VARCHAR(50) NOT NULL,
    role             VARCHAR(20) NOT NULL,
    -- Empty string rather than NULL: these columns form a unique key, and PostgreSQL treats every
    -- NULL as distinct, which would let the same device insert a new row on every admission.
    -- Behind a reverse proxy this is the proxy address unless SessionEvidence:TrustForwardedFor
    -- is enabled — a wrong IP would be worse than none, so trusting the header is opt-in.
    ip_address       VARCHAR(45) NOT NULL DEFAULT '',
    device_id        VARCHAR(100) NOT NULL DEFAULT '',
    device_label     VARCHAR(120) NOT NULL DEFAULT '',
    user_agent       VARCHAR(400) NOT NULL DEFAULT '',
    first_seen_at    TIMESTAMP WITHOUT TIME ZONE NOT NULL,
    last_seen_at     TIMESTAMP WITHOUT TIME ZONE NOT NULL,
    admission_count  INTEGER NOT NULL DEFAULT 1
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_participant_devices_identity
    ON session_participant_devices (class_session_id, app_user_id, ip_address, device_id);

-- Supports the cross-session question "which accounts came from this address".
CREATE INDEX IF NOT EXISTS idx_participant_devices_ip
    ON session_participant_devices (ip_address, last_seen_at);
