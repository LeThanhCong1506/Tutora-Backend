-- Bảng lưu mẫu điểm cảm xúc / độ tập trung của học viên trong buổi học (append-only).
-- Dữ liệu do MÁY HỌC VIÊN tự phân tích cục bộ trong trình duyệt (MediaPipe + FER+) — ảnh khuôn
-- mặt KHÔNG rời máy học viên; chỉ điểm số/nhãn đã tổng hợp mới được ghi ở đây để làm báo cáo.
-- Không lưu ảnh, không lưu embedding khuôn mặt.
CREATE TABLE IF NOT EXISTS session_engagement_samples (
    sample_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    class_session_id INTEGER NOT NULL,
    student_user_id VARCHAR(50) NOT NULL,
    emotion VARCHAR(20),
    engagement_score DOUBLE PRECISION NOT NULL DEFAULT 0,
    drowsy BOOLEAN NOT NULL DEFAULT FALSE,
    alert_reason VARCHAR(20),
    sampled_at TIMESTAMP WITHOUT TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT fk_session_engagement_samples_class_session
        FOREIGN KEY (class_session_id) REFERENCES class_sessions(class_session_id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_session_engagement_samples_session_time
    ON session_engagement_samples (class_session_id, sampled_at);
