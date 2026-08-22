-- Ghi lại độ tin cậy của mỗi lời giải để đo được chất lượng RAG theo thời gian.
--
-- metadata (jsonb) đã chứa khối trust cho UI, nhưng truy vấn thống kê trên jsonb bất
-- tiện và không đánh index gọn. Ba cột riêng để trả lời được: RAG trúng bao nhiêu %,
-- similarity trung bình bao nhiêu, bao nhiêu lời giải bị code Python bác.

BEGIN;

ALTER TABLE public.chat_histories
    -- Cosine similarity của bài mẫu khớp nhất trong question bank. NULL = không trúng bank.
    ADD COLUMN IF NOT EXISTS rag_similarity   real  NULL,
    -- Câu gốc trong questions -> đối chiếu lời giải đã duyệt.
    ADD COLUMN IF NOT EXISTS rag_question_id  uuid  NULL,
    -- Kết quả chạy code kiểm tra đáp số. NULL = không kết luận được (bài hình/chứng minh).
    ADD COLUMN IF NOT EXISTS answer_verified  boolean NULL;

ALTER TABLE public.chat_histories
    DROP CONSTRAINT IF EXISTS chat_histories_rag_similarity_check;

ALTER TABLE public.chat_histories
    ADD CONSTRAINT chat_histories_rag_similarity_check
    CHECK (rag_similarity IS NULL OR (rag_similarity >= 0 AND rag_similarity <= 1));

-- Chỉ index hàng CÓ trúng bank: phần lớn tin nhắn là NULL, index đầy đủ chỉ tốn chỗ.
CREATE INDEX IF NOT EXISTS idx_chat_histories_rag_similarity
    ON public.chat_histories (rag_similarity DESC)
    WHERE rag_similarity IS NOT NULL;

COMMENT ON COLUMN public.chat_histories.rag_similarity IS
    'Cosine similarity bài mẫu khớp nhất trong question bank. >= 0.97 -> nhãn "đã kiểm chứng từ gia sư".';
COMMENT ON COLUMN public.chat_histories.answer_verified IS
    'Code Python đối chiếu đáp số: true=khớp, false=lệch, NULL=không chạy được. Đây là tính nhất quán nội bộ, KHÔNG phải bằng chứng đúng — cùng model sinh cả lời giải lẫn code kiểm tra.';

COMMIT;
