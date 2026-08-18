-- Migration: đáp án trắc nghiệm cho question bank — "Ngân hàng kiểm tra" trong CMS.
-- Date: 2026-08-18
-- Purpose: tab "Ngân hàng kiểm tra" (CMS) cho staff/admin tự soạn câu hỏi trắc nghiệm —
--          dùng CHUNG bảng questions với "Ngân hàng câu hỏi" (AI Homework/RAG), chỉ thêm
--          các cột đáp án có cấu trúc. answer_format=NULL nghĩa là câu tự luận thường
--          (Ngân hàng câu hỏi), answer_format='mc' là câu trắc nghiệm soạn ở tab mới.
--
-- KHÔNG có cờ answer_verified như thiết kế cũ (spike backfill AI) — ở đây admin tự nhập
-- tay trực tiếp, nên chính review_status (pending_review -> published) đã là bước duyệt,
-- không cần cờ xác minh riêng.

ALTER TABLE public.questions
    ADD COLUMN IF NOT EXISTS answer_format varchar(10)
        CHECK (answer_format IS NULL OR answer_format IN ('mc', 'numeric', 'text')),
    ADD COLUMN IF NOT EXISTS answer_options jsonb,
    ADD COLUMN IF NOT EXISTS correct_answer text,
    ADD COLUMN IF NOT EXISTS explanation text;

COMMENT ON COLUMN public.questions.answer_format IS
    'NULL = câu tự luận (Ngân hàng câu hỏi) | mc (trắc nghiệm) | numeric | text — 2 loại sau
     chưa có UI ở CMS, để dành cho sau.';
COMMENT ON COLUMN public.questions.answer_options IS
    'Chỉ dùng khi answer_format=''mc''. Mảng jsonb [{"key":"A","text":"..."}, ...].';
COMMENT ON COLUMN public.questions.correct_answer IS
    'mc -> đúng 1 key trong answer_options (vd "A").';
COMMENT ON COLUMN public.questions.explanation IS
    'Giải thích vì sao đáp án đúng — ngắn hơn Solution (lời giải đầy đủ dùng cho câu tự luận).';
