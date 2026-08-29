-- File audio-only (recorder song song với video, xem V20260829__class_sessions_audio_recording.sql)
-- giờ được relay lên Google Drive lưu vĩnh viễn giống hệt video, thay vì chỉ forward tạm lên Gemini
-- rồi xoá. Cột này lưu link Drive tương ứng — song song với recording_url của video.

BEGIN;

ALTER TABLE class_sessions
    ADD COLUMN IF NOT EXISTS audio_recording_url text;

COMMENT ON COLUMN class_sessions.audio_recording_url IS
    'Link Drive của file audio-only sau khi relay xong (giống recording_url nhưng cho audio) — vĩnh viễn, không bị xoá.';

COMMIT;
