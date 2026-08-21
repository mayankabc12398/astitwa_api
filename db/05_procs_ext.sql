-- =====================================================================
--  HrSuite - layer 5 extension procedures
--
--  A hook row with tenant_id = NULL applies to every tenant; a row with a tenant_id
--  applies to that one only. Every read below honours both, and never returns another
--  tenant's row.
-- =====================================================================

USE astitwa;

DROP PROCEDURE IF EXISTS sp_ext_hook_list;
DROP PROCEDURE IF EXISTS sp_ext_hook_get;
DROP PROCEDURE IF EXISTS sp_ext_hook_save;
DROP PROCEDURE IF EXISTS sp_ext_hook_set_active;
DROP PROCEDURE IF EXISTS sp_ext_hook_delete;
DROP PROCEDURE IF EXISTS sp_ext_hook_history_list;
DROP PROCEDURE IF EXISTS sp_ext_hook_rollback;
DROP PROCEDURE IF EXISTS sp_ext_hook_active_for_key;
DROP PROCEDURE IF EXISTS sp_ext_hook_client_scripts;
DROP PROCEDURE IF EXISTS sp_ext_named_query_list;
DROP PROCEDURE IF EXISTS sp_ext_named_query_get;
DROP PROCEDURE IF EXISTS sp_ext_named_query_get_by_key;
DROP PROCEDURE IF EXISTS sp_ext_named_query_save;
DROP PROCEDURE IF EXISTS sp_ext_named_query_delete;
DROP PROCEDURE IF EXISTS sp_ext_hook_log_insert;
DROP PROCEDURE IF EXISTS sp_ext_hook_log_list;

DELIMITER $$

-- =====================================================================
-- SCRIPT HOOKS
-- =====================================================================

CREATE PROCEDURE sp_ext_hook_list (
  IN p_tenant_id INT,
  IN p_user_id   INT,
  IN p_search    VARCHAR(200),
  IN p_run_on    VARCHAR(10),
  IN p_page_size INT,
  IN p_offset    INT
)
BEGIN
  SELECT hook_id, tenant_id, hook_key, seq_no, run_on, debounce_ms,
         is_active, version_no, updated_by, updated_on
  FROM   ext_script_hook
  WHERE  is_deleted = 0
    AND  (tenant_id = p_tenant_id OR tenant_id IS NULL)
    AND  (p_run_on IS NULL OR run_on = p_run_on)
    AND  (p_search IS NULL OR hook_key LIKE CONCAT('%', p_search, '%'))
  ORDER  BY hook_key, seq_no
  LIMIT  p_page_size OFFSET p_offset;

  SELECT COUNT(*) AS total_count
  FROM   ext_script_hook
  WHERE  is_deleted = 0
    AND  (tenant_id = p_tenant_id OR tenant_id IS NULL)
    AND  (p_run_on IS NULL OR run_on = p_run_on)
    AND  (p_search IS NULL OR hook_key LIKE CONCAT('%', p_search, '%'));
END$$

CREATE PROCEDURE sp_ext_hook_get (
  IN p_tenant_id INT,
  IN p_user_id   INT,
  IN p_hook_id   INT
)
BEGIN
  SELECT hook_id, tenant_id, hook_key, seq_no, run_on, script_body, debounce_ms,
         is_active, version_no, updated_by, updated_on
  FROM   ext_script_hook
  WHERE  hook_id = p_hook_id
    AND  is_deleted = 0
    AND  (tenant_id = p_tenant_id OR tenant_id IS NULL);
END$$

