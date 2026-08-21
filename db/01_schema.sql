-- =====================================================================
--  HrSuite - schema
--  Conventions (section 5):
--    * tables      : sys_* | hr_* | cfg_* | ext_*
--    * every table : tenant_id, created_by, created_on, updated_by, updated_on, is_active
--    * soft delete : is_active = 0. No hard delete on master data.
--    * all access  : stored procedures only. Nothing here is queried inline.
--  Apply order: 01_schema -> 02_procs_sys -> 03_procs_hr -> 04_procs_cfg
--               -> 05_procs_ext -> 06_seed
-- =====================================================================

CREATE DATABASE IF NOT EXISTS astitwa
  DEFAULT CHARACTER SET utf8mb4
  DEFAULT COLLATE utf8mb4_unicode_ci;

USE astitwa;

SET FOREIGN_KEY_CHECKS = 0;

-- ---------------------------------------------------------------------
-- SYS - tenancy, identity, licensing
-- ---------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS sys_tenant (
  tenant_id     INT AUTO_INCREMENT PRIMARY KEY,
  tenant_code   VARCHAR(40)  NOT NULL,
  tenant_name   VARCHAR(150) NOT NULL,
  is_active     BIT          NOT NULL DEFAULT 1,
  created_by    INT          NULL,
  created_on    DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_by    INT          NULL,
  updated_on    DATETIME     NULL,
  UNIQUE KEY uk_sys_tenant_code (tenant_code)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS sys_user (
  user_id       INT AUTO_INCREMENT PRIMARY KEY,
  tenant_id     INT          NOT NULL,
  user_name     VARCHAR(80)  NOT NULL,
  display_name  VARCHAR(150) NOT NULL,
  email         VARCHAR(150) NULL,
  password_hash VARCHAR(255) NOT NULL,
  employee_id   INT          NULL,           -- links a login to an employee row
  last_login_on DATETIME     NULL,
  is_active     BIT          NOT NULL DEFAULT 1,
  created_by    INT          NULL,
  created_on    DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_by    INT          NULL,
  updated_on    DATETIME     NULL,
  UNIQUE KEY uk_sys_user (tenant_id, user_name),
  KEY ix_sys_user_tenant (tenant_id, is_active)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS sys_role (
  role_id       INT AUTO_INCREMENT PRIMARY KEY,
  tenant_id     INT          NOT NULL,
  role_code     VARCHAR(60)  NOT NULL,
  role_name     VARCHAR(150) NOT NULL,
  is_active     BIT          NOT NULL DEFAULT 1,
  created_by    INT          NULL,
  created_on    DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_by    INT          NULL,
  updated_on    DATETIME     NULL,
  UNIQUE KEY uk_sys_role (tenant_id, role_code)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS sys_user_role (
  user_role_id  INT AUTO_INCREMENT PRIMARY KEY,
  tenant_id     INT      NOT NULL,
  user_id       INT      NOT NULL,
  role_id       INT      NOT NULL,
  is_active     BIT      NOT NULL DEFAULT 1,
  created_by    INT      NULL,
  created_on    DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_by    INT      NULL,
  updated_on    DATETIME NULL,
  UNIQUE KEY uk_sys_user_role (tenant_id, user_id, role_id),
  KEY ix_sys_user_role_user (user_id)
) ENGINE=InnoDB;

-- Permission keys are declared by base code; a role simply holds a set of them.
CREATE TABLE IF NOT EXISTS sys_role_permission (
  role_permission_id INT AUTO_INCREMENT PRIMARY KEY,
  tenant_id      INT          NOT NULL,
  role_id        INT          NOT NULL,
  permission_key VARCHAR(120) NOT NULL,
  is_active      BIT          NOT NULL DEFAULT 1,
  created_by     INT          NULL,
  created_on     DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_by     INT          NULL,
  updated_on     DATETIME     NULL,
  UNIQUE KEY uk_sys_role_permission (tenant_id, role_id, permission_key),
  KEY ix_sys_role_permission_role (role_id)
) ENGINE=InnoDB;

-- Layer 3: which add-ons this client is licensed for.
CREATE TABLE IF NOT EXISTS sys_tenant_module (
  tenant_module_id INT AUTO_INCREMENT PRIMARY KEY,
  tenant_id     INT         NOT NULL,
  module_key    VARCHAR(80) NOT NULL,
  is_enabled    BIT         NOT NULL DEFAULT 0,
  is_active     BIT         NOT NULL DEFAULT 1,
  created_by    INT         NULL,
  created_on    DATETIME    NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_by    INT         NULL,
  updated_on    DATETIME    NULL,
  UNIQUE KEY uk_sys_tenant_module (tenant_id, module_key)
) ENGINE=InnoDB;

-- Layer 4: which external systems this client is connected to.
CREATE TABLE IF NOT EXISTS sys_tenant_integration (
  tenant_integration_id INT AUTO_INCREMENT PRIMARY KEY,
  tenant_id       INT         NOT NULL,
  integration_key VARCHAR(80) NOT NULL,
  settings_json   TEXT        NULL,
  is_enabled      BIT         NOT NULL DEFAULT 0,
  is_active       BIT         NOT NULL DEFAULT 1,
  created_by      INT         NULL,
  created_on      DATETIME    NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_by      INT         NULL,
  updated_on      DATETIME    NULL,
  UNIQUE KEY uk_sys_tenant_integration (tenant_id, integration_key)
) ENGINE=InnoDB;

-- ---------------------------------------------------------------------
-- HR - layer 1 base scope (section 6). Nothing more.
-- ---------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS hr_department (
  department_id INT AUTO_INCREMENT PRIMARY KEY,
  tenant_id     INT          NOT NULL,
  dept_code     VARCHAR(40)  NOT NULL,
  dept_name     VARCHAR(150) NOT NULL,
  is_active     BIT          NOT NULL DEFAULT 1,
  created_by    INT          NULL,
  created_on    DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_by    INT          NULL,
  updated_on    DATETIME     NULL,
  UNIQUE KEY uk_hr_department (tenant_id, dept_code),
  KEY ix_hr_department_name (tenant_id, dept_name)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS hr_designation (
  designation_id INT AUTO_INCREMENT PRIMARY KEY,
  tenant_id      INT          NOT NULL,
  desig_code     VARCHAR(40)  NOT NULL,
  desig_name     VARCHAR(150) NOT NULL,
  grade          VARCHAR(40)  NULL,
  is_active      BIT          NOT NULL DEFAULT 1,
  created_by     INT          NULL,
  created_on     DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_by     INT          NULL,
  updated_on     DATETIME     NULL,
  UNIQUE KEY uk_hr_designation (tenant_id, desig_code)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS hr_employee (
  employee_id          INT AUTO_INCREMENT PRIMARY KEY,
  tenant_id            INT          NOT NULL,
  employee_code        VARCHAR(40)  NOT NULL,
  full_name            VARCHAR(180) NOT NULL,
  dob                  DATE         NULL,
  date_of_joining      DATE         NULL,
  department_id        INT          NULL,
  designation_id       INT          NULL,
  reporting_manager_id INT          NULL,
  mobile               VARCHAR(30)  NULL,
  email                VARCHAR(150) NULL,
  employment_status    VARCHAR(30)  NOT NULL DEFAULT 'Active',
  -- Compensation. Nullable throughout: a tenant that does not track pay here simply
  -- hides these on the Employee screen with a cfg_field_rule row, and the columns stay
  -- empty. DECIMAL, never FLOAT - money must not carry binary rounding error.
  gross_ctc            DECIMAL(14,2) NULL,
  hra                  DECIMAL(14,2) NULL,
  tds                  DECIMAL(14,2) NULL,
  net_salary           DECIMAL(14,2) NULL,
  is_active            BIT          NOT NULL DEFAULT 1,
  created_by           INT          NULL,
  created_on           DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_by           INT          NULL,
  updated_on           DATETIME     NULL,
  -- Layer 1 rule: employee code is unique per tenant.
  UNIQUE KEY uk_hr_employee_code (tenant_id, employee_code),
  KEY ix_hr_employee_name (tenant_id, full_name),
  KEY ix_hr_employee_mobile (tenant_id, mobile),
  KEY ix_hr_employee_dept (tenant_id, department_id)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS hr_leave_type (
  leave_type_id INT AUTO_INCREMENT PRIMARY KEY,
  tenant_id     INT          NOT NULL,
  leave_code    VARCHAR(40)  NOT NULL,
  leave_name    VARCHAR(150) NOT NULL,
  is_active     BIT          NOT NULL DEFAULT 1,
  created_by    INT          NULL,
  created_on    DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_by    INT          NULL,
  updated_on    DATETIME     NULL,
  UNIQUE KEY uk_hr_leave_type (tenant_id, leave_code)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS hr_leave_request (
  leave_request_id INT AUTO_INCREMENT PRIMARY KEY,
  tenant_id        INT           NOT NULL,
  employee_id      INT           NOT NULL,
  leave_type_id    INT           NOT NULL,
  from_date        DATE          NOT NULL,
  to_date          DATE          NOT NULL,
  days             DECIMAL(5,1)  NOT NULL DEFAULT 0,
  reason           VARCHAR(500)  NULL,
  status           VARCHAR(20)   NOT NULL DEFAULT 'Pending',  -- Pending | Approved | Rejected
  approved_by      INT           NULL,
  approved_on      DATETIME      NULL,
  approval_remark  VARCHAR(500)  NULL,
  is_active        BIT           NOT NULL DEFAULT 1,
  created_by       INT           NULL,
  created_on       DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_by       INT           NULL,
  updated_on       DATETIME      NULL,
  KEY ix_hr_leave_request_emp (tenant_id, employee_id, status),
  KEY ix_hr_leave_request_status (tenant_id, status, from_date)
) ENGINE=InnoDB;

-- ---------------------------------------------------------------------
-- CFG - layer 2. Behaviour that changes without a build (section 7).
-- ---------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS cfg_setting (
  setting_id    INT AUTO_INCREMENT PRIMARY KEY,
  tenant_id     INT          NOT NULL,
  setting_key   VARCHAR(120) NOT NULL,
  setting_value TEXT         NULL,
  data_type     VARCHAR(20)  NOT NULL DEFAULT 'string',   -- string | int | bool | json
  is_active     BIT          NOT NULL DEFAULT 1,
  created_by    INT          NULL,
  created_on    DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_by    INT          NULL,
  updated_on    DATETIME     NULL,
  UNIQUE KEY uk_cfg (tenant_id, setting_key)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS cfg_field_rule (
  rule_id     INT AUTO_INCREMENT PRIMARY KEY,
  tenant_id   INT          NOT NULL,
  screen_key  VARCHAR(100) NOT NULL,   -- 'hr.employee'
  field_key   VARCHAR(100) NOT NULL,   -- 'reportingManager'
  is_visible  BIT          NOT NULL DEFAULT 1,
  is_required BIT          NOT NULL DEFAULT 0,
  label       VARCHAR(150) NULL,       -- override caption
  seq_no      INT          NOT NULL DEFAULT 10,
  is_active   BIT          NOT NULL DEFAULT 1,
  created_by  INT          NULL,
  created_on  DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_by  INT          NULL,
  updated_on  DATETIME     NULL,
  UNIQUE KEY uk_cfg_field_rule (tenant_id, screen_key, field_key),
  KEY ix_cfg_field_rule_screen (tenant_id, screen_key, seq_no)
) ENGINE=InnoDB;

-- ---------------------------------------------------------------------
-- EXT - layer 5. Client behaviour stored as data (section 10).
-- ---------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS ext_script_hook (
  hook_id     INT AUTO_INCREMENT PRIMARY KEY,
  tenant_id   INT          NULL,        -- NULL = applies to all tenants
  hook_key    VARCHAR(150) NOT NULL,    -- 'hr.employee.afterSave'
  seq_no      INT          NOT NULL DEFAULT 10,
  run_on      VARCHAR(10)  NOT NULL,    -- 'client' | 'server'
  script_body MEDIUMTEXT   NOT NULL,
  debounce_ms INT          NULL,
  is_active   BIT          NOT NULL DEFAULT 0,
  version_no  INT          NOT NULL DEFAULT 1,
  is_deleted  BIT          NOT NULL DEFAULT 0,
  created_by  INT          NULL,
  created_on  DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_by  INT          NULL,
  updated_on  DATETIME     NULL,
  KEY ix_ext_script_hook_lookup (hook_key, is_active, tenant_id, seq_no)
) ENGINE=InnoDB;

-- Every save pushes the outgoing row here first, so rollback is always possible.
CREATE TABLE IF NOT EXISTS ext_script_hook_history (
  history_id  INT AUTO_INCREMENT PRIMARY KEY,
  hook_id     INT          NOT NULL,
  tenant_id   INT          NULL,
  hook_key    VARCHAR(150) NOT NULL,
  seq_no      INT          NOT NULL DEFAULT 10,
  run_on      VARCHAR(10)  NOT NULL,
  script_body MEDIUMTEXT   NOT NULL,
  debounce_ms INT          NULL,
  is_active   BIT          NOT NULL DEFAULT 0,
  version_no  INT          NOT NULL,
  archived_by INT          NULL,
  archived_on DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  KEY ix_ext_script_hook_history (hook_id, version_no)
) ENGINE=InnoDB;

-- A script may run only the procedures registered here, with only the parameters
-- declared here, and may see only the columns whitelisted here.
CREATE TABLE IF NOT EXISTS ext_named_query (
  query_id     INT AUTO_INCREMENT PRIMARY KEY,
  tenant_id    INT          NULL,
  query_key    VARCHAR(120) NOT NULL,   -- 'hr.employee.searchByMobile'
  proc_name    VARCHAR(150) NOT NULL,   -- the ONLY thing executed
  params_json  TEXT         NULL,       -- declared parameter names + types
  columns_json TEXT         NULL,       -- declared output columns (whitelist)
  max_rows     INT          NOT NULL DEFAULT 100,
  required_permission VARCHAR(120) NULL,
  is_active    BIT          NOT NULL DEFAULT 1,
  created_by   INT          NULL,
  created_on   DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_by   INT          NULL,
  updated_on   DATETIME     NULL,
  KEY ix_ext_named_query_lookup (query_key, is_active, tenant_id)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS ext_hook_log (
  log_id       BIGINT AUTO_INCREMENT PRIMARY KEY,
  hook_id      INT         NULL,
  tenant_id    INT         NULL,
  hook_key     VARCHAR(150) NULL,
  run_on       VARCHAR(10)  NULL,
  status       VARCHAR(20)  NOT NULL,   -- ok | error | timeout
  duration_ms  INT          NOT NULL DEFAULT 0,
  message      TEXT         NULL,
  context_json TEXT         NULL,
  logged_by    INT          NULL,
  logged_on    DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  KEY ix_ext_hook_log_filter (tenant_id, status, logged_on),
  KEY ix_ext_hook_log_hook (hook_id, logged_on)
) ENGINE=InnoDB;

SET FOREIGN_KEY_CHECKS = 1;
