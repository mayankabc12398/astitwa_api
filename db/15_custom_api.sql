-- =====================================================================
--  HrSuite - API Builder: endpoints an administrator writes, stored as data
--
--  Apply after 14_employee_by_department.sql. Re-runnable.
--
--  ext_named_query already lets a script reach the database, but only through a
--  procedure somebody wrote in SQL and deployed. This adds the other half: the
--  SELECT itself is stored, and it answers on a URL of its own. Creating one is
--  an INSERT, so a new endpoint is live on the next request - no build, no
--  restart, no deployment.
--
--  What keeps that from being a hole is spread across three places, and all
--  three have to hold:
--    * SqlGuard, in the engine, refuses anything that is not a single read.
--    * The SQL must carry a {tenant} token, which the runner replaces with a
--      bound parameter. An endpoint that forgets its tenant filter cannot be
--      saved and cannot be run.
--    * The runner opens a READ ONLY transaction, so a write that slipped past
--      the first two is refused by MySQL itself.
-- =====================================================================

SET FOREIGN_KEY_CHECKS = 0;

CREATE TABLE IF NOT EXISTS ext_api_endpoint (
  endpoint_id  INT AUTO_INCREMENT PRIMARY KEY,
  tenant_id    INT          NULL,        -- NULL = every tenant
  slug         VARCHAR(80)  NOT NULL,    -- the URL: /api/x/<slug>
  title        VARCHAR(150) NULL,
  http_method  VARCHAR(6)   NOT NULL DEFAULT 'POST',
  sql_text     MEDIUMTEXT   NOT NULL,
  params_json  TEXT         NULL,        -- declared parameters, names + types
  columns_json TEXT         NULL,        -- declared output columns (whitelist)
  max_rows     INT          NOT NULL DEFAULT 100,
  required_permission VARCHAR(120) NULL,
  is_active    BIT          NOT NULL DEFAULT 0,
  version_no   INT          NOT NULL DEFAULT 1,
  is_deleted   BIT          NOT NULL DEFAULT 0,
  created_by   INT          NULL,
  created_on   DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_by   INT          NULL,
  updated_on   DATETIME     NULL,
  KEY ix_ext_api_endpoint_lookup (slug, is_active, tenant_id)
) ENGINE=InnoDB;

-- Every save pushes the outgoing row here first, so rollback is always possible.
CREATE TABLE IF NOT EXISTS ext_api_endpoint_history (
  history_id   INT AUTO_INCREMENT PRIMARY KEY,
  endpoint_id  INT          NOT NULL,
  tenant_id    INT          NULL,
  slug         VARCHAR(80)  NOT NULL,
  title        VARCHAR(150) NULL,
  http_method  VARCHAR(6)   NOT NULL,
  sql_text     MEDIUMTEXT   NOT NULL,
  params_json  TEXT         NULL,
  columns_json TEXT         NULL,
  max_rows     INT          NOT NULL,
  required_permission VARCHAR(120) NULL,
  is_active    BIT          NOT NULL,
  version_no   INT          NOT NULL,
  archived_by  INT          NULL,
  archived_on  DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  KEY ix_ext_api_endpoint_history (endpoint_id, version_no)
) ENGINE=InnoDB;

-- An endpoint an administrator wrote needs the same audit trail a script has.
CREATE TABLE IF NOT EXISTS ext_api_call_log (
  log_id      BIGINT AUTO_INCREMENT PRIMARY KEY,
  endpoint_id INT          NULL,
  tenant_id   INT          NULL,
  slug        VARCHAR(80)  NULL,
  status      VARCHAR(20)  NOT NULL,   -- ok | error | denied
  duration_ms INT          NOT NULL DEFAULT 0,
  row_count   INT          NOT NULL DEFAULT 0,
  message     TEXT         NULL,
  called_by   INT          NULL,
  called_on   DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  KEY ix_ext_api_call_log_filter (tenant_id, status, called_on),
  KEY ix_ext_api_call_log_endpoint (endpoint_id, called_on)
) ENGINE=InnoDB;

SET FOREIGN_KEY_CHECKS = 1;

-- =====================================================================
--  PROCEDURES
-- =====================================================================

