-- =====================================================================
--  Demo Hospital - the full registration form
--
--  Apply after 18_patient_code_series.sql. Re-runnable.
--
--  16_patient.sql gave hr_patient the eleven columns the first registration
--  screen captured. The screen now captures sixty-five fields and a list of
--  schemes, and everything outside those eleven was reaching the API and being
--  dropped on the floor - the save succeeded, the data did not exist.
--
--  Three things happen here:
--
--    1. The fifty-one missing columns are added to hr_patient.
--    2. hr_patient_scheme is created. A patient may hold several policies, so
--       it is a child table rather than ten more columns.
--    3. The list, get and save procedures are recreated to read and write all
--       of it. The save keeps the UHID series from 18 exactly as it was.
--
--  full_name and mobile stay. They are NOT NULL, everything downstream of a
--  registration reads them, and a print template or a named query written
--  against them must not start returning blanks - so the save now maintains
--  them from first_name / last_name / mobile_no rather than being told them.
-- =====================================================================

-- No USE here on purpose: the schema this belongs to is whichever one the connection is
-- pointed at, and the earlier scripts naming a database are the reason a deployment can be
-- applied to the wrong one. Connect to the target schema, then run this.

-- ---------------------------------------------------------------------
-- 1. The columns
--
-- ALTER TABLE ... ADD COLUMN IF NOT EXISTS is MariaDB, not MySQL 8, so the
-- check is explicit against information_schema - the same shape as
-- 10_alter_employee_salary.sql.
-- ---------------------------------------------------------------------

DROP PROCEDURE IF EXISTS sp_tmp_add_patient_registration_columns;

DELIMITER $$

CREATE PROCEDURE sp_tmp_add_patient_registration_columns()
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_schema = DATABASE()
                   AND table_name   = 'hr_patient'
                   AND column_name  = 'first_name') THEN
    ALTER TABLE hr_patient
      -- Personal details
      ADD COLUMN barcode               VARCHAR(60)  NULL AFTER patient_code,
      ADD COLUMN first_name            VARCHAR(80)  NULL AFTER full_name,
      ADD COLUMN last_name             VARCHAR(80)  NULL AFTER first_name,
      ADD COLUMN title                 VARCHAR(20)  NULL AFTER last_name,
      ADD COLUMN marital_status        VARCHAR(30)  NULL AFTER gender,
      ADD COLUMN age                   INT          NULL AFTER dob,
      -- YRS / MTH / DAYS. A newborn is registered in days and the unit is half
      -- the answer, so it is stored rather than assumed.
      ADD COLUMN age_type              VARCHAR(10)  NULL AFTER age,
      ADD COLUMN mobile_no             VARCHAR(30)  NULL AFTER mobile,
      ADD COLUMN local_address         VARCHAR(250) NULL AFTER address,
      ADD COLUMN same_as_local_address BIT          NOT NULL DEFAULT 0 AFTER local_address,
      ADD COLUMN permanent_address     VARCHAR(250) NULL AFTER same_as_local_address,
      ADD COLUMN country               VARCHAR(80)  NULL AFTER city,
      ADD COLUMN state                 VARCHAR(80)  NULL AFTER country,
      ADD COLUMN district              VARCHAR(80)  NULL AFTER state,
      ADD COLUMN id_proof_name         VARCHAR(60)  NULL AFTER district,
      ADD COLUMN id_proof_no           VARCHAR(60)  NULL AFTER id_proof_name,
      ADD COLUMN kra_pin               VARCHAR(40)  NULL AFTER id_proof_no,
      ADD COLUMN family_number         VARCHAR(40)  NULL AFTER kra_pin,
      ADD COLUMN staff_id              VARCHAR(40)  NULL AFTER family_number,
      ADD COLUMN dependent_id          VARCHAR(40)  NULL AFTER staff_id,
      ADD COLUMN national_id           VARCHAR(40)  NULL AFTER dependent_id,
      ADD COLUMN pregnancy_days        INT          NULL AFTER national_id,
      -- Other details
      ADD COLUMN alt_country_code      VARCHAR(10)  NULL AFTER pregnancy_days,
      ADD COLUMN alternative_no        VARCHAR(30)  NULL AFTER alt_country_code,
      ADD COLUMN occupation            VARCHAR(60)  NULL AFTER alternative_no,
      ADD COLUMN birth_place           VARCHAR(100) NULL AFTER occupation,
      ADD COLUMN religion              VARCHAR(40)  NULL AFTER birth_place,
      ADD COLUMN emg_first_name        VARCHAR(80)  NULL AFTER religion,
      ADD COLUMN emg_last_name         VARCHAR(80)  NULL AFTER emg_first_name,
      ADD COLUMN emg_relation          VARCHAR(40)  NULL AFTER emg_last_name,
      ADD COLUMN emg_mobile_code       VARCHAR(10)  NULL AFTER emg_relation,
      ADD COLUMN emg_mobile_no         VARCHAR(30)  NULL AFTER emg_mobile_code,
      ADD COLUMN emg_resident_no       VARCHAR(30)  NULL AFTER emg_mobile_no,
      ADD COLUMN emg_address           VARCHAR(250) NULL AFTER emg_resident_no,
      ADD COLUMN is_international      VARCHAR(1)   NULL AFTER emg_address,
      ADD COLUMN nationality           VARCHAR(80)  NULL AFTER is_international,
      ADD COLUMN passport_number       VARCHAR(40)  NULL AFTER nationality,
      ADD COLUMN international_no      VARCHAR(40)  NULL AFTER passport_number,
      ADD COLUMN locality              VARCHAR(100) NULL AFTER international_no,
      ADD COLUMN membership_no         VARCHAR(60)  NULL AFTER locality,
      ADD COLUMN patient_type          VARCHAR(40)  NULL AFTER membership_no,
      ADD COLUMN source                VARCHAR(40)  NULL AFTER patient_type,
      ADD COLUMN emp_reference_id      VARCHAR(40)  NULL AFTER source,
      ADD COLUMN identity_mark         VARCHAR(120) NULL AFTER emp_reference_id,
      ADD COLUMN identity_mark_2       VARCHAR(120) NULL AFTER identity_mark,
      ADD COLUMN reference_type        VARCHAR(40)  NULL AFTER identity_mark_2,
      ADD COLUMN mlc_type              VARCHAR(40)  NULL AFTER reference_type,
      ADD COLUMN mlc_no                VARCHAR(40)  NULL AFTER mlc_type,
      ADD COLUMN relation_of           VARCHAR(40)  NULL AFTER mlc_no,
      ADD COLUMN relation_name         VARCHAR(120) NULL AFTER relation_of,
      ADD COLUMN relation_phone        VARCHAR(30)  NULL AFTER relation_name;

    -- A desk searches by barcode as often as by phone once barcodes are in use.
    CREATE INDEX ix_hr_patient_barcode ON hr_patient (tenant_id, barcode);
    CREATE INDEX ix_hr_patient_last_name ON hr_patient (tenant_id, last_name);
  END IF;
