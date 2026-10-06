BEGIN TRANSACTION;
DROP INDEX [IX_Customers_CountryCode] ON [Customers];

DELETE FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20261006042903_AddCustomerCountryCodeIndex';

COMMIT;
GO

