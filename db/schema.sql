-- Базова схема. Студент розширює під власні потреби.

CREATE TABLE IF NOT EXISTS requests (
    request_id      UUID PRIMARY KEY,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    model           TEXT NOT NULL,
    provider        TEXT,
    prompt_version  TEXT,
    -- яка саме версія пішла в модель: імʼя промпта в реєстрі, id рядка з prompts і sha256
    -- відправленого system-тексту (id NULL, коли реєстр порожній і спрацював дефолт — тоді prompt_version = 'none')
    prompt_name     TEXT,
    prompt_version_id UUID,
    prompt_sha256   TEXT,
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
    -- стабільний id версії: його пишемо в requests поряд із міткою v1/v2
    version_id  UUID NOT NULL DEFAULT gen_random_uuid(),
    PRIMARY KEY (name, version)
);

-- seed реєстру (W1): рівно дві версії, активна — v2. Живе тут, у міграції, а не в коді сервісу.
-- виконується лише на чистому томі: docker compose down -v && docker compose up -d
INSERT INTO prompts (name, version, body, active) VALUES
    ('support-system', 'v1', 'You are an assistant.', false),
    ('support-system', 'v2', 'You are a support assistant. Be concise and helpful.', true)
ON CONFLICT (name, version) DO NOTHING;

-- audit trail активацій (W1, опційно): хто, коли і з якої версії перемкнув.
-- рядок пишеться в тій самій транзакції, що й зміна active; на 404 рядка немає.
CREATE TABLE IF NOT EXISTS prompt_activations (
    id               BIGSERIAL PRIMARY KEY,
    name             TEXT NOT NULL,
    version          TEXT NOT NULL,
    previous_version TEXT,
    actor            TEXT NOT NULL,
    approval_quote   TEXT,  -- необовʼязкове {"quote": "..."} з тіла запиту: чиє рішення і дослівно яке
    activated_at     TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_requests_created_at ON requests (created_at);
CREATE INDEX IF NOT EXISTS idx_requests_model ON requests (model);
