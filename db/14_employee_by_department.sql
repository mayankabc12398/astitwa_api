-- =====================================================================
--  HrSuite - employees of one department, for scripts
--
--  Apply after 13_document_stats.sql. Re-runnable.
--
--  A client script cannot write SQL: it names a key in ext_named_query and
--  the server runs the procedure that key is registered against. So the
--  "who else is in this department" list needs both halves - the procedure,
--  and the registration that declares its parameters and its columns.
-- =====================================================================

DROP PROCEDURE IF EXISTS sp_hr_employee_by_department;

DELIMITER $$

CREATE PROCEDURE sp_hr_employee_by_department (
  IN p_tenant_id     INT,
  IN p_user_id       INT,
  IN p_department_id INT
)
BEGIN
  SELECT e.employee_id,
         e.employee_code,
         e.full_name,
         e.mobile,
         e.employment_status,
         g.desig_name AS designation_name,
         m.full_name  AS reporting_manager_name
  FROM   hr_employee e
  LEFT   JOIN hr_designation g ON g.designation_id = e.designation_id AND g.tenant_id = e.tenant_id
  LEFT   JOIN hr_employee    m ON m.employee_id    = e.reporting_manager_id AND m.tenant_id = e.tenant_id
  WHERE  e.tenant_id     = p_tenant_id
    AND  e.is_active     = 1
    AND  p_department_id IS NOT NULL
    AND  e.department_id = p_department_id
  ORDER  BY e.full_name
  LIMIT  200;
END$$

DELIMITER ;

-- The registration. params_json names the procedure's own parameters without
-- their p_ prefix; columns_json is a whitelist, and a column the procedure
-- returns but this list omits never leaves the server.
INSERT INTO ext_named_query
      (tenant_id, query_key, proc_name, params_json, columns_json, max_rows, required_permission, is_active, created_by, created_on)
SELECT NULL, 'hr.employee.listByDepartment', 'sp_hr_employee_by_department',
       '[{"name":"department_id","type":"int","required":true}]',
       '["employee_id","employee_code","full_name","mobile","employment_status","designation_name","reporting_manager_name"]',
       200, 'hr.employee.view', 1, 1, UTC_TIMESTAMP()
WHERE NOT EXISTS (SELECT 1 FROM ext_named_query WHERE query_key = 'hr.employee.listByDepartment');
