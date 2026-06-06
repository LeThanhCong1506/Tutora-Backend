-- Migration: Add soft-delete fields to users table
-- Date: 2026-06-02
-- Purpose: Distinguish user self-delete (isdeleted=true) from admin ban (status=0)

ALTER TABLE public.users
    ADD COLUMN IF NOT EXISTS isdeleted boolean DEFAULT false,
    ADD COLUMN IF NOT EXISTS deletedat timestamp without time zone;


ALTER TABLE public.users RENAME COLUMN isdeleted TO isdeactivated;
ALTER TABLE public.users RENAME COLUMN deletedat TO deactivatedat;