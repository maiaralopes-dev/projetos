IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109011900_Inicial'
)
BEGIN
    CREATE TABLE [Tarefas] (
        [Id] int NOT NULL IDENTITY,
        [Titulo] nvarchar(120) NOT NULL,
        [Descricao] nvarchar(1000) NULL,
        [Situacao] nvarchar(20) NOT NULL,
        [Prioridade] nvarchar(10) NOT NULL,
        [Prazo] date NULL,
        [CriadaEm] datetimeoffset NOT NULL DEFAULT (SYSDATETIMEOFFSET()),
        [Versao] rowversion NOT NULL,
        CONSTRAINT [PK_Tarefas] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109011900_Inicial'
)
BEGIN
    CREATE TABLE [EventosAuditoria] (
        [Id] bigint NOT NULL IDENTITY,
        [TarefaId] int NOT NULL,
        [Acao] nvarchar(50) NOT NULL,
        [Detalhe] nvarchar(500) NULL,
        [Usuario] nvarchar(100) NULL,
        [OcorridoEm] datetimeoffset NOT NULL,
        CONSTRAINT [PK_EventosAuditoria] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EventosAuditoria_Tarefas_TarefaId] FOREIGN KEY ([TarefaId]) REFERENCES [Tarefas] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109011900_Inicial'
)
BEGIN
    CREATE INDEX [IX_EventosAuditoria_TarefaId_OcorridoEm] ON [EventosAuditoria] ([TarefaId], [OcorridoEm]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109011900_Inicial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Tarefas_Titulo] ON [Tarefas] ([Titulo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109011900_Inicial'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261109011900_Inicial', N'10.0.14');
END;

COMMIT;
GO