DROP PROCEDURE IF EXISTS sp_ext_api_list;
DROP PROCEDURE IF EXISTS sp_ext_api_get;
DROP PROCEDURE IF EXISTS sp_ext_api_get_by_slug;
DROP PROCEDURE IF EXISTS sp_ext_api_slug_taken;
DROP PROCEDURE IF EXISTS sp_ext_api_save;
DROP PROCEDURE IF EXISTS sp_ext_api_set_active;
DROP PROCEDURE IF EXISTS sp_ext_api_delete;
DROP PROCEDURE IF EXISTS sp_ext_api_history_list;
DROP PROCEDURE IF EXISTS sp_ext_api_rollback;
DROP PROCEDURE IF EXISTS sp_ext_api_log_insert;
DROP PROCEDURE IF EXISTS sp_ext_api_log_list;

DELIMITER $$

CREATE PROCEDURE sp_ext_api_list (
  IN p_tenant_id INT,
  IN p_user_id   INT,
  IN p_search    VARCHAR(200),
  IN p_page_size INT,
  IN p_offset    INT
)
BEGIN
  SELECT endpoint_id, tenant_id, slug, title, http_method, max_rows,
         required_permission, is_active, version_no, updated_by, updated_on
  FROM   ext_api_endpoint
  WHERE  is_deleted = 0
    AND  (tenant_id = p_tenant_id OR tenant_id IS NULL)
    AND  (p_search IS NULL OR slug LIKE CONCAT('%', p_search, '%')
                           OR title LIKE CONCAT('%', p_search, '%'))
  ORDER  BY slug
  LIMIT  p_page_size OFFSET p_offset;

  SELECT COUNT(*)
  FROM   ext_api_endpoint
  WHERE  is_deleted = 0
    AND  (tenant_id = p_tenant_id OR tenant_id IS NULL)
    AND  (p_search IS NULL OR slug LIKE CONCAT('%', p_search, '%')
                           OR title LIKE CONCAT('%', p_search, '%'));
END$$

CREATE PROCEDURE sp_ext_api_get (
  IN p_tenant_id   INT,
  IN p_user_id     INT,
  IN p_endpoint_id INT
)
BEGIN
  SELECT endpoint_id, tenant_id, slug, title, http_method, sql_text, params_json,
         columns_json, max_rows, required_permission, is_active, version_no,
         updated_by, updated_on
  FROM   ext_api_endpoint
  WHERE  endpoint_id = p_endpoint_id
    AND  is_deleted = 0
    AND  (tenant_id = p_tenant_id OR tenant_id IS NULL);
END$$

-- What the runtime resolves a URL against. A row belonging to this tenant wins over
-- a global one, so a tenant can override an endpoint the product shipped.
CREATE PROCEDURE sp_ext_api_get_by_slug (
  IN p_tenant_id INT,
  IN p_user_id   INT,
  IN p_slug      VARCHAR(80)
)
BEGIN
  SELECT endpoint_id, tenant_id, slug, title, http_method, sql_text, params_json,
         columns_json, max_rows, required_permission, is_active, version_no,
         updated_by, updated_on
  FROM   ext_api_endpoint
  WHERE  slug = p_slug
    AND  is_deleted = 0
    AND  is_active = 1
    AND  (tenant_id = p_tenant_id OR tenant_id IS NULL)
  ORDER  BY (tenant_id IS NULL)
  LIMIT  1;
END$$

-- A slug is a URL, so two rows answering to one is a routing accident waiting to
-- happen. MySQL will not do this with a unique index: tenant_id is nullable, and
-- NULL never equals NULL, so the global rows would not be covered.
CREATE PROCEDURE sp_ext_api_slug_taken (
  IN p_tenant_id   INT,
  IN p_user_id     INT,
  IN p_slug        VARCHAR(80),
  IN p_endpoint_id INT
)
BEGIN
  SELECT COUNT(*) AS match_count
  FROM   ext_api_endpoint
  WHERE  slug = p_slug
    AND  is_deleted = 0
    AND  (tenant_id = p_tenant_id OR tenant_id IS NULL)
    AND  endpoint_id <> IFNULL(p_endpoint_id, 0);
END$$

