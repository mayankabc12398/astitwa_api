-- =====================================================================
--  HrSuite - seed data
--
--  Two tenants, so every acceptance scenario can be proved to affect one client and
--  not the other:
--
--    ACME   (Tenant A) - the one that gets the configuration rows and the scripts
--    GLOBEX (Tenant B) - the control. Nothing below is aimed at it.
--
--  Sign-in for every seeded user is Password123!
--  CHANGE IT. These hashes are published in a reference implementation.
--
--  Re-runnable: every insert is guarded, so applying this twice is harmless.
-- =====================================================================

USE astitwa;

-- ---------------------------------------------------------------------
-- Tenants
-- ---------------------------------------------------------------------

INSERT INTO sys_tenant (tenant_code, tenant_name, created_by, created_on)
SELECT 'ACME', 'Acme Manufacturing', 1, UTC_TIMESTAMP()
WHERE NOT EXISTS (SELECT 1 FROM sys_tenant WHERE tenant_code = 'ACME');

INSERT INTO sys_tenant (tenant_code, tenant_name, created_by, created_on)
SELECT 'GLOBEX', 'Globex Services', 1, UTC_TIMESTAMP()
WHERE NOT EXISTS (SELECT 1 FROM sys_tenant WHERE tenant_code = 'GLOBEX');

SET @acme   = (SELECT tenant_id FROM sys_tenant WHERE tenant_code = 'ACME');
SET @globex = (SELECT tenant_id FROM sys_tenant WHERE tenant_code = 'GLOBEX');

-- ---------------------------------------------------------------------
-- Roles
-- ---------------------------------------------------------------------

INSERT INTO sys_role (tenant_id, role_code, role_name, created_by, created_on)
SELECT t.tenant_id, r.role_code, r.role_name, 1, UTC_TIMESTAMP()
FROM   sys_tenant t
CROSS  JOIN (
         SELECT 'ADMIN' AS role_code, 'Administrator' AS role_name
         UNION ALL SELECT 'HR',       'HR Officer'
         UNION ALL SELECT 'MANAGER',  'Manager'
       ) r
WHERE  t.tenant_code IN ('ACME', 'GLOBEX')
  AND  NOT EXISTS (SELECT 1 FROM sys_role x WHERE x.tenant_id = t.tenant_id AND x.role_code = r.role_code);

-- ---------------------------------------------------------------------
-- Role permissions
--   ADMIN   - everything, including the layer 5 admin screens
--   HR      - the HR screens, no administration
--   MANAGER - read plus leave approval
-- ---------------------------------------------------------------------

INSERT INTO sys_role_permission (tenant_id, role_id, permission_key, created_by, created_on)
SELECT r.tenant_id, r.role_id, p.permission_key, 1, UTC_TIMESTAMP()
FROM   sys_role r
JOIN (
  SELECT 'ADMIN' AS role_code, 'hr.department.view'  AS permission_key
  UNION ALL SELECT 'ADMIN', 'hr.department.edit'
  UNION ALL SELECT 'ADMIN', 'hr.designation.view'
  UNION ALL SELECT 'ADMIN', 'hr.designation.edit'
  UNION ALL SELECT 'ADMIN', 'hr.employee.view'
  UNION ALL SELECT 'ADMIN', 'hr.employee.edit'
  UNION ALL SELECT 'ADMIN', 'hr.leave.view'
  UNION ALL SELECT 'ADMIN', 'hr.leave.edit'
  UNION ALL SELECT 'ADMIN', 'hr.leave.approve'
  UNION ALL SELECT 'ADMIN', 'admin.extensions'
  UNION ALL SELECT 'ADMIN', 'admin.tenant'
  UNION ALL SELECT 'ADMIN', 'payroll.view'
  UNION ALL SELECT 'ADMIN', 'payroll.run'

  UNION ALL SELECT 'HR', 'hr.department.view'
  UNION ALL SELECT 'HR', 'hr.department.edit'
  UNION ALL SELECT 'HR', 'hr.designation.view'
  UNION ALL SELECT 'HR', 'hr.designation.edit'
  UNION ALL SELECT 'HR', 'hr.employee.view'
  UNION ALL SELECT 'HR', 'hr.employee.edit'
  UNION ALL SELECT 'HR', 'hr.leave.view'
  UNION ALL SELECT 'HR', 'hr.leave.edit'

  UNION ALL SELECT 'MANAGER', 'hr.employee.view'
  UNION ALL SELECT 'MANAGER', 'hr.department.view'
  UNION ALL SELECT 'MANAGER', 'hr.designation.view'
  UNION ALL SELECT 'MANAGER', 'hr.leave.view'
  UNION ALL SELECT 'MANAGER', 'hr.leave.approve'
) p ON p.role_code = r.role_code
WHERE NOT EXISTS (
  SELECT 1 FROM sys_role_permission x
  WHERE x.tenant_id = r.tenant_id AND x.role_id = r.role_id AND x.permission_key = p.permission_key
);

