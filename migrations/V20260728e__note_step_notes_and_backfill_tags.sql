-- Migration: ghi chú theo từng bước giải + backfill tag chương cho note cũ
-- Date: 2026-07-28
-- Purpose: Note hiện chỉ là ảnh chụp tĩnh của lời giải — lưu vào rồi không ai quay lại.
--          Hai thiếu sót cụ thể:
--
--          1) Cột subject/grade_level/chapter đã có từ đầu nhưng FE KHÔNG BAO GIỜ điền
--             (đo thực tế: 1 note trong DB, cả 3 cột đều null). Không có tag thì không
--             nhóm được note theo chương -> không gộp được thành cheat sheet (hướng đã chốt).
--             Nay backfill từ student_topic_signals: note có source_session_id, mà tín hiệu
--             cũng lưu theo session -> suy ngược ra chương của note cũ.
--
--          2) Học sinh không ghi chú được vào TỪNG BƯỚC giải, chỉ có một ô ghi chú chung
--             tách rời bên trên. Chỗ hay nhầm nằm ở bước cụ thể, ghi chú chung không bám
--             được vào đó. Thêm step_notes dạng jsonb {"<chỉ số bước>": "ghi chú"}.
--
--          Vì sao jsonb chứ không phải bảng riêng: ghi chú bước luôn đọc/ghi CÙNG note,
--          không bao giờ query độc lập ("tìm mọi ghi chú bước chứa X" không phải nhu cầu).
--          Bảng riêng chỉ thêm join mà không đổi lại được gì.

ALTER TABLE public.question_notes
    ADD COLUMN IF NOT EXISTS step_notes jsonb NOT NULL DEFAULT '{}'::jsonb;

COMMENT ON COLUMN public.question_notes.step_notes IS
    'Ghi chú của học sinh cho từng bước giải: {"0":"hay quên điều kiện","2":"..."}. Khoá là index bước trong solution_steps.';

-- Backfill tag cho note cũ: lấy chương xuất hiện nhiều nhất trong phiên sinh ra note đó.
-- Chỉ đụng note CHƯA có chapter, nên chạy lại nhiều lần vẫn an toàn.
UPDATE public.question_notes n
   SET chapter = s.chapter_slug,
       grade_level = CASE WHEN s.grade ~ '^[0-9]+$' THEN s.grade::int ELSE NULL END,
       subject = COALESCE(n.subject, 'Toán Học'),
       updated_at = now()
  FROM (
        SELECT DISTINCT ON (session_id)
               session_id, chapter_slug, grade, count(*) AS c
          FROM public.student_topic_signals
         GROUP BY session_id, chapter_slug, grade
         ORDER BY session_id, count(*) DESC, max(created_at) DESC
       ) s
 WHERE n.source_session_id = s.session_id
   AND n.chapter IS NULL;

-- Lọc/nhóm note theo chương là đường đọc chính của trang /notes sau khi gộp cheat sheet.
CREATE INDEX IF NOT EXISTS idx_question_notes_user_chapter
    ON public.question_notes (user_id, chapter);
