-- Chống lưu TRÙNG note: 1 version canvas (cùng user + session + title) chỉ được lưu 1 note.
-- Trước đây thiếu ràng buộc này -> bấm "Lưu Note" nhiều lần (hoặc mở lại trang rồi bấm)
-- tạo ra nhiều bản trùng. App-level đã check (QuestionNoteService.CreateNoteAsync idempotent),
-- index này chặn nốt race 2 request đồng thời.

-- (1) Dọn dữ liệu trùng đã lỡ tạo: giữ bản CŨ NHẤT (created_at nhỏ nhất) cho mỗi
--     (user_id, source_session_id, title), xoá các bản còn lại. source_session_id NULL
--     coi như 1 nhóm riêng (dùng COALESCE để gom NULL về 1 giá trị so sánh).
DELETE FROM question_notes q
USING (
    SELECT note_id,
           ROW_NUMBER() OVER (
               PARTITION BY user_id, COALESCE(source_session_id, '00000000-0000-0000-0000-000000000000'::uuid), title
               ORDER BY created_at ASC, note_id ASC
           ) AS rn
    FROM question_notes
) dup
WHERE q.note_id = dup.note_id AND dup.rn > 1;

-- (2) Unique index. NULLS NOT DISTINCT (Postgres 15+) để source_session_id NULL cũng bị
--     coi là trùng nhau -> không lách được bằng note không gắn session.
CREATE UNIQUE INDEX IF NOT EXISTS uq_question_notes_user_session_title
    ON question_notes (user_id, source_session_id, title) NULLS NOT DISTINCT;
