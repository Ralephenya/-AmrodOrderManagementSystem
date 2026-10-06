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

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005173707_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Customers_Email] ON [Customers] ([Email]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005173707_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Customers_Name] ON [Customers] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005173707_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_OrderLineItems_OrderId_ProductSku] ON [OrderLineItems] ([OrderId], [ProductSku]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005173707_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Orders_CustomerId_Status_CreatedAt] ON [Orders] ([CustomerId], [Status], [CreatedAt]) INCLUDE ([TotalAmount], [CurrencyCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005173707_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Orders_Status_CreatedAt] ON [Orders] ([Status], [CreatedAt]) INCLUDE ([CustomerId], [TotalAmount], [CurrencyCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005173707_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261005173707_InitialCreate', N'9.0.20');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005173758_AddOrderRowVersion'
)
BEGIN
    ALTER TABLE [Orders] ADD [RowVersion] rowversion NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005173758_AddOrderRowVersion'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261005173758_AddOrderRowVersion', N'9.0.20');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005173810_AddOrderAllocatedAt'
)
BEGIN
    ALTER TABLE [Orders] ADD [AllocatedAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005173810_AddOrderAllocatedAt'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261005173810_AddOrderAllocatedAt', N'9.0.20');
END;

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

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005183849_AddIdempotencyKeys'
)
BEGIN
    CREATE INDEX [IX_IdempotencyKeys_ExpiresAt] ON [IdempotencyKeys] ([ExpiresAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005183849_AddIdempotencyKeys'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261005183849_AddIdempotencyKeys', N'9.0.20');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005191701_AddMessagingOutbox'
)
BEGIN
    CREATE TABLE [InboxState] (
        [Id] bigint NOT NULL IDENTITY,
        [MessageId] uniqueidentifier NOT NULL,
        [ConsumerId] uniqueidentifier NOT NULL,
        [LockId] uniqueidentifier NOT NULL,
        [RowVersion] rowversion NULL,
        [Received] datetime2 NOT NULL,
        [ReceiveCount] int NOT NULL,
        [ExpirationTime] datetime2 NULL,
        [Consumed] datetime2 NULL,
        [Delivered] datetime2 NULL,
        [LastSequenceNumber] bigint NULL,
        CONSTRAINT [PK_InboxState] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_InboxState_MessageId_ConsumerId] UNIQUE ([MessageId], [ConsumerId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005191701_AddMessagingOutbox'
)
BEGIN
    CREATE TABLE [OutboxState] (
        [OutboxId] uniqueidentifier NOT NULL,
        [LockId] uniqueidentifier NOT NULL,
        [RowVersion] rowversion NULL,
        [Created] datetime2 NOT NULL,
        [Delivered] datetime2 NULL,
        [LastSequenceNumber] bigint NULL,
        CONSTRAINT [PK_OutboxState] PRIMARY KEY ([OutboxId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005191701_AddMessagingOutbox'
)
BEGIN
    CREATE TABLE [OutboxMessage] (
        [SequenceNumber] bigint NOT NULL IDENTITY,
        [EnqueueTime] datetime2 NULL,
        [SentTime] datetime2 NOT NULL,
        [Headers] nvarchar(max) NULL,
        [Properties] nvarchar(max) NULL,
        [InboxMessageId] uniqueidentifier NULL,
        [InboxConsumerId] uniqueidentifier NULL,
        [OutboxId] uniqueidentifier NULL,
        [MessageId] uniqueidentifier NOT NULL,
        [ContentType] nvarchar(256) NOT NULL,
        [MessageType] nvarchar(max) NOT NULL,
        [Body] nvarchar(max) NOT NULL,
        [ConversationId] uniqueidentifier NULL,
        [CorrelationId] uniqueidentifier NULL,
        [InitiatorId] uniqueidentifier NULL,
        [RequestId] uniqueidentifier NULL,
        [SourceAddress] nvarchar(256) NULL,
        [DestinationAddress] nvarchar(256) NULL,
        [ResponseAddress] nvarchar(256) NULL,
        [FaultAddress] nvarchar(256) NULL,
        [ExpirationTime] datetime2 NULL,
        CONSTRAINT [PK_OutboxMessage] PRIMARY KEY ([SequenceNumber]),
        CONSTRAINT [FK_OutboxMessage_InboxState_InboxMessageId_InboxConsumerId] FOREIGN KEY ([InboxMessageId], [InboxConsumerId]) REFERENCES [InboxState] ([MessageId], [ConsumerId]),
        CONSTRAINT [FK_OutboxMessage_OutboxState_OutboxId] FOREIGN KEY ([OutboxId]) REFERENCES [OutboxState] ([OutboxId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005191701_AddMessagingOutbox'
)
BEGIN
    CREATE INDEX [IX_InboxState_Delivered] ON [InboxState] ([Delivered]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005191701_AddMessagingOutbox'
)
BEGIN
    CREATE INDEX [IX_OutboxMessage_EnqueueTime] ON [OutboxMessage] ([EnqueueTime]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005191701_AddMessagingOutbox'
)
BEGIN
    CREATE INDEX [IX_OutboxMessage_ExpirationTime] ON [OutboxMessage] ([ExpirationTime]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005191701_AddMessagingOutbox'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_OutboxMessage_InboxMessageId_InboxConsumerId_SequenceNumber] ON [OutboxMessage] ([InboxMessageId], [InboxConsumerId], [SequenceNumber]) WHERE [InboxMessageId] IS NOT NULL AND [InboxConsumerId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005191701_AddMessagingOutbox'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_OutboxMessage_OutboxId_SequenceNumber] ON [OutboxMessage] ([OutboxId], [SequenceNumber]) WHERE [OutboxId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005191701_AddMessagingOutbox'
)
BEGIN
    CREATE INDEX [IX_OutboxState_Created] ON [OutboxState] ([Created]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005191701_AddMessagingOutbox'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261005191701_AddMessagingOutbox', N'9.0.20');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006042903_AddCustomerCountryCodeIndex'
)
BEGIN
    CREATE INDEX [IX_Customers_CountryCode] ON [Customers] ([CountryCode]) INCLUDE ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006042903_AddCustomerCountryCodeIndex'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261006042903_AddCustomerCountryCodeIndex', N'9.0.20');
END;

COMMIT;
GO

