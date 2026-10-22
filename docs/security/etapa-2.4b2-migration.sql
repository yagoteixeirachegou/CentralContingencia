START TRANSACTION;
CREATE TABLE audit."OperationalEvents" (
    "Id" uuid NOT NULL,
    "ActorId" uuid NOT NULL,
    "ProfileId" uuid NOT NULL,
    "AllocationId" uuid,
    "Action" character varying(32) NOT NULL,
    "OccurredAt" timestamp with time zone NOT NULL,
    "CorrelationId" character varying(128) NOT NULL,
    CONSTRAINT "PK_OperationalEvents" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_OperationalEvents_Action" CHECK (("Action" IN ('ProfileCreated','ProfileUpdated','ProfileClosed') AND "AllocationId" IS NULL) OR ("Action" IN ('AllocationReserved','AllocationConfirmed','AllocationReleased','AllocationExpired') AND "AllocationId" IS NOT NULL)),
    CONSTRAINT "CK_OperationalEvents_Correlation" CHECK (length(btrim("CorrelationId")) BETWEEN 1 AND 128 AND "CorrelationId" !~ '[[:cntrl:]]'),
    CONSTRAINT "CK_OperationalEvents_Ids" CHECK ("Id" <> '00000000-0000-0000-0000-000000000000'::uuid AND "ActorId" <> '00000000-0000-0000-0000-000000000000'::uuid AND "ProfileId" <> '00000000-0000-0000-0000-000000000000'::uuid AND ("AllocationId" IS NULL OR "AllocationId" <> '00000000-0000-0000-0000-000000000000'::uuid)),
    CONSTRAINT "CK_OperationalEvents_Time" CHECK (isfinite("OccurredAt"))
);

CREATE INDEX "IX_OperationalEvents_AllocationId_OccurredAt" ON audit."OperationalEvents" ("AllocationId", "OccurredAt");

CREATE INDEX "IX_OperationalEvents_ProfileId_OccurredAt" ON audit."OperationalEvents" ("ProfileId", "OccurredAt");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261011230449_AddOperationalAudit', '10.0.8');

COMMIT;

