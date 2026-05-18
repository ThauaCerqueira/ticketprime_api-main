IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'TicketPrime')
BEGIN
    CREATE DATABASE TicketPrime;
END
GO

USE TicketPrime;
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Usuarios]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[Usuarios] (
        [Cpf]   CHAR(11)      NOT NULL,
        [Nome]  VARCHAR(100)  NOT NULL,
        [Email] VARCHAR(100)  NOT NULL,
        [Senha] VARCHAR(60)   NOT NULL,
        [Perfil] VARCHAR(10)  NOT NULL DEFAULT 'CLIENTE',
        [Slug]  VARCHAR(32)   NULL,
        
        CONSTRAINT [PK_Usuarios] PRIMARY KEY CLUSTERED ([Cpf] ASC)
    );
END
GO

-- Cupons deve ser criada ANTES de Reservas por causa da FK
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Cupons]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[Cupons] (
        [Codigo]              NVARCHAR(20)   NOT NULL,
        [PorcentagemDesconto] DECIMAL(5, 2)  NOT NULL,
        [ValorMinimoRegra]    DECIMAL(18, 2) NOT NULL,

        CONSTRAINT [PK_Cupons] PRIMARY KEY CLUSTERED ([Codigo] ASC),
        CONSTRAINT [CK_Cupons_Desconto] CHECK ([PorcentagemDesconto] >= 0 AND [PorcentagemDesconto] <= 100),
        CONSTRAINT [CK_Cupons_ValorMinimo] CHECK ([ValorMinimoRegra] >= 0)
    );
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Eventos]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[Eventos] (
        [Id]                        INT              IDENTITY (1, 1) NOT NULL,
        [Nome]                      NVARCHAR (200)   NOT NULL,
        [CapacidadeTotal]           INT              NOT NULL,
        [CapacidadeRestante]        INT              NOT NULL,
        [DataEvento]                DATETIME2 (7)    NOT NULL,
        [DataTermino]               DATETIME2 (7)    NULL,
        [PrecoPadrao]               DECIMAL (18, 2)  NOT NULL,
        [LimiteIngressosPorUsuario] INT              NOT NULL DEFAULT 6,
        [Local]                     NVARCHAR (500)   NOT NULL DEFAULT '',
        [Descricao]                 NVARCHAR (2000)  NULL,
        [GeneroMusical]             NVARCHAR (100)   NOT NULL DEFAULT '',
        [EventoGratuito]            BIT              NOT NULL DEFAULT 0,
        [Status]                    VARCHAR (20)     NOT NULL DEFAULT 'Rascunho',
        [TaxaServico]               DECIMAL (18, 2)  NOT NULL DEFAULT 0,
        
        CONSTRAINT [PK_Eventos] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [CK_Eventos_Status] CHECK ([Status] IN ('Rascunho', 'Publicado', 'Encerrado', 'Cancelado')),
        CONSTRAINT [CK_Eventos_TaxaServico] CHECK ([TaxaServico] >= 0)
    );
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Reservas]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[Reservas] (
        [Id]              INT            IDENTITY (1, 1) NOT NULL,
        [UsuarioCpf]      CHAR(11)       NOT NULL,
        [EventoId]        INT            NOT NULL,
        [DataCompra]      DATETIME       DEFAULT GETDATE(),
        [CupomUtilizado]  NVARCHAR(20)   NULL,
        [ValorFinalPago]  DECIMAL(18, 2) NOT NULL DEFAULT 0,
        [TaxaServicoPago] DECIMAL(18, 2) NOT NULL DEFAULT 0,
        [TemSeguro]       BIT            NOT NULL DEFAULT 0,
        [ValorSeguroPago] DECIMAL(18, 2) NOT NULL DEFAULT 0,

        CONSTRAINT [PK_Reservas] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Reservas_Usuarios] FOREIGN KEY ([UsuarioCpf]) REFERENCES [Usuarios]([Cpf]),
        CONSTRAINT [FK_Reservas_Eventos] FOREIGN KEY ([EventoId]) REFERENCES [Eventos]([Id]),
        CONSTRAINT [FK_Reservas_Cupons] FOREIGN KEY ([CupomUtilizado]) REFERENCES [Cupons]([Codigo])
    );
END
GO

-- Adiciona coluna Perfil caso o banco já exista sem ela
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Usuarios]') AND name = 'Perfil')
BEGIN
    ALTER TABLE Usuarios ADD Perfil VARCHAR(10) NOT NULL DEFAULT 'CLIENTE';
END
GO

-- Adiciona coluna LimiteIngressosPorUsuario na tabela Eventos (caso não exista)
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Eventos]') AND name = 'LimiteIngressosPorUsuario')
BEGIN
    ALTER TABLE Eventos ADD LimiteIngressosPorUsuario INT NOT NULL DEFAULT 6;
END
GO

-- Adiciona colunas na tabela Reservas (caso a tabela já exista sem elas)
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Reservas]') AND name = 'CupomUtilizado')
BEGIN
    ALTER TABLE Reservas ADD CupomUtilizado NVARCHAR(20) NULL;
    ALTER TABLE Reservas ADD CONSTRAINT [FK_Reservas_Cupons]
        FOREIGN KEY ([CupomUtilizado]) REFERENCES [Cupons]([Codigo]);
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Reservas]') AND name = 'ValorFinalPago')
BEGIN
    ALTER TABLE Reservas ADD ValorFinalPago DECIMAL(18, 2) NOT NULL DEFAULT 0;
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Reservas]') AND name = 'TaxaServicoPago')
BEGIN
    ALTER TABLE Reservas ADD TaxaServicoPago DECIMAL(18, 2) NOT NULL DEFAULT 0;
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Reservas]') AND name = 'TemSeguro')
BEGIN
    ALTER TABLE Reservas ADD TemSeguro BIT NOT NULL DEFAULT 0;
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Reservas]') AND name = 'ValorSeguroPago')
BEGIN
    ALTER TABLE Reservas ADD ValorSeguroPago DECIMAL(18, 2) NOT NULL DEFAULT 0;
END
GO

-- ─── Coluna TemMeiaEntrada na tabela Eventos ──────────────────────────────────
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Eventos]') AND name = 'TemMeiaEntrada')
BEGIN
    ALTER TABLE Eventos ADD TemMeiaEntrada BIT NOT NULL DEFAULT 0;
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Reservas]') AND name = 'EhMeiaEntrada')
BEGIN
    ALTER TABLE Reservas ADD EhMeiaEntrada BIT NOT NULL DEFAULT 0;
