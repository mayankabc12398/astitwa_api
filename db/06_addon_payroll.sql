-- =====================================================================
--  HrSuite - layer 3 add-on: Payroll
--
--  An add-on ships its own table and its own procedures. Nothing in the base schema
--  refers to them, so an installation that never licenses Payroll can skip this file
--  entirely and the product is unaffected.
--
--  Payroll CALCULATION is an explicit non-goal (section 14). This exists to prove the
--  registration, licensing and isolation pattern.
-- =====================================================================

USE astitwa;

CREATE TABLE IF NOT EXISTS pay_payroll_run (
  payroll_run_id INT AUTO_INCREMENT PRIMARY KEY,
  tenant_id      INT          NOT NULL,
  period_label   VARCHAR(40)  NOT NULL,      -- '2026-07'
  status         VARCHAR(30)  NOT NULL DEFAULT 'Draft',
  employee_count INT          NOT NULL DEFAULT 0,
  run_on         DATE         NULL,
  is_active      BIT          NOT NULL DEFAULT 1,
  created_by     INT          NULL,
  created_on     DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_by     INT          NULL,
  updated_on     DATETIME     NULL,
  UNIQUE KEY uk_pay_payroll_run (tenant_id, period_label)
) ENGINE=InnoDB;

DROP PROCEDURE IF EXISTS sp_pay_run_list;

DELIMITER $$

CREATE PROCEDURE sp_pay_run_list (
  IN p_tenant_id INT,
  IN p_user_id   INT,
  IN p_search    VARCHAR(200),
  IN p_page_size INT,
  IN p_offset    INT
)
BEGIN
  SELECT payroll_run_id, period_label, status, employee_count, run_on
  FROM   pay_payroll_run
  WHERE  tenant_id = p_tenant_id
    AND  is_active = 1
    AND  (p_search IS NULL OR period_label LIKE CONCAT('%', p_search, '%')
                           OR status       LIKE CONCAT('%', p_search, '%'))
  ORDER  BY period_label DESC
  LIMIT  p_page_size OFFSET p_offset;

  SELECT COUNT(*) AS total_count
  FROM   pay_payroll_run
  WHERE  tenant_id = p_tenant_id
    AND  is_active = 1
    AND  (p_search IS NULL OR period_label LIKE CONCAT('%', p_search, '%')
                           OR status       LIKE CONCAT('%', p_search, '%'));
END$$

DELIMITER ;
