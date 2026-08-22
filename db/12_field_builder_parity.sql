-- =====================================================================
--  HrSuite - field builder parity: computed fields, bound dropdowns,
--                                  change audit and value archive
--
--  Apply after 11_print_fields_documents.sql. Re-runnable.
--
--  Three additions, each mirroring something the reference implementation
--  does, adapted to this product's rules.
--
--  1. Computed fields
--     A field's value can be derived from other fields of the same screen
--     rather than typed: "{basicPay} * 0.10". The grammar is fixed and tiny
--     (+ - * / comparisons, MIN MAX ROUND IF) and is implemented twice, in
--     the browser for immediacy and on the server for truth. The server
--     recalculates on every save and its answer is the one stored.
--
--  2. Bound dropdowns
--     The reference lets an administrator point a dropdown at an API. The
--     endpoint is never free text - it is a row in an allowlist, because a
--     configuration screen that accepts a URL is an SSRF. Here the allowlist
--     offers three kinds of source and none of them is a URL a user typed:
--
--       Lookup      one of the product's own bounded lookup procedures
--       NamedQuery  a query registered in ext_named_query, which already
--                   validates the key, binds only declared parameters,
--                   strips undeclared columns, caps the rows and checks the
--                   caller's permission
--       Api         a relative path inside this API, resolved by the browser
--                   with the caller's own token
--
--     Storing SQL in a registry row - which is what the reference does for
--     its Sql sources - is not possible here: RepositoryBase has no overload
--     that accepts SQL text, which is the point of it.
--
--  3. Audit and archive
--     The reference audits DDL, because adding a field there runs ALTER
--     TABLE. Nothing is ALTERed here, so the equivalent record is what
--     actually changes: the field definition itself, before and after. The
--     archive keeps a record's values at the moment a field is removed, so
--     "remove this field" stays reversible.
-- =====================================================================

USE astitwa;

-- ---------------------------------------------------------------------
-- 1. Computed fields
-- ---------------------------------------------------------------------

-- MySQL has no ADD COLUMN IF NOT EXISTS, so each addition is guarded by a
-- look in INFORMATION_SCHEMA and applied through a prepared statement. The
-- statement text is fixed here in the script; nothing is composed from data.
DROP PROCEDURE IF EXISTS sp_tmp_add_column;

DELIMITER $$

CREATE PROCEDURE sp_tmp_add_column (
  IN p_table  VARCHAR(64),
  IN p_column VARCHAR(64),
  IN p_ddl    VARCHAR(500)
)
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = p_table AND COLUMN_NAME = p_column
  ) THEN
    SET @sql = CONCAT('ALTER TABLE `', p_table, '` ADD COLUMN ', p_ddl);
    PREPARE stmt FROM @sql;
    EXECUTE stmt;
    DEALLOCATE PREPARE stmt;
  END IF;
END$$

DELIMITER ;

-- Manual | Computed. A computed field's control is read-only when the mode is
-- Always, and merely pre-filled when it is Prefill.
CALL sp_tmp_add_column('cfg_custom_field', 'value_mode',
  "value_mode VARCHAR(12) NOT NULL DEFAULT 'Manual' AFTER data_source_type");

CALL sp_tmp_add_column('cfg_custom_field', 'formula_text',
  'formula_text VARCHAR(800) NULL AFTER value_mode');

-- The keys the formula resolved to, cached so a dependant can be found without
-- re-parsing every formula on the screen.
CALL sp_tmp_add_column('cfg_custom_field', 'formula_refs_csv',
  'formula_refs_csv VARCHAR(500) NULL AFTER formula_text');

CALL sp_tmp_add_column('cfg_custom_field', 'round_to',
  'round_to INT NULL AFTER formula_refs_csv');

-- Always recomputes on every save; Prefill only fills a blank, so the user may
-- override what was suggested.
CALL sp_tmp_add_column('cfg_custom_field', 'recalc_mode',
  "recalc_mode VARCHAR(12) NOT NULL DEFAULT 'Always' AFTER round_to");

DROP PROCEDURE IF EXISTS sp_tmp_add_column;