CREATE PROCEDURE sp_ext_api_save (
  IN p_tenant_id            INT,
  IN p_user_id              INT,
  IN p_endpoint_id          INT,
  IN p_slug                 VARCHAR(80),
  IN p_title                VARCHAR(150),
  IN p_http_method          VARCHAR(6),
  IN p_sql_text             MEDIUMTEXT,
  IN p_params_json          TEXT,
  IN p_columns_json         TEXT,
  IN p_max_rows             INT,
  IN p_required_permission  VARCHAR(120),
  IN p_is_active            BIT,
  IN p_apply_to_all_tenants BIT
)
BEGIN
  DECLARE v_id      INT;
  DECLARE v_version INT;
  DECLARE v_owner   INT;

  SET v_owner = IF(p_apply_to_all_tenants = 1, NULL, p_tenant_id);

  IF IFNULL(p_endpoint_id, 0) = 0 THEN
    INSERT INTO ext_api_endpoint
          (tenant_id, slug, title, http_method, sql_text, params_json, columns_json,
           max_rows, required_permission, is_active, version_no,
           created_by, created_on, updated_by, updated_on)
    VALUES (v_owner, p_slug, p_title, IFNULL(p_http_method, 'POST'), p_sql_text,
            p_params_json, p_columns_json, IFNULL(p_max_rows, 100), p_required_permission,
            IFNULL(p_is_active, 0), 1, p_user_id, UTC_TIMESTAMP(), p_user_id, UTC_TIMESTAMP());
    SET v_id = LAST_INSERT_ID();
  ELSE
    INSERT INTO ext_api_endpoint_history
          (endpoint_id, tenant_id, slug, title, http_method, sql_text, params_json,
           columns_json, max_rows, required_permission, is_active, version_no,
           archived_by, archived_on)
    SELECT endpoint_id, tenant_id, slug, title, http_method, sql_text, params_json,
           columns_json, max_rows, required_permission, is_active, version_no,
           p_user_id, UTC_TIMESTAMP()
    FROM   ext_api_endpoint
    WHERE  endpoint_id = p_endpoint_id
      AND  (tenant_id = p_tenant_id OR tenant_id IS NULL);

    SELECT version_no INTO v_version
    FROM   ext_api_endpoint
    WHERE  endpoint_id = p_endpoint_id;

    UPDATE ext_api_endpoint
    SET    slug                = p_slug,
           title               = p_title,
           http_method         = IFNULL(p_http_method, http_method),
           sql_text            = p_sql_text,
           params_json         = p_params_json,
           columns_json        = p_columns_json,
           max_rows            = IFNULL(p_max_rows, max_rows),
           required_permission = p_required_permission,
           is_active           = IFNULL(p_is_active, is_active),
           version_no          = IFNULL(v_version, 1) + 1,
           updated_by          = p_user_id,
           updated_on          = UTC_TIMESTAMP()
    WHERE  endpoint_id = p_endpoint_id
      AND  (tenant_id = p_tenant_id OR tenant_id IS NULL);

    SET v_id = p_endpoint_id;
  END IF;

  SELECT endpoint_id, tenant_id, slug, title, http_method, sql_text, params_json,
         columns_json, max_rows, required_permission, is_active, version_no,
         updated_by, updated_on
  FROM   ext_api_endpoint
  WHERE  endpoint_id = v_id;
END$$

CREATE PROCEDURE sp_ext_api_set_active (
  IN p_tenant_id   INT,
  IN p_user_id     INT,
  IN p_endpoint_id INT,
  IN p_is_active   BIT
)
BEGIN
  UPDATE ext_api_endpoint
  SET    is_active  = p_is_active,
         updated_by = p_user_id,
         updated_on = UTC_TIMESTAMP()
  WHERE  endpoint_id = p_endpoint_id
    AND  is_deleted = 0
    AND  (tenant_id = p_tenant_id OR tenant_id IS NULL);

  SELECT endpoint_id, tenant_id, slug, title, http_method, sql_text, params_json,
         columns_json, max_rows, required_permission, is_active, version_no,
         updated_by, updated_on
  FROM   ext_api_endpoint
  WHERE  endpoint_id = p_endpoint_id;
END$$

-- Soft delete. A URL that answered yesterday and 404s today is a thing somebody has
-- to be able to look at afterwards.
CREATE PROCEDURE sp_ext_api_delete (
  IN p_tenant_id   INT,
  IN p_user_id     INT,
  IN p_endpoint_id INT
)
BEGIN
  UPDATE ext_api_endpoint
  SET    is_deleted = 1,
         is_active  = 0,
         updated_by = p_user_id,
         updated_on = UTC_TIMESTAMP()
  WHERE  endpoint_id = p_endpoint_id
    AND  (tenant_id = p_tenant_id OR tenant_id IS NULL);
END$$

