-- Migration: Add parent_phone to student_profiles table
-- Date: 2026-07-15
-- Purpose: Học sinh có thể có SĐT phụ huynh (tùy chọn) để hệ thống gửi ZNS theo dõi.
--          - Học sinh tự đăng ký: tự nhập SĐT phụ huynh.
--          - Tài khoản do phụ huynh tạo: tự động điền từ SĐT của phụ huynh.

ALTER TABLE public.student_profiles
    ADD COLUMN IF NOT EXISTS parent_phone character varying(20);
