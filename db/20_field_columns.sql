-- =====================================================================
--  Demo Hospital - the Screen Field Builder, backed by real columns
--
--  Apply after 19_patient_registration_fields.sql. Re-runnable.
--
--  The Field Builder in 11/12 stores a tenant's extra fields as VALUES IN ROWS:
--  one row per field per record. That is safe and needs no DDL, and it is also
--  why a custom field can never be indexed, joined or reported on like a real
--  column. This file adds the other model beside it, the one the legacy product
--  used: a configured field IS a column on the screen's own table.
--
--    cfg_fb_screen        which screens may be configured, and what table each
--                         one writes to. Also the wizard's step labels.
--    cfg_fb_field         one row per field. A row with is_custom = 0 describes
--                         a column the product shipped - it exists so a custom
--                         field can be placed BETWEEN two shipped ones.
--    cfg_fb_field_option  static option lists.
--    cfg_fb_ddl_audit     every ALTER this feature has ever run, successful or
--                         not, with the exact statement text.
--    cfg_fb_value_archive what a dropped column held, written before the drop.
--
--  None of these five carry tenant_id, and that is deliberate: a column is
--  physical. It exists for every tenant on the instance or for none of them, so
--  a per-tenant definition would be a promise the database cannot keep. What a
--  tenant may see and edit is still decided by cfg_field_rule and permissions.
--
--  hr_job_requisition at the bottom is the first screen to use it: three steps,
--  a handful of shipped columns, and room for whatever a hospital adds.
-- =====================================================================

