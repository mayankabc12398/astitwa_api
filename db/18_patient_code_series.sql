-- =====================================================================
--  Demo Hospital - the UHID is issued by the server
--
--  Apply after 17_ai_thread.sql. Re-runnable.
--
--  A registration desk should not be inventing identifiers. Leaving the UHID
--  blank now means "give me the next one", and the number comes from a counter
--  row rather than from MAX(patient_code) + 1 - two desks registering at the
--  same moment both read the same MAX and both get the same number, and the
--  unique index turns that into an error somebody has to retype.
--
--  The counter is generic on purpose. A series key is free text, so the next
--  thing that needs numbering (a visit, an invoice) adds a row rather than a
--  table.
-- =====================================================================

CREATE TABLE IF NOT EXISTS sys_number_series (
  series_id  INT AUTO_INCREMENT PRIMARY KEY,
  tenant_id  INT         NOT NULL,
  -- 'hr.patient.code'. What is being numbered, not what the number looks like.
  series_key VARCHAR(60) NOT NULL,
  prefix     VARCHAR(20) NOT NULL DEFAULT '',
  pad_width  INT         NOT NULL DEFAULT 4,
  next_no    INT         NOT NULL DEFAULT 1,
  updated_on DATETIME    NULL,
  UNIQUE KEY uk_sys_number_series (tenant_id, series_key)
) ENGINE=InnoDB;

DROP PROCEDURE IF EXISTS sp_sys_number_series_next;
DROP PROCEDURE IF EXISTS sp_hr_patient_save;

DELIMITER $$

-- =====================================================================
-- One number, handed out once.
--
-- OUT rather than SELECT: this is called from inside another procedure, and a
-- SELECT here would send a second result set back to the client, which is not
-- what the caller of sp_hr_patient_save is reading.
--
-- The UPDATE is the whole trick. LAST_INSERT_ID(next_no) stores the CURRENT
-- value in the session and returns it, and the + 1 is written back - so the
-- read and the increment are one statement holding one row lock. Two
-- connections cannot come away with the same number.
-- =====================================================================
CREATE PROCEDURE sp_sys_number_series_next (
  IN  p_tenant_id  INT,
  IN  p_user_id    INT,
  IN  p_series_key VARCHAR(60),
  OUT p_code       VARCHAR(40)
)
BEGIN
  DECLARE v_prefix VARCHAR(20);
  DECLARE v_width  INT;
  DECLARE v_number INT;

  -- A tenant created after this migration ran still gets a series the first
  -- time it asks for a number.
  INSERT IGNORE INTO sys_number_series (tenant_id, series_key, prefix, pad_width, next_no, updated_on)
  VALUES (p_tenant_id, p_series_key, '', 4, 1, UTC_TIMESTAMP());

  UPDATE sys_number_series
  SET    next_no    = LAST_INSERT_ID(next_no) + 1,
         updated_on = UTC_TIMESTAMP()
  WHERE  tenant_id  = p_tenant_id
    AND  series_key = p_series_key;

  SET v_number = LAST_INSERT_ID();

  SELECT prefix, pad_width INTO v_prefix, v_width
  FROM   sys_number_series
  WHERE  tenant_id = p_tenant_id AND series_key = p_series_key;

  SET p_code = CONCAT(v_prefix, LPAD(v_number, IFNULL(v_width, 4), '0'));
END$$

-- =====================================================================
-- Save, with the UHID issued here when the caller left it blank.
--
-- Generated inside the save rather than by a separate call from the API, so
-- there is no window in which a number has been handed out and the row that
-- was meant to carry it never arrives.
-- =====================================================================
CREATE PROCEDURE sp_hr_patient_save (
  IN p_tenant_id     INT,
  IN p_user_id       INT,
  IN p_patient_id    INT,
  IN p_patient_code  VARCHAR(40),
  IN p_full_name     VARCHAR(180),
  IN p_gender        VARCHAR(20),
  IN p_dob           DATE,
  IN p_mobile        VARCHAR(30),
  IN p_email         VARCHAR(150),
  IN p_blood_group   VARCHAR(10),
  IN p_address       VARCHAR(250),
  IN p_city          VARCHAR(100),
  IN p_registered_on DATE
)
BEGIN
  DECLARE v_id     INT;
  DECLARE v_code   VARCHAR(40);
  DECLARE v_taken  INT DEFAULT 0;
  DECLARE v_tries  INT DEFAULT 0;

  IF IFNULL(p_patient_id, 0) = 0 AND IFNULL(p_patient_code, '') = '' THEN
    -- A number the counter has never issued can still be taken, because a UHID
    -- may also be typed by hand. Skip past those rather than failing the save;
    -- the loop is bounded so a series that has fallen far behind cannot spin.
    REPEAT
      CALL sp_sys_number_series_next(p_tenant_id, p_user_id, 'hr.patient.code', v_code);
      SET v_tries = v_tries + 1;

      SELECT COUNT(*) INTO v_taken
      FROM   hr_patient
      WHERE  tenant_id = p_tenant_id AND patient_code = v_code;
    UNTIL v_taken = 0 OR v_tries >= 50
    END REPEAT;

    SET p_patient_code = v_code;
  END IF;

  IF IFNULL(p_patient_id, 0) = 0 THEN
    INSERT INTO hr_patient (tenant_id, patient_code, full_name, gender, dob, mobile, email,
                            blood_group, address, city, registered_on, created_by, created_on)
    VALUES (p_tenant_id, p_patient_code, p_full_name, p_gender, p_dob, p_mobile, p_email,
            p_blood_group, p_address, p_city, IFNULL(p_registered_on, CURRENT_DATE()),
            p_user_id, UTC_TIMESTAMP());
    SET v_id = LAST_INSERT_ID();
  ELSE
    UPDATE hr_patient
    SET    patient_code  = p_patient_code,
           full_name     = p_full_name,
           gender        = p_gender,
           dob           = p_dob,
           mobile        = p_mobile,
           email         = p_email,
           blood_group   = p_blood_group,
           address       = p_address,
           city          = p_city,
           registered_on = p_registered_on,
           updated_by    = p_user_id,
           updated_on    = UTC_TIMESTAMP()
    WHERE  tenant_id  = p_tenant_id
      AND  patient_id = p_patient_id;
    SET v_id = p_patient_id;
  END IF;

  SELECT patient_id, patient_code, full_name, gender, dob, mobile, email,
         blood_group, address, city, registered_on, is_active
  FROM   hr_patient
  WHERE  tenant_id = p_tenant_id AND patient_id = v_id;
END$$

DELIMITER ;

-- ---------------------------------------------------------------------
-- Seed one series per tenant, starting above whatever is already registered.
--
-- Read from the existing codes rather than started at 1: a database with
-- P-1001 in it would otherwise hand out P-0001 and then collide its way
-- forward fifty times before issuing anything.
-- ---------------------------------------------------------------------

INSERT INTO sys_number_series (tenant_id, series_key, prefix, pad_width, next_no, updated_on)
SELECT t.tenant_id,
       'hr.patient.code',
       'P-',
       4,
       IFNULL(MAX(CAST(REGEXP_SUBSTR(p.patient_code, '[0-9]+$') AS UNSIGNED)), 1000) + 1,
       UTC_TIMESTAMP()
FROM   sys_tenant t
LEFT   JOIN hr_patient p
       ON p.tenant_id = t.tenant_id
      AND p.patient_code REGEXP '^P-[0-9]+$'
GROUP  BY t.tenant_id
ON DUPLICATE KEY UPDATE series_key = sys_number_series.series_key;
