-- Chống race tạo trùng phiên chat tóm tắt video: job tóm tắt tự seed phiên chat VÀ học sinh hỏi câu đầu
-- tiên (AskFollowUpAsync cũng tự seed nếu chưa có) có thể cùng lúc thấy "chưa có phiên nào" rồi cùng
-- insert — trước giờ không có unique index nên cả 2 insert đều thành công, tạo ra 2 phiên độc lập cho
-- cùng (user, buổi học). Hệ quả: bản chép lời/tổng hợp chuỗi (job riêng, chạy xong sau) có thể bị ghi
-- vào phiên "thua" mà FE không hiển thị, khiến chat trông như không có ngữ cảnh dù DB có đủ dữ liệu.
-- Xem ClassSessionVideoAiService.EnsureVideoSummaryChatSessionAsync.

BEGIN;

-- Dọn dữ liệu trùng hiện có trước khi thêm unique index — giữ phiên có updated_at MỚI NHẤT (tie-break
-- theo session_id để có thứ tự chặt chẽ, tránh trường hợp updated_at bằng nhau tuyệt đối). Cascade tự
-- xoá luôn chat_histories của phiên bị xoá (fk_chat_histories_session ON DELETE CASCADE).
DELETE FROM chat_sessions a
USING chat_sessions b
WHERE a.user_id = b.user_id
  AND a.session_type = b.session_type
  AND a.class_session_id = b.class_session_id
  AND a.class_session_id IS NOT NULL
  AND (a.updated_at, a.session_id) < (b.updated_at, b.session_id);

CREATE UNIQUE INDEX IF NOT EXISTS ux_chat_sessions_user_type_class_session
    ON chat_sessions (user_id, session_type, class_session_id)
    WHERE class_session_id IS NOT NULL;

COMMIT;
