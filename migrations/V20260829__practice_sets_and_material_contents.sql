-- Migration: Bài tập nhanh trong buổi học (AI sinh từ tài liệu, gia sư duyệt rồi gửi)
-- Date: 2026-08-29
--
-- BỐI CẢNH: gia sư đang dạy, muốn tạo nhanh vài câu ôn tập từ tài liệu của khoá,
-- đọc lại/sửa rồi gửi cho học sinh làm ngay trong buổi. Học sinh làm từng câu,
-- trắc nghiệm biết đúng/sai tức thì, tự luận hỏi miệng gia sư; ôn lại được sau buổi.
--
-- VÌ SAO KHÔNG DÙNG BẢNG `questions` (question bank):
--   1. `questions` bị EMBED vào pool RAG của /solve. Đề gia sư mà nằm trong đó thì
--      buổi sau học sinh chụp chính câu đó gửi AI -> match ~1.0 -> AI đọc luôn lời
--      giải. Bài kiểm tra tự phá chính nó.
--   2. `questions` là pool TOÀN HỆ THỐNG do staff Tutora duyệt (review_status);
--      đề sinh nhanh giữa buổi chỉ do chính gia sư đó duyệt -> trộn vào là hạ chuẩn.
--   3. `questions` bắt buộc subject_id + grade_level_id NOT NULL, còn đề sinh từ
--      tài liệu buổi học thì hai field này thường mơ hồ.
-- Cùng lý do mà `assessment_questions` đã tách khỏi `questions` từ trước (xem
-- comment trong AssessmentQuestion.cs). Ở đây theo đúng tiền lệ đó.
--
-- VÌ SAO KHÔNG CÓ CỘT ĐIỂM: gia sư chấm bằng miệng ngay trong buổi. Trắc nghiệm
-- đối chiếu correct_answer là ra; tự luận thì trao đổi trực tiếp. Lưu điểm chỉ tạo
-- gánh nặng nhập liệu giữa lúc đang dạy.

-- ── 1. Nội dung đã trích xuất của tài liệu ───────────────────────────────────
-- 1 dòng / 1 tài liệu (khoá chính = material_id). Trích 1 lần lúc upload để lúc
-- gia sư bấm "Tạo câu hỏi" giữa buổi KHÔNG phải tải file về parse lại (mất vài
-- giây giữa lúc đang dạy là không chấp nhận được).
--
-- KHÔNG chunk, KHÔNG vector: gia sư CHỌN THẲNG tài liệu nên không có truy vấn nào
-- để mà retrieve — cả file được nhét vào prompt (Gemini context window đủ lớn).
-- Khi nào gặp giáo trình quá dày mới cắt/embed; lúc đó full_text đã sẵn ở đây.
CREATE TABLE IF NOT EXISTS public.learning_material_contents (
    material_id  integer PRIMARY KEY,
    -- Toàn văn, có chèn mốc "[trang N]" giữa các trang để AI trích dẫn được
    -- "câu này lấy ý từ trang 14" dù đọc cả file.
    full_text    text NOT NULL,
    page_count   integer,
    -- processing = đang trích | ready = dùng được | failed = file hỏng/không đọc được
    status       character varying(20) NOT NULL DEFAULT 'processing',
    error_message text,
    extracted_at timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT learning_material_contents_status_check
        CHECK (status IN ('processing', 'ready', 'failed')),
    CONSTRAINT learning_material_contents_material_fk
        FOREIGN KEY (material_id) REFERENCES public.learning_materials (material_id) ON DELETE CASCADE
);

