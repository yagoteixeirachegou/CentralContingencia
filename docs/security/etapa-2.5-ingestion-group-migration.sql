START TRANSACTION;
ALTER TABLE profiles."IngestionBatches" ADD "AdsPowerGroupId" character varying(64);

ALTER TABLE profiles."IngestionBatches" ADD "AdsPowerGroupName" character varying(200);

CREATE INDEX "IX_IngestionBatches_AdsPowerGroupId" ON profiles."IngestionBatches" ("AdsPowerGroupId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261013105059_AddIngestionBatchGroup', '10.0.8');

COMMIT;