END
GO

-- ─── Novos campos da tabela Eventos (compatibilidade com bancos existentes) ──────────────────

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Eventos]') AND name = 'Local')
BEGIN
    ALTER TABLE Eventos ADD [Local] NVARCHAR(500) NOT NULL DEFAULT '';
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Eventos]') AND name = 'Descricao')
BEGIN
    ALTER TABLE Eventos ADD [Descricao] NVARCHAR(2000) NULL;
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Eventos]') AND name = 'GeneroMusical')
BEGIN
    ALTER TABLE Eventos ADD [GeneroMusical] NVARCHAR(100) NOT NULL DEFAULT '';
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Eventos]') AND name = 'EventoGratuito')
BEGIN
    ALTER TABLE Eventos ADD [EventoGratuito] BIT NOT NULL DEFAULT 0;
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Eventos]') AND name = 'Status')
BEGIN
    -- Constraint inline no ADD COLUMN para evitar erros de resolução em batch único
    ALTER TABLE Eventos ADD [Status] VARCHAR(20) NOT NULL
        DEFAULT 'Rascunho'
        CONSTRAINT [CK_Eventos_Status]
        CHECK ([Status] IN ('Rascunho', 'Publicado', 'Encerrado', 'Cancelado'));
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Eventos]') AND name = 'TaxaServico')
BEGIN
    ALTER TABLE Eventos ADD [TaxaServico] DECIMAL(18, 2) NOT NULL DEFAULT 0
        CONSTRAINT [CK_Eventos_TaxaServico] CHECK ([TaxaServico] >= 0);
END
GO

-- ─── Coluna DataTermino na tabela Eventos ──────────────────────────────────
-- Data/hora de término do evento. Quando nulo, o sistema usa DataEvento + 4h como fallback.
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Eventos]') AND name = 'DataTermino')
BEGIN
    ALTER TABLE Eventos ADD [DataTermino] DATETIME2 (7) NULL;
END
GO

-- Expande coluna Senha de 25 para 60 chars (BCrypt sempre gera hashes de 60 caracteres)
IF EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[dbo].[Usuarios]')
      AND name = 'Senha'
      AND max_length < 60
)
BEGIN
    ALTER TABLE Usuarios ALTER COLUMN [Senha] VARCHAR(60) NOT NULL;
END
GO

-- Expande Nome de 150 para 200 chars (caso a coluna já exista com tamanho menor)
-- Executar apenas se a precisão atual for 150
IF EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[dbo].[Eventos]')
      AND name = 'Nome'
      AND max_length = 300   -- NVARCHAR(150) ocupa 300 bytes
)
BEGIN
    ALTER TABLE Eventos ALTER COLUMN [Nome] NVARCHAR(200) NOT NULL;
END
GO

-- ─── Tabela de fotos criptografadas ──────────────────────────────────────────

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[EventoFotos]') AND type = N'U')
BEGIN
    CREATE TABLE [dbo].[EventoFotos] (
        [Id]                    INT              IDENTITY (1, 1) NOT NULL,
        [EventoId]              INT              NOT NULL,
        [CiphertextBase64]      NVARCHAR (MAX)   NOT NULL,
        [IvBase64]              NVARCHAR (200)   NOT NULL,
        [ChaveAesCifradaBase64] NVARCHAR (MAX)   NOT NULL,
        [ChavePublicaOrgJwk]    NVARCHAR (MAX)   NOT NULL,
        [HashNomeOriginal]      NVARCHAR (100)   NOT NULL,
        [TipoMime]              VARCHAR  (50)    NOT NULL,
        [TamanhoBytes]          BIGINT           NOT NULL DEFAULT 0,
        [Criptografada]         BIT              NOT NULL DEFAULT 1,
        [DataUpload]            DATETIME2 (7)    NOT NULL DEFAULT GETUTCDATE(),

        CONSTRAINT [PK_EventoFotos] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_EventoFotos_Eventos] FOREIGN KEY ([EventoId])
            REFERENCES [Eventos]([Id]) ON DELETE CASCADE,
        CONSTRAINT [CK_EventoFotos_TamanhoBytes] CHECK ([TamanhoBytes] >= 0)
    );
END
GO

-- ─── Cria o usuário admin padrão ─────────────────────────────────────────────

-- Cria o usuário admin padrão
-- ═══════════════════════════════════════════════════════════════════
-- SEGURANÇA: A senha abaixo é TEMPORÁRIA e conhecida publicamente.
--   O AdminSecurityService na inicialização da aplicação irá:
--   1. DETECTAR que a senha é a padrão
--   2. GERAR uma nova senha aleatória de 16 caracteres
--   3. LOGAR a nova senha no console para o administrador
--   4. Em PRODUÇÃO, BLOQUEAR endpoints admin até a troca
--
--   A senha 'admin123' NUNCA deve ser usada em produção.
--   Consulte os logs da aplicação após o primeiro deploy
--   para obter a senha gerada automaticamente.
-- ═══════════════════════════════════════════════════════════════════
-- CPF padrão: 00000000191 — troque por um CPF real antes de ir a produção.
IF NOT EXISTS (SELECT * FROM Usuarios WHERE Cpf = '00000000191')
BEGIN
    INSERT INTO Usuarios (Cpf, Nome, Email, Senha, Perfil, SenhaTemporaria)
    VALUES ('00000000191', 'Administrador', 'admin@ticketprime.com',
            '$2a$11$Fhms4zc2uBueAl.VMdeJOe4JPnokxLe8b2DyOqL1J/VstjOYpVFEO', 'ADMIN', 1);
END
GO

-- ─── Migrações de segurança e novos recursos ─────────────────────────────────

-- Expande Senha de VARCHAR(25) para VARCHAR(255) para suportar hash BCrypt
IF EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[dbo].[Usuarios]')
      AND name = 'Senha'
      AND max_length < 255
)
BEGIN
    ALTER TABLE Usuarios ALTER COLUMN [Senha] VARCHAR(255) NOT NULL;
END
GO

