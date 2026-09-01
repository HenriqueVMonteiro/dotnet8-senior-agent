USE ReproDb_F4Q;
GO
CREATE OR ALTER FUNCTION dbo.fnAno(@d datetime2) RETURNS int AS BEGIN RETURN YEAR(@d) END;
GO
SET NOCOUNT ON;
PRINT '== Q6: UDF escalar em predicado vs expressao inline ==';
SET STATISTICS IO ON;
SET STATISTICS TIME ON;
PRINT 'Q6a com UDF escalar: WHERE dbo.fnAno(Quando) = 2023';
SELECT COUNT(*) FROM dbo.Evento WHERE dbo.fnAno(Quando) = 2023;
PRINT 'Q6b inline sargavel: WHERE Quando >= 20230101 AND Quando < 20240101';
SELECT COUNT(*) FROM dbo.Evento WHERE Quando >= '2023-01-01' AND Quando < '2024-01-01';
SET STATISTICS TIME OFF;
SET STATISTICS IO OFF;
GO
PRINT '';
PRINT '== Q7: o plano do UDF paralelizou? ==';
SELECT
    CAST(qp.query_plan AS nvarchar(max)) LIKE '%Parallelism%' AS TemParalelismo,
    SUBSTRING(st.text, 1, 60) AS Consulta
FROM sys.dm_exec_cached_plans cp
CROSS APPLY sys.dm_exec_sql_text(cp.plan_handle) st
CROSS APPLY sys.dm_exec_query_plan(cp.plan_handle) qp
WHERE st.text LIKE '%dbo.Evento%' AND st.text NOT LIKE '%dm_exec%';
GO
PRINT 'FIM';
