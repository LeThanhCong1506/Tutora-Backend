BEGIN;

DO $$
BEGIN
    IF to_regclass('public.tutorcombos') IS NOT NULL
       AND to_regclass('public.tutorpackages') IS NULL THEN
        ALTER TABLE public.tutorcombos RENAME TO tutorpackages;
    END IF;

    IF to_regclass('public.tutorcombofixedslots') IS NOT NULL
       AND to_regclass('public.tutorpackagefixedslots') IS NULL THEN
        ALTER TABLE public.tutorcombofixedslots RENAME TO tutorpackagefixedslots;
    END IF;
END
$$;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'bookings'
          AND column_name = 'comboid'
    ) THEN
        ALTER TABLE public.bookings RENAME COLUMN comboid TO packageid;
    END IF;

    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'tutorpackages'
          AND column_name = 'comboid'
    ) THEN
        ALTER TABLE public.tutorpackages RENAME COLUMN comboid TO packageid;
    END IF;

    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'tutorpackages'
          AND column_name = 'combotype'
    ) THEN
        ALTER TABLE public.tutorpackages RENAME COLUMN combotype TO packagetype;
    END IF;

    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'tutorpackagefixedslots'
          AND column_name = 'comboid'
    ) THEN
        ALTER TABLE public.tutorpackagefixedslots RENAME COLUMN comboid TO packageid;
    END IF;
END
$$;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'tutorpackages'
          AND column_name = 'packagetype'
          AND data_type <> 'integer'
    ) THEN
        ALTER TABLE public.tutorpackages
            ALTER COLUMN packagetype TYPE integer
            USING CASE
                WHEN lower(packagetype::text) = 'flexible' THEN 1
                WHEN lower(packagetype::text) = 'fixed' THEN 2
                ELSE packagetype::integer
            END;
    END IF;
END
$$;

ALTER TABLE public.tutorpackages
    DROP COLUMN IF EXISTS totalsessions;

ALTER TABLE public.bookings
    DROP COLUMN IF EXISTS teachingmode;

ALTER TABLE public.tutorprofiles
    DROP COLUMN IF EXISTS teachingmode,
    DROP COLUMN IF EXISTS verifiedat,
    DROP COLUMN IF EXISTS verifiedby;

ALTER TABLE public.lessons
    DROP COLUMN IF EXISTS sessionindex;

ALTER TABLE public.tutorprofiles
    ALTER COLUMN profilestatus SET DEFAULT 'draft';

DO $$
BEGIN
    IF to_regclass('public.tutorcombos_comboid_seq') IS NOT NULL
       AND to_regclass('public.tutorpackages_packageid_seq') IS NULL THEN
        ALTER SEQUENCE public.tutorcombos_comboid_seq RENAME TO tutorpackages_packageid_seq;
    END IF;

    IF to_regclass('public.tutorcombofixedslots_fixedslotid_seq') IS NOT NULL
       AND to_regclass('public.tutorpackagefixedslots_fixedslotid_seq') IS NULL THEN
        ALTER SEQUENCE public.tutorcombofixedslots_fixedslotid_seq RENAME TO tutorpackagefixedslots_fixedslotid_seq;
    END IF;
END
$$;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'tutorcombos_pkey'
          AND conrelid = 'public.tutorpackages'::regclass
    ) THEN
        ALTER TABLE public.tutorpackages
            RENAME CONSTRAINT tutorcombos_pkey TO tutorpackages_pkey;
    END IF;

    IF EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'tutorcombofixedslots_pkey'
          AND conrelid = 'public.tutorpackagefixedslots'::regclass
    ) THEN
        ALTER TABLE public.tutorpackagefixedslots
            RENAME CONSTRAINT tutorcombofixedslots_pkey TO tutorpackagefixedslots_pkey;
    END IF;

    IF EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_bookings_combo'
          AND conrelid = 'public.bookings'::regclass
    ) THEN
        ALTER TABLE public.bookings
            RENAME CONSTRAINT fk_bookings_combo TO fk_bookings_package;
    END IF;

    IF EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_tutorcombos_tutor'
          AND conrelid = 'public.tutorpackages'::regclass
    ) THEN
        ALTER TABLE public.tutorpackages
            RENAME CONSTRAINT fk_tutorcombos_tutor TO fk_tutorpackages_tutor;
    END IF;

    IF EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_tutorcombofixedslots_combo'
          AND conrelid = 'public.tutorpackagefixedslots'::regclass
    ) THEN
        ALTER TABLE public.tutorpackagefixedslots
            RENAME CONSTRAINT fk_tutorcombofixedslots_combo TO fk_tutorpackagefixedslots_package;
    END IF;

    IF EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_tutorcombos_combotype'
          AND conrelid = 'public.tutorpackages'::regclass
    ) THEN
        ALTER TABLE public.tutorpackages
            DROP CONSTRAINT ck_tutorcombos_combotype;
    END IF;

    IF EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_tutorcombos_totalsessions_positive'
          AND conrelid = 'public.tutorpackages'::regclass
    ) THEN
        ALTER TABLE public.tutorpackages
            DROP CONSTRAINT ck_tutorcombos_totalsessions_positive;
    END IF;

    IF EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_tutorcombos_duration_positive'
          AND conrelid = 'public.tutorpackages'::regclass
    ) THEN
        ALTER TABLE public.tutorpackages
            RENAME CONSTRAINT ck_tutorcombos_duration_positive TO ck_tutorpackages_duration_positive;
    END IF;

    IF EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_tutorcombofixedslots_dayofweek'
          AND conrelid = 'public.tutorpackagefixedslots'::regclass
    ) THEN
        ALTER TABLE public.tutorpackagefixedslots
            RENAME CONSTRAINT ck_tutorcombofixedslots_dayofweek TO ck_tutorpackagefixedslots_dayofweek;
    END IF;

    IF EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_tutorcombofixedslots_time_range'
          AND conrelid = 'public.tutorpackagefixedslots'::regclass
    ) THEN
        ALTER TABLE public.tutorpackagefixedslots
            RENAME CONSTRAINT ck_tutorcombofixedslots_time_range TO ck_tutorpackagefixedslots_time_range;
    END IF;

    IF EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_lessons_originallessonid'
          AND conrelid = 'public.lessons'::regclass
    ) THEN
        ALTER TABLE public.lessons
            DROP CONSTRAINT fk_lessons_originallessonid;
    END IF;
