-- Migration: gender column varchar(10) -> smallint
-- Enum mapping: 0=Other, 1=Male, 2=Female
-- Safe to run: all existing rows have gender = NULL (verified from data dump)

-- (Optional) sanity check before running:
-- SELECT DISTINCT gender FROM public.users WHERE gender IS NOT NULL;

ALTER TABLE public.users
    ALTER COLUMN gender TYPE smallint
    USING (CASE upper(trim(gender))
               WHEN 'NAM'  THEN 1
               WHEN 'NỮ'   THEN 2
               WHEN 'NU'   THEN 2
               WHEN '0'    THEN 0
               WHEN '1'    THEN 1
               WHEN '2'    THEN 2
               ELSE NULL
           END);

ALTER TABLE public.users
    ADD CONSTRAINT users_gender_check
    CHECK (gender IS NULL OR gender IN (0, 1, 2));
