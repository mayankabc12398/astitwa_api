-- =====================================================================
--  HrSuite - demo data
--
--  07_seed.sql is the MINIMUM the product needs to start: two tenants, roles,
--  three users, a handful of master rows. This file is the opposite - volume, so
--  that paging, searching, sorting and the empty/one/many states of every screen
--  can actually be seen.
--
--  Safe to run more than once. Every block is guarded by NOT EXISTS on the natural
--  key, so a second run inserts nothing and changes nothing. Nothing here is
--  referenced by 08_acceptance_scenarios.sql, and nothing here writes to
--  cfg_field_rule or ext_script_hook - those belong to the scenarios.
--
--  Rows are matched by CODE, never by id, so this survives a database whose
--  auto-increment sequence ran differently.
-- =====================================================================

USE astitwa;

SET @acme   = (SELECT tenant_id FROM sys_tenant WHERE tenant_code = 'ACME');
SET @globex = (SELECT tenant_id FROM sys_tenant WHERE tenant_code = 'GLOBEX');

-- The seeding user. Audit columns are NOT NULL by convention (section 5), and a
-- demo row is still a row somebody has to be able to account for.
SET @by = (SELECT user_id FROM sys_user WHERE tenant_id = @acme AND user_name = 'admin');

-- ---------------------------------------------------------------------
-- Master data - departments and designations
-- ---------------------------------------------------------------------

DROP TEMPORARY TABLE IF EXISTS tmp_dept;
CREATE TEMPORARY TABLE tmp_dept (tenant_id INT, dept_code VARCHAR(40), dept_name VARCHAR(150));

INSERT INTO tmp_dept VALUES
  (@acme,   'QA',    'Quality Assurance'),
  (@acme,   'HR',    'Human Resources'),
  (@acme,   'SALES', 'Sales'),
  (@globex, 'CONSULT', 'Consulting'),
  (@globex, 'SUPPORT', 'Customer Support');

INSERT INTO hr_department (tenant_id, dept_code, dept_name, created_by, created_on)
SELECT t.tenant_id, t.dept_code, t.dept_name, @by, NOW()
FROM   tmp_dept t
WHERE  NOT EXISTS (SELECT 1 FROM hr_department d
                   WHERE d.tenant_id = t.tenant_id AND d.dept_code = t.dept_code);

DROP TEMPORARY TABLE IF EXISTS tmp_desig;
CREATE TEMPORARY TABLE tmp_desig (tenant_id INT, desig_code VARCHAR(40), desig_name VARCHAR(150), grade VARCHAR(20));

INSERT INTO tmp_desig VALUES
  (@acme,   'JRENGR', 'Junior Engineer',   'G2'),
  (@acme,   'LEAD',   'Team Lead',         'G4'),
  (@acme,   'ANALYST','Analyst',           'G3'),
  (@acme,   'DIR',    'Director',          'G6'),
  (@globex, 'SRCONS', 'Senior Consultant', 'G4'),
  (@globex, 'SUPENG', 'Support Engineer',  'G2');

INSERT INTO hr_designation (tenant_id, desig_code, desig_name, grade, created_by, created_on)
SELECT t.tenant_id, t.desig_code, t.desig_name, t.grade, @by, NOW()
FROM   tmp_desig t
WHERE  NOT EXISTS (SELECT 1 FROM hr_designation d
                   WHERE d.tenant_id = t.tenant_id AND d.desig_code = t.desig_code);

-- ---------------------------------------------------------------------
-- Employees
--
--  Deliberately not uniform: two tenants, several departments, a manager
--  hierarchy, people who have left, and enough rows that page 2 exists.
-- ---------------------------------------------------------------------

DROP TEMPORARY TABLE IF EXISTS tmp_emp;
CREATE TEMPORARY TABLE tmp_emp (
  tenant_id     INT,
  employee_code VARCHAR(40),
  full_name     VARCHAR(180),
  dob           DATE,
  doj           DATE,
  dept_code     VARCHAR(40),
  desig_code    VARCHAR(40),
  mgr_code      VARCHAR(40),
  mobile        VARCHAR(30),
  email         VARCHAR(150),
  status        VARCHAR(30),
  active        INT
);