-- ---------------------------------------------------------------------
-- Master data - ACME
-- ---------------------------------------------------------------------

INSERT INTO hr_department (tenant_id, dept_code, dept_name, created_by, created_on)
SELECT @acme, d.code, d.name, 1, UTC_TIMESTAMP()
FROM (
  SELECT 'ENG' AS code, 'Engineering' AS name
  UNION ALL SELECT 'FIN', 'Finance'
  UNION ALL SELECT 'OPS', 'Operations'
) d
WHERE NOT EXISTS (SELECT 1 FROM hr_department x WHERE x.tenant_id = @acme AND x.dept_code = d.code);

INSERT INTO hr_designation (tenant_id, desig_code, desig_name, grade, created_by, created_on)
SELECT @acme, g.code, g.name, g.grade, 1, UTC_TIMESTAMP()
FROM (
  SELECT 'ENGR' AS code, 'Engineer' AS name, 'G3' AS grade
  UNION ALL SELECT 'SRENGR', 'Senior Engineer', 'G4'
  UNION ALL SELECT 'MGR',    'Manager',         'G5'
) g
WHERE NOT EXISTS (SELECT 1 FROM hr_designation x WHERE x.tenant_id = @acme AND x.desig_code = g.code);

INSERT INTO hr_leave_type (tenant_id, leave_code, leave_name, created_by, created_on)
SELECT t.tenant_id, l.code, l.name, 1, UTC_TIMESTAMP()
FROM   sys_tenant t
CROSS  JOIN (
  SELECT 'CL' AS code, 'Casual Leave' AS name
  UNION ALL SELECT 'SL', 'Sick Leave'
  UNION ALL SELECT 'EL', 'Earned Leave'
) l
WHERE  t.tenant_code IN ('ACME', 'GLOBEX')
  AND  NOT EXISTS (SELECT 1 FROM hr_leave_type x WHERE x.tenant_id = t.tenant_id AND x.leave_code = l.code);

-- ---------------------------------------------------------------------
-- Master data - GLOBEX (the control tenant)
-- ---------------------------------------------------------------------

INSERT INTO hr_department (tenant_id, dept_code, dept_name, created_by, created_on)
SELECT @globex, 'SVC', 'Services', 1, UTC_TIMESTAMP()
WHERE NOT EXISTS (SELECT 1 FROM hr_department x WHERE x.tenant_id = @globex AND x.dept_code = 'SVC');

INSERT INTO hr_designation (tenant_id, desig_code, desig_name, grade, created_by, created_on)
SELECT @globex, 'CONS', 'Consultant', 'C2', 1, UTC_TIMESTAMP()
WHERE NOT EXISTS (SELECT 1 FROM hr_designation x WHERE x.tenant_id = @globex AND x.desig_code = 'CONS');

-- ---------------------------------------------------------------------
-- Employees
-- ---------------------------------------------------------------------

INSERT INTO hr_employee
      (tenant_id, employee_code, full_name, dob, date_of_joining, department_id, designation_id,
       mobile, email, employment_status, created_by, created_on)
SELECT @acme, 'E-1000', 'Priya Sharma', '1988-04-12', '2015-06-01',
       (SELECT department_id  FROM hr_department  WHERE tenant_id = @acme AND dept_code  = 'ENG'),
       (SELECT designation_id FROM hr_designation WHERE tenant_id = @acme AND desig_code = 'MGR'),
       '9876500001', 'priya.sharma@acme.example', 'Active', 1, UTC_TIMESTAMP()
WHERE NOT EXISTS (SELECT 1 FROM hr_employee WHERE tenant_id = @acme AND employee_code = 'E-1000');

SET @acme_mgr = (SELECT employee_id FROM hr_employee WHERE tenant_id = @acme AND employee_code = 'E-1000');

