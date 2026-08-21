-- =====================================================================
--  HrSuite - the acceptance scenarios from section 13, as data.
--
--  This file is the proof of the whole exercise. Every statement below changes what the
--  application does for one client, and NONE of them requires a code change, a build or a
--  restart. Sign out and back in (or reload) so the client re-reads the bootstrap payload.
--
--  Run the blocks one at a time and watch the effect. Each is labelled with the scenario
--  number it satisfies.
-- =====================================================================

USE astitwa;

SET @acme   = (SELECT tenant_id FROM sys_tenant WHERE tenant_code = 'ACME');
SET @globex = (SELECT tenant_id FROM sys_tenant WHERE tenant_code = 'GLOBEX');


-- =====================================================================
-- 1. Hide reportingManager on the Employee form for ACME only.
--    One row. Sign in as ACME/admin and the field is gone; sign in as GLOBEX/admin
--    and it is still there.
-- =====================================================================

INSERT INTO cfg_field_rule (tenant_id, screen_key, field_key, is_visible, is_required, label, seq_no, created_by, created_on)
VALUES (@acme, 'hr.employee', 'reportingManagerId', 0, 0, NULL, 70, 1, UTC_TIMESTAMP())
ON DUPLICATE KEY UPDATE is_visible = 0, updated_by = 1, updated_on = UTC_TIMESTAMP();

-- To put it back:
--   UPDATE cfg_field_rule SET is_visible = 1 WHERE tenant_id = @acme AND field_key = 'reportingManagerId';

-- While you are here, two more things Layer 2 can do without a developer:
--   rename a caption, and make an optional field mandatory for one client.
INSERT INTO cfg_field_rule (tenant_id, screen_key, field_key, is_visible, is_required, label, seq_no, created_by, created_on)
VALUES (@acme, 'hr.employee', 'mobile', 1, 1, 'Mobile (work)', 80, 1, UTC_TIMESTAMP())
ON DUPLICATE KEY UPDATE is_required = 1, label = 'Mobile (work)', updated_by = 1, updated_on = UTC_TIMESTAMP();


-- =====================================================================
-- 2. Block saving an employee whose joining date is in the future - ACME only.
--
--    Registered twice on purpose, client and server:
--      * the client copy gives immediate feedback and stops the round trip
--      * the server copy is the one that actually enforces it, because a client-side
--        check is never a control (section 11)
-- =====================================================================

INSERT INTO ext_script_hook
      (tenant_id, hook_key, seq_no, run_on, script_body, is_active, version_no, created_by, created_on, updated_by, updated_on)
SELECT @acme, 'hr.employee.beforeSave', 10, 'client',
'var doj = ctx.form.dateOfJoining;
if (doj) {
  var joining = new Date(doj);
  var today = new Date();
  today.setHours(0, 0, 0, 0);
  if (joining > today) {
    return {
      cancelSave: true,
      message: "Date of joining cannot be in the future. Acme records staff only from their actual start date."
    };
  }
}',
       1, 1, 1, UTC_TIMESTAMP(), 1, UTC_TIMESTAMP()
WHERE NOT EXISTS (
  SELECT 1 FROM ext_script_hook
  WHERE tenant_id = @acme AND hook_key = 'hr.employee.beforeSave' AND run_on = 'client'
);

INSERT INTO ext_script_hook
      (tenant_id, hook_key, seq_no, run_on, script_body, is_active, version_no, created_by, created_on, updated_by, updated_on)
SELECT @acme, 'hr.employee.beforeSave', 10, 'server',
'var doj = ctx.form.dateOfJoining;
if (doj) {
  var joining = new Date(doj);
  var today = new Date();
  today.setHours(0, 0, 0, 0);
  if (joining > today) {
    return {
      cancelSave: true,
      message: "Date of joining cannot be in the future."
    };
  }
}',
       1, 1, 1, UTC_TIMESTAMP(), 1, UTC_TIMESTAMP()
WHERE NOT EXISTS (
  SELECT 1 FROM ext_script_hook
  WHERE tenant_id = @acme AND hook_key = 'hr.employee.beforeSave' AND run_on = 'server'
);

-- Proof it is scoped: sign in as GLOBEX/admin, set a future joining date, and it saves.


-- =====================================================================
-- 3. On leaving the mobile field, look up employees with that number and let the user
--    pick one. Selecting a row fills the form.
--
--    Note what the script does NOT contain: no SQL, no procedure name, no markup. It
--    names a registered query and hands ui.pickList some data.
-- =====================================================================

INSERT INTO ext_script_hook
      (tenant_id, hook_key, seq_no, run_on, script_body, debounce_ms, is_active, version_no, created_by, created_on, updated_by, updated_on)
SELECT @acme, 'hr.employee.field.mobile.onBlur', 10, 'client',
'var mobile = ctx.value;
if (utils.isEmpty(mobile) || String(mobile).length < 4) return;

var result = await api.query("hr.employee.searchByMobile", { mobile: mobile });
if (!result.ok || result.rows.length === 0) return;

var matches = result.rows.filter(function (r) {
  return String(r.employee_id) !== String(ctx.form.employeeId);
});
if (matches.length === 0) return;

