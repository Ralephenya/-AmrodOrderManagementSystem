BEGIN TRANSACTION;
GO

DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Orders]') AND [c].[name] = N'AllocatedAt');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [Orders] DROP CONSTRAINT [' + @var0 + '];');
ALTER TABLE [Orders] DROP COLUMN [AllocatedAt];
GO

DELETE FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20261005173810_AddOrderAllocatedAt';
GO

COMMIT;
GO

