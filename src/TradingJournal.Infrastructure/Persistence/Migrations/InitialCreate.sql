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
    WHERE [MigrationId] = N'20260926192041_InitialCreate'
)
BEGIN
    CREATE TABLE [Emotions] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(50) NOT NULL,
        [Description] nvarchar(250) NULL,
        CONSTRAINT [PK_Emotions] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926192041_InitialCreate'
)
BEGIN
    CREATE TABLE [Users] (
        [Id] uniqueidentifier NOT NULL,
        [DisplayName] nvarchar(100) NOT NULL,
        [Email] nvarchar(255) NOT NULL,
        [PasswordHash] nvarchar(500) NOT NULL,
        [EmailVerified] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [LastLoginAt] datetime2 NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926192041_InitialCreate'
)
BEGIN
    CREATE TABLE [Strategies] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NULL,
        [Name] nvarchar(100) NOT NULL,
        [Description] nvarchar(1000) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_Strategies] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Strategies_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926192041_InitialCreate'
)
BEGIN
    CREATE TABLE [Trades] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [MarketType] int NOT NULL,
        [Symbol] nvarchar(30) NOT NULL,
        [Direction] int NOT NULL,
        [PositionSize] decimal(18,6) NOT NULL,
        [EntryDateTime] datetime2 NOT NULL,
        [EntryPrice] decimal(18,8) NOT NULL,
        [StopLoss] decimal(18,8) NULL,
        [TakeProfit] decimal(18,8) NULL,
        [ExitDateTime] datetime2 NULL,
        [ExitPrice] decimal(18,8) NULL,
        [ResultAmount] decimal(18,2) NULL,
        [Status] int NOT NULL,
        [StrategyId] uniqueidentifier NULL,
        [EntryEmotionId] uniqueidentifier NULL,
        [ExitEmotionId] uniqueidentifier NULL,
        [EntryReason] nvarchar(1000) NULL,
        [ExitReason] nvarchar(1000) NULL,
        [PlanRespected] bit NULL,
        [Notes] nvarchar(2000) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_Trades] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Trades_ClosedRequiresExit] CHECK ([Status] IN (0, 1) AND ([Status] <> 1 OR ([ExitDateTime] IS NOT NULL AND [ExitPrice] IS NOT NULL AND [ResultAmount] IS NOT NULL))),
        CONSTRAINT [FK_Trades_Emotions_EntryEmotionId] FOREIGN KEY ([EntryEmotionId]) REFERENCES [Emotions] ([Id]),
        CONSTRAINT [FK_Trades_Emotions_ExitEmotionId] FOREIGN KEY ([ExitEmotionId]) REFERENCES [Emotions] ([Id]),
        CONSTRAINT [FK_Trades_Strategies_StrategyId] FOREIGN KEY ([StrategyId]) REFERENCES [Strategies] ([Id]),
        CONSTRAINT [FK_Trades_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926192041_InitialCreate'
)
BEGIN
    CREATE TABLE [TradeImages] (
        [Id] uniqueidentifier NOT NULL,
        [TradeId] uniqueidentifier NOT NULL,
        [StorageKey] nvarchar(500) NOT NULL,
        [ImageType] int NOT NULL,
        [FileName] nvarchar(255) NOT NULL,
        [ContentType] nvarchar(100) NOT NULL,
        [DisplayOrder] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_TradeImages] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TradeImages_Trades_TradeId] FOREIGN KEY ([TradeId]) REFERENCES [Trades] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926192041_InitialCreate'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Description', N'Name') AND [object_id] = OBJECT_ID(N'[Emotions]'))
        SET IDENTITY_INSERT [Emotions] ON;
    EXEC(N'INSERT INTO [Emotions] ([Id], [Description], [Name])
    VALUES (''10000000-0000-0000-0000-000000000001'', NULL, N''Calme''),
    (''10000000-0000-0000-0000-000000000002'', NULL, N''Confiant''),
    (''10000000-0000-0000-0000-000000000003'', NULL, N''Stressé''),
    (''10000000-0000-0000-0000-000000000004'', NULL, N''Impatient''),
    (''10000000-0000-0000-0000-000000000005'', NULL, N''Euphorique''),
    (''10000000-0000-0000-0000-000000000006'', NULL, N''Frustré''),
    (''10000000-0000-0000-0000-000000000007'', NULL, N''Craintif'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Description', N'Name') AND [object_id] = OBJECT_ID(N'[Emotions]'))
        SET IDENTITY_INSERT [Emotions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926192041_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Strategies_UserId] ON [Strategies] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926192041_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_TradeImages_TradeId] ON [TradeImages] ([TradeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926192041_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Trades_EntryEmotionId] ON [Trades] ([EntryEmotionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926192041_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Trades_ExitEmotionId] ON [Trades] ([ExitEmotionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926192041_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Trades_StrategyId] ON [Trades] ([StrategyId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926192041_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Trades_UserId_EntryDateTime] ON [Trades] ([UserId], [EntryDateTime]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926192041_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Trades_UserId_MarketType] ON [Trades] ([UserId], [MarketType]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926192041_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Users_Email] ON [Users] ([Email]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926192041_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260926192041_InitialCreate', N'10.0.12');
END;

COMMIT;
GO

