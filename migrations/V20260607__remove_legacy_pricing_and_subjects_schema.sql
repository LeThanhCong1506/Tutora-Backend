BEGIN;

-- Remove legacy pricing columns from tutorprofiles
DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'tutorprofiles'
          AND column_name = 'triallessonprice'
    ) THEN
        ALTER TABLE public.tutorprofiles DROP COLUMN triallessonprice;
    END IF;

    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'tutorprofiles'
          AND column_name = 'allowpricenegotiation'
    ) THEN
        ALTER TABLE public.tutorprofiles DROP COLUMN allowpricenegotiation;
    END IF;
END
$$;

COMMIT;
