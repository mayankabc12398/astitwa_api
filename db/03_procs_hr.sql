-- =====================================================================
--  HrSuite - layer 1 HR procedures
--
--  Paging convention: a *_list procedure returns the page as its first result set and a
--  single total-count scalar as its second. RepositoryBase.QueryPagedAsync reads both.
--  Every procedure takes p_tenant_id and p_user_id first, because RepositoryBase stamps
--  them on every call and a caller cannot omit them.
-- =====================================================================

USE astitwa;

DROP PROCEDURE IF EXISTS sp_hr_department_list;
DROP PROCEDURE IF EXISTS sp_hr_department_get;
DROP PROCEDURE IF EXISTS sp_hr_department_save;
DROP PROCEDURE IF EXISTS sp_hr_department_delete;
DROP PROCEDURE IF EXISTS sp_hr_department_lookup;
DROP PROCEDURE IF EXISTS sp_hr_designation_list;
DROP PROCEDURE IF EXISTS sp_hr_designation_get;
DROP PROCEDURE IF EXISTS sp_hr_designation_save;
DROP PROCEDURE IF EXISTS sp_hr_designation_delete;
DROP PROCEDURE IF EXISTS sp_hr_designation_lookup;
DROP PROCEDURE IF EXISTS sp_hr_employee_list;
DROP PROCEDURE IF EXISTS sp_hr_employee_get;
DROP PROCEDURE IF EXISTS sp_hr_employee_save;
DROP PROCEDURE IF EXISTS sp_hr_employee_delete;
DROP PROCEDURE IF EXISTS sp_hr_employee_lookup;
DROP PROCEDURE IF EXISTS sp_hr_employee_code_exists;
DROP PROCEDURE IF EXISTS sp_hr_employee_search_by_mobile;
DROP PROCEDURE IF EXISTS sp_hr_leave_type_lookup;
DROP PROCEDURE IF EXISTS sp_hr_leave_request_list;
DROP PROCEDURE IF EXISTS sp_hr_leave_request_get;
DROP PROCEDURE IF EXISTS sp_hr_leave_request_save;
DROP PROCEDURE IF EXISTS sp_hr_leave_request_approve;

DELIMITER $$

-- =====================================================================
-- DEPARTMENT
-- =====================================================================

CREATE PROCEDURE sp_hr_department_list (
  IN p_tenant_id INT,
  IN p_user_id   INT,
  IN p_search    VARCHAR(200),
  IN p_page_size INT,
  IN p_offset    INT
)
BEGIN
  SELECT department_id, dept_code, dept_name, is_active, created_on, updated_on
  FROM   hr_department
  WHERE  tenant_id = p_tenant_id
    AND  is_active = 1
    AND  (p_search IS NULL OR dept_code LIKE CONCAT('%', p_search, '%')
                           OR dept_name LIKE CONCAT('%', p_search, '%'))
  ORDER  BY dept_name
  LIMIT  p_page_size OFFSET p_offset;

  SELECT COUNT(*) AS total_count
  FROM   hr_department
  WHERE  tenant_id = p_tenant_id
    AND  is_active = 1
    AND  (p_search IS NULL OR dept_code LIKE CONCAT('%', p_search, '%')
                           OR dept_name LIKE CONCAT('%', p_search, '%'));
END$$

CREATE PROCEDURE sp_hr_department_get (
  IN p_tenant_id     INT,
  IN p_user_id       INT,
  IN p_department_id INT
)
BEGIN
  SELECT department_id, dept_code, dept_name, is_active
  FROM   hr_department
  WHERE  tenant_id     = p_tenant_id
    AND  department_id = p_department_id
    AND  is_active     = 1;
END$$

