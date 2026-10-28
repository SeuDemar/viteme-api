-- Migration CnpjUsuario (RN24): CNPJ do organizador no usuário.
-- Idempotente: pode rodar mais de uma vez; só aplica se a migration ainda não estiver em __EFMigrationsHistory.
-- Rodar no SQL Editor do Supabase (ou: dotnet ef database update).

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261028224208_CnpjUsuario') THEN
    ALTER TABLE usuario ADD cnpj character varying(14);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261028224208_CnpjUsuario') THEN
    ALTER TABLE usuario ADD cnpj_verificado_em timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261028224208_CnpjUsuario') THEN
    ALTER TABLE usuario ADD razao_social character varying(200);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261028224208_CnpjUsuario') THEN
    CREATE UNIQUE INDEX ux_usuario_cnpj ON usuario (cnpj) WHERE cnpj IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261028224208_CnpjUsuario') THEN
    ALTER TABLE usuario ADD CONSTRAINT ck_usuario_cnpj_formato CHECK (cnpj IS NULL OR cnpj ~ '^[A-Z0-9]{12}[0-9]{2}$');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261028224208_CnpjUsuario') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20261028224208_CnpjUsuario', '10.0.13');
    END IF;
END $EF$;
COMMIT;

