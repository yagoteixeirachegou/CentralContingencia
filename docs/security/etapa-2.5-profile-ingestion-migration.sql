START TRANSACTION;
ALTER TABLE profiles."OperationalProfiles" ADD "AdsPowerId" character varying(64);

ALTER TABLE profiles."OperationalProfiles" ADD "AdsPowerSerialNumber" character varying(20);

ALTER TABLE profiles."OperationalProfiles" ADD "IngestionBatchId" uuid;

CREATE TABLE profiles."IngestionBatches" (
    "Id" uuid NOT NULL,
    "ActorId" uuid NOT NULL,
    "IdempotencyKey" uuid NOT NULL,
    "Requested" integer NOT NULL,
    "Platform1" character varying(100),
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_IngestionBatches" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_IngestionBatches_Ids" CHECK ("Id" <> '00000000-0000-0000-0000-000000000000'::uuid AND "ActorId" <> '00000000-0000-0000-0000-000000000000'::uuid AND "IdempotencyKey" <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT "CK_IngestionBatches_Requested" CHECK ("Requested" BETWEEN 1 AND 50),
    CONSTRAINT "CK_IngestionBatches_Time" CHECK (isfinite("CreatedAt"))
);

CREATE INDEX "IX_OperationalProfiles_IngestionBatchId" ON profiles."OperationalProfiles" ("IngestionBatchId") WHERE "IngestionBatchId" IS NOT NULL;

CREATE UNIQUE INDEX "UX_Profiles_AdsPowerId" ON profiles."OperationalProfiles" ("AdsPowerId") WHERE "AdsPowerId" IS NOT NULL;

ALTER TABLE profiles."OperationalProfiles" ADD CONSTRAINT "CK_Profiles_AdsPower" CHECK (("AdsPowerId" IS NULL OR "AdsPowerId" ~ '^[A-Za-z0-9_-]{3,64}$') AND ("AdsPowerSerialNumber" IS NULL OR ("AdsPowerId" IS NOT NULL AND "AdsPowerSerialNumber" ~ '^[0-9]{1,20}$')));

CREATE UNIQUE INDEX "IX_IngestionBatches_ActorId_IdempotencyKey" ON profiles."IngestionBatches" ("ActorId", "IdempotencyKey");

CREATE INDEX "IX_IngestionBatches_CreatedAt" ON profiles."IngestionBatches" ("CreatedAt");

ALTER TABLE profiles."OperationalProfiles" ADD CONSTRAINT "FK_OperationalProfiles_IngestionBatches_IngestionBatchId" FOREIGN KEY ("IngestionBatchId") REFERENCES profiles."IngestionBatches" ("Id") ON DELETE RESTRICT;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261012230807_AddProfileIngestion', '10.0.8');

COMMIT;