-- Adiciona índice UNIQUE em Email (garante unicidade de e-mail por usuário)
-- ⚠ Antes de criar o índice, renomeia e-mails duplicados que possam ter sido
--   inseridos antes da existência da constraint. Usamos UPDATE em vez de DELETE
--   para evitar violações de FK (RefreshTokens, Reservas etc. referenciam Usuarios).
--   A prioridade é:
--     1. Manter o admin padrão (Cpf = '00000000191') com o e-mail original
--     2. Renomear os demais registros com e-mail duplicado
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[dbo].[Usuarios]')
      AND name = 'UX_Usuarios_Email'
)
BEGIN
    -- Renomeia e-mails duplicados: mantém a primeira ocorrência (admin primeiro),
    -- as demais recebem um sufixo único baseado no CPF para preservar a integridade
    -- referencial.
    WITH Duplicatas AS (
        SELECT Cpf, Email,
               ROW_NUMBER() OVER (
                   PARTITION BY Email
                   ORDER BY CASE WHEN Cpf = '00000000191' THEN 0 ELSE 1 END, Cpf
               ) AS rn
        FROM Usuarios
        WHERE Email IS NOT NULL
    )
    UPDATE Duplicatas
    SET Email = Email + '_dup_' + Cpf
    WHERE rn > 1;

    CREATE UNIQUE INDEX [UX_Usuarios_Email] ON [dbo].[Usuarios]([Email]);
END
GO

-- Adiciona CodigoIngresso na tabela Reservas (código digital do ingresso)
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Reservas]') AND name = 'CodigoIngresso')
BEGIN
    ALTER TABLE Reservas ADD [CodigoIngresso] NVARCHAR(32) NOT NULL DEFAULT '';
END
GO

-- Adiciona colunas de controle de cupons (expiração e limite de usos)
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Cupons]') AND name = 'DataExpiracao')
BEGIN
    ALTER TABLE Cupons ADD [DataExpiracao] DATETIME2 NULL;
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Cupons]') AND name = 'LimiteUsos')
BEGIN
    ALTER TABLE Cupons ADD [LimiteUsos] INT NOT NULL DEFAULT 0;
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Cupons]') AND name = 'TotalUsado')
BEGIN
    ALTER TABLE Cupons ADD [TotalUsado] INT NOT NULL DEFAULT 0;
END
GO

-- Adiciona coluna SenhaTemporaria (força troca de senha no primeiro login)
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Usuarios]') AND name = 'SenhaTemporaria')
BEGIN
    ALTER TABLE Usuarios ADD [SenhaTemporaria] BIT NOT NULL DEFAULT 0;
END
GO

-- Marca o admin padrão como senha temporária (força troca no primeiro login)
UPDATE Usuarios SET SenhaTemporaria = 1 WHERE Cpf = '00000000191' AND SenhaTemporaria = 0;
GO

-- ─── Índices para performance da tabela Reservas ───────────────────────────

-- Índice em UsuarioCpf para buscas rápidas por usuário
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[dbo].[Reservas]')
      AND name = 'IX_Reservas_UsuarioCpf'
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Reservas_UsuarioCpf]
        ON [dbo].[Reservas]([UsuarioCpf])
        INCLUDE ([EventoId], [DataCompra], [ValorFinalPago]);
END
GO

-- Índice em EventoId para contagem de ingressos vendidos por evento
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[dbo].[Reservas]')
      AND name = 'IX_Reservas_EventoId'
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Reservas_EventoId]
        ON [dbo].[Reservas]([EventoId])
        INCLUDE ([UsuarioCpf], [ValorFinalPago]);
END
GO

-- Índice único em CodigoIngresso (garante unicidade do código digital do ingresso)
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[dbo].[Reservas]')
      AND name = 'UX_Reservas_CodigoIngresso'
)
BEGIN
    -- Primeiro atualiza registros existentes com código vazio para garantir unicidade
    UPDATE Reservas SET CodigoIngresso = CAST(NEWID() AS NVARCHAR(32))
    WHERE CodigoIngresso = '' OR CodigoIngresso IS NULL;

    CREATE UNIQUE NONCLUSTERED INDEX [UX_Reservas_CodigoIngresso]
        ON [dbo].[Reservas]([CodigoIngresso])
        WHERE CodigoIngresso <> '';
END
GO

-- Se o banco já existia com a senha em texto plano, atualize para o hash BCrypt:
-- UPDATE Usuarios
-- SET Senha = '$2a$11$Fhms4zc2uBueAl.VMdeJOe4JPnokxLe8b2DyOqL1J/VstjOYpVFEO'
-- WHERE Cpf = '00000000000' AND LEN(Senha) < 60;
GO
GO

-- ─── Tabela de Refresh Tokens ──────────────────────────────────────────────
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[RefreshTokens]') AND type = N'U')
BEGIN
    CREATE TABLE [dbo].[RefreshTokens] (
        [Id]          INT              IDENTITY (1, 1) NOT NULL,
        [UsuarioCpf]  CHAR(11)         NOT NULL,
        [TokenHash]   NVARCHAR(128)    NOT NULL,
        [ExpiresAt]   DATETIME2 (7)    NOT NULL,
        [CreatedAt]   DATETIME2 (7)    NOT NULL DEFAULT GETUTCDATE(),
        [RevokedAt]   DATETIME2 (7)    NULL,
        CONSTRAINT [PK_RefreshTokens] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_RefreshTokens_Usuarios] FOREIGN KEY ([UsuarioCpf]) REFERENCES [Usuarios]([Cpf])
    );
END
GO

-- Índice para busca rápida por TokenHash
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[dbo].[RefreshTokens]')
      AND name = 'IX_RefreshTokens_TokenHash'
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_RefreshTokens_TokenHash]
        ON [dbo].[RefreshTokens]([TokenHash])
        INCLUDE ([UsuarioCpf], [ExpiresAt], [RevokedAt]);
END
GO

-- ─── Soft Delete / Auditoria na tabela Reservas ───────────────────────────

-- Adiciona coluna Status na tabela Reservas (Ativa, Cancelada)
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Reservas]') AND name = 'Status')
BEGIN
    ALTER TABLE Reservas ADD [Status] VARCHAR(20) NOT NULL DEFAULT 'Ativa'
        CONSTRAINT [CK_Reservas_Status] CHECK ([Status] IN ('Ativa', 'Cancelada'));
END
GO

-- Adiciona coluna DataCancelamento na tabela Reservas
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Reservas]') AND name = 'DataCancelamento')
BEGIN
    ALTER TABLE Reservas ADD [DataCancelamento] DATETIME2 (7) NULL;
END
GO

-- Adiciona coluna MotivoCancelamento na tabela Reservas
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Reservas]') AND name = 'MotivoCancelamento')
BEGIN
    ALTER TABLE Reservas ADD [MotivoCancelamento] NVARCHAR(500) NULL;
END
GO

-- ─── Check-in / Validação Física na Entrada ──────────────────────────────