END$$

DELIMITER ;

CALL sp_tmp_add_patient_registration_columns();
DROP PROCEDURE sp_tmp_add_patient_registration_columns;

-- ---------------------------------------------------------------------
-- Backfill, so a patient registered before today opens on the new form with
-- their name and number in the new controls rather than in blank ones.
--
-- The split is on the first space: "Blaze Calderon" is first Blaze, last
-- Calderon, and a single-word name keeps the whole of it as the first name.
-- ---------------------------------------------------------------------

UPDATE hr_patient
SET    first_name = TRIM(SUBSTRING_INDEX(full_name, ' ', 1)),
       last_name  = TRIM(CASE WHEN LOCATE(' ', full_name) > 0
                              THEN SUBSTRING(full_name, LOCATE(' ', full_name) + 1)
                              ELSE '' END)
WHERE  IFNULL(first_name, '') = '' AND IFNULL(full_name, '') <> '';

UPDATE hr_patient SET mobile_no     = mobile  WHERE IFNULL(mobile_no, '') = '';
UPDATE hr_patient SET local_address = address WHERE IFNULL(local_address, '') = '' AND address IS NOT NULL;

-- ---------------------------------------------------------------------
-- 2. Schemes
--
-- One row per policy the patient is covered by. Owned by the patient: the save
-- replaces the whole set, which is what the screen edits, and the rows go when
-- the patient is deactivated only in the sense that nothing reads them again -
-- they are kept, because a claim already made against one has to stay
-- explicable.
-- ---------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS hr_patient_scheme (
  scheme_id       INT AUTO_INCREMENT PRIMARY KEY,
  tenant_id       INT           NOT NULL,
  patient_id      INT           NOT NULL,
  seq_no          INT           NOT NULL DEFAULT 1,
  insurance_group VARCHAR(60)   NULL,
  insurance       VARCHAR(80)   NULL,
  panel           VARCHAR(60)   NULL,
  policy_no       VARCHAR(60)   NULL,
  policy_card_no  VARCHAR(60)   NULL,
  name_on_card    VARCHAR(120)  NULL,
  expire_date     DATE          NULL,
  card_holder     VARCHAR(120)  NULL,
  approval_amount DECIMAL(14,2) NULL,
  approval_remark VARCHAR(250)  NULL,
  created_by      INT           NULL,
  created_on      DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
  KEY ix_hr_patient_scheme (tenant_id, patient_id, seq_no)
) ENGINE=InnoDB;

