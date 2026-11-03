-- Migration CnpjUsuario (RN24): CNPJ do organizador no usuário.
-- Rodar no SQL Editor do Supabase. Pode rodar mais de uma vez: cada comando só age se ainda não foi aplicado.
-- Não depende da tabela "__EFMigrationsHistory" (há bancos criados sem ela); se ela existir, a migration é registrada.
-- Tudo em uma transação: se algo falhar, nada é aplicado.

BEGIN;

ALTER TABLE usuario ADD COLUMN IF NOT EXISTS cnpj character varying(14);
ALTER TABLE usuario ADD COLUMN IF NOT EXISTS cnpj_verificado_em timestamp with time zone;
ALTER TABLE usuario ADD COLUMN IF NOT EXISTS razao_social character varying(200);

CREATE UNIQUE INDEX IF NOT EXISTS ux_usuario_cnpj ON usuario (cnpj) WHERE cnpj IS NOT NULL;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_usuario_cnpj_formato') THEN
        ALTER TABLE usuario ADD CONSTRAINT ck_usuario_cnpj_formato CHECK (cnpj IS NULL OR cnpj ~ '^[A-Z0-9]{12}[0-9]{2}$');
    END IF;

    IF to_regclass('"__EFMigrationsHistory"') IS NOT NULL THEN
        INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
        VALUES ('20261028224208_CnpjUsuario', '10.0.13')
        ON CONFLICT DO NOTHING;
    END IF;
END $$;

COMMIT;