CREATE PROCEDURE sp_ext_api_history_list (
  IN p_tenant_id   INT,
  IN p_user_id     INT,
  IN p_endpoint_id INT
)
BEGIN
  SELECT h.history_id, h.endpoint_id, h.version_no, h.slug, h.title, h.http_method,
         h.sql_text, h.params_json, h.columns_json, h.max_rows, h.required_permission,
         h.is_active, h.archived_by, h.archived_on
  FROM   ext_api_endpoint_history h
  JOIN   ext_api_endpoint e ON e.endpoint_id = h.endpoint_id
  WHERE  h.endpoint_id = p_endpoint_id
    AND  (e.tenant_id = p_tenant_id OR e.tenant_id IS NULL)
  ORDER  BY h.version_no DESC
  LIMIT  50;
END$$

-- Rolling back is itself a save: the current version is archived before being
-- replaced, so a rollback can be rolled back.
CREATE PROCEDURE sp_ext_api_rollback (
  IN p_tenant_id   INT,
  IN p_user_id     INT,
  IN p_endpoint_id INT,
  IN p_history_id  INT
)
BEGIN
  DECLARE v_version INT;

  INSERT INTO ext_api_endpoint_history
        (endpoint_id, tenant_id, slug, title, http_method, sql_text, params_json,
         columns_json, max_rows, required_permission, is_active, version_no,
         archived_by, archived_on)
  SELECT endpoint_id, tenant_id, slug, title, http_method, sql_text, params_json,
         columns_json, max_rows, required_permission, is_active, version_no,
         p_user_id, UTC_TIMESTAMP()
  FROM   ext_api_endpoint
  WHERE  endpoint_id = p_endpoint_id
    AND  (tenant_id = p_tenant_id OR tenant_id IS NULL);

  SELECT version_no INTO v_version
  FROM   ext_api_endpoint
  WHERE  endpoint_id = p_endpoint_id;

  UPDATE ext_api_endpoint e
  JOIN   ext_api_endpoint_history h
    ON   h.history_id = p_history_id AND h.endpoint_id = e.endpoint_id
  SET    e.slug                = h.slug,
         e.title               = h.title,
         e.http_method         = h.http_method,
         e.sql_text            = h.sql_text,
         e.params_json         = h.params_json,
         e.columns_json        = h.columns_json,
         e.max_rows            = h.max_rows,
         e.required_permission = h.required_permission,
         e.version_no          = IFNULL(v_version, 1) + 1,
         e.updated_by          = p_user_id,
         e.updated_on          = UTC_TIMESTAMP()
  WHERE  e.endpoint_id = p_endpoint_id
    AND  (e.tenant_id = p_tenant_id OR e.tenant_id IS NULL);

  SELECT endpoint_id, tenant_id, slug, title, http_method, sql_text, params_json,
         columns_json, max_rows, required_permission, is_active, version_no,
         updated_by, updated_on
  FROM   ext_api_endpoint
  WHERE  endpoint_id = p_endpoint_id;
END$$

CREATE PROCEDURE sp_ext_api_log_insert (
  IN p_tenant_id   INT,
  IN p_user_id     INT,
  IN p_endpoint_id INT,
  IN p_slug        VARCHAR(80),
  IN p_status      VARCHAR(20),
  IN p_duration_ms INT,
  IN p_row_count   INT,
  IN p_message     TEXT
)
BEGIN
  INSERT INTO ext_api_call_log
        (endpoint_id, tenant_id, slug, status, duration_ms, row_count, message,
         called_by, called_on)
  VALUES (NULLIF(IFNULL(p_endpoint_id, 0), 0), p_tenant_id, p_slug, p_status,
          IFNULL(p_duration_ms, 0), IFNULL(p_row_count, 0), p_message,
          p_user_id, UTC_TIMESTAMP());
END$$

CREATE PROCEDURE sp_ext_api_log_list (
  IN p_tenant_id INT,
  IN p_user_id   INT,
  IN p_search    VARCHAR(200),
  IN p_page_size INT,
  IN p_offset    INT
)
BEGIN
  SELECT log_id, endpoint_id, tenant_id, slug, status, duration_ms, row_count,
         message, called_by, called_on
  FROM   ext_api_call_log
  WHERE  (tenant_id = p_tenant_id OR tenant_id IS NULL)
    AND  (p_search IS NULL OR slug LIKE CONCAT('%', p_search, '%')
                           OR status LIKE CONCAT('%', p_search, '%'))
  ORDER  BY called_on DESC
  LIMIT  p_page_size OFFSET p_offset;

  SELECT COUNT(*)
  FROM   ext_api_call_log
  WHERE  (tenant_id = p_tenant_id OR tenant_id IS NULL)
    AND  (p_search IS NULL OR slug LIKE CONCAT('%', p_search, '%')
                           OR status LIKE CONCAT('%', p_search, '%'));
END$$

DELIMITER ;
