-- Store the legal operating country used for company-specific tax defaults.
ALTER TABLE public.companies
    ADD COLUMN IF NOT EXISTS country varchar(100);