-- Adiciona coluna DataCheckin na tabela Reservas (registra quando o ingresso foi validado na entrada)
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Reservas]') AND name = 'DataCheckin')
BEGIN
    ALTER TABLE Reservas ADD [DataCheckin] DATETIME2 (7) NULL;
END
GO

-- Atualiza a constraint CK_Reservas_Status para incluir o status 'Usada' (check-in realizado)
IF EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE object_id = OBJECT_ID(N'[dbo].[CK_Reservas_Status]')
      AND parent_object_id = OBJECT_ID(N'[dbo].[Reservas]')
      AND definition = '([Status]=''Ativa'' OR [Status]=''Cancelada'')'
)
BEGIN
    ALTER TABLE Reservas DROP CONSTRAINT [CK_Reservas_Status];
    ALTER TABLE Reservas ADD CONSTRAINT [CK_Reservas_Status]
        CHECK ([Status] IN ('Ativa', 'Usada', 'Cancelada'));
END
GO

-- ─── Email Verification Columns ────────────────────────────────────────────
-- Adiciona colunas para verificação de email
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Usuarios]') AND name = 'EmailVerificado')
BEGIN
    -- DEFAULT 0 para novas inserções (novos cadastros precisam verificar o email).
    ALTER TABLE Usuarios ADD [EmailVerificado] BIT NOT NULL DEFAULT 0;
    -- Marca os usuários já existentes como verificados para evitar lock-out
    -- de contas criadas antes desta funcionalidade ser introduzida.
    -- Este UPDATE só executa na primeira vez (dentro do IF NOT EXISTS).
    UPDATE Usuarios SET EmailVerificado = 1;
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Usuarios]') AND name = 'TokenVerificacaoEmail')
BEGIN
    ALTER TABLE Usuarios ADD [TokenVerificacaoEmail] NVARCHAR(100) NULL;
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Usuarios]') AND name = 'TokenExpiracaoEmail')
BEGIN
    ALTER TABLE Usuarios ADD [TokenExpiracaoEmail] DATETIME2(7) NULL;
END
GO

-- ─── Password Recovery Columns ──────────────────────────────────────────
-- Adiciona colunas para redefinição de senha via token enviado por email
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Usuarios]') AND name = 'ResetToken')
BEGIN
    ALTER TABLE Usuarios ADD [ResetToken] NVARCHAR(100) NULL;
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Usuarios]') AND name = 'ResetTokenExpiracao')
BEGIN
    ALTER TABLE Usuarios ADD [ResetTokenExpiracao] DATETIME2(7) NULL;
END
GO

-- ─── Tabela de Auditoria (legada, não mais utilizada) ─────────────────────
-- NOTA: Esta tabela foi substituída pela tabela AuditLog abaixo, que oferece
-- encadeamento criptográfico de hash (blockchain-like) garantindo integridade.
-- Mantida no schema apenas para compatibilidade com ambientes existentes.
-- NÃO inserir dados aqui — use AuditLogRepository em vez disso.
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Auditoria]') AND type = N'U')
BEGIN
    CREATE TABLE [dbo].[Auditoria] (
        [Id]          INT              IDENTITY (1, 1) NOT NULL,
        [Tabela]      VARCHAR(50)      NOT NULL,
        [RegistroId]  INT              NULL,
        [Acao]        VARCHAR(20)      NOT NULL,
        [UsuarioCpf]  CHAR(11)         NULL,
        [ValoresAntigos] NVARCHAR(MAX) NULL,
        [ValoresNovos]   NVARCHAR(MAX) NULL,
        [DataHora]    DATETIME2 (7)    NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT [PK_Auditoria] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
END
GO

-- ─── Tabela de Auditoria Financeira Imutável (AuditLog) ────────────────────
-- Cada transação financeira sensível é registrada com trilha hash SHA256:
--   - Quem: UsuarioCpf
--   - De onde: IpAddress + UserAgent
--   - Quando: Timestamp (UTC)
--   - O quê: ActionType + Detalhes (JSON)
--   - Valor financeiro: ValorTransacionado
--   - Integridade: PreviousHash + Hash (encadeamento blockchain-like)
--
-- A chain de hashs garante que qualquer alteração em registros passados
-- quebre a cadeia, tornando a auditoria à prova de adulteração.
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[AuditLog]') AND type = N'U')
BEGIN
    CREATE TABLE [dbo].[AuditLog] (
        [Id]                 INT              IDENTITY (1, 1) NOT NULL,
        [Timestamp]          DATETIME2 (7)    NOT NULL DEFAULT GETUTCDATE(),
        [ActionType]         VARCHAR (50)     NOT NULL,
        [UsuarioCpf]         CHAR (11)        NULL,
        [EventoId]           INT              NULL,
        [ReservaId]          INT              NULL,
        [ValorTransacionado] DECIMAL (18, 2)  NULL,
        [IpAddress]          VARCHAR (45)     NOT NULL DEFAULT 'unknown',
        [UserAgent]          NVARCHAR (500)   NULL,
        [Detalhes]           NVARCHAR (MAX)   NULL,
        [PreviousHash]       VARCHAR (64)     NOT NULL DEFAULT '0',
        [Hash]               VARCHAR (64)     NOT NULL,

        CONSTRAINT [PK_AuditLog] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
END
GO

-- Índice para consultas de auditoria por usuário (CPF)
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[dbo].[AuditLog]')
      AND name = 'IX_AuditLog_UsuarioCpf'
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_AuditLog_UsuarioCpf]
        ON [dbo].[AuditLog]([UsuarioCpf] ASC)
        INCLUDE ([Timestamp], [ActionType], [ValorTransacionado]);
END
GO

-- Índice para consultas de auditoria por período
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[dbo].[AuditLog]')
      AND name = 'IX_AuditLog_Timestamp'
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_AuditLog_Timestamp]
        ON [dbo].[AuditLog]([Timestamp] DESC);
END
GO

-- Índice para consultas de auditoria por tipo de ação
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[dbo].[AuditLog]')
      AND name = 'IX_AuditLog_ActionType'
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_AuditLog_ActionType]
        ON [dbo].[AuditLog]([ActionType] ASC)
        INCLUDE ([Timestamp], [UsuarioCpf], [ValorTransacionado]);
END
GO

-- Índice único para garantir a integridade da chain de hashs
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[dbo].[AuditLog]')
      AND name = 'UX_AuditLog_Hash'
)
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX [UX_AuditLog_Hash]
        ON [dbo].[AuditLog]([Hash]);
END
GO

