-- Migration: ai_usage_events — nhật ký token/chi phí mỗi lời gọi Gemini
-- Date: 2026-08-31
--
-- VÌ SAO: Google KHÔNG có API đọc usage/spend theo API key (AI Studio chỉ có
-- dashboard nội bộ, không endpoint). Số liệu duy nhất lấy được là `usage_metadata`
-- trả kèm TỪNG response — mà mọi lời gọi Gemini đều nằm ở tutora-ai. Nên tutora-ai
-- đọc usage rồi POST về đây; bảng này là nguồn sự thật cho trang admin "Chi phí AI".
--
-- KHÁC với ai_usage_monthly (đếm LƯỢT dùng của user, phục vụ hạn mức credit):
-- bảng này đo TOKEN + TIỀN TRẢ GOOGLE, phục vụ quan sát vận hành.

CREATE TABLE IF NOT EXISTS public.ai_usage_events (
    id              bigserial PRIMARY KEY,

    -- Nhãn tính năng do tutora-ai truyền vào ('solve', 'classroom_generate',
    -- 'zalo_agent'...). KHÔNG suy ra được từ model vì nhiều feature dùng chung
    -- gemini-2.5-flash.
    feature         text NOT NULL,
    model           text NOT NULL,

    -- Tên trường bám theo usage_metadata của google-genai.
    -- cached_tokens: phần prompt được cache, Google tính giá RẺ hơn input thường.
    -- thoughts_tokens: token "thinking", Google tính giá NHƯ output.
    prompt_tokens   integer NOT NULL DEFAULT 0,
    output_tokens   integer NOT NULL DEFAULT 0,
    thoughts_tokens integer NOT NULL DEFAULT 0,
    cached_tokens   integer NOT NULL DEFAULT 0,
    total_tokens    integer NOT NULL DEFAULT 0,

    -- SDK chỉ trả token, không trả tiền -> tutora-ai tự tính từ bảng giá của nó.
    -- Lưu sẵn giá trị đã tính để sau này Google đổi giá không làm sai số liệu cũ.
    cost_usd        numeric(12, 6) NOT NULL DEFAULT 0,

    latency_ms      integer,
    -- false = lời gọi lỗi; vẫn ghi để đếm tỉ lệ hỏng (thường token = 0).
    success         boolean NOT NULL DEFAULT true,
    error           text,

    created_at      timestamp with time zone NOT NULL DEFAULT now()
);

-- Dashboard luôn lọc khoảng thời gian trước, rồi mới gom theo model/feature.
CREATE INDEX IF NOT EXISTS idx_ai_usage_events_created
    ON public.ai_usage_events (created_at DESC);
CREATE INDEX IF NOT EXISTS idx_ai_usage_events_feature_created
    ON public.ai_usage_events (feature, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_ai_usage_events_model_created
    ON public.ai_usage_events (model, created_at DESC);
