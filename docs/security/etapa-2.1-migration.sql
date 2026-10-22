START TRANSACTION;
DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'proxies') THEN
        CREATE SCHEMA proxies;
    END IF;
END $EF$;

CREATE TABLE proxies."Proxies" (
    "Id" uuid NOT NULL,
    "Host" character varying(253) NOT NULL,
    "Port" integer NOT NULL,
    "Username" character varying(128) NOT NULL,
    "PasswordKeyId" character varying(64) NOT NULL,
    "PasswordNonce" bytea NOT NULL,
    "PasswordCiphertext" bytea NOT NULL,
    "PasswordTag" bytea NOT NULL,
    "Status" character varying(16) NOT NULL,
    "Region" character varying(100) NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_Proxies" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_Proxies_Host" CHECK (length(btrim("Host")) BETWEEN 1 AND 253 AND "Host" = lower("Host") AND "Host" !~ '[[:space:]/@?#]'),
    CONSTRAINT "CK_Proxies_Id" CHECK ("Id" <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT "CK_Proxies_Password" CHECK (octet_length("PasswordNonce") = 12 AND octet_length("PasswordTag") = 16 AND octet_length("PasswordCiphertext") BETWEEN 1 AND 1024 AND "PasswordKeyId" ~ '^[A-Za-z0-9_.-]{1,64}$'),
    CONSTRAINT "CK_Proxies_Port" CHECK ("Port" BETWEEN 1 AND 65535),
    CONSTRAINT "CK_Proxies_Region" CHECK (length(btrim("Region")) BETWEEN 1 AND 100 AND "Region" !~ '[[:cntrl:]]'),
    CONSTRAINT "CK_Proxies_Status" CHECK ("Status" IN ('Active', 'Suspended', 'Disabled')),
    CONSTRAINT "CK_Proxies_Timestamps" CHECK (isfinite("CreatedAt") AND isfinite("UpdatedAt") AND isfinite("ExpiresAt") AND "UpdatedAt" >= "CreatedAt"),
    CONSTRAINT "CK_Proxies_Username" CHECK (length(btrim("Username")) BETWEEN 1 AND 128 AND "Username" !~ '[[:cntrl:]]')
);

CREATE INDEX "IX_Proxies_Host_Port" ON proxies."Proxies" ("Host", "Port");

CREATE INDEX "IX_Proxies_Region" ON proxies."Proxies" ("Region");

CREATE INDEX "IX_Proxies_Status_ExpiresAt" ON proxies."Proxies" ("Status", "ExpiresAt");

CREATE UNIQUE INDEX "UX_Proxies_PasswordKeyId_Nonce" ON proxies."Proxies" ("PasswordKeyId", "PasswordNonce");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261011024141_AddProxyPersistence', '10.0.8');

COMMIT;

