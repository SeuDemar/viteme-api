-- Migration CpfUsuario: CPF do pagador (exigido pelo Asaas para cobrar).
-- Rodar no SQL Editor do Supabase. Pode rodar mais de uma vez: cada comando só age se ainda não foi aplicado.
-- Não depende da tabela "__EFMigrationsHistory"; se ela existir, a migration é registrada.
-- Tudo em uma transação: se algo falhar, nada é aplicado.

BEGIN;

ALTER TABLE usuario ADD COLUMN IF NOT EXISTS cpf character varying(11);

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_usuario_cpf_formato') THEN
        ALTER TABLE usuario ADD CONSTRAINT ck_usuario_cpf_formato CHECK (cpf IS NULL OR cpf ~ '^[0-9]{11}$');
    END IF;

    IF to_regclass('"__EFMigrationsHistory"') IS NOT NULL THEN
        INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
        VALUES ('20261124120000_CpfUsuario', '10.0.13')
        ON CONFLICT DO NOTHING;
    END IF;
END $$;

COMMIT;