-- ---------------------------------------------------------------------
-- 3. The procedures
-- ---------------------------------------------------------------------

DROP PROCEDURE IF EXISTS sp_hr_patient_list;
DROP PROCEDURE IF EXISTS sp_hr_patient_get;
DROP PROCEDURE IF EXISTS sp_hr_patient_save;

DELIMITER $$

-- Page first, total second: RepositoryBase.QueryPagedAsync reads both result sets.
--
-- The list carries what the list screen shows and no more. A patient's seventy
-- fields are read one patient at a time, by sp_hr_patient_get.
CREATE PROCEDURE sp_hr_patient_list (
  IN p_tenant_id INT,
  IN p_user_id   INT,
  IN p_search    VARCHAR(200),
  IN p_page_size INT,
  IN p_offset    INT
)
BEGIN
  SELECT patient_id, patient_code, barcode, full_name, first_name, last_name, title,
         gender, marital_status, dob, age, age_type, mobile, mobile_no, email,
         city, patient_type, registered_on, is_active
  FROM   hr_patient
  WHERE  tenant_id = p_tenant_id
    AND  is_active = 1
    AND  (p_search IS NULL OR patient_code LIKE CONCAT('%', p_search, '%')
                           OR barcode      LIKE CONCAT('%', p_search, '%')
                           OR full_name    LIKE CONCAT('%', p_search, '%')
                           OR first_name   LIKE CONCAT('%', p_search, '%')
                           OR last_name    LIKE CONCAT('%', p_search, '%')
                           OR mobile       LIKE CONCAT('%', p_search, '%')
                           OR mobile_no    LIKE CONCAT('%', p_search, '%')
                           OR email        LIKE CONCAT('%', p_search, '%'))
  ORDER  BY patient_id DESC
  LIMIT  p_page_size OFFSET p_offset;

  SELECT COUNT(*) AS total_count
  FROM   hr_patient
  WHERE  tenant_id = p_tenant_id
    AND  is_active = 1
    AND  (p_search IS NULL OR patient_code LIKE CONCAT('%', p_search, '%')
                           OR barcode      LIKE CONCAT('%', p_search, '%')
                           OR full_name    LIKE CONCAT('%', p_search, '%')
                           OR first_name   LIKE CONCAT('%', p_search, '%')
                           OR last_name    LIKE CONCAT('%', p_search, '%')
                           OR mobile       LIKE CONCAT('%', p_search, '%')
                           OR mobile_no    LIKE CONCAT('%', p_search, '%')
                           OR email        LIKE CONCAT('%', p_search, '%'));
END$$

-- The patient, then their schemes. Two result sets in one round trip, so the
-- form cannot be handed a patient and somebody else's policies.
CREATE PROCEDURE sp_hr_patient_get (
  IN p_tenant_id  INT,
  IN p_user_id    INT,
  IN p_patient_id INT
)
BEGIN
  SELECT patient_id, patient_code, barcode, full_name, first_name, last_name, title,
         gender, marital_status, dob, age, age_type, mobile, mobile_no, email, blood_group,
         address, local_address, same_as_local_address, permanent_address,
         city, country, state, district,
         id_proof_name, id_proof_no, kra_pin, family_number, staff_id, dependent_id,
         national_id, pregnancy_days,
         alt_country_code, alternative_no, occupation, birth_place, religion,
         emg_first_name, emg_last_name, emg_relation, emg_mobile_code, emg_mobile_no,
         emg_resident_no, emg_address, is_international, nationality, passport_number,
         international_no, locality, membership_no, patient_type, source, emp_reference_id,
         identity_mark, identity_mark_2, reference_type, mlc_type, mlc_no,
         relation_of, relation_name, relation_phone,
         registered_on, is_active
  FROM   hr_patient
  WHERE  tenant_id  = p_tenant_id
    AND  patient_id = p_patient_id
    AND  is_active  = 1;

  SELECT scheme_id, patient_id, seq_no, insurance_group, insurance, panel, policy_no,
         policy_card_no, name_on_card, expire_date, card_holder, approval_amount,
         approval_remark
  FROM   hr_patient_scheme
  WHERE  tenant_id  = p_tenant_id
    AND  patient_id = p_patient_id
  ORDER  BY seq_no, scheme_id;