-- ─── Full-Text Search para busca de eventos ──────────────────────────────────
-- Habilita full-text search na tabela Eventos para busca com relevância,
-- eliminando o uso de LIKE '%termo%' que causa full table scan.

-- Cria o catálogo full-text (caso não exista)
IF NOT EXISTS (SELECT * FROM sys.fulltext_catalogs WHERE name = 'FT_TicketPrime')
BEGIN
    CREATE FULLTEXT CATALOG FT_TicketPrime AS DEFAULT;
END
GO

-- Cria o índice full-text na tabela Eventos (caso não exista)
-- Abrange: Nome (busca principal), Descricao (descrição do evento),
--          Local (localização), GeneroMusical (gênero musical)
-- Usa LANGUAGE 1046 (Português Brasileiro) para stemming e thesaurus
IF NOT EXISTS (
    SELECT 1 FROM sys.fulltext_indexes
    WHERE object_id = OBJECT_ID(N'[dbo].[Eventos]')
)
BEGIN
    CREATE FULLTEXT INDEX ON [dbo].[Eventos](
        Nome LANGUAGE 1046,
        Descricao LANGUAGE 1046,
        Local LANGUAGE 1046,
        GeneroMusical LANGUAGE 1046
    )
    KEY INDEX [PK_Eventos]
    ON FT_TicketPrime
    WITH (CHANGE_TRACKING AUTO);
END
GO

-- ─── Adiciona coluna ThumbnailBase64 na tabela EventoFotos ─────────────────────
-- Necessário para exibir thumbnails de eventos na vitrine pública sem quebrar
-- a criptografia E2E das fotos originais.
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[EventoFotos]') AND name = 'ThumbnailBase64')
BEGIN
    ALTER TABLE EventoFotos ADD ThumbnailBase64 NVARCHAR(MAX) NULL;
END
GO

-- ─── Cupons: novos campos para marketing ──────────────────────────────────────
-- Suporte a desconto de valor fixo, cupom por categoria e cupom de primeiro acesso.

-- TipoDesconto: 0 = Percentual (clássico), 1 = ValorFixo
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Cupons]') AND name = 'TipoDesconto')
BEGIN
    ALTER TABLE Cupons ADD [TipoDesconto] INT NOT NULL DEFAULT 0
        CONSTRAINT [CK_Cupons_TipoDesconto] CHECK ([TipoDesconto] IN (0, 1));
END
GO

-- ValorDescontoFixo: valor em reais (usado quando TipoDesconto = 1)
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Cupons]') AND name = 'ValorDescontoFixo')
BEGIN
    ALTER TABLE Cupons ADD [ValorDescontoFixo] DECIMAL(18, 2) NULL
        CONSTRAINT [CK_Cupons_ValorDescontoFixo] CHECK ([ValorDescontoFixo] IS NULL OR [ValorDescontoFixo] > 0);
END
GO

-- CategoriaEvento: se preenchido, cupom só vale para eventos deste gênero musical
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Cupons]') AND name = 'CategoriaEvento')
BEGIN
    ALTER TABLE Cupons ADD [CategoriaEvento] NVARCHAR(100) NULL;
END
GO

-- PrimeiroAcesso: se true, cupom só vale na primeira compra do usuário
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Cupons]') AND name = 'PrimeiroAcesso')
BEGIN
    ALTER TABLE Cupons ADD [PrimeiroAcesso] BIT NOT NULL DEFAULT 0;
END
GO

-- Remove a antiga constraint CK_Cupons_Desconto que limitava PorcentagemDesconto entre 0 e 100
-- (agora o desconto pode ser 0 se o tipo for ValorFixo)
IF EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE object_id = OBJECT_ID(N'[dbo].[CK_Cupons_Desconto]')
      AND parent_object_id = OBJECT_ID(N'[dbo].[Cupons]')
)
BEGIN
    ALTER TABLE Cupons DROP CONSTRAINT [CK_Cupons_Desconto];
END
GO

-- Recria a constraint com validação condicional: se TipoDesconto = 0, PorcentagemDesconto deve estar entre 0 e 100
IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE object_id = OBJECT_ID(N'[dbo].[CK_Cupons_Desconto]')
      AND parent_object_id = OBJECT_ID(N'[dbo].[Cupons]')
)
BEGIN
    ALTER TABLE Cupons ADD CONSTRAINT [CK_Cupons_Desconto]
        CHECK (
            (TipoDesconto = 0 AND PorcentagemDesconto >= 0 AND PorcentagemDesconto <= 100)
            OR
            (TipoDesconto = 1 AND PorcentagemDesconto = 0)
        );
END
GO

-- ─── Fila de Espera para Eventos Lotados ─────────────────────────────────────
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[FilaEspera]') AND type = N'U')
BEGIN
    CREATE TABLE [dbo].[FilaEspera] (
        [Id]              INT            IDENTITY (1, 1) NOT NULL,
        [UsuarioCpf]      CHAR(11)       NOT NULL,
        [EventoId]        INT            NOT NULL,
        [DataEntrada]     DATETIME2 (7)  NOT NULL DEFAULT GETUTCDATE(),
        [Status]          VARCHAR(20)    NOT NULL DEFAULT 'Aguardando',
        [DataNotificacao] DATETIME2 (7)  NULL,

        CONSTRAINT [PK_FilaEspera] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_FilaEspera_Usuarios] FOREIGN KEY ([UsuarioCpf]) REFERENCES [Usuarios]([Cpf]),
        CONSTRAINT [FK_FilaEspera_Eventos] FOREIGN KEY ([EventoId]) REFERENCES [Eventos]([Id]) ON DELETE CASCADE,
        CONSTRAINT [CK_FilaEspera_Status] CHECK ([Status] IN ('Aguardando', 'Notificado', 'Expirado', 'Confirmado'))
    );
END
GO

-- Índice para buscar fila por evento (ordem de entrada)
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[dbo].[FilaEspera]')
      AND name = 'IX_FilaEspera_EventoId_Status'
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_FilaEspera_EventoId_Status]
        ON [dbo].[FilaEspera]([EventoId] ASC, [Status] ASC)
        INCLUDE ([UsuarioCpf], [DataEntrada]);
END
GO

-- Índice para buscar fila por usuário
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[dbo].[FilaEspera]')
      AND name = 'IX_FilaEspera_UsuarioCpf'
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_FilaEspera_UsuarioCpf]
        ON [dbo].[FilaEspera]([UsuarioCpf] ASC)
        INCLUDE ([EventoId], [Status], [DataEntrada]);
