-- Migration RedefinicaoSenha: links de redefinição de senha enviados por e-mail (E05/E06).
-- Rodar no SQL Editor do Supabase. Pode rodar mais de uma vez: cada comando só age se ainda não foi aplicado.
-- Não depende da tabela "__EFMigrationsHistory" (há bancos criados sem ela); se ela existir, a migration é registrada.
-- Tudo em uma transação: se algo falhar, nada é aplicado.

BEGIN;

CREATE TABLE IF NOT EXISTS token_redefinicao_senha (
    id uuid NOT NULL,
    usuario_id uuid NOT NULL,
    token_hash character varying(255) NOT NULL,
    expira_em timestamp with time zone NOT NULL,
    usado_em timestamp with time zone,
    criado_em timestamp with time zone NOT NULL,
    CONSTRAINT pk_token_redefinicao_senha PRIMARY KEY (id),
    CONSTRAINT fk_token_redefinicao_senha_usuarios_usuario_id FOREIGN KEY (usuario_id) REFERENCES usuario (id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_token_redefinicao_senha_token_hash ON token_redefinicao_senha (token_hash);
CREATE INDEX IF NOT EXISTS ix_token_redefinicao_senha_usuario_id ON token_redefinicao_senha (usuario_id);

-- O Supabase expõe as tabelas do schema public pela API REST: sem políticas, o RLS bloqueia esse acesso (a API do Viteme
-- usa a conexão direta do banco). Mesmo tratamento das demais tabelas.
ALTER TABLE token_redefinicao_senha ENABLE ROW LEVEL SECURITY;

DO $$
BEGIN
    IF to_regclass('"__EFMigrationsHistory"') IS NOT NULL THEN
        INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
        VALUES ('20261123120000_RedefinicaoSenha', '10.0.13')
        ON CONFLICT DO NOTHING;
    END IF;
END $$;

COMMIT;
