-- =====================================================================
--  HrSuite - sys procedures (tenancy, identity, licensing)
--  Every parameter is prefixed p_ so it can never collide with a column name.
-- =====================================================================

USE astitwa;

DROP PROCEDURE IF EXISTS sp_sys_tenant_get_by_code;
DROP PROCEDURE IF EXISTS sp_sys_tenant_list;
DROP PROCEDURE IF EXISTS sp_sys_user_get_for_login;
DROP PROCEDURE IF EXISTS sp_sys_user_permissions;
DROP PROCEDURE IF EXISTS sp_sys_user_touch_login;
DROP PROCEDURE IF EXISTS sp_sys_tenant_module_list;
DROP PROCEDURE IF EXISTS sp_sys_tenant_module_set;
DROP PROCEDURE IF EXISTS sp_sys_tenant_integration_list;
DROP PROCEDURE IF EXISTS sp_sys_tenant_integration_get;
DROP PROCEDURE IF EXISTS sp_sys_tenant_integration_set;

DELIMITER $$

-- ---------------------------------------------------------------------
CREATE PROCEDURE sp_sys_tenant_get_by_code (IN p_tenant_code VARCHAR(40))
BEGIN
  SELECT tenant_id, tenant_code, tenant_name, is_active
  FROM   sys_tenant
  WHERE  tenant_code = p_tenant_code
    AND  is_active = 1;
END$$

-- ---------------------------------------------------------------------
CREATE PROCEDURE sp_sys_tenant_list ()
BEGIN
  SELECT tenant_id, tenant_code, tenant_name, is_active
  FROM   sys_tenant
  WHERE  is_active = 1
  ORDER  BY tenant_name;
END$$

-- ---------------------------------------------------------------------
-- Returns the hash so the API can verify it. Nothing else reads this proc.
CREATE PROCEDURE sp_sys_user_get_for_login (
  IN p_tenant_code VARCHAR(40),
  IN p_user_name   VARCHAR(80)
)
BEGIN
  SELECT u.user_id,
         u.tenant_id,
         t.tenant_code,
         t.tenant_name,
         u.user_name,
         u.display_name,
         u.email,
         u.password_hash,
         u.employee_id,
         u.is_active
  FROM   sys_user u
  JOIN   sys_tenant t ON t.tenant_id = u.tenant_id AND t.is_active = 1
  WHERE  t.tenant_code = p_tenant_code
    AND  u.user_name   = p_user_name
    AND  u.is_active   = 1;
END$$

-- ---------------------------------------------------------------------
-- Two result sets: roles, then permission keys.
CREATE PROCEDURE sp_sys_user_permissions (
  IN p_tenant_id INT,
  IN p_user_id   INT
)
BEGIN
  SELECT r.role_code
  FROM   sys_user_role ur
  JOIN   sys_role r ON r.role_id = ur.role_id AND r.tenant_id = ur.tenant_id AND r.is_active = 1
  WHERE  ur.tenant_id = p_tenant_id
    AND  ur.user_id   = p_user_id
    AND  ur.is_active = 1;

  SELECT DISTINCT rp.permission_key
  FROM   sys_user_role ur
  JOIN   sys_role_permission rp ON rp.role_id = ur.role_id AND rp.tenant_id = ur.tenant_id AND rp.is_active = 1
  JOIN   sys_role r ON r.role_id = ur.role_id AND r.tenant_id = ur.tenant_id AND r.is_active = 1
  WHERE  ur.tenant_id = p_tenant_id
    AND  ur.user_id   = p_user_id
    AND  ur.is_active = 1;
END$$

-- ---------------------------------------------------------------------
CREATE PROCEDURE sp_sys_user_touch_login (
  IN p_tenant_id INT,
  IN p_user_id   INT
)
BEGIN
  UPDATE sys_user
  SET    last_login_on = UTC_TIMESTAMP(),
         updated_by    = p_user_id,
         updated_on    = UTC_TIMESTAMP()
  WHERE  tenant_id = p_tenant_id
    AND  user_id   = p_user_id;
END$$

-- ---------------------------------------------------------------------
-- Layer 3 licensing. Only rows for this tenant are ever returned.
CREATE PROCEDURE sp_sys_tenant_module_list (IN p_tenant_id INT, IN p_user_id INT)
BEGIN
  SELECT module_key, is_enabled
  FROM   sys_tenant_module
  WHERE  tenant_id = p_tenant_id
    AND  is_active = 1
  ORDER  BY module_key;
END$$

-- ---------------------------------------------------------------------
CREATE PROCEDURE sp_sys_tenant_module_set (
  IN p_tenant_id  INT,
  IN p_user_id    INT,
  IN p_module_key VARCHAR(80),
  IN p_is_enabled BIT
)
BEGIN
  INSERT INTO sys_tenant_module (tenant_id, module_key, is_enabled, created_by, created_on)
  VALUES (p_tenant_id, p_module_key, p_is_enabled, p_user_id, UTC_TIMESTAMP())
  ON DUPLICATE KEY UPDATE
    is_enabled = p_is_enabled,
    is_active  = 1,
    updated_by = p_user_id,
    updated_on = UTC_TIMESTAMP();

  SELECT module_key, is_enabled
  FROM   sys_tenant_module
  WHERE  tenant_id = p_tenant_id AND module_key = p_module_key;
END$$

-- ---------------------------------------------------------------------
-- Layer 4 integration settings.
CREATE PROCEDURE sp_sys_tenant_integration_list (IN p_tenant_id INT, IN p_user_id INT)
BEGIN
  SELECT integration_key, settings_json, is_enabled
  FROM   sys_tenant_integration
  WHERE  tenant_id = p_tenant_id
    AND  is_active = 1
  ORDER  BY integration_key;
END$$

-- ---------------------------------------------------------------------
CREATE PROCEDURE sp_sys_tenant_integration_get (
  IN p_tenant_id       INT,
  IN p_user_id         INT,
  IN p_integration_key VARCHAR(80)
)
BEGIN
  SELECT integration_key, settings_json, is_enabled
  FROM   sys_tenant_integration
  WHERE  tenant_id       = p_tenant_id
    AND  integration_key = p_integration_key
    AND  is_active       = 1;
END$$

-- ---------------------------------------------------------------------
CREATE PROCEDURE sp_sys_tenant_integration_set (
  IN p_tenant_id       INT,
  IN p_user_id         INT,
  IN p_integration_key VARCHAR(80),
  IN p_settings_json   TEXT,
  IN p_is_enabled      BIT
)
BEGIN
  INSERT INTO sys_tenant_integration
        (tenant_id, integration_key, settings_json, is_enabled, created_by, created_on)
  VALUES (p_tenant_id, p_integration_key, p_settings_json, p_is_enabled, p_user_id, UTC_TIMESTAMP())
  ON DUPLICATE KEY UPDATE
    settings_json = p_settings_json,
    is_enabled    = p_is_enabled,
    is_active     = 1,
    updated_by    = p_user_id,
    updated_on    = UTC_TIMESTAMP();

  SELECT integration_key, settings_json, is_enabled
  FROM   sys_tenant_integration
  WHERE  tenant_id = p_tenant_id AND integration_key = p_integration_key;
END$$

DELIMITER ;
