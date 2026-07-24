-- Bảng question_notes: "Note" do HỌC SINH chủ đích tạo từ một lời giải trên Interactive
-- Solution Canvas (AI homework helper). KHÁC hoàn toàn chat_histories/chat_sessions:
--   • chat_sessions = luồng hội thoại (mọi lần hỏi đều lưu, tự động).
--   • question_notes = artifact học sinh CHỌN giữ lại để ôn/đánh dấu — có snapshot lời
--     giải, ghi chú cá nhân, ảnh đề, tag phân loại. Xoá note KHÔNG đụng lịch sử chat.
--
-- solution_steps lưu SNAPSHOT các bước canvas tại thời điểm tạo note (JSONB) -> mở lại
-- không cần gọi AI, không tốn lượt. Giữ nguyên shape SolutionStep (index/title/explanation/
-- formulas/goal/detailed/hints) mà FE render.
CREATE TABLE IF NOT EXISTS question_notes (
    note_id         UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id         VARCHAR(50) NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,

    -- Phiên chat nguồn (nếu note tạo từ 1 lời giải trong chat). SET NULL khi phiên bị xoá
    -- để note vẫn còn (note là bản độc lập, không phụ thuộc vòng đời chat).
    source_session_id UUID REFERENCES chat_sessions(session_id) ON DELETE SET NULL,

    title           VARCHAR(255) NOT NULL,
    -- Đề bài đã chuẩn hoá (text) để hiển thị + tìm kiếm.
    problem_text    TEXT,
    -- Ảnh đề gốc học sinh chụp/tải (public URL trên storage), tuỳ chọn.
    problem_image_url TEXT,

    -- Snapshot lời giải từng bước (mảng SolutionStep) — nguồn để render canvas khi mở note.
    solution_steps  JSONB NOT NULL DEFAULT '[]'::jsonb,
    -- Đáp án gọn (markdown) hiển thị đầu note.
    answer_summary  TEXT,

    -- Ghi chú cá nhân học sinh tự viết thêm (vd "dễ nhầm dấu chỗ này").
    personal_note   TEXT,

    -- Tag phân loại để lọc/tìm sau này.
    subject         VARCHAR(100),
    grade_level     INTEGER,
    chapter         VARCHAR(255),

    created_at      TIMESTAMP WITHOUT TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at      TIMESTAMP WITHOUT TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- Liệt kê note của 1 học sinh, mới nhất trước (màn hình "Note của tôi").
CREATE INDEX IF NOT EXISTS idx_question_notes_user_created
    ON question_notes (user_id, created_at DESC);

-- Lọc theo môn/lớp trong danh sách note.
CREATE INDEX IF NOT EXISTS idx_question_notes_user_subject
    ON question_notes (user_id, subject, grade_level);