-- Every save archives the outgoing row first, so rollback is always available.
-- p_apply_to_all_tenants is honoured only when the caller passes it; the API gates that
-- on a permission before it gets here.
CREATE PROCEDURE sp_ext_hook_save (
  IN p_tenant_id             INT,
  IN p_user_id               INT,
  IN p_hook_id               INT,
  IN p_hook_key              VARCHAR(150),
  IN p_seq_no                INT,
  IN p_run_on                VARCHAR(10),
  IN p_script_body           MEDIUMTEXT,
  IN p_debounce_ms           INT,
  IN p_is_active             BIT,
  IN p_apply_to_all_tenants  BIT
)
BEGIN
  DECLARE v_id      INT;
  DECLARE v_version INT;
  DECLARE v_owner   INT;

  SET v_owner = IF(p_apply_to_all_tenants = 1, NULL, p_tenant_id);

  IF IFNULL(p_hook_id, 0) = 0 THEN
    INSERT INTO ext_script_hook
          (tenant_id, hook_key, seq_no, run_on, script_body, debounce_ms, is_active,
           version_no, created_by, created_on, updated_by, updated_on)
    VALUES (v_owner, p_hook_key, IFNULL(p_seq_no, 10), p_run_on, p_script_body, p_debounce_ms,
            IFNULL(p_is_active, 0), 1, p_user_id, UTC_TIMESTAMP(), p_user_id, UTC_TIMESTAMP());
    SET v_id = LAST_INSERT_ID();
  ELSE
    -- Archive the version being replaced.
    INSERT INTO ext_script_hook_history
          (hook_id, tenant_id, hook_key, seq_no, run_on, script_body, debounce_ms,
           is_active, version_no, archived_by, archived_on)
    SELECT hook_id, tenant_id, hook_key, seq_no, run_on, script_body, debounce_ms,
           is_active, version_no, p_user_id, UTC_TIMESTAMP()
    FROM   ext_script_hook
    WHERE  hook_id = p_hook_id
      AND  (tenant_id = p_tenant_id OR tenant_id IS NULL);

    SELECT version_no INTO v_version
    FROM   ext_script_hook
    WHERE  hook_id = p_hook_id;

    UPDATE ext_script_hook
    SET    hook_key    = p_hook_key,
           seq_no      = IFNULL(p_seq_no, seq_no),
           run_on      = p_run_on,
           script_body = p_script_body,
           debounce_ms = p_debounce_ms,
           is_active   = IFNULL(p_is_active, is_active),
           version_no  = IFNULL(v_version, 1) + 1,
           updated_by  = p_user_id,
           updated_on  = UTC_TIMESTAMP()
    WHERE  hook_id = p_hook_id
      AND  (tenant_id = p_tenant_id OR tenant_id IS NULL);

    SET v_id = p_hook_id;
  END IF;

  SELECT hook_id, tenant_id, hook_key, seq_no, run_on, script_body, debounce_ms,
         is_active, version_no, updated_by, updated_on
  FROM   ext_script_hook
  WHERE  hook_id = v_id;
END$$

CREATE PROCEDURE sp_ext_hook_set_active (
  IN p_tenant_id INT,
  IN p_user_id   INT,
  IN p_hook_id   INT,
  IN p_is_active BIT
)
BEGIN
  UPDATE ext_script_hook
  SET    is_active  = p_is_active,
         updated_by = p_user_id,
         updated_on = UTC_TIMESTAMP()
  WHERE  hook_id = p_hook_id
    AND  is_deleted = 0
    AND  (tenant_id = p_tenant_id OR tenant_id IS NULL);

  SELECT hook_id, tenant_id, hook_key, seq_no, run_on, script_body, debounce_ms,
         is_active, version_no, updated_by, updated_on
  FROM   ext_script_hook
  WHERE  hook_id = p_hook_id;
END$$

CREATE PROCEDURE sp_ext_hook_delete (
  IN p_tenant_id INT,
  IN p_user_id   INT,
  IN p_hook_id   INT
)
BEGIN
  UPDATE ext_script_hook
  SET    is_deleted = 1,
         is_active  = 0,
         updated_by = p_user_id,
         updated_on = UTC_TIMESTAMP()
  WHERE  hook_id = p_hook_id
    AND  (tenant_id = p_tenant_id OR tenant_id IS NULL);
