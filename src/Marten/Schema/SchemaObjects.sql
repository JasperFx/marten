select column_name, data_type, character_maximum_length 
from information_schema.columns where table_schema = :schema and table_name = :table_name 
order by ordinal_position;

select a.attname, format_type(a.atttypid, a.atttypmod) as data_type
from pg_index i
join   pg_attribute a on a.attrelid = i.indrelid and a.attnum = ANY(i.indkey)
where attrelid = (select pg_class.oid 
                  from pg_class 
                  join pg_catalog.pg_namespace n ON n.oid = pg_class.relnamespace
                  where n.nspname = :schema and relname = :table_name)
and i.indisprimary; 

SELECT pg_get_functiondef(pg_proc.oid) 
FROM pg_proc JOIN pg_namespace as ns ON pg_proc.pronamespace = ns.oid WHERE ns.nspname = :schema and proname = :function;

SELECT
  U.usename                AS user_name,
  ns.nspname               AS schema_name,
  pg_catalog.textin(pg_catalog.regclassout(idx.indrelid :: REGCLASS)) AS table_name,
  i.relname                AS index_name,
  pg_get_indexdef(i.oid) as ddl,
  idx.indisunique          AS is_unique,
  idx.indisprimary         AS is_primary,
  am.amname                AS index_type,
  idx.indkey,
       ARRAY(
           SELECT pg_get_indexdef(idx.indexrelid, k + 1, TRUE)
           FROM
             generate_subscripts(idx.indkey, 1) AS k
           ORDER BY k
       ) AS index_keys,
  (idx.indexprs IS NOT NULL) OR (idx.indkey::int[] @> array[0]) AS is_functional,
  idx.indpred IS NOT NULL AS is_partial
FROM pg_index AS idx
  JOIN pg_class AS i
    ON i.oid = idx.indexrelid
  JOIN pg_am AS am
    ON i.relam = am.oid
  JOIN pg_namespace AS NS ON i.relnamespace = NS.OID
  JOIN pg_user AS U ON i.relowner = U.usesysid
WHERE NOT nspname LIKE 'pg%' AND i.relname like 'mt_%' AND pg_catalog.textin(pg_catalog.regclassout(idx.indrelid :: REGCLASS)) = :qualified_name; -- Excluding system table

SELECT format('DROP FUNCTION %s.%s(%s);'
             ,n.nspname
             ,p.proname
             ,pg_get_function_identity_arguments(p.oid))
FROM   pg_proc p
LEFT JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace 
WHERE  p.proname = :function
AND    n.nspname = :schema;

select constraint_name 
from information_schema.table_constraints as c
where 
  c.constraint_name LIKE 'mt_%' and 
  c.constraint_type = 'FOREIGN KEY' and 
  c.table_schema = :schema and
  c.table_name = :table_name;