CREATE PROCEDURE sp_hr_department_save (
  IN p_tenant_id     INT,
  IN p_user_id       INT,
  IN p_department_id INT,
  IN p_dept_code     VARCHAR(40),
  IN p_dept_name     VARCHAR(150)
)
BEGIN
  DECLARE v_id INT;

  IF IFNULL(p_department_id, 0) = 0 THEN
    INSERT INTO hr_department (tenant_id, dept_code, dept_name, created_by, created_on)
    VALUES (p_tenant_id, p_dept_code, p_dept_name, p_user_id, UTC_TIMESTAMP());
    SET v_id = LAST_INSERT_ID();
  ELSE
    UPDATE hr_department
    SET    dept_code  = p_dept_code,
           dept_name  = p_dept_name,
           updated_by = p_user_id,
           updated_on = UTC_TIMESTAMP()
    WHERE  tenant_id     = p_tenant_id
      AND  department_id = p_department_id;
    SET v_id = p_department_id;
  END IF;

  SELECT department_id, dept_code, dept_name, is_active
  FROM   hr_department
  WHERE  tenant_id = p_tenant_id AND department_id = v_id;
END$$

-- Soft delete only. Master data is never removed (section 5).
CREATE PROCEDURE sp_hr_department_delete (
  IN p_tenant_id     INT,
  IN p_user_id       INT,
  IN p_department_id INT
)
BEGIN
  UPDATE hr_department
  SET    is_active  = 0,
         updated_by = p_user_id,
         updated_on = UTC_TIMESTAMP()
  WHERE  tenant_id     = p_tenant_id
    AND  department_id = p_department_id;
END$$

CREATE PROCEDURE sp_hr_department_lookup (
  IN p_tenant_id INT,
  IN p_user_id   INT
)
BEGIN
  SELECT department_id AS id, CONCAT(dept_code, ' - ', dept_name) AS label
  FROM   hr_department
  WHERE  tenant_id = p_tenant_id AND is_active = 1
  ORDER  BY dept_name
  LIMIT  500;
END$$

-- =====================================================================
-- DESIGNATION
-- =====================================================================

CREATE PROCEDURE sp_hr_designation_list (
  IN p_tenant_id INT,
  IN p_user_id   INT,
  IN p_search    VARCHAR(200),
  IN p_page_size INT,
  IN p_offset    INT
)
BEGIN
  SELECT designation_id, desig_code, desig_name, grade, is_active, created_on, updated_on
  FROM   hr_designation
  WHERE  tenant_id = p_tenant_id
    AND  is_active = 1
    AND  (p_search IS NULL OR desig_code LIKE CONCAT('%', p_search, '%')
                           OR desig_name LIKE CONCAT('%', p_search, '%')
                           OR grade      LIKE CONCAT('%', p_search, '%'))
  ORDER  BY desig_name
  LIMIT  p_page_size OFFSET p_offset;

  SELECT COUNT(*) AS total_count
  FROM   hr_designation
  WHERE  tenant_id = p_tenant_id
    AND  is_active = 1
    AND  (p_search IS NULL OR desig_code LIKE CONCAT('%', p_search, '%')
                           OR desig_name LIKE CONCAT('%', p_search, '%')
                           OR grade      LIKE CONCAT('%', p_search, '%'));
END$$

CREATE PROCEDURE sp_hr_designation_get (
  IN p_tenant_id      INT,
  IN p_user_id        INT,
  IN p_designation_id INT
)
BEGIN
  SELECT designation_id, desig_code, desig_name, grade, is_active
  FROM   hr_designation
  WHERE  tenant_id      = p_tenant_id
    AND  designation_id = p_designation_id
    AND  is_active      = 1;
END$$

CREATE PROCEDURE sp_hr_designation_save (
  IN p_tenant_id      INT,
  IN p_user_id        INT,
  IN p_designation_id INT,
  IN p_desig_code     VARCHAR(40),
  IN p_desig_name     VARCHAR(150),
  IN p_grade          VARCHAR(40)
)
BEGIN
  DECLARE v_id INT;

  IF IFNULL(p_designation_id, 0) = 0 THEN
    INSERT INTO hr_designation (tenant_id, desig_code, desig_name, grade, created_by, created_on)
    VALUES (p_tenant_id, p_desig_code, p_desig_name, p_grade, p_user_id, UTC_TIMESTAMP());
    SET v_id = LAST_INSERT_ID();
  ELSE
    UPDATE hr_designation
    SET    desig_code = p_desig_code,
           desig_name = p_desig_name,
           grade      = p_grade,
           updated_by = p_user_id,
           updated_on = UTC_TIMESTAMP()
    WHERE  tenant_id      = p_tenant_id
      AND  designation_id = p_designation_id;
    SET v_id = p_designation_id;
  END IF;

  SELECT designation_id, desig_code, desig_name, grade, is_active
  FROM   hr_designation
  WHERE  tenant_id = p_tenant_id AND designation_id = v_id;