END$$

CREATE PROCEDURE sp_ext_hook_history_list (
  IN p_tenant_id INT,
  IN p_user_id   INT,
  IN p_hook_id   INT
)
BEGIN
  SELECT h.history_id, h.hook_id, h.version_no, h.run_on, h.seq_no, h.debounce_ms,
         h.is_active, h.script_body, h.archived_by, h.archived_on
  FROM   ext_script_hook_history h
  JOIN   ext_script_hook s ON s.hook_id = h.hook_id
  WHERE  h.hook_id = p_hook_id
    AND  (s.tenant_id = p_tenant_id OR s.tenant_id IS NULL)
  ORDER  BY h.version_no DESC
  LIMIT  50;
END$$

-- Rolling back is itself a save: the current version is archived before being replaced,
-- so a rollback can be rolled back.
CREATE PROCEDURE sp_ext_hook_rollback (
  IN p_tenant_id  INT,
  IN p_user_id    INT,
  IN p_hook_id    INT,
  IN p_history_id INT
)
BEGIN
  DECLARE v_body    MEDIUMTEXT;
  DECLARE v_run_on  VARCHAR(10);
  DECLARE v_seq     INT;
  DECLARE v_debounce INT;
  DECLARE v_version INT;

  SELECT h.script_body, h.run_on, h.seq_no, h.debounce_ms
  INTO   v_body, v_run_on, v_seq, v_debounce
  FROM   ext_script_hook_history h
  JOIN   ext_script_hook s ON s.hook_id = h.hook_id
  WHERE  h.history_id = p_history_id
    AND  h.hook_id    = p_hook_id
    AND  (s.tenant_id = p_tenant_id OR s.tenant_id IS NULL);

  IF v_body IS NOT NULL THEN
    INSERT INTO ext_script_hook_history
          (hook_id, tenant_id, hook_key, seq_no, run_on, script_body, debounce_ms,
           is_active, version_no, archived_by, archived_on)
    SELECT hook_id, tenant_id, hook_key, seq_no, run_on, script_body, debounce_ms,
           is_active, version_no, p_user_id, UTC_TIMESTAMP()
    FROM   ext_script_hook
    WHERE  hook_id = p_hook_id;

    SELECT version_no INTO v_version FROM ext_script_hook WHERE hook_id = p_hook_id;

    UPDATE ext_script_hook
    SET    script_body = v_body,
           run_on      = v_run_on,
           seq_no      = v_seq,
           debounce_ms = v_debounce,
           version_no  = IFNULL(v_version, 1) + 1,
           updated_by  = p_user_id,
           updated_on  = UTC_TIMESTAMP()
    WHERE  hook_id = p_hook_id
      AND  (tenant_id = p_tenant_id OR tenant_id IS NULL);
  END IF;

  SELECT hook_id, tenant_id, hook_key, seq_no, run_on, script_body, debounce_ms,
         is_active, version_no, updated_by, updated_on
  FROM   ext_script_hook
  WHERE  hook_id = p_hook_id;
END$$

-- What the engine runs. A tenant-specific row wins over a global one at the same seq_no.
CREATE PROCEDURE sp_ext_hook_active_for_key (
  IN p_tenant_id INT,
  IN p_user_id   INT,
  IN p_hook_key  VARCHAR(150),
  IN p_run_on    VARCHAR(10)
)
BEGIN
  SELECT hook_id, tenant_id, hook_key, seq_no, run_on, script_body, debounce_ms, version_no
  FROM   ext_script_hook
  WHERE  hook_key   = p_hook_key
    AND  run_on     = p_run_on
    AND  is_active  = 1
    AND  is_deleted = 0
    AND  (tenant_id = p_tenant_id OR tenant_id IS NULL)
  ORDER  BY seq_no, (tenant_id IS NULL), hook_id
  LIMIT  20;
