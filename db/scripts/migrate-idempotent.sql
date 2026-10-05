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
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005173707_InitialCreate'
)
BEGIN
    CREATE TABLE [Customers] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Email] nvarchar(320) NOT NULL,
        [CountryCode] char(2) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Customers] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Customers_CountryCode] CHECK (LEN([CountryCode]) = 2)
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005173707_InitialCreate'
)
BEGIN
    CREATE TABLE [Orders] (
        [Id] uniqueidentifier NOT NULL,
        [CustomerId] uniqueidentifier NOT NULL,
        [Status] varchar(16) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CurrencyCode] char(3) NOT NULL,
        [TotalAmount] decimal(18,2) NOT NULL,
        CONSTRAINT [PK_Orders] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Orders_Status] CHECK ([Status] IN ('Pending', 'Paid', 'Fulfilled', 'Cancelled')),
        CONSTRAINT [CK_Orders_TotalAmount] CHECK ([TotalAmount] >= 0),
        CONSTRAINT [FK_Orders_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005173707_InitialCreate'
)
BEGIN
    CREATE TABLE [OrderLineItems] (
        [Id] uniqueidentifier NOT NULL,
        [OrderId] uniqueidentifier NOT NULL,
        [ProductSku] varchar(64) NOT NULL,
        [Quantity] int NOT NULL,
        [UnitPrice] decimal(18,2) NOT NULL,
        CONSTRAINT [PK_OrderLineItems] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_OrderLineItems_Quantity] CHECK ([Quantity] > 0),
        CONSTRAINT [CK_OrderLineItems_UnitPrice] CHECK ([UnitPrice] >= 0),
        CONSTRAINT [FK_OrderLineItems_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005173707_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Customers_Email] ON [Customers] ([Email]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005173707_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Customers_Name] ON [Customers] ([Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005173707_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_OrderLineItems_OrderId_ProductSku] ON [OrderLineItems] ([OrderId], [ProductSku]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005173707_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Orders_CustomerId_Status_CreatedAt] ON [Orders] ([CustomerId], [Status], [CreatedAt]) INCLUDE ([TotalAmount], [CurrencyCode]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005173707_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Orders_Status_CreatedAt] ON [Orders] ([Status], [CreatedAt]) INCLUDE ([CustomerId], [TotalAmount], [CurrencyCode]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005173707_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261005173707_InitialCreate', N'8.0.31');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005173758_AddOrderRowVersion'
)
BEGIN
    ALTER TABLE [Orders] ADD [RowVersion] rowversion NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005173758_AddOrderRowVersion'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261005173758_AddOrderRowVersion', N'8.0.31');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005173810_AddOrderAllocatedAt'
)
BEGIN
    ALTER TABLE [Orders] ADD [AllocatedAt] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005173810_AddOrderAllocatedAt'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261005173810_AddOrderAllocatedAt', N'8.0.31');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005183849_AddIdempotencyKeys'
)
BEGIN
    CREATE TABLE [IdempotencyKeys] (
        [ClientId] varchar(64) NOT NULL,
        [Key] varchar(128) NOT NULL,
        [RequestHash] char(64) NOT NULL,
        [Outcome] varchar(16) NOT NULL,
        [Payload] nvarchar(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [ExpiresAt] datetime2 NOT NULL,
        CONSTRAINT [PK_IdempotencyKeys] PRIMARY KEY ([ClientId], [Key])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005183849_AddIdempotencyKeys'
)
BEGIN
    CREATE INDEX [IX_IdempotencyKeys_ExpiresAt] ON [IdempotencyKeys] ([ExpiresAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005183849_AddIdempotencyKeys'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261005183849_AddIdempotencyKeys', N'8.0.31');
END;
GO

COMMIT;
GO

