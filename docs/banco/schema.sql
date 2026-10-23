CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    migration_id character varying(150) NOT NULL,
    product_version character varying(32) NOT NULL,
    CONSTRAINT pk___ef_migrations_history PRIMARY KEY (migration_id)
);

START TRANSACTION;
CREATE TYPE motivo_denuncia AS ENUM ('EVENTO_FALSO', 'GOLPE', 'CONTEUDO_IMPROPRIO', 'SPAM', 'OUTRO');
CREATE TYPE status_evento AS ENUM ('RASCUNHO', 'PUBLICADO', 'OCULTO', 'CANCELADO', 'ENCERRADO');
CREATE TYPE status_inscricao AS ENUM ('PENDENTE_PAGAMENTO', 'CONFIRMADA', 'CANCELADA', 'EXPIRADA');
CREATE TYPE status_pagamento AS ENUM ('PENDENTE', 'APROVADO', 'RECUSADO', 'ESTORNADO', 'EXPIRADO');
CREATE TYPE status_sms AS ENUM ('PENDENTE', 'ENVIADO', 'FALHOU');
CREATE TYPE tipo_notificacao AS ENUM ('INSCRICAO', 'PAGAMENTO', 'ALTERACAO', 'LEMBRETE', 'CANCELAMENTO', 'COLABORACAO', 'DENUNCIA');

CREATE TABLE usuario (
    id uuid NOT NULL,
    nome character varying(120) NOT NULL,
    email character varying(160) NOT NULL,
    senha_hash character varying(255) NOT NULL,
    telefone character varying(20),
    telefone_verificado boolean NOT NULL DEFAULT FALSE,
    aceita_sms boolean NOT NULL DEFAULT FALSE,
    aceite_sms_em timestamp with time zone,
    tentativas_login smallint NOT NULL DEFAULT 0,
    bloqueado_ate timestamp with time zone,
    criado_em timestamp with time zone NOT NULL,
    atualizado_em timestamp with time zone NOT NULL,
    CONSTRAINT pk_usuario PRIMARY KEY (id),
    CONSTRAINT ck_usuario_sms_exige_telefone_verificado CHECK (NOT aceita_sms OR telefone_verificado),
    CONSTRAINT ck_usuario_telefone_e164 CHECK (telefone IS NULL OR telefone ~ '^\+[1-9][0-9]{7,14}$')
);

CREATE TABLE codigo_verificacao (
    id uuid NOT NULL,
    usuario_id uuid NOT NULL,
    codigo_hash character varying(255) NOT NULL,
    expira_em timestamp with time zone NOT NULL,
    tentativas smallint NOT NULL DEFAULT 0,
    utilizado boolean NOT NULL DEFAULT FALSE,
    criado_em timestamp with time zone NOT NULL,
    CONSTRAINT pk_codigo_verificacao PRIMARY KEY (id),
    CONSTRAINT ck_codigo_verificacao_tentativas CHECK (tentativas <= 5),
    CONSTRAINT fk_codigo_verificacao_usuarios_usuario_id FOREIGN KEY (usuario_id) REFERENCES usuario (id) ON DELETE CASCADE
);

