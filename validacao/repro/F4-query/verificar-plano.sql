USE ReproDb_F4Q;
GO
SET NOCOUNT ON;
PRINT '== Q7: o plano do UDF tem paralelismo? houve inlining (Froid)? ==';
PRINT 'compatibility_level do banco:';
SELECT compatibility_level FROM sys.databases WHERE name = DB_NAME();

SELECT
    CASE WHEN CAST(qp.query_plan AS nvarchar(max)) LIKE '%Parallelism%' THEN 'SIM' ELSE 'NAO' END AS Paralelismo,
    CASE WHEN CAST(qp.query_plan AS nvarchar(max)) LIKE '%UserDefinedFunction%' THEN 'SIM' ELSE 'NAO' END AS UdfNoPlano,
    cp.usecounts,
    LEFT(REPLACE(REPLACE(st.text, CHAR(13), ' '), CHAR(10), ' '), 70) AS Consulta
FROM sys.dm_exec_cached_plans cp
CROSS APPLY sys.dm_exec_sql_text(cp.plan_handle) st
CROSS APPLY sys.dm_exec_query_plan(cp.plan_handle) qp
WHERE st.text LIKE '%dbo.Evento%'
  AND st.text NOT LIKE '%dm_exec_cached_plans%';
GO
PRINT '';
PRINT '== Q8: mesma UDF com compatibility_level 140 (sem inlining) ==';
ALTER DATABASE ReproDb_F4Q SET COMPATIBILITY_LEVEL = 140;
GO
USE ReproDb_F4Q;
SET NOCOUNT ON;
SET STATISTICS TIME ON;
PRINT 'Q8 UDF sob compat 140:';
SELECT COUNT(*) FROM dbo.Evento WHERE dbo.fnAno(Quando) = 2023 OPTION (RECOMPILE);
SET STATISTICS TIME OFF;
GO
ALTER DATABASE ReproDb_F4Q SET COMPATIBILITY_LEVEL = 160;
GO
USE ReproDb_F4Q;
SET NOCOUNT ON;
SET STATISTICS TIME ON;
PRINT 'Q8 UDF sob compat 160 (inlining habilitado):';
SELECT COUNT(*) FROM dbo.Evento WHERE dbo.fnAno(Quando) = 2023 OPTION (RECOMPILE);
SET STATISTICS TIME OFF;
GO
PRINT 'FIM';
