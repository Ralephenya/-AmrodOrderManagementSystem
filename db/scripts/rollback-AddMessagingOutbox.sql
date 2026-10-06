BEGIN TRANSACTION;
DROP TABLE [OutboxMessage];

DROP TABLE [InboxState];

DROP TABLE [OutboxState];

DELETE FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20261005191701_AddMessagingOutbox';

COMMIT;
GO