CREATE TABLE evento (
    id uuid NOT NULL,
    organizador_id uuid NOT NULL,
    titulo character varying(150) NOT NULL,
    descricao text NOT NULL,
    categoria character varying(60) NOT NULL,
    local character varying(200) NOT NULL,
    imagem_url character varying(500),
    data_inicio timestamp with time zone NOT NULL,
    data_fim timestamp with time zone NOT NULL,
    inscricao_inicio timestamp with time zone NOT NULL,
    inscricao_fim timestamp with time zone NOT NULL,
    capacidade integer,
    preco numeric(10,2) NOT NULL DEFAULT 0.0,
    carga_horaria smallint,
    status status_evento NOT NULL DEFAULT ('RASCUNHO'::status_evento),
    reaberto_em timestamp with time zone,
    criado_em timestamp with time zone NOT NULL,
    atualizado_em timestamp with time zone NOT NULL,
    CONSTRAINT pk_evento PRIMARY KEY (id),
    CONSTRAINT ck_evento_capacidade CHECK (capacidade IS NULL OR capacidade > 0),
    CONSTRAINT ck_evento_carga_horaria CHECK (carga_horaria IS NULL OR carga_horaria > 0),
    CONSTRAINT ck_evento_data_fim CHECK (data_fim > data_inicio),
    CONSTRAINT ck_evento_inscricao_fim_ate_inicio CHECK (inscricao_fim <= data_inicio),
    CONSTRAINT ck_evento_inscricao_periodo CHECK (inscricao_fim > inscricao_inicio),
    CONSTRAINT ck_evento_preco CHECK (preco >= 0),
    CONSTRAINT fk_evento_usuarios_organizador_id FOREIGN KEY (organizador_id) REFERENCES usuario (id) ON DELETE RESTRICT
);

CREATE TABLE token_renovacao (
    id uuid NOT NULL,
    usuario_id uuid NOT NULL,
    token_hash character varying(255) NOT NULL,
    expira_em timestamp with time zone NOT NULL,
    revogado_em timestamp with time zone,
    criado_em timestamp with time zone NOT NULL,
    CONSTRAINT pk_token_renovacao PRIMARY KEY (id),
    CONSTRAINT fk_token_renovacao_usuarios_usuario_id FOREIGN KEY (usuario_id) REFERENCES usuario (id) ON DELETE CASCADE
);

CREATE TABLE alteracao_evento (
    id uuid NOT NULL,
    evento_id uuid NOT NULL,
    autor_id uuid NOT NULL,
    campo character varying(50) NOT NULL,
    valor_anterior text,
    valor_novo text,
    motivo character varying(300) NOT NULL,
    criado_em timestamp with time zone NOT NULL,
    CONSTRAINT pk_alteracao_evento PRIMARY KEY (id),
    CONSTRAINT fk_alteracao_evento_eventos_evento_id FOREIGN KEY (evento_id) REFERENCES evento (id) ON DELETE RESTRICT,
    CONSTRAINT fk_alteracao_evento_usuarios_autor_id FOREIGN KEY (autor_id) REFERENCES usuario (id) ON DELETE RESTRICT
);

CREATE TABLE atividade_programacao (
    id uuid NOT NULL,
    evento_id uuid NOT NULL,
    titulo character varying(150) NOT NULL,
    palestrante character varying(120),
    local character varying(200),
    inicio timestamp with time zone NOT NULL,
    fim timestamp with time zone NOT NULL,
    CONSTRAINT pk_atividade_programacao PRIMARY KEY (id),
    CONSTRAINT ck_atividade_programacao_fim CHECK (fim > inicio),
    CONSTRAINT fk_atividade_programacao_eventos_evento_id FOREIGN KEY (evento_id) REFERENCES evento (id) ON DELETE CASCADE
);

CREATE TABLE colaborador_evento (
    id uuid NOT NULL,
    evento_id uuid NOT NULL,
    usuario_id uuid NOT NULL,
    adicionado_em timestamp with time zone NOT NULL,
    CONSTRAINT pk_colaborador_evento PRIMARY KEY (id),
    CONSTRAINT fk_colaborador_evento_eventos_evento_id FOREIGN KEY (evento_id) REFERENCES evento (id) ON DELETE CASCADE,
    CONSTRAINT fk_colaborador_evento_usuarios_usuario_id FOREIGN KEY (usuario_id) REFERENCES usuario (id) ON DELETE CASCADE
);

CREATE TABLE denuncia (
    id uuid NOT NULL,
    evento_id uuid NOT NULL,
    usuario_id uuid NOT NULL,
    motivo motivo_denuncia NOT NULL,
    descricao character varying(500),
    criado_em timestamp with time zone NOT NULL,
    CONSTRAINT pk_denuncia PRIMARY KEY (id),
    CONSTRAINT ck_denuncia_descricao_outro CHECK (motivo <> 'OUTRO' OR descricao IS NOT NULL),
    CONSTRAINT fk_denuncia_eventos_evento_id FOREIGN KEY (evento_id) REFERENCES evento (id) ON DELETE RESTRICT,
    CONSTRAINT fk_denuncia_usuarios_usuario_id FOREIGN KEY (usuario_id) REFERENCES usuario (id) ON DELETE RESTRICT
);

