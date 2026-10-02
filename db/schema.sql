-- Базова схема. Студент розширює під власні потреби.

CREATE TABLE IF NOT EXISTS requests (
    request_id      UUID PRIMARY KEY,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    model           TEXT NOT NULL,
    provider        TEXT,
    prompt_version  TEXT,
    latency_ms      INTEGER,
    prompt_tokens   INTEGER,
    completion_tokens INTEGER,
    cost_usd        NUMERIC(10, 6),
    status          TEXT
);

CREATE TABLE IF NOT EXISTS prompts (
    name        TEXT NOT NULL,
    version     TEXT NOT NULL,
    body        TEXT NOT NULL,
    active      BOOLEAN NOT NULL DEFAULT false,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (name, version)
);

CREATE INDEX IF NOT EXISTS idx_requests_created_at ON requests (created_at);
CREATE INDEX IF NOT EXISTS idx_requests_model ON requests (model);