END
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- NOVO: Tipos de Ingresso (Setores) e Lotes Progressivos
-- ═══════════════════════════════════════════════════════════════════════════════

-- ─── Tabela TiposIngresso (setores: Pista, VIP, Camarote, etc.) ────────────
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[TiposIngresso]') AND type = N'U')
BEGIN
    CREATE TABLE [dbo].[TiposIngresso] (
        [Id]                INT             IDENTITY (1, 1) NOT NULL,
        [EventoId]          INT             NOT NULL,
        [Nome]              NVARCHAR (100)  NOT NULL,
        [Descricao]         NVARCHAR (500)  NULL,
        [Preco]             DECIMAL (18, 2) NOT NULL,
        [CapacidadeTotal]   INT             NOT NULL,
        [CapacidadeRestante] INT            NOT NULL,
        [Ordem]             INT             NOT NULL DEFAULT 0,

        CONSTRAINT [PK_TiposIngresso] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_TiposIngresso_Eventos] FOREIGN KEY ([EventoId])
            REFERENCES [Eventos]([Id]) ON DELETE CASCADE,
        CONSTRAINT [CK_TiposIngresso_Preco] CHECK ([Preco] >= 0),
        CONSTRAINT [CK_TiposIngresso_Capacidade] CHECK ([CapacidadeTotal] > 0 AND [CapacidadeRestante] >= 0)
    );
END
GO

-- ─── Tabela Lotes (progressivos: 1º Lote, 2º Lote, etc.) ───────────────────
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Lotes]') AND type = N'U')
BEGIN
    CREATE TABLE [dbo].[Lotes] (
        [Id]                INT             IDENTITY (1, 1) NOT NULL,
        [EventoId]          INT             NOT NULL,
        [TicketTypeId]      INT             NULL,
        [Nome]              NVARCHAR (100)  NOT NULL,
        [Preco]             DECIMAL (18, 2) NOT NULL,
        [QuantidadeMaxima]  INT             NOT NULL,
        [QuantidadeVendida] INT             NOT NULL DEFAULT 0,
        [DataInicio]        DATETIME2 (7)   NULL,
        [DataFim]           DATETIME2 (7)   NULL,
        [Ativo]             BIT             NOT NULL DEFAULT 1,

        CONSTRAINT [PK_Lotes] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_Lotes_Eventos] FOREIGN KEY ([EventoId])
            REFERENCES [Eventos]([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_Lotes_TiposIngresso] FOREIGN KEY ([TicketTypeId])
            REFERENCES [TiposIngresso]([Id]),
        CONSTRAINT [CK_Lotes_Preco] CHECK ([Preco] >= 0),
        CONSTRAINT [CK_Lotes_Quantidade] CHECK ([QuantidadeMaxima] > 0 AND [QuantidadeVendida] >= 0)
    );
END
GO

-- ─── Colunas TicketTypeId e LoteId na tabela Reservas ──────────────────────
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Reservas]') AND name = 'TicketTypeId')
BEGIN
    ALTER TABLE Reservas ADD [TicketTypeId] INT NOT NULL DEFAULT 1
        CONSTRAINT [FK_Reservas_TiposIngresso] FOREIGN KEY ([TicketTypeId])
            REFERENCES [TiposIngresso]([Id]);
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Reservas]') AND name = 'LoteId')
BEGIN
    ALTER TABLE Reservas ADD [LoteId] INT NULL
        CONSTRAINT [FK_Reservas_Lotes] FOREIGN KEY ([LoteId])
            REFERENCES [Lotes]([Id]);
END
GO

-- ─── Índices para performance ─────────────────────────────────────────────

-- Índice em EventoId para buscar tipos de ingresso por evento
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[dbo].[TiposIngresso]')
      AND name = 'IX_TiposIngresso_EventoId'
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_TiposIngresso_EventoId]
        ON [dbo].[TiposIngresso]([EventoId])
        INCLUDE ([Nome], [Preco], [CapacidadeTotal], [CapacidadeRestante], [Ordem]);
END
GO

-- Índice em EventoId para buscar lotes por evento
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[dbo].[Lotes]')
      AND name = 'IX_Lotes_EventoId'
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Lotes_EventoId]
        ON [dbo].[Lotes]([EventoId])
        INCLUDE ([TicketTypeId], [Nome], [Preco], [QuantidadeMaxima], [QuantidadeVendida], [DataInicio], [DataFim], [Ativo]);
END
GO

-- Índice em TicketTypeId na tabela Reservas para buscas por tipo de ingresso
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[dbo].[Reservas]')
      AND name = 'IX_Reservas_TicketTypeId'
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Reservas_TicketTypeId]
        ON [dbo].[Reservas]([TicketTypeId])
        INCLUDE ([EventoId], [Status]);
END
GO

-- Índice único para evitar duplicatas (um usuário só pode ter uma entrada ativa por evento)
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[dbo].[FilaEspera]')
      AND name = 'UX_FilaEspera_UsuarioEvento'
)
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX [UX_FilaEspera_UsuarioEvento]
        ON [dbo].[FilaEspera]([UsuarioCpf] ASC, [EventoId] ASC)
        WHERE [Status] = 'Aguardando';
END
GO

-- ─── Índices de alta consulta em Eventos (Status e DataEvento) ────────────────
-- Queries de vitrine e painel admin filtram frequentemente por Status e DataEvento.
-- Sem índice, um full scan é feito em toda a tabela a cada busca.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[dbo].[Eventos]')
      AND name = 'IX_Eventos_Status'
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Eventos_Status]
        ON [dbo].[Eventos]([Status])
        INCLUDE ([Id], [Nome], [DataEvento], [Local], [PrecoPadrao], [CapacidadeTotal], [EventoGratuito]);
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[dbo].[Eventos]')
      AND name = 'IX_Eventos_DataEvento'
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Eventos_DataEvento]
        ON [dbo].[Eventos]([DataEvento] ASC)
        INCLUDE ([Id], [Nome], [Status], [Local], [PrecoPadrao], [CapacidadeTotal]);
END
GO

-- Índice composto Status+DataEvento para queries de vitrine filtradas e ordenadas por data
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[dbo].[Eventos]')
      AND name = 'IX_Eventos_Status_DataEvento'
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Eventos_Status_DataEvento]
        ON [dbo].[Eventos]([Status] ASC, [DataEvento] ASC)
        INCLUDE ([Id], [Nome], [Local], [PrecoPadrao], [CapacidadeTotal], [GeneroMusical], [EventoGratuito]);
