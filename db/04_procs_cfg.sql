-- =====================================================================
--  HrSuite - layer 2 configuration procedures
--  These are the only way cfg_setting and cfg_field_rule are ever read or written.
-- =====================================================================

USE astitwa;

DROP PROCEDURE IF EXISTS sp_cfg_setting_list;
DROP PROCEDURE IF EXISTS sp_cfg_setting_get;
DROP PROCEDURE IF EXISTS sp_cfg_setting_save;
DROP PROCEDURE IF EXISTS sp_cfg_field_rule_list;
DROP PROCEDURE IF EXISTS sp_cfg_field_rule_list_by_screen;
DROP PROCEDURE IF EXISTS sp_cfg_field_rule_save;

DELIMITER $$

-- ---------------------------------------------------------------------
CREATE PROCEDURE sp_cfg_setting_list (IN p_tenant_id INT, IN p_user_id INT)
BEGIN
  SELECT setting_key, setting_value, data_type
  FROM   cfg_setting
  WHERE  tenant_id = p_tenant_id
    AND  is_active = 1
  ORDER  BY setting_key;
END$$

-- ---------------------------------------------------------------------
CREATE PROCEDURE sp_cfg_setting_get (
  IN p_tenant_id   INT,
  IN p_user_id     INT,
  IN p_setting_key VARCHAR(120)
)
BEGIN
  SELECT setting_key, setting_value, data_type
  FROM   cfg_setting
  WHERE  tenant_id   = p_tenant_id
    AND  setting_key = p_setting_key
    AND  is_active   = 1;
END$$

-- ---------------------------------------------------------------------
CREATE PROCEDURE sp_cfg_setting_save (
  IN p_tenant_id     INT,
  IN p_user_id       INT,
  IN p_setting_key   VARCHAR(120),
  IN p_setting_value TEXT,
  IN p_data_type     VARCHAR(20)
)
BEGIN
  INSERT INTO cfg_setting (tenant_id, setting_key, setting_value, data_type, created_by, created_on)
  VALUES (p_tenant_id, p_setting_key, p_setting_value, IFNULL(p_data_type, 'string'), p_user_id, UTC_TIMESTAMP())
  ON DUPLICATE KEY UPDATE
    setting_value = p_setting_value,
    data_type     = IFNULL(p_data_type, data_type),
    is_active     = 1,
    updated_by    = p_user_id,
    updated_on    = UTC_TIMESTAMP();

  SELECT setting_key, setting_value, data_type
  FROM   cfg_setting
  WHERE  tenant_id = p_tenant_id AND setting_key = p_setting_key;
END$$

-- ---------------------------------------------------------------------
-- Every field rule for the tenant, in one call. The client caches the lot at start-up.
CREATE PROCEDURE sp_cfg_field_rule_list (IN p_tenant_id INT, IN p_user_id INT)
BEGIN
  SELECT screen_key, field_key, is_visible, is_required, label, seq_no
  FROM   cfg_field_rule
  WHERE  tenant_id = p_tenant_id
    AND  is_active = 1
  ORDER  BY screen_key, seq_no, field_key;
END$$

-- ---------------------------------------------------------------------
CREATE PROCEDURE sp_cfg_field_rule_list_by_screen (
  IN p_tenant_id  INT,
  IN p_user_id    INT,
  IN p_screen_key VARCHAR(100)
)
BEGIN
  SELECT screen_key, field_key, is_visible, is_required, label, seq_no
  FROM   cfg_field_rule
  WHERE  tenant_id  = p_tenant_id
    AND  screen_key = p_screen_key
    AND  is_active  = 1
  ORDER  BY seq_no, field_key;
END$$

-- ---------------------------------------------------------------------
CREATE PROCEDURE sp_cfg_field_rule_save (
  IN p_tenant_id   INT,
  IN p_user_id     INT,
  IN p_screen_key  VARCHAR(100),
  IN p_field_key   VARCHAR(100),
  IN p_is_visible  BIT,
  IN p_is_required BIT,
  IN p_label       VARCHAR(150),
  IN p_seq_no      INT
)
BEGIN
  INSERT INTO cfg_field_rule
        (tenant_id, screen_key, field_key, is_visible, is_required, label, seq_no, created_by, created_on)
  VALUES (p_tenant_id, p_screen_key, p_field_key, p_is_visible, p_is_required, p_label,
          IFNULL(p_seq_no, 10), p_user_id, UTC_TIMESTAMP())
  ON DUPLICATE KEY UPDATE
    is_visible  = p_is_visible,
    is_required = p_is_required,
    label       = p_label,
    seq_no      = IFNULL(p_seq_no, seq_no),
    is_active   = 1,
    updated_by  = p_user_id,
    updated_on  = UTC_TIMESTAMP();

  SELECT screen_key, field_key, is_visible, is_required, label, seq_no
  FROM   cfg_field_rule
  WHERE  tenant_id = p_tenant_id AND screen_key = p_screen_key AND field_key = p_field_key;
END$$

DELIMITER ;
