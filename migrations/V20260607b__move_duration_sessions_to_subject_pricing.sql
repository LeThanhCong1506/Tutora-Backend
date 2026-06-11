BEGIN;

-- 1. Add durationminutespersession and sessionsperweek to tutorsubjectgradeprices
ALTER TABLE public.tutorsubjectgradeprices
    ADD COLUMN IF NOT EXISTS durationminutespersession integer NOT NULL DEFAULT 60;

ALTER TABLE public.tutorsubjectgradeprices
    ADD COLUMN IF NOT EXISTS sessionsperweek integer NOT NULL DEFAULT 1;

-- Add check constraints
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_tutorsubjectgradeprices_duration_positive'
          AND conrelid = 'public.tutorsubjectgradeprices'::regclass
    ) THEN
        ALTER TABLE public.tutorsubjectgradeprices
            ADD CONSTRAINT ck_tutorsubjectgradeprices_duration_positive
            CHECK (durationminutespersession > 0);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_tutorsubjectgradeprices_sessionsperweek_positive'
          AND conrelid = 'public.tutorsubjectgradeprices'::regclass
    ) THEN
        ALTER TABLE public.tutorsubjectgradeprices
            ADD CONSTRAINT ck_tutorsubjectgradeprices_sessionsperweek_positive
            CHECK (sessionsperweek > 0);
    END IF;
END
$$;

-- 2. Drop description and durationminutespersession from tutorpackages
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'tutorpackages'
          AND column_name = 'description'
    ) THEN
        ALTER TABLE public.tutorpackages DROP COLUMN description;
    END IF;

    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'tutorpackages'
          AND column_name = 'durationminutespersession'
    ) THEN
        ALTER TABLE public.tutorpackages DROP COLUMN durationminutespersession;
    END IF;
END
$$;

COMMIT;