END
GO

-- ─── Campo Telefone nos Usuários ──────────────────────────────────────────────
-- Permite contato por WhatsApp/SMS e recuperação de conta via telefone.
-- Opcional: usuários existentes não precisam preencher imediatamente.
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Usuarios]') AND name = 'Telefone')
BEGIN
    ALTER TABLE Usuarios ADD [Telefone] NVARCHAR(20) NULL;
END
GO

-- ─── Campo OrganizadorCpf nos Eventos ─────────────────────────────────────────
-- Vincula cada evento ao CPF do admin/organizador que o criou.
-- Permite exibir uma página pública de perfil do organizador.
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Eventos]') AND name = 'OrganizadorCpf')
BEGIN
    ALTER TABLE Eventos ADD [OrganizadorCpf] CHAR(11) NULL;
END
GO

-- ─── Tabela de Avaliações de Eventos ─────────────────────────────────────────
-- Permite que compradores avaliem eventos após sua realização (nota 1-5).
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Avaliacoes]') AND type = N'U')
BEGIN
    CREATE TABLE [dbo].[Avaliacoes] (
        [Id]             INT            IDENTITY(1,1) NOT NULL,
        [UsuarioCpf]     CHAR(11)       NOT NULL,
        [EventoId]       INT            NOT NULL,
        [Nota]           TINYINT        NOT NULL CONSTRAINT [CK_Avaliacoes_Nota] CHECK ([Nota] BETWEEN 1 AND 5),
        [Comentario]     NVARCHAR(1000) NULL,
        [DataAvaliacao]  DATETIME2      NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT [PK_Avaliacoes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Avaliacoes_Usuarios] FOREIGN KEY ([UsuarioCpf]) REFERENCES [dbo].[Usuarios]([Cpf]),
        CONSTRAINT [FK_Avaliacoes_Eventos]  FOREIGN KEY ([EventoId])   REFERENCES [dbo].[Eventos]([Id]),
        CONSTRAINT [UQ_Avaliacoes_Usuario_Evento] UNIQUE ([UsuarioCpf], [EventoId])
    );
    CREATE INDEX [IX_Avaliacoes_EventoId] ON [dbo].[Avaliacoes] ([EventoId]);
END
GO

-- Adiciona coluna Anonima na tabela Avaliacoes (avaliação anônima)
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Avaliacoes]') AND name = 'Anonima')
BEGIN
    ALTER TABLE Avaliacoes ADD [Anonima] BIT NOT NULL DEFAULT 0;
END
GO

-- ─── Migrações incrementais (idempotentes) ─────────────────────────────────
-- Reservas: colunas de rastreamento de transação de gateway e estorno
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'dbo.Reservas') AND name = 'CodigoTransacaoGateway')
    ALTER TABLE [dbo].[Reservas] ADD [CodigoTransacaoGateway] NVARCHAR(100) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'dbo.Reservas') AND name = 'IdEstornoGateway')
    ALTER TABLE [dbo].[Reservas] ADD [IdEstornoGateway] NVARCHAR(100) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'dbo.Reservas') AND name = 'DataEstorno')
    ALTER TABLE [dbo].[Reservas] ADD [DataEstorno] DATETIME2(7) NULL;
GO

-- Reservas: status de pagamento (ex: 'approved', 'pending', 'rejected', 'refunded')
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'dbo.Reservas') AND name = 'StatusPagamento')
    ALTER TABLE [dbo].[Reservas] ADD [StatusPagamento] NVARCHAR(30) NULL;
GO

-- Reservas: chave de idempotência para evitar cobrança duplicada no gateway
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'dbo.Reservas') AND name = 'IdempotencyKey')
    ALTER TABLE [dbo].[Reservas] ADD [IdempotencyKey] NVARCHAR(100) NULL;
GO

-- Eventos: URL de imagem de capa e categoria
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'dbo.Eventos') AND name = 'ImagemUrl')
    ALTER TABLE [dbo].[Eventos] ADD [ImagemUrl] NVARCHAR(500) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'dbo.Eventos') AND name = 'Categoria')
    ALTER TABLE [dbo].[Eventos] ADD [Categoria] NVARCHAR(100) NOT NULL DEFAULT '';
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- MEIA-ENTRADA: Comprovante de Elegibilidade (Lei 12.933/2013)
-- ═══════════════════════════════════════════════════════════════════════════════
-- Tabela para armazenar documentos comprobatórios de meia-entrada.
-- Exige que o comprador faça upload de documento (carteirinha estudantil,
-- identidade de idoso, laudo médico, etc.) e permite que o ADMIN
-- verifique e aprove/rejeite o documento posteriormente.
-- Caso o documento seja rejeitado, a diferença entre o valor da inteira
-- e o valor pago deve ser cobrada do usuário.
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[MeiaEntradaDocumentos]') AND type = N'U')
BEGIN
    CREATE TABLE [dbo].[MeiaEntradaDocumentos] (
        [Id]                INT             IDENTITY (1, 1) NOT NULL,
        [ReservaId]         INT             NULL,
        [UsuarioCpf]        CHAR(11)        NOT NULL,
        [EventoId]          INT             NOT NULL,

        -- Arquivo
        [CaminhoArquivo]    NVARCHAR(500)   NOT NULL,
        [NomeOriginal]      NVARCHAR(200)   NOT NULL,
        [TipoMime]          VARCHAR(50)     NOT NULL,
        [TamanhoBytes]      BIGINT          NOT NULL DEFAULT 0,

        -- Status da verificação
        [Status]            VARCHAR(20)     NOT NULL DEFAULT 'Pendente',
        [DataUpload]        DATETIME2(7)    NOT NULL DEFAULT GETUTCDATE(),
        [DataVerificacao]   DATETIME2(7)    NULL,
        [VerificadoPorCpf]  CHAR(11)        NULL,
        [MotivoRejeicao]    NVARCHAR(500)   NULL,

        CONSTRAINT [PK_MeiaEntradaDocumentos] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_MeiaEntradaDocumentos_Reservas] FOREIGN KEY ([ReservaId])
            REFERENCES [dbo].[Reservas]([Id]),
        CONSTRAINT [FK_MeiaEntradaDocumentos_Usuarios] FOREIGN KEY ([UsuarioCpf])
            REFERENCES [dbo].[Usuarios]([Cpf]),
        CONSTRAINT [FK_MeiaEntradaDocumentos_Eventos] FOREIGN KEY ([EventoId])
            REFERENCES [dbo].[Eventos]([Id]),
        CONSTRAINT [CK_MeiaEntradaDocumentos_Status]
            CHECK ([Status] IN ('Pendente', 'Aprovado', 'Rejeitado')),
        CONSTRAINT [CK_MeiaEntradaDocumentos_TamanhoBytes]
            CHECK ([TamanhoBytes] >= 0)
    );
