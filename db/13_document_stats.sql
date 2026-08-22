-- =====================================================================
--  HrSuite - document register totals
--
--  Apply after 12_field_builder_parity.sql. Re-runnable.
--
--  The Documents Center shows counts across the whole register: how many are
--  awaiting a signature, how many renewals lapse within ninety days, how many
--  letters exist of each type. Every list endpoint in this product is paged and
--  clamped at 200 rows, so those numbers cannot be counted in the browser
--  without either lying on page two or lifting the cap.
--
--  Counting them here is the answer: three small aggregates in one round trip,
--  none of which grows with the size of the register.
-- =====================================================================

USE astitwa;

DROP PROCEDURE IF EXISTS sp_hr_document_stats;

DELIMITER $$

CREATE PROCEDURE sp_hr_document_stats (
  IN p_tenant_id INT,
  IN p_user_id   INT
)
BEGIN
  -- 1. The headline numbers.
  SELECT
    COUNT(*)                                                             AS total_count,
    SUM(status = 'Draft')                                                AS draft_count,
    SUM(status = 'Pending Signature')                                    AS pending_signature_count,
    SUM(status = 'Issued')                                               AS issued_count,
    SUM(status = 'Acknowledged')                                         AS acknowledged_count,
    SUM(status = 'Expired')                                              AS expired_count,
    SUM(status = 'Revoked')                                              AS revoked_count,
    -- "Out of the building": everything past the preparation stages.
    SUM(status NOT IN ('Draft', 'Pending Signature'))                    AS delivered_count,
    -- A term that has not lapsed yet and falls inside the next ninety days.
    SUM(valid_till IS NOT NULL
        AND status <> 'Revoked'
        AND valid_till >= CURRENT_DATE()
        AND valid_till <= DATE_ADD(CURRENT_DATE(), INTERVAL 90 DAY))     AS expiring_90_count,
    SUM(valid_till IS NOT NULL AND status <> 'Revoked')                  AS dated_count,
    MIN(CASE WHEN valid_till IS NOT NULL
                  AND status <> 'Revoked'
                  AND valid_till >= CURRENT_DATE()
             THEN valid_till END)                                        AS next_expiry
  FROM   hr_document
  WHERE  tenant_id = p_tenant_id AND is_active = 1;

  -- 2. One row per document type, so the gallery can show a count on every card
  --    including the types nobody has issued yet.
  SELECT d.document_type,
         d.display_name,
         COUNT(h.document_id) AS document_count
  FROM   cfg_print_document_type d
  LEFT   JOIN hr_document h
         ON  h.tenant_id     = d.tenant_id
         AND h.document_type = d.document_type
         AND h.is_active     = 1
  WHERE  d.tenant_id = p_tenant_id AND d.is_active = 1
  GROUP  BY d.document_type, d.display_name, d.seq_no
  ORDER  BY d.seq_no;

  -- 3. Issued per month for the last twelve, for the trend chart. Months with
  --    nothing issued are absent rather than zero; the chart plots what happened.
  SELECT DATE_FORMAT(created_on, '%Y-%m') AS period,
         COUNT(*)                         AS document_count
  FROM   hr_document
  WHERE  tenant_id  = p_tenant_id
    AND  is_active  = 1
    AND  created_on >= DATE_SUB(CURRENT_DATE(), INTERVAL 12 MONTH)
  GROUP  BY period
  ORDER  BY period;
END$$

DELIMITER ;