END$$

-- Sent to the browser sandbox at start-up. Server-only scripts never leave the server.
CREATE PROCEDURE sp_ext_hook_client_scripts (
  IN p_tenant_id INT,
  IN p_user_id   INT
)
BEGIN
  SELECT hook_id, hook_key, seq_no, script_body, debounce_ms
  FROM   ext_script_hook
  WHERE  run_on     = 'client'
    AND  is_active  = 1
    AND  is_deleted = 0
    AND  (tenant_id = p_tenant_id OR tenant_id IS NULL)
  ORDER  BY hook_key, seq_no, (tenant_id IS NULL)
  LIMIT  200;
END$$

-- =====================================================================
-- NAMED QUERIES
-- =====================================================================

CREATE PROCEDURE sp_ext_named_query_list (
  IN p_tenant_id INT,
  IN p_user_id   INT,
  IN p_search    VARCHAR(200),
  IN p_page_size INT,
  IN p_offset    INT
)
BEGIN
  SELECT query_id, tenant_id, query_key, proc_name, params_json, columns_json,
         max_rows, required_permission, is_active
  FROM   ext_named_query
  WHERE  (tenant_id = p_tenant_id OR tenant_id IS NULL)
    AND  (p_search IS NULL OR query_key LIKE CONCAT('%', p_search, '%')
                           OR proc_name LIKE CONCAT('%', p_search, '%'))
  ORDER  BY query_key
  LIMIT  p_page_size OFFSET p_offset;

  SELECT COUNT(*) AS total_count
  FROM   ext_named_query
  WHERE  (tenant_id = p_tenant_id OR tenant_id IS NULL)
    AND  (p_search IS NULL OR query_key LIKE CONCAT('%', p_search, '%')
                           OR proc_name LIKE CONCAT('%', p_search, '%'));
END$$

CREATE PROCEDURE sp_ext_named_query_get (
  IN p_tenant_id INT,
  IN p_user_id   INT,
  IN p_query_id  INT
)
BEGIN
  SELECT query_id, tenant_id, query_key, proc_name, params_json, columns_json,
         max_rows, required_permission, is_active
  FROM   ext_named_query
  WHERE  query_id = p_query_id
    AND  (tenant_id = p_tenant_id OR tenant_id IS NULL);
END$$

-- The single lookup the runner uses. Tenant-specific registration wins over global.
CREATE PROCEDURE sp_ext_named_query_get_by_key (
  IN p_tenant_id INT,
  IN p_user_id   INT,
  IN p_query_key VARCHAR(120)
)
BEGIN
  SELECT query_id, tenant_id, query_key, proc_name, params_json, columns_json,
         max_rows, required_permission, is_active
  FROM   ext_named_query
  WHERE  query_key = p_query_key
    AND  is_active = 1
    AND  (tenant_id = p_tenant_id OR tenant_id IS NULL)
  ORDER  BY (tenant_id IS NULL)
  LIMIT  1;
END$$