-- ---------------------------------------------------------------------
-- 1. The registry
-- ---------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS cfg_fb_screen (
  screen_id       INT AUTO_INCREMENT PRIMARY KEY,
  screen_code     VARCHAR(60)  NOT NULL,
  screen_name     VARCHAR(150) NOT NULL,
  -- The table the columns are added to, and its primary key. Both are read from
  -- here and never from the request: they are what makes an ALTER safe.
  base_table      VARCHAR(120) NOT NULL,
  pk_column       VARCHAR(120) NOT NULL,
  module_name     VARCHAR(80)  NULL,
  route_path      VARCHAR(160) NULL,
  -- 'Role,Compensation & Timeline,Review'. Order is the wizard's order.
  step_labels_csv VARCHAR(400) NULL,
  is_active       BIT          NOT NULL DEFAULT 1,
  created_by      INT          NULL,
  created_on      DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_by      INT          NULL,
  updated_on      DATETIME     NULL,
  UNIQUE KEY uk_cfg_fb_screen (screen_code)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS cfg_fb_field (
  field_id         INT AUTO_INCREMENT PRIMARY KEY,
  screen_id        INT          NOT NULL,
  -- The payload key the form uses. For a shipped column it is the camelCase name
  -- the API already sends; for a custom one it is the column name.
  field_key        VARCHAR(80)  NOT NULL,
  label            VARCHAR(160) NOT NULL,
  -- cf_… for a field this feature created; the real column name for a shipped one.
  column_name      VARCHAR(64)  NOT NULL,
  control_type     VARCHAR(30)  NOT NULL,
  -- Resolved on the server from control_type. A client-sent type is ignored.
  sql_type         VARCHAR(60)  NULL,
  is_required      BIT          NOT NULL DEFAULT 0,
  default_value    VARCHAR(255) NULL,
  -- range_min / range_max rather than min_value / max_value: MAXVALUE is reserved.
  range_min        VARCHAR(40)  NULL,
  range_max        VARCHAR(40)  NULL,
  max_length       INT          NULL,
  regex_pattern    VARCHAR(300) NULL,
  help_text        VARCHAR(300) NULL,
  placeholder      VARCHAR(160) NULL,
  step_index       INT          NOT NULL DEFAULT 0,
  sort_order       INT          NOT NULL DEFAULT 0,
  width            VARCHAR(10)  NOT NULL DEFAULT 'half',
  data_source_type VARCHAR(10)  NOT NULL DEFAULT 'None',
  -- Where the field may appear. The three Live-preview modes read these.
  show_in_form     BIT          NOT NULL DEFAULT 1,
  show_in_detail   BIT          NOT NULL DEFAULT 1,
  show_in_print    BIT          NOT NULL DEFAULT 1,
  -- 0 = a column the product ships. It is here to be placed against, and this
  -- feature refuses to alter or drop it.
  is_custom        BIT          NOT NULL DEFAULT 1,
  is_deleted       BIT          NOT NULL DEFAULT 0,
  created_by       INT          NULL,
  created_on       DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_by       INT          NULL,
  updated_on       DATETIME     NULL,
  KEY ix_cfg_fb_field_screen (screen_id, is_deleted, step_index, sort_order),
  -- Deliberately not unique: a dropped field's column name must be reusable, and
  -- uniqueness among live fields is checked in the service against the live table.
  KEY ix_cfg_fb_field_column (screen_id, column_name)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS cfg_fb_field_option (
  option_id    INT AUTO_INCREMENT PRIMARY KEY,
  field_id     INT          NOT NULL,
  option_value VARCHAR(200) NOT NULL,
  option_label VARCHAR(200) NOT NULL,
  sort_order   INT          NOT NULL DEFAULT 10,
  is_active    BIT          NOT NULL DEFAULT 1,
  KEY ix_cfg_fb_field_option (field_id, sort_order)
) ENGINE=InnoDB;

-- Every statement this feature has run. Kept whether it succeeded or not: the
-- failures are the interesting ones when a column is missing and nobody knows why.
CREATE TABLE IF NOT EXISTS cfg_fb_ddl_audit (
  audit_id     INT AUTO_INCREMENT PRIMARY KEY,
  screen_id    INT          NULL,
  action       VARCHAR(20)  NOT NULL,
  table_name   VARCHAR(120) NOT NULL,
  column_name  VARCHAR(64)  NULL,
  sql_text     TEXT         NOT NULL,
  performed_by INT          NULL,
  performed_on DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  success      BIT          NOT NULL DEFAULT 1,
  error_text   VARCHAR(500) NULL,
  KEY ix_cfg_fb_ddl_audit (screen_id, audit_id)
) ENGINE=InnoDB;

-- What a column held before it was dropped. A drop is irreversible in the table;
-- this is what makes it answerable afterwards.
CREATE TABLE IF NOT EXISTS cfg_fb_value_archive (
  archive_id  INT AUTO_INCREMENT PRIMARY KEY,
  screen_id   INT          NOT NULL,
  field_id    INT          NOT NULL,
  column_name VARCHAR(64)  NOT NULL,
  record_id   VARCHAR(60)  NOT NULL,
  value_text  TEXT         NULL,
  archived_by INT          NULL,
  archived_on DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  KEY ix_cfg_fb_value_archive (field_id, archive_id)
) ENGINE=InnoDB;

-- ---------------------------------------------------------------------
-- 2. The first screen to use it
-- ---------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS hr_job_requisition (
  requisition_id   INT AUTO_INCREMENT PRIMARY KEY,
  tenant_id        INT          NOT NULL,
  requisition_code VARCHAR(40)  NOT NULL,
  -- Step 1: the role
  job_title        VARCHAR(160) NOT NULL,
  department_id    INT          NULL,
  openings         INT          NOT NULL DEFAULT 1,
  experience_range VARCHAR(60)  NULL,
  employment_type  VARCHAR(40)  NULL,
  priority         VARCHAR(20)  NULL,
  key_skills       VARCHAR(400) NULL,
  -- Step 2: money and dates
  budget_min       DECIMAL(14,2) NULL,
  budget_max       DECIMAL(14,2) NULL,
  target_date      DATE         NULL,
  notes            VARCHAR(500) NULL,
  -- Step 3 is Review: it confirms what the first two captured and owns no column.
  status           VARCHAR(20)  NOT NULL DEFAULT 'Draft',
  is_active        BIT          NOT NULL DEFAULT 1,
  created_by       INT          NULL,
  created_on       DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_by       INT          NULL,
  updated_on       DATETIME     NULL,
  UNIQUE KEY uk_hr_job_requisition (tenant_id, requisition_code),
  KEY ix_hr_job_requisition_title (tenant_id, job_title)
) ENGINE=InnoDB;

-- ---------------------------------------------------------------------
-- 3. Procedures
--
-- Every one takes p_tenant_id and p_user_id whether it reads them or not:
-- RepositoryBase stamps both on every call, and a procedure that does not
-- declare them cannot be called at all.
-- ---------------------------------------------------------------------

DROP PROCEDURE IF EXISTS sp_cfg_fb_screen_list;
DROP PROCEDURE IF EXISTS sp_cfg_fb_screen_get;
DROP PROCEDURE IF EXISTS sp_cfg_fb_field_list;
DROP PROCEDURE IF EXISTS sp_cfg_fb_field_get;
DROP PROCEDURE IF EXISTS sp_cfg_fb_option_list;
DROP PROCEDURE IF EXISTS sp_cfg_fb_field_save;
DROP PROCEDURE IF EXISTS sp_cfg_fb_field_delete;
DROP PROCEDURE IF EXISTS sp_cfg_fb_field_reorder;
DROP PROCEDURE IF EXISTS sp_cfg_fb_audit_add;
DROP PROCEDURE IF EXISTS sp_cfg_fb_audit_list;
DROP PROCEDURE IF EXISTS sp_cfg_fb_archive_add;
DROP PROCEDURE IF EXISTS sp_hr_job_requisition_list;
DROP PROCEDURE IF EXISTS sp_hr_job_requisition_get;
DROP PROCEDURE IF EXISTS sp_hr_job_requisition_save;
DROP PROCEDURE IF EXISTS sp_hr_job_requisition_delete;

DELIMITER $$

CREATE PROCEDURE sp_cfg_fb_screen_list (IN p_tenant_id INT, IN p_user_id INT)
BEGIN
  SELECT s.screen_id, s.screen_code, s.screen_name, s.base_table, s.pk_column,
         s.module_name, s.route_path, s.step_labels_csv,
         (SELECT COUNT(*) FROM cfg_fb_field f
           WHERE f.screen_id = s.screen_id AND f.is_deleted = 0) AS field_count,
         (SELECT COUNT(*) FROM cfg_fb_field f
           WHERE f.screen_id = s.screen_id AND f.is_deleted = 0 AND f.is_custom = 1) AS custom_field_count
  FROM   cfg_fb_screen s
  WHERE  s.is_active = 1
  ORDER  BY s.screen_name;
END$$

CREATE PROCEDURE sp_cfg_fb_screen_get (IN p_tenant_id INT, IN p_user_id INT, IN p_screen_code VARCHAR(60))
BEGIN
  SELECT screen_id, screen_code, screen_name, base_table, pk_column,
         module_name, route_path, step_labels_csv
  FROM   cfg_fb_screen
  WHERE  screen_code = p_screen_code AND is_active = 1;
END$$

CREATE PROCEDURE sp_cfg_fb_field_list (IN p_tenant_id INT, IN p_user_id INT, IN p_screen_id INT)
BEGIN
  SELECT field_id, screen_id, field_key, label, column_name, control_type, sql_type,
         is_required, default_value, range_min, range_max, max_length, regex_pattern,
         help_text, placeholder, step_index, sort_order, width, data_source_type,
         show_in_form, show_in_detail, show_in_print, is_custom
  FROM   cfg_fb_field
  WHERE  screen_id = p_screen_id AND is_deleted = 0
  ORDER  BY step_index, sort_order, field_id;
END$$

CREATE PROCEDURE sp_cfg_fb_field_get (IN p_tenant_id INT, IN p_user_id INT, IN p_field_id INT)
BEGIN
  SELECT f.field_id, f.screen_id, f.field_key, f.label, f.column_name, f.control_type, f.sql_type,
         f.is_required, f.default_value, f.range_min, f.range_max, f.max_length, f.regex_pattern,
         f.help_text, f.placeholder, f.step_index, f.sort_order, f.width, f.data_source_type,
         f.show_in_form, f.show_in_detail, f.show_in_print, f.is_custom,
         s.screen_code, s.base_table, s.pk_column
  FROM   cfg_fb_field f
  JOIN   cfg_fb_screen s ON s.screen_id = f.screen_id
  WHERE  f.field_id = p_field_id AND f.is_deleted = 0;
END$$

CREATE PROCEDURE sp_cfg_fb_option_list (IN p_tenant_id INT, IN p_user_id INT, IN p_screen_id INT)
BEGIN
  SELECT o.option_id, o.field_id, o.option_value, o.option_label, o.sort_order
  FROM   cfg_fb_field_option o
  JOIN   cfg_fb_field f ON f.field_id = o.field_id
  WHERE  f.screen_id = p_screen_id AND f.is_deleted = 0 AND o.is_active = 1
  ORDER  BY o.field_id, o.sort_order, o.option_id;
END$$

-- =====================================================================
-- Writes the metadata row. The column itself is created by the service before
-- this runs: a row describing a column that does not exist would be a field the
-- form draws and the save cannot store.
--
-- Options arrive as a JSON array and replace the set, which is what the editor
-- edits. p_options NULL leaves them alone.
-- =====================================================================
CREATE PROCEDURE sp_cfg_fb_field_save (
  IN p_tenant_id        INT,
  IN p_user_id          INT,
  IN p_field_id         INT,
  IN p_screen_id        INT,
  IN p_field_key        VARCHAR(80),
  IN p_label            VARCHAR(160),
  IN p_column_name      VARCHAR(64),
  IN p_control_type     VARCHAR(30),
  IN p_sql_type         VARCHAR(60),
  IN p_is_required      TINYINT,
  IN p_default_value    VARCHAR(255),
  IN p_range_min        VARCHAR(40),
  IN p_range_max        VARCHAR(40),
  IN p_max_length       INT,
  IN p_regex_pattern    VARCHAR(300),
  IN p_help_text        VARCHAR(300),
  IN p_placeholder      VARCHAR(160),
  IN p_step_index       INT,
  IN p_sort_order       INT,
  IN p_width            VARCHAR(10),
  IN p_data_source_type VARCHAR(10),
  IN p_show_in_form     TINYINT,
  IN p_show_in_detail   TINYINT,
  IN p_show_in_print    TINYINT,
  IN p_options          JSON
)
BEGIN
  DECLARE v_id INT;

  IF IFNULL(p_field_id, 0) = 0 THEN
    INSERT INTO cfg_fb_field (
      screen_id, field_key, label, column_name, control_type, sql_type, is_required,
      default_value, range_min, range_max, max_length, regex_pattern, help_text, placeholder,
      step_index, sort_order, width, data_source_type,
      show_in_form, show_in_detail, show_in_print, is_custom, created_by, created_on)
    VALUES (
      p_screen_id, p_field_key, p_label, p_column_name, p_control_type, p_sql_type, IFNULL(p_is_required, 0),
      p_default_value, p_range_min, p_range_max, p_max_length, p_regex_pattern, p_help_text, p_placeholder,
      IFNULL(p_step_index, 0), IFNULL(p_sort_order, 0), IFNULL(p_width, 'half'), IFNULL(p_data_source_type, 'None'),
      IFNULL(p_show_in_form, 1), IFNULL(p_show_in_detail, 1), IFNULL(p_show_in_print, 1), 1,
      p_user_id, UTC_TIMESTAMP());
    SET v_id = LAST_INSERT_ID();
  ELSE
    UPDATE cfg_fb_field
    SET    label            = p_label,
           column_name      = p_column_name,
           control_type     = p_control_type,
           sql_type         = p_sql_type,
           is_required      = IFNULL(p_is_required, 0),
           default_value    = p_default_value,
           range_min        = p_range_min,
           range_max        = p_range_max,
           max_length       = p_max_length,
           regex_pattern    = p_regex_pattern,
           help_text        = p_help_text,
           placeholder      = p_placeholder,
           step_index       = IFNULL(p_step_index, step_index),
           sort_order       = IFNULL(p_sort_order, sort_order),
           width            = IFNULL(p_width, 'half'),
           data_source_type = IFNULL(p_data_source_type, 'None'),
           show_in_form     = IFNULL(p_show_in_form, 1),
           show_in_detail   = IFNULL(p_show_in_detail, 1),
           show_in_print    = IFNULL(p_show_in_print, 1),
           updated_by       = p_user_id,
           updated_on       = UTC_TIMESTAMP()
    WHERE  field_id = p_field_id AND is_custom = 1;
    SET v_id = p_field_id;
  END IF;

  IF p_options IS NOT NULL AND JSON_VALID(p_options) THEN
    DELETE FROM cfg_fb_field_option WHERE field_id = v_id;

    IF JSON_LENGTH(p_options) > 0 THEN
      INSERT INTO cfg_fb_field_option (field_id, option_value, option_label, sort_order, is_active)
      SELECT v_id, j.option_value, IFNULL(j.option_label, j.option_value), j.rn * 10, 1
      FROM   JSON_TABLE(p_options, '$[*]' COLUMNS (
               rn           FOR ORDINALITY,
               option_value VARCHAR(200) PATH '$.value',
               option_label VARCHAR(200) PATH '$.label'
             )) AS j
      WHERE  j.option_value IS NOT NULL AND j.option_value <> '';
    END IF;
  END IF;

  SELECT field_id, screen_id, field_key, label, column_name, control_type, sql_type,
         is_required, default_value, range_min, range_max, max_length, regex_pattern,
         help_text, placeholder, step_index, sort_order, width, data_source_type,
         show_in_form, show_in_detail, show_in_print, is_custom
  FROM   cfg_fb_field
  WHERE  field_id = v_id;
END$$

-- Soft delete of the metadata. The column is dropped by the service, after the
-- values are archived; this only stops the form drawing the field.
CREATE PROCEDURE sp_cfg_fb_field_delete (IN p_tenant_id INT, IN p_user_id INT, IN p_field_id INT)
BEGIN
  UPDATE cfg_fb_field
  SET    is_deleted = 1,
         updated_by = p_user_id,
         updated_on = UTC_TIMESTAMP()
  WHERE  field_id = p_field_id AND is_custom = 1;
END$$

-- Step and position for several fields at once, as the structure list left them.
CREATE PROCEDURE sp_cfg_fb_field_reorder (IN p_tenant_id INT, IN p_user_id INT, IN p_items JSON)
BEGIN
  IF p_items IS NOT NULL AND JSON_VALID(p_items) AND JSON_LENGTH(p_items) > 0 THEN
    UPDATE cfg_fb_field f
    JOIN   JSON_TABLE(p_items, '$[*]' COLUMNS (
             field_id   INT PATH '$.fieldId',
             step_index INT PATH '$.stepIndex',
             sort_order INT PATH '$.sortOrder'
           )) AS j ON j.field_id = f.field_id
    SET    f.step_index = j.step_index,
           f.sort_order = j.sort_order,
           f.updated_by = p_user_id,
           f.updated_on = UTC_TIMESTAMP()
    WHERE  f.is_custom = 1;
  END IF;
END$$

CREATE PROCEDURE sp_cfg_fb_audit_add (
  IN p_tenant_id   INT,
  IN p_user_id     INT,
  IN p_screen_id   INT,
  IN p_action      VARCHAR(20),
  IN p_table_name  VARCHAR(120),
  IN p_column_name VARCHAR(64),
  IN p_sql_text    TEXT,
  IN p_success     TINYINT,
  IN p_error_text  VARCHAR(500)
)
BEGIN
  INSERT INTO cfg_fb_ddl_audit (screen_id, action, table_name, column_name, sql_text,
                                performed_by, performed_on, success, error_text)
  VALUES (p_screen_id, p_action, p_table_name, p_column_name, p_sql_text,
          p_user_id, UTC_TIMESTAMP(), IFNULL(p_success, 1), p_error_text);
END$$

CREATE PROCEDURE sp_cfg_fb_audit_list (IN p_tenant_id INT, IN p_user_id INT, IN p_screen_code VARCHAR(60), IN p_take INT)
BEGIN
  SELECT d.audit_id, d.action, d.table_name, d.column_name, d.sql_text, d.performed_by,
         d.performed_on, d.success, d.error_text, s.screen_code, s.screen_name
  FROM   cfg_fb_ddl_audit d
  LEFT   JOIN cfg_fb_screen s ON s.screen_id = d.screen_id
  WHERE  (p_screen_code IS NULL OR s.screen_code = p_screen_code)
  ORDER  BY d.audit_id DESC
  LIMIT  100;
END$$

CREATE PROCEDURE sp_cfg_fb_archive_add (
  IN p_tenant_id   INT,
  IN p_user_id     INT,
  IN p_screen_id   INT,
  IN p_field_id    INT,
  IN p_column_name VARCHAR(64),
  IN p_rows        JSON
)
BEGIN
  IF p_rows IS NOT NULL AND JSON_VALID(p_rows) AND JSON_LENGTH(p_rows) > 0 THEN
    INSERT INTO cfg_fb_value_archive (screen_id, field_id, column_name, record_id, value_text,
                                      archived_by, archived_on)
    SELECT p_screen_id, p_field_id, p_column_name, j.record_id, j.value_text, p_user_id, UTC_TIMESTAMP()
    FROM   JSON_TABLE(p_rows, '$[*]' COLUMNS (
             record_id  VARCHAR(60) PATH '$.recordId',
             value_text TEXT        PATH '$.value'
           )) AS j;
  END IF;
END$$

-- ---------------------------------------------------------------------
-- Job requisitions. The shipped columns only; whatever a hospital adds through
-- the builder is read straight off the same row.
-- ---------------------------------------------------------------------

CREATE PROCEDURE sp_hr_job_requisition_list (
  IN p_tenant_id INT,
  IN p_user_id   INT,
  IN p_search    VARCHAR(200),
  IN p_page_size INT,
  IN p_offset    INT
)
BEGIN
  SELECT r.requisition_id, r.requisition_code, r.job_title, r.department_id,
         d.dept_name AS department_name, r.openings, r.experience_range, r.employment_type,
         r.priority, r.target_date, r.status, r.is_active
  FROM   hr_job_requisition r
  LEFT   JOIN hr_department d ON d.department_id = r.department_id AND d.tenant_id = r.tenant_id
  WHERE  r.tenant_id = p_tenant_id
    AND  r.is_active = 1
    AND  (p_search IS NULL OR r.requisition_code LIKE CONCAT('%', p_search, '%')
                           OR r.job_title        LIKE CONCAT('%', p_search, '%')
                           OR r.employment_type  LIKE CONCAT('%', p_search, '%'))
  ORDER  BY r.requisition_id DESC
  LIMIT  p_page_size OFFSET p_offset;

  SELECT COUNT(*) AS total_count
  FROM   hr_job_requisition r
  WHERE  r.tenant_id = p_tenant_id
    AND  r.is_active = 1
    AND  (p_search IS NULL OR r.requisition_code LIKE CONCAT('%', p_search, '%')
                           OR r.job_title        LIKE CONCAT('%', p_search, '%')
                           OR r.employment_type  LIKE CONCAT('%', p_search, '%'));
END$$

CREATE PROCEDURE sp_hr_job_requisition_get (IN p_tenant_id INT, IN p_user_id INT, IN p_requisition_id INT)
BEGIN
  SELECT requisition_id, requisition_code, job_title, department_id, openings, experience_range,
         employment_type, priority, key_skills, budget_min, budget_max, target_date, notes,
         status, is_active
  FROM   hr_job_requisition
  WHERE  tenant_id = p_tenant_id AND requisition_id = p_requisition_id AND is_active = 1;
END$$

CREATE PROCEDURE sp_hr_job_requisition_save (
  IN p_tenant_id        INT,
  IN p_user_id          INT,
  IN p_requisition_id   INT,
  IN p_requisition_code VARCHAR(40),
  IN p_job_title        VARCHAR(160),
  IN p_department_id    INT,
  IN p_openings         INT,
  IN p_experience_range VARCHAR(60),
  IN p_employment_type  VARCHAR(40),
  IN p_priority         VARCHAR(20),
  IN p_key_skills       VARCHAR(400),
  IN p_budget_min       DECIMAL(14,2),
  IN p_budget_max       DECIMAL(14,2),
  IN p_target_date      DATE,
  IN p_notes            VARCHAR(500),
  IN p_status           VARCHAR(20)
)
BEGIN
  DECLARE v_id   INT;
  DECLARE v_code VARCHAR(40);

  SET v_code = NULLIF(TRIM(IFNULL(p_requisition_code, '')), '');

  IF IFNULL(p_requisition_id, 0) = 0 AND v_code IS NULL THEN
    -- Same series mechanism the UHID uses, under a key of its own.
    CALL sp_sys_number_series_next(p_tenant_id, p_user_id, 'hr.jobRequisition.code', v_code);
  END IF;

  IF IFNULL(p_requisition_id, 0) = 0 THEN
    INSERT INTO hr_job_requisition (
      tenant_id, requisition_code, job_title, department_id, openings, experience_range,
      employment_type, priority, key_skills, budget_min, budget_max, target_date, notes,
      status, created_by, created_on)
    VALUES (
      p_tenant_id, v_code, p_job_title, p_department_id, IFNULL(p_openings, 1), p_experience_range,
      p_employment_type, p_priority, p_key_skills, p_budget_min, p_budget_max, p_target_date, p_notes,
      IFNULL(p_status, 'Draft'), p_user_id, UTC_TIMESTAMP());
    SET v_id = LAST_INSERT_ID();
  ELSE
    UPDATE hr_job_requisition
    SET    requisition_code = IFNULL(v_code, requisition_code),
           job_title        = p_job_title,
           department_id    = p_department_id,
           openings         = IFNULL(p_openings, 1),
           experience_range = p_experience_range,
           employment_type  = p_employment_type,
           priority         = p_priority,
           key_skills       = p_key_skills,
           budget_min       = p_budget_min,
           budget_max       = p_budget_max,
           target_date      = p_target_date,
           notes            = p_notes,
           status           = IFNULL(p_status, status),
           updated_by       = p_user_id,
           updated_on       = UTC_TIMESTAMP()
    WHERE  tenant_id = p_tenant_id AND requisition_id = p_requisition_id;
    SET v_id = p_requisition_id;
  END IF;

  SELECT requisition_id, requisition_code, job_title, department_id, openings, experience_range,
         employment_type, priority, key_skills, budget_min, budget_max, target_date, notes,
         status, is_active
  FROM   hr_job_requisition
  WHERE  tenant_id = p_tenant_id AND requisition_id = v_id;
END$$

CREATE PROCEDURE sp_hr_job_requisition_delete (IN p_tenant_id INT, IN p_user_id INT, IN p_requisition_id INT)
BEGIN
  UPDATE hr_job_requisition
  SET    is_active  = 0,
         updated_by = p_user_id,
         updated_on = UTC_TIMESTAMP()
  WHERE  tenant_id = p_tenant_id AND requisition_id = p_requisition_id;
END$$

DELIMITER ;

-- ---------------------------------------------------------------------
-- 4. Seed: the requisition screen and the columns it ships with
--
-- The anchor rows are what make placement possible. Without them the builder
-- can only append, because it has nothing to put a new column after.
-- ---------------------------------------------------------------------

INSERT INTO cfg_fb_screen (screen_code, screen_name, base_table, pk_column, module_name, route_path, step_labels_csv, created_by, created_on)
SELECT 'HR_JOB_REQUISITION', 'New Job Requisition — Recruitment', 'hr_job_requisition', 'requisition_id',
       'Recruitment', '/hr/recruitment', 'Role,Compensation & Timeline,Review', 1, UTC_TIMESTAMP()
WHERE NOT EXISTS (SELECT 1 FROM cfg_fb_screen WHERE screen_code = 'HR_JOB_REQUISITION');

INSERT INTO cfg_fb_field (screen_id, field_key, label, column_name, control_type, sql_type,
                          is_required, step_index, sort_order, width, is_custom, created_by, created_on)
SELECT s.screen_id, a.field_key, a.label, a.column_name, a.control_type, a.sql_type,
       a.is_required, a.step_index, a.sort_order, a.width, 0, 1, UTC_TIMESTAMP()
FROM   cfg_fb_screen s
JOIN (
  SELECT 'jobTitle'        AS field_key, 'Job title'        AS label, 'job_title'        AS column_name, 'text'     AS control_type, 'VARCHAR(160)'  AS sql_type, 1 AS is_required, 0 AS step_index, 10 AS sort_order, 'full' AS width
  UNION ALL SELECT 'departmentId',    'Department',       'department_id',    'dropdown', 'INT',           1, 0, 20,  'half'
  UNION ALL SELECT 'openings',        'Openings',         'openings',         'number',   'INT',           1, 0, 30,  'half'
  UNION ALL SELECT 'experienceRange', 'Experience range', 'experience_range', 'text',     'VARCHAR(60)',   0, 0, 40,  'half'
  UNION ALL SELECT 'employmentType',  'Employment type',  'employment_type',  'dropdown', 'VARCHAR(40)',   0, 0, 50,  'half'
  UNION ALL SELECT 'priority',        'Priority',         'priority',         'dropdown', 'VARCHAR(20)',   0, 0, 60,  'half'
  UNION ALL SELECT 'keySkills',       'Key skills',       'key_skills',       'textarea', 'VARCHAR(400)',  0, 0, 70,  'full'
  UNION ALL SELECT 'budgetMin',       'Budget from',      'budget_min',       'decimal',  'DECIMAL(14,2)', 0, 1, 10,  'half'
  UNION ALL SELECT 'budgetMax',       'Budget to',        'budget_max',       'decimal',  'DECIMAL(14,2)', 0, 1, 20,  'half'
  UNION ALL SELECT 'targetDate',      'Target date',      'target_date',      'date',     'DATE',          0, 1, 30,  'half'
  UNION ALL SELECT 'status',          'Status',           'status',           'dropdown', 'VARCHAR(20)',   0, 1, 40,  'half'
  UNION ALL SELECT 'notes',           'Notes',            'notes',            'textarea', 'VARCHAR(500)',  0, 1, 50,  'full'
) a
WHERE  s.screen_code = 'HR_JOB_REQUISITION'
  AND  NOT EXISTS (SELECT 1 FROM cfg_fb_field f
                    WHERE f.screen_id = s.screen_id AND f.field_key = a.field_key);

-- The dropdowns the shipped columns use. Department comes from a lookup, so it
-- has no static list of its own.
INSERT INTO cfg_fb_field_option (field_id, option_value, option_label, sort_order, is_active)
SELECT f.field_id, o.option_value, o.option_label, o.sort_order, 1
FROM   cfg_fb_field f
JOIN   cfg_fb_screen s ON s.screen_id = f.screen_id AND s.screen_code = 'HR_JOB_REQUISITION'
JOIN (
  SELECT 'employmentType' AS field_key, 'Full time'  AS option_value, 'Full time'  AS option_label, 10 AS sort_order
  UNION ALL SELECT 'employmentType', 'Part time',  'Part time',  20
  UNION ALL SELECT 'employmentType', 'Contract',   'Contract',   30
  UNION ALL SELECT 'employmentType', 'Locum',      'Locum',      40
  UNION ALL SELECT 'priority',       'Low',        'Low',        10
  UNION ALL SELECT 'priority',       'Medium',     'Medium',     20
  UNION ALL SELECT 'priority',       'High',       'High',       30
  UNION ALL SELECT 'priority',       'Critical',   'Critical',   40
  UNION ALL SELECT 'status',         'Draft',      'Draft',      10
  UNION ALL SELECT 'status',         'Approved',   'Approved',   20
  UNION ALL SELECT 'status',         'On hold',    'On hold',    30
  UNION ALL SELECT 'status',         'Closed',     'Closed',     40
) o ON o.field_key = f.field_key
WHERE  NOT EXISTS (SELECT 1 FROM cfg_fb_field_option x
                    WHERE x.field_id = f.field_id AND x.option_value = o.option_value);

-- The requisition code series, alongside the patient one.
INSERT INTO sys_number_series (tenant_id, series_key, prefix, pad_width, next_no, updated_on)
SELECT t.tenant_id, 'hr.jobRequisition.code', 'REQ-', 4, 1, UTC_TIMESTAMP()
FROM   sys_tenant t
ON DUPLICATE KEY UPDATE series_key = sys_number_series.series_key;

-- ---------------------------------------------------------------------
-- 5. Who may see it
-- ---------------------------------------------------------------------

INSERT INTO sys_role_permission (tenant_id, role_id, permission_key, created_by, created_on)
SELECT r.tenant_id, r.role_id, p.permission_key, 1, UTC_TIMESTAMP()
FROM   sys_role r
JOIN (
  SELECT 'ADMIN'   AS role_code, 'hr.jobRequisition.view' AS permission_key
  UNION ALL SELECT 'ADMIN',   'hr.jobRequisition.edit'
  UNION ALL SELECT 'ADMIN',   'admin.fieldColumn'
  UNION ALL SELECT 'HR',      'hr.jobRequisition.view'
  UNION ALL SELECT 'HR',      'hr.jobRequisition.edit'
  UNION ALL SELECT 'MANAGER', 'hr.jobRequisition.view'
) p ON p.role_code = r.role_code
WHERE NOT EXISTS (
  SELECT 1 FROM sys_role_permission x
  WHERE x.tenant_id = r.tenant_id AND x.role_id = r.role_id AND x.permission_key = p.permission_key
);

SELECT (SELECT COUNT(*) FROM cfg_fb_screen)                                  AS screens,
       (SELECT COUNT(*) FROM cfg_fb_field WHERE is_custom = 0)               AS anchors,
       (SELECT COUNT(*) FROM cfg_fb_field_option)                            AS options,
       (SELECT COUNT(*) FROM hr_job_requisition)                             AS requisitions;