INSERT INTO tmp_emp VALUES
  -- ACME - Engineering
  (@acme, 'E-1010', 'Kavita Deshpande', '1985-02-14', '2015-06-01', 'ENG',   'LEAD',    NULL,     '9876510010', 'kavita.deshpande@acme.example', 'Active',   1),
  (@acme, 'E-1011', 'Rohit Bansal',     '1990-07-23', '2018-01-15', 'ENG',   'SRENGR',  'E-1010', '9876510011', 'rohit.bansal@acme.example',     'Active',   1),
  (@acme, 'E-1012', 'Sneha Kulkarni',   '1993-11-02', '2019-09-09', 'ENG',   'ENGR',    'E-1010', '9876510012', 'sneha.kulkarni@acme.example',   'Active',   1),
  (@acme, 'E-1013', 'Imran Sheikh',     '1996-04-19', '2021-03-22', 'ENG',   'JRENGR',  'E-1011', '9876510013', 'imran.sheikh@acme.example',     'Active',   1),
  (@acme, 'E-1014', 'Priya Menon',      '1994-08-30', '2020-11-02', 'ENG',   'ENGR',    'E-1011', '9876510014', 'priya.menon@acme.example',      'Active',   1),
  (@acme, 'E-1015', 'Tarun Gupta',      '1992-01-07', '2017-05-18', 'ENG',   'SRENGR',  'E-1010', '9876510015', 'tarun.gupta@acme.example',      'Resigned', 1),

  -- ACME - Quality Assurance
  (@acme, 'E-1020', 'Anita Rao',        '1988-12-11', '2016-02-08', 'QA',    'LEAD',    NULL,     '9876510020', 'anita.rao@acme.example',        'Active',   1),
  (@acme, 'E-1021', 'Vikram Shetty',    '1995-05-25', '2020-07-13', 'QA',    'ENGR',    'E-1020', '9876510021', 'vikram.shetty@acme.example',    'Active',   1),
  (@acme, 'E-1022', 'Fatima Ansari',    '1997-09-16', '2022-01-10', 'QA',    'JRENGR',  'E-1020', '9876510022', 'fatima.ansari@acme.example',    'Active',   1),

  -- ACME - Finance
  (@acme, 'E-1030', 'Sanjay Mehta',     '1982-03-05', '2013-04-01', 'FIN',   'MGR',     NULL,     '9876510030', 'sanjay.mehta@acme.example',     'Active',   1),
  (@acme, 'E-1031', 'Divya Nambiar',    '1991-10-28', '2018-08-20', 'FIN',   'ANALYST', 'E-1030', '9876510031', 'divya.nambiar@acme.example',    'Active',   1),
  (@acme, 'E-1032', 'Harsh Vardhan',    '1994-06-12', '2021-06-07', 'FIN',   'ANALYST', 'E-1030', '9876510032', 'harsh.vardhan@acme.example',    'Active',   1),

  -- ACME - Operations, HR, Sales
  (@acme, 'E-1040', 'Lakshmi Iyer',     '1987-07-19', '2014-10-06', 'OPS',   'MGR',     NULL,     '9876510040', 'lakshmi.iyer@acme.example',     'Active',   1),
  (@acme, 'E-1041', 'Gaurav Pillai',    '1993-02-27', '2019-02-11', 'OPS',   'ANALYST', 'E-1040', '9876510041', 'gaurav.pillai@acme.example',    'Active',   1),
  (@acme, 'E-1050', 'Ritu Chawla',      '1989-11-30', '2016-09-05', 'HR',    'MGR',     NULL,     '9876510050', 'ritu.chawla@acme.example',      'Active',   1),
  (@acme, 'E-1051', 'Neeraj Kohli',     '1996-01-21', '2022-04-18', 'HR',    'ANALYST', 'E-1050', '9876510051', 'neeraj.kohli@acme.example',     'Active',   1),
  (@acme, 'E-1060', 'Alok Srivastava',  '1984-05-09', '2012-07-02', 'SALES', 'DIR',     NULL,     '9876510060', 'alok.srivastava@acme.example',  'Active',   1),
  (@acme, 'E-1061', 'Megha Joshi',      '1992-12-03', '2018-03-26', 'SALES', 'MGR',     'E-1060', '9876510061', 'megha.joshi@acme.example',      'Active',   1),
  (@acme, 'E-1062', 'Sameer Qureshi',   '1998-08-08', '2023-01-09', 'SALES', 'ANALYST', 'E-1061', '9876510062', 'sameer.qureshi@acme.example',   'Active',   1),

  -- Left the company. Soft delete only (section 5) - the row stays, is_active goes to 0.
  (@acme, 'E-1070', 'Deepak Chandra',   '1986-04-02', '2015-01-12', 'OPS',   'ANALYST', NULL,     '9876510070', 'deepak.chandra@acme.example',   'Exited',   0),

  -- GLOBEX - a second tenant with its OWN codes, so a cross-tenant leak is obvious on sight
  (@globex, 'G-2010', 'Elena Ruiz',      '1989-03-17', '2017-02-06', 'CONSULT', 'SRCONS', NULL,     '9123410010', 'elena.ruiz@globex.example',     'Active', 1),
  (@globex, 'G-2011', 'Marcus Feldman',  '1991-06-24', '2019-05-20', 'CONSULT', 'CONS',   'G-2010', '9123410011', 'marcus.feldman@globex.example', 'Active', 1),
  (@globex, 'G-2012', 'Yuki Tanaka',     '1994-10-05', '2021-08-16', 'CONSULT', 'CONS',   'G-2010', '9123410012', 'yuki.tanaka@globex.example',    'Active', 1),
  (@globex, 'G-2020', 'Olivia Barnes',   '1990-01-29', '2018-11-12', 'SUPPORT', 'SUPENG', NULL,     '9123410020', 'olivia.barnes@globex.example',  'Active', 1),
  (@globex, 'G-2021', 'Peter Novak',     '1997-07-11', '2022-06-27', 'SUPPORT', 'SUPENG', 'G-2020', '9123410021', 'peter.novak@globex.example',    'Active', 1);

