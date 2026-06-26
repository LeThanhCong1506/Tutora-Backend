ALTER TABLE public.tutor_profiles
    ADD COLUMN IF NOT EXISTS is_accepting_bookings boolean NOT NULL DEFAULT true;
