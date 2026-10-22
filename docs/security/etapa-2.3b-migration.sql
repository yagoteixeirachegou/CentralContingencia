START TRANSACTION;
CREATE TABLE audit."ProxyImportBatches" (
    "Id" uuid NOT NULL,
    "ActorId" uuid NOT NULL,
    "IdempotencyKey" uuid NOT NULL,
    "ProtectedFingerprint" character varying(2048) NOT NULL,
    "OccurredAt" timestamp with time zone NOT NULL,
    "CorrelationId" character varying(128) NOT NULL,
    "Total" integer NOT NULL,
    "Imported" integer NOT NULL,
    "Ignored" integer NOT NULL,
    "Invalid" integer NOT NULL,
    "Conflicting" integer NOT NULL,
    CONSTRAINT "PK_ProxyImportBatches" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_ImportBatch_Counts" CHECK ("Total" BETWEEN 1 AND 10000 AND "Imported" >= 0 AND "Ignored" >= 0 AND "Invalid" = 0 AND "Conflicting" >= 0 AND "Total" = "Imported" + "Ignored" + "Invalid" + "Conflicting"),
    CONSTRAINT "CK_ImportBatch_Ids" CHECK ("Id" <> '00000000-0000-0000-0000-000000000000'::uuid AND "ActorId" <> '00000000-0000-0000-0000-000000000000'::uuid AND "IdempotencyKey" <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT "CK_ImportBatch_Time" CHECK (isfinite("OccurredAt"))
);

CREATE TABLE audit."ProxyImportLines" (
    "BatchId" uuid NOT NULL,
    "Line" integer NOT NULL,
    "ProxyId" uuid,
    "Outcome" character varying(16) NOT NULL,
    CONSTRAINT "PK_ProxyImportLines" PRIMARY KEY ("BatchId", "Line"),
    CONSTRAINT "CK_ImportLine_Outcome" CHECK ("Line" >= 2 AND "Outcome" IN ('Imported', 'Empty', 'Duplicate', 'Conflict') AND (("Outcome" = 'Imported') = ("ProxyId" IS NOT NULL))),
    CONSTRAINT "FK_ProxyImportLines_Proxies_ProxyId" FOREIGN KEY ("ProxyId") REFERENCES proxies."Proxies" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_ProxyImportLines_ProxyImportBatches_BatchId" FOREIGN KEY ("BatchId") REFERENCES audit."ProxyImportBatches" ("Id") ON DELETE CASCADE
);

CREATE UNIQUE INDEX "IX_ProxyImportBatches_ActorId_IdempotencyKey" ON audit."ProxyImportBatches" ("ActorId", "IdempotencyKey");

CREATE INDEX "IX_ProxyImportBatches_OccurredAt" ON audit."ProxyImportBatches" ("OccurredAt");

CREATE INDEX "IX_ProxyImportLines_ProxyId" ON audit."ProxyImportLines" ("ProxyId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261011165123_AddProxyImportBatches', '10.0.8');

COMMIT;