INSERT INTO hr_employee
  (tenant_id, employee_code, full_name, dob, date_of_joining, department_id, designation_id,
   mobile, email, employment_status, is_active, created_by, created_on)
SELECT t.tenant_id, t.employee_code, t.full_name, t.dob, t.doj,
       d.department_id, g.designation_id,
       t.mobile, t.email, t.status, t.active, @by, NOW()
FROM       tmp_emp t
LEFT JOIN  hr_department  d ON d.tenant_id = t.tenant_id AND d.dept_code  = t.dept_code
LEFT JOIN  hr_designation g ON g.tenant_id = t.tenant_id AND g.desig_code = t.desig_code
WHERE NOT EXISTS (SELECT 1 FROM hr_employee e
                  WHERE e.tenant_id = t.tenant_id AND e.employee_code = t.employee_code);

-- The manager is a second pass: a row cannot point at somebody who is not inserted yet.
UPDATE hr_employee e
JOIN   tmp_emp     t ON t.tenant_id = e.tenant_id AND t.employee_code = e.employee_code
JOIN   hr_employee m ON m.tenant_id = t.tenant_id AND m.employee_code = t.mgr_code
SET    e.reporting_manager_id = m.employee_id,
       e.updated_by = @by,
       e.updated_on = NOW()
WHERE  t.mgr_code IS NOT NULL
  AND  (e.reporting_manager_id IS NULL OR e.reporting_manager_id <> m.employee_id);

-- ---------------------------------------------------------------------
-- Leave requests
--
--  Every status the Leave screen can show, plus a pending one for each tenant so
--  the approve path has something to act on.
-- ---------------------------------------------------------------------

DROP TEMPORARY TABLE IF EXISTS tmp_leave;
CREATE TEMPORARY TABLE tmp_leave (
  tenant_id  INT,
  emp_code   VARCHAR(40),
  leave_code VARCHAR(40),
  from_date  DATE,
  to_date    DATE,
  reason     VARCHAR(500),
  status     VARCHAR(20),
  remark     VARCHAR(500)
);