END
$$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_tutorpackages_packagetype'
          AND conrelid = 'public.tutorpackages'::regclass
    ) THEN
        ALTER TABLE public.tutorpackages
            ADD CONSTRAINT ck_tutorpackages_packagetype
            CHECK (packagetype IN (1, 2));
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_tutorpackages_duration_positive'
          AND conrelid = 'public.tutorpackages'::regclass
    ) THEN
        ALTER TABLE public.tutorpackages
            ADD CONSTRAINT ck_tutorpackages_duration_positive
            CHECK (durationminutespersession > 0);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_tutorpackages_tutor'
          AND conrelid = 'public.tutorpackages'::regclass
    ) THEN
        ALTER TABLE public.tutorpackages
            ADD CONSTRAINT fk_tutorpackages_tutor
            FOREIGN KEY (tutorid) REFERENCES public.tutorprofiles(tutorid) ON DELETE CASCADE;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_tutorpackagefixedslots_package'
          AND conrelid = 'public.tutorpackagefixedslots'::regclass
    ) THEN
        ALTER TABLE public.tutorpackagefixedslots
            ADD CONSTRAINT fk_tutorpackagefixedslots_package
            FOREIGN KEY (packageid) REFERENCES public.tutorpackages(packageid) ON DELETE CASCADE;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_bookings_package'
          AND conrelid = 'public.bookings'::regclass
    ) THEN
        ALTER TABLE public.bookings
            ADD CONSTRAINT fk_bookings_package
            FOREIGN KEY (packageid) REFERENCES public.tutorpackages(packageid);
    END IF;
END
$$;

DO $$
BEGIN
    IF to_regclass('public.idx_bookings_comboid') IS NOT NULL
       AND to_regclass('public.idx_bookings_packageid') IS NULL THEN
        ALTER INDEX public.idx_bookings_comboid RENAME TO idx_bookings_packageid;
    END IF;

    IF to_regclass('public.idx_tutorcombofixedslots_combo_day') IS NOT NULL
       AND to_regclass('public.idx_tutorpackagefixedslots_package_day') IS NULL THEN
        ALTER INDEX public.idx_tutorcombofixedslots_combo_day RENAME TO idx_tutorpackagefixedslots_package_day;
    END IF;

    IF to_regclass('public.idx_tutorcombos_tutor_active_type') IS NOT NULL
       AND to_regclass('public.idx_tutorpackages_tutor_active_type') IS NULL THEN
        ALTER INDEX public.idx_tutorcombos_tutor_active_type RENAME TO idx_tutorpackages_tutor_active_type;
    END IF;
END
$$;

CREATE INDEX IF NOT EXISTS idx_bookings_packageid
    ON public.bookings (packageid);

CREATE INDEX IF NOT EXISTS idx_tutorpackagefixedslots_package_day
    ON public.tutorpackagefixedslots (packageid, dayofweek);

CREATE INDEX IF NOT EXISTS idx_tutorpackages_tutor_active_type
    ON public.tutorpackages (tutorid, isactive, packagetype);

COMMENT ON COLUMN public.bookings.packageid IS
    'Package duoc chon tai thoi diem booking.';

COMMENT ON COLUMN public.tutorprofiles.triallessonprice IS
    'Trial lesson price in VND.';

COMMENT ON TABLE public.tutorpackages IS
    'Package hoc do tutor tao. packagetype=1 la flexible, packagetype=2 la fixed. Booking moi tham chieu package nay qua bookings.packageid.';

COMMENT ON TABLE public.tutorpackagefixedslots IS
    'Lich co dinh cua package fixed. Package flexible khong co ban ghi trong bang nay.';

COMMIT;
