PRINT 'hello from a sql file';
SELECT DB_NAME() AS CurrentDatabase, SYSDATETIME() AS Now;
GO
SELECT name, database_id FROM sys.databases ORDER BY database_id;
