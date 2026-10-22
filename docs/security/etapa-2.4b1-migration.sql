START TRANSACTION;
DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'profiles') THEN
        CREATE SCHEMA profiles;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'allocations') THEN
        CREATE SCHEMA allocations;
    END IF;
END $EF$;

CREATE TABLE profiles."OperationalProfiles" (
    "Id" uuid NOT NULL,
    "Name" character varying(200) NOT NULL,
    "Platform" character varying(100) NOT NULL,
    "Status" character varying(16) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "ClosedAt" timestamp with time zone,
    CONSTRAINT "PK_OperationalProfiles" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_Profiles_Id" CHECK ("Id" <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT "CK_Profiles_Name" CHECK (length(btrim("Name")) BETWEEN 1 AND 200 AND "Name" = btrim("Name") AND "Name" !~ '[[:cntrl:]]'),
    CONSTRAINT "CK_Profiles_Platform" CHECK (length(btrim("Platform")) BETWEEN 1 AND 100 AND "Platform" = btrim("Platform") AND "Platform" !~ '[[:cntrl:]]'),
    CONSTRAINT "CK_Profiles_State" CHECK (("Status" = 'Active' AND "ClosedAt" IS NULL) OR ("Status" = 'Closed' AND "ClosedAt" IS NOT NULL AND "ClosedAt" = "UpdatedAt")),
    CONSTRAINT "CK_Profiles_Times" CHECK (isfinite("CreatedAt") AND isfinite("UpdatedAt") AND "UpdatedAt" >= "CreatedAt" AND ("ClosedAt" IS NULL OR (isfinite("ClosedAt") AND "ClosedAt" >= "CreatedAt")))
);

CREATE TABLE allocations."ProxyAllocations" (
    "Id" uuid NOT NULL,
    "ProxyId" uuid NOT NULL,
    "ProfileId" uuid NOT NULL,
    "ActorId" uuid NOT NULL,
    "IdempotencyKey" uuid NOT NULL,
    "Status" character varying(16) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "ReservationExpiresAt" timestamp with time zone NOT NULL,
    "ConfirmedAt" timestamp with time zone,
    "EndedAt" timestamp with time zone,
    "EndReason" character varying(32),
    CONSTRAINT "PK_ProxyAllocations" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_Allocations_Ids" CHECK ("Id" <> '00000000-0000-0000-0000-000000000000'::uuid AND "ProfileId" <> '00000000-0000-0000-0000-000000000000'::uuid AND "ProxyId" <> '00000000-0000-0000-0000-000000000000'::uuid AND "ActorId" <> '00000000-0000-0000-0000-000000000000'::uuid AND "IdempotencyKey" <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT "CK_Allocations_State" CHECK (("Status" = 'Reserved' AND "ConfirmedAt" IS NULL AND "EndedAt" IS NULL AND "EndReason" IS NULL) OR ("Status" = 'Confirmed' AND "ConfirmedAt" IS NOT NULL AND "EndedAt" IS NULL AND "EndReason" IS NULL) OR ("Status" = 'Released' AND "EndedAt" IS NOT NULL AND "EndReason" IS NOT NULL AND "EndReason" IN ('Released','ProfileClosed')) OR ("Status" = 'Expired' AND "ConfirmedAt" IS NULL AND "EndedAt" IS NOT NULL AND "EndedAt" >= "ReservationExpiresAt" AND "EndReason" = 'ReservationExpired' AND "EndReason" IS NOT NULL)),
    CONSTRAINT "CK_Allocations_Times" CHECK (isfinite("CreatedAt") AND isfinite("UpdatedAt") AND isfinite("ReservationExpiresAt") AND "UpdatedAt" >= "CreatedAt" AND "ReservationExpiresAt" > "CreatedAt" AND ("ConfirmedAt" IS NULL OR (isfinite("ConfirmedAt") AND "ConfirmedAt" >= "CreatedAt" AND "ConfirmedAt" < "ReservationExpiresAt" AND "ConfirmedAt" <= "UpdatedAt")) AND ("EndedAt" IS NULL OR (isfinite("EndedAt") AND "EndedAt" >= "CreatedAt" AND "EndedAt" = "UpdatedAt"))),
    CONSTRAINT "FK_ProxyAllocations_OperationalProfiles_ProfileId" FOREIGN KEY ("ProfileId") REFERENCES profiles."OperationalProfiles" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_ProxyAllocations_Proxies_ProxyId" FOREIGN KEY ("ProxyId") REFERENCES proxies."Proxies" ("Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_OperationalProfiles_Status_CreatedAt" ON profiles."OperationalProfiles" ("Status", "CreatedAt");

CREATE UNIQUE INDEX "IX_ProxyAllocations_ActorId_IdempotencyKey" ON allocations."ProxyAllocations" ("ActorId", "IdempotencyKey");

CREATE INDEX "IX_ProxyAllocations_ProfileId_CreatedAt" ON allocations."ProxyAllocations" ("ProfileId", "CreatedAt");

CREATE INDEX "IX_ProxyAllocations_ProxyId_CreatedAt" ON allocations."ProxyAllocations" ("ProxyId", "CreatedAt");

CREATE UNIQUE INDEX "UX_ProxyAllocations_ActiveProfile" ON allocations."ProxyAllocations" ("ProfileId") WHERE "Status" IN ('Reserved', 'Confirmed');

CREATE UNIQUE INDEX "UX_ProxyAllocations_ActiveProxy" ON allocations."ProxyAllocations" ("ProxyId") WHERE "Status" IN ('Reserved', 'Confirmed');

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261011223416_AddOperationalProfilesAndAllocations', '10.0.8');

COMMIT;