INSERT INTO tmp_leave VALUES
  (@acme, 'E-1011', 'CL', '2026-09-07', '2026-09-08', 'Family function',              'Pending',  NULL),
  (@acme, 'E-1012', 'SL', '2026-08-24', '2026-08-26', 'Viral fever',                  'Pending',  NULL),
  (@acme, 'E-1013', 'CL', '2026-09-14', '2026-09-14', 'Personal errand',              'Pending',  NULL),
  (@acme, 'E-1014', 'EL', '2026-10-05', '2026-10-09', 'Annual holiday',               'Approved', 'Handover to Sneha agreed.'),
  (@acme, 'E-1021', 'SL', '2026-07-15', '2026-07-16', 'Dental surgery',               'Approved', 'Get well soon.'),
  (@acme, 'E-1022', 'CL', '2026-08-03', '2026-08-04', 'House shifting',               'Approved', NULL),
  (@acme, 'E-1031', 'EL', '2026-09-21', '2026-09-25', 'Wedding in the family',        'Rejected', 'Quarter close falls in that week.'),
  (@acme, 'E-1041', 'CL', '2026-06-11', '2026-06-11', 'Medical appointment',          'Approved', NULL),
  (@acme, 'E-1051', 'SL', '2026-08-18', '2026-08-19', 'Migraine',                     'Approved', NULL),
  (@acme, 'E-1062', 'CL', '2026-09-30', '2026-10-01', 'Travel',                       'Pending',  NULL),

  (@globex, 'G-2011', 'CL', '2026-09-02', '2026-09-03', 'Personal',                   'Pending',  NULL),
  (@globex, 'G-2012', 'EL', '2026-10-12', '2026-10-16', 'Vacation',                   'Approved', 'Client informed.'),
  (@globex, 'G-2021', 'SL', '2026-08-11', '2026-08-12', 'Flu',                        'Approved', NULL);

INSERT INTO hr_leave_request
  (tenant_id, employee_id, leave_type_id, from_date, to_date, days, reason, status,
   approved_by, approved_on, approval_remark, created_by, created_on)
SELECT t.tenant_id, e.employee_id, lt.leave_type_id, t.from_date, t.to_date,
       DATEDIFF(t.to_date, t.from_date) + 1,
       t.reason, t.status,
       CASE WHEN t.status = 'Pending' THEN NULL ELSE @by END,
       CASE WHEN t.status = 'Pending' THEN NULL ELSE TIMESTAMP(t.from_date) END,
       t.remark, @by, NOW()
FROM      tmp_leave     t
JOIN      hr_employee   e  ON e.tenant_id = t.tenant_id AND e.employee_code = t.emp_code
JOIN      hr_leave_type lt ON lt.tenant_id = t.tenant_id AND lt.leave_code = t.leave_code
WHERE NOT EXISTS (SELECT 1 FROM hr_leave_request r
                  WHERE r.tenant_id  = t.tenant_id
                    AND r.employee_id = e.employee_id
                    AND r.from_date  = t.from_date);

-- ---------------------------------------------------------------------
-- Payroll (layer 3) - only for a tenant that licenses it
-- ---------------------------------------------------------------------

DROP TEMPORARY TABLE IF EXISTS tmp_pay;
CREATE TEMPORARY TABLE tmp_pay (tenant_id INT, period_label VARCHAR(40), status VARCHAR(30), run_on DATE);

INSERT INTO tmp_pay VALUES
  (@acme, '2026-04', 'Completed', '2026-04-30'),
  (@acme, '2026-05', 'Completed', '2026-05-31'),
  (@acme, '2026-06', 'Completed', '2026-06-30'),
  (@acme, '2026-08', 'Draft',     NULL);

INSERT INTO pay_payroll_run (tenant_id, period_label, status, employee_count, run_on, created_by, created_on)
SELECT t.tenant_id, t.period_label, t.status,
       (SELECT COUNT(*) FROM hr_employee e WHERE e.tenant_id = t.tenant_id AND e.is_active = 1),
       t.run_on, @by, NOW()
FROM   tmp_pay t
WHERE  NOT EXISTS (SELECT 1 FROM pay_payroll_run p
                   WHERE p.tenant_id = t.tenant_id AND p.period_label = t.period_label);

-- ---------------------------------------------------------------------

DROP TEMPORARY TABLE IF EXISTS tmp_dept;
DROP TEMPORARY TABLE IF EXISTS tmp_desig;
DROP TEMPORARY TABLE IF EXISTS tmp_emp;
DROP TEMPORARY TABLE IF EXISTS tmp_leave;
DROP TEMPORARY TABLE IF EXISTS tmp_pay;

SELECT t.tenant_code,
       (SELECT COUNT(*) FROM hr_employee      e WHERE e.tenant_id = t.tenant_id) AS employees,
       (SELECT COUNT(*) FROM hr_leave_request r WHERE r.tenant_id = t.tenant_id) AS leave_requests,
       (SELECT COUNT(*) FROM hr_department    d WHERE d.tenant_id = t.tenant_id) AS departments,
       (SELECT COUNT(*) FROM hr_designation   g WHERE g.tenant_id = t.tenant_id) AS designations
FROM   sys_tenant t
ORDER  BY t.tenant_code;
