-- Migration: cờ tắt gợi ý gia sư theo từng học sinh
-- Date: 2026-07-28
-- Purpose: Gợi ý gia sư dựa trên suy đoán ("hỏi nhiều bài cùng chương = đang vướng"),
--          mà suy đoán thì sai được. Học sinh phải tự tắt được dù chưa có booking —
--          nếu không, người bị gợi ý nhầm sẽ khó chịu.

ALTER TABLE public.student_profiles
    ADD COLUMN IF NOT EXISTS tutor_suggestion_enabled boolean NOT NULL DEFAULT true;