END$$

-- =====================================================================
-- Save.
--
-- The UHID logic is 18_patient_code_series.sql's, unchanged: blank means
-- "issue the next one", and it is issued in here rather than by a separate
-- call, so no number is handed out for a row that never arrives.
--
-- full_name and mobile are derived rather than accepted. They are the columns
-- everything downstream still reads, and letting a caller send a full_name
-- that disagrees with first_name + last_name would make the two names for one
-- patient drift apart silently.
--
-- Schemes arrive as a JSON array and replace the set wholesale, because that
-- is what the screen edits: a row the user removed is gone, and one they added
-- is new. scheme_id is therefore not stable across a save, and nothing refers
-- to it - a claim refers to the policy number.
-- =====================================================================
CREATE PROCEDURE sp_hr_patient_save (
  IN p_tenant_id             INT,
  IN p_user_id               INT,
  IN p_patient_id            INT,
  IN p_patient_code          VARCHAR(40),
  IN p_barcode               VARCHAR(60),
  IN p_first_name            VARCHAR(80),
  IN p_last_name             VARCHAR(80),
  IN p_title                 VARCHAR(20),
  IN p_gender                VARCHAR(20),
  IN p_marital_status        VARCHAR(30),
  IN p_dob                   DATE,
  IN p_age                   INT,
  IN p_age_type              VARCHAR(10),
  IN p_mobile_no             VARCHAR(30),
  IN p_email                 VARCHAR(150),
  IN p_blood_group           VARCHAR(10),
  IN p_local_address         VARCHAR(250),
  IN p_same_as_local_address TINYINT,
  IN p_permanent_address     VARCHAR(250),
  IN p_city                  VARCHAR(100),
  IN p_country               VARCHAR(80),
  IN p_state                 VARCHAR(80),
  IN p_district              VARCHAR(80),
  IN p_id_proof_name         VARCHAR(60),
  IN p_id_proof_no           VARCHAR(60),
  IN p_kra_pin               VARCHAR(40),
  IN p_family_number         VARCHAR(40),
  IN p_staff_id              VARCHAR(40),
  IN p_dependent_id          VARCHAR(40),
  IN p_national_id           VARCHAR(40),
  IN p_pregnancy_days        INT,
  IN p_alt_country_code      VARCHAR(10),
  IN p_alternative_no        VARCHAR(30),
  IN p_occupation            VARCHAR(60),
  IN p_birth_place           VARCHAR(100),
  IN p_religion              VARCHAR(40),
  IN p_emg_first_name        VARCHAR(80),
  IN p_emg_last_name         VARCHAR(80),
  IN p_emg_relation          VARCHAR(40),
  IN p_emg_mobile_code       VARCHAR(10),
  IN p_emg_mobile_no         VARCHAR(30),
  IN p_emg_resident_no       VARCHAR(30),
  IN p_emg_address           VARCHAR(250),
  IN p_is_international      VARCHAR(1),
  IN p_nationality           VARCHAR(80),
  IN p_passport_number       VARCHAR(40),
  IN p_international_no      VARCHAR(40),
  IN p_locality              VARCHAR(100),
  IN p_membership_no         VARCHAR(60),
  IN p_patient_type          VARCHAR(40),
  IN p_source                VARCHAR(40),
  IN p_emp_reference_id      VARCHAR(40),
  IN p_identity_mark         VARCHAR(120),
  IN p_identity_mark_2       VARCHAR(120),
  IN p_reference_type        VARCHAR(40),
  IN p_mlc_type              VARCHAR(40),
  IN p_mlc_no                VARCHAR(40),
  IN p_relation_of           VARCHAR(40),
  IN p_relation_name         VARCHAR(120),
  IN p_relation_phone        VARCHAR(30),
  IN p_registered_on         DATE,
  IN p_schemes               JSON
)
BEGIN
  DECLARE v_id        INT;
  DECLARE v_code      VARCHAR(40);
  DECLARE v_taken     INT DEFAULT 0;
  DECLARE v_tries     INT DEFAULT 0;
  DECLARE v_full_name VARCHAR(180);

  SET v_full_name = TRIM(CONCAT(IFNULL(p_first_name, ''), ' ', IFNULL(p_last_name, '')));

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
    INSERT INTO hr_patient (
      tenant_id, patient_code, barcode, full_name, first_name, last_name, title,
      gender, marital_status, dob, age, age_type, mobile, mobile_no, email, blood_group,
      address, local_address, same_as_local_address, permanent_address,
      city, country, state, district,
      id_proof_name, id_proof_no, kra_pin, family_number, staff_id, dependent_id,
      national_id, pregnancy_days,
      alt_country_code, alternative_no, occupation, birth_place, religion,
      emg_first_name, emg_last_name, emg_relation, emg_mobile_code, emg_mobile_no,
      emg_resident_no, emg_address, is_international, nationality, passport_number,
      international_no, locality, membership_no, patient_type, source, emp_reference_id,
      identity_mark, identity_mark_2, reference_type, mlc_type, mlc_no,
      relation_of, relation_name, relation_phone,
      registered_on, created_by, created_on)
    VALUES (
      p_tenant_id, p_patient_code, p_barcode, v_full_name, p_first_name, p_last_name, p_title,
      p_gender, p_marital_status, p_dob, p_age, p_age_type, p_mobile_no, p_mobile_no, p_email, p_blood_group,
      p_local_address, p_local_address, IFNULL(p_same_as_local_address, 0), p_permanent_address,
      p_city, p_country, p_state, p_district,
      p_id_proof_name, p_id_proof_no, p_kra_pin, p_family_number, p_staff_id, p_dependent_id,
      p_national_id, p_pregnancy_days,
      p_alt_country_code, p_alternative_no, p_occupation, p_birth_place, p_religion,
      p_emg_first_name, p_emg_last_name, p_emg_relation, p_emg_mobile_code, p_emg_mobile_no,
      p_emg_resident_no, p_emg_address, p_is_international, p_nationality, p_passport_number,
      p_international_no, p_locality, p_membership_no, p_patient_type, p_source, p_emp_reference_id,
      p_identity_mark, p_identity_mark_2, p_reference_type, p_mlc_type, p_mlc_no,
      p_relation_of, p_relation_name, p_relation_phone,
      IFNULL(p_registered_on, CURRENT_DATE()), p_user_id, UTC_TIMESTAMP());

    SET v_id = LAST_INSERT_ID();
  ELSE
    UPDATE hr_patient
    SET    patient_code          = p_patient_code,
           barcode               = p_barcode,
           full_name             = v_full_name,
           first_name            = p_first_name,
           last_name             = p_last_name,
           title                 = p_title,
           gender                = p_gender,
           marital_status        = p_marital_status,
           dob                   = p_dob,
           age                   = p_age,
           age_type              = p_age_type,
           mobile                = p_mobile_no,
           mobile_no             = p_mobile_no,
           email                 = p_email,
           blood_group           = p_blood_group,
           address               = p_local_address,
           local_address         = p_local_address,
           same_as_local_address = IFNULL(p_same_as_local_address, 0),
           permanent_address     = p_permanent_address,
           city                  = p_city,
           country               = p_country,
           state                 = p_state,
           district              = p_district,
           id_proof_name         = p_id_proof_name,
           id_proof_no           = p_id_proof_no,
           kra_pin               = p_kra_pin,
           family_number         = p_family_number,
           staff_id              = p_staff_id,
           dependent_id          = p_dependent_id,
           national_id           = p_national_id,
           pregnancy_days        = p_pregnancy_days,
           alt_country_code      = p_alt_country_code,
           alternative_no        = p_alternative_no,
           occupation            = p_occupation,
           birth_place           = p_birth_place,
           religion              = p_religion,
           emg_first_name        = p_emg_first_name,
           emg_last_name         = p_emg_last_name,
           emg_relation          = p_emg_relation,
           emg_mobile_code       = p_emg_mobile_code,
           emg_mobile_no         = p_emg_mobile_no,
           emg_resident_no       = p_emg_resident_no,
           emg_address           = p_emg_address,
           is_international      = p_is_international,
           nationality           = p_nationality,
           passport_number       = p_passport_number,
           international_no      = p_international_no,
           locality              = p_locality,
           membership_no         = p_membership_no,
           patient_type          = p_patient_type,
           source                = p_source,
           emp_reference_id      = p_emp_reference_id,
           identity_mark         = p_identity_mark,
           identity_mark_2       = p_identity_mark_2,
           reference_type        = p_reference_type,
           mlc_type              = p_mlc_type,
           mlc_no                = p_mlc_no,
           relation_of           = p_relation_of,
           relation_name         = p_relation_name,
           relation_phone        = p_relation_phone,
           registered_on         = p_registered_on,
           updated_by            = p_user_id,
           updated_on            = UTC_TIMESTAMP()
    WHERE  tenant_id  = p_tenant_id
      AND  patient_id = p_patient_id;

    SET v_id = p_patient_id;
  END IF;

  -- Schemes: replace the set. A NULL array means "the caller is not editing
  -- schemes", which is not the same as an empty one meaning "there are none",
  -- so only a real array touches the table.
  IF p_schemes IS NOT NULL AND JSON_VALID(p_schemes) THEN
    DELETE FROM hr_patient_scheme WHERE tenant_id = p_tenant_id AND patient_id = v_id;

    IF JSON_LENGTH(p_schemes) > 0 THEN
      INSERT INTO hr_patient_scheme (
        tenant_id, patient_id, seq_no, insurance_group, insurance, panel, policy_no,
        policy_card_no, name_on_card, expire_date, card_holder, approval_amount,
        approval_remark, created_by, created_on)
      SELECT p_tenant_id, v_id, j.seq_no, j.insurance_group, j.insurance, j.panel, j.policy_no,
             j.policy_card_no, j.name_on_card, j.expire_date, j.card_holder, j.approval_amount,
             j.approval_remark, p_user_id, UTC_TIMESTAMP()
      FROM   JSON_TABLE(p_schemes, '$[*]' COLUMNS (
               seq_no          FOR ORDINALITY,
               insurance_group VARCHAR(60)   PATH '$.insuranceGroup',
               insurance       VARCHAR(80)   PATH '$.insurance',
               panel           VARCHAR(60)   PATH '$.panel',
               policy_no       VARCHAR(60)   PATH '$.policyNo',
               policy_card_no  VARCHAR(60)   PATH '$.policyCardNo',
               name_on_card    VARCHAR(120)  PATH '$.nameOnCard',
               expire_date     DATE          PATH '$.expireDate',
               card_holder     VARCHAR(120)  PATH '$.cardHolder',
               approval_amount DECIMAL(14,2) PATH '$.approvalAmount',
               approval_remark VARCHAR(250)  PATH '$.approvalRemark'
             )) AS j;
    END IF;
  END IF;

  SELECT patient_id, patient_code, barcode, full_name, first_name, last_name, title,
         gender, marital_status, dob, age, age_type, mobile, mobile_no, email, blood_group,
         address, local_address, same_as_local_address, permanent_address,
         city, country, state, district,
         id_proof_name, id_proof_no, kra_pin, family_number, staff_id, dependent_id,
         national_id, pregnancy_days,
         alt_country_code, alternative_no, occupation, birth_place, religion,
         emg_first_name, emg_last_name, emg_relation, emg_mobile_code, emg_mobile_no,
         emg_resident_no, emg_address, is_international, nationality, passport_number,
         international_no, locality, membership_no, patient_type, source, emp_reference_id,
         identity_mark, identity_mark_2, reference_type, mlc_type, mlc_no,
         relation_of, relation_name, relation_phone,
         registered_on, is_active
  FROM   hr_patient
  WHERE  tenant_id = p_tenant_id AND patient_id = v_id;

  -- The saved schemes are not returned here. ExecuteReturningAsync reads one result set, and
  -- the repository re-reads the patient through sp_hr_patient_get afterwards, which returns
  -- both — one shape for "a patient with their schemes" rather than two.
END$$

DELIMITER ;

-- ---------------------------------------------------------------------
-- What was added, for the person running this.
-- ---------------------------------------------------------------------

SELECT COUNT(*) AS patient_columns
FROM   information_schema.columns
WHERE  table_schema = DATABASE() AND table_name = 'hr_patient';

SELECT COUNT(*) AS scheme_columns
FROM   information_schema.columns
WHERE  table_schema = DATABASE() AND table_name = 'hr_patient_scheme';
