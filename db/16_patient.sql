-- =====================================================================
--  Demo Hospital - patient registration
--
--  Apply after 15_custom_api.sql. Re-runnable.
--
--  The same shape as hr_department and hr_employee, deliberately: one table
--  scoped by tenant_id, five procedures behind it, and nothing the API can
--  reach except through them. A patient is master data like an employee is,
--  so it gets soft delete rather than DELETE - a record somebody has already
--  billed against must not be able to disappear.
--
--  The permission rows at the bottom are what make the menu entry appear.
--  Without them the screen is deployed and invisible, which is the correct
--  order: code first, then the tenant decides who may see it.
-- =====================================================================

CREATE TABLE IF NOT EXISTS hr_patient (
  patient_id    INT AUTO_INCREMENT PRIMARY KEY,
  tenant_id     INT          NOT NULL,
  patient_code  VARCHAR(40)  NOT NULL,
  full_name     VARCHAR(180) NOT NULL,
  gender        VARCHAR(20)  NULL,
  dob           DATE         NULL,
  mobile        VARCHAR(30)  NOT NULL,
  email         VARCHAR(150) NULL,
  blood_group   VARCHAR(10)  NULL,
  address       VARCHAR(250) NULL,
  city          VARCHAR(100) NULL,
  registered_on DATE         NULL,
  is_active     BIT          NOT NULL DEFAULT 1,
  created_by    INT          NULL,
  created_on    DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_by    INT          NULL,
  updated_on    DATETIME     NULL,
  -- The UHID is unique inside a tenant and nowhere else. Two hospitals on the
  -- same instance may both use P-0001 and neither is wrong.
  UNIQUE KEY uk_hr_patient (tenant_id, patient_code),
  KEY ix_hr_patient_name (tenant_id, full_name),
  -- Registration desks search by phone far more often than by name.
  KEY ix_hr_patient_mobile (tenant_id, mobile)
) ENGINE=InnoDB;

DROP PROCEDURE IF EXISTS sp_hr_patient_list;
DROP PROCEDURE IF EXISTS sp_hr_patient_get;
DROP PROCEDURE IF EXISTS sp_hr_patient_save;
DROP PROCEDURE IF EXISTS sp_hr_patient_delete;
DROP PROCEDURE IF EXISTS sp_hr_patient_lookup;
DROP PROCEDURE IF EXISTS sp_hr_patient_code_exists;

DELIMITER $$

-- Page first, total second: RepositoryBase.QueryPagedAsync reads both result sets.
CREATE PROCEDURE sp_hr_patient_list (
  IN p_tenant_id INT,
  IN p_user_id   INT,
  IN p_search    VARCHAR(200),
  IN p_page_size INT,
  IN p_offset    INT
)
BEGIN
  SELECT patient_id, patient_code, full_name, gender, dob, mobile, email,
         blood_group, address, city, registered_on, is_active
  FROM   hr_patient
  WHERE  tenant_id = p_tenant_id
    AND  is_active = 1
    AND  (p_search IS NULL OR patient_code LIKE CONCAT('%', p_search, '%')
                           OR full_name    LIKE CONCAT('%', p_search, '%')
                           OR mobile       LIKE CONCAT('%', p_search, '%')
                           OR email        LIKE CONCAT('%', p_search, '%'))
  ORDER  BY patient_id DESC
  LIMIT  p_page_size OFFSET p_offset;

  SELECT COUNT(*) AS total_count
  FROM   hr_patient
  WHERE  tenant_id = p_tenant_id
    AND  is_active = 1
    AND  (p_search IS NULL OR patient_code LIKE CONCAT('%', p_search, '%')
                           OR full_name    LIKE CONCAT('%', p_search, '%')
                           OR mobile       LIKE CONCAT('%', p_search, '%')
                           OR email        LIKE CONCAT('%', p_search, '%'));
END$$

CREATE PROCEDURE sp_hr_patient_get (
  IN p_tenant_id  INT,
  IN p_user_id    INT,
  IN p_patient_id INT
)
BEGIN
  SELECT patient_id, patient_code, full_name, gender, dob, mobile, email,
         blood_group, address, city, registered_on, is_active
  FROM   hr_patient
  WHERE  tenant_id  = p_tenant_id
    AND  patient_id = p_patient_id
    AND  is_active  = 1;
END$$

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
  DECLARE v_id INT;

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

-- Soft delete. A registration is referred to by everything downstream of it.
CREATE PROCEDURE sp_hr_patient_delete (
  IN p_tenant_id  INT,
  IN p_user_id    INT,
  IN p_patient_id INT
)
BEGIN
  UPDATE hr_patient
  SET    is_active  = 0,
         updated_by = p_user_id,
         updated_on = UTC_TIMESTAMP()
  WHERE  tenant_id  = p_tenant_id
    AND  patient_id = p_patient_id;
END$$

CREATE PROCEDURE sp_hr_patient_lookup (
  IN p_tenant_id INT,
  IN p_user_id   INT
)
BEGIN
  SELECT patient_id AS id, CONCAT(patient_code, ' - ', full_name) AS label
  FROM   hr_patient
  WHERE  tenant_id = p_tenant_id AND is_active = 1
  ORDER  BY full_name
  LIMIT  500;
END$$

-- Asked before the insert so the user is told on the field rather than by a
-- duplicate-key error. The unique index is still what decides.
CREATE PROCEDURE sp_hr_patient_code_exists (
  IN p_tenant_id    INT,
  IN p_user_id      INT,
  IN p_patient_code VARCHAR(40),
  IN p_patient_id   INT
)
BEGIN
  SELECT COUNT(*) AS found
  FROM   hr_patient
  WHERE  tenant_id    = p_tenant_id
    AND  patient_code = p_patient_code
    AND  patient_id  <> IFNULL(p_patient_id, 0);
END$$

DELIMITER ;

-- ---------------------------------------------------------------------
-- Who may see it.
--
-- Every tenant on this instance, and every role that already holds the
-- equivalent employee permission - a desk that may register an employee may
-- register a patient. Guarded by NOT EXISTS, so re-running changes nothing.
-- ---------------------------------------------------------------------

INSERT INTO sys_role_permission (tenant_id, role_id, permission_key, created_by, created_on)
SELECT r.tenant_id, r.role_id, p.permission_key, 1, UTC_TIMESTAMP()
FROM   sys_role r
JOIN (
  SELECT 'ADMIN'   AS role_code, 'hr.patient.view' AS permission_key
  UNION ALL SELECT 'ADMIN',   'hr.patient.edit'
  UNION ALL SELECT 'HR',      'hr.patient.view'
  UNION ALL SELECT 'HR',      'hr.patient.edit'
  UNION ALL SELECT 'MANAGER', 'hr.patient.view'
) p ON p.role_code = r.role_code
WHERE NOT EXISTS (
  SELECT 1 FROM sys_role_permission x
  WHERE x.tenant_id = r.tenant_id AND x.role_id = r.role_id AND x.permission_key = p.permission_key
);
