-- Migration ContaRecebimento: subconta do organizador no Asaas (Fase 2) e garantia do pagamento (Fase 3).
-- As duas fases ficam desligadas por configuração (Gateway__Asaas__Subcontas__Habilitar / Escrow__Habilitar); a tabela pode
-- existir vazia. Rodar no SQL Editor do Supabase. Pode rodar mais de uma vez: cada comando só age se ainda não foi aplicado.
-- Não depende da tabela "__EFMigrationsHistory"; se ela existir, a migration é registrada.
-- Tudo em uma transação: se algo falhar, nada é aplicado.

BEGIN;

ALTER TABLE pagamento ADD COLUMN IF NOT EXISTS garantia_id character varying(100);
ALTER TABLE pagamento ADD COLUMN IF NOT EXISTS garantia_liberada_em timestamp with time zone;

CREATE TABLE IF NOT EXISTS conta_recebimento (
    id uuid NOT NULL,
    usuario_id uuid NOT NULL,
    conta_gateway_id character varying(100) NOT NULL,
    wallet_id character varying(100) NOT NULL,
    chave_api_cifrada character varying(1000) NOT NULL,
    situacao character varying(20) NOT NULL,
    url_onboarding character varying(500),
    aprovada_em timestamp with time zone,
    garantia_habilitada_em timestamp with time zone,
    criado_em timestamp with time zone NOT NULL,
    atualizado_em timestamp with time zone NOT NULL,
    CONSTRAINT pk_conta_recebimento PRIMARY KEY (id),
    CONSTRAINT ck_conta_recebimento_situacao CHECK (situacao IN ('PENDENTE', 'APROVADA', 'REJEITADA')),
    CONSTRAINT fk_conta_recebimento_usuarios_usuario_id FOREIGN KEY (usuario_id) REFERENCES usuario (id) ON DELETE RESTRICT
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_conta_recebimento_conta_gateway_id ON conta_recebimento (conta_gateway_id);
CREATE UNIQUE INDEX IF NOT EXISTS ix_conta_recebimento_usuario_id ON conta_recebimento (usuario_id);

-- O Supabase expõe as tabelas do schema public pela API REST: sem políticas, o RLS bloqueia esse acesso (a API do Viteme
-- usa a conexão direta do banco). A tabela guarda a chave de API da subconta, ainda que cifrada.
ALTER TABLE conta_recebimento ENABLE ROW LEVEL SECURITY;

DO $$
BEGIN
    IF to_regclass('"__EFMigrationsHistory"') IS NOT NULL THEN
        INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
        VALUES ('20261124150000_ContaRecebimento', '10.0.13')
        ON CONFLICT DO NOTHING;
    END IF;
END $$;

COMMIT;
