-- =====================================================================
--  HrSuite - migration: compensation fields on hr_employee
--
--  01_schema.sql now declares gross_ctc, hra, tds and net_salary, but it creates the
--  table with CREATE TABLE IF NOT EXISTS - so re-running it against a database that
--  already has hr_employee changes nothing. This file is for that database.
--
--  A fresh install does not need this file: 01 already has the columns.
--
--  After applying it, re-run 03_procs_hr.sql. Both employee procedures now read and
--  write the new columns, and 03 drops and recreates every procedure it owns, so it is
--  safe to run again.
--
--  Idempotent: each column is added only if it is not already there, so this can be run
--  twice with no effect and no error.
-- =====================================================================

USE astitwa;

DROP PROCEDURE IF EXISTS sp_tmp_add_employee_salary_columns;

DELIMITER $$

CREATE PROCEDURE sp_tmp_add_employee_salary_columns()
BEGIN
  -- ALTER TABLE ... ADD COLUMN IF NOT EXISTS is MariaDB, not MySQL 8, so the check is
  -- explicit against information_schema.
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_schema = DATABASE()
                   AND table_name   = 'hr_employee'
                   AND column_name  = 'gross_ctc') THEN
    ALTER TABLE hr_employee
      ADD COLUMN gross_ctc  DECIMAL(14,2) NULL AFTER employment_status,
      ADD COLUMN hra        DECIMAL(14,2) NULL AFTER gross_ctc,
      ADD COLUMN tds        DECIMAL(14,2) NULL AFTER hra,
      ADD COLUMN net_salary DECIMAL(14,2) NULL AFTER tds;
  END IF;
END$$

DELIMITER ;

CALL sp_tmp_add_employee_salary_columns();
DROP PROCEDURE sp_tmp_add_employee_salary_columns;

SELECT column_name, column_type, is_nullable
FROM   information_schema.columns
WHERE  table_schema = DATABASE()
  AND  table_name   = 'hr_employee'
  AND  column_name IN ('gross_ctc', 'hra', 'tds', 'net_salary')
ORDER  BY ordinal_position;