-- ---------------------------------------------------------------------
-- 2. Bound dropdowns
-- ---------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS cfg_data_source (
  source_id           INT AUTO_INCREMENT PRIMARY KEY,
  tenant_id           INT          NOT NULL,
  source_code         VARCHAR(60)  NOT NULL,
  display_name        VARCHAR(150) NOT NULL,
  -- Lookup | NamedQuery | Api. Never a URL somebody typed.
  source_type         VARCHAR(20)  NOT NULL DEFAULT 'Lookup',
  -- Lookup: the lookup key. NamedQuery: the ext_named_query key. Api: unused.
  source_key          VARCHAR(120) NULL,
  -- Api only: a path inside this API, resolved by the browser with its token.
  relative_url        VARCHAR(300) NULL,
  http_method         VARCHAR(10)  NOT NULL DEFAULT 'GET',
  default_result_path VARCHAR(120) NULL,
  default_value_field VARCHAR(120) NULL,
  default_label_field VARCHAR(120) NULL,
  -- The source returns nothing until a parent value is supplied (a district
  -- needs its state). An empty parent yields an empty list, never every row.
  requires_parent     BIT          NOT NULL DEFAULT 0,
  is_active           BIT          NOT NULL DEFAULT 1,
  created_by          INT          NULL,
  created_on          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_by          INT          NULL,
  updated_on          DATETIME     NULL,
  UNIQUE KEY uk_cfg_data_source (tenant_id, source_code)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS cfg_custom_field_binding (
  binding_id         INT AUTO_INCREMENT PRIMARY KEY,
  tenant_id          INT          NOT NULL,
  field_id           INT          NOT NULL,
  source_id          INT          NOT NULL,
  result_path        VARCHAR(120) NULL,
  value_field        VARCHAR(120) NOT NULL,
  label_field        VARCHAR(120) NOT NULL,
  label_template     VARCHAR(200) NULL,   -- "{deptName} - {deptCode}"
  static_params_json TEXT         NULL,
  search_param_name  VARCHAR(80)  NULL,
  parent_field_key   VARCHAR(80)  NULL,
  parent_param_name  VARCHAR(80)  NULL,
  cache_seconds      INT          NOT NULL DEFAULT 300,
  is_active          BIT          NOT NULL DEFAULT 1,
  created_by         INT          NULL,
  created_on         DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_by         INT          NULL,
  updated_on         DATETIME     NULL,
  UNIQUE KEY uk_cfg_custom_field_binding (tenant_id, field_id),
  CONSTRAINT fk_cfg_binding_field  FOREIGN KEY (field_id)  REFERENCES cfg_custom_field (field_id) ON DELETE CASCADE,
  CONSTRAINT fk_cfg_binding_source FOREIGN KEY (source_id) REFERENCES cfg_data_source (source_id)
) ENGINE=InnoDB;

-- ---------------------------------------------------------------------
-- 3. Audit and archive
-- ---------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS cfg_custom_field_audit (
  audit_id     INT AUTO_INCREMENT PRIMARY KEY,
  tenant_id    INT          NOT NULL,
  screen_key   VARCHAR(100) NULL,
  field_id     INT          NULL,      -- deliberately not a foreign key: the
  field_key    VARCHAR(80)  NULL,      -- record outlives the row it describes
  action       VARCHAR(20)  NOT NULL,  -- ADD | UPDATE | DELETE | REORDER
  before_json  TEXT         NULL,
  after_json   TEXT         NULL,
  performed_by INT          NULL,
  performed_on DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  success      BIT          NOT NULL DEFAULT 1,
  error_text   TEXT         NULL,
  KEY ix_cfg_custom_field_audit (tenant_id, performed_on),
  KEY ix_cfg_custom_field_audit_screen (tenant_id, screen_key, performed_on)
) ENGINE=InnoDB;

-- Written before a field is removed, so what people typed survives the layout
-- decision that hid it.
CREATE TABLE IF NOT EXISTS cfg_custom_value_archive (
  archive_id  INT AUTO_INCREMENT PRIMARY KEY,
  tenant_id   INT          NOT NULL,
  screen_key  VARCHAR(100) NOT NULL,
  field_id    INT          NULL,
  field_key   VARCHAR(80)  NULL,
  record_id   INT          NOT NULL,
  value_text  TEXT         NULL,
  dropped_by  INT          NULL,
  dropped_on  DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  KEY ix_cfg_custom_value_archive (tenant_id, field_id),
  KEY ix_cfg_custom_value_archive_record (tenant_id, screen_key, record_id)
) ENGINE=InnoDB;

-- =====================================================================
--  PROCEDURES
-- =====================================================================

DROP PROCEDURE IF EXISTS sp_cfg_data_source_list;
DROP PROCEDURE IF EXISTS sp_cfg_data_source_get;
DROP PROCEDURE IF EXISTS sp_cfg_custom_field_binding_get;
DROP PROCEDURE IF EXISTS sp_cfg_custom_field_binding_save;
DROP PROCEDURE IF EXISTS sp_cfg_custom_field_binding_delete;
DROP PROCEDURE IF EXISTS sp_cfg_custom_field_audit_list;
DROP PROCEDURE IF EXISTS sp_cfg_custom_field_audit_add;
DROP PROCEDURE IF EXISTS sp_cfg_custom_value_archive_add;
DROP PROCEDURE IF EXISTS sp_cfg_custom_value_archive_list;
DROP PROCEDURE IF EXISTS sp_cfg_custom_field_computed_list;

DELIMITER $$

CREATE PROCEDURE sp_cfg_data_source_list (
  IN p_tenant_id INT,
  IN p_user_id   INT
)
BEGIN
  SELECT source_id, source_code, display_name, source_type, source_key,
         relative_url, http_method, default_result_path, default_value_field,
         default_label_field, requires_parent
  FROM   cfg_data_source
  WHERE  tenant_id = p_tenant_id AND is_active = 1
  ORDER  BY display_name;
END$$

CREATE PROCEDURE sp_cfg_data_source_get (
  IN p_tenant_id INT,
  IN p_user_id   INT,
  IN p_source_id INT
)
BEGIN
  SELECT source_id, source_code, display_name, source_type, source_key,
         relative_url, http_method, default_result_path, default_value_field,
         default_label_field, requires_parent
  FROM   cfg_data_source
  WHERE  tenant_id = p_tenant_id AND source_id = p_source_id AND is_active = 1;
END$$

-- The binding plus the source it points at, in one read: the runtime needs
-- both to resolve a single dropdown and neither is useful alone.
CREATE PROCEDURE sp_cfg_custom_field_binding_get (
  IN p_tenant_id INT,
  IN p_user_id   INT,
  IN p_field_id  INT
)
BEGIN
  SELECT b.binding_id, b.field_id, b.source_id, b.result_path, b.value_field,
         b.label_field, b.label_template, b.static_params_json, b.search_param_name,
         b.parent_field_key, b.parent_param_name, b.cache_seconds,
         s.source_code, s.display_name AS source_name, s.source_type, s.source_key,
         s.relative_url, s.http_method, s.requires_parent
  FROM   cfg_custom_field_binding b
  JOIN   cfg_data_source s ON s.source_id = b.source_id AND s.tenant_id = b.tenant_id
  WHERE  b.tenant_id = p_tenant_id AND b.field_id = p_field_id AND b.is_active = 1;
END$$

CREATE PROCEDURE sp_cfg_custom_field_binding_save (
  IN p_tenant_id         INT,
  IN p_user_id           INT,
  IN p_field_id          INT,
  IN p_source_id         INT,
  IN p_result_path       VARCHAR(120),
  IN p_value_field       VARCHAR(120),
  IN p_label_field       VARCHAR(120),
  IN p_label_template    VARCHAR(200),
  IN p_static_params_json TEXT,
  IN p_search_param_name VARCHAR(80),
  IN p_parent_field_key  VARCHAR(80),
  IN p_parent_param_name VARCHAR(80),
  IN p_cache_seconds     INT
)
BEGIN
  INSERT INTO cfg_custom_field_binding (
    tenant_id, field_id, source_id, result_path, value_field, label_field,
    label_template, static_params_json, search_param_name, parent_field_key,
    parent_param_name, cache_seconds, created_by, created_on)
  VALUES (
    p_tenant_id, p_field_id, p_source_id, p_result_path, p_value_field, p_label_field,
    p_label_template, p_static_params_json, p_search_param_name, p_parent_field_key,
    p_parent_param_name, IFNULL(p_cache_seconds, 300), p_user_id, UTC_TIMESTAMP())
  ON DUPLICATE KEY UPDATE
    source_id          = VALUES(source_id),
    result_path        = VALUES(result_path),
    value_field        = VALUES(value_field),
    label_field        = VALUES(label_field),
    label_template     = VALUES(label_template),
    static_params_json = VALUES(static_params_json),
    search_param_name  = VALUES(search_param_name),
    parent_field_key   = VALUES(parent_field_key),
    parent_param_name  = VALUES(parent_param_name),
    cache_seconds      = VALUES(cache_seconds),
    is_active          = 1,
    updated_by         = p_user_id,
    updated_on         = UTC_TIMESTAMP();

  SELECT ROW_COUNT() AS affected;
END$$

CREATE PROCEDURE sp_cfg_custom_field_binding_delete (
  IN p_tenant_id INT,
  IN p_user_id   INT,
  IN p_field_id  INT
)
BEGIN
  DELETE FROM cfg_custom_field_binding
  WHERE  tenant_id = p_tenant_id AND field_id = p_field_id;

  SELECT ROW_COUNT() AS affected;
END$$

CREATE PROCEDURE sp_cfg_custom_field_audit_add (
  IN p_tenant_id    INT,
  IN p_user_id      INT,
  IN p_screen_key   VARCHAR(100),
  IN p_field_id     INT,
  IN p_field_key    VARCHAR(80),
  IN p_action       VARCHAR(20),
  IN p_before_json  TEXT,
  IN p_after_json   TEXT,
  IN p_success      TINYINT,
  IN p_error_text   TEXT
)
BEGIN
  INSERT INTO cfg_custom_field_audit (
    tenant_id, screen_key, field_id, field_key, action,
    before_json, after_json, performed_by, performed_on, success, error_text)
  VALUES (
    p_tenant_id, p_screen_key, NULLIF(p_field_id, 0), p_field_key, p_action,
    p_before_json, p_after_json, p_user_id, UTC_TIMESTAMP(), IFNULL(p_success, 1), p_error_text);

  SELECT LAST_INSERT_ID() AS audit_id;
END$$

CREATE PROCEDURE sp_cfg_custom_field_audit_list (
  IN p_tenant_id  INT,
  IN p_user_id    INT,
  IN p_screen_key VARCHAR(100),
  IN p_search     VARCHAR(200),
  IN p_page_size  INT,
  IN p_offset     INT
)
BEGIN
  SELECT a.audit_id, a.screen_key, a.field_id, a.field_key, a.action,
         a.before_json, a.after_json, a.performed_on, a.success, a.error_text,
         u.display_name AS performed_by_name
  FROM   cfg_custom_field_audit a
  LEFT   JOIN sys_user u ON u.user_id = a.performed_by AND u.tenant_id = a.tenant_id
  WHERE  a.tenant_id = p_tenant_id
    AND  (p_screen_key IS NULL OR a.screen_key = p_screen_key)
    AND  (p_search IS NULL OR a.field_key LIKE CONCAT('%', p_search, '%')
                           OR a.action    LIKE CONCAT('%', p_search, '%'))
  ORDER  BY a.performed_on DESC, a.audit_id DESC
  LIMIT  p_page_size OFFSET p_offset;

  SELECT COUNT(*) AS total_count
  FROM   cfg_custom_field_audit a
  WHERE  a.tenant_id = p_tenant_id
    AND  (p_screen_key IS NULL OR a.screen_key = p_screen_key)
    AND  (p_search IS NULL OR a.field_key LIKE CONCAT('%', p_search, '%')
                           OR a.action    LIKE CONCAT('%', p_search, '%'));
END$$

-- Copies every value a field holds into the archive. Called before the field is
-- deactivated, so the copy is of what was actually there.
CREATE PROCEDURE sp_cfg_custom_value_archive_add (
  IN p_tenant_id INT,
  IN p_user_id   INT,
  IN p_field_id  INT
)
BEGIN
  INSERT INTO cfg_custom_value_archive (
    tenant_id, screen_key, field_id, field_key, record_id, value_text, dropped_by, dropped_on)
  SELECT v.tenant_id, v.screen_key, v.field_id, c.field_key, v.record_id, v.value_text,
         p_user_id, UTC_TIMESTAMP()
  FROM   cfg_custom_value v
  JOIN   cfg_custom_field c ON c.field_id = v.field_id
  WHERE  v.tenant_id  = p_tenant_id
    AND  v.field_id   = p_field_id
    AND  v.is_active  = 1
    AND  v.value_text IS NOT NULL
    AND  v.value_text <> '';

  SELECT ROW_COUNT() AS archived;
END$$

CREATE PROCEDURE sp_cfg_custom_value_archive_list (
  IN p_tenant_id INT,
  IN p_user_id   INT,
  IN p_field_id  INT,
  IN p_page_size INT,
  IN p_offset    INT
)
BEGIN
  SELECT archive_id, screen_key, field_id, field_key, record_id, value_text, dropped_on
  FROM   cfg_custom_value_archive
  WHERE  tenant_id = p_tenant_id
    AND  (p_field_id IS NULL OR field_id = p_field_id)
  ORDER  BY dropped_on DESC, archive_id DESC
  LIMIT  p_page_size OFFSET p_offset;

  SELECT COUNT(*) AS total_count
  FROM   cfg_custom_value_archive
  WHERE  tenant_id = p_tenant_id
    AND  (p_field_id IS NULL OR field_id = p_field_id);
END$$

-- Every computed field on a screen, in the order the service has to evaluate
-- them. Ordering itself is done in code: it is a graph walk, and a procedure
-- that tried it would be a topological sort written in SQL.
CREATE PROCEDURE sp_cfg_custom_field_computed_list (
  IN p_tenant_id  INT,
  IN p_user_id    INT,
  IN p_screen_key VARCHAR(100)
)
BEGIN
  SELECT field_id, field_key, label, control_type, value_mode, formula_text,
         formula_refs_csv, round_to, recalc_mode, seq_no
  FROM   cfg_custom_field
  WHERE  tenant_id  = p_tenant_id
    AND  screen_key = p_screen_key
    AND  is_active  = 1
    AND  value_mode = 'Computed'
  ORDER  BY seq_no, field_id;
END$$

DELIMITER ;

-- =====================================================================
--  SEED - the data sources an administrator may bind a dropdown to
--
--  Every one is a bounded read the product already exposes. Adding a row
--  here is how a new source becomes available; there is no UI for it, which
--  is what keeps the list an allowlist.
-- =====================================================================

INSERT INTO cfg_data_source
  (tenant_id, source_code, display_name, source_type, source_key, relative_url,
   default_value_field, default_label_field, requires_parent, created_by, created_on)
SELECT t.tenant_id, s.source_code, s.display_name, s.source_type, s.source_key, s.relative_url,
       s.default_value_field, s.default_label_field, s.requires_parent, 1, UTC_TIMESTAMP()
FROM   sys_tenant t
JOIN (
            SELECT 'DEPARTMENT'  AS source_code, 'Departments'  AS display_name, 'Lookup' AS source_type, 'department'  AS source_key, '/hr/department/lookup'  AS relative_url, 'id' AS default_value_field, 'label' AS default_label_field, 0 AS requires_parent
  UNION ALL SELECT 'DESIGNATION', 'Designations', 'Lookup', 'designation', '/hr/designation/lookup', 'id', 'label', 0
  UNION ALL SELECT 'EMPLOYEE',    'Employees',    'Lookup', 'employee',    '/hr/employee/lookup',    'id', 'label', 0
  UNION ALL SELECT 'LEAVETYPE',   'Leave types',  'Lookup', 'leaveType',   '/hr/leave/types',        'id', 'label', 0
  UNION ALL SELECT 'DOCTYPE',     'Document types', 'Api',  NULL,          '/hr/print-template/document-type', 'documentType', 'displayName', 0
) s
WHERE  t.is_active = 1
  AND  NOT EXISTS (
    SELECT 1 FROM cfg_data_source x
    WHERE  x.tenant_id = t.tenant_id AND x.source_code = s.source_code);

-- =====================================================================
--  SUPERSEDED PROCEDURES
--
--  Three procedures from 11_print_fields_documents.sql are redefined here
--  because the shape they answer with has grown: a field now carries its
--  formula and, when it reads from a data source, its binding. The apply
--  order makes this file the later word, and 11 stays correct on its own
--  for a database that has not reached this migration yet.
-- =====================================================================

DROP PROCEDURE IF EXISTS sp_cfg_custom_field_save;
DROP PROCEDURE IF EXISTS sp_cfg_custom_field_list_by_screen;
DROP PROCEDURE IF EXISTS sp_cfg_custom_field_get;

DELIMITER $$

CREATE PROCEDURE sp_cfg_custom_field_save (
  IN p_tenant_id        INT,
  IN p_user_id          INT,
  IN p_field_id         INT,
  IN p_screen_key       VARCHAR(100),
  IN p_field_key        VARCHAR(80),
  IN p_label            VARCHAR(160),
  IN p_control_type     VARCHAR(30),
  IN p_is_required      TINYINT,
  IN p_default_value    VARCHAR(255),
  IN p_range_min        VARCHAR(40),
  IN p_range_max        VARCHAR(40),
  IN p_max_length       INT,
  IN p_regex_pattern    VARCHAR(300),
  IN p_help_text        VARCHAR(300),
  IN p_placeholder      VARCHAR(160),
  IN p_section_key      VARCHAR(80),
  IN p_seq_no           INT,
  IN p_width            VARCHAR(10),
  IN p_data_source_type VARCHAR(10),
  IN p_lookup_key       VARCHAR(60),
  IN p_parent_field_key VARCHAR(80),
  IN p_show_in_form     TINYINT,
  IN p_show_in_detail   TINYINT,
  IN p_show_in_print    TINYINT,
  IN p_value_mode       VARCHAR(12),
  IN p_formula_text     VARCHAR(800),
  IN p_formula_refs_csv VARCHAR(500),
  IN p_round_to         INT,
  IN p_recalc_mode      VARCHAR(12),
  IN p_options_json     LONGTEXT
)
BEGIN
  DECLARE v_id     INT;
  DECLARE v_option JSON;
  DECLARE i        INT DEFAULT 0;
  DECLARE v_count  INT DEFAULT 0;

  IF IFNULL(p_field_id, 0) = 0 THEN
    INSERT INTO cfg_custom_field (
      tenant_id, screen_key, field_key, label, control_type, is_required,
      default_value, range_min, range_max, max_length, regex_pattern,
      help_text, placeholder, section_key, seq_no, width,
      data_source_type, lookup_key, parent_field_key,
      value_mode, formula_text, formula_refs_csv, round_to, recalc_mode,
      show_in_form, show_in_detail, show_in_print, created_by, created_on)
    VALUES (
      p_tenant_id, p_screen_key, p_field_key, p_label, p_control_type, IFNULL(p_is_required, 0),
      p_default_value, p_range_min, p_range_max, p_max_length, p_regex_pattern,
      p_help_text, p_placeholder, p_section_key, IFNULL(p_seq_no, 1000), IFNULL(p_width, 'half'),
      IFNULL(p_data_source_type, 'None'), p_lookup_key, p_parent_field_key,
      IFNULL(p_value_mode, 'Manual'), p_formula_text, p_formula_refs_csv, p_round_to,
      IFNULL(p_recalc_mode, 'Always'),
      IFNULL(p_show_in_form, 1), IFNULL(p_show_in_detail, 1), IFNULL(p_show_in_print, 1),
      p_user_id, UTC_TIMESTAMP());
    SET v_id = LAST_INSERT_ID();
  ELSE
    -- field_key stays as it is: a print template and a stored script both name a
    -- field by key, and renaming it would break them with no error anywhere.
    UPDATE cfg_custom_field
    SET    label            = p_label,
           control_type     = p_control_type,
           is_required      = IFNULL(p_is_required, 0),
           default_value    = p_default_value,
           range_min        = p_range_min,
           range_max        = p_range_max,
           max_length       = p_max_length,
           regex_pattern    = p_regex_pattern,
           help_text        = p_help_text,
           placeholder      = p_placeholder,
           section_key      = p_section_key,
           seq_no           = IFNULL(p_seq_no, seq_no),
           width            = IFNULL(p_width, 'half'),
           data_source_type = IFNULL(p_data_source_type, 'None'),
           lookup_key       = p_lookup_key,
           parent_field_key = p_parent_field_key,
           value_mode       = IFNULL(p_value_mode, 'Manual'),
           formula_text     = p_formula_text,
           formula_refs_csv = p_formula_refs_csv,
           round_to         = p_round_to,
           recalc_mode      = IFNULL(p_recalc_mode, 'Always'),
           show_in_form     = IFNULL(p_show_in_form, 1),
           show_in_detail   = IFNULL(p_show_in_detail, 1),
           show_in_print    = IFNULL(p_show_in_print, 1),
           updated_by       = p_user_id,
           updated_on       = UTC_TIMESTAMP()
    WHERE  tenant_id = p_tenant_id AND field_id = p_field_id;
    SET v_id = p_field_id;
  END IF;

  -- NULL leaves the option list alone; an empty array clears it, which is what
  -- turning a dropdown back into a text box has to do.
  IF p_options_json IS NOT NULL AND JSON_VALID(p_options_json) THEN
    DELETE FROM cfg_custom_field_option
    WHERE  tenant_id = p_tenant_id AND field_id = v_id;

    SET v_count = JSON_LENGTH(p_options_json);
    SET i = 0;

    WHILE i < v_count DO
      SET v_option = JSON_EXTRACT(p_options_json, CONCAT('$[', i, ']'));

      INSERT INTO cfg_custom_field_option (
        tenant_id, field_id, option_value, option_label, parent_value, seq_no, created_by, created_on)
      VALUES (
        p_tenant_id,
        v_id,
        JSON_UNQUOTE(JSON_EXTRACT(v_option, '$.optionValue')),
        COALESCE(NULLIF(JSON_UNQUOTE(JSON_EXTRACT(v_option, '$.optionLabel')), 'null'),
                 JSON_UNQUOTE(JSON_EXTRACT(v_option, '$.optionValue'))),
        NULLIF(JSON_UNQUOTE(JSON_EXTRACT(v_option, '$.parentValue')), 'null'),
        (i + 1) * 10,
        p_user_id, UTC_TIMESTAMP());

      SET i = i + 1;
    END WHILE;
  END IF;

  SELECT * FROM cfg_custom_field WHERE tenant_id = p_tenant_id AND field_id = v_id;
END$$

-- Three result sets now: the fields, their options, then the bindings of the
-- ones that read from a data source. A form needs all three before it can draw
-- itself, and one round trip keeps a dropdown from rendering before its source.
CREATE PROCEDURE sp_cfg_custom_field_list_by_screen (
  IN p_tenant_id  INT,
  IN p_user_id    INT,
  IN p_screen_key VARCHAR(100)
)
BEGIN
  SELECT *
  FROM   cfg_custom_field
  WHERE  tenant_id = p_tenant_id
    AND  is_active = 1
    AND  (p_screen_key IS NULL OR screen_key = p_screen_key)
  ORDER  BY screen_key, seq_no, field_id;

  SELECT o.*
  FROM   cfg_custom_field_option o
  JOIN   cfg_custom_field c ON c.field_id = o.field_id
  WHERE  o.tenant_id = p_tenant_id
    AND  o.is_active = 1
    AND  c.is_active = 1
    AND  (p_screen_key IS NULL OR c.screen_key = p_screen_key)
  ORDER  BY o.field_id, o.seq_no;

  SELECT b.binding_id, b.field_id, b.source_id, b.result_path, b.value_field,
         b.label_field, b.label_template, b.static_params_json, b.search_param_name,
         b.parent_field_key, b.parent_param_name, b.cache_seconds,
         s.source_code, s.display_name AS source_name, s.source_type, s.source_key,
         s.relative_url, s.http_method, s.requires_parent
  FROM   cfg_custom_field_binding b
  JOIN   cfg_custom_field c ON c.field_id = b.field_id
  JOIN   cfg_data_source  s ON s.source_id = b.source_id AND s.tenant_id = b.tenant_id
  WHERE  b.tenant_id = p_tenant_id
    AND  b.is_active = 1
    AND  c.is_active = 1
    AND  (p_screen_key IS NULL OR c.screen_key = p_screen_key)
  ORDER  BY b.field_id;
END$$

CREATE PROCEDURE sp_cfg_custom_field_get (
  IN p_tenant_id INT,
  IN p_user_id   INT,
  IN p_field_id  INT
)
BEGIN
  SELECT *
  FROM   cfg_custom_field
  WHERE  tenant_id = p_tenant_id AND field_id = p_field_id AND is_active = 1;

  SELECT *
  FROM   cfg_custom_field_option
  WHERE  tenant_id = p_tenant_id AND field_id = p_field_id AND is_active = 1
  ORDER  BY seq_no;

  SELECT b.binding_id, b.field_id, b.source_id, b.result_path, b.value_field,
         b.label_field, b.label_template, b.static_params_json, b.search_param_name,
         b.parent_field_key, b.parent_param_name, b.cache_seconds,
         s.source_code, s.display_name AS source_name, s.source_type, s.source_key,
         s.relative_url, s.http_method, s.requires_parent
  FROM   cfg_custom_field_binding b
  JOIN   cfg_data_source s ON s.source_id = b.source_id AND s.tenant_id = b.tenant_id
  WHERE  b.tenant_id = p_tenant_id AND b.field_id = p_field_id AND b.is_active = 1;
END$$

DELIMITER ;