END$$

CREATE PROCEDURE sp_hr_designation_delete (
  IN p_tenant_id      INT,
  IN p_user_id        INT,
  IN p_designation_id INT
)
BEGIN
  UPDATE hr_designation
  SET    is_active  = 0,
         updated_by = p_user_id,
         updated_on = UTC_TIMESTAMP()
  WHERE  tenant_id      = p_tenant_id
    AND  designation_id = p_designation_id;
END$$

CREATE PROCEDURE sp_hr_designation_lookup (
  IN p_tenant_id INT,
  IN p_user_id   INT
)
BEGIN
  SELECT designation_id AS id, CONCAT(desig_code, ' - ', desig_name) AS label
  FROM   hr_designation
  WHERE  tenant_id = p_tenant_id AND is_active = 1
  ORDER  BY desig_name
  LIMIT  500;
END$$

-- =====================================================================
-- EMPLOYEE
-- =====================================================================

CREATE PROCEDURE sp_hr_employee_list (
  IN p_tenant_id INT,
  IN p_user_id   INT,
  IN p_search    VARCHAR(200),
  IN p_page_size INT,
  IN p_offset    INT
)
BEGIN
  SELECT e.employee_id,
         e.employee_code,
         e.full_name,
         e.mobile,
         e.email,
         e.employment_status,
         e.date_of_joining,
         e.department_id,
         d.dept_name   AS department_name,
         e.designation_id,
         g.desig_name  AS designation_name,
         e.reporting_manager_id,
         m.full_name   AS reporting_manager_name
  FROM   hr_employee e
  LEFT   JOIN hr_department  d ON d.department_id  = e.department_id  AND d.tenant_id = e.tenant_id
  LEFT   JOIN hr_designation g ON g.designation_id = e.designation_id AND g.tenant_id = e.tenant_id
  LEFT   JOIN hr_employee    m ON m.employee_id    = e.reporting_manager_id AND m.tenant_id = e.tenant_id
  WHERE  e.tenant_id = p_tenant_id
    AND  e.is_active = 1
    AND  (p_search IS NULL OR e.employee_code LIKE CONCAT('%', p_search, '%')
                           OR e.full_name     LIKE CONCAT('%', p_search, '%')
                           OR e.mobile        LIKE CONCAT('%', p_search, '%')
                           OR e.email         LIKE CONCAT('%', p_search, '%'))
  ORDER  BY e.full_name
  LIMIT  p_page_size OFFSET p_offset;

  SELECT COUNT(*) AS total_count
  FROM   hr_employee e
  WHERE  e.tenant_id = p_tenant_id
    AND  e.is_active = 1
    AND  (p_search IS NULL OR e.employee_code LIKE CONCAT('%', p_search, '%')
                           OR e.full_name     LIKE CONCAT('%', p_search, '%')
                           OR e.mobile        LIKE CONCAT('%', p_search, '%')
                           OR e.email         LIKE CONCAT('%', p_search, '%'));
END$$

CREATE PROCEDURE sp_hr_employee_get (
  IN p_tenant_id   INT,
  IN p_user_id     INT,
  IN p_employee_id INT
)
BEGIN
  SELECT employee_id,
         employee_code,
         full_name,
         dob,
         date_of_joining,
         department_id,
         designation_id,
         reporting_manager_id,
         mobile,
         email,
         employment_status,
         gross_ctc,
         hra,
         tds,
         net_salary,
         is_active
  FROM   hr_employee
  WHERE  tenant_id   = p_tenant_id
    AND  employee_id = p_employee_id
    AND  is_active   = 1;
END$$

