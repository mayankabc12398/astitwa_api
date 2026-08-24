-- =====================================================================
--  Demo Hospital - assistant conversations, kept in the database
--
--  Apply after 16_patient.sql. Re-runnable.
--
--  The assistant beside the editor held its thread in React state, so it
--  lived exactly as long as the dialog did. Closing the hook, saving it, or
--  refreshing the page threw away the reasoning that produced the script -
--  which is the part somebody reads six months later when they want to know
--  why the script does what it does.
--
--  A thread is per tenant, per user, per thing being edited. Per user because
--  a conversation is a person's working notes, not tenant-wide documentation:
--  a colleague opening the same hook gets their own thread rather than
--  reading someone else's half-finished questions.
--
--  Trimming is in sp_ext_ai_message_add rather than in a nightly job, so the
--  table cannot grow between cleanups.
-- =====================================================================

CREATE TABLE IF NOT EXISTS ext_ai_thread (
  thread_id     INT AUTO_INCREMENT PRIMARY KEY,
  tenant_id     INT          NOT NULL,
  user_id       INT          NOT NULL,
  -- What the conversation is about: 'hook:hr.patient.onLoad', 'endpoint:getemployeelist'.
  -- Free text on purpose - a surface added later needs no migration to get a thread.
  thread_key    VARCHAR(160) NOT NULL,
  title         VARCHAR(200) NULL,
  -- javascript | mysql. Only used to label the thread; the answer's own language
  -- is whatever the editor was holding at the time.
  language      VARCHAR(20)  NULL,
  message_count INT          NOT NULL DEFAULT 0,
  created_on    DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_on    DATETIME     NULL,
  UNIQUE KEY uk_ext_ai_thread (tenant_id, user_id, thread_key),
  KEY ix_ext_ai_thread_recent (tenant_id, user_id, updated_on)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS ext_ai_message (
  message_id  BIGINT AUTO_INCREMENT PRIMARY KEY,
  thread_id   INT         NOT NULL,
  tenant_id   INT         NOT NULL,
  -- user | model, the two names the Vertex API and the panel already use.
  role        VARCHAR(10) NOT NULL,
  body        MEDIUMTEXT  NOT NULL,
  -- Which model answered. An answer written by gemini-2.5-flash and one written
  -- by whatever replaces it are not the same evidence.
  model       VARCHAR(60) NULL,
  duration_ms INT         NULL,
  created_on  DATETIME    NOT NULL DEFAULT CURRENT_TIMESTAMP,
  KEY ix_ext_ai_message_thread (thread_id, message_id)
) ENGINE=InnoDB;

DROP PROCEDURE IF EXISTS sp_ext_ai_thread_open;
DROP PROCEDURE IF EXISTS sp_ext_ai_thread_messages;
DROP PROCEDURE IF EXISTS sp_ext_ai_thread_clear;
DROP PROCEDURE IF EXISTS sp_ext_ai_thread_list;
DROP PROCEDURE IF EXISTS sp_ext_ai_message_add;

DELIMITER $$

-- Get or create. Called by every path, so nothing else has to care whether this
-- is the first question about a hook or the fortieth.
CREATE PROCEDURE sp_ext_ai_thread_open (
  IN p_tenant_id  INT,
  IN p_user_id    INT,
  IN p_thread_key VARCHAR(160),
  IN p_title      VARCHAR(200),
  IN p_language   VARCHAR(20)
)
BEGIN
  INSERT INTO ext_ai_thread (tenant_id, user_id, thread_key, title, language, created_on)
  VALUES (p_tenant_id, p_user_id, p_thread_key, p_title, p_language, UTC_TIMESTAMP())
  ON DUPLICATE KEY UPDATE
    -- A title arrives with the first question and again with every later one. Keep the
    -- newest non-empty: the hook may have been renamed since the thread started.
    title      = IFNULL(NULLIF(p_title, ''), title),
    language   = IFNULL(NULLIF(p_language, ''), language),
    updated_on = UTC_TIMESTAMP();

  SELECT thread_id, thread_key, title, language, message_count, created_on, updated_on
  FROM   ext_ai_thread
  WHERE  tenant_id = p_tenant_id AND user_id = p_user_id AND thread_key = p_thread_key;
END$$

CREATE PROCEDURE sp_ext_ai_message_add (
  IN p_tenant_id   INT,
  IN p_user_id     INT,
  IN p_thread_key  VARCHAR(160),
  IN p_title       VARCHAR(200),
  IN p_language    VARCHAR(20),
  IN p_role        VARCHAR(10),
  IN p_body        MEDIUMTEXT,
  IN p_model       VARCHAR(60),
  IN p_duration_ms INT
)
BEGIN
  DECLARE v_thread_id INT;
  DECLARE v_keep      INT DEFAULT 100;
  DECLARE v_cutoff    BIGINT;

  INSERT INTO ext_ai_thread (tenant_id, user_id, thread_key, title, language, created_on)
  VALUES (p_tenant_id, p_user_id, p_thread_key, p_title, p_language, UTC_TIMESTAMP())
  ON DUPLICATE KEY UPDATE
    title      = IFNULL(NULLIF(p_title, ''), title),
    language   = IFNULL(NULLIF(p_language, ''), language),
    updated_on = UTC_TIMESTAMP();

  SELECT thread_id INTO v_thread_id
  FROM   ext_ai_thread
  WHERE  tenant_id = p_tenant_id AND user_id = p_user_id AND thread_key = p_thread_key;

  INSERT INTO ext_ai_message (thread_id, tenant_id, role, body, model, duration_ms, created_on)
  VALUES (v_thread_id, p_tenant_id, p_role, p_body, NULLIF(p_model, ''), p_duration_ms, UTC_TIMESTAMP());

  -- Keep the last hundred exchanges and drop the rest. A thread is working notes; a
  -- conversation nobody has scrolled back through in a hundred turns is not being read.
  SELECT message_id INTO v_cutoff
  FROM   ext_ai_message
  WHERE  thread_id = v_thread_id
  ORDER  BY message_id DESC
  LIMIT  1 OFFSET v_keep;

  IF v_cutoff IS NOT NULL THEN
    DELETE FROM ext_ai_message WHERE thread_id = v_thread_id AND message_id <= v_cutoff;
  END IF;

  UPDATE ext_ai_thread
  SET    message_count = (SELECT COUNT(*) FROM ext_ai_message WHERE thread_id = v_thread_id),
         updated_on    = UTC_TIMESTAMP()
  WHERE  thread_id = v_thread_id;
END$$

-- Oldest first: the panel renders them in the order they were said.
CREATE PROCEDURE sp_ext_ai_thread_messages (
  IN p_tenant_id  INT,
  IN p_user_id    INT,
  IN p_thread_key VARCHAR(160),
  IN p_limit      INT
)
BEGIN
  SET p_limit = IFNULL(NULLIF(p_limit, 0), 100);

  SELECT m.message_id, m.role, m.body, m.model, m.duration_ms, m.created_on
  FROM   ext_ai_message m
  JOIN   ext_ai_thread  t ON t.thread_id = m.thread_id
  WHERE  t.tenant_id  = p_tenant_id
    AND  t.user_id    = p_user_id
    AND  t.thread_key = p_thread_key
  ORDER  BY m.message_id
  LIMIT  p_limit;
END$$

-- The user's own threads, most recently used first. What a "recent conversations"
-- list reads, and what tells an administrator the feature is being used at all.
CREATE PROCEDURE sp_ext_ai_thread_list (
  IN p_tenant_id INT,
  IN p_user_id   INT,
  IN p_search    VARCHAR(200),
  IN p_page_size INT,
  IN p_offset    INT
)
BEGIN
  SELECT thread_id, thread_key, title, language, message_count, created_on, updated_on
  FROM   ext_ai_thread
  WHERE  tenant_id = p_tenant_id
    AND  user_id   = p_user_id
    AND  (p_search IS NULL OR thread_key LIKE CONCAT('%', p_search, '%')
                           OR title      LIKE CONCAT('%', p_search, '%'))
  ORDER  BY IFNULL(updated_on, created_on) DESC
  LIMIT  p_page_size OFFSET p_offset;

  SELECT COUNT(*) AS total_count
  FROM   ext_ai_thread
  WHERE  tenant_id = p_tenant_id
    AND  user_id   = p_user_id
    AND  (p_search IS NULL OR thread_key LIKE CONCAT('%', p_search, '%')
                           OR title      LIKE CONCAT('%', p_search, '%'));
END$$

-- A real delete, not a soft one: "clear this conversation" has to mean the text is
-- gone. Scoped to the one user's own thread, so nobody can clear a colleague's.
CREATE PROCEDURE sp_ext_ai_thread_clear (
  IN p_tenant_id  INT,
  IN p_user_id    INT,
  IN p_thread_key VARCHAR(160)
)
BEGIN
  DECLARE v_thread_id INT;

  SELECT thread_id INTO v_thread_id
  FROM   ext_ai_thread
  WHERE  tenant_id = p_tenant_id AND user_id = p_user_id AND thread_key = p_thread_key;

  IF v_thread_id IS NOT NULL THEN
    DELETE FROM ext_ai_message WHERE thread_id = v_thread_id;
    DELETE FROM ext_ai_thread  WHERE thread_id = v_thread_id;
  END IF;
END$$

DELIMITER ;
