BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927041103_AddImageUploadWorkflow'
)
BEGIN
    CREATE TABLE [ImageDeletions] (
        [Id] uniqueidentifier NOT NULL,
        [StorageKey] nvarchar(500) NOT NULL,
        [Attempts] int NOT NULL,
        [NextAttemptAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_ImageDeletions] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927041103_AddImageUploadWorkflow'
)
BEGIN
    CREATE TABLE [ImageUploads] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [TradeId] uniqueidentifier NOT NULL,
        [StagingKey] nvarchar(500) NOT NULL,
        [FinalKey] nvarchar(500) NOT NULL,
        [ImageType] int NOT NULL,
        [FileName] nvarchar(255) NOT NULL,
        [ContentType] nvarchar(100) NOT NULL,
        [SizeBytes] bigint NOT NULL,
        [DisplayOrder] int NOT NULL,
        [ExpiresAtUtc] datetime2 NOT NULL,
        [Confirmed] bit NOT NULL,
        CONSTRAINT [PK_ImageUploads] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927041103_AddImageUploadWorkflow'
)
BEGIN
    CREATE INDEX [IX_ImageDeletions_NextAttemptAtUtc] ON [ImageDeletions] ([NextAttemptAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927041103_AddImageUploadWorkflow'
)
BEGIN
    CREATE INDEX [IX_ImageUploads_ExpiresAtUtc] ON [ImageUploads] ([ExpiresAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927041103_AddImageUploadWorkflow'
)
BEGIN
    CREATE INDEX [IX_ImageUploads_UserId_TradeId_ExpiresAtUtc] ON [ImageUploads] ([UserId], [TradeId], [ExpiresAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927041103_AddImageUploadWorkflow'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260927041103_AddImageUploadWorkflow', N'10.0.12');
END;

COMMIT;
GO