var chosen = await ui.pickList({
  title: "This number is already on file",
  columns: [
    { key: "employee_code", label: "Code" },
    { key: "full_name", label: "Name" },
    { key: "department_name", label: "Department" },
    { key: "mobile", label: "Mobile" }
  ],
  rows: matches,
  emptyAction: { label: "Keep the new record", action: "keep" }
});

if (!chosen || chosen.__action) return;

ctx.setForm({
  employeeId: chosen.employee_id,
  employeeCode: chosen.employee_code,
  fullName: chosen.full_name,
  mobile: chosen.mobile
});

return { message: "Loaded " + chosen.full_name + " from the existing record." };',
       400, 1, 1, 1, UTC_TIMESTAMP(), 1, UTC_TIMESTAMP()
WHERE NOT EXISTS (
  SELECT 1 FROM ext_script_hook
  WHERE tenant_id = @acme AND hook_key = 'hr.employee.field.mobile.onBlur'
);

-- Try it: open any ACME employee, type 9876500002 into Mobile and tab out.
-- Arun Nair and Meera Iyer both have that number, so the pick list appears.


-- =====================================================================
-- 4. Break that script on purpose. The Employee screen must still save.
--
--    Run this, reload the client, then edit and save an employee. The save works.
--    The failure is in Administration > Hook Log, with status 'error'.
-- =====================================================================

-- Archive the working version first, exactly as the admin screen would.
INSERT INTO ext_script_hook_history
      (hook_id, tenant_id, hook_key, seq_no, run_on, script_body, debounce_ms, is_active, version_no, archived_by, archived_on)
SELECT hook_id, tenant_id, hook_key, seq_no, run_on, script_body, debounce_ms, is_active, version_no, 1, UTC_TIMESTAMP()
FROM   ext_script_hook
WHERE  tenant_id = @acme AND hook_key = 'hr.employee.field.mobile.onBlur';

UPDATE ext_script_hook
SET    script_body = 'var mobile = ctx.value
if (utils.isEmpty(mobile) return;   /* deliberate syntax error: unbalanced parenthesis */',
       version_no = version_no + 1,
       updated_by = 1,
       updated_on = UTC_TIMESTAMP()
WHERE  tenant_id = @acme AND hook_key = 'hr.employee.field.mobile.onBlur';


-- =====================================================================
-- 5. Roll it back.
--
--    Do this one through the UI, because that is the point: Administration >
--    Script Hooks > the mobile hook > History > Roll back. No SQL required.
--
--    The equivalent, if you would rather see it as data:
--
--      SELECT history_id, version_no, archived_on
--      FROM   ext_script_hook_history
--      WHERE  hook_id = (SELECT hook_id FROM ext_script_hook
--                        WHERE tenant_id = @acme AND hook_key = 'hr.employee.field.mobile.onBlur')
--      ORDER  BY version_no DESC;
--
--      CALL sp_ext_hook_rollback(@acme, 1, <hook_id>, <history_id>);
-- =====================================================================


-- =====================================================================
-- 6. Take Payroll away from ACME.
--
--    After a reload: the menu entry is gone, /api/payroll/runs answers 403, and the
--    addon-payroll chunk is never requested - check the browser network tab.
-- =====================================================================

UPDATE sys_tenant_module
SET    is_enabled = 0, updated_by = 1, updated_on = UTC_TIMESTAMP()
WHERE  tenant_id = @acme AND module_key = 'payroll';

-- Give it back with:
--   UPDATE sys_tenant_module SET is_enabled = 1 WHERE tenant_id = @acme AND module_key = 'payroll';


-- =====================================================================
-- 7. Turn the email integration off. Leave approval must still succeed.
--
--    It is already off in the seed; this makes the intent explicit. Approve a leave
--    request and watch it go through with no mail server configured anywhere.
-- =====================================================================

UPDATE sys_tenant_integration
SET    is_enabled = 0, updated_by = 1, updated_on = UTC_TIMESTAMP()
WHERE  tenant_id = @acme AND integration_key = 'email.smtp';

-- A leave request to approve, if you need one:
INSERT INTO hr_leave_request
      (tenant_id, employee_id, leave_type_id, from_date, to_date, days, reason, status, created_by, created_on)
SELECT @acme,
       (SELECT employee_id FROM hr_employee WHERE tenant_id = @acme AND employee_code = 'E-1001'),
       (SELECT leave_type_id FROM hr_leave_type WHERE tenant_id = @acme AND leave_code = 'CL'),
       '2026-09-01', '2026-09-03', 3, 'Family function', 'Pending', 1, UTC_TIMESTAMP()
WHERE NOT EXISTS (
  SELECT 1 FROM hr_leave_request
  WHERE tenant_id = @acme AND reason = 'Family function' AND status = 'Pending'
);


-- =====================================================================
-- Clean-up: put everything back the way the seed left it.
-- =====================================================================
--
--   DELETE FROM ext_script_hook WHERE tenant_id = @acme;
--   DELETE FROM ext_script_hook_history WHERE tenant_id = @acme;
--   DELETE FROM cfg_field_rule WHERE tenant_id = @acme;
--   UPDATE sys_tenant_module SET is_enabled = 1 WHERE tenant_id = @acme AND module_key = 'payroll';
