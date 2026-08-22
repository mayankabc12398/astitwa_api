-- =====================================================================
--  HrSuite - print templates, custom screen fields, employee documents
--
--  Apply after 10_alter_employee_salary.sql. Re-runnable: every table is
--  CREATE TABLE IF NOT EXISTS, every procedure is dropped before it is
--  created, and every seed is guarded by NOT EXISTS.
--
--  Conventions (section 5), unchanged here:
--    * tables      : cfg_* for tenant configuration, hr_* for HR data
--    * every table : tenant_id, created_by, created_on, updated_by, updated_on, is_active
--    * soft delete : is_active = 0
--    * all access  : stored procedures only
--
--  Why custom fields are stored as rows rather than as columns
--  ------------------------------------------------------------------
--  Every tenant shares hr_employee. Adding a real column for one tenant's
--  extra field would put that column on every other tenant's rows, and
--  RepositoryBase has no overload that would let a repository run the DDL
--  anyway. cfg_custom_field holds the definition and cfg_custom_value holds
--  one row per (record, field), so a tenant's extra fields are invisible to
--  every other tenant and nothing is ever ALTERed at run time.
--
--  JSON parameters
--  ------------------------------------------------------------------
--  A template is a tree (template -> sections -> fields) and a save has to be
--  one round trip, so the children arrive as JSON and are walked here. Two
--  idioms recur:
--    COALESCE(JSON_EXTRACT(v, '$.x') = TRUE, 1)   boolean, default true
--    NULLIF(JSON_UNQUOTE(JSON_EXTRACT(v, '$.x')), 'null')   string or NULL
--  The second exists because JSON_UNQUOTE turns a JSON null into the four
--  characters 'null' rather than into SQL NULL.
-- =====================================================================

USE astitwa;

-- ---------------------------------------------------------------------
-- CFG - print template designer
-- ---------------------------------------------------------------------