CREATE TABLE inscricao (
    id uuid NOT NULL,
    evento_id uuid NOT NULL,
    usuario_id uuid NOT NULL,
    status status_inscricao NOT NULL,
    codigo_checkin character varying(12) NOT NULL,
    reservada_ate timestamp with time zone,
    presenca_confirmada boolean NOT NULL DEFAULT FALSE,
    checkin_em timestamp with time zone,
    criado_em timestamp with time zone NOT NULL,
    atualizado_em timestamp with time zone NOT NULL,
    CONSTRAINT pk_inscricao PRIMARY KEY (id),
    CONSTRAINT fk_inscricao_evento_evento_id FOREIGN KEY (evento_id) REFERENCES evento (id) ON DELETE RESTRICT,
    CONSTRAINT fk_inscricao_usuarios_usuario_id FOREIGN KEY (usuario_id) REFERENCES usuario (id) ON DELETE RESTRICT
);

CREATE TABLE interesse_evento (
    usuario_id uuid NOT NULL,
    evento_id uuid NOT NULL,
    criado_em timestamp with time zone NOT NULL,
    CONSTRAINT pk_interesse_evento PRIMARY KEY (usuario_id, evento_id),
    CONSTRAINT fk_interesse_evento_evento_evento_id FOREIGN KEY (evento_id) REFERENCES evento (id) ON DELETE CASCADE,
    CONSTRAINT fk_interesse_evento_usuarios_usuario_id FOREIGN KEY (usuario_id) REFERENCES usuario (id) ON DELETE CASCADE
);

CREATE TABLE notificacao (
    id uuid NOT NULL,
    usuario_id uuid NOT NULL,
    evento_id uuid,
    tipo tipo_notificacao NOT NULL,
    titulo character varying(150) NOT NULL,
    mensagem character varying(1000) NOT NULL,
    lida boolean NOT NULL DEFAULT FALSE,
    criado_em timestamp with time zone NOT NULL,
    CONSTRAINT pk_notificacao PRIMARY KEY (id),
    CONSTRAINT fk_notificacao_evento_evento_id FOREIGN KEY (evento_id) REFERENCES evento (id) ON DELETE SET NULL,
    CONSTRAINT fk_notificacao_usuarios_usuario_id FOREIGN KEY (usuario_id) REFERENCES usuario (id) ON DELETE RESTRICT
);

CREATE TABLE certificado (
    id uuid NOT NULL,
    inscricao_id uuid NOT NULL,
    codigo_validacao character varying(20) NOT NULL,
    emitido_em timestamp with time zone NOT NULL,
    CONSTRAINT pk_certificado PRIMARY KEY (id),
    CONSTRAINT fk_certificado_inscricoes_inscricao_id FOREIGN KEY (inscricao_id) REFERENCES inscricao (id) ON DELETE RESTRICT
);

CREATE TABLE pagamento (
    id uuid NOT NULL,
    inscricao_id uuid NOT NULL,
    valor numeric(10,2) NOT NULL,
    status status_pagamento NOT NULL,
    metodo character varying(30),
    id_transacao_gateway character varying(100),
    url_pagamento character varying(500),
    criado_em timestamp with time zone NOT NULL,
    atualizado_em timestamp with time zone NOT NULL,
    CONSTRAINT pk_pagamento PRIMARY KEY (id),
    CONSTRAINT ck_pagamento_valor CHECK (valor > 0),
    CONSTRAINT fk_pagamento_inscricao_inscricao_id FOREIGN KEY (inscricao_id) REFERENCES inscricao (id) ON DELETE RESTRICT
);

