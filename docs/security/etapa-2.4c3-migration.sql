START TRANSACTION;
CREATE TABLE audit."ProfileRecoveryBatches" (
    "Id" uuid NOT NULL,
    "ActorId" uuid NOT NULL,
    "IdempotencyKey" uuid NOT NULL,
    "ProtectedFingerprint" character varying(2048) NOT NULL,
    "OccurredAt" timestamp with time zone NOT NULL,
    "CorrelationId" character varying(128) NOT NULL,
    "Total" integer NOT NULL,
    "Recovered" integer NOT NULL,
    "Ignored" integer NOT NULL,
    CONSTRAINT "PK_ProfileRecoveryBatches" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_Recovery_Correlation" CHECK (length(btrim("CorrelationId")) BETWEEN 1 AND 128 AND "CorrelationId" !~ '[[:cntrl:]]'),
    CONSTRAINT "CK_Recovery_Counts" CHECK ("Total" BETWEEN 1 AND 10000 AND "Recovered" >= 0 AND "Ignored" >= 0 AND "Total" = "Recovered" + "Ignored"),
    CONSTRAINT "CK_Recovery_Ids" CHECK ("Id" <> '00000000-0000-0000-0000-000000000000'::uuid AND "ActorId" <> '00000000-0000-0000-0000-000000000000'::uuid AND "IdempotencyKey" <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT "CK_Recovery_Time" CHECK (isfinite("OccurredAt"))
);

CREATE TABLE audit."ProfileRecoveryLines" (
    "BatchId" uuid NOT NULL,
    "Line" integer NOT NULL,
    "ProxyId" uuid,
    "ProfileId" uuid,
    "AllocationId" uuid,
    "Outcome" character varying(16) NOT NULL,
    CONSTRAINT "PK_ProfileRecoveryLines" PRIMARY KEY ("BatchId", "Line"),
    CONSTRAINT "CK_RecoveryLine_Outcome" CHECK ("Line" >= 2 AND "Outcome" IN ('Recovered','Ignored','AlreadyLinked') AND ("Outcome" = 'Ignored' OR ("ProxyId" IS NOT NULL AND "ProfileId" IS NOT NULL AND "AllocationId" IS NOT NULL))),
    CONSTRAINT "FK_ProfileRecoveryLines_OperationalProfiles_ProfileId" FOREIGN KEY ("ProfileId") REFERENCES profiles."OperationalProfiles" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_ProfileRecoveryLines_ProfileRecoveryBatches_BatchId" FOREIGN KEY ("BatchId") REFERENCES audit."ProfileRecoveryBatches" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_ProfileRecoveryLines_Proxies_ProxyId" FOREIGN KEY ("ProxyId") REFERENCES proxies."Proxies" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_ProfileRecoveryLines_ProxyAllocations_AllocationId" FOREIGN KEY ("AllocationId") REFERENCES allocations."ProxyAllocations" ("Id") ON DELETE RESTRICT
);

CREATE UNIQUE INDEX "IX_ProfileRecoveryBatches_ActorId_IdempotencyKey" ON audit."ProfileRecoveryBatches" ("ActorId", "IdempotencyKey");

CREATE INDEX "IX_ProfileRecoveryBatches_OccurredAt" ON audit."ProfileRecoveryBatches" ("OccurredAt");

CREATE INDEX "IX_ProfileRecoveryLines_AllocationId" ON audit."ProfileRecoveryLines" ("AllocationId");

CREATE INDEX "IX_ProfileRecoveryLines_ProfileId" ON audit."ProfileRecoveryLines" ("ProfileId");

CREATE INDEX "IX_ProfileRecoveryLines_ProxyId" ON audit."ProfileRecoveryLines" ("ProxyId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261012131756_AddProfileRecoveryReceipts', '10.0.8');

COMMIT;