INSERT INTO hr_employee
      (tenant_id, employee_code, full_name, dob, date_of_joining, department_id, designation_id,
       reporting_manager_id, mobile, email, employment_status, created_by, created_on)
SELECT @acme, e.code, e.name, e.dob, e.doj,
       (SELECT department_id  FROM hr_department  WHERE tenant_id = @acme AND dept_code  = 'ENG'),
       (SELECT designation_id FROM hr_designation WHERE tenant_id = @acme AND desig_code = 'ENGR'),
       @acme_mgr, e.mobile, e.email, 'Active', 1, UTC_TIMESTAMP()
FROM (
  -- Two employees deliberately share a mobile number, so the pickList in acceptance
  -- scenario 3 has more than one row to choose between.
  SELECT 'E-1001' AS code, 'Arun Nair'    AS name, '1994-01-20' AS dob, '2019-03-11' AS doj, '9876500002' AS mobile, 'arun.nair@acme.example' AS email
  UNION ALL SELECT 'E-1002', 'Meera Iyer',  '1996-09-02', '2021-08-16', '9876500002', 'meera.iyer@acme.example'
  UNION ALL SELECT 'E-1003', 'Rahul Verma', '1991-12-30', '2018-01-02', '9876500003', 'rahul.verma@acme.example'
) e
WHERE NOT EXISTS (SELECT 1 FROM hr_employee x WHERE x.tenant_id = @acme AND x.employee_code = e.code);

INSERT INTO hr_employee
      (tenant_id, employee_code, full_name, dob, date_of_joining, department_id, designation_id,
       mobile, email, employment_status, created_by, created_on)
SELECT @globex, 'G-2000', 'Sam Whitfield', '1990-07-07', '2020-02-10',
       (SELECT department_id  FROM hr_department  WHERE tenant_id = @globex AND dept_code  = 'SVC'),
       (SELECT designation_id FROM hr_designation WHERE tenant_id = @globex AND desig_code = 'CONS'),
       '9123400001', 'sam.whitfield@globex.example', 'Active', 1, UTC_TIMESTAMP()
WHERE NOT EXISTS (SELECT 1 FROM hr_employee WHERE tenant_id = @globex AND employee_code = 'G-2000');

-- ---------------------------------------------------------------------
-- Logins
--   Every password below is Password123!  -- change it before anything real.
--   The admin accounts are linked to an employee so the "cannot approve your own
--   leave" rule is demonstrable.
-- ---------------------------------------------------------------------

INSERT INTO sys_user (tenant_id, user_name, display_name, email, password_hash, employee_id, created_by, created_on)
SELECT @acme, 'admin', 'Acme Administrator', 'admin@acme.example',
       '$2a$12$gKYRTrWdaFIhgdqHI0qbgObxylW/STogrWjrlrafjdZgVbkUQHLfC',
       @acme_mgr, 1, UTC_TIMESTAMP()
WHERE NOT EXISTS (SELECT 1 FROM sys_user WHERE tenant_id = @acme AND user_name = 'admin');

INSERT INTO sys_user (tenant_id, user_name, display_name, email, password_hash, employee_id, created_by, created_on)
SELECT @acme, 'hr', 'Acme HR Officer', 'hr@acme.example',
       '$2a$12$BswraZxpZz65IPz6fGRTFebRXFrtoyWeLUy02b1ovQtMGLrpF5gHq',
       NULL, 1, UTC_TIMESTAMP()
WHERE NOT EXISTS (SELECT 1 FROM sys_user WHERE tenant_id = @acme AND user_name = 'hr');

INSERT INTO sys_user (tenant_id, user_name, display_name, email, password_hash, employee_id, created_by, created_on)
SELECT @globex, 'admin', 'Globex Administrator', 'admin@globex.example',
       '$2a$12$LHeysXq7k2o4dxUIxXJZ1OORUAojAkm9./T.poU7HrBCf8rLJyxGq',
       (SELECT employee_id FROM hr_employee WHERE tenant_id = @globex AND employee_code = 'G-2000'),
       1, UTC_TIMESTAMP()
WHERE NOT EXISTS (SELECT 1 FROM sys_user WHERE tenant_id = @globex AND user_name = 'admin');