CREATE TABLE envio_sms (
    id uuid NOT NULL,
    notificacao_id uuid NOT NULL,
    telefone character varying(20) NOT NULL,
    status status_sms NOT NULL,
    tentativas smallint NOT NULL DEFAULT 0,
    ultimo_erro character varying(300),
    enviado_em timestamp with time zone,
    criado_em timestamp with time zone NOT NULL,
    CONSTRAINT pk_envio_sms PRIMARY KEY (id),
    CONSTRAINT ck_envio_sms_tentativas CHECK (tentativas <= 4),
    CONSTRAINT fk_envio_sms_notificacoes_notificacao_id FOREIGN KEY (notificacao_id) REFERENCES notificacao (id) ON DELETE CASCADE
);

CREATE INDEX ix_alteracao_evento_autor_id ON alteracao_evento (autor_id);

CREATE INDEX ix_alteracao_evento_evento_id_criado_em ON alteracao_evento (evento_id, criado_em DESC);

CREATE INDEX ix_atividade_programacao_evento_id ON atividade_programacao (evento_id);

CREATE UNIQUE INDEX ix_certificado_codigo_validacao ON certificado (codigo_validacao);

CREATE UNIQUE INDEX ix_certificado_inscricao_id ON certificado (inscricao_id);

CREATE INDEX ix_codigo_verificacao_usuario_id ON codigo_verificacao (usuario_id);

CREATE UNIQUE INDEX ix_colaborador_evento_evento_id_usuario_id ON colaborador_evento (evento_id, usuario_id);

CREATE INDEX ix_colaborador_evento_usuario_id ON colaborador_evento (usuario_id);

CREATE INDEX ix_denuncia_evento_id_criado_em ON denuncia (evento_id, criado_em);

CREATE UNIQUE INDEX ix_denuncia_evento_id_usuario_id ON denuncia (evento_id, usuario_id);

CREATE INDEX ix_denuncia_usuario_id ON denuncia (usuario_id);

CREATE UNIQUE INDEX ix_envio_sms_notificacao_id ON envio_sms (notificacao_id);

CREATE INDEX ix_evento_organizador_id ON evento (organizador_id);

CREATE INDEX ix_evento_status_data_inicio ON evento (status, data_inicio);

CREATE UNIQUE INDEX ix_inscricao_codigo_checkin ON inscricao (codigo_checkin);

CREATE INDEX ix_inscricao_evento_id_status ON inscricao (evento_id, status);

CREATE INDEX ix_inscricao_reservada_ate_pendente ON inscricao (reservada_ate) WHERE status = 'PENDENTE_PAGAMENTO';

CREATE INDEX ix_inscricao_usuario_id ON inscricao (usuario_id);

CREATE UNIQUE INDEX ux_inscricao_ativa_por_usuario_evento ON inscricao (evento_id, usuario_id) WHERE status IN ('PENDENTE_PAGAMENTO', 'CONFIRMADA');

CREATE INDEX ix_interesse_evento_evento_id ON interesse_evento (evento_id);

CREATE INDEX ix_notificacao_evento_id ON notificacao (evento_id);

CREATE INDEX ix_notificacao_usuario_id_lida_criado_em ON notificacao (usuario_id, lida, criado_em DESC);

CREATE UNIQUE INDEX ix_pagamento_id_transacao_gateway ON pagamento (id_transacao_gateway);

CREATE INDEX ix_pagamento_inscricao_id ON pagamento (inscricao_id);

CREATE UNIQUE INDEX ix_token_renovacao_token_hash ON token_renovacao (token_hash);

CREATE INDEX ix_token_renovacao_usuario_id ON token_renovacao (usuario_id);

CREATE UNIQUE INDEX ux_usuario_email_lower ON usuario (lower(email));

INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
VALUES ('20261022202353_Inicial', '10.0.13');

COMMIT;

START TRANSACTION;
INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
VALUES ('20261023125306_VersaoInscricaoPagamento', '10.0.13');

COMMIT;

