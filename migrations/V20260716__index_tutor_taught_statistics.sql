-- Speed up lifetime tutor statistics calculated directly from class_sessions.
-- A disputed session is excluded by a NOT EXISTS lookup on disputes.
CREATE INDEX IF NOT EXISTS idx_class_sessions_taught_stats
    ON public.class_sessions (tutor_id, student_id)
    WHERE status = 'completed'
      AND is_settled = true;

CREATE INDEX IF NOT EXISTS idx_disputes_class_session_id
    ON public.disputes (class_session_id);
