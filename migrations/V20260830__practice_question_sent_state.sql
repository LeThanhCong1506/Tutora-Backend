-- Migration: Chuyển trạng thái "đã gửi" từ BỘ ĐỀ xuống TỪNG CÂU HỎI
-- Date: 2026-08-30
--
-- VÌ SAO: gia sư tạo 1 lần ra nhiều câu, nhưng khi duyệt thì muốn gửi LẺ từng câu —
-- câu nào ưng gửi trước, câu chưa ưng thì sửa hoặc bỏ. Với trạng thái đặt ở
-- practice_sets, bấm gửi 1 câu là cả bộ (kể cả câu chưa duyệt) tới tay học sinh.
--
-- practice_sets.status/sent_at GIỮ LẠI làm trạng thái tổng hợp của bộ (sent khi đã
-- gửi ít nhất 1 câu) để không phá code/dữ liệu cũ, nhưng nguồn sự thật cho "học sinh
-- thấy câu nào" từ nay là practice_questions.sent_at.

ALTER TABLE public.practice_questions
    ADD COLUMN IF NOT EXISTS sent_at timestamp with time zone;

-- Dữ liệu cũ: bộ đã gửi -> mọi câu trong bộ coi như đã gửi, giữ đúng mốc thời gian.
UPDATE public.practice_questions q
SET    sent_at = s.sent_at
FROM   public.practice_sets s
WHERE  q.set_id = s.id
  AND  s.status = 'sent'
  AND  q.sent_at IS NULL;

-- Học sinh chỉ đọc câu đã gửi -> lọc theo cột này là chính.
CREATE INDEX IF NOT EXISTS idx_practice_questions_sent
    ON public.practice_questions (set_id, sent_at);