INSERT INTO sys_user_role (tenant_id, user_id, role_id, created_by, created_on)
SELECT u.tenant_id, u.user_id, r.role_id, 1, UTC_TIMESTAMP()
FROM   sys_user u
JOIN   sys_role r ON r.tenant_id = u.tenant_id
                 AND r.role_code = CASE u.user_name WHEN 'admin' THEN 'ADMIN' WHEN 'hr' THEN 'HR' ELSE 'MANAGER' END
WHERE  NOT EXISTS (
  SELECT 1 FROM sys_user_role x WHERE x.tenant_id = u.tenant_id AND x.user_id = u.user_id AND x.role_id = r.role_id
);

-- ---------------------------------------------------------------------
-- Layer 3 licensing
--   Payroll starts ON for ACME so acceptance scenario 6 has something to switch off,
--   and OFF for GLOBEX so the difference is visible from the first login.
-- ---------------------------------------------------------------------

INSERT INTO sys_tenant_module (tenant_id, module_key, is_enabled, created_by, created_on)
SELECT @acme, 'payroll', 1, 1, UTC_TIMESTAMP()
WHERE NOT EXISTS (SELECT 1 FROM sys_tenant_module WHERE tenant_id = @acme AND module_key = 'payroll');

INSERT INTO sys_tenant_module (tenant_id, module_key, is_enabled, created_by, created_on)
SELECT @globex, 'payroll', 0, 1, UTC_TIMESTAMP()
WHERE NOT EXISTS (SELECT 1 FROM sys_tenant_module WHERE tenant_id = @globex AND module_key = 'payroll');

INSERT INTO pay_payroll_run (tenant_id, period_label, status, employee_count, run_on, created_by, created_on)
SELECT @acme, '2026-07', 'Completed', 4, '2026-07-31', 1, UTC_TIMESTAMP()
WHERE NOT EXISTS (SELECT 1 FROM pay_payroll_run WHERE tenant_id = @acme AND period_label = '2026-07');

-- ---------------------------------------------------------------------
-- Layer 4 integrations
--   Registered but switched off, which is exactly the state acceptance scenario 7
--   requires: leave approval must succeed with no mail server anywhere in sight.
-- ---------------------------------------------------------------------

INSERT INTO sys_tenant_integration (tenant_id, integration_key, settings_json, is_enabled, created_by, created_on)
SELECT @acme, 'email.smtp',
       '{"host":"localhost","port":25,"useStartTls":false,"fromAddress":"noreply@acme.example","fromName":"Acme HR"}',
       0, 1, UTC_TIMESTAMP()
WHERE NOT EXISTS (SELECT 1 FROM sys_tenant_integration WHERE tenant_id = @acme AND integration_key = 'email.smtp');

-- ---------------------------------------------------------------------
-- Layer 2 configuration
--   Wording lives in rows, not in a component.
-- ---------------------------------------------------------------------

INSERT INTO cfg_setting (tenant_id, setting_key, setting_value, data_type, created_by, created_on)
SELECT @acme, s.k, s.v, s.t, 1, UTC_TIMESTAMP()
FROM (
  SELECT 'template.leave.decision.subject' AS k, 'Your leave request was {{status}}' AS v, 'string' AS t
  UNION ALL SELECT 'template.leave.decision.body',
                   'Hello {{employeeName}}, your {{leaveType}} from {{fromDate}} to {{toDate}} ({{days}} day(s)) was {{status}}. {{remark}}',
                   'string'
  UNION ALL SELECT 'hr.employee.listPageSize', '25', 'int'
) s
WHERE NOT EXISTS (SELECT 1 FROM cfg_setting x WHERE x.tenant_id = @acme AND x.setting_key = s.k);

-- ---------------------------------------------------------------------
-- Layer 5 registration
--   The named query acceptance scenario 3 uses. Note what is declared: one parameter,
--   five columns. A script can bind nothing else and see nothing else.
-- ---------------------------------------------------------------------

INSERT INTO ext_named_query
      (tenant_id, query_key, proc_name, params_json, columns_json, max_rows, required_permission, is_active, created_by, created_on)
SELECT NULL, 'hr.employee.searchByMobile', 'sp_hr_employee_search_by_mobile',
       '[{"name":"mobile","type":"string","required":true}]',
       '["employee_id","employee_code","full_name","mobile","department_name"]',
       25, 'hr.employee.view', 1, 1, UTC_TIMESTAMP()
WHERE NOT EXISTS (SELECT 1 FROM ext_named_query WHERE query_key = 'hr.employee.searchByMobile');
