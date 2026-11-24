-- Migration CobrancaNaSubconta: com a garantia (Fase 3 do Asaas) ligada, a cobrança nasce na subconta do organizador,
-- porque o Asaas só retém em garantia as cobranças da própria subconta (o valor recebido por split da raiz não fica retido).
-- O pagamento guarda qual conta de recebimento cobrou. Rodar no SQL Editor do Supabase, depois de 20261124150000_ContaRecebimento.
-- Pode rodar mais de uma vez. Tudo em uma transação.

BEGIN;

ALTER TABLE pagamento ADD COLUMN IF NOT EXISTS conta_recebimento_id uuid;

CREATE INDEX IF NOT EXISTS ix_pagamento_conta_recebimento_id ON pagamento (conta_recebimento_id);

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_pagamento_conta_recebimento_conta_recebimento_id') THEN
        ALTER TABLE pagamento ADD CONSTRAINT fk_pagamento_conta_recebimento_conta_recebimento_id
            FOREIGN KEY (conta_recebimento_id) REFERENCES conta_recebimento (id) ON DELETE RESTRICT;
    END IF;

    IF to_regclass('"__EFMigrationsHistory"') IS NOT NULL THEN
        INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
        VALUES ('20261125120000_CobrancaNaSubconta', '10.0.13')
        ON CONFLICT DO NOTHING;
    END IF;
END $$;

COMMIT;
