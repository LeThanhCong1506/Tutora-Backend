-- Ghi thêm 1 bản audio-only song song với video mix hiện có (Agora hỗ trợ chạy nhiều recorder
-- cùng lúc trên 1 channel, xem docs.agora.io/en/cloud-recording/develop/composite-mode). Mục đích:
-- pipeline AI (tóm tắt/điền báo cáo) chỉ cần audio, nhưng trước giờ phải tải NGUYÊN video (700MB-2GB)
-- rồi ffmpeg tách audio ra — audio-only recording của Agora bỏ hẳn bước tải+transcode đó, chỉ cần
-- tải file audio nhỏ (~50-60MB/giờ) rồi forward thẳng lên Gemini. Video mix vẫn giữ nguyên vì còn
-- dùng cho xem lại buổi học + dispute review — 2 bản ghi độc lập, không thay thế nhau.

BEGIN;

ALTER TABLE class_sessions
    ADD COLUMN IF NOT EXISTS audio_recording_resource_id varchar(255),
    ADD COLUMN IF NOT EXISTS audio_recording_sid varchar(255),
    ADD COLUMN IF NOT EXISTS audio_recording_s3key varchar(500);

COMMENT ON COLUMN class_sessions.audio_recording_resource_id IS
    'resourceId của recorder audio-only (Agora acquire) — cần để gọi stop. Độc lập với recording_resource_id (video).';
COMMENT ON COLUMN class_sessions.audio_recording_sid IS
    'sid của recorder audio-only (Agora start) — cần để gọi stop.';
COMMENT ON COLUMN class_sessions.audio_recording_s3key IS
    'Object key file audio trên S3 (kho đệm) — RecordingRelayService tải về, forward thẳng lên Gemini rồi xoá; KHÔNG relay lên Drive (không có nhu cầu phát lại audio riêng).';

COMMIT;