END
GO

-- Índice para buscar documentos por reserva
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[dbo].[MeiaEntradaDocumentos]')
      AND name = 'IX_MeiaEntradaDocumentos_ReservaId'
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_MeiaEntradaDocumentos_ReservaId]
        ON [dbo].[MeiaEntradaDocumentos]([ReservaId])
        INCLUDE ([Status], [DataUpload]);
END
GO

-- Índice para buscas do admin por status (pendentes primeiro)
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[dbo].[MeiaEntradaDocumentos]')
      AND name = 'IX_MeiaEntradaDocumentos_Status'
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_MeiaEntradaDocumentos_Status]
        ON [dbo].[MeiaEntradaDocumentos]([Status] ASC)
        INCLUDE ([ReservaId], [UsuarioCpf], [EventoId], [DataUpload]);
END
GO

-- Índice para buscar por usuário (histórico de documentos do usuário)
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[dbo].[MeiaEntradaDocumentos]')
      AND name = 'IX_MeiaEntradaDocumentos_UsuarioCpf'
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_MeiaEntradaDocumentos_UsuarioCpf]
        ON [dbo].[MeiaEntradaDocumentos]([UsuarioCpf] ASC)
        INCLUDE ([ReservaId], [Status]);
END
GO

-- ─── Slug único opaco nos Usuários ───────────────────────────────────────────
-- Slug público (16 chars hex) usado em URLs no lugar do CPF para evitar enumeração.
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Usuarios]') AND name = 'Slug')
BEGIN
    ALTER TABLE Usuarios ADD [Slug] VARCHAR(32) NULL;
END
GO

-- Índice único filtrado para busca rápida por slug (apenas registros com slug preenchido)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[Usuarios]') AND name = 'IX_Usuarios_Slug')
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX [IX_Usuarios_Slug]
        ON [dbo].[Usuarios]([Slug] ASC)
        WHERE [Slug] IS NOT NULL;
END
GO

-- ─── Eventos.CapacidadeRestante (contador mutável de vagas) ──────────────────────────
-- CapacidadeTotal → valor imutável (capacidade máxima do espaço)
-- CapacidadeRestante → decrementado em compra, incrementado em cancelamento
-- Separa a semântica dos dois valores para evitar corrupção de dados.
IF NOT EXISTS (
    SELECT * FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[dbo].[Eventos]') AND name = 'CapacidadeRestante'
)
BEGIN
    ALTER TABLE [dbo].[Eventos] ADD [CapacidadeRestante] INT NOT NULL DEFAULT 0;
    -- Backfill: eventos existentes começam com CapacidadeRestante = CapacidadeTotal
    -- ⚠ Use dynamic SQL because the column doesn't exist at batch compile time
    --   on existing databases, causing "Invalid column name" at parse time.
    EXEC sp_executesql N'
        UPDATE [dbo].[Eventos] SET [CapacidadeRestante] = [CapacidadeTotal]
        WHERE [CapacidadeRestante] = 0;
    ';
END
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- ÍNDICES RECOMENDADOS — Performance para consultas frequentes
-- ═══════════════════════════════════════════════════════════════════════════════

-- Índice para filtragem de reservas por status (ativo/cancelado)
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[dbo].[Reservas]') AND name = 'IX_Reservas_Status_DataCompra'
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Reservas_Status_DataCompra]
        ON [dbo].[Reservas]([Status])
        INCLUDE ([DataCompra], [UsuarioCpf]);
END
GO

-- Índice para listagem de eventos disponíveis (vitrine)
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[dbo].[Eventos]') AND name = 'IX_Eventos_Status_DataEvento'
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Eventos_Status_DataEvento]
        ON [dbo].[Eventos]([Status], [DataEvento])
        INCLUDE ([Id], [Nome], [CapacidadeRestante], [PrecoPadrao]);
END
GO

-- Índice para validação de cupom na compra (expiração e limite de usos)
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[dbo].[Cupons]') AND name = 'IX_Cupons_Expiracao'
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Cupons_Expiracao]
        ON [dbo].[Cupons]([DataExpiracao], [TotalUsado], [LimiteUsos])
        INCLUDE ([PorcentagemDesconto], [ValorMinimoRegra]);
END
GO

-- Índice para trilha de auditoria por usuário
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[dbo].[AuditLog]') AND name = 'IX_AuditLog_UsuarioCpf_Timestamp'
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_AuditLog_UsuarioCpf_Timestamp]
        ON [dbo].[AuditLog]([UsuarioCpf], [Timestamp] DESC)
        INCLUDE ([ActionType]);
END
GO

-- Índice para busca de usuário verificado por email
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[dbo].[Usuarios]') AND name = 'IX_Usuarios_Email_Verificado'
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Usuarios_Email_Verificado]
        ON [dbo].[Usuarios]([Email])
        WHERE [EmailVerificado] = 1;
END
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- FAVORITOS / WISHLIST — Eventos favoritados pelo usuário
-- ═══════════════════════════════════════════════════════════════════════════════
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Favoritos]') AND type = N'U')
BEGIN
    CREATE TABLE [dbo].[Favoritos] (
        [Id]             INT           IDENTITY (1, 1) NOT NULL,
        [UsuarioCpf]     CHAR(11)      NOT NULL,
        [EventoId]       INT           NOT NULL,
        [DataFavoritado] DATETIME2 (7) NOT NULL DEFAULT GETUTCDATE(),

        CONSTRAINT [PK_Favoritos] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_Favoritos_Usuarios] FOREIGN KEY ([UsuarioCpf]) REFERENCES [Usuarios]([Cpf]),
        CONSTRAINT [FK_Favoritos_Eventos]  FOREIGN KEY ([EventoId])   REFERENCES [Eventos]([Id]) ON DELETE CASCADE,
        CONSTRAINT [UQ_Favoritos_Usuario_Evento] UNIQUE ([UsuarioCpf], [EventoId])
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[Favoritos]') AND name = 'IX_Favoritos_UsuarioCpf')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Favoritos_UsuarioCpf]
        ON [dbo].[Favoritos]([UsuarioCpf] ASC)
        INCLUDE ([EventoId], [DataFavoritado]);
END
GO