-- The employee-code uniqueness rule is enforced by uk_hr_employee_code as well; this
-- lets the service report it as a field error instead of a driver exception.
CREATE PROCEDURE sp_hr_employee_code_exists (
  IN p_tenant_id     INT,
  IN p_user_id       INT,
  IN p_employee_code VARCHAR(40),
  IN p_employee_id   INT
)
BEGIN
  SELECT COUNT(*) AS match_count
  FROM   hr_employee
  WHERE  tenant_id     = p_tenant_id
    AND  employee_code = p_employee_code
    AND  is_active     = 1
    AND  employee_id  <> IFNULL(p_employee_id, 0);
END$$

CREATE PROCEDURE sp_hr_employee_save (
  IN p_tenant_id            INT,
  IN p_user_id              INT,
  IN p_employee_id          INT,
  IN p_employee_code        VARCHAR(40),
  IN p_full_name            VARCHAR(180),
  IN p_dob                  DATE,
  IN p_date_of_joining      DATE,
  IN p_department_id        INT,
  IN p_designation_id       INT,
  IN p_reporting_manager_id INT,
  IN p_mobile               VARCHAR(30),
  IN p_email                VARCHAR(150),
  IN p_employment_status    VARCHAR(30),
  IN p_gross_ctc            DECIMAL(14,2),
  IN p_hra                  DECIMAL(14,2),
  IN p_tds                  DECIMAL(14,2),
  IN p_net_salary           DECIMAL(14,2)
)
BEGIN
  DECLARE v_id INT;

  IF IFNULL(p_employee_id, 0) = 0 THEN
    INSERT INTO hr_employee
          (tenant_id, employee_code, full_name, dob, date_of_joining, department_id,
           designation_id, reporting_manager_id, mobile, email, employment_status,
           gross_ctc, hra, tds, net_salary,
           created_by, created_on)
    VALUES (p_tenant_id, p_employee_code, p_full_name, p_dob, p_date_of_joining, p_department_id,
            p_designation_id, p_reporting_manager_id, p_mobile, p_email,
            IFNULL(p_employment_status, 'Active'),
            p_gross_ctc, p_hra, p_tds, p_net_salary,
            p_user_id, UTC_TIMESTAMP());
    SET v_id = LAST_INSERT_ID();
  ELSE
    UPDATE hr_employee
    SET    employee_code        = p_employee_code,
           full_name            = p_full_name,
           dob                  = p_dob,
           date_of_joining      = p_date_of_joining,
           department_id        = p_department_id,
           designation_id       = p_designation_id,
           reporting_manager_id = p_reporting_manager_id,
           mobile               = p_mobile,
           email                = p_email,
           employment_status    = IFNULL(p_employment_status, employment_status),
           gross_ctc            = p_gross_ctc,
           hra                  = p_hra,
           tds                  = p_tds,
           net_salary           = p_net_salary,
           updated_by           = p_user_id,
           updated_on           = UTC_TIMESTAMP()
    WHERE  tenant_id   = p_tenant_id
      AND  employee_id = p_employee_id;
    SET v_id = p_employee_id;
  END IF;

  SELECT employee_id, employee_code, full_name, dob, date_of_joining, department_id,
         designation_id, reporting_manager_id, mobile, email, employment_status,
         gross_ctc, hra, tds, net_salary, is_active
  FROM   hr_employee
  WHERE  tenant_id = p_tenant_id AND employee_id = v_id;
END$$

CREATE PROCEDURE sp_hr_employee_delete (
  IN p_tenant_id   INT,
  IN p_user_id     INT,
  IN p_employee_id INT
)
BEGIN
  UPDATE hr_employee
  SET    is_active  = 0,
         updated_by = p_user_id,
         updated_on = UTC_TIMESTAMP()
  WHERE  tenant_id   = p_tenant_id
    AND  employee_id = p_employee_id;
END$$

CREATE PROCEDURE sp_hr_employee_lookup (
  IN p_tenant_id INT,
  IN p_user_id   INT
)
BEGIN
  SELECT employee_id AS id, CONCAT(employee_code, ' - ', full_name) AS label
  FROM   hr_employee
  WHERE  tenant_id = p_tenant_id AND is_active = 1
  ORDER  BY full_name
  LIMIT  500;