CREATE PROCEDURE sp_ext_named_query_save (
  IN p_tenant_id           INT,
  IN p_user_id             INT,
  IN p_query_id            INT,
  IN p_query_key           VARCHAR(120),
  IN p_proc_name           VARCHAR(150),
  IN p_params_json         TEXT,
  IN p_columns_json        TEXT,
  IN p_max_rows            INT,
  IN p_required_permission VARCHAR(120),
  IN p_is_active           BIT,
  IN p_apply_to_all_tenants BIT
)
BEGIN
  DECLARE v_id    INT;
  DECLARE v_owner INT;

  SET v_owner = IF(p_apply_to_all_tenants = 1, NULL, p_tenant_id);

  IF IFNULL(p_query_id, 0) = 0 THEN
    INSERT INTO ext_named_query
          (tenant_id, query_key, proc_name, params_json, columns_json, max_rows,
           required_permission, is_active, created_by, created_on)
    VALUES (v_owner, p_query_key, p_proc_name, p_params_json, p_columns_json,
            IFNULL(p_max_rows, 100), p_required_permission, IFNULL(p_is_active, 1),
            p_user_id, UTC_TIMESTAMP());
    SET v_id = LAST_INSERT_ID();
  ELSE
    UPDATE ext_named_query
    SET    query_key           = p_query_key,
           proc_name           = p_proc_name,
           params_json         = p_params_json,
           columns_json        = p_columns_json,
           max_rows            = IFNULL(p_max_rows, max_rows),
           required_permission = p_required_permission,
           is_active           = IFNULL(p_is_active, is_active),
           updated_by          = p_user_id,
           updated_on          = UTC_TIMESTAMP()
    WHERE  query_id = p_query_id
      AND  (tenant_id = p_tenant_id OR tenant_id IS NULL);
    SET v_id = p_query_id;
  END IF;

  SELECT query_id, tenant_id, query_key, proc_name, params_json, columns_json,
         max_rows, required_permission, is_active
  FROM   ext_named_query
  WHERE  query_id = v_id;
END$$

CREATE PROCEDURE sp_ext_named_query_delete (
  IN p_tenant_id INT,
  IN p_user_id   INT,
  IN p_query_id  INT
)
BEGIN
  UPDATE ext_named_query
  SET    is_active  = 0,
         updated_by = p_user_id,
         updated_on = UTC_TIMESTAMP()
  WHERE  query_id = p_query_id
    AND  (tenant_id = p_tenant_id OR tenant_id IS NULL);
END$$

-- =====================================================================
-- HOOK LOG
-- =====================================================================

CREATE PROCEDURE sp_ext_hook_log_insert (
  IN p_tenant_id    INT,
  IN p_user_id      INT,
  IN p_hook_id      INT,
  IN p_hook_key     VARCHAR(150),
  IN p_run_on       VARCHAR(10),
  IN p_status       VARCHAR(20),
  IN p_duration_ms  INT,
  IN p_message      TEXT,
  IN p_context_json TEXT
)
BEGIN
  INSERT INTO ext_hook_log
        (hook_id, tenant_id, hook_key, run_on, status, duration_ms, message, context_json,
         logged_by, logged_on)
  VALUES (p_hook_id, p_tenant_id, p_hook_key, p_run_on, p_status, IFNULL(p_duration_ms, 0),
          p_message, p_context_json, p_user_id, UTC_TIMESTAMP());
END$$

CREATE PROCEDURE sp_ext_hook_log_list (
  IN p_tenant_id INT,
  IN p_user_id   INT,
  IN p_search    VARCHAR(200),
  IN p_status    VARCHAR(20),
  IN p_hook_id   INT,
  IN p_page_size INT,
  IN p_offset    INT
)
BEGIN
  SELECT log_id, hook_id, tenant_id, hook_key, run_on, status, duration_ms,
         message, context_json, logged_by, logged_on
  FROM   ext_hook_log
  WHERE  tenant_id = p_tenant_id
    AND  (p_status  IS NULL OR status  = p_status)
    AND  (p_hook_id IS NULL OR hook_id = p_hook_id)
    AND  (p_search  IS NULL OR hook_key LIKE CONCAT('%', p_search, '%')
                            OR message  LIKE CONCAT('%', p_search, '%'))
  ORDER  BY log_id DESC
  LIMIT  p_page_size OFFSET p_offset;

  SELECT COUNT(*) AS total_count
  FROM   ext_hook_log
  WHERE  tenant_id = p_tenant_id
    AND  (p_status  IS NULL OR status  = p_status)
    AND  (p_hook_id IS NULL OR hook_id = p_hook_id)
    AND  (p_search  IS NULL OR hook_key LIKE CONCAT('%', p_search, '%')
                            OR message  LIKE CONCAT('%', p_search, '%'));
END$$

DELIMITER ;
