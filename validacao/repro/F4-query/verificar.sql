-- FASE 4 em lote — eixo `query`. Uma afirmacao por secao, saida discriminante.
SET NOCOUNT ON;
IF DB_ID('ReproDb_F4Q') IS NOT NULL
BEGIN
    ALTER DATABASE ReproDb_F4Q SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE ReproDb_F4Q;
END;
CREATE DATABASE ReproDb_F4Q;
GO
USE ReproDb_F4Q;
SET NOCOUNT ON;

CREATE TABLE dbo.Cliente(Id int PRIMARY KEY, Cidade varchar(40) NULL);
INSERT INTO dbo.Cliente VALUES (1,'SP'),(2,'RJ'),(3,NULL);

CREATE TABLE dbo.Pedido(Id int PRIMARY KEY, ClienteId int NULL, Total decimal(10,2) NULL, Criado datetime2 NOT NULL);
INSERT INTO dbo.Pedido VALUES
 (1,1,100.00,'2024-01-15T10:00:00'),(2,2,NULL,'2024-02-20T11:00:00'),
 (3,NULL,50.00,'2024-03-25T12:00:00'),(4,1,75.00,'2025-01-05T13:00:00');
GO

PRINT '== Q1: NOT IN com NULL na subquery ==';
DECLARE @notin int = (SELECT COUNT(*) FROM dbo.Cliente WHERE Id NOT IN (SELECT ClienteId FROM dbo.Pedido));
DECLARE @notexists int = (SELECT COUNT(*) FROM dbo.Cliente c WHERE NOT EXISTS (SELECT 1 FROM dbo.Pedido p WHERE p.ClienteId = c.Id));
PRINT 'Q1 NOT IN devolveu: ' + CAST(@notin AS varchar(10)) + ' linha(s)';
PRINT 'Q1 NOT EXISTS devolveu: ' + CAST(@notexists AS varchar(10)) + ' linha(s)  (cliente 3 nao tem pedido)';
GO

PRINT '';
PRINT '== Q2: COUNT(coluna) vs COUNT(*) ==';
DECLARE @cstar int = (SELECT COUNT(*) FROM dbo.Pedido);
DECLARE @ccol int = (SELECT COUNT(Total) FROM dbo.Pedido);
PRINT 'Q2 COUNT(*)     = ' + CAST(@cstar AS varchar(10));
PRINT 'Q2 COUNT(Total) = ' + CAST(@ccol AS varchar(10)) + '  (Total tem 1 NULL)';
GO

PRINT '';
PRINT '== Q3: alias do SELECT referenciado no WHERE ==';
BEGIN TRY
    EXEC sp_executesql N'SELECT YEAR(Criado) AS Ano FROM dbo.Pedido WHERE Ano = 2024;';
    PRINT 'Q3 alias no WHERE: EXECUTOU (inesperado)';
END TRY
BEGIN CATCH
    PRINT 'Q3 alias no WHERE: FALHOU erro ' + CAST(ERROR_NUMBER() AS varchar(10)) + ' - ' + ERROR_MESSAGE();
END CATCH
BEGIN TRY
    EXEC sp_executesql N'SELECT YEAR(Criado) AS Ano FROM dbo.Pedido ORDER BY Ano;';
    PRINT 'Q3 alias no ORDER BY: EXECUTOU (esperado - ORDER BY vem depois do SELECT)';
END TRY
BEGIN CATCH
    PRINT 'Q3 alias no ORDER BY: FALHOU erro ' + CAST(ERROR_NUMBER() AS varchar(10));
END CATCH
GO

PRINT '';
PRINT '== Q4: sargabilidade - funcao sobre coluna indexada ==';
CREATE TABLE dbo.Evento(Id int IDENTITY PRIMARY KEY, Quando datetime2 NOT NULL, Carga char(80) NOT NULL DEFAULT('x'));
INSERT INTO dbo.Evento(Quando)
SELECT DATEADD(minute, ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), '2023-01-01')
FROM sys.all_objects a CROSS JOIN sys.all_objects b;
CREATE INDEX IX_Evento_Quando ON dbo.Evento(Quando);
DECLARE @n int = (SELECT COUNT(*) FROM dbo.Evento);
PRINT 'Q4 linhas na tabela: ' + CAST(@n AS varchar(20));

SET STATISTICS IO ON;
PRINT 'Q4a NAO sargavel: WHERE YEAR(Quando) = 2023';
SELECT COUNT(*) FROM dbo.Evento WHERE YEAR(Quando) = 2023;
PRINT 'Q4b sargavel: WHERE Quando >= 20230101 AND Quando < 20240101';
SELECT COUNT(*) FROM dbo.Evento WHERE Quando >= '2023-01-01' AND Quando < '2024-01-01';
SET STATISTICS IO OFF;
GO

PRINT '';
PRINT '== Q5: ordem sem ORDER BY nao e garantida (plano revela) ==';
SELECT TOP 3 Id FROM dbo.Pedido;
PRINT 'Q5 acima: sem ORDER BY o servidor entrega na ordem que for conveniente ao plano';
GO

PRINT '';
PRINT '== Q6: UDF escalar em predicado ==';
CREATE FUNCTION dbo.fnAno(@d datetime2) RETURNS int AS BEGIN RETURN YEAR(@d) END;
GO
SET STATISTICS IO ON;
PRINT 'Q6 com UDF escalar no WHERE';
SELECT COUNT(*) FROM dbo.Evento WHERE dbo.fnAno(Quando) = 2023;
SET STATISTICS IO OFF;
GO

PRINT '';
PRINT 'FIM';