END$$

-- Registered as the named query 'hr.employee.searchByMobile' (acceptance scenario 3).
-- A layer 5 script may reach it only through ext_named_query, never by name.
CREATE PROCEDURE sp_hr_employee_search_by_mobile (
  IN p_tenant_id INT,
  IN p_user_id   INT,
  IN p_mobile    VARCHAR(30)
)
BEGIN
  SELECT e.employee_id,
         e.employee_code,
         e.full_name,
         e.mobile,
         e.email,
         d.dept_name AS department_name
  FROM   hr_employee e
  LEFT   JOIN hr_department d ON d.department_id = e.department_id AND d.tenant_id = e.tenant_id
  WHERE  e.tenant_id = p_tenant_id
    AND  e.is_active = 1
    AND  p_mobile IS NOT NULL
    AND  e.mobile LIKE CONCAT('%', p_mobile, '%')
  ORDER  BY e.full_name
  LIMIT  50;
END$$

-- =====================================================================
-- LEAVE
-- =====================================================================

CREATE PROCEDURE sp_hr_leave_type_lookup (
  IN p_tenant_id INT,
  IN p_user_id   INT
)
BEGIN
  SELECT leave_type_id AS id, CONCAT(leave_code, ' - ', leave_name) AS label
  FROM   hr_leave_type
  WHERE  tenant_id = p_tenant_id AND is_active = 1
  ORDER  BY leave_name
  LIMIT  200;
END$$

-- p_status filters the approval queue; p_employee_id scopes to one person. Both optional.
CREATE PROCEDURE sp_hr_leave_request_list (
  IN p_tenant_id   INT,
  IN p_user_id     INT,
  IN p_search      VARCHAR(200),
  IN p_status      VARCHAR(20),
  IN p_employee_id INT,
  IN p_page_size   INT,
  IN p_offset      INT
)
BEGIN
  SELECT r.leave_request_id,
         r.employee_id,
         e.employee_code,
         e.full_name       AS employee_name,
         r.leave_type_id,
         t.leave_name      AS leave_type_name,
         r.from_date,
         r.to_date,
         r.days,
         r.reason,
         r.status,
         r.approved_by,
         r.approved_on,
         r.approval_remark
  FROM   hr_leave_request r
  JOIN   hr_employee   e ON e.employee_id   = r.employee_id   AND e.tenant_id = r.tenant_id
  LEFT   JOIN hr_leave_type t ON t.leave_type_id = r.leave_type_id AND t.tenant_id = r.tenant_id
  WHERE  r.tenant_id = p_tenant_id
    AND  r.is_active = 1
    AND  (p_status      IS NULL OR r.status      = p_status)
    AND  (p_employee_id IS NULL OR r.employee_id = p_employee_id)
    AND  (p_search IS NULL OR e.full_name     LIKE CONCAT('%', p_search, '%')
                           OR e.employee_code LIKE CONCAT('%', p_search, '%')
                           OR r.reason        LIKE CONCAT('%', p_search, '%'))
  ORDER  BY r.from_date DESC, r.leave_request_id DESC
  LIMIT  p_page_size OFFSET p_offset;

  SELECT COUNT(*) AS total_count
  FROM   hr_leave_request r
  JOIN   hr_employee e ON e.employee_id = r.employee_id AND e.tenant_id = r.tenant_id
  WHERE  r.tenant_id = p_tenant_id
    AND  r.is_active = 1
    AND  (p_status      IS NULL OR r.status      = p_status)
    AND  (p_employee_id IS NULL OR r.employee_id = p_employee_id)
    AND  (p_search IS NULL OR e.full_name     LIKE CONCAT('%', p_search, '%')
                           OR e.employee_code LIKE CONCAT('%', p_search, '%')
                           OR r.reason        LIKE CONCAT('%', p_search, '%'));
END$$