-- The catalogue of what can be templated. Seeded, not editable from the UI:
-- a document type exists because base code knows how to gather its data.
CREATE TABLE IF NOT EXISTS cfg_print_document_type (
  document_type_id INT AUTO_INCREMENT PRIMARY KEY,
  tenant_id        INT          NOT NULL,
  document_type    VARCHAR(60)  NOT NULL,   -- stable code, used by callers
  display_name     VARCHAR(150) NOT NULL,
  category         VARCHAR(60)  NOT NULL DEFAULT 'Letter',   -- Letter | Form
  layout_family    VARCHAR(30)  NOT NULL DEFAULT 'Letter',   -- Letter | Tabular | Form
  screen_key       VARCHAR(100) NULL,       -- ties custom fields to this document
  default_title    VARCHAR(200) NULL,
  supports_table   BIT          NOT NULL DEFAULT 0,
  seq_no           INT          NOT NULL DEFAULT 100,
  is_active        BIT          NOT NULL DEFAULT 1,
  created_by       INT          NULL,
  created_on       DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_by       INT          NULL,
  updated_on       DATETIME     NULL,
  UNIQUE KEY uk_cfg_print_document_type (tenant_id, document_type)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS cfg_print_template (
  template_id       INT AUTO_INCREMENT PRIMARY KEY,
  tenant_id         INT          NOT NULL,
  template_code     VARCHAR(80)  NOT NULL,
  template_name     VARCHAR(150) NOT NULL,
  document_type     VARCHAR(60)  NOT NULL,
  is_default        BIT          NOT NULL DEFAULT 0,
  -- A seeded fallback. It can be edited but never deleted, so every document
  -- type always resolves to something printable.
  is_system         BIT          NOT NULL DEFAULT 0,

  -- Ruled | Letter | Accent
  style_preset      VARCHAR(20)  NOT NULL DEFAULT 'Letter',

  -- page
  page_size         VARCHAR(20)  NOT NULL DEFAULT 'A4',        -- A4 | A5 | Letter | Legal
  orientation       VARCHAR(20)  NOT NULL DEFAULT 'portrait',  -- portrait | landscape
  margin_top        DECIMAL(5,1) NOT NULL DEFAULT 14.0,        -- mm
  margin_right      DECIMAL(5,1) NOT NULL DEFAULT 14.0,
  margin_bottom     DECIMAL(5,1) NOT NULL DEFAULT 14.0,
  margin_left       DECIMAL(5,1) NOT NULL DEFAULT 14.0,

  -- type
  font_family       VARCHAR(120) NOT NULL DEFAULT 'Segoe UI',
  font_size_pt      DECIMAL(4,1) NOT NULL DEFAULT 10.5,
  line_height       DECIMAL(4,2) NOT NULL DEFAULT 1.45,
  accent_color      VARCHAR(20)  NOT NULL DEFAULT '#4f46e5',
  text_color        VARCHAR(20)  NOT NULL DEFAULT '#1f2937',

  -- chrome
  show_logo         BIT          NOT NULL DEFAULT 1,
  logo_url          VARCHAR(400) NULL,
  logo_height_mm    DECIMAL(5,1) NOT NULL DEFAULT 14.0,
  logo_align        VARCHAR(10)  NOT NULL DEFAULT 'left',
  header_align      VARCHAR(10)  NULL,       -- NULL follows logo_align
  show_header       BIT          NOT NULL DEFAULT 1,
  header_html       TEXT         NULL,
  show_footer       BIT          NOT NULL DEFAULT 1,
  footer_html       TEXT         NULL,
  show_page_numbers BIT          NOT NULL DEFAULT 1,
  show_watermark    BIT          NOT NULL DEFAULT 0,
  watermark_text    VARCHAR(120) NULL,

  version           INT          NOT NULL DEFAULT 1,
  is_active         BIT          NOT NULL DEFAULT 1,
  created_by        INT          NULL,
  created_on        DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_by        INT          NULL,
  updated_on        DATETIME     NULL,
  UNIQUE KEY uk_cfg_print_template_code (tenant_id, template_code),
  KEY ix_cfg_print_template_scope (tenant_id, document_type, is_active, is_default)
) ENGINE=InnoDB;

-- Vertical blocks, in order. A FieldGrid or Table block with no rows in
-- cfg_print_template_field stays in "auto" mode and the renderer prints every
-- printable value it was handed, so a new template is never blank.
CREATE TABLE IF NOT EXISTS cfg_print_template_section (
  section_id       INT AUTO_INCREMENT PRIMARY KEY,
  tenant_id        INT          NOT NULL,
  template_id      INT          NOT NULL,
  section_type     VARCHAR(30)  NOT NULL,   -- Header|Title|RefDate|Addressee|Subject|Paragraphs|FieldGrid|Table|RichText|SignOff|Signature|Spacer|PageBreak|QrCode|Footer
  seq_no           INT          NOT NULL DEFAULT 10,
  title            VARCHAR(200) NULL,
  column_count     INT          NOT NULL DEFAULT 2,
  border_style     VARCHAR(20)  NOT NULL DEFAULT 'none',   -- none | box | underline | grid
  border_color     VARCHAR(20)  NULL,       -- blank inherits the template accent
  background_color VARCHAR(20)  NULL,
  padding_mm       DECIMAL(5,1) NOT NULL DEFAULT 0.0,
  is_visible       BIT          NOT NULL DEFAULT 1,
  config_json      TEXT         NULL,       -- block-specific extras
  is_active        BIT          NOT NULL DEFAULT 1,
  created_by       INT          NULL,
  created_on       DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_by       INT          NULL,
  updated_on       DATETIME     NULL,
  KEY ix_cfg_print_template_section (tenant_id, template_id, seq_no),
  CONSTRAINT fk_cfg_print_section_template
    FOREIGN KEY (template_id) REFERENCES cfg_print_template (template_id) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS cfg_print_template_field (
  template_field_id INT AUTO_INCREMENT PRIMARY KEY,
  tenant_id         INT          NOT NULL,
  section_id        INT          NOT NULL,
  field_key         VARCHAR(120) NOT NULL,  -- an employee column, a custom field key, or '@static'
  label             VARCHAR(200) NULL,      -- NULL keeps the field's own caption
  seq_no            INT          NOT NULL DEFAULT 10,
  width_percent     INT          NOT NULL DEFAULT 50,
  align             VARCHAR(10)  NOT NULL DEFAULT 'left',
  is_bold           BIT          NOT NULL DEFAULT 0,
  format            VARCHAR(20)  NOT NULL DEFAULT 'text',  -- text | date | datetime | currency | number
  show_label        BIT          NOT NULL DEFAULT 1,
  static_text       TEXT         NULL,      -- field_key = '@static'
  is_active         BIT          NOT NULL DEFAULT 1,
  created_by        INT          NULL,
  created_on        DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_by        INT          NULL,
  updated_on        DATETIME     NULL,
  KEY ix_cfg_print_template_field (tenant_id, section_id, seq_no),
  CONSTRAINT fk_cfg_print_field_section
    FOREIGN KEY (section_id) REFERENCES cfg_print_template_section (section_id) ON DELETE CASCADE
) ENGINE=InnoDB;

-- ---------------------------------------------------------------------
-- CFG - custom screen fields
--
-- cfg_field_rule already lets a tenant hide, rename and reorder a field the
-- product compiled in. These two tables are the other half: a field the
-- product never had at all. The two never overlap - a rule row keys on a
-- compiled field_key, a custom field declares its own.
-- ---------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS cfg_custom_field (
  field_id         INT AUTO_INCREMENT PRIMARY KEY,
  tenant_id        INT          NOT NULL,
  screen_key       VARCHAR(100) NOT NULL,   -- 'hr.employee', matches ScreenCatalog
  field_key        VARCHAR(80)  NOT NULL,   -- payload key, slugged from the label
  label            VARCHAR(160) NOT NULL,
  control_type     VARCHAR(30)  NOT NULL DEFAULT 'text',   -- text|textarea|number|decimal|date|datetime|checkbox|dropdown|radio|lookup
  is_required      BIT          NOT NULL DEFAULT 0,
  default_value    VARCHAR(255) NULL,
  -- range_min / range_max rather than min_value / max_value: MAXVALUE is a
  -- MySQL reserved word and the pair reads better matched.
  range_min        VARCHAR(40)  NULL,
  range_max        VARCHAR(40)  NULL,
  max_length       INT          NULL,
  regex_pattern    VARCHAR(300) NULL,
  help_text        VARCHAR(300) NULL,
  placeholder      VARCHAR(160) NULL,
  section_key      VARCHAR(80)  NULL,
  seq_no           INT          NOT NULL DEFAULT 1000,     -- after the compiled fields
  width            VARCHAR(10)  NOT NULL DEFAULT 'half',   -- half | full
  -- None   : free input
  -- Static : options in cfg_custom_field_option
  -- Lookup : one of the built-in lookup endpoints, named in lookup_key
  data_source_type VARCHAR(10)  NOT NULL DEFAULT 'None',
  lookup_key       VARCHAR(60)  NULL,       -- department | designation | employee | leaveType
  parent_field_key VARCHAR(80)  NULL,       -- cascading static list
  show_in_form     BIT          NOT NULL DEFAULT 1,
  show_in_detail   BIT          NOT NULL DEFAULT 1,
  show_in_print    BIT          NOT NULL DEFAULT 1,
  is_active        BIT          NOT NULL DEFAULT 1,
  created_by       INT          NULL,
  created_on       DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_by       INT          NULL,
  updated_on       DATETIME     NULL,
  UNIQUE KEY uk_cfg_custom_field (tenant_id, screen_key, field_key),
  KEY ix_cfg_custom_field_screen (tenant_id, screen_key, is_active, seq_no)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS cfg_custom_field_option (
  option_id    INT AUTO_INCREMENT PRIMARY KEY,
  tenant_id    INT          NOT NULL,
  field_id     INT          NOT NULL,
  option_value VARCHAR(200) NOT NULL,
  option_label VARCHAR(200) NOT NULL,
  -- Cascading list: shown only when the parent field holds this value.
  -- NULL means "shown whatever the parent is".
  parent_value VARCHAR(200) NULL,
  seq_no       INT          NOT NULL DEFAULT 10,
  is_active    BIT          NOT NULL DEFAULT 1,
  created_by   INT          NULL,
  created_on   DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_by   INT          NULL,
  updated_on   DATETIME     NULL,
  KEY ix_cfg_custom_field_option (tenant_id, field_id, seq_no),
  CONSTRAINT fk_cfg_custom_option_field
    FOREIGN KEY (field_id) REFERENCES cfg_custom_field (field_id) ON DELETE CASCADE
) ENGINE=InnoDB;

-- One row per (record, field). value_text holds every control type; the
-- service coerces on the way in and on the way out, so a date and a number
-- are validated rather than merely stored.
CREATE TABLE IF NOT EXISTS cfg_custom_value (
  value_id   INT AUTO_INCREMENT PRIMARY KEY,
  tenant_id  INT          NOT NULL,
  screen_key VARCHAR(100) NOT NULL,
  record_id  INT          NOT NULL,
  field_id   INT          NOT NULL,
  value_text TEXT         NULL,
  is_active  BIT          NOT NULL DEFAULT 1,
  created_by INT          NULL,
  created_on DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_by INT          NULL,
  updated_on DATETIME     NULL,
  UNIQUE KEY uk_cfg_custom_value (tenant_id, screen_key, record_id, field_id),
  KEY ix_cfg_custom_value_record (tenant_id, screen_key, record_id),
  CONSTRAINT fk_cfg_custom_value_field
    FOREIGN KEY (field_id) REFERENCES cfg_custom_field (field_id) ON DELETE CASCADE
) ENGINE=InnoDB;

-- ---------------------------------------------------------------------
-- HR - issued documents
-- ---------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS hr_document (
  document_id     INT AUTO_INCREMENT PRIMARY KEY,
  tenant_id       INT          NOT NULL,
  ref_no          VARCHAR(80)  NOT NULL,
  employee_id     INT          NOT NULL,
  document_type   VARCHAR(60)  NOT NULL,   -- -> cfg_print_document_type.document_type
  template_id     INT          NULL,       -- NULL means "resolve the default at print time"
  subject         VARCHAR(300) NULL,
  body_text       TEXT         NULL,       -- the letter's paragraphs, one per line
  effective_date  DATE         NULL,
  valid_till      DATE         NULL,
  signed_by       VARCHAR(150) NULL,
  status          VARCHAR(30)  NOT NULL DEFAULT 'Draft',  -- Draft|Pending Signature|Issued|Acknowledged|Expired|Revoked
  issued_on       DATETIME     NULL,
  acknowledged_on DATETIME     NULL,
  delivered_via   VARCHAR(100) NULL,
  -- What was actually printed, captured at issue. A template edited afterwards
  -- must not change what a document already said.
  payload_json    LONGTEXT     NULL,
  is_active       BIT          NOT NULL DEFAULT 1,
  created_by      INT          NULL,
  created_on      DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_by      INT          NULL,
  updated_on      DATETIME     NULL,
  UNIQUE KEY uk_hr_document_ref (tenant_id, ref_no),
  KEY ix_hr_document_employee (tenant_id, employee_id, status),
  KEY ix_hr_document_type (tenant_id, document_type, status)
) ENGINE=InnoDB;

-- =====================================================================
--  PROCEDURES
-- =====================================================================

DROP PROCEDURE IF EXISTS sp_cfg_print_document_type_list;
DROP PROCEDURE IF EXISTS sp_cfg_print_template_list;
DROP PROCEDURE IF EXISTS sp_cfg_print_template_get;
DROP PROCEDURE IF EXISTS sp_cfg_print_template_save;
DROP PROCEDURE IF EXISTS sp_cfg_print_template_delete;
DROP PROCEDURE IF EXISTS sp_cfg_print_template_set_default;
DROP PROCEDURE IF EXISTS sp_cfg_print_template_clone;
DROP PROCEDURE IF EXISTS sp_cfg_print_template_resolve;
DROP PROCEDURE IF EXISTS sp_cfg_print_template_lookup;
DROP PROCEDURE IF EXISTS sp_cfg_print_available_field_list;
DROP PROCEDURE IF EXISTS sp_cfg_custom_field_list_by_screen;
DROP PROCEDURE IF EXISTS sp_cfg_custom_field_get;
DROP PROCEDURE IF EXISTS sp_cfg_custom_field_save;
DROP PROCEDURE IF EXISTS sp_cfg_custom_field_delete;
DROP PROCEDURE IF EXISTS sp_cfg_custom_field_reorder;
DROP PROCEDURE IF EXISTS sp_cfg_custom_field_usage;
DROP PROCEDURE IF EXISTS sp_cfg_custom_value_list;
DROP PROCEDURE IF EXISTS sp_cfg_custom_value_save;
DROP PROCEDURE IF EXISTS sp_hr_document_list;
DROP PROCEDURE IF EXISTS sp_hr_document_get;
DROP PROCEDURE IF EXISTS sp_hr_document_save;
DROP PROCEDURE IF EXISTS sp_hr_document_delete;
DROP PROCEDURE IF EXISTS sp_hr_document_status_set;
DROP PROCEDURE IF EXISTS sp_hr_document_print_context;

DELIMITER $$

-- =====================================================================
-- PRINT TEMPLATE
-- =====================================================================

CREATE PROCEDURE sp_cfg_print_document_type_list (
  IN p_tenant_id INT,
  IN p_user_id   INT
)
BEGIN
  SELECT d.document_type_id,
         d.document_type,
         d.display_name,
         d.category,
         d.layout_family,
         d.screen_key,
         d.default_title,
         d.supports_table,
         d.seq_no,
         (SELECT COUNT(*)
          FROM   cfg_print_template t
          WHERE  t.tenant_id     = p_tenant_id
            AND  t.document_type = d.document_type
            AND  t.is_active     = 1) AS template_count
  FROM   cfg_print_document_type d
  WHERE  d.tenant_id = p_tenant_id
    AND  d.is_active = 1
  ORDER  BY d.seq_no, d.display_name;
END$$

CREATE PROCEDURE sp_cfg_print_template_list (
  IN p_tenant_id     INT,
  IN p_user_id       INT,
  IN p_document_type VARCHAR(60),
  IN p_search        VARCHAR(200),
  IN p_page_size     INT,
  IN p_offset        INT
)
BEGIN
  SELECT t.template_id,
         t.template_code,
         t.template_name,
         t.document_type,
         t.is_default,
         t.is_system,
         t.page_size,
         t.orientation,
         t.style_preset,
         t.version,
         t.updated_on,
         (SELECT COUNT(*)
          FROM   cfg_print_template_section s
          WHERE  s.template_id = t.template_id AND s.is_active = 1) AS section_count
  FROM   cfg_print_template t
  WHERE  t.tenant_id = p_tenant_id
    AND  t.is_active = 1
    AND  (p_document_type IS NULL OR t.document_type = p_document_type)
    AND  (p_search IS NULL OR t.template_name LIKE CONCAT('%', p_search, '%')
                           OR t.template_code LIKE CONCAT('%', p_search, '%'))
  ORDER  BY t.document_type, t.is_default DESC, t.template_name
  LIMIT  p_page_size OFFSET p_offset;

  SELECT COUNT(*) AS total_count
  FROM   cfg_print_template t
  WHERE  t.tenant_id = p_tenant_id
    AND  t.is_active = 1
    AND  (p_document_type IS NULL OR t.document_type = p_document_type)
    AND  (p_search IS NULL OR t.template_name LIKE CONCAT('%', p_search, '%')
                           OR t.template_code LIKE CONCAT('%', p_search, '%'));
END$$

-- Three result sets: the template, its sections, then every field of those
-- sections. One round trip, and the client rebuilds the tree by section_id.
CREATE PROCEDURE sp_cfg_print_template_get (
  IN p_tenant_id   INT,
  IN p_user_id     INT,
  IN p_template_id INT
)
BEGIN
  SELECT t.*,
         d.default_title AS document_title
  FROM   cfg_print_template t
  LEFT   JOIN cfg_print_document_type d
         ON  d.tenant_id     = t.tenant_id
         AND d.document_type = t.document_type
  WHERE  t.tenant_id   = p_tenant_id
    AND  t.template_id = p_template_id
    AND  t.is_active   = 1;

  SELECT s.*
  FROM   cfg_print_template_section s
  WHERE  s.tenant_id   = p_tenant_id
    AND  s.template_id = p_template_id
    AND  s.is_active   = 1
  ORDER  BY s.seq_no;

  SELECT f.*
  FROM   cfg_print_template_field f
  JOIN   cfg_print_template_section s ON s.section_id = f.section_id
  WHERE  f.tenant_id   = p_tenant_id
    AND  s.template_id = p_template_id
    AND  f.is_active   = 1
    AND  s.is_active   = 1
  ORDER  BY s.seq_no, f.seq_no;
END$$

-- The template the runtime should print with: the tenant's default for this
-- document type, or its only active one. Returns nothing when the tenant has
-- configured none, and the client then falls back to its built-in layout, so
-- installing this feature changes no existing output.
CREATE PROCEDURE sp_cfg_print_template_resolve (
  IN p_tenant_id     INT,
  IN p_user_id       INT,
  IN p_document_type VARCHAR(60)
)
BEGIN
  DECLARE v_template_id INT DEFAULT NULL;

  SELECT t.template_id INTO v_template_id
  FROM   cfg_print_template t
  WHERE  t.tenant_id     = p_tenant_id
    AND  t.document_type = p_document_type
    AND  t.is_active     = 1
  ORDER  BY t.is_default DESC, t.is_system ASC, t.template_id
  LIMIT  1;

  SELECT t.*,
         d.default_title AS document_title
  FROM   cfg_print_template t
  LEFT   JOIN cfg_print_document_type d
         ON  d.tenant_id     = t.tenant_id
         AND d.document_type = t.document_type
  WHERE  t.tenant_id   = p_tenant_id
    AND  t.template_id = v_template_id;

  SELECT s.*
  FROM   cfg_print_template_section s
  WHERE  s.tenant_id   = p_tenant_id
    AND  s.template_id = v_template_id
    AND  s.is_active   = 1
  ORDER  BY s.seq_no;

  SELECT f.*
  FROM   cfg_print_template_field f
  JOIN   cfg_print_template_section s ON s.section_id = f.section_id
  WHERE  f.tenant_id   = p_tenant_id
    AND  s.template_id = v_template_id
    AND  f.is_active   = 1
    AND  s.is_active   = 1
  ORDER  BY s.seq_no, f.seq_no;
END$$

CREATE PROCEDURE sp_cfg_print_template_lookup (
  IN p_tenant_id     INT,
  IN p_user_id       INT,
  IN p_document_type VARCHAR(60)
)
BEGIN
  SELECT template_id AS id,
         CONCAT(template_name, IF(is_default = 1, ' (default)', '')) AS label
  FROM   cfg_print_template
  WHERE  tenant_id = p_tenant_id
    AND  is_active = 1
    AND  (p_document_type IS NULL OR document_type = p_document_type)
  ORDER  BY is_default DESC, template_name
  LIMIT  500;
END$$

-- Saves the whole tree. Sections and their fields are replaced rather than
-- merged: the designer always sends the complete block list, and a diff would
-- have to guess at identity for a block the user dragged.
CREATE PROCEDURE sp_cfg_print_template_save (
  IN p_tenant_id         INT,
  IN p_user_id           INT,
  IN p_template_id       INT,
  IN p_template_code     VARCHAR(80),
  IN p_template_name     VARCHAR(150),
  IN p_document_type     VARCHAR(60),
  IN p_is_default        TINYINT,
  IN p_style_preset      VARCHAR(20),
  IN p_page_size         VARCHAR(20),
  IN p_orientation       VARCHAR(20),
  IN p_margin_top        DECIMAL(5,1),
  IN p_margin_right      DECIMAL(5,1),
  IN p_margin_bottom     DECIMAL(5,1),
  IN p_margin_left       DECIMAL(5,1),
  IN p_font_family       VARCHAR(120),
  IN p_font_size_pt      DECIMAL(4,1),
  IN p_line_height       DECIMAL(4,2),
  IN p_accent_color      VARCHAR(20),
  IN p_text_color        VARCHAR(20),
  IN p_show_logo         TINYINT,
  IN p_logo_url          VARCHAR(400),
  IN p_logo_height_mm    DECIMAL(5,1),
  IN p_logo_align        VARCHAR(10),
  IN p_header_align      VARCHAR(10),
  IN p_show_header       TINYINT,
  IN p_header_html       TEXT,
  IN p_show_footer       TINYINT,
  IN p_footer_html       TEXT,
  IN p_show_page_numbers TINYINT,
  IN p_show_watermark    TINYINT,
  IN p_watermark_text    VARCHAR(120),
  IN p_sections_json     LONGTEXT
)
BEGIN
  DECLARE v_id         INT;
  DECLARE v_section_id INT;
  DECLARE v_section    JSON;
  DECLARE v_field      JSON;
  DECLARE v_fields     JSON;
  DECLARE i            INT DEFAULT 0;
  DECLARE j            INT DEFAULT 0;
  DECLARE v_sections   INT DEFAULT 0;
  DECLARE v_field_ct   INT DEFAULT 0;

  IF IFNULL(p_template_id, 0) = 0 THEN
    INSERT INTO cfg_print_template (
      tenant_id, template_code, template_name, document_type, is_default, is_system,
      style_preset, page_size, orientation, margin_top, margin_right, margin_bottom, margin_left,
      font_family, font_size_pt, line_height, accent_color, text_color,
      show_logo, logo_url, logo_height_mm, logo_align, header_align,
      show_header, header_html, show_footer, footer_html,
      show_page_numbers, show_watermark, watermark_text,
      version, created_by, created_on)
    VALUES (
      p_tenant_id, p_template_code, p_template_name, p_document_type, IFNULL(p_is_default, 0), 0,
      p_style_preset, p_page_size, p_orientation, p_margin_top, p_margin_right, p_margin_bottom, p_margin_left,
      p_font_family, p_font_size_pt, p_line_height, p_accent_color, p_text_color,
      IFNULL(p_show_logo, 1), p_logo_url, p_logo_height_mm, p_logo_align, p_header_align,
      IFNULL(p_show_header, 1), p_header_html, IFNULL(p_show_footer, 1), p_footer_html,
      IFNULL(p_show_page_numbers, 1), IFNULL(p_show_watermark, 0), p_watermark_text,
      1, p_user_id, UTC_TIMESTAMP());
    SET v_id = LAST_INSERT_ID();
  ELSE
    UPDATE cfg_print_template
    SET    template_name     = p_template_name,
           document_type     = p_document_type,
           is_default        = IFNULL(p_is_default, is_default),
           style_preset      = p_style_preset,
           page_size         = p_page_size,
           orientation       = p_orientation,
           margin_top        = p_margin_top,
           margin_right      = p_margin_right,
           margin_bottom     = p_margin_bottom,
           margin_left       = p_margin_left,
           font_family       = p_font_family,
           font_size_pt      = p_font_size_pt,
           line_height       = p_line_height,
           accent_color      = p_accent_color,
           text_color        = p_text_color,
           show_logo         = IFNULL(p_show_logo, 1),
           logo_url          = p_logo_url,
           logo_height_mm    = p_logo_height_mm,
           logo_align        = p_logo_align,
           header_align      = p_header_align,
           show_header       = IFNULL(p_show_header, 1),
           header_html       = p_header_html,
           show_footer       = IFNULL(p_show_footer, 1),
           footer_html       = p_footer_html,
           show_page_numbers = IFNULL(p_show_page_numbers, 1),
           show_watermark    = IFNULL(p_show_watermark, 0),
           watermark_text    = p_watermark_text,
           version           = version + 1,
           updated_by        = p_user_id,
           updated_on        = UTC_TIMESTAMP()
    WHERE  tenant_id   = p_tenant_id
      AND  template_id = p_template_id;
    SET v_id = p_template_id;
  END IF;

  -- Only one default per document type, and the flip is part of the same save
  -- so two templates can never both claim it.
  IF IFNULL(p_is_default, 0) = 1 THEN
    UPDATE cfg_print_template
    SET    is_default = 0, updated_by = p_user_id, updated_on = UTC_TIMESTAMP()
    WHERE  tenant_id     = p_tenant_id
      AND  document_type = p_document_type
      AND  template_id  <> v_id;
  END IF;

  IF p_sections_json IS NOT NULL AND JSON_VALID(p_sections_json) THEN
    -- Fields go with their sections through the FK cascade.
    DELETE FROM cfg_print_template_section
    WHERE  tenant_id = p_tenant_id AND template_id = v_id;

    SET v_sections = JSON_LENGTH(p_sections_json);
    SET i = 0;

    WHILE i < v_sections DO
      SET v_section = JSON_EXTRACT(p_sections_json, CONCAT('$[', i, ']'));

      INSERT INTO cfg_print_template_section (
        tenant_id, template_id, section_type, seq_no, title, column_count,
        border_style, border_color, background_color, padding_mm, is_visible, config_json,
        created_by, created_on)
      VALUES (
        p_tenant_id,
        v_id,
        JSON_UNQUOTE(JSON_EXTRACT(v_section, '$.sectionType')),
        (i + 1) * 10,
        NULLIF(JSON_UNQUOTE(JSON_EXTRACT(v_section, '$.title')), 'null'),
        CAST(COALESCE(JSON_EXTRACT(v_section, '$.columnCount'), 2) AS UNSIGNED),
        COALESCE(NULLIF(JSON_UNQUOTE(JSON_EXTRACT(v_section, '$.borderStyle')), 'null'), 'none'),
        NULLIF(JSON_UNQUOTE(JSON_EXTRACT(v_section, '$.borderColor')), 'null'),
        NULLIF(JSON_UNQUOTE(JSON_EXTRACT(v_section, '$.backgroundColor')), 'null'),
        CAST(COALESCE(JSON_EXTRACT(v_section, '$.paddingMm'), 0) AS DECIMAL(5,1)),
        COALESCE(JSON_EXTRACT(v_section, '$.isVisible') = TRUE, 1),
        NULLIF(JSON_UNQUOTE(JSON_EXTRACT(v_section, '$.configJson')), 'null'),
        p_user_id, UTC_TIMESTAMP());

      SET v_section_id = LAST_INSERT_ID();
      SET v_fields = JSON_EXTRACT(v_section, '$.fields');
      SET v_field_ct = IFNULL(JSON_LENGTH(v_fields), 0);
      SET j = 0;

      WHILE j < v_field_ct DO
        SET v_field = JSON_EXTRACT(v_fields, CONCAT('$[', j, ']'));

        INSERT INTO cfg_print_template_field (
          tenant_id, section_id, field_key, label, seq_no, width_percent,
          align, is_bold, format, show_label, static_text, created_by, created_on)
        VALUES (
          p_tenant_id,
          v_section_id,
          JSON_UNQUOTE(JSON_EXTRACT(v_field, '$.fieldKey')),
          NULLIF(JSON_UNQUOTE(JSON_EXTRACT(v_field, '$.label')), 'null'),
          (j + 1) * 10,
          CAST(COALESCE(JSON_EXTRACT(v_field, '$.widthPercent'), 50) AS UNSIGNED),
          COALESCE(NULLIF(JSON_UNQUOTE(JSON_EXTRACT(v_field, '$.align')), 'null'), 'left'),
          COALESCE(JSON_EXTRACT(v_field, '$.isBold') = TRUE, 0),
          COALESCE(NULLIF(JSON_UNQUOTE(JSON_EXTRACT(v_field, '$.format')), 'null'), 'text'),
          COALESCE(JSON_EXTRACT(v_field, '$.showLabel') = TRUE, 1),
          NULLIF(JSON_UNQUOTE(JSON_EXTRACT(v_field, '$.staticText')), 'null'),
          p_user_id, UTC_TIMESTAMP());

        SET j = j + 1;
      END WHILE;

      SET i = i + 1;
    END WHILE;
  END IF;

  SELECT template_id, template_code, template_name, document_type,
         is_default, is_system, version
  FROM   cfg_print_template
  WHERE  tenant_id = p_tenant_id AND template_id = v_id;
END$$

-- Soft delete. A seeded system template is refused rather than hidden, so a
-- document type can never end up with nothing to print with.
CREATE PROCEDURE sp_cfg_print_template_delete (
  IN p_tenant_id   INT,
  IN p_user_id     INT,
  IN p_template_id INT
)
BEGIN
  UPDATE cfg_print_template
  SET    is_active  = 0,
         is_default = 0,
         updated_by = p_user_id,
         updated_on = UTC_TIMESTAMP()
  WHERE  tenant_id   = p_tenant_id
    AND  template_id = p_template_id
    AND  is_system   = 0;

  SELECT ROW_COUNT() AS affected;
END$$

CREATE PROCEDURE sp_cfg_print_template_set_default (
  IN p_tenant_id   INT,
  IN p_user_id     INT,
  IN p_template_id INT
)
BEGIN
  DECLARE v_document_type VARCHAR(60);

  SELECT document_type INTO v_document_type
  FROM   cfg_print_template
  WHERE  tenant_id = p_tenant_id AND template_id = p_template_id AND is_active = 1;

  IF v_document_type IS NOT NULL THEN
    UPDATE cfg_print_template
    SET    is_default = IF(template_id = p_template_id, 1, 0),
           updated_by = p_user_id,
           updated_on = UTC_TIMESTAMP()
    WHERE  tenant_id     = p_tenant_id
      AND  document_type = v_document_type;
  END IF;

  SELECT template_id, template_name, document_type, is_default
  FROM   cfg_print_template
  WHERE  tenant_id = p_tenant_id AND template_id = p_template_id;
END$$

CREATE PROCEDURE sp_cfg_print_template_clone (
  IN p_tenant_id     INT,
  IN p_user_id       INT,
  IN p_template_id   INT,
  IN p_template_name VARCHAR(150)
)
BEGIN
  DECLARE v_new_id INT;

  INSERT INTO cfg_print_template (
    tenant_id, template_code, template_name, document_type, is_default, is_system,
    style_preset, page_size, orientation, margin_top, margin_right, margin_bottom, margin_left,
    font_family, font_size_pt, line_height, accent_color, text_color,
    show_logo, logo_url, logo_height_mm, logo_align, header_align,
    show_header, header_html, show_footer, footer_html,
    show_page_numbers, show_watermark, watermark_text,
    version, created_by, created_on)
  SELECT tenant_id,
         CONCAT(LEFT(template_code, 60), '-C', UNIX_TIMESTAMP()),
         p_template_name,
         document_type, 0, 0,
         style_preset, page_size, orientation, margin_top, margin_right, margin_bottom, margin_left,
         font_family, font_size_pt, line_height, accent_color, text_color,
         show_logo, logo_url, logo_height_mm, logo_align, header_align,
         show_header, header_html, show_footer, footer_html,
         show_page_numbers, show_watermark, watermark_text,
         1, p_user_id, UTC_TIMESTAMP()
  FROM   cfg_print_template
  WHERE  tenant_id = p_tenant_id AND template_id = p_template_id AND is_active = 1;

  SET v_new_id = LAST_INSERT_ID();

  -- Sections first, then fields matched back through the section they came
  -- from. A temporary map is the only way to carry the old-to-new pairing
  -- across two set-based inserts.
  DROP TEMPORARY TABLE IF EXISTS tmp_clone_section;
  CREATE TEMPORARY TABLE tmp_clone_section (
    old_section_id INT NOT NULL,
    new_section_id INT NOT NULL
  ) ENGINE=MEMORY;

  BEGIN
    DECLARE v_done       INT DEFAULT 0;
    DECLARE v_old_id     INT;
    DECLARE cur CURSOR FOR
      SELECT section_id
      FROM   cfg_print_template_section
      WHERE  tenant_id = p_tenant_id AND template_id = p_template_id AND is_active = 1
      ORDER  BY seq_no;
    DECLARE CONTINUE HANDLER FOR NOT FOUND SET v_done = 1;

    OPEN cur;
    clone_loop: LOOP
      FETCH cur INTO v_old_id;
      IF v_done = 1 THEN
        LEAVE clone_loop;
      END IF;

      INSERT INTO cfg_print_template_section (
        tenant_id, template_id, section_type, seq_no, title, column_count,
        border_style, border_color, background_color, padding_mm, is_visible, config_json,
        created_by, created_on)
      SELECT tenant_id, v_new_id, section_type, seq_no, title, column_count,
             border_style, border_color, background_color, padding_mm, is_visible, config_json,
             p_user_id, UTC_TIMESTAMP()
      FROM   cfg_print_template_section
      WHERE  section_id = v_old_id;

      INSERT INTO tmp_clone_section (old_section_id, new_section_id)
      VALUES (v_old_id, LAST_INSERT_ID());
    END LOOP;
    CLOSE cur;
  END;

  INSERT INTO cfg_print_template_field (
    tenant_id, section_id, field_key, label, seq_no, width_percent,
    align, is_bold, format, show_label, static_text, created_by, created_on)
  SELECT f.tenant_id, m.new_section_id, f.field_key, f.label, f.seq_no, f.width_percent,
         f.align, f.is_bold, f.format, f.show_label, f.static_text,
         p_user_id, UTC_TIMESTAMP()
  FROM   cfg_print_template_field f
  JOIN   tmp_clone_section m ON m.old_section_id = f.section_id
  WHERE  f.tenant_id = p_tenant_id AND f.is_active = 1
  ORDER  BY f.section_id, f.seq_no;

  DROP TEMPORARY TABLE IF EXISTS tmp_clone_section;

  SELECT template_id, template_code, template_name, document_type, is_default, is_system, version
  FROM   cfg_print_template
  WHERE  tenant_id = p_tenant_id AND template_id = v_new_id;
END$$

-- What the designer may drop into a block for a given document type: the
-- employee columns base code always has, the tenant's own printable custom
-- fields, and the keys the print context adds at run time.
CREATE PROCEDURE sp_cfg_print_available_field_list (
  IN p_tenant_id     INT,
  IN p_user_id       INT,
  IN p_document_type VARCHAR(60)
)
BEGIN
  DECLARE v_screen_key VARCHAR(100);

  SELECT screen_key INTO v_screen_key
  FROM   cfg_print_document_type
  WHERE  tenant_id = p_tenant_id AND document_type = p_document_type AND is_active = 1;

  SELECT 'employeeCode'     AS field_key, 'Employee code'     AS label, 'Base' AS origin, 'text' AS control_type, 'text'     AS suggested_format,  10 AS seq_no
  UNION ALL SELECT 'fullName',            'Name',                       'Base', 'text',     'text',      20
  UNION ALL SELECT 'departmentName',      'Department',                 'Base', 'text',     'text',      30
  UNION ALL SELECT 'designationName',     'Designation',                'Base', 'text',     'text',      40
  UNION ALL SELECT 'dob',                 'Date of birth',              'Base', 'date',     'date',      50
  UNION ALL SELECT 'dateOfJoining',       'Date of joining',            'Base', 'date',     'date',      60
  UNION ALL SELECT 'mobile',              'Mobile',                     'Base', 'text',     'text',      70
  UNION ALL SELECT 'email',               'Email',                      'Base', 'text',     'text',      80
  UNION ALL SELECT 'employmentStatus',    'Employment status',          'Base', 'text',     'text',      90
  UNION ALL SELECT 'grossCtc',            'Gross CTC',                  'Base', 'decimal',  'currency', 100
  UNION ALL SELECT 'hra',                 'HRA',                        'Base', 'decimal',  'currency', 110
  UNION ALL SELECT 'tds',                 'TDS',                        'Base', 'decimal',  'currency', 120
  UNION ALL SELECT 'netSalary',           'Net salary',                 'Base', 'decimal',  'currency', 130
  UNION ALL SELECT 'refNo',               'Reference number',           'Context', 'text',  'text',     200
  UNION ALL SELECT 'documentType',        'Document type',              'Context', 'text',  'text',     210
  UNION ALL SELECT 'subject',             'Subject',                    'Context', 'text',  'text',     220
  UNION ALL SELECT 'effectiveDate',       'Effective date',             'Context', 'date',  'date',     230
  UNION ALL SELECT 'validTill',           'Valid till',                 'Context', 'date',  'date',     240
  UNION ALL SELECT 'signedBy',            'Signatory',                  'Context', 'text',  'text',     250
  UNION ALL SELECT 'issuedOn',            'Issued on',                  'Context', 'date',  'date',     260
  UNION ALL SELECT 'tenantName',          'Organisation',               'Context', 'text',  'text',     270
  UNION ALL SELECT 'printedOn',           'Printed on',                 'Context', 'date',  'datetime', 280
  UNION ALL
  SELECT c.field_key,
         c.label,
         'Custom',
         c.control_type,
         CASE c.control_type
           WHEN 'date'     THEN 'date'
           WHEN 'datetime' THEN 'datetime'
           WHEN 'decimal'  THEN 'number'
           WHEN 'number'   THEN 'number'
           ELSE 'text'
         END,
         300 + c.seq_no
  FROM   cfg_custom_field c
  WHERE  c.tenant_id     = p_tenant_id
    AND  c.is_active     = 1
    AND  c.show_in_print = 1
    AND  (v_screen_key IS NULL OR c.screen_key IN (v_screen_key, 'hr.employee'))
  ORDER  BY seq_no;
END$$

-- =====================================================================
-- CUSTOM FIELDS
-- =====================================================================

-- Two result sets: the fields, then every option of those fields. The client
-- and the API both need the pair together, and one round trip keeps a field
-- from rendering before its choices exist.
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
END$$

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
  IN p_options_json     LONGTEXT
)
BEGIN
  DECLARE v_id       INT;
  DECLARE v_option   JSON;
  DECLARE i          INT DEFAULT 0;
  DECLARE v_count    INT DEFAULT 0;

  IF IFNULL(p_field_id, 0) = 0 THEN
    INSERT INTO cfg_custom_field (
      tenant_id, screen_key, field_key, label, control_type, is_required,
      default_value, range_min, range_max, max_length, regex_pattern,
      help_text, placeholder, section_key, seq_no, width,
      data_source_type, lookup_key, parent_field_key,
      show_in_form, show_in_detail, show_in_print, created_by, created_on)
    VALUES (
      p_tenant_id, p_screen_key, p_field_key, p_label, p_control_type, IFNULL(p_is_required, 0),
      p_default_value, p_range_min, p_range_max, p_max_length, p_regex_pattern,
      p_help_text, p_placeholder, p_section_key, IFNULL(p_seq_no, 1000), IFNULL(p_width, 'half'),
      IFNULL(p_data_source_type, 'None'), p_lookup_key, p_parent_field_key,
      IFNULL(p_show_in_form, 1), IFNULL(p_show_in_detail, 1), IFNULL(p_show_in_print, 1),
      p_user_id, UTC_TIMESTAMP());
    SET v_id = LAST_INSERT_ID();
  ELSE
    -- field_key is deliberately not updatable: values in cfg_custom_value are
    -- keyed by field_id, but a template and a stored script both reference the
    -- key by name, and renaming it would silently break them.
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
           show_in_form     = IFNULL(p_show_in_form, 1),
           show_in_detail   = IFNULL(p_show_in_detail, 1),
           show_in_print    = IFNULL(p_show_in_print, 1),
           updated_by       = p_user_id,
           updated_on       = UTC_TIMESTAMP()
    WHERE  tenant_id = p_tenant_id AND field_id = p_field_id;
    SET v_id = p_field_id;
  END IF;

  -- A NULL options payload means "leave the list alone"; an empty array means
  -- "this field has no options any more", which is what switching a dropdown
  -- back to a plain text control has to do.
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

  SELECT *
  FROM   cfg_custom_field
  WHERE  tenant_id = p_tenant_id AND field_id = v_id;
END$$

-- Soft delete, and the values stay. Deleting a field is a layout decision;
-- throwing away what people typed into it is not, and a field re-added with
-- the same key would otherwise come back empty.
CREATE PROCEDURE sp_cfg_custom_field_delete (
  IN p_tenant_id INT,
  IN p_user_id   INT,
  IN p_field_id  INT
)
BEGIN
  UPDATE cfg_custom_field
  SET    is_active  = 0,
         updated_by = p_user_id,
         updated_on = UTC_TIMESTAMP()
  WHERE  tenant_id = p_tenant_id AND field_id = p_field_id;

  SELECT ROW_COUNT() AS affected;
END$$

CREATE PROCEDURE sp_cfg_custom_field_reorder (
  IN p_tenant_id  INT,
  IN p_user_id    INT,
  IN p_items_json LONGTEXT
)
BEGIN
  DECLARE v_item  JSON;
  DECLARE i       INT DEFAULT 0;
  DECLARE v_count INT DEFAULT 0;

  IF p_items_json IS NOT NULL AND JSON_VALID(p_items_json) THEN
    SET v_count = JSON_LENGTH(p_items_json);

    WHILE i < v_count DO
      SET v_item = JSON_EXTRACT(p_items_json, CONCAT('$[', i, ']'));

      UPDATE cfg_custom_field
      SET    seq_no      = CAST(JSON_EXTRACT(v_item, '$.seqNo') AS SIGNED),
             section_key = NULLIF(JSON_UNQUOTE(JSON_EXTRACT(v_item, '$.sectionKey')), 'null'),
             updated_by  = p_user_id,
             updated_on  = UTC_TIMESTAMP()
      WHERE  tenant_id = p_tenant_id
        AND  field_id  = CAST(JSON_EXTRACT(v_item, '$.fieldId') AS SIGNED);

      SET i = i + 1;
    END WHILE;
  END IF;

  SELECT v_count AS affected;
END$$

-- How many records already hold a value for this field. The builder shows it
-- before a delete, so "remove this field" is an informed choice.
CREATE PROCEDURE sp_cfg_custom_field_usage (
  IN p_tenant_id INT,
  IN p_user_id   INT,
  IN p_field_id  INT
)
BEGIN
  SELECT COUNT(*) AS filled_count
  FROM   cfg_custom_value
  WHERE  tenant_id  = p_tenant_id
    AND  field_id   = p_field_id
    AND  is_active  = 1
    AND  value_text IS NOT NULL
    AND  value_text <> '';
END$$

CREATE PROCEDURE sp_cfg_custom_value_list (
  IN p_tenant_id  INT,
  IN p_user_id    INT,
  IN p_screen_key VARCHAR(100),
  IN p_record_id  INT
)
BEGIN
  SELECT c.field_id,
         c.field_key,
         c.label,
         c.control_type,
         c.show_in_form,
         c.show_in_detail,
         c.show_in_print,
         c.seq_no,
         v.value_text
  FROM   cfg_custom_field c
  LEFT   JOIN cfg_custom_value v
         ON  v.tenant_id  = c.tenant_id
         AND v.field_id   = c.field_id
         AND v.screen_key = c.screen_key
         AND v.record_id  = p_record_id
         AND v.is_active  = 1
  WHERE  c.tenant_id  = p_tenant_id
    AND  c.screen_key = p_screen_key
    AND  c.is_active  = 1
  ORDER  BY c.seq_no, c.field_id;
END$$

-- The payload is an array of { fieldKey, valueText }. Keys the tenant has no
-- field for are ignored rather than rejected: a form posted from a stale tab
-- must not fail the whole save over a field that was deleted meanwhile.
CREATE PROCEDURE sp_cfg_custom_value_save (
  IN p_tenant_id   INT,
  IN p_user_id     INT,
  IN p_screen_key  VARCHAR(100),
  IN p_record_id   INT,
  IN p_values_json LONGTEXT
)
BEGIN
  DECLARE v_entry    JSON;
  DECLARE v_field_id INT;
  DECLARE v_value    TEXT;
  DECLARE i          INT DEFAULT 0;
  DECLARE v_count    INT DEFAULT 0;
  DECLARE v_written  INT DEFAULT 0;

  IF p_values_json IS NOT NULL AND JSON_VALID(p_values_json) THEN
    SET v_count = JSON_LENGTH(p_values_json);

    WHILE i < v_count DO
      SET v_entry = JSON_EXTRACT(p_values_json, CONCAT('$[', i, ']'));
      SET v_field_id = NULL;

      SELECT field_id INTO v_field_id
      FROM   cfg_custom_field
      WHERE  tenant_id  = p_tenant_id
        AND  screen_key = p_screen_key
        AND  field_key  = JSON_UNQUOTE(JSON_EXTRACT(v_entry, '$.fieldKey'))
        AND  is_active  = 1
      LIMIT  1;

      IF v_field_id IS NOT NULL THEN
        SET v_value = NULLIF(JSON_UNQUOTE(JSON_EXTRACT(v_entry, '$.valueText')), 'null');

        INSERT INTO cfg_custom_value (
          tenant_id, screen_key, record_id, field_id, value_text, created_by, created_on)
        VALUES (p_tenant_id, p_screen_key, p_record_id, v_field_id, v_value, p_user_id, UTC_TIMESTAMP())
        ON DUPLICATE KEY UPDATE
          value_text = VALUES(value_text),
          is_active  = 1,
          updated_by = p_user_id,
          updated_on = UTC_TIMESTAMP();

        SET v_written = v_written + 1;
      END IF;

      SET i = i + 1;
    END WHILE;
  END IF;

  SELECT v_written AS affected;
END$$

-- =====================================================================
-- DOCUMENTS
-- =====================================================================

CREATE PROCEDURE sp_hr_document_list (
  IN p_tenant_id   INT,
  IN p_user_id     INT,
  IN p_status      VARCHAR(30),
  IN p_employee_id INT,
  IN p_search      VARCHAR(200),
  IN p_page_size   INT,
  IN p_offset      INT
)
BEGIN
  SELECT d.document_id,
         d.ref_no,
         d.employee_id,
         e.employee_code,
         e.full_name       AS employee_name,
         dep.dept_name     AS department_name,
         des.desig_name    AS designation_name,
         d.document_type,
         dt.display_name   AS document_type_name,
         d.subject,
         d.effective_date,
         d.valid_till,
         d.signed_by,
         d.status,
         d.issued_on,
         d.acknowledged_on,
         d.delivered_via,
         d.created_on
  FROM   hr_document d
  JOIN   hr_employee e   ON e.employee_id = d.employee_id AND e.tenant_id = d.tenant_id
  LEFT   JOIN hr_department  dep ON dep.department_id  = e.department_id
  LEFT   JOIN hr_designation des ON des.designation_id = e.designation_id
  LEFT   JOIN cfg_print_document_type dt
         ON  dt.tenant_id     = d.tenant_id
         AND dt.document_type = d.document_type
  WHERE  d.tenant_id = p_tenant_id
    AND  d.is_active = 1
    AND  (p_status      IS NULL OR d.status      = p_status)
    AND  (p_employee_id IS NULL OR d.employee_id = p_employee_id)
    AND  (p_search IS NULL OR d.ref_no  LIKE CONCAT('%', p_search, '%')
                           OR e.full_name LIKE CONCAT('%', p_search, '%')
                           OR d.subject LIKE CONCAT('%', p_search, '%'))
  ORDER  BY d.created_on DESC, d.document_id DESC
  LIMIT  p_page_size OFFSET p_offset;

  SELECT COUNT(*) AS total_count
  FROM   hr_document d
  JOIN   hr_employee e ON e.employee_id = d.employee_id AND e.tenant_id = d.tenant_id
  WHERE  d.tenant_id = p_tenant_id
    AND  d.is_active = 1
    AND  (p_status      IS NULL OR d.status      = p_status)
    AND  (p_employee_id IS NULL OR d.employee_id = p_employee_id)
    AND  (p_search IS NULL OR d.ref_no  LIKE CONCAT('%', p_search, '%')
                           OR e.full_name LIKE CONCAT('%', p_search, '%')
                           OR d.subject LIKE CONCAT('%', p_search, '%'));
END$$

CREATE PROCEDURE sp_hr_document_get (
  IN p_tenant_id   INT,
  IN p_user_id     INT,
  IN p_document_id INT
)
BEGIN
  SELECT d.*,
         e.employee_code,
         e.full_name    AS employee_name,
         dep.dept_name  AS department_name,
         des.desig_name AS designation_name
  FROM   hr_document d
  JOIN   hr_employee e   ON e.employee_id = d.employee_id AND e.tenant_id = d.tenant_id
  LEFT   JOIN hr_department  dep ON dep.department_id  = e.department_id
  LEFT   JOIN hr_designation des ON des.designation_id = e.designation_id
  WHERE  d.tenant_id   = p_tenant_id
    AND  d.document_id = p_document_id
    AND  d.is_active   = 1;
END$$

-- Everything the renderer needs for one document, in one round trip: the
-- document with its employee, then the employee's custom field values.
CREATE PROCEDURE sp_hr_document_print_context (
  IN p_tenant_id   INT,
  IN p_user_id     INT,
  IN p_document_id INT
)
BEGIN
  DECLARE v_employee_id INT;

  SELECT employee_id INTO v_employee_id
  FROM   hr_document
  WHERE  tenant_id = p_tenant_id AND document_id = p_document_id AND is_active = 1;

  SELECT d.*,
         e.employee_code,
         e.full_name         AS employee_name,
         e.dob,
         e.date_of_joining,
         e.mobile,
         e.email,
         e.employment_status,
         e.gross_ctc,
         e.hra,
         e.tds,
         e.net_salary,
         dep.dept_name       AS department_name,
         des.desig_name      AS designation_name,
         t.tenant_name
  FROM   hr_document d
  JOIN   hr_employee e   ON e.employee_id = d.employee_id AND e.tenant_id = d.tenant_id
  JOIN   sys_tenant  t   ON t.tenant_id   = d.tenant_id
  LEFT   JOIN hr_department  dep ON dep.department_id  = e.department_id
  LEFT   JOIN hr_designation des ON des.designation_id = e.designation_id
  WHERE  d.tenant_id   = p_tenant_id
    AND  d.document_id = p_document_id
    AND  d.is_active   = 1;

  SELECT c.field_id, c.field_key, c.label, c.control_type, c.seq_no, v.value_text
  FROM   cfg_custom_field c
  LEFT   JOIN cfg_custom_value v
         ON  v.tenant_id  = c.tenant_id
         AND v.field_id   = c.field_id
         AND v.screen_key = c.screen_key
         AND v.record_id  = v_employee_id
         AND v.is_active  = 1
  WHERE  c.tenant_id     = p_tenant_id
    AND  c.screen_key    = 'hr.employee'
    AND  c.is_active     = 1
    AND  c.show_in_print = 1
  ORDER  BY c.seq_no;
END$$

CREATE PROCEDURE sp_hr_document_save (
  IN p_tenant_id      INT,
  IN p_user_id        INT,
  IN p_document_id    INT,
  IN p_ref_no         VARCHAR(80),
  IN p_employee_id    INT,
  IN p_document_type  VARCHAR(60),
  IN p_template_id    INT,
  IN p_subject        VARCHAR(300),
  IN p_body_text      TEXT,
  IN p_effective_date DATE,
  IN p_valid_till     DATE,
  IN p_signed_by      VARCHAR(150),
  IN p_status         VARCHAR(30)
)
BEGIN
  DECLARE v_id     INT;
  DECLARE v_ref    VARCHAR(80);
  DECLARE v_seq    INT;
  DECLARE v_year   CHAR(4);

  IF IFNULL(p_document_id, 0) = 0 THEN
    SET v_ref = NULLIF(TRIM(IFNULL(p_ref_no, '')), '');

    -- A tenant-unique, human-readable reference: TYPE/YYYY/0001. Derived from
    -- the count already issued this year rather than from an auto-increment,
    -- so two tenants never see each other's numbering.
    IF v_ref IS NULL THEN
      SET v_year = DATE_FORMAT(UTC_TIMESTAMP(), '%Y');

      SELECT COUNT(*) + 1 INTO v_seq
      FROM   hr_document
      WHERE  tenant_id     = p_tenant_id
        AND  document_type = p_document_type
        AND  YEAR(created_on) = v_year;

      SET v_ref = CONCAT(UPPER(LEFT(REPLACE(p_document_type, ' ', ''), 6)),
                         '/', v_year, '/', LPAD(v_seq, 4, '0'));
    END IF;

    INSERT INTO hr_document (
      tenant_id, ref_no, employee_id, document_type, template_id, subject, body_text,
      effective_date, valid_till, signed_by, status, created_by, created_on)
    VALUES (
      p_tenant_id, v_ref, p_employee_id, p_document_type, NULLIF(p_template_id, 0),
      p_subject, p_body_text, p_effective_date, p_valid_till, p_signed_by,
      COALESCE(NULLIF(p_status, ''), 'Draft'), p_user_id, UTC_TIMESTAMP());
    SET v_id = LAST_INSERT_ID();
  ELSE
    UPDATE hr_document
    SET    employee_id    = p_employee_id,
           document_type  = p_document_type,
           template_id    = NULLIF(p_template_id, 0),
           subject        = p_subject,
           body_text      = p_body_text,
           effective_date = p_effective_date,
           valid_till     = p_valid_till,
           signed_by      = p_signed_by,
           status         = COALESCE(NULLIF(p_status, ''), status),
           updated_by     = p_user_id,
           updated_on     = UTC_TIMESTAMP()
    WHERE  tenant_id   = p_tenant_id
      AND  document_id = p_document_id;
    SET v_id = p_document_id;
  END IF;

  SELECT d.*,
         e.employee_code,
         e.full_name    AS employee_name,
         dep.dept_name  AS department_name,
         des.desig_name AS designation_name
  FROM   hr_document d
  JOIN   hr_employee e   ON e.employee_id = d.employee_id AND e.tenant_id = d.tenant_id
  LEFT   JOIN hr_department  dep ON dep.department_id  = e.department_id
  LEFT   JOIN hr_designation des ON des.designation_id = e.designation_id
  WHERE  d.tenant_id = p_tenant_id AND d.document_id = v_id;
END$$

-- Status moves and the timestamps that go with them. payload_json is written
-- on the way to Issued and never rewritten, so a template edited later cannot
-- change what an already-issued document said.
CREATE PROCEDURE sp_hr_document_status_set (
  IN p_tenant_id     INT,
  IN p_user_id       INT,
  IN p_document_id   INT,
  IN p_status        VARCHAR(30),
  IN p_delivered_via VARCHAR(100),
  IN p_payload_json  LONGTEXT
)
BEGIN
  UPDATE hr_document
  SET    status          = p_status,
         issued_on       = IF(p_status = 'Issued'       AND issued_on       IS NULL, UTC_TIMESTAMP(), issued_on),
         acknowledged_on = IF(p_status = 'Acknowledged' AND acknowledged_on IS NULL, UTC_TIMESTAMP(), acknowledged_on),
         delivered_via   = COALESCE(NULLIF(p_delivered_via, ''), delivered_via),
         payload_json    = COALESCE(payload_json, p_payload_json),
         updated_by      = p_user_id,
         updated_on      = UTC_TIMESTAMP()
  WHERE  tenant_id   = p_tenant_id
    AND  document_id = p_document_id
    AND  is_active   = 1;

  SELECT d.*,
         e.employee_code,
         e.full_name    AS employee_name,
         dep.dept_name  AS department_name,
         des.desig_name AS designation_name
  FROM   hr_document d
  JOIN   hr_employee e   ON e.employee_id = d.employee_id AND e.tenant_id = d.tenant_id
  LEFT   JOIN hr_department  dep ON dep.department_id  = e.department_id
  LEFT   JOIN hr_designation des ON des.designation_id = e.designation_id
  WHERE  d.tenant_id = p_tenant_id AND d.document_id = p_document_id;
END$$

CREATE PROCEDURE sp_hr_document_delete (
  IN p_tenant_id   INT,
  IN p_user_id     INT,
  IN p_document_id INT
)
BEGIN
  UPDATE hr_document
  SET    is_active  = 0,
         updated_by = p_user_id,
         updated_on = UTC_TIMESTAMP()
  WHERE  tenant_id   = p_tenant_id
    AND  document_id = p_document_id;

  SELECT ROW_COUNT() AS affected;
END$$

DELIMITER ;

-- =====================================================================
--  SEED
-- =====================================================================

-- Document catalogue, for every existing tenant. Only the letters this
-- product can actually fill from hr_employee, hr_department and
-- hr_designation - a payslip row here would advertise a document with no
-- data behind it.
INSERT INTO cfg_print_document_type
  (tenant_id, document_type, display_name, category, layout_family, screen_key,
   default_title, supports_table, seq_no, created_by, created_on)
SELECT t.tenant_id, c.document_type, c.display_name, c.category, c.layout_family, c.screen_key,
       c.default_title, c.supports_table, c.seq_no, 1, UTC_TIMESTAMP()
FROM   sys_tenant t
JOIN (
            SELECT 'OfferLetter'       AS document_type, 'Offer Letter'        AS display_name, 'Letter' AS category, 'Letter' AS layout_family, 'hr.employee' AS screen_key, 'LETTER OF OFFER'          AS default_title, 0 AS supports_table,  10 AS seq_no
  UNION ALL SELECT 'AppointmentLetter',  'Appointment Letter',  'Letter', 'Letter', 'hr.employee', 'LETTER OF APPOINTMENT',     0,  20
  UNION ALL SELECT 'ConfirmationLetter', 'Confirmation Letter', 'Letter', 'Letter', 'hr.employee', 'LETTER OF CONFIRMATION',    0,  30
  UNION ALL SELECT 'PromotionLetter',    'Promotion Letter',    'Letter', 'Letter', 'hr.employee', 'LETTER OF PROMOTION',       0,  40
  UNION ALL SELECT 'IncrementLetter',    'Increment Letter',    'Letter', 'Letter', 'hr.employee', 'SALARY REVISION LETTER',    0,  50
  UNION ALL SELECT 'TransferLetter',     'Transfer Letter',     'Letter', 'Letter', 'hr.employee', 'LETTER OF TRANSFER',        0,  60
  UNION ALL SELECT 'ContractRenewal',    'Employment Renewal',  'Letter', 'Letter', 'hr.employee', 'CONTRACT RENEWAL',          0,  70
  UNION ALL SELECT 'ExperienceLetter',   'Experience Letter',   'Letter', 'Letter', 'hr.employee', 'CERTIFICATE OF EXPERIENCE', 0,  80
  UNION ALL SELECT 'RelievingLetter',    'Relieving Letter',    'Letter', 'Letter', 'hr.employee', 'RELIEVING LETTER',          0,  90
  UNION ALL SELECT 'WarningLetter',      'Warning Letter',      'Letter', 'Letter', 'hr.employee', 'WARNING LETTER',            0, 100
) c
WHERE  t.is_active = 1
  AND  NOT EXISTS (
    SELECT 1 FROM cfg_print_document_type x
    WHERE  x.tenant_id = t.tenant_id AND x.document_type = c.document_type);

-- One system template per document type per tenant. It carries no sections,
-- which leaves it in auto mode: the renderer prints a complete letter from the
-- data context, and an admin curates blocks only when they want to.
INSERT INTO cfg_print_template
  (tenant_id, template_code, template_name, document_type, is_default, is_system,
   style_preset, created_by, created_on)
SELECT d.tenant_id,
       CONCAT('SYS-', d.document_type, '-', d.tenant_id),
       CONCAT(d.display_name, ' - standard'),
       d.document_type,
       1, 1, 'Letter', 1, UTC_TIMESTAMP()
FROM   cfg_print_document_type d
WHERE  d.is_active = 1
  AND  NOT EXISTS (
    SELECT 1 FROM cfg_print_template x
    WHERE  x.tenant_id = d.tenant_id AND x.document_type = d.document_type AND x.is_system = 1);

-- New permission keys, granted the same way 07_seed.sql grants the originals.
INSERT INTO sys_role_permission (tenant_id, role_id, permission_key, created_by, created_on)
SELECT r.tenant_id, r.role_id, p.permission_key, 1, UTC_TIMESTAMP()
FROM   sys_role r
JOIN (
            SELECT 'ADMIN' AS role_code, 'hr.document.view'    AS permission_key
  UNION ALL SELECT 'ADMIN', 'hr.document.edit'
  UNION ALL SELECT 'ADMIN', 'hr.document.issue'
  UNION ALL SELECT 'ADMIN', 'admin.printTemplate'
  UNION ALL SELECT 'ADMIN', 'admin.customField'

  UNION ALL SELECT 'HR', 'hr.document.view'
  UNION ALL SELECT 'HR', 'hr.document.edit'
  UNION ALL SELECT 'HR', 'hr.document.issue'
  UNION ALL SELECT 'HR', 'admin.printTemplate'

  UNION ALL SELECT 'MANAGER', 'hr.document.view'
) p ON p.role_code = r.role_code
WHERE NOT EXISTS (
  SELECT 1 FROM sys_role_permission x
  WHERE  x.tenant_id = r.tenant_id AND x.role_id = r.role_id AND x.permission_key = p.permission_key
);