-- ── 2. Bộ câu hỏi ────────────────────────────────────────────────────────────
-- Gắn BOOKING (không phải class_session) để học sinh mở lại được mọi bài tập của
-- cả khoá và gia sư tái dùng bộ đề cho buổi sau. class_session_id chỉ ghi nhận
-- bộ này sinh ra ở buổi nào.
CREATE TABLE IF NOT EXISTS public.practice_sets (
    id               uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    booking_id       integer NOT NULL,
    class_session_id integer,
    tutor_id         character varying(50) NOT NULL,
    title            character varying(255) NOT NULL,
    -- Prompt gia sư đã gõ — giữ lại để tạo lại bộ tương tự và để soi khi đề lệch.
    prompt           text,
    -- draft = mới sinh, chỉ gia sư thấy | sent = đã gửi, học sinh thấy
    status           character varying(20) NOT NULL DEFAULT 'draft',
    sent_at          timestamp with time zone,
    created_at       timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at       timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT practice_sets_status_check CHECK (status IN ('draft', 'sent')),
    CONSTRAINT practice_sets_booking_fk
        FOREIGN KEY (booking_id) REFERENCES public.bookings (booking_id) ON DELETE CASCADE,
    CONSTRAINT practice_sets_tutor_fk
        FOREIGN KEY (tutor_id) REFERENCES public.users (user_id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_practice_sets_booking
    ON public.practice_sets (booking_id, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_practice_sets_session
    ON public.practice_sets (class_session_id);

-- ── 3. Tài liệu nguồn của bộ (N-N) ───────────────────────────────────────────
-- Gia sư chọn được NHIỀU tài liệu ("slide chương 1" + "đề cương ôn tập") cho cùng
-- một lần sinh đề, nên phải là bảng nối chứ không phải cột material_id đơn.
CREATE TABLE IF NOT EXISTS public.practice_set_materials (
    set_id      uuid    NOT NULL,
    material_id integer NOT NULL,

    CONSTRAINT practice_set_materials_pkey PRIMARY KEY (set_id, material_id),
    CONSTRAINT practice_set_materials_set_fk
        FOREIGN KEY (set_id) REFERENCES public.practice_sets (id) ON DELETE CASCADE,
    CONSTRAINT practice_set_materials_material_fk
        FOREIGN KEY (material_id) REFERENCES public.learning_materials (material_id) ON DELETE CASCADE
);

-- ── 4. Câu hỏi ───────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS public.practice_questions (
    id                 uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    set_id             uuid NOT NULL,
    display_order      integer NOT NULL DEFAULT 0,
    -- mc = trắc nghiệm (đối chiếu correct_answer) | essay = tự luận (gia sư xem miệng)
    question_format    character varying(20) NOT NULL,
    -- Đề bài, LaTeX inline kẹp trong $...$ (FE render bằng KaTeX).
    content            text NOT NULL,
    -- [{"key":"A","text":"..."}] — chỉ mc. Cùng shape với questions.answer_options
    -- và assessment_questions.answer_options để FE dùng lại được component.
    answer_options     jsonb,
    -- Chỉ mc: đúng 1 key trong answer_options. essay để NULL.
    correct_answer     character varying(20),
    -- Giải thích ngắn, hiện cho học sinh SAU khi chọn đáp án.
    explanation        text,
    -- Nguồn trích: file nào, trang nào -> hiện "Trích từ Slide chương 1 — trang 12".
    -- Đây là giá trị cốt lõi của tính năng: gia sư hiện diện trong từng câu hỏi.
    source_material_id integer,
    source_page        integer,
    created_at         timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at         timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT practice_questions_format_check
        CHECK (question_format IN ('mc', 'essay')),
    -- Trắc nghiệm BẮT BUỘC có đáp án đúng, không thì chấm tự động vô nghĩa.
    CONSTRAINT practice_questions_mc_needs_answer
        CHECK (question_format <> 'mc' OR correct_answer IS NOT NULL),
    CONSTRAINT practice_questions_set_fk
        FOREIGN KEY (set_id) REFERENCES public.practice_sets (id) ON DELETE CASCADE,
    CONSTRAINT practice_questions_material_fk
        FOREIGN KEY (source_material_id) REFERENCES public.learning_materials (material_id) ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS idx_practice_questions_set
    ON public.practice_questions (set_id, display_order);

-- ── 5. Bài làm của học sinh ──────────────────────────────────────────────────
-- 1 dòng / (câu, học sinh) — làm lại thì GHI ĐÈ (unique bên dưới). MVP không giữ
-- lịch sử nhiều lần làm: bài ôn nhanh giữa buổi không cần bảng thành tích. Muốn
-- theo dõi tiến bộ thì bỏ unique và thêm attempt_no.
--
-- KHÔNG có cột điểm/nhận xét: xem ghi chú đầu file.
CREATE TABLE IF NOT EXISTS public.practice_answers (
    id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    question_id uuid NOT NULL,
    student_id  character varying(50) NOT NULL,
    -- mc: key đã chọn ("A"). essay: nguyên văn bài làm.
    answer      text NOT NULL,
    -- Chỉ mc mới có đúng/sai. essay luôn NULL (gia sư nhận xét miệng).
    is_correct  boolean,
    answered_at timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT practice_answers_unique UNIQUE (question_id, student_id),
    CONSTRAINT practice_answers_question_fk
        FOREIGN KEY (question_id) REFERENCES public.practice_questions (id) ON DELETE CASCADE,
    CONSTRAINT practice_answers_student_fk
        FOREIGN KEY (student_id) REFERENCES public.users (user_id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_practice_answers_student
    ON public.practice_answers (student_id, question_id);