CREATE PROCEDURE sp_hr_leave_request_get (
  IN p_tenant_id        INT,
  IN p_user_id          INT,
  IN p_leave_request_id INT
)
BEGIN
  SELECT r.leave_request_id,
         r.employee_id,
         e.full_name  AS employee_name,
         e.email      AS employee_email,
         r.leave_type_id,
         t.leave_name AS leave_type_name,
         r.from_date,
         r.to_date,
         r.days,
         r.reason,
         r.status,
         r.approved_by,
         r.approved_on,
         r.approval_remark,
         r.is_active
  FROM   hr_leave_request r
  JOIN   hr_employee e ON e.employee_id = r.employee_id AND e.tenant_id = r.tenant_id
  LEFT   JOIN hr_leave_type t ON t.leave_type_id = r.leave_type_id AND t.tenant_id = r.tenant_id
  WHERE  r.tenant_id        = p_tenant_id
    AND  r.leave_request_id = p_leave_request_id
    AND  r.is_active        = 1;
END$$

CREATE PROCEDURE sp_hr_leave_request_save (
  IN p_tenant_id        INT,
  IN p_user_id          INT,
  IN p_leave_request_id INT,
  IN p_employee_id      INT,
  IN p_leave_type_id    INT,
  IN p_from_date        DATE,
  IN p_to_date          DATE,
  IN p_days             DECIMAL(5,1),
  IN p_reason           VARCHAR(500)
)
BEGIN
  DECLARE v_id INT;

  IF IFNULL(p_leave_request_id, 0) = 0 THEN
    INSERT INTO hr_leave_request
          (tenant_id, employee_id, leave_type_id, from_date, to_date, days, reason,
           status, created_by, created_on)
    VALUES (p_tenant_id, p_employee_id, p_leave_type_id, p_from_date, p_to_date, p_days,
            p_reason, 'Pending', p_user_id, UTC_TIMESTAMP());
    SET v_id = LAST_INSERT_ID();
  ELSE
    -- An approved or rejected request is closed; only a pending one may be edited.
    UPDATE hr_leave_request
    SET    employee_id   = p_employee_id,
           leave_type_id = p_leave_type_id,
           from_date     = p_from_date,
           to_date       = p_to_date,
           days          = p_days,
           reason        = p_reason,
           updated_by    = p_user_id,
           updated_on    = UTC_TIMESTAMP()
    WHERE  tenant_id        = p_tenant_id
      AND  leave_request_id = p_leave_request_id
      AND  status           = 'Pending';
    SET v_id = p_leave_request_id;
  END IF;

  SELECT leave_request_id, employee_id, leave_type_id, from_date, to_date, days,
         reason, status, approved_by, approved_on, approval_remark
  FROM   hr_leave_request
  WHERE  tenant_id = p_tenant_id AND leave_request_id = v_id;
END$$

-- Only a pending request can be decided, so a double approval is a no-op rather than an overwrite.
CREATE PROCEDURE sp_hr_leave_request_approve (
  IN p_tenant_id        INT,
  IN p_user_id          INT,
  IN p_leave_request_id INT,
  IN p_status           VARCHAR(20),
  IN p_remark           VARCHAR(500),
  IN p_approver_emp_id  INT
)
BEGIN
  UPDATE hr_leave_request
  SET    status          = p_status,
         approval_remark = p_remark,
         approved_by     = p_approver_emp_id,
         approved_on     = UTC_TIMESTAMP(),
         updated_by      = p_user_id,
         updated_on      = UTC_TIMESTAMP()
  WHERE  tenant_id        = p_tenant_id
    AND  leave_request_id = p_leave_request_id
    AND  status           = 'Pending';

  SELECT r.leave_request_id, r.employee_id, e.full_name AS employee_name, e.email AS employee_email,
         r.leave_type_id, t.leave_name AS leave_type_name, r.from_date, r.to_date, r.days,
         r.reason, r.status, r.approved_by, r.approved_on, r.approval_remark
  FROM   hr_leave_request r
  JOIN   hr_employee e ON e.employee_id = r.employee_id AND e.tenant_id = r.tenant_id
  LEFT   JOIN hr_leave_type t ON t.leave_type_id = r.leave_type_id AND t.tenant_id = r.tenant_id
  WHERE  r.tenant_id = p_tenant_id AND r.leave_request_id = p_leave_request_id;
END$$

DELIMITER ;
